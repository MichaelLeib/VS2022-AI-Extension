using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OllamaAssistant.Infrastructure;
using OllamaAssistant.Services.Interfaces;

namespace OllamaAssistant.Services.Implementation
{
    /// <summary>
    /// Service for implementing rate limiting and abuse prevention
    /// </summary>
    [Export(typeof(IRateLimitingService))]
    public class RateLimitingService : IRateLimitingService, IDisposable
    {
        private readonly ISettingsService _settingsService;
        private readonly ILogger _logger;
        private readonly object _lockObject = new object();

        // Rate limiting tracking
        private readonly ConcurrentDictionary<string, RateLimitBucket> _rateLimitBuckets;
        private readonly ConcurrentDictionary<string, UsageQuota> _usageQuotas;
        private readonly ConcurrentQueue<RequestMetrics> _requestHistory;
        private readonly Timer _cleanupTimer;
        private readonly Timer _quotaResetTimer;

        // Circuit breaker state
        private readonly ConcurrentDictionary<string, CircuitBreakerState> _circuitBreakers;

        // Suspicious activity monitoring
        private readonly ConcurrentDictionary<string, SuspiciousActivityTracker> _suspiciousActivityTrackers;

        private bool _disposed;

        public RateLimitingService(ISettingsService settingsService = null, ILogger logger = null)
        {
            _settingsService = settingsService;
            _logger = logger;

            _rateLimitBuckets = new ConcurrentDictionary<string, RateLimitBucket>();
            _usageQuotas = new ConcurrentDictionary<string, UsageQuota>();
            _requestHistory = new ConcurrentQueue<RequestMetrics>();
            _circuitBreakers = new ConcurrentDictionary<string, CircuitBreakerState>();
            _suspiciousActivityTrackers = new ConcurrentDictionary<string, SuspiciousActivityTracker>();

            // Clean up old entries every minute
            _cleanupTimer = new Timer(CleanupOldEntries, null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
            
            // Reset quotas daily
            _quotaResetTimer = new Timer(ResetDailyQuotas, null, GetTimeUntilMidnight(), TimeSpan.FromDays(1));
        }

        #region Rate Limiting

        /// <summary>
        /// Checks if a request is allowed based on rate limits
        /// </summary>
        public async Task<RateLimitResult> CheckRateLimitAsync(string identifier, RateLimitType type)
        {
            if (string.IsNullOrEmpty(identifier))
                identifier = "anonymous";

            try
            {
                var limits = GetRateLimits(type);
                var bucketKey = $"{identifier}:{type}";
                
                var bucket = _rateLimitBuckets.GetOrAdd(bucketKey, key => new RateLimitBucket
                {
                    Identifier = identifier,
                    Type = type,
                    Limit = limits.requests,
                    WindowSize = limits.window,
                    LastReset = DateTime.UtcNow
                });

                lock (bucket)
                {
                    var now = DateTime.UtcNow;
                    
                    // Reset bucket if window has passed
                    if (now - bucket.LastReset >= bucket.WindowSize)
                    {
                        bucket.RequestCount = 0;
                        bucket.LastReset = now;
                    }

                    // Check if limit is exceeded
                    if (bucket.RequestCount >= bucket.Limit)
                    {
                        bucket.LastViolation = now;
                        
                        await _logger?.LogWarningAsync(
                            $"Rate limit exceeded for {identifier} ({type}): {bucket.RequestCount}/{bucket.Limit}",
                            "RateLimiting");

                        // Track suspicious activity
                        await TrackSuspiciousActivityAsync(identifier, SuspiciousActivityType.RateLimitViolation);

                        return new RateLimitResult
                        {
                            IsAllowed = false,
                            Limit = bucket.Limit,
                            Remaining = 0,
                            ResetTime = bucket.LastReset.Add(bucket.WindowSize),
                            RetryAfter = bucket.LastReset.Add(bucket.WindowSize) - now
                        };
                    }

                    // Allow request and increment counter
                    bucket.RequestCount++;
                    bucket.LastRequest = now;

                    return new RateLimitResult
                    {
                        IsAllowed = true,
                        Limit = bucket.Limit,
                        Remaining = bucket.Limit - bucket.RequestCount,
                        ResetTime = bucket.LastReset.Add(bucket.WindowSize),
                        RetryAfter = TimeSpan.Zero
                    };
                }
            }
            catch (Exception ex)
            {
                await _logger?.LogErrorAsync(ex, "Error checking rate limit", "RateLimiting");
                // Allow request on error to avoid blocking functionality
                return new RateLimitResult
                {
                    IsAllowed = true,
                    Limit = int.MaxValue,
                    Remaining = int.MaxValue,
                    ResetTime = DateTime.UtcNow.AddHours(1),
                    RetryAfter = TimeSpan.Zero
                };
            }
        }

        /// <summary>
        /// Records a completed request for tracking
        /// </summary>
        public async Task RecordRequestAsync(string identifier, RateLimitType type, bool success, TimeSpan duration)
        {
            if (string.IsNullOrEmpty(identifier))
                identifier = "anonymous";

            try
            {
                var metrics = new RequestMetrics
                {
                    Identifier = identifier,
                    Type = type,
                    Timestamp = DateTime.UtcNow,
                    Success = success,
                    Duration = duration
                };

                _requestHistory.Enqueue(metrics);

                // Limit history size
                while (_requestHistory.Count > 10000)
                {
                    _requestHistory.TryDequeue(out _);
                }

                // Update circuit breaker
                await UpdateCircuitBreakerAsync(identifier, success);

                // Track suspicious patterns
                if (!success)
                {
                    await TrackSuspiciousActivityAsync(identifier, SuspiciousActivityType.FailedRequest);
                }

                await _logger?.LogDebugAsync(
                    $"Recorded request: {identifier} ({type}) - Success: {success}, Duration: {duration.TotalMilliseconds}ms",
                    "RateLimiting");
            }
            catch (Exception ex)
            {
                await _logger?.LogErrorAsync(ex, "Error recording request", "RateLimiting");
            }
        }

        #endregion

        #region Usage Quotas

        /// <summary>
        /// Checks if usage quota is available
        /// </summary>
        public async Task<QuotaResult> CheckUsageQuotaAsync(string identifier, QuotaType type)
        {
            if (string.IsNullOrEmpty(identifier))
                identifier = "anonymous";

            try
            {
                var limits = GetQuotaLimits(type);
                var quotaKey = $"{identifier}:{type}";
                
                var quota = _usageQuotas.GetOrAdd(quotaKey, key => new UsageQuota
                {
                    Identifier = identifier,
                    Type = type,
                    Limit = limits.limit,
                    Period = limits.period,
                    LastReset = DateTime.UtcNow
                });

                lock (quota)
                {
                    var now = DateTime.UtcNow;
                    
                    // Reset quota if period has passed
                    if (now - quota.LastReset >= quota.Period)
                    {
                        quota.Usage = 0;
                        quota.LastReset = now;
                    }

                    // Check if quota is exceeded
                    if (quota.Usage >= quota.Limit)
                    {
                        await _logger?.LogWarningAsync(
                            $"Usage quota exceeded for {identifier} ({type}): {quota.Usage}/{quota.Limit}",
                            "RateLimiting");

                        await TrackSuspiciousActivityAsync(identifier, SuspiciousActivityType.QuotaViolation);

                        return new QuotaResult
                        {
                            IsAllowed = false,
                            Limit = quota.Limit,
                            Used = quota.Usage,
                            Remaining = 0,
                            ResetTime = quota.LastReset.Add(quota.Period)
                        };
                    }

                    return new QuotaResult
                    {
                        IsAllowed = true,
                        Limit = quota.Limit,
                        Used = quota.Usage,
                        Remaining = quota.Limit - quota.Usage,
                        ResetTime = quota.LastReset.Add(quota.Period)
                    };
                }
            }
            catch (Exception ex)
            {
                await _logger?.LogErrorAsync(ex, "Error checking usage quota", "RateLimiting");
                return new QuotaResult
                {
                    IsAllowed = true,
                    Limit = int.MaxValue,
                    Used = 0,
                    Remaining = int.MaxValue,
                    ResetTime = DateTime.UtcNow.AddDays(1)
                };
            }
        }

        /// <summary>
        /// Records usage against quota
        /// </summary>
        public async Task RecordUsageAsync(string identifier, QuotaType type, int amount = 1)
        {
            if (string.IsNullOrEmpty(identifier))
                identifier = "anonymous";

            try
            {
                var quotaKey = $"{identifier}:{type}";
                
                if (_usageQuotas.TryGetValue(quotaKey, out var quota))
                {
                    lock (quota)
                    {
                        quota.Usage += amount;
                        quota.LastUsage = DateTime.UtcNow;
                    }

                    await _logger?.LogDebugAsync(
                        $"Recorded usage: {identifier} ({type}) - Amount: {amount}, Total: {quota.Usage}/{quota.Limit}",
                        "RateLimiting");
                }
            }
            catch (Exception ex)
            {
                await _logger?.LogErrorAsync(ex, "Error recording usage", "RateLimiting");
            }
        }

        #endregion

        #region Circuit Breaker

        /// <summary>
        /// Checks if circuit breaker allows the request
        /// </summary>
        public async Task<bool> CheckCircuitBreakerAsync(string identifier)
        {
            if (string.IsNullOrEmpty(identifier))
                identifier = "global";

            try
            {
                var breakerKey = $"circuit:{identifier}";
                var breaker = _circuitBreakers.GetOrAdd(breakerKey, key => new CircuitBreakerState
                {
                    Identifier = identifier,
                    State = CircuitState.Closed,
                    FailureThreshold = 5,
                    TimeoutDuration = TimeSpan.FromMinutes(2),
                    LastStateChange = DateTime.UtcNow
                });

                lock (breaker)
                {
                    var now = DateTime.UtcNow;

                    switch (breaker.State)
                    {
                        case CircuitState.Closed:
                            return true;

                        case CircuitState.Open:
                            // Check if timeout has passed
                            if (now - breaker.LastStateChange >= breaker.TimeoutDuration)
                            {
                                breaker.State = CircuitState.HalfOpen;
                                breaker.LastStateChange = now;
                                await _logger?.LogInfoAsync($"Circuit breaker {identifier} moved to half-open", "RateLimiting");
                                return true;
                            }
                            return false;

                        case CircuitState.HalfOpen:
                            return true;

                        default:
                            return true;
                    }
                }
            }
            catch (Exception ex)
            {
                await _logger?.LogErrorAsync(ex, "Error checking circuit breaker", "RateLimiting");
                return true; // Allow on error
            }
        }

        #endregion

        #region Suspicious Activity Monitoring

        /// <summary>
        /// Checks if an identifier is engaging in suspicious activity
        /// </summary>
        public async Task<bool> IsSuspiciousActivityAsync(string identifier)
        {
            if (string.IsNullOrEmpty(identifier))
                return false;

            try
            {
                if (_suspiciousActivityTrackers.TryGetValue(identifier, out var tracker))
                {
                    var now = DateTime.UtcNow;
                    var recentActivity = tracker.Activities.Where(a => now - a.Timestamp < TimeSpan.FromHours(1)).ToList();
                    
                    // Check for various suspicious patterns
                    var rateLimitViolations = recentActivity.Count(a => a.Type == SuspiciousActivityType.RateLimitViolation);
                    var failedRequests = recentActivity.Count(a => a.Type == SuspiciousActivityType.FailedRequest);
                    var quotaViolations = recentActivity.Count(a => a.Type == SuspiciousActivityType.QuotaViolation);

                    // Suspicious if too many violations in the last hour
                    var isSuspicious = rateLimitViolations >= 3 || failedRequests >= 10 || quotaViolations >= 2;

                    if (isSuspicious && !tracker.IsBlocked)
                    {
                        tracker.IsBlocked = true;
                        tracker.BlockedUntil = now.AddHours(1);
                        
                        await _logger?.LogWarningAsync(
                            $"Blocking suspicious identifier: {identifier} (Rate: {rateLimitViolations}, Failed: {failedRequests}, Quota: {quotaViolations})",
                            "RateLimiting");
                    }

                    return tracker.IsBlocked && now < tracker.BlockedUntil;
                }

                return false;
            }
            catch (Exception ex)
            {
                await _logger?.LogErrorAsync(ex, "Error checking suspicious activity", "RateLimiting");
                return false;
            }
        }

        /// <summary>
        /// Gets current rate limiting statistics
        /// </summary>
        public async Task<RateLimitingStats> GetStatsAsync()
        {
            try
            {
                var now = DateTime.UtcNow;
                var recentRequests = _requestHistory.Where(r => now - r.Timestamp < TimeSpan.FromHours(1)).ToList();

                return new RateLimitingStats
                {
                    TotalRequests = _requestHistory.Count,
                    RecentRequests = recentRequests.Count,
                    SuccessfulRequests = recentRequests.Count(r => r.Success),
                    FailedRequests = recentRequests.Count(r => !r.Success),
                    AverageResponseTime = recentRequests.Any() ? 
                        TimeSpan.FromMilliseconds(recentRequests.Average(r => r.Duration.TotalMilliseconds)) : 
                        TimeSpan.Zero,
                    ActiveRateLimitBuckets = _rateLimitBuckets.Count,
                    ActiveQuotas = _usageQuotas.Count,
                    CircuitBreakersOpen = _circuitBreakers.Values.Count(cb => cb.State == CircuitState.Open),
                    SuspiciousIdentifiers = _suspiciousActivityTrackers.Values.Count(s => s.IsBlocked),
                    Timestamp = now
                };
            }
            catch (Exception ex)
            {
                await _logger?.LogErrorAsync(ex, "Error getting rate limiting stats", "RateLimiting");
                return new RateLimitingStats { Timestamp = DateTime.UtcNow };
            }
        }

        #endregion

        #region Private Methods

        private (int requests, TimeSpan window) GetRateLimits(RateLimitType type)
        {
            return type switch
            {
                RateLimitType.CodeCompletion => (100, TimeSpan.FromMinutes(1)), // 100 completions per minute
                RateLimitType.JumpSuggestion => (50, TimeSpan.FromMinutes(1)),  // 50 jumps per minute
                RateLimitType.HealthCheck => (10, TimeSpan.FromMinutes(1)),     // 10 health checks per minute
                RateLimitType.ModelInfo => (5, TimeSpan.FromMinutes(1)),        // 5 model queries per minute
                _ => (30, TimeSpan.FromMinutes(1))                              // Default: 30 per minute
            };
        }

        private (int limit, TimeSpan period) GetQuotaLimits(QuotaType type)
        {
            return type switch
            {
                QuotaType.DailyRequests => (1000, TimeSpan.FromDays(1)),        // 1000 requests per day
                QuotaType.HourlyRequests => (200, TimeSpan.FromHours(1)),       // 200 requests per hour
                QuotaType.DailyTokens => (100000, TimeSpan.FromDays(1)),        // 100K tokens per day
                _ => (500, TimeSpan.FromHours(1))                               // Default: 500 per hour
            };
        }

        private async Task UpdateCircuitBreakerAsync(string identifier, bool success)
        {
            var breakerKey = $"circuit:{identifier}";
            var breaker = _circuitBreakers.GetOrAdd(breakerKey, key => new CircuitBreakerState
            {
                Identifier = identifier,
                State = CircuitState.Closed,
                FailureThreshold = 5,
                TimeoutDuration = TimeSpan.FromMinutes(2),
                LastStateChange = DateTime.UtcNow
            });

            lock (breaker)
            {
                var now = DateTime.UtcNow;

                if (success)
                {
                    breaker.ConsecutiveFailures = 0;
                    
                    if (breaker.State == CircuitState.HalfOpen)
                    {
                        breaker.State = CircuitState.Closed;
                        breaker.LastStateChange = now;
                        _ = _logger?.LogInfoAsync($"Circuit breaker {identifier} closed after successful request", "RateLimiting");
                    }
                }
                else
                {
                    breaker.ConsecutiveFailures++;
                    
                    if (breaker.State == CircuitState.Closed && breaker.ConsecutiveFailures >= breaker.FailureThreshold)
                    {
                        breaker.State = CircuitState.Open;
                        breaker.LastStateChange = now;
                        _ = _logger?.LogWarningAsync($"Circuit breaker {identifier} opened after {breaker.ConsecutiveFailures} failures", "RateLimiting");
                    }
                    else if (breaker.State == CircuitState.HalfOpen)
                    {
                        breaker.State = CircuitState.Open;
                        breaker.LastStateChange = now;
                        _ = _logger?.LogWarningAsync($"Circuit breaker {identifier} reopened after failed half-open attempt", "RateLimiting");
                    }
                }
            }
        }

        private async Task TrackSuspiciousActivityAsync(string identifier, SuspiciousActivityType type)
        {
            var tracker = _suspiciousActivityTrackers.GetOrAdd(identifier, key => new SuspiciousActivityTracker
            {
                Identifier = identifier,
                Activities = new List<SuspiciousActivity>()
            });

            lock (tracker.Activities)
            {
                tracker.Activities.Add(new SuspiciousActivity
                {
                    Type = type,
                    Timestamp = DateTime.UtcNow
                });

                // Keep only recent activities
                var cutoff = DateTime.UtcNow.AddHours(-24);
                tracker.Activities.RemoveAll(a => a.Timestamp < cutoff);
            }

            await _logger?.LogDebugAsync($"Tracked suspicious activity: {identifier} - {type}", "RateLimiting");
        }

        private void CleanupOldEntries(object state)
        {
            try
            {
                var now = DateTime.UtcNow;
                var cutoff = now.AddHours(-1);

                // Clean up rate limit buckets
                var expiredBuckets = _rateLimitBuckets.Where(kvp => 
                    now - kvp.Value.LastRequest > TimeSpan.FromHours(1)).ToList();
                
                foreach (var bucket in expiredBuckets)
                {
                    _rateLimitBuckets.TryRemove(bucket.Key, out _);
                }

                // Clean up request history
                var oldHistoryCount = _requestHistory.Count;
                while (_requestHistory.TryPeek(out var oldest) && oldest.Timestamp < cutoff)
                {
                    _requestHistory.TryDequeue(out _);
                }

                // Clean up suspicious activity trackers
                var expiredTrackers = _suspiciousActivityTrackers.Where(kvp =>
                    !kvp.Value.Activities.Any() || kvp.Value.Activities.Max(a => a.Timestamp) < cutoff).ToList();
                
                foreach (var tracker in expiredTrackers)
                {
                    _suspiciousActivityTrackers.TryRemove(tracker.Key, out _);
                }

                var newHistoryCount = _requestHistory.Count;
                if (oldHistoryCount != newHistoryCount || expiredBuckets.Any() || expiredTrackers.Any())
                {
                    _logger?.LogDebugAsync(
                        $"Cleanup completed: Removed {expiredBuckets.Count} buckets, {oldHistoryCount - newHistoryCount} history entries, {expiredTrackers.Count} trackers",
                        "RateLimiting").Wait();
                }
            }
            catch (Exception ex)
            {
                _logger?.LogErrorAsync(ex, "Error during cleanup", "RateLimiting").Wait();
            }
        }

        private void ResetDailyQuotas(object state)
        {
            try
            {
                var dailyQuotas = _usageQuotas.Where(kvp => kvp.Value.Type == QuotaType.DailyRequests || kvp.Value.Type == QuotaType.DailyTokens).ToList();
                
                foreach (var quota in dailyQuotas)
                {
                    lock (quota.Value)
                    {
                        quota.Value.Usage = 0;
                        quota.Value.LastReset = DateTime.UtcNow;
                    }
                }

                _logger?.LogInfoAsync($"Reset {dailyQuotas.Count} daily quotas", "RateLimiting").Wait();
            }
            catch (Exception ex)
            {
                _logger?.LogErrorAsync(ex, "Error resetting daily quotas", "RateLimiting").Wait();
            }
        }

        private TimeSpan GetTimeUntilMidnight()
        {
            var now = DateTime.Now;
            var midnight = now.Date.AddDays(1);
            return midnight - now;
        }

        #endregion

        #region IDisposable

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            
            _cleanupTimer?.Dispose();
            _quotaResetTimer?.Dispose();
        }

        #endregion
    }

    #region Data Classes

    public class RateLimitBucket
    {
        public string Identifier { get; set; }
        public RateLimitType Type { get; set; }
        public int Limit { get; set; }
        public int RequestCount { get; set; }
        public TimeSpan WindowSize { get; set; }
        public DateTime LastReset { get; set; }
        public DateTime LastRequest { get; set; }
        public DateTime? LastViolation { get; set; }
    }

    public class UsageQuota
    {
        public string Identifier { get; set; }
        public QuotaType Type { get; set; }
        public int Limit { get; set; }
        public int Usage { get; set; }
        public TimeSpan Period { get; set; }
        public DateTime LastReset { get; set; }
        public DateTime LastUsage { get; set; }
    }

    public class RequestMetrics
    {
        public string Identifier { get; set; }
        public RateLimitType Type { get; set; }
        public DateTime Timestamp { get; set; }
        public bool Success { get; set; }
        public TimeSpan Duration { get; set; }
    }

    public class CircuitBreakerState
    {
        public string Identifier { get; set; }
        public CircuitState State { get; set; }
        public int FailureThreshold { get; set; }
        public int ConsecutiveFailures { get; set; }
        public TimeSpan TimeoutDuration { get; set; }
        public DateTime LastStateChange { get; set; }
    }

    public class SuspiciousActivityTracker
    {
        public string Identifier { get; set; }
        public List<SuspiciousActivity> Activities { get; set; } = new List<SuspiciousActivity>();
        public bool IsBlocked { get; set; }
        public DateTime BlockedUntil { get; set; }
    }

    public class SuspiciousActivity
    {
        public SuspiciousActivityType Type { get; set; }
        public DateTime Timestamp { get; set; }
    }

    public enum RateLimitType
    {
        CodeCompletion,
        JumpSuggestion,
        HealthCheck,
        ModelInfo,
        General
    }

    public enum QuotaType
    {
        DailyRequests,
        HourlyRequests,
        DailyTokens
    }

    public enum CircuitState
    {
        Closed,
        Open,
        HalfOpen
    }

    public enum SuspiciousActivityType
    {
        RateLimitViolation,
        QuotaViolation,
        FailedRequest,
        UnauthorizedAccess
    }

    #endregion
}