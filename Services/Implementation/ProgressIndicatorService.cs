using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Threading;
using OllamaAssistant.Services.Interfaces;

namespace OllamaAssistant.Services.Implementation
{
    /// <summary>
    /// Service for managing progress indicators and loading states across the extension
    /// </summary>
    public class ProgressIndicatorService : IDisposable
    {
        private readonly JoinableTaskFactory _joinableTaskFactory;
        private readonly ConcurrentDictionary<string, ProgressOperation> _activeOperations;
        private readonly IVsStatusbar _statusBar;
        private readonly IVsThreadedWaitDialogFactory _waitDialogFactory;
        private bool _disposed;

        public ProgressIndicatorService(JoinableTaskFactory joinableTaskFactory = null)
        {
            _joinableTaskFactory = joinableTaskFactory ?? ThreadHelper.JoinableTaskFactory;
            _activeOperations = new ConcurrentDictionary<string, ProgressOperation>();
            
            // Get VS services
            _statusBar = ServiceProvider.GlobalProvider.GetService(typeof(SVsStatusbar)) as IVsStatusbar;
            _waitDialogFactory = ServiceProvider.GlobalProvider.GetService(typeof(SVsThreadedWaitDialogFactory)) as IVsThreadedWaitDialogFactory;
        }

        /// <summary>
        /// Shows a progress indicator for an AI request operation
        /// </summary>
        public async Task<IProgressToken> ShowAIRequestProgressAsync(string operationId, string message, CancellationToken cancellationToken = default)
        {
            if (_disposed || string.IsNullOrEmpty(operationId))
                return new NullProgressToken();

            var operation = new ProgressOperation
            {
                OperationId = operationId,
                Message = message,
                StartTime = DateTime.UtcNow,
                CancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)
            };

            _activeOperations.TryAdd(operationId, operation);

            // Show progress in status bar
            await ShowStatusBarProgressAsync(message, operation.CancellationTokenSource.Token);

            // Create progress token for the operation
            var progressToken = new ProgressToken(operationId, this, operation.CancellationTokenSource);
            operation.ProgressToken = progressToken;

            return progressToken;
        }

        /// <summary>
        /// Shows a threaded wait dialog for long-running operations
        /// </summary>
        public async Task<IProgressToken> ShowThreadedWaitDialogAsync(string operationId, string title, string message, bool isCancellable = true)
        {
            if (_disposed || string.IsNullOrEmpty(operationId))
                return new NullProgressToken();

            var operation = new ProgressOperation
            {
                OperationId = operationId,
                Message = message,
                StartTime = DateTime.UtcNow,
                CancellationTokenSource = new CancellationTokenSource()
            };

            _activeOperations.TryAdd(operationId, operation);

            await _joinableTaskFactory.SwitchToMainThreadAsync();

            try
            {
                if (_waitDialogFactory != null)
                {
                    _waitDialogFactory.CreateInstance(out var waitDialog);
                    
                    waitDialog.StartWaitDialog(
                        title,
                        message,
                        null, // progress text
                        null, // status bar text
                        null, // delay to show dialog
                        isCancellable,
                        true); // show progress

                    operation.WaitDialog = waitDialog;
                }
            }
            catch (Exception)
            {
                // Continue without dialog if creation fails
            }

            var progressToken = new ProgressToken(operationId, this, operation.CancellationTokenSource);
            operation.ProgressToken = progressToken;

            return progressToken;
        }

        /// <summary>
        /// Shows IntelliSense loading spinner
        /// </summary>
        public ILoadingSpinner ShowIntelliSenseSpinner(string operationId)
        {
            if (_disposed || string.IsNullOrEmpty(operationId))
                return new NullLoadingSpinner();

            var spinner = new IntelliSenseLoadingSpinner(operationId);
            
            var operation = new ProgressOperation
            {
                OperationId = operationId,
                Message = "Loading AI suggestions...",
                StartTime = DateTime.UtcNow,
                LoadingSpinner = spinner
            };

            _activeOperations.TryAdd(operationId, operation);
            return spinner;
        }

        /// <summary>
        /// Updates progress for an active operation
        /// </summary>
        public async Task UpdateProgressAsync(string operationId, int percentage, string message = null)
        {
            if (_disposed || !_activeOperations.TryGetValue(operationId, out var operation))
                return;

            operation.Progress = percentage;
            if (!string.IsNullOrEmpty(message))
                operation.Message = message;

            await _joinableTaskFactory.SwitchToMainThreadAsync();

            try
            {
                // Update wait dialog if present
                if (operation.WaitDialog != null)
                {
                    operation.WaitDialog.UpdateProgress(
                        message ?? operation.Message,
                        null, // progress text
                        null, // status bar text
                        percentage,
                        100,
                        true,
                        out bool cancelled);

                    if (cancelled)
                    {
                        operation.CancellationTokenSource?.Cancel();
                    }
                }

                // Update status bar
                if (_statusBar != null)
                {
                    _statusBar.Progress(ref operation.StatusBarCookie, 1, message ?? operation.Message, (uint)percentage, 100);
                }
            }
            catch (Exception)
            {
                // Continue if update fails
            }
        }

        /// <summary>
        /// Completes and removes a progress operation
        /// </summary>
        public async Task CompleteOperationAsync(string operationId, bool success = true, string finalMessage = null)
        {
            if (_disposed || !_activeOperations.TryRemove(operationId, out var operation))
                return;

            await _joinableTaskFactory.SwitchToMainThreadAsync();

            try
            {
                // Complete wait dialog
                if (operation.WaitDialog != null)
                {
                    operation.WaitDialog.EndWaitDialog(out int userCancel);
                    operation.WaitDialog = null;
                }

                // Clear status bar
                if (_statusBar != null && operation.StatusBarCookie != 0)
                {
                    _statusBar.Progress(ref operation.StatusBarCookie, 0, "", 0, 0);
                }

                // Complete loading spinner
                operation.LoadingSpinner?.Complete(success);

                // Show final message if provided
                if (!string.IsNullOrEmpty(finalMessage))
                {
                    await ShowStatusMessageAsync(finalMessage, success);
                }
            }
            catch (Exception)
            {
                // Continue if cleanup fails
            }
            finally
            {
                operation.CancellationTokenSource?.Dispose();
            }
        }

        /// <summary>
        /// Cancels an active operation
        /// </summary>
        public async Task CancelOperationAsync(string operationId, string cancelMessage = null)
        {
            if (_disposed || !_activeOperations.TryGetValue(operationId, out var operation))
                return;

            operation.CancellationTokenSource?.Cancel();
            await CompleteOperationAsync(operationId, success: false, cancelMessage ?? "Operation cancelled");
        }

        /// <summary>
        /// Gets statistics about active progress operations
        /// </summary>
        public ProgressStatistics GetProgressStatistics()
        {
            return new ProgressStatistics
            {
                ActiveOperations = _activeOperations.Count,
                LongestRunningOperation = GetLongestRunningOperationDuration(),
                TotalOperationsStarted = _totalOperationsStarted,
                TotalOperationsCompleted = _totalOperationsCompleted,
                TotalOperationsCancelled = _totalOperationsCancelled,
                Timestamp = DateTime.UtcNow
            };
        }

        private int _totalOperationsStarted;
        private int _totalOperationsCompleted;
        private int _totalOperationsCancelled;

        /// <summary>
        /// Shows progress in Visual Studio status bar
        /// </summary>
        private async Task ShowStatusBarProgressAsync(string message, CancellationToken cancellationToken)
        {
            if (_statusBar == null)
                return;

            await _joinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

            try
            {
                uint cookie = 0;
                _statusBar.Progress(ref cookie, 1, message, 0, 0);
                Interlocked.Increment(ref _totalOperationsStarted);
            }
            catch (Exception)
            {
                // Continue if status bar update fails
            }
        }

        /// <summary>
        /// Shows a message in the status bar
        /// </summary>
        private async Task ShowStatusMessageAsync(string message, bool isSuccess)
        {
            if (_statusBar == null)
                return;

            await _joinableTaskFactory.SwitchToMainThreadAsync();

            try
            {
                _statusBar.SetText(message);
                
                if (isSuccess)
                    Interlocked.Increment(ref _totalOperationsCompleted);
                else
                    Interlocked.Increment(ref _totalOperationsCancelled);
            }
            catch (Exception)
            {
                // Continue if status bar update fails
            }
        }

        /// <summary>
        /// Gets the duration of the longest running operation
        /// </summary>
        private TimeSpan GetLongestRunningOperationDuration()
        {
            var maxDuration = TimeSpan.Zero;
            var now = DateTime.UtcNow;

            foreach (var operation in _activeOperations.Values)
            {
                var duration = now - operation.StartTime;
                if (duration > maxDuration)
                    maxDuration = duration;
            }

            return maxDuration;
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;

            // Complete all active operations
            foreach (var operation in _activeOperations.Values)
            {
                try
                {
                    operation.CancellationTokenSource?.Cancel();
                    operation.WaitDialog?.EndWaitDialog(out _);
                }
                catch
                {
                    // Ignore cleanup errors
                }
            }

            _activeOperations.Clear();
        }
    }

    /// <summary>
    /// Represents an active progress operation
    /// </summary>
    internal class ProgressOperation
    {
        public string OperationId { get; set; }
        public string Message { get; set; }
        public DateTime StartTime { get; set; }
        public int Progress { get; set; }
        public CancellationTokenSource CancellationTokenSource { get; set; }
        public IVsThreadedWaitDialog2 WaitDialog { get; set; }
        public ILoadingSpinner LoadingSpinner { get; set; }
        public IProgressToken ProgressToken { get; set; }
        public uint StatusBarCookie { get; set; }
    }

    /// <summary>
    /// Token for tracking and controlling progress operations
    /// </summary>
    public interface IProgressToken : IDisposable
    {
        string OperationId { get; }
        bool IsCancelled { get; }
        CancellationToken CancellationToken { get; }
        Task UpdateAsync(int percentage, string message = null);
        Task CompleteAsync(bool success = true, string finalMessage = null);
        Task CancelAsync(string cancelMessage = null);
    }

    /// <summary>
    /// Implementation of progress token
    /// </summary>
    internal class ProgressToken : IProgressToken
    {
        private readonly ProgressIndicatorService _service;
        private readonly CancellationTokenSource _cancellationTokenSource;
        private bool _disposed;

        public ProgressToken(string operationId, ProgressIndicatorService service, CancellationTokenSource cancellationTokenSource)
        {
            OperationId = operationId;
            _service = service;
            _cancellationTokenSource = cancellationTokenSource;
        }

        public string OperationId { get; }
        public bool IsCancelled => _cancellationTokenSource?.Token.IsCancellationRequested ?? true;
        public CancellationToken CancellationToken => _cancellationTokenSource?.Token ?? new CancellationToken(true);

        public async Task UpdateAsync(int percentage, string message = null)
        {
            if (!_disposed)
                await _service.UpdateProgressAsync(OperationId, percentage, message);
        }

        public async Task CompleteAsync(bool success = true, string finalMessage = null)
        {
            if (!_disposed)
            {
                await _service.CompleteOperationAsync(OperationId, success, finalMessage);
                _disposed = true;
            }
        }

        public async Task CancelAsync(string cancelMessage = null)
        {
            if (!_disposed)
            {
                await _service.CancelOperationAsync(OperationId, cancelMessage);
                _disposed = true;
            }
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                _cancellationTokenSource?.Cancel();
            }
        }
    }

    /// <summary>
    /// Null implementation of progress token
    /// </summary>
    internal class NullProgressToken : IProgressToken
    {
        public string OperationId => string.Empty;
        public bool IsCancelled => false;
        public CancellationToken CancellationToken => CancellationToken.None;

        public Task UpdateAsync(int percentage, string message = null) => Task.CompletedTask;
        public Task CompleteAsync(bool success = true, string finalMessage = null) => Task.CompletedTask;
        public Task CancelAsync(string cancelMessage = null) => Task.CompletedTask;
        public void Dispose() { }
    }

    /// <summary>
    /// Interface for loading spinners
    /// </summary>
    public interface ILoadingSpinner : IDisposable
    {
        string OperationId { get; }
        bool IsActive { get; }
        void Show();
        void Hide();
        void Complete(bool success);
    }

    /// <summary>
    /// IntelliSense loading spinner implementation
    /// </summary>
    internal class IntelliSenseLoadingSpinner : ILoadingSpinner
    {
        private bool _isActive;
        private bool _disposed;

        public IntelliSenseLoadingSpinner(string operationId)
        {
            OperationId = operationId;
        }

        public string OperationId { get; }
        public bool IsActive => _isActive && !_disposed;

        public void Show()
        {
            if (!_disposed)
            {
                _isActive = true;
                // In a real implementation, this would show a spinner in the IntelliSense popup
                // For now, this is a placeholder for the UI integration
            }
        }

        public void Hide()
        {
            _isActive = false;
        }

        public void Complete(bool success)
        {
            Hide();
            // Could show a brief success/failure indicator
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                Hide();
            }
        }
    }

    /// <summary>
    /// Null implementation of loading spinner
    /// </summary>
    internal class NullLoadingSpinner : ILoadingSpinner
    {
        public string OperationId => string.Empty;
        public bool IsActive => false;
        public void Show() { }
        public void Hide() { }
        public void Complete(bool success) { }
        public void Dispose() { }
    }

    /// <summary>
    /// Statistics about progress operations
    /// </summary>
    public class ProgressStatistics
    {
        public int ActiveOperations { get; set; }
        public TimeSpan LongestRunningOperation { get; set; }
        public int TotalOperationsStarted { get; set; }
        public int TotalOperationsCompleted { get; set; }
        public int TotalOperationsCancelled { get; set; }
        public DateTime Timestamp { get; set; }
    }
}