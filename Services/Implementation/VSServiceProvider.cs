using System;
using System.ComponentModel.Composition;
using System.Threading.Tasks;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.TextManager.Interop;
using OllamaAssistant.Services.Interfaces;

namespace OllamaAssistant.Services.Implementation
{
    /// <summary>
    /// Provides access to Visual Studio services with proper thread marshaling
    /// </summary>
    [Export(typeof(IVSServiceProvider))]
    public class VSServiceProvider : IVSServiceProvider
    {
        private readonly object _lockObject = new object();
        private bool _isInitialized;

        // Cached services
        private IVsUIShell _uiShell;
        private IVsTextManager _textManager;
        private IVsMonitorSelection _selectionMonitor;
        private IVsRunningDocumentTable _runningDocumentTable;
        private IVsActivityLog _activityLog;
        private IVsErrorList _errorList;
        private IVsStatusbar _statusBar;
        private IVsOutputWindow _outputWindow;
        private IVsSolution _solution;

        /// <summary>
        /// Gets whether the service provider is initialized
        /// </summary>
        public bool IsInitialized => _isInitialized;

        /// <summary>
        /// Initializes the VS service provider
        /// </summary>
        public async Task InitializeAsync()
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            try
            {
                if (_isInitialized)
                    return;

                // Cache commonly used services with individual error handling
                _uiShell = GetServiceSafely<IVsUIShell>(typeof(SVsUIShell), "UI Shell");
                _textManager = GetServiceSafely<IVsTextManager>(typeof(SVsTextManager), "Text Manager");
                _selectionMonitor = GetServiceSafely<IVsMonitorSelection>(typeof(SVsShellMonitorSelection), "Selection Monitor");
                _runningDocumentTable = GetServiceSafely<IVsRunningDocumentTable>(typeof(SVsRunningDocumentTable), "Running Document Table");
                _activityLog = GetServiceSafely<IVsActivityLog>(typeof(SVsActivityLog), "Activity Log");
                _errorList = GetServiceSafely<IVsErrorList>(typeof(SVsErrorList), "Error List");
                _statusBar = GetServiceSafely<IVsStatusbar>(typeof(SVsStatusbar), "Status Bar");
                _outputWindow = GetServiceSafely<IVsOutputWindow>(typeof(SVsOutputWindow), "Output Window");
                _solution = GetServiceSafely<IVsSolution>(typeof(SVsSolution), "Solution");

                _isInitialized = true;
            }
            catch (Exception ex)
            {
                ActivityLog.LogError(nameof(VSServiceProvider), $"Failed to initialize VS service provider: {ex.Message}");
                
                // Don't throw - allow partial initialization
                _isInitialized = true;
                System.Diagnostics.Debug.WriteLine($"VS Service Provider initialized with errors: {ex.Message}");
            }
        }

        /// <summary>
        /// Safely gets a VS service with error handling
        /// </summary>
        private T GetServiceSafely<T>(Type serviceType, string serviceName) where T : class
        {
            try
            {
                var service = ServiceProvider.GlobalProvider.GetService(serviceType) as T;
                if (service == null)
                {
                    System.Diagnostics.Debug.WriteLine($"Warning: {serviceName} service is not available");
                }
                return service;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error getting {serviceName} service: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Gets the VS UI Shell service
        /// </summary>
        public async Task<IVsUIShell> GetUIShellAsync()
        {
            await EnsureInitializedAsync();
            
            if (_uiShell == null)
            {
                // Try to get the service again if it wasn't available during initialization
                _uiShell = GetServiceSafely<IVsUIShell>(typeof(SVsUIShell), "UI Shell");
            }
            
            return _uiShell;
        }

        /// <summary>
        /// Gets the VS Text Manager service
        /// </summary>
        public async Task<IVsTextManager> GetTextManagerAsync()
        {
            await EnsureInitializedAsync();
            return _textManager;
        }

        /// <summary>
        /// Gets the VS Selection Monitor service
        /// </summary>
        public async Task<IVsMonitorSelection> GetSelectionMonitorAsync()
        {
            await EnsureInitializedAsync();
            return _selectionMonitor;
        }

        /// <summary>
        /// Gets the VS Running Document Table service
        /// </summary>
        public async Task<IVsRunningDocumentTable> GetRunningDocumentTableAsync()
        {
            await EnsureInitializedAsync();
            return _runningDocumentTable;
        }

        /// <summary>
        /// Gets the VS Activity Log service
        /// </summary>
        public async Task<IVsActivityLog> GetActivityLogAsync()
        {
            await EnsureInitializedAsync();
            return _activityLog;
        }

        /// <summary>
        /// Gets the VS Error List service
        /// </summary>
        public async Task<IVsErrorList> GetErrorListAsync()
        {
            await EnsureInitializedAsync();
            return _errorList;
        }

        /// <summary>
        /// Gets the VS Status Bar service
        /// </summary>
        public async Task<IVsStatusbar> GetStatusBarAsync()
        {
            await EnsureInitializedAsync();
            return _statusBar;
        }

        /// <summary>
        /// Gets the VS Output Window service
        /// </summary>
        public async Task<IVsOutputWindow> GetOutputWindowAsync()
        {
            await EnsureInitializedAsync();
            return _outputWindow;
        }

        /// <summary>
        /// Gets the VS Solution service
        /// </summary>
        public async Task<IVsSolution> GetSolutionAsync()
        {
            await EnsureInitializedAsync();
            return _solution;
        }

        /// <summary>
        /// Gets a service by type
        /// </summary>
        public async Task<T> GetServiceAsync<T>(Type serviceType) where T : class
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            return ServiceProvider.GlobalProvider.GetService(serviceType) as T;
        }

        /// <summary>
        /// Shows a message box
        /// </summary>
        public async Task<int> ShowMessageBoxAsync(string message, string title, OLEMSGICON icon = OLEMSGICON.OLEMSGICON_INFO, OLEMSGBUTTON buttons = OLEMSGBUTTON.OLEMSGBUTTON_OK)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            try
            {
                var uiShell = await GetUIShellAsync();
                if (uiShell != null)
                {
                    var clsid = Guid.Empty;
                    int result;
                    var hr = uiShell.ShowMessageBox(
                        0,
                        ref clsid,
                        title,
                        message,
                        string.Empty,
                        0,
                        buttons,
                        OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST,
                        icon,
                        0,
                        out result);
                        
                    if (hr != VSConstants.S_OK)
                    {
                        System.Diagnostics.Debug.WriteLine($"ShowMessageBox failed with HRESULT: 0x{hr:X8}");
                        // Fallback to system message box
                        var dialogResult = System.Windows.MessageBox.Show(message, title);
                        return dialogResult == System.Windows.MessageBoxResult.OK ? 1 : 0;
                    }
                    
                    return result;
                }
                else
                {
                    // Fallback to system message box when VS UI Shell is unavailable
                    System.Diagnostics.Debug.WriteLine("UI Shell unavailable, using system message box");
                    var dialogResult = System.Windows.MessageBox.Show(message, title);
                    return dialogResult == System.Windows.MessageBoxResult.OK ? 1 : 0;
                }
            }
            catch (COMException ex)
            {
                System.Diagnostics.Debug.WriteLine($"COM error showing message box: {ex.Message} (HRESULT: 0x{ex.HResult:X8})");
                // Fallback to system message box
                var dialogResult = System.Windows.MessageBox.Show(message, title);
                return dialogResult == System.Windows.MessageBoxResult.OK ? 1 : 0;
            }
            catch (Exception ex)
            {
                ActivityLog.LogError(nameof(VSServiceProvider), $"Failed to show message box: {ex.Message}");
                // Last resort fallback
                try
                {
                    var dialogResult = System.Windows.MessageBox.Show(message, title);
                    return dialogResult == System.Windows.MessageBoxResult.OK ? 1 : 0;
                }
                catch
                {
                    // If even system message box fails, just log and return
                    System.Diagnostics.Debug.WriteLine($"All message box fallbacks failed for: {title} - {message}");
                    return 0;
                }
            }
        }

        /// <summary>
        /// Gets the active text view
        /// </summary>
        public async Task<IVsTextView> GetActiveTextViewAsync()
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            try
            {
                var textManager = await GetTextManagerAsync();
                if (textManager == null)
                {
                    System.Diagnostics.Debug.WriteLine("Text Manager is not available for getting active text view");
                    return null;
                }

                var hr = textManager.GetActiveView(1, null, out IVsTextView activeView);
                if (hr == VSConstants.S_OK && activeView != null)
                {
                    return activeView;
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"GetActiveView failed with HRESULT: 0x{hr:X8}");
                    return null;
                }
            }
            catch (COMException ex)
            {
                System.Diagnostics.Debug.WriteLine($"COM error getting active text view: {ex.Message} (HRESULT: 0x{ex.HResult:X8})");
                return null;
            }
            catch (Exception ex)
            {
                ActivityLog.LogError(nameof(VSServiceProvider), $"Failed to get active text view: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Gets information about the active document
        /// </summary>
        public async Task<(string filePath, IVsHierarchy hierarchy, uint itemId)> GetActiveDocumentAsync()
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            try
            {
                var selectionMonitor = await GetSelectionMonitorAsync();
                if (selectionMonitor == null)
                {
                    System.Diagnostics.Debug.WriteLine("Selection Monitor is not available for getting active document");
                    return (null, null, 0);
                }

                var hr = selectionMonitor.GetCurrentSelection(out IntPtr hierarchyPtr, out uint itemId, out IVsMultiItemSelect multiSelect, out IntPtr selectionContainer);
                if (hr != VSConstants.S_OK)
                {
                    System.Diagnostics.Debug.WriteLine($"GetCurrentSelection failed with HRESULT: 0x{hr:X8}");
                    return (null, null, 0);
                }

                if (hierarchyPtr != IntPtr.Zero)
                {
                    try
                    {
                        var hierarchy = System.Runtime.InteropServices.Marshal.GetObjectForIUnknown(hierarchyPtr) as IVsHierarchy;
                        if (hierarchy != null)
                        {
                            var nameResult = hierarchy.GetCanonicalName(itemId, out string filePath);
                            if (nameResult == VSConstants.S_OK && !string.IsNullOrEmpty(filePath))
                            {
                                return (filePath, hierarchy, itemId);
                            }
                            else
                            {
                                System.Diagnostics.Debug.WriteLine($"GetCanonicalName failed with HRESULT: 0x{nameResult:X8}");
                            }
                        }
                    }
                    finally
                    {
                        // Clean up COM pointer
                        if (hierarchyPtr != IntPtr.Zero)
                        {
                            System.Runtime.InteropServices.Marshal.Release(hierarchyPtr);
                        }
                    }
                }
                
                // Clean up selection container if needed
                if (selectionContainer != IntPtr.Zero)
                {
                    System.Runtime.InteropServices.Marshal.Release(selectionContainer);
                }
            }
            catch (COMException ex)
            {
                System.Diagnostics.Debug.WriteLine($"COM error getting active document: {ex.Message} (HRESULT: 0x{ex.HResult:X8})");
            }
            catch (Exception ex)
            {
                ActivityLog.LogError(nameof(VSServiceProvider), $"Failed to get active document: {ex.Message}");
            }

            return (null, null, 0);
        }

        /// <summary>
        /// Checks if a solution is loaded
        /// </summary>
        public async Task<bool> IsSolutionLoadedAsync()
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            try
            {
                var solution = await GetSolutionAsync();
                if (solution != null)
                {
                    return solution.GetProperty((int)__VSPROPID.VSPROPID_IsSolutionOpen, out object isOpen) == VSConstants.S_OK &&
                           isOpen is bool opened && opened;
                }
            }
            catch (Exception ex)
            {
                ActivityLog.LogError(nameof(VSServiceProvider), $"Failed to check solution state: {ex.Message}");
            }

            return false;
        }

        /// <summary>
        /// Gets the solution file path
        /// </summary>
        public async Task<string> GetSolutionFilePathAsync()
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            try
            {
                var solution = await GetSolutionAsync();
                if (solution?.GetSolutionInfo(out string solutionDir, out string solutionFile, out string userOptsFile) == VSConstants.S_OK)
                {
                    return solutionFile;
                }
            }
            catch (Exception ex)
            {
                ActivityLog.LogError(nameof(VSServiceProvider), $"Failed to get solution file path: {ex.Message}");
            }

            return null;
        }

        /// <summary>
        /// Logs an error to the VS activity log
        /// </summary>
        public async Task LogErrorAsync(string source, string message, Exception exception = null)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            try
            {
                var activityLog = await GetActivityLogAsync();
                var fullMessage = exception != null ? $"{message}\n{exception}" : message;
                activityLog?.LogEntry(
                    (uint)__ACTIVITYLOG_ENTRYTYPE.ALE_ERROR,
                    source,
                    fullMessage);
            }
            catch
            {
                // Fallback to debug output if activity log fails
                System.Diagnostics.Debug.WriteLine($"[ERROR] {source}: {message}");
            }
        }

        /// <summary>
        /// Logs a warning to the VS activity log
        /// </summary>
        public async Task LogWarningAsync(string source, string message)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            try
            {
                var activityLog = await GetActivityLogAsync();
                activityLog?.LogEntry(
                    (uint)__ACTIVITYLOG_ENTRYTYPE.ALE_WARNING,
                    source,
                    message);
            }
            catch
            {
                System.Diagnostics.Debug.WriteLine($"[WARNING] {source}: {message}");
            }
        }

        /// <summary>
        /// Logs an info message to the VS activity log
        /// </summary>
        public async Task LogInfoAsync(string source, string message)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            try
            {
                var activityLog = await GetActivityLogAsync();
                activityLog?.LogEntry(
                    (uint)__ACTIVITYLOG_ENTRYTYPE.ALE_INFORMATION,
                    source,
                    message);
            }
            catch
            {
                System.Diagnostics.Debug.WriteLine($"[INFO] {source}: {message}");
            }
        }

        /// <summary>
        /// Ensures the service provider is initialized
        /// </summary>
        private async Task EnsureInitializedAsync()
        {
            if (!_isInitialized)
            {
                await InitializeAsync();
            }
        }

        /// <summary>
        /// Checks if VS is in a valid state for service operations
        /// </summary>
        public async Task<bool> IsVSReadyAsync()
        {
            try
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                
                // Basic check - can we get essential services?
                var uiShell = ServiceProvider.GlobalProvider.GetService(typeof(SVsUIShell));
                return uiShell != null;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"VS readiness check failed: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Attempts to recover from service unavailability
        /// </summary>
        public async Task<bool> AttemptServiceRecoveryAsync()
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("Attempting VS service recovery...");
                
                // Clear cached services
                lock (_lockObject)
                {
                    _uiShell = null;
                    _textManager = null;
                    _selectionMonitor = null;
                    _runningDocumentTable = null;
                    _activityLog = null;
                    _errorList = null;
                    _statusBar = null;
                    _outputWindow = null;
                    _solution = null;
                    _isInitialized = false;
                }
                
                // Reinitialize
                await InitializeAsync();
                
                // Basic functionality test
                var isReady = await IsVSReadyAsync();
                
                if (isReady)
                {
                    System.Diagnostics.Debug.WriteLine("VS service recovery successful");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("VS service recovery failed - services still unavailable");
                }
                
                return isReady;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Service recovery failed: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Disposes the service provider
        /// </summary>
        public void Dispose()
        {
            lock (_lockObject)
            {
                _uiShell = null;
                _textManager = null;
                _selectionMonitor = null;
                _runningDocumentTable = null;
                _activityLog = null;
                _errorList = null;
                _statusBar = null;
                _outputWindow = null;
                _solution = null;
                _isInitialized = false;
            }
        }
    }
}