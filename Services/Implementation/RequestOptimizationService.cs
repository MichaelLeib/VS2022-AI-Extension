using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OllamaAssistant.Infrastructure;
using OllamaAssistant.Models;
using OllamaAssistant.Services.Interfaces;

namespace OllamaAssistant.Services.Implementation
{
    /// <summary>
    /// Service for optimizing AI requests through intelligent debouncing, caching, and prioritization
    /// </summary>
    public class RequestOptimizationService : IDisposable
    {
        private readonly DebounceService _debounceService;
        private readonly CacheService<string, CodeSuggestion> _responseCache;
        private readonly ConcurrentDictionary<string, RequestMetrics> _requestMetrics;
        private readonly Timer _metricsCleanupTimer;
        private readonly object _lockObject = new object();
        private bool _disposed;

        // Configuration
        private readonly TimeSpan _defaultDebounceDelay = TimeSpan.FromMilliseconds(300);
        private readonly TimeSpan _userInitiatedDebounceDelay = TimeSpan.FromMilliseconds(100);
        private readonly TimeSpan _cacheExpiration = TimeSpan.FromMinutes(5);
        private readonly int _maxContextSize = 10000; // characters
        private readonly int _optimalContextSize = 5000; // characters

        public RequestOptimizationService()
        {
            _debounceService = new DebounceService();
            _responseCache = new CacheService<string, CodeSuggestion>(_cacheExpiration, maxSize: 500);
            _requestMetrics = new ConcurrentDictionary<string, RequestMetrics>();
            
            // Cleanup metrics every 10 minutes
            _metricsCleanupTimer = new Timer(CleanupMetrics, null, 
                TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(10));
        }

        /// <summary>
        /// Optimizes an AI request by applying debouncing, caching, and context optimization
        /// </summary>
        public async Task<CodeSuggestion> OptimizeRequestAsync(
            string requestId,
            CodeContext context,
            Func<CodeContext, Task<CodeSuggestion>> aiRequestFunc,
            RequestPriority priority = RequestPriority.Automatic,
            CancellationToken cancellationToken = default)
        {
            if (_disposed || string.IsNullOrEmpty(requestId) || context == null || aiRequestFunc == null)
                return null;

            // Generate cache key based on optimized context
            var optimizedContext = OptimizeContext(context);
            var cacheKey = GenerateCacheKey(optimizedContext);

            // Check cache first
            if (_responseCache.TryGet(cacheKey, out var cachedSuggestion))
            {
                RecordCacheHit(requestId, priority);
                return cachedSuggestion;
            }

            // Apply intelligent debouncing based on priority
            var debounceDelay = GetDebounceDelay(priority, requestId);
            var debounceKey = $"{requestId}_{priority}";

            var suggestion = await _debounceService.DebounceAsync(
                debounceKey,
                async () =>
                {
                    var startTime = DateTime.UtcNow;
                    try
                    {
                        var result = await aiRequestFunc(optimizedContext);
                        RecordSuccessfulRequest(requestId, priority, DateTime.UtcNow - startTime);
                        
                        // Cache the result if valid
                        if (result != null && !string.IsNullOrEmpty(result.Text))
                        {
                            var customExpiration = GetCacheExpiration(result.Confidence);
                            _responseCache.Set(cacheKey, result, customExpiration);
                        }
                        
                        return result;
                    }
                    catch (Exception ex)
                    {
                        RecordFailedRequest(requestId, priority, DateTime.UtcNow - startTime, ex);
                        throw;
                    }
                },
                debounceDelay,
                cancellationToken);

            return suggestion;
        }

        /// <summary>
        /// Optimizes context size and content for better AI performance
        /// </summary>
        private CodeContext OptimizeContext(CodeContext context)
        {
            if (context == null)
                return null;

            var optimized = new CodeContext
            {
                FilePath = context.FilePath,
                CursorPosition = context.CursorPosition,
                Language = context.Language,
                ProjectContext = context.ProjectContext
            };

            // Optimize lines before and after cursor
            optimized.LinesBefore = OptimizeLines(context.LinesBefore, _optimalContextSize / 2);
            optimized.LinesAfter = OptimizeLines(context.LinesAfter, _optimalContextSize / 2);

            // Optimize cursor history - keep only most recent and relevant
            if (context.CursorHistory?.Count > 0)
            {
                optimized.CursorHistory = context.CursorHistory
                    .Where(h => IsRelevantHistory(h, context))
                    .OrderByDescending(h => h.Timestamp)
                    .Take(5) // Keep only 5 most recent
                    .ToList();
            }

            return optimized;
        }

        /// <summary>
        /// Optimizes lines by removing less relevant content while preserving structure
        /// </summary>
        private List<string> OptimizeLines(List<string> lines, int maxCharacters)
        {
            if (lines == null || !lines.Any())
                return lines;

            var totalChars = lines.Sum(l => l.Length);
            if (totalChars <= maxCharacters)
                return lines;

            var optimized = new List<string>();
            var currentChars = 0;

            // Prioritize lines with code over empty lines or comments
            var prioritizedLines = lines
                .Select((line, index) => new { Line = line, Index = index, Priority = GetLinePriority(line) })
                .OrderByDescending(x => x.Priority)
                .ToList();

            foreach (var item in prioritizedLines)
            {
                if (currentChars + item.Line.Length <= maxCharacters)
                {
                    optimized.Add(item.Line);
                    currentChars += item.Line.Length;
                }
                else
                {
                    break;
                }
            }

            // Restore original order
            return optimized.OrderBy(line => lines.IndexOf(line)).ToList();
        }

        /// <summary>
        /// Assigns priority to lines for optimization
        /// </summary>
        private int GetLinePriority(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
                return 1; // Low priority for empty lines

            var trimmed = line.Trim();
            
            if (trimmed.StartsWith("//") || trimmed.StartsWith("/*") || trimmed.StartsWith("*"))
                return 2; // Low priority for comments

            if (trimmed.Contains("class ") || trimmed.Contains("function ") || 
                trimmed.Contains("public ") || trimmed.Contains("private "))
                return 5; // High priority for declarations

            if (trimmed.Contains("if ") || trimmed.Contains("for ") || 
                trimmed.Contains("while ") || trimmed.Contains("switch "))
                return 4; // Medium-high priority for control structures

            return 3; // Medium priority for regular code
        }

        /// <summary>
        /// Determines if cursor history is relevant to current context
        /// </summary>
        private bool IsRelevantHistory(CursorPosition history, CodeContext context)
        {
            if (history == null || context == null)
                return false;

            // Same file is always relevant
            if (string.Equals(history.FilePath, context.FilePath, StringComparison.OrdinalIgnoreCase))
                return true;

            // Recent history from related files
            if (DateTime.UtcNow - history.Timestamp < TimeSpan.FromMinutes(5))
            {
                // Check if files are in same directory or related
                var contextDir = System.IO.Path.GetDirectoryName(context.FilePath);
                var historyDir = System.IO.Path.GetDirectoryName(history.FilePath);
                return string.Equals(contextDir, historyDir, StringComparison.OrdinalIgnoreCase);
            }

            return false;
        }

        /// <summary>
        /// Generates cache key for context
        /// </summary>
        private string GenerateCacheKey(CodeContext context)
        {
            if (context == null)
                return string.Empty;

            var keyComponents = new List<string>
            {
                context.Language ?? "unknown",
                context.CursorPosition?.Line.ToString() ?? "0",
                context.CursorPosition?.Column.ToString() ?? "0"
            };

            if (context.LinesBefore?.Any() == true)
                keyComponents.Add(string.Join("", context.LinesBefore).GetHashCode().ToString());

            if (context.LinesAfter?.Any() == true)
                keyComponents.Add(string.Join("", context.LinesAfter).GetHashCode().ToString());

            return string.Join("_", keyComponents);
        }

        /// <summary>
        /// Gets debounce delay based on request priority and history
        /// </summary>
        private TimeSpan GetDebounceDelay(RequestPriority priority, string requestId)
        {
            switch (priority)
            {
                case RequestPriority.UserInitiated:
                    return _userInitiatedDebounceDelay;

                case RequestPriority.High:
                    return TimeSpan.FromMilliseconds(150);

                case RequestPriority.Automatic:
                    // Use adaptive delay based on recent activity
                    var metrics = GetRequestMetrics(requestId);
                    if (metrics.RecentFailures > 2)
                        return TimeSpan.FromMilliseconds(500); // Slow down on failures
                    
                    return _defaultDebounceDelay;

                case RequestPriority.Low:
                    return TimeSpan.FromMilliseconds(600);

                default:
                    return _defaultDebounceDelay;
            }
        }

        /// <summary>
        /// Gets cache expiration based on suggestion confidence
        /// </summary>
        private TimeSpan GetCacheExpiration(double confidence)
        {
            if (confidence >= 0.9)
                return TimeSpan.FromMinutes(10); // High confidence, cache longer

            if (confidence >= 0.7)
                return _cacheExpiration; // Normal cache time

            if (confidence >= 0.5)
                return TimeSpan.FromMinutes(2); // Low confidence, shorter cache

            return TimeSpan.FromMinutes(1); // Very low confidence, minimal cache
        }

        /// <summary>
        /// Records cache hit for metrics
        /// </summary>
        private void RecordCacheHit(string requestId, RequestPriority priority)
        {
            var metrics = GetOrCreateRequestMetrics(requestId);
            lock (metrics)
            {
                metrics.TotalRequests++;
                metrics.CacheHits++;
                metrics.LastRequestTime = DateTime.UtcNow;
                metrics.LastRequestPriority = priority;
            }
        }

        /// <summary>
        /// Records successful request for metrics
        /// </summary>
        private void RecordSuccessfulRequest(string requestId, RequestPriority priority, TimeSpan duration)
        {
            var metrics = GetOrCreateRequestMetrics(requestId);
            lock (metrics)
            {
                metrics.TotalRequests++;
                metrics.SuccessfulRequests++;
                metrics.LastRequestTime = DateTime.UtcNow;
                metrics.LastRequestPriority = priority;
                metrics.RecentFailures = 0; // Reset failure count on success
                
                // Update average response time
                metrics.TotalResponseTime = metrics.TotalResponseTime.Add(duration);
                metrics.AverageResponseTime = TimeSpan.FromMilliseconds(
                    metrics.TotalResponseTime.TotalMilliseconds / metrics.SuccessfulRequests);
            }
        }

        /// <summary>
        /// Records failed request for metrics
        /// </summary>
        private void RecordFailedRequest(string requestId, RequestPriority priority, TimeSpan duration, Exception exception)
        {
            var metrics = GetOrCreateRequestMetrics(requestId);
            lock (metrics)
            {
                metrics.TotalRequests++;
                metrics.FailedRequests++;
                metrics.RecentFailures++;
                metrics.LastRequestTime = DateTime.UtcNow;
                metrics.LastRequestPriority = priority;
                metrics.LastError = exception?.Message;
            }
        }

        /// <summary>
        /// Gets or creates request metrics for an identifier
        /// </summary>
        private RequestMetrics GetOrCreateRequestMetrics(string requestId)
        {
            return _requestMetrics.GetOrAdd(requestId, _ => new RequestMetrics
            {
                RequestId = requestId,
                FirstRequestTime = DateTime.UtcNow
            });
        }

        /// <summary>
        /// Gets existing request metrics
        /// </summary>
        private RequestMetrics GetRequestMetrics(string requestId)
        {
            _requestMetrics.TryGetValue(requestId, out var metrics);
            return metrics ?? new RequestMetrics { RequestId = requestId };
        }

        /// <summary>
        /// Cleans up old metrics to prevent memory leaks
        /// </summary>
        private void CleanupMetrics(object state)
        {
            if (_disposed)
                return;

            try
            {
                var cutoff = DateTime.UtcNow.AddHours(-2);
                var keysToRemove = new List<string>();

                foreach (var kvp in _requestMetrics)
                {
                    if (kvp.Value.LastRequestTime < cutoff)
                    {
                        keysToRemove.Add(kvp.Key);
                    }
                }

                foreach (var key in keysToRemove)
                {
                    _requestMetrics.TryRemove(key, out _);
                }
            }
            catch (Exception)
            {
                // Ignore cleanup errors
            }
        }

        /// <summary>
        /// Gets optimization statistics
        /// </summary>
        public RequestOptimizationStats GetStats()
        {
            lock (_lockObject)
            {
                var totalRequests = _requestMetrics.Values.Sum(m => m.TotalRequests);
                var totalCacheHits = _requestMetrics.Values.Sum(m => m.CacheHits);
                var totalSuccessful = _requestMetrics.Values.Sum(m => m.SuccessfulRequests);
                var totalFailed = _requestMetrics.Values.Sum(m => m.FailedRequests);

                return new RequestOptimizationStats
                {
                    TotalRequests = totalRequests,
                    CacheHits = totalCacheHits,
                    CacheHitRate = totalRequests > 0 ? (double)totalCacheHits / totalRequests : 0,
                    SuccessfulRequests = totalSuccessful,
                    FailedRequests = totalFailed,
                    SuccessRate = totalRequests > 0 ? (double)totalSuccessful / totalRequests : 0,
                    ActiveRequestMetrics = _requestMetrics.Count,
                    CacheSize = _responseCache.Count,
                    AverageResponseTime = CalculateOverallAverageResponseTime(),
                    Timestamp = DateTime.UtcNow
                };
            }
        }

        /// <summary>
        /// Calculates overall average response time
        /// </summary>
        private TimeSpan CalculateOverallAverageResponseTime()
        {
            var metrics = _requestMetrics.Values.Where(m => m.SuccessfulRequests > 0).ToList();
            if (!metrics.Any())
                return TimeSpan.Zero;

            var totalMs = metrics.Sum(m => m.AverageResponseTime.TotalMilliseconds * m.SuccessfulRequests);
            var totalRequests = metrics.Sum(m => m.SuccessfulRequests);

            return totalRequests > 0 ? TimeSpan.FromMilliseconds(totalMs / totalRequests) : TimeSpan.Zero;
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            _debounceService?.Dispose();
            _responseCache?.Dispose();
            _metricsCleanupTimer?.Dispose();
        }
    }

    /// <summary>
    /// Request priority levels for optimization
    /// </summary>
    public enum RequestPriority
    {
        Low = 0,
        Automatic = 1,
        High = 2,
        UserInitiated = 3
    }

    /// <summary>
    /// Metrics for tracking request performance
    /// </summary>
    internal class RequestMetrics
    {
        public string RequestId { get; set; }
        public DateTime FirstRequestTime { get; set; }
        public DateTime LastRequestTime { get; set; }
        public int TotalRequests { get; set; }
        public int SuccessfulRequests { get; set; }
        public int FailedRequests { get; set; }
        public int CacheHits { get; set; }
        public int RecentFailures { get; set; }
        public TimeSpan TotalResponseTime { get; set; }
        public TimeSpan AverageResponseTime { get; set; }
        public RequestPriority LastRequestPriority { get; set; }
        public string LastError { get; set; }
    }

    /// <summary>
    /// Statistics for request optimization service
    /// </summary>
    public class RequestOptimizationStats
    {
        public int TotalRequests { get; set; }
        public int CacheHits { get; set; }
        public double CacheHitRate { get; set; }
        public int SuccessfulRequests { get; set; }
        public int FailedRequests { get; set; }
        public double SuccessRate { get; set; }
        public int ActiveRequestMetrics { get; set; }
        public int CacheSize { get; set; }
        public TimeSpan AverageResponseTime { get; set; }
        public DateTime Timestamp { get; set; }
    }
}