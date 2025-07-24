using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace OllamaAssistant.Infrastructure
{
    /// <summary>
    /// Generic caching service with time-based expiration
    /// </summary>
    /// <typeparam name="TKey">Type of cache keys</typeparam>
    /// <typeparam name="TValue">Type of cached values</typeparam>
    public class CacheService<TKey, TValue> : IDisposable
    {
        private readonly ConcurrentDictionary<TKey, CacheEntry<TValue>> _cache;
        private readonly TimeSpan _defaultExpiration;
        private readonly int _maxSize;
        private readonly Timer _cleanupTimer;
        private readonly object _lockObject = new object();
        private bool _disposed;

        public CacheService(TimeSpan defaultExpiration, int maxSize = 1000)
        {
            _cache = new ConcurrentDictionary<TKey, CacheEntry<TValue>>();
            _defaultExpiration = defaultExpiration;
            _maxSize = maxSize;

            // Setup cleanup timer to run every minute
            _cleanupTimer = new Timer(CleanupExpiredEntries, null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
        }

        /// <summary>
        /// Gets a value from the cache if it exists and hasn't expired
        /// </summary>
        public bool TryGet(TKey key, out TValue value)
        {
            value = default(TValue);

            if (_disposed || key == null)
                return false;

            if (_cache.TryGetValue(key, out var entry))
            {
                if (entry.IsExpired)
                {
                    // Remove expired entry
                    _cache.TryRemove(key, out _);
                    return false;
                }

                // Update access time for LRU behavior
                entry.LastAccessed = DateTime.UtcNow;
                value = entry.Value;
                return true;
            }

            return false;
        }

        /// <summary>
        /// Adds or updates a value in the cache
        /// </summary>
        public void Set(TKey key, TValue value, TimeSpan? expiration = null)
        {
            if (_disposed || key == null)
                return;

            var expirationTime = expiration ?? _defaultExpiration;
            var entry = new CacheEntry<TValue>
            {
                Value = value,
                ExpiresAt = DateTime.UtcNow.Add(expirationTime),
                LastAccessed = DateTime.UtcNow
            };

            _cache.AddOrUpdate(key, entry, (k, existing) => entry);

            // Check if we need to evict entries due to size limit
            if (_cache.Count > _maxSize)
            {
                EvictOldestEntries();
            }
        }

        /// <summary>
        /// Gets a value from cache or computes it using the provided factory
        /// </summary>
        public async Task<TValue> GetOrAddAsync(TKey key, Func<TKey, Task<TValue>> factory, TimeSpan? expiration = null)
        {
            if (TryGet(key, out var cachedValue))
            {
                return cachedValue;
            }

            var value = await factory(key);
            Set(key, value, expiration);
            return value;
        }

        /// <summary>
        /// Gets a value from cache or computes it using the provided factory (synchronous)
        /// </summary>
        public TValue GetOrAdd(TKey key, Func<TKey, TValue> factory, TimeSpan? expiration = null)
        {
            if (TryGet(key, out var cachedValue))
            {
                return cachedValue;
            }

            var value = factory(key);
            Set(key, value, expiration);
            return value;
        }

        /// <summary>
        /// Removes a specific key from the cache
        /// </summary>
        public bool Remove(TKey key)
        {
            if (_disposed || key == null)
                return false;

            return _cache.TryRemove(key, out _);
        }

        /// <summary>
        /// Clears all entries from the cache
        /// </summary>
        public void Clear()
        {
            if (!_disposed)
            {
                _cache.Clear();
            }
        }

        /// <summary>
        /// Gets the current number of entries in the cache
        /// </summary>
        public int Count => _disposed ? 0 : _cache.Count;

        /// <summary>
        /// Gets cache hit rate statistics
        /// </summary>
        public CacheStatistics GetStatistics()
        {
            lock (_lockObject)
            {
                return new CacheStatistics
                {
                    EntryCount = _cache.Count,
                    MaxSize = _maxSize,
                    DefaultExpiration = _defaultExpiration
                };
            }
        }

        private void CleanupExpiredEntries(object state)
        {
            if (_disposed)
                return;

            try
            {
                var now = DateTime.UtcNow;
                var expiredKeys = new List<TKey>();

                foreach (var kvp in _cache)
                {
                    if (kvp.Value.IsExpired)
                    {
                        expiredKeys.Add(kvp.Key);
                    }
                }

                foreach (var key in expiredKeys)
                {
                    _cache.TryRemove(key, out _);
                }
            }
            catch (Exception)
            {
                // Ignore cleanup errors to prevent crashes
            }
        }

        private void EvictOldestEntries()
        {
            try
            {
                var entriesToRemove = _cache.Count - _maxSize + (_maxSize / 10); // Remove 10% extra
                var sortedEntries = new List<KeyValuePair<TKey, CacheEntry<TValue>>>();

                foreach (var kvp in _cache)
                {
                    sortedEntries.Add(kvp);
                }

                // Sort by last accessed time (oldest first)
                sortedEntries.Sort((x, y) => x.Value.LastAccessed.CompareTo(y.Value.LastAccessed));

                // Remove oldest entries
                for (int i = 0; i < Math.Min(entriesToRemove, sortedEntries.Count); i++)
                {
                    _cache.TryRemove(sortedEntries[i].Key, out _);
                }
            }
            catch (Exception)
            {
                // Ignore eviction errors
            }
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            _cleanupTimer?.Dispose();
            _cache?.Clear();
        }
    }

    /// <summary>
    /// Represents a cached entry with expiration information
    /// </summary>
    internal class CacheEntry<T>
    {
        public T Value { get; set; }
        public DateTime ExpiresAt { get; set; }
        public DateTime LastAccessed { get; set; }

        public bool IsExpired => DateTime.UtcNow > ExpiresAt;
    }

    /// <summary>
    /// Cache performance statistics
    /// </summary>
    public class CacheStatistics
    {
        public int EntryCount { get; set; }
        public int MaxSize { get; set; }
        public TimeSpan DefaultExpiration { get; set; }
        public double HitRate { get; set; }
        public long TotalRequests { get; set; }
        public long CacheHits { get; set; }
    }
}