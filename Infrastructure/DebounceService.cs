using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace OllamaAssistant.Infrastructure
{
    /// <summary>
    /// Service for debouncing rapid function calls
    /// </summary>
    public class DebounceService : IDisposable
    {
        private readonly ConcurrentDictionary<string, DebounceEntry> _debounceEntries;
        private bool _disposed;

        public DebounceService()
        {
            _debounceEntries = new ConcurrentDictionary<string, DebounceEntry>();
        }

        /// <summary>
        /// Debounces an action by key - only the last call within the delay period will execute
        /// </summary>
        /// <param name="key">Unique key for this debounced operation</param>
        /// <param name="action">Action to execute after debounce delay</param>
        /// <param name="delay">Delay before executing the action</param>
        /// <param name="cancellationToken">Cancellation token for the operation</param>
        public async Task DebounceAsync(string key, Func<Task> action, TimeSpan delay, CancellationToken cancellationToken = default)
        {
            if (_disposed || string.IsNullOrEmpty(key) || action == null)
                return;

            // Cancel any existing debounce for this key
            if (_debounceEntries.TryGetValue(key, out var existingEntry))
            {
                existingEntry.CancellationTokenSource.Cancel();
            }

            // Create new debounce entry
            var newCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var entry = new DebounceEntry
            {
                CancellationTokenSource = newCts,
                ScheduledTime = DateTime.UtcNow.Add(delay)
            };

            _debounceEntries.AddOrUpdate(key, entry, (k, existing) =>
            {
                existing.CancellationTokenSource.Cancel();
                return entry;
            });

            try
            {
                // Wait for the debounce delay
                await Task.Delay(delay, newCts.Token);

                // Execute the action if not cancelled
                if (!newCts.Token.IsCancellationRequested)
                {
                    await action();
                }
            }
            catch (OperationCanceledException)
            {
                // Expected when debounce is cancelled
            }
            catch (Exception)
            {
                // Let other exceptions bubble up
                throw;
            }
            finally
            {
                // Cleanup the entry
                _debounceEntries.TryRemove(key, out _);
                newCts.Dispose();
            }
        }

        /// <summary>
        /// Debounces a function call and returns the result
        /// </summary>
        /// <typeparam name="T">Return type of the function</typeparam>
        /// <param name="key">Unique key for this debounced operation</param>
        /// <param name="function">Function to execute after debounce delay</param>
        /// <param name="delay">Delay before executing the function</param>
        /// <param name="cancellationToken">Cancellation token for the operation</param>
        /// <returns>Result of the function or default value if cancelled</returns>
        public async Task<T> DebounceAsync<T>(string key, Func<Task<T>> function, TimeSpan delay, CancellationToken cancellationToken = default)
        {
            if (_disposed || string.IsNullOrEmpty(key) || function == null)
                return default(T);

            // Cancel any existing debounce for this key
            if (_debounceEntries.TryGetValue(key, out var existingEntry))
            {
                existingEntry.CancellationTokenSource.Cancel();
            }

            // Create new debounce entry
            var newCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var entry = new DebounceEntry
            {
                CancellationTokenSource = newCts,
                ScheduledTime = DateTime.UtcNow.Add(delay)
            };

            _debounceEntries.AddOrUpdate(key, entry, (k, existing) =>
            {
                existing.CancellationTokenSource.Cancel();
                return entry;
            });

            try
            {
                // Wait for the debounce delay
                await Task.Delay(delay, newCts.Token);

                // Execute the function if not cancelled
                if (!newCts.Token.IsCancellationRequested)
                {
                    return await function();
                }
            }
            catch (OperationCanceledException)
            {
                // Expected when debounce is cancelled
            }
            catch (Exception)
            {
                // Let other exceptions bubble up
                throw;
            }
            finally
            {
                // Cleanup the entry
                _debounceEntries.TryRemove(key, out _);
                newCts.Dispose();
            }

            return default(T);
        }

        /// <summary>
        /// Cancels a specific debounced operation
        /// </summary>
        /// <param name="key">Key of the operation to cancel</param>
        public void Cancel(string key)
        {
            if (_disposed || string.IsNullOrEmpty(key))
                return;

            if (_debounceEntries.TryRemove(key, out var entry))
            {
                entry.CancellationTokenSource.Cancel();
                entry.CancellationTokenSource.Dispose();
            }
        }

        /// <summary>
        /// Cancels all pending debounced operations
        /// </summary>
        public void CancelAll()
        {
            if (_disposed)
                return;

            foreach (var entry in _debounceEntries.Values)
            {
                entry.CancellationTokenSource.Cancel();
                entry.CancellationTokenSource.Dispose();
            }

            _debounceEntries.Clear();
        }

        /// <summary>
        /// Checks if a debounced operation is currently pending
        /// </summary>
        /// <param name="key">Key to check</param>
        /// <returns>True if operation is pending</returns>
        public bool IsPending(string key)
        {
            if (_disposed || string.IsNullOrEmpty(key))
                return false;

            return _debounceEntries.ContainsKey(key);
        }

        /// <summary>
        /// Gets the number of currently pending debounced operations
        /// </summary>
        public int PendingCount => _disposed ? 0 : _debounceEntries.Count;

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            CancelAll();
        }
    }

    /// <summary>
    /// Internal class to track debounce entries
    /// </summary>
    internal class DebounceEntry
    {
        public CancellationTokenSource CancellationTokenSource { get; set; }
        public DateTime ScheduledTime { get; set; }
    }

    /// <summary>
    /// Static helper for simple debouncing scenarios
    /// </summary>
    public static class Debounce
    {
        private static readonly DebounceService _defaultService = new DebounceService();

        /// <summary>
        /// Debounces an action using the default service
        /// </summary>
        public static Task ExecuteAsync(string key, Func<Task> action, TimeSpan delay, CancellationToken cancellationToken = default)
        {
            return _defaultService.DebounceAsync(key, action, delay, cancellationToken);
        }

        /// <summary>
        /// Debounces a function using the default service
        /// </summary>
        public static Task<T> ExecuteAsync<T>(string key, Func<Task<T>> function, TimeSpan delay, CancellationToken cancellationToken = default)
        {
            return _defaultService.DebounceAsync(key, function, delay, cancellationToken);
        }

        /// <summary>
        /// Cancels a debounced operation using the default service
        /// </summary>
        public static void Cancel(string key)
        {
            _defaultService.Cancel(key);
        }
    }
}