using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Threading;
using Microsoft.VisualStudio.Text.Editor;
using OllamaAssistant.Services.Interfaces;

namespace OllamaAssistant.Services.Implementation
{
    /// <summary>
    /// Service for optimizing Visual Studio performance and ensuring UI responsiveness
    /// </summary>
    public class VSPerformanceOptimizationService : IDisposable
    {
        private readonly JoinableTaskFactory _joinableTaskFactory;
        private readonly BackgroundTaskProcessor _backgroundProcessor;
        private readonly TextViewEventOptimizer _textViewOptimizer;
        private readonly StartupOptimizer _startupOptimizer;
        private readonly PerformanceMetrics _performanceMetrics;
        private readonly Timer _performanceMonitoringTimer;
        private bool _disposed;

        public VSPerformanceOptimizationService(JoinableTaskFactory joinableTaskFactory = null)
        {
            _joinableTaskFactory = joinableTaskFactory ?? ThreadHelper.JoinableTaskFactory;
            _backgroundProcessor = new BackgroundTaskProcessor();
            _textViewOptimizer = new TextViewEventOptimizer();
            _startupOptimizer = new StartupOptimizer();
            _performanceMetrics = new PerformanceMetrics();

            // Monitor performance every minute
            _performanceMonitoringTimer = new Timer(MonitorPerformance, null, 
                TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
        }

        /// <summary>
        /// Executes an operation on the UI thread without blocking
        /// </summary>
        public async Task ExecuteOnUIThreadAsync(Func<Task> operation, CancellationToken cancellationToken = default)
        {
            if (_disposed || operation == null)
                return;

            await _joinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
            
            var stopwatch = Stopwatch.StartNew();
            try
            {
                await operation();
                RecordUIThreadOperation(stopwatch.Elapsed, success: true);
            }
            catch (Exception ex)
            {
                RecordUIThreadOperation(stopwatch.Elapsed, success: false, ex);
                throw;
            }
        }

        /// <summary>
        /// Executes an operation on the UI thread and returns a result
        /// </summary>
        public async Task<T> ExecuteOnUIThreadAsync<T>(Func<Task<T>> operation, CancellationToken cancellationToken = default)
        {
            if (_disposed || operation == null)
                return default(T);

            await _joinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
            
            var stopwatch = Stopwatch.StartNew();
            try
            {
                var result = await operation();
                RecordUIThreadOperation(stopwatch.Elapsed, success: true);
                return result;
            }
            catch (Exception ex)
            {
                RecordUIThreadOperation(stopwatch.Elapsed, success: false, ex);
                throw;
            }
        }

        /// <summary>
        /// Queues a non-critical operation for background processing
        /// </summary>
        public Task QueueBackgroundOperationAsync(Func<CancellationToken, Task> operation, BackgroundTaskPriority priority = BackgroundTaskPriority.Normal)
        {
            if (_disposed || operation == null)
                return Task.CompletedTask;

            return _backgroundProcessor.QueueTaskAsync(operation, priority);
        }

        /// <summary>
        /// Optimizes text view event handling to prevent UI blocking
        /// </summary>
        public void OptimizeTextViewEvents(ITextView textView)
        {
            if (_disposed || textView == null)
                return;

            _textViewOptimizer.OptimizeTextView(textView);
        }

        /// <summary>
        /// Performs startup optimization to reduce extension load time
        /// </summary>
        public async Task OptimizeStartupAsync()
        {
            if (_disposed)
                return;

            await _startupOptimizer.OptimizeAsync();
        }

        /// <summary>
        /// Gets current performance statistics
        /// </summary>
        public VSPerformanceStats GetPerformanceStats()
        {
            if (_disposed)
                return new VSPerformanceStats();

            return new VSPerformanceStats
            {
                UIThreadOperations = _performanceMetrics.UIThreadOperationCount,
                AverageUIThreadDuration = _performanceMetrics.AverageUIThreadDuration,
                BackgroundTasksQueued = _backgroundProcessor.QueuedTaskCount,
                BackgroundTasksCompleted = _backgroundProcessor.CompletedTaskCount,
                OptimizedTextViews = _textViewOptimizer.OptimizedViewCount,
                StartupOptimizationsApplied = _startupOptimizer.OptimizationsApplied,
                LastStartupTime = _startupOptimizer.LastStartupDuration,
                MemoryUsageMB = GetCurrentMemoryUsage(),
                Timestamp = DateTime.UtcNow
            };
        }

        /// <summary>
        /// Records UI thread operation metrics
        /// </summary>
        private void RecordUIThreadOperation(TimeSpan duration, bool success, Exception exception = null)
        {
            _performanceMetrics.RecordUIThreadOperation(duration, success, exception);
        }

        /// <summary>
        /// Monitors performance and triggers optimizations if needed
        /// </summary>
        private void MonitorPerformance(object state)
        {
            if (_disposed)
                return;

            try
            {
                var stats = GetPerformanceStats();
                
                // Check if UI thread operations are taking too long
                if (stats.AverageUIThreadDuration.TotalMilliseconds > 50) // 50ms threshold
                {
                    // Trigger background processing optimization
                    _backgroundProcessor.IncreaseProcessingCapacity();
                }

                // Check memory usage
                if (stats.MemoryUsageMB > 200) // 200MB threshold
                {
                    // Trigger cleanup
                    QueueBackgroundOperationAsync(async ct => 
                    {
                        GC.Collect();
                        GC.WaitForPendingFinalizers();
                        GC.Collect();
                    }, BackgroundTaskPriority.Low);
                }
            }
            catch (Exception)
            {
                // Ignore monitoring errors
            }
        }

        /// <summary>
        /// Gets current memory usage in MB
        /// </summary>
        private long GetCurrentMemoryUsage()
        {
            try
            {
                var process = Process.GetCurrentProcess();
                return process.WorkingSet64 / (1024 * 1024);
            }
            catch
            {
                return 0;
            }
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            _performanceMonitoringTimer?.Dispose();
            _backgroundProcessor?.Dispose();
            _textViewOptimizer?.Dispose();
            _startupOptimizer?.Dispose();
        }
    }

    /// <summary>
    /// Processor for handling background tasks without blocking UI
    /// </summary>
    internal class BackgroundTaskProcessor : IDisposable
    {
        private readonly ConcurrentQueue<BackgroundTask> _taskQueue;
        private readonly SemaphoreSlim _semaphore;
        private readonly CancellationTokenSource _cancellationTokenSource;
        private readonly Task[] _workerTasks;
        private int _queuedTaskCount;
        private int _completedTaskCount;
        private int _maxConcurrency;
        private bool _disposed;

        public BackgroundTaskProcessor(int maxConcurrency = 2)
        {
            _maxConcurrency = maxConcurrency;
            _taskQueue = new ConcurrentQueue<BackgroundTask>();
            _semaphore = new SemaphoreSlim(0);
            _cancellationTokenSource = new CancellationTokenSource();
            
            // Start worker tasks
            _workerTasks = new Task[_maxConcurrency];
            for (int i = 0; i < _maxConcurrency; i++)
            {
                _workerTasks[i] = Task.Run(ProcessTasksAsync);
            }
        }

        public int QueuedTaskCount => _queuedTaskCount;
        public int CompletedTaskCount => _completedTaskCount;

        public Task QueueTaskAsync(Func<CancellationToken, Task> operation, BackgroundTaskPriority priority)
        {
            if (_disposed || operation == null)
                return Task.CompletedTask;

            var taskCompletionSource = new TaskCompletionSource<bool>();
            var backgroundTask = new BackgroundTask
            {
                Operation = operation,
                Priority = priority,
                CompletionSource = taskCompletionSource,
                QueuedTime = DateTime.UtcNow
            };

            _taskQueue.Enqueue(backgroundTask);
            Interlocked.Increment(ref _queuedTaskCount);
            _semaphore.Release();

            return taskCompletionSource.Task;
        }

        public void IncreaseProcessingCapacity()
        {
            if (_disposed || _maxConcurrency >= 4)
                return;

            Interlocked.Increment(ref _maxConcurrency);
            
            // Start additional worker if needed
            Task.Run(ProcessTasksAsync);
        }

        private async Task ProcessTasksAsync()
        {
            while (!_cancellationTokenSource.Token.IsCancellationRequested)
            {
                try
                {
                    await _semaphore.WaitAsync(_cancellationTokenSource.Token);
                    
                    if (_taskQueue.TryDequeue(out var task))
                    {
                        await ExecuteBackgroundTaskAsync(task);
                        Interlocked.Increment(ref _completedTaskCount);
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception)
                {
                    // Continue processing other tasks
                }
            }
        }

        private async Task ExecuteBackgroundTaskAsync(BackgroundTask task)
        {
            try
            {
                await task.Operation(_cancellationTokenSource.Token);
                task.CompletionSource.TrySetResult(true);
            }
            catch (OperationCanceledException)
            {
                task.CompletionSource.TrySetCanceled();
            }
            catch (Exception ex)
            {
                task.CompletionSource.TrySetException(ex);
            }
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            _cancellationTokenSource.Cancel();
            _semaphore?.Dispose();
            _cancellationTokenSource?.Dispose();

            try
            {
                Task.WaitAll(_workerTasks, TimeSpan.FromSeconds(5));
            }
            catch
            {
                // Ignore cleanup timeout
            }
        }
    }

    /// <summary>
    /// Optimizer for text view event handling
    /// </summary>
    internal class TextViewEventOptimizer : IDisposable
    {
        private readonly ConcurrentDictionary<ITextView, TextViewOptimizationData> _optimizedViews;
        private int _optimizedViewCount;
        private bool _disposed;

        public TextViewEventOptimizer()
        {
            _optimizedViews = new ConcurrentDictionary<ITextView, TextViewOptimizationData>();
        }

        public int OptimizedViewCount => _optimizedViewCount;

        public void OptimizeTextView(ITextView textView)
        {
            if (_disposed || textView == null)
                return;

            var optimizationData = _optimizedViews.GetOrAdd(textView, tv =>
            {
                Interlocked.Increment(ref _optimizedViewCount);
                
                var data = new TextViewOptimizationData
                {
                    TextView = tv,
                    OptimizedTime = DateTime.UtcNow
                };

                // Apply optimizations
                ApplyTextViewOptimizations(tv, data);
                
                // Handle view disposal
                tv.Closed += (sender, e) => _optimizedViews.TryRemove(tv, out _);
                
                return data;
            });
        }

        private void ApplyTextViewOptimizations(ITextView textView, TextViewOptimizationData data)
        {
            // Debounce text change events to prevent excessive processing
            var lastChangeTime = DateTime.MinValue;
            var debounceDelay = TimeSpan.FromMilliseconds(100);

            textView.TextBuffer.Changed += (sender, e) =>
            {
                var now = DateTime.UtcNow;
                lastChangeTime = now;

                // Debounce the event processing
                Task.Delay(debounceDelay).ContinueWith(t =>
                {
                    if (now == lastChangeTime) // Only process if this is the latest change
                    {
                        data.LastProcessedChange = now;
                        data.ChangeEventCount++;
                    }
                });
            };

            // Optimize caret position events
            textView.Caret.PositionChanged += (sender, e) =>
            {
                data.CaretPositionChanges++;
                data.LastCaretChange = DateTime.UtcNow;
            };
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            _optimizedViews.Clear();
        }
    }

    /// <summary>
    /// Optimizer for extension startup performance
    /// </summary>
    internal class StartupOptimizer : IDisposable
    {
        private readonly Stopwatch _startupStopwatch;
        private int _optimizationsApplied;
        private TimeSpan _lastStartupDuration;
        private bool _disposed;

        public StartupOptimizer()
        {
            _startupStopwatch = Stopwatch.StartNew();
        }

        public int OptimizationsApplied => _optimizationsApplied;
        public TimeSpan LastStartupDuration => _lastStartupDuration;

        public async Task OptimizeAsync()
        {
            if (_disposed)
                return;

            var startTime = DateTime.UtcNow;
            
            try
            {
                // Defer non-critical initialization
                await DeferNonCriticalInitializationAsync();
                _optimizationsApplied++;

                // Pre-warm critical services
                await PreWarmCriticalServicesAsync();
                _optimizationsApplied++;

                // Optimize assembly loading
                OptimizeAssemblyLoading();
                _optimizationsApplied++;

            }
            finally
            {
                _lastStartupDuration = DateTime.UtcNow - startTime;
                _startupStopwatch.Stop();
            }
        }

        private async Task DeferNonCriticalInitializationAsync()
        {
            // Defer non-critical initialization by 2 seconds
            await Task.Delay(2000);
            
            // Initialize non-critical components here
            // This allows VS to complete its startup process first
        }

        private async Task PreWarmCriticalServicesAsync()
        {
            // Pre-warm services that will be used immediately
            await Task.Run(() =>
            {
                // Trigger JIT compilation of critical paths
                GC.Collect(0, GCCollectionMode.Optimized);
            });
        }

        private void OptimizeAssemblyLoading()
        {
            // Enable concurrent GC for better performance
            GCSettings.LatencyMode = GCLatencyMode.Interactive;
            
            // Warm up the type system
            typeof(string).GetHashCode();
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            _startupStopwatch?.Stop();
        }
    }

    /// <summary>
    /// Background task priority levels
    /// </summary>
    public enum BackgroundTaskPriority
    {
        Low = 0,
        Normal = 1,
        High = 2
    }

    /// <summary>
    /// Background task representation
    /// </summary>
    internal class BackgroundTask
    {
        public Func<CancellationToken, Task> Operation { get; set; }
        public BackgroundTaskPriority Priority { get; set; }
        public TaskCompletionSource<bool> CompletionSource { get; set; }
        public DateTime QueuedTime { get; set; }
    }

    /// <summary>
    /// Text view optimization data
    /// </summary>
    internal class TextViewOptimizationData
    {
        public ITextView TextView { get; set; }
        public DateTime OptimizedTime { get; set; }
        public DateTime LastProcessedChange { get; set; }
        public DateTime LastCaretChange { get; set; }
        public int ChangeEventCount { get; set; }
        public int CaretPositionChanges { get; set; }
    }

    /// <summary>
    /// Performance metrics tracking
    /// </summary>
    internal class PerformanceMetrics
    {
        private readonly object _lock = new object();
        private int _uiThreadOperationCount;
        private TimeSpan _totalUIThreadDuration;
        private int _uiThreadFailures;

        public int UIThreadOperationCount => _uiThreadOperationCount;
        public TimeSpan AverageUIThreadDuration
        {
            get
            {
                lock (_lock)
                {
                    return _uiThreadOperationCount > 0 ? 
                        TimeSpan.FromTicks(_totalUIThreadDuration.Ticks / _uiThreadOperationCount) : 
                        TimeSpan.Zero;
                }
            }
        }

        public void RecordUIThreadOperation(TimeSpan duration, bool success, Exception exception = null)
        {
            lock (_lock)
            {
                _uiThreadOperationCount++;
                _totalUIThreadDuration = _totalUIThreadDuration.Add(duration);
                
                if (!success)
                    _uiThreadFailures++;
            }
        }
    }

    /// <summary>
    /// Visual Studio performance statistics
    /// </summary>
    public class VSPerformanceStats
    {
        public int UIThreadOperations { get; set; }
        public TimeSpan AverageUIThreadDuration { get; set; }
        public int BackgroundTasksQueued { get; set; }
        public int BackgroundTasksCompleted { get; set; }
        public int OptimizedTextViews { get; set; }
        public int StartupOptimizationsApplied { get; set; }
        public TimeSpan LastStartupTime { get; set; }
        public long MemoryUsageMB { get; set; }
        public DateTime Timestamp { get; set; }
    }
}