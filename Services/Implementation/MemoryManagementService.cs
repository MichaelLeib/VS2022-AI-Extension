using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OllamaAssistant.Models;
using OllamaAssistant.Services.Interfaces;
using Microsoft.Win32;
using System.Management;

namespace OllamaAssistant.Services.Implementation
{
    /// <summary>
    /// Service for managing memory usage and optimizing storage of cursor history and other data
    /// </summary>
    public class MemoryManagementService : IDisposable
    {
        private readonly Timer _memoryMonitoringTimer;
        private readonly Timer _cleanupTimer;
        private readonly object _lockObject = new object();
        private readonly MemoryConfiguration _config;
        private bool _disposed;

        // Memory pressure thresholds (in MB)
        private long _lowMemoryThreshold;
        private long _highMemoryThreshold;
        private long _criticalMemoryThreshold;

        // Efficient circular buffer for cursor history
        private readonly CircularBuffer<CursorPosition> _cursorHistory;
        private readonly Dictionary<string, FileMetrics> _fileMetrics;
        private readonly MemoryMetrics _memoryMetrics;

        public MemoryManagementService(MemoryConfiguration config = null)
        {
            _config = config ?? new MemoryConfiguration();
            _fileMetrics = new Dictionary<string, FileMetrics>();
            _memoryMetrics = new MemoryMetrics();
            
            // Initialize memory thresholds based on system memory
            InitializeMemoryThresholds();
            
            // Initialize cursor history with efficient circular buffer
            _cursorHistory = new CircularBuffer<CursorPosition>(_config.MaxCursorHistoryEntries);
            
            // Start memory monitoring (every 30 seconds)
            _memoryMonitoringTimer = new Timer(MonitorMemoryPressure, null, 
                TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
            
            // Start cleanup timer (every 5 minutes)
            _cleanupTimer = new Timer(PerformCleanup, null, 
                TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));
        }

        /// <summary>
        /// Adds a cursor position to history with memory-aware management
        /// </summary>
        public void AddCursorPosition(CursorPosition position)
        {
            if (_disposed || position == null)
                return;

            lock (_lockObject)
            {
                // Add to circular buffer (automatically evicts oldest when full)
                _cursorHistory.Add(position);
                
                // Update file metrics
                UpdateFileMetrics(position.FilePath);
                
                // Check memory pressure and cleanup if needed
                if (IsMemoryPressureHigh())
                {
                    PerformEmergencyCleanup();
                }
            }
        }

        /// <summary>
        /// Gets cursor history with intelligent filtering based on relevance and recency
        /// </summary>
        public List<CursorPosition> GetOptimizedCursorHistory(string currentFilePath, int maxEntries = -1)
        {
            if (_disposed)
                return new List<CursorPosition>();

            lock (_lockObject)
            {
                var allHistory = _cursorHistory.ToList();
                if (!allHistory.Any())
                    return new List<CursorPosition>();

                // Apply intelligent filtering
                var filtered = FilterHistoryByRelevance(allHistory, currentFilePath);
                
                // Limit entries if specified
                if (maxEntries > 0 && filtered.Count > maxEntries)
                {
                    filtered = filtered.Take(maxEntries).ToList();
                }

                return filtered;
            }
        }

        /// <summary>
        /// Optimizes memory usage by cleaning up least relevant data
        /// </summary>
        public void OptimizeMemoryUsage()
        {
            if (_disposed)
                return;

            lock (_lockObject)
            {
                // Clean up old file metrics
                CleanupFileMetrics();
                
                // Compact cursor history based on relevance
                CompactCursorHistory();
                
                // Update memory metrics
                UpdateMemoryMetrics();
            }
        }

        /// <summary>
        /// Gets current memory usage statistics
        /// </summary>
        public MemoryUsageStats GetMemoryStats()
        {
            lock (_lockObject)
            {
                var process = Process.GetCurrentProcess();
                var workingSet = process.WorkingSet64;
                var privateBytes = process.PrivateMemorySize64;

                return new MemoryUsageStats
                {
                    WorkingSetMB = workingSet / (1024 * 1024),
                    PrivateBytesMB = privateBytes / (1024 * 1024),
                    CursorHistoryCount = _cursorHistory.Count,
                    FileMetricsCount = _fileMetrics.Count,
                    MemoryPressureLevel = GetCurrentMemoryPressureLevel(),
                    LowMemoryThresholdMB = _lowMemoryThreshold,
                    HighMemoryThresholdMB = _highMemoryThreshold,
                    CriticalMemoryThresholdMB = _criticalMemoryThreshold,
                    LastCleanupTime = _memoryMetrics.LastCleanupTime,
                    TotalCleanupsPerformed = _memoryMetrics.TotalCleanupsPerformed,
                    Timestamp = DateTime.UtcNow
                };
            }
        }

        /// <summary>
        /// Forces immediate cleanup if memory usage is high
        /// </summary>
        public void ForceCleanupIfNeeded()
        {
            if (_disposed)
                return;

            if (IsMemoryPressureHigh())
            {
                PerformEmergencyCleanup();
            }
        }

        /// <summary>
        /// Initializes memory thresholds based on available system memory
        /// </summary>
        private void InitializeMemoryThresholds()
        {
            try
            {
                var totalMemoryMB = GetTotalSystemMemoryMB();
                
                if (totalMemoryMB > 0)
                {
                    // Set thresholds as percentage of system memory
                    _lowMemoryThreshold = Math.Max(256, totalMemoryMB * 5 / 100);     // 5% or 256MB min
                    _highMemoryThreshold = Math.Max(512, totalMemoryMB * 10 / 100);   // 10% or 512MB min
                    _criticalMemoryThreshold = Math.Max(1024, totalMemoryMB * 15 / 100); // 15% or 1GB min
                }
                else
                {
                    // Fallback values if system memory detection fails
                    _lowMemoryThreshold = 256;
                    _highMemoryThreshold = 512;
                    _criticalMemoryThreshold = 1024;
                }
            }
            catch
            {
                // Use conservative defaults
                _lowMemoryThreshold = 256;
                _highMemoryThreshold = 512;
                _criticalMemoryThreshold = 1024;
            }
        }

        /// <summary>
        /// Gets total system memory in MB
        /// </summary>
        private long GetTotalSystemMemoryMB()
        {
            try
            {
                using (var searcher = new ManagementObjectSearcher("SELECT TotalPhysicalMemory FROM Win32_ComputerSystem"))
                {
                    foreach (var obj in searcher.Get())
                    {
                        var totalBytes = Convert.ToInt64(obj["TotalPhysicalMemory"]);
                        return totalBytes / (1024 * 1024);
                    }
                }
            }
            catch
            {
                // Fallback to GC method
                try
                {
                    var gcMemory = GC.GetTotalMemory(false);
                    return Math.Max(4096, gcMemory / (1024 * 1024) * 4); // Estimate 4x current usage
                }
                catch
                {
                    return 0; // Detection failed
                }
            }

            return 0;
        }

        /// <summary>
        /// Monitors memory pressure and triggers cleanup if needed
        /// </summary>
        private void MonitorMemoryPressure(object state)
        {
            if (_disposed)
                return;

            try
            {
                var pressureLevel = GetCurrentMemoryPressureLevel();
                _memoryMetrics.CurrentMemoryPressure = pressureLevel;

                switch (pressureLevel)
                {
                    case MemoryPressureLevel.High:
                        PerformRegularCleanup();
                        break;
                    case MemoryPressureLevel.Critical:
                        PerformEmergencyCleanup();
                        break;
                }
            }
            catch (Exception)
            {
                // Ignore monitoring errors to prevent crashes
            }
        }

        /// <summary>
        /// Performs regular cleanup operations
        /// </summary>
        private void PerformCleanup(object state)
        {
            if (_disposed)
                return;

            PerformRegularCleanup();
        }

        /// <summary>
        /// Performs regular memory cleanup
        /// </summary>
        private void PerformRegularCleanup()
        {
            lock (_lockObject)
            {
                CleanupFileMetrics();
                CompactCursorHistory();
                UpdateMemoryMetrics();
                
                _memoryMetrics.LastCleanupTime = DateTime.UtcNow;
                _memoryMetrics.TotalCleanupsPerformed++;
            }
        }

        /// <summary>
        /// Performs aggressive cleanup in high memory pressure situations
        /// </summary>
        private void PerformEmergencyCleanup()
        {
            lock (_lockObject)
            {
                // More aggressive cleanup
                CleanupFileMetrics(aggressive: true);
                CompactCursorHistory(aggressive: true);
                
                // Trigger garbage collection
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
                
                UpdateMemoryMetrics();
                _memoryMetrics.LastCleanupTime = DateTime.UtcNow;
                _memoryMetrics.TotalCleanupsPerformed++;
                _memoryMetrics.EmergencyCleanupsPerformed++;
            }
        }

        /// <summary>
        /// Gets current memory pressure level
        /// </summary>
        private MemoryPressureLevel GetCurrentMemoryPressureLevel()
        {
            try
            {
                var process = Process.GetCurrentProcess();
                var workingSetMB = process.WorkingSet64 / (1024 * 1024);

                if (workingSetMB >= _criticalMemoryThreshold)
                    return MemoryPressureLevel.Critical;
                
                if (workingSetMB >= _highMemoryThreshold)
                    return MemoryPressureLevel.High;
                
                if (workingSetMB >= _lowMemoryThreshold)
                    return MemoryPressureLevel.Medium;
                
                return MemoryPressureLevel.Low;
            }
            catch
            {
                return MemoryPressureLevel.Low; // Conservative default
            }
        }

        /// <summary>
        /// Checks if memory pressure is high
        /// </summary>
        private bool IsMemoryPressureHigh()
        {
            var level = GetCurrentMemoryPressureLevel();
            return level >= MemoryPressureLevel.High;
        }

        /// <summary>
        /// Updates file access metrics
        /// </summary>
        private void UpdateFileMetrics(string filePath)
        {
            if (string.IsNullOrEmpty(filePath))
                return;

            if (_fileMetrics.TryGetValue(filePath, out var metrics))
            {
                metrics.AccessCount++;
                metrics.LastAccessTime = DateTime.UtcNow;
            }
            else
            {
                _fileMetrics[filePath] = new FileMetrics
                {
                    FilePath = filePath,
                    AccessCount = 1,
                    FirstAccessTime = DateTime.UtcNow,
                    LastAccessTime = DateTime.UtcNow
                };
            }
        }

        /// <summary>
        /// Cleans up old file metrics
        /// </summary>
        private void CleanupFileMetrics(bool aggressive = false)
        {
            var cutoffTime = aggressive ? 
                DateTime.UtcNow.AddMinutes(-30) : // 30 minutes for aggressive
                DateTime.UtcNow.AddHours(-2);     // 2 hours for regular

            var keysToRemove = _fileMetrics.Where(kvp => kvp.Value.LastAccessTime < cutoffTime)
                                          .Select(kvp => kvp.Key)
                                          .ToList();

            foreach (var key in keysToRemove)
            {
                _fileMetrics.Remove(key);
            }
        }

        /// <summary>
        /// Filters cursor history by relevance to current context
        /// </summary>
        private List<CursorPosition> FilterHistoryByRelevance(List<CursorPosition> history, string currentFilePath)
        {
            var now = DateTime.UtcNow;
            
            return history.Where(pos => pos != null)
                         .OrderByDescending(pos => CalculateRelevanceScore(pos, currentFilePath, now))
                         .ToList();
        }

        /// <summary>
        /// Calculates relevance score for cursor position
        /// </summary>
        private double CalculateRelevanceScore(CursorPosition position, string currentFilePath, DateTime now)
        {
            double score = 0;

            // Time-based relevance (recent is more relevant)
            var timeDiff = now - position.Timestamp;
            if (timeDiff.TotalMinutes <= 5)
                score += 10;
            else if (timeDiff.TotalMinutes <= 30)
                score += 5;
            else if (timeDiff.TotalHours <= 2)
                score += 2;
            else
                score += 1;

            // File-based relevance
            if (string.Equals(position.FilePath, currentFilePath, StringComparison.OrdinalIgnoreCase))
                score += 15; // Same file is highly relevant

            // Directory-based relevance
            if (!string.IsNullOrEmpty(currentFilePath) && !string.IsNullOrEmpty(position.FilePath))
            {
                var currentDir = System.IO.Path.GetDirectoryName(currentFilePath);
                var positionDir = System.IO.Path.GetDirectoryName(position.FilePath);
                
                if (string.Equals(currentDir, positionDir, StringComparison.OrdinalIgnoreCase))
                    score += 5; // Same directory is moderately relevant
            }

            return score;
        }

        /// <summary>
        /// Compacts cursor history by removing least relevant entries
        /// </summary>
        private void CompactCursorHistory(bool aggressive = false)
        {
            var currentHistory = _cursorHistory.ToList();
            if (!currentHistory.Any())
                return;

            var targetSize = aggressive ? 
                _config.MaxCursorHistoryEntries / 2 : // 50% reduction for aggressive
                (int)(_config.MaxCursorHistoryEntries * 0.8); // 20% reduction for regular

            if (currentHistory.Count <= targetSize)
                return;

            // Get most relevant entries
            var filtered = FilterHistoryByRelevance(currentHistory, null)
                          .Take(targetSize)
                          .OrderBy(pos => pos.Timestamp) // Restore chronological order
                          .ToList();

            // Clear and refill buffer
            _cursorHistory.Clear();
            foreach (var position in filtered)
            {
                _cursorHistory.Add(position);
            }
        }

        /// <summary>
        /// Updates internal memory metrics
        /// </summary>
        private void UpdateMemoryMetrics()
        {
            try
            {
                var process = Process.GetCurrentProcess();
                _memoryMetrics.CurrentWorkingSetMB = process.WorkingSet64 / (1024 * 1024);
                _memoryMetrics.CurrentPrivateBytesMB = process.PrivateMemorySize64 / (1024 * 1024);
                _memoryMetrics.LastUpdateTime = DateTime.UtcNow;
            }
            catch
            {
                // Ignore metric update errors
            }
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            _memoryMonitoringTimer?.Dispose();
            _cleanupTimer?.Dispose();
            
            lock (_lockObject)
            {
                _cursorHistory?.Clear();
                _fileMetrics?.Clear();
            }
        }
    }

    /// <summary>
    /// Memory management configuration
    /// </summary>
    public class MemoryConfiguration
    {
        public int MaxCursorHistoryEntries { get; set; } = 1000;
        public TimeSpan FileMetricsRetentionTime { get; set; } = TimeSpan.FromHours(2);
        public int MaxFileMetricsEntries { get; set; } = 500;
        public bool EnableAggressiveCleanup { get; set; } = true;
        public TimeSpan CleanupInterval { get; set; } = TimeSpan.FromMinutes(5);
        public TimeSpan MemoryMonitoringInterval { get; set; } = TimeSpan.FromSeconds(30);
    }

    /// <summary>
    /// Memory pressure levels
    /// </summary>
    public enum MemoryPressureLevel
    {
        Low = 0,
        Medium = 1,
        High = 2,
        Critical = 3
    }

    /// <summary>
    /// Metrics for file access patterns
    /// </summary>
    internal class FileMetrics
    {
        public string FilePath { get; set; }
        public int AccessCount { get; set; }
        public DateTime FirstAccessTime { get; set; }
        public DateTime LastAccessTime { get; set; }
    }

    /// <summary>
    /// Internal memory tracking metrics
    /// </summary>
    internal class MemoryMetrics
    {
        public long CurrentWorkingSetMB { get; set; }
        public long CurrentPrivateBytesMB { get; set; }
        public MemoryPressureLevel CurrentMemoryPressure { get; set; }
        public DateTime LastCleanupTime { get; set; }
        public DateTime LastUpdateTime { get; set; }
        public int TotalCleanupsPerformed { get; set; }
        public int EmergencyCleanupsPerformed { get; set; }
    }

    /// <summary>
    /// Memory usage statistics
    /// </summary>
    public class MemoryUsageStats
    {
        public long WorkingSetMB { get; set; }
        public long PrivateBytesMB { get; set; }
        public int CursorHistoryCount { get; set; }
        public int FileMetricsCount { get; set; }
        public MemoryPressureLevel MemoryPressureLevel { get; set; }
        public long LowMemoryThresholdMB { get; set; }
        public long HighMemoryThresholdMB { get; set; }
        public long CriticalMemoryThresholdMB { get; set; }
        public DateTime LastCleanupTime { get; set; }
        public int TotalCleanupsPerformed { get; set; }
        public DateTime Timestamp { get; set; }
    }

    /// <summary>
    /// Efficient circular buffer implementation for cursor history
    /// </summary>
    internal class CircularBuffer<T> : IEnumerable<T>
    {
        private readonly T[] _buffer;
        private int _head;
        private int _count;
        private readonly object _lock = new object();

        public CircularBuffer(int capacity)
        {
            _buffer = new T[capacity];
            _head = 0;
            _count = 0;
        }

        public int Count => _count;
        public int Capacity => _buffer.Length;

        public void Add(T item)
        {
            lock (_lock)
            {
                _buffer[_head] = item;
                _head = (_head + 1) % _buffer.Length;
                
                if (_count < _buffer.Length)
                    _count++;
            }
        }

        public void Clear()
        {
            lock (_lock)
            {
                Array.Clear(_buffer, 0, _buffer.Length);
                _head = 0;
                _count = 0;
            }
        }

        public List<T> ToList()
        {
            lock (_lock)
            {
                var result = new List<T>(_count);
                
                if (_count == 0)
                    return result;

                var start = _count < _buffer.Length ? 0 : _head;
                
                for (int i = 0; i < _count; i++)
                {
                    var index = (start + i) % _buffer.Length;
                    result.Add(_buffer[index]);
                }

                return result;
            }
        }

        public System.Collections.IEnumerator GetEnumerator()
        {
            return ToList().GetEnumerator();
        }

        IEnumerator<T> IEnumerable<T>.GetEnumerator()
        {
            return ToList().GetEnumerator();
        }
    }
}