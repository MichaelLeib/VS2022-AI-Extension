using System;
using System.ComponentModel.Composition;
using System.Threading.Tasks;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using OllamaAssistant.Services.Interfaces;

namespace OllamaAssistant.Services.Implementation
{
    /// <summary>
    /// Service for integrating with Visual Studio Status Bar
    /// </summary>
    [Export(typeof(IVSStatusBarService))]
    public class VSStatusBarService : IVSStatusBarService
    {
        private IVsStatusbar _statusBar;
        private readonly object _lockObject = new object();
        private bool _isInitialized;
        private uint _currentProgress;
        private string _currentLabel = "";

        /// <summary>
        /// Gets or sets whether the status bar integration is enabled
        /// </summary>
        public bool IsEnabled { get; set; } = true;

        /// <summary>
        /// Initializes the status bar service
        /// </summary>
        public async Task InitializeAsync()
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            try
            {
                if (_isInitialized)
                    return;

                _statusBar = ServiceProvider.GlobalProvider.GetService(typeof(SVsStatusbar)) as IVsStatusbar;
                _isInitialized = _statusBar != null;
            }
            catch (Exception ex)
            {
                ActivityLog.LogError(nameof(VSStatusBarService), $"Failed to initialize status bar: {ex.Message}");
            }
        }

        /// <summary>
        /// Sets the status bar text
        /// </summary>
        public async Task SetTextAsync(string text)
        {
            if (!IsEnabled || string.IsNullOrEmpty(text))
                return;

            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            try
            {
                await EnsureInitializedAsync();
                
                lock (_lockObject)
                {
                    if (_statusBar != null)
                    {
                        _statusBar.SetText(text);
                        _currentLabel = text;
                    }
                }
            }
            catch (Exception ex)
            {
                ActivityLog.LogError(nameof(VSStatusBarService), $"Failed to set status bar text: {ex.Message}");
            }
        }

        /// <summary>
        /// Shows AI processing status
        /// </summary>
        public async Task ShowAIProcessingAsync(string operation = "Processing AI request...")
        {
            if (!IsEnabled)
                return;

            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            try
            {
                await EnsureInitializedAsync();
                
                lock (_lockObject)
                {
                    if (_statusBar != null)
                    {
                        var text = $"🤖 {operation}";
                        _statusBar.SetText(text);
                        _currentLabel = text;
                        
                        // Start animation
                        _statusBar.Animation(1, ref VSConstants.SBAI_General);
                    }
                }
            }
            catch (Exception ex)
            {
                ActivityLog.LogError(nameof(VSStatusBarService), $"Failed to show AI processing status: {ex.Message}");
            }
        }

        /// <summary>
        /// Shows AI completion status
        /// </summary>
        public async Task ShowAICompletedAsync(string result = "AI request completed")
        {
            if (!IsEnabled)
                return;

            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            try
            {
                await EnsureInitializedAsync();
                
                lock (_lockObject)
                {
                    if (_statusBar != null)
                    {
                        var text = $"✅ {result}";
                        _statusBar.SetText(text);
                        _currentLabel = text;
                        
                        // Stop animation
                        _statusBar.Animation(0, ref VSConstants.SBAI_General);
                        
                        // Clear after 3 seconds
                        Task.Delay(3000).ContinueWith(_ => ClearAsync());
                    }
                }
            }
            catch (Exception ex)
            {
                ActivityLog.LogError(nameof(VSStatusBarService), $"Failed to show AI completed status: {ex.Message}");
            }
        }

        /// <summary>
        /// Shows AI error status
        /// </summary>
        public async Task ShowAIErrorAsync(string error = "AI request failed")
        {
            if (!IsEnabled)
                return;

            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            try
            {
                await EnsureInitializedAsync();
                
                lock (_lockObject)
                {
                    if (_statusBar != null)
                    {
                        var text = $"❌ {error}";
                        _statusBar.SetText(text);
                        _currentLabel = text;
                        
                        // Stop animation
                        _statusBar.Animation(0, ref VSConstants.SBAI_General);
                        
                        // Clear after 5 seconds (longer for errors)
                        Task.Delay(5000).ContinueWith(_ => ClearAsync());
                    }
                }
            }
            catch (Exception ex)
            {
                ActivityLog.LogError(nameof(VSStatusBarService), $"Failed to show AI error status: {ex.Message}");
            }
        }

        /// <summary>
        /// Shows progress with a progress bar
        /// </summary>
        public async Task ShowProgressAsync(string label, uint current, uint total)
        {
            if (!IsEnabled)
                return;

            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            try
            {
                await EnsureInitializedAsync();
                
                lock (_lockObject)
                {
                    if (_statusBar != null)
                    {
                        _currentProgress = current;
                        _currentLabel = label;
                        
                        // Set progress bar
                        _statusBar.Progress(ref _currentProgress, label, current, total);
                        
                        // Also update text
                        var progressText = $"{label} ({current}/{total})";
                        _statusBar.SetText(progressText);
                    }
                }
            }
            catch (Exception ex)
            {
                ActivityLog.LogError(nameof(VSStatusBarService), $"Failed to show progress: {ex.Message}");
            }
        }

        /// <summary>
        /// Updates existing progress
        /// </summary>
        public async Task UpdateProgressAsync(uint current, uint total)
        {
            if (!IsEnabled)
                return;

            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            try
            {
                await EnsureInitializedAsync();
                
                lock (_lockObject)
                {
                    if (_statusBar != null)
                    {
                        _currentProgress = current;
                        
                        // Update progress bar
                        _statusBar.Progress(ref _currentProgress, _currentLabel, current, total);
                        
                        // Update text
                        var progressText = $"{_currentLabel} ({current}/{total})";
                        _statusBar.SetText(progressText);
                    }
                }
            }
            catch (Exception ex)
            {
                ActivityLog.LogError(nameof(VSStatusBarService), $"Failed to update progress: {ex.Message}");
            }
        }

        /// <summary>
        /// Clears the progress bar
        /// </summary>
        public async Task ClearProgressAsync()
        {
            if (!IsEnabled)
                return;

            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            try
            {
                await EnsureInitializedAsync();
                
                lock (_lockObject)
                {
                    if (_statusBar != null)
                    {
                        _currentProgress = 0;
                        _statusBar.Progress(ref _currentProgress, "", 0, 0);
                    }
                }
            }
            catch (Exception ex)
            {
                ActivityLog.LogError(nameof(VSStatusBarService), $"Failed to clear progress: {ex.Message}");
            }
        }

        /// <summary>
        /// Clears the status bar
        /// </summary>
        public async Task ClearAsync()
        {
            if (!IsEnabled)
                return;

            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            try
            {
                await EnsureInitializedAsync();
                
                lock (_lockObject)
                {
                    if (_statusBar != null)
                    {
                        _statusBar.Clear();
                        _currentLabel = "";
                        _currentProgress = 0;
                        
                        // Stop any animations
                        _statusBar.Animation(0, ref VSConstants.SBAI_General);
                    }
                }
            }
            catch (Exception ex)
            {
                ActivityLog.LogError(nameof(VSStatusBarService), $"Failed to clear status bar: {ex.Message}");
            }
        }

        /// <summary>
        /// Shows Ollama connection status
        /// </summary>
        public async Task ShowConnectionStatusAsync(bool isConnected, string serverInfo = null)
        {
            if (!IsEnabled)
                return;

            var statusIcon = isConnected ? "🟢" : "🔴";
            var statusText = isConnected ? "Connected" : "Disconnected";
            var fullText = $"Ollama {statusIcon} {statusText}";
            
            if (isConnected && !string.IsNullOrEmpty(serverInfo))
            {
                fullText += $" ({serverInfo})";
            }

            await SetTextAsync(fullText);
            
            // Clear after 3 seconds if connected, keep visible if disconnected
            if (isConnected)
            {
                Task.Delay(3000).ContinueWith(_ => ClearAsync());
            }
        }

        /// <summary>
        /// Shows suggestion acceptance status
        /// </summary>
        public async Task ShowSuggestionAcceptedAsync(string suggestionType = null)
        {
            if (!IsEnabled)
                return;

            var text = suggestionType != null 
                ? $"✨ AI {suggestionType} suggestion accepted"
                : "✨ AI suggestion accepted";
                
            await SetTextAsync(text);
            
            // Clear after 2 seconds
            Task.Delay(2000).ContinueWith(_ => ClearAsync());
        }

        /// <summary>
        /// Ensures the service is initialized
        /// </summary>
        private async Task EnsureInitializedAsync()
        {
            if (!_isInitialized)
            {
                await InitializeAsync();
            }
        }

        /// <summary>
        /// Disposes the service
        /// </summary>
        public void Dispose()
        {
            try
            {
                lock (_lockObject)
                {
                    _statusBar = null;
                    _isInitialized = false;
                    _currentProgress = 0;
                    _currentLabel = "";
                }
            }
            catch
            {
                // Ignore disposal errors
            }
        }
    }
}