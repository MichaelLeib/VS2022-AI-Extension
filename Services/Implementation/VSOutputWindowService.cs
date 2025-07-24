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
    /// Service for integrating with Visual Studio Output Window
    /// </summary>
    [Export(typeof(IVSOutputWindowService))]
    public class VSOutputWindowService : IVSOutputWindowService
    {
        private const string OUTPUT_PANE_NAME = "Ollama Assistant";
        private readonly Guid _outputPaneGuid = new Guid("E14C5C9A-7F8B-4E5D-9A2B-3C4D5E6F7A8B");
        
        private IVsOutputWindow _outputWindow;
        private IVsOutputWindowPane _pane;
        private readonly object _lockObject = new object();
        private bool _isInitialized;

        /// <summary>
        /// Gets or sets whether the output window is enabled
        /// </summary>
        public bool IsEnabled { get; set; } = true;

        /// <summary>
        /// Initializes the output window service
        /// </summary>
        public async Task InitializeAsync()
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            try
            {
                if (_isInitialized)
                    return;

                // Get the output window service
                _outputWindow = ServiceProvider.GlobalProvider.GetService(typeof(SVsOutputWindow)) as IVsOutputWindow;
                if (_outputWindow == null)
                    return;

                // Create or get the output pane
                var hr = _outputWindow.CreatePane(ref _outputPaneGuid, OUTPUT_PANE_NAME, 1, 1);
                if (ErrorHandler.Failed(hr))
                    return;

                hr = _outputWindow.GetPane(ref _outputPaneGuid, out _pane);
                if (ErrorHandler.Failed(hr) || _pane == null)
                    return;

                _isInitialized = true;
            }
            catch (Exception ex)
            {
                // Log to VS activity log as fallback
                ActivityLog.LogError(nameof(VSOutputWindowService), $"Failed to initialize output window: {ex.Message}");
            }
        }

        /// <summary>
        /// Writes a message to the output window
        /// </summary>
        public async Task WriteLineAsync(string message)
        {
            if (!IsEnabled || string.IsNullOrEmpty(message))
                return;

            await WriteAsync(message + Environment.NewLine);
        }

        /// <summary>
        /// Writes text to the output window
        /// </summary>
        public async Task WriteAsync(string text)
        {
            if (!IsEnabled || string.IsNullOrEmpty(text))
                return;

            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            try
            {
                lock (_lockObject)
                {
                    if (!_isInitialized)
                    {
                        InitializeAsync().Wait();
                    }

                    if (_pane != null)
                    {
                        var timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
                        var formattedText = $"[{timestamp}] {text}";
                        _pane.OutputString(formattedText);
                    }
                }
            }
            catch (Exception ex)
            {
                // Fallback to activity log
                ActivityLog.LogError(nameof(VSOutputWindowService), $"Failed to write to output window: {ex.Message}");
            }
        }

        /// <summary>
        /// Writes an informational message with formatting
        /// </summary>
        public async Task WriteInfoAsync(string message, string source = null)
        {
            var formattedMessage = FormatMessage("INFO", message, source);
            await WriteLineAsync(formattedMessage);
        }

        /// <summary>
        /// Writes a warning message with formatting
        /// </summary>
        public async Task WriteWarningAsync(string message, string source = null)
        {
            var formattedMessage = FormatMessage("WARN", message, source);
            await WriteLineAsync(formattedMessage);
        }

        /// <summary>
        /// Writes an error message with formatting
        /// </summary>
        public async Task WriteErrorAsync(string message, string source = null)
        {
            var formattedMessage = FormatMessage("ERROR", message, source);
            await WriteLineAsync(formattedMessage);
        }

        /// <summary>
        /// Writes an error message from an exception
        /// </summary>
        public async Task WriteExceptionAsync(Exception exception, string context = null)
        {
            if (exception == null)
                return;

            var message = $"Exception in {context ?? "Unknown"}: {exception.Message}";
            if (exception.InnerException != null)
            {
                message += $" Inner: {exception.InnerException.Message}";
            }
            
            await WriteErrorAsync(message);
            
            // Write stack trace for debugging
            if (!string.IsNullOrEmpty(exception.StackTrace))
            {
                await WriteLineAsync($"Stack Trace: {exception.StackTrace}");
            }
        }

        /// <summary>
        /// Clears the output window
        /// </summary>
        public async Task ClearAsync()
        {
            if (!IsEnabled)
                return;

            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            try
            {
                lock (_lockObject)
                {
                    _pane?.Clear();
                }
            }
            catch (Exception ex)
            {
                ActivityLog.LogError(nameof(VSOutputWindowService), $"Failed to clear output window: {ex.Message}");
            }
        }

        /// <summary>
        /// Activates and shows the output window
        /// </summary>
        public async Task ShowAsync()
        {
            if (!IsEnabled)
                return;

            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            try
            {
                lock (_lockObject)
                {
                    if (_pane != null)
                    {
                        _pane.Activate();
                        _outputWindow?.GetPane(ref _outputPaneGuid, out _pane);
                    }
                }
            }
            catch (Exception ex)
            {
                ActivityLog.LogError(nameof(VSOutputWindowService), $"Failed to show output window: {ex.Message}");
            }
        }

        /// <summary>
        /// Formats a message with level and source information
        /// </summary>
        private string FormatMessage(string level, string message, string source)
        {
            var sourceInfo = !string.IsNullOrEmpty(source) ? $"[{source}] " : "";
            return $"{level}: {sourceInfo}{message}";
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
                    _pane = null;
                    _outputWindow = null;
                    _isInitialized = false;
                }
            }
            catch
            {
                // Ignore disposal errors
            }
        }
    }
}