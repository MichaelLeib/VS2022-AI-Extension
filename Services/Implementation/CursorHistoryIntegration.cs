using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.TextManager.Interop;
using OllamaAssistant.Infrastructure;
using OllamaAssistant.Models;
using OllamaAssistant.Services.Interfaces;

namespace OllamaAssistant.Services.Implementation
{
    /// <summary>
    /// Handles integration between cursor history service and Visual Studio workspace events
    /// </summary>
    [Export(typeof(ICursorHistoryIntegration))]
    public class CursorHistoryIntegration : ICursorHistoryIntegration, IDisposable, IVsSolutionEvents
    {
        private readonly ICursorHistoryService _cursorHistoryService;
        private readonly ISettingsService _settingsService;
        private readonly ILogger _logger;
        private readonly IVSDocumentTrackingService _documentTrackingService;
        private readonly IVSOutputWindowService _outputWindowService;
        
        private IVsSolution _solution;
        private uint _solutionEventsCookie;
        private bool _disposed;
        private readonly Dictionary<IWpfTextView, CursorTracker> _activeTrackers = new Dictionary<IWpfTextView, CursorTracker>();
        private readonly object _lockObject = new object();

        [ImportingConstructor]
        public CursorHistoryIntegration(
            ICursorHistoryService cursorHistoryService,
            ISettingsService settingsService)
        {
            _cursorHistoryService = cursorHistoryService ?? throw new ArgumentNullException(nameof(cursorHistoryService));
            _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
            
            // Get services from locator
            var services = ServiceLocator.Current;
            _logger = services?.Resolve<ILogger>();
            _documentTrackingService = services?.Resolve<IVSDocumentTrackingService>();
            _outputWindowService = services?.Resolve<IVSOutputWindowService>();

            SubscribeToEvents();
        }

        private void SubscribeToEvents()
        {
            ThreadHelper.JoinableTaskFactory.RunAsync(async delegate
            {
                try
                {
                    // Subscribe to settings changes to update history depth
                    _settingsService.SettingsChanged += OnSettingsChanged;

                    // Subscribe to document tracking events
                    if (_documentTrackingService != null)
                    {
                        _documentTrackingService.ActiveDocumentChanged += OnActiveDocumentChanged;
                        _documentTrackingService.DocumentOpened += OnDocumentOpened;
                        _documentTrackingService.DocumentClosed += OnDocumentClosed;
                    }

                    // Subscribe to solution/workspace events for cleanup
                    await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                    
                    _solution = Package.GetGlobalService(typeof(SVsSolution)) as IVsSolution;
                    if (_solution != null)
                    {
                        var hr = _solution.AdviseSolutionEvents(this, out _solutionEventsCookie);
                        if (hr != Microsoft.VisualStudio.VSConstants.S_OK)
                        {
                            await _logger?.LogWarningAsync($"Failed to subscribe to solution events. HRESULT: {hr}", "CursorHistoryIntegration");
                        }
                        else
                        {
                            await _logger?.LogInfoAsync("Cursor history integration initialized successfully", "CursorHistoryIntegration");
                        }
                    }
                }
                catch (Exception ex)
                {
                    await _logger?.LogErrorAsync(ex, "Error subscribing to events", "CursorHistoryIntegration");
                }
            });
        }

        private void OnSettingsChanged(object sender, SettingsChangedEventArgs e)
        {
            ThreadHelper.JoinableTaskFactory.RunAsync(async delegate
            {
                try
                {
                    if (e.SettingName == nameof(ISettingsService.CursorHistoryMemoryDepth))
                    {
                        var newDepth = _settingsService.CursorHistoryMemoryDepth;
                        _cursorHistoryService.SetMaxHistoryDepth(newDepth);
                        await _logger?.LogInfoAsync($"Cursor history depth updated to {newDepth}", "CursorHistoryIntegration");
                    }
                }
                catch (Exception ex)
                {
                    await _logger?.LogErrorAsync(ex, "Error handling settings change", "CursorHistoryIntegration");
                }
            });
        }

        private void OnActiveDocumentChanged(object sender, DocumentChangedEventArgs e)
        {
            ThreadHelper.JoinableTaskFactory.RunAsync(async delegate
            {
                try
                {
                    // Record document switch in cursor history
                    if (e.NewDocument?.FilePath != null)
                    {
                        await RecordDocumentActivationAsync(e.NewDocument.FilePath);
                        await _logger?.LogDebugAsync($"Active document changed to: {e.NewDocument.FileName}", "CursorHistoryIntegration");
                    }
                }
                catch (Exception ex)
                {
                    await _logger?.LogErrorAsync(ex, "Error handling active document change", "CursorHistoryIntegration");
                }
            });
        }

        private void OnDocumentOpened(object sender, DocumentEventArgs e)
        {
            ThreadHelper.JoinableTaskFactory.RunAsync(async delegate
            {
                try
                {
                    if (e.Document?.FilePath != null)
                    {
                        await StartTrackingDocumentAsync(e.Document.FilePath);
                        await _logger?.LogDebugAsync($"Started tracking document: {e.Document.FileName}", "CursorHistoryIntegration");
                    }
                }
                catch (Exception ex)
                {
                    await _logger?.LogErrorAsync(ex, "Error handling document opened", "CursorHistoryIntegration");
                }
            });
        }

        private void OnDocumentClosed(object sender, DocumentEventArgs e)
        {
            ThreadHelper.JoinableTaskFactory.RunAsync(async delegate
            {
                try
                {
                    if (e.Document?.FilePath != null)
                    {
                        await StopTrackingDocumentAsync(e.Document.FilePath);
                        await _logger?.LogDebugAsync($"Stopped tracking document: {e.Document.FileName}", "CursorHistoryIntegration");
                    }
                }
                catch (Exception ex)
                {
                    await _logger?.LogErrorAsync(ex, "Error handling document closed", "CursorHistoryIntegration");
                }
            });
        }

        #region Text View Tracking

        /// <summary>
        /// Starts tracking cursor positions for a text view
        /// </summary>
        public void StartTrackingTextView(IWpfTextView textView, string filePath)
        {
            if (textView == null || string.IsNullOrEmpty(filePath))
                return;

            ThreadHelper.JoinableTaskFactory.RunAsync(async delegate
            {
                try
                {
                    lock (_lockObject)
                    {
                        if (!_activeTrackers.ContainsKey(textView))
                        {
                            var tracker = new CursorTracker(textView, filePath, _cursorHistoryService, _logger);
                            _activeTrackers[textView] = tracker;
                            
                            // Subscribe to text view close event
                            textView.Closed += (s, e) => StopTrackingTextView(textView);
                        }
                    }

                    await _logger?.LogDebugAsync($"Started tracking text view for: {System.IO.Path.GetFileName(filePath)}", "CursorHistoryIntegration");
                }
                catch (Exception ex)
                {
                    await _logger?.LogErrorAsync(ex, "Error starting text view tracking", "CursorHistoryIntegration");
                }
            });
        }

        /// <summary>
        /// Stops tracking cursor positions for a text view
        /// </summary>
        public void StopTrackingTextView(IWpfTextView textView)
        {
            if (textView == null)
                return;

            ThreadHelper.JoinableTaskFactory.RunAsync(async delegate
            {
                try
                {
                    lock (_lockObject)
                    {
                        if (_activeTrackers.TryGetValue(textView, out var tracker))
                        {
                            tracker.Dispose();
                            _activeTrackers.Remove(textView);
                        }
                    }

                    await _logger?.LogDebugAsync("Stopped tracking text view", "CursorHistoryIntegration");
                }
                catch (Exception ex)
                {
                    await _logger?.LogErrorAsync(ex, "Error stopping text view tracking", "CursorHistoryIntegration");
                }
            });
        }

        private async Task RecordDocumentActivationAsync(string filePath)
        {
            if (string.IsNullOrEmpty(filePath))
                return;

            try
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

                // Get current cursor position if available
                var textManager = Package.GetGlobalService(typeof(SVsTextManager)) as IVsTextManager;
                if (textManager?.GetActiveView(1, null, out IVsTextView activeView) == Microsoft.VisualStudio.VSConstants.S_OK)
                {
                    if (activeView.GetCaretPos(out int line, out int column) == Microsoft.VisualStudio.VSConstants.S_OK)
                    {
                        var entry = new CursorHistoryEntry
                        {
                            FilePath = filePath,
                            LineNumber = line + 1, // Convert to 1-based
                            ColumnNumber = column + 1, // Convert to 1-based
                            Timestamp = DateTime.Now,
                            Context = "Document Activation"
                        };

                        _cursorHistoryService.RecordCursorPosition(entry);
                    }
                }
            }
            catch (Exception ex)
            {
                await _logger?.LogErrorAsync(ex, "Error recording document activation", "CursorHistoryIntegration");
            }
        }

        private async Task StartTrackingDocumentAsync(string filePath)
        {
            if (string.IsNullOrEmpty(filePath) || _documentTrackingService == null)
                return;

            try
            {
                // Get text views for this file
                var textViews = _documentTrackingService.GetTextViewsForFile(filePath);
                foreach (var textView in textViews)
                {
                    StartTrackingTextView(textView, filePath);
                }
            }
            catch (Exception ex)
            {
                await _logger?.LogErrorAsync(ex, "Error starting document tracking", "CursorHistoryIntegration");
            }
        }

        private async Task StopTrackingDocumentAsync(string filePath)
        {
            if (string.IsNullOrEmpty(filePath))
                return;

            try
            {
                lock (_lockObject)
                {
                    var trackersToRemove = _activeTrackers
                        .Where(kvp => kvp.Value.FilePath.Equals(filePath, StringComparison.OrdinalIgnoreCase))
                        .ToList();

                    foreach (var kvp in trackersToRemove)
                    {
                        kvp.Value.Dispose();
                        _activeTrackers.Remove(kvp.Key);
                    }
                }
            }
            catch (Exception ex)
            {
                await _logger?.LogErrorAsync(ex, "Error stopping document tracking", "CursorHistoryIntegration");
            }
        }

        #endregion

        #region Solution Events

        /// <summary>
        /// Handles solution closing events to clear history
        /// </summary>
        public void OnSolutionClosing()
        {
            ThreadHelper.JoinableTaskFactory.RunAsync(async delegate
            {
                try
                {
                    _cursorHistoryService.ClearHistory();
                    await _outputWindowService?.WriteInfoAsync("Cursor history cleared due to solution closing", "CursorHistory");
                    await _logger?.LogInfoAsync("Cursor history cleared on solution close", "CursorHistoryIntegration");
                }
                catch (Exception ex)
                {
                    await _logger?.LogErrorAsync(ex, "Error clearing cursor history on solution close", "CursorHistoryIntegration");
                }
            });
        }

        /// <summary>
        /// Handles project unloading to clear file-specific history
        /// </summary>
        public void OnProjectUnloading(string projectPath)
        {
            ThreadHelper.JoinableTaskFactory.RunAsync(async delegate
            {
                try
                {
                    // Clear history for files in the unloaded project
                    if (!string.IsNullOrWhiteSpace(projectPath))
                    {
                        var projectDir = System.IO.Path.GetDirectoryName(projectPath);
                        var history = _cursorHistoryService.GetRecentHistory(100);
                        
                        int clearedCount = 0;
                        foreach (var entry in history)
                        {
                            if (entry.FilePath.StartsWith(projectDir, StringComparison.OrdinalIgnoreCase))
                            {
                                await _cursorHistoryService.ClearHistoryForFileAsync(entry.FilePath);
                                clearedCount++;
                            }
                        }

                        await _outputWindowService?.WriteInfoAsync($"Cleared cursor history for {clearedCount} files from project: {System.IO.Path.GetFileName(projectPath)}", "CursorHistory");
                        await _logger?.LogInfoAsync($"Cleared cursor history for {clearedCount} files from unloaded project", "CursorHistoryIntegration");
                    }
                }
                catch (Exception ex)
                {
                    await _logger?.LogErrorAsync(ex, "Error clearing project history", "CursorHistoryIntegration");
                }
            });
        }

        /// <summary>
        /// Handles file deletion/rename events
        /// </summary>
        public void OnFileDeleted(string filePath)
        {
            ThreadHelper.JoinableTaskFactory.RunAsync(async delegate
            {
                try
                {
                    await _cursorHistoryService.ClearHistoryForFileAsync(filePath);
                    await _outputWindowService?.WriteInfoAsync($"Cleared cursor history for deleted file: {System.IO.Path.GetFileName(filePath)}", "CursorHistory");
                    await _logger?.LogInfoAsync($"Cleared cursor history for deleted file: {filePath}", "CursorHistoryIntegration");
                }
                catch (Exception ex)
                {
                    await _logger?.LogErrorAsync(ex, "Error clearing deleted file history", "CursorHistoryIntegration");
                }
            });
        }

        /// <summary>
        /// Handles file rename events
        /// </summary>
        public void OnFileRenamed(string oldPath, string newPath)
        {
            ThreadHelper.JoinableTaskFactory.RunAsync(async delegate
            {
                try
                {
                    // Get existing history for the old path
                    var existingHistory = _cursorHistoryService.GetFileHistory(oldPath, 100);
                    
                    // Clear old history
                    await _cursorHistoryService.ClearHistoryForFileAsync(oldPath);
                    
                    // Re-add history with new path
                    foreach (var entry in existingHistory)
                    {
                        entry.FilePath = newPath;
                        _cursorHistoryService.RecordCursorPosition(entry);
                    }

                    await _outputWindowService?.WriteInfoAsync($"Updated cursor history for renamed file: {System.IO.Path.GetFileName(oldPath)} → {System.IO.Path.GetFileName(newPath)}", "CursorHistory");
                    await _logger?.LogInfoAsync($"Updated cursor history for file rename: {oldPath} → {newPath}", "CursorHistoryIntegration");
                }
                catch (Exception ex)
                {
                    await _logger?.LogErrorAsync(ex, "Error handling file rename in history", "CursorHistoryIntegration");
                }
            });
        }

        #endregion

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;

            ThreadHelper.JoinableTaskFactory.RunAsync(async delegate
            {
                try
                {
                    // Dispose all active trackers
                    lock (_lockObject)
                    {
                        foreach (var tracker in _activeTrackers.Values)
                        {
                            tracker.Dispose();
                        }
                        _activeTrackers.Clear();
                    }

                    // Unsubscribe from document tracking events
                    if (_documentTrackingService != null)
                    {
                        _documentTrackingService.ActiveDocumentChanged -= OnActiveDocumentChanged;
                        _documentTrackingService.DocumentOpened -= OnDocumentOpened;
                        _documentTrackingService.DocumentClosed -= OnDocumentClosed;
                    }

                    // Unsubscribe from solution events
                    if (_solution != null && _solutionEventsCookie != 0)
                    {
                        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                        _solution.UnadviseSolutionEvents(_solutionEventsCookie);
                        _solutionEventsCookie = 0;
                    }

                    // Unsubscribe from settings events
                    if (_settingsService != null)
                    {
                        _settingsService.SettingsChanged -= OnSettingsChanged;
                    }

                    await _logger?.LogInfoAsync("Cursor history integration disposed", "CursorHistoryIntegration");
                }
                catch (Exception ex)
                {
                    await _logger?.LogErrorAsync(ex, "Error disposing CursorHistoryIntegration", "CursorHistoryIntegration");
                }
            });
        }

        #region IVsSolutionEvents Implementation

        public int OnAfterOpenProject(IVsHierarchy pHierarchy, int fAdded)
        {
            return Microsoft.VisualStudio.VSConstants.S_OK;
        }

        public int OnQueryCloseProject(IVsHierarchy pHierarchy, int fRemoving, ref int pfCancel)
        {
            pfCancel = 0; // Allow closing
            return Microsoft.VisualStudio.VSConstants.S_OK;
        }

        public int OnBeforeCloseProject(IVsHierarchy pHierarchy, int fRemoved)
        {
            try
            {
                // Get project path and clear related history
                if (pHierarchy != null)
                {
                    if (pHierarchy.GetProperty((uint)Microsoft.VisualStudio.VSConstants.VSITEMID.VSITEMID_ROOT, 
                        (int)__VSHPROPID.VSHPROPID_ProjectDir, out object projectDir) == Microsoft.VisualStudio.VSConstants.S_OK)
                    {
                        OnProjectUnloading(projectDir as string);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in OnBeforeCloseProject: {ex.Message}");
            }
            
            return Microsoft.VisualStudio.VSConstants.S_OK;
        }

        public int OnAfterLoadProject(IVsHierarchy pStubHierarchy, IVsHierarchy pRealHierarchy)
        {
            return Microsoft.VisualStudio.VSConstants.S_OK;
        }

        public int OnQueryUnloadProject(IVsHierarchy pRealHierarchy, ref int pfCancel)
        {
            pfCancel = 0; // Allow unloading
            return Microsoft.VisualStudio.VSConstants.S_OK;
        }

        public int OnBeforeUnloadProject(IVsHierarchy pRealHierarchy, IVsHierarchy pStubHierarchy)
        {
            return Microsoft.VisualStudio.VSConstants.S_OK;
        }

        public int OnAfterOpenSolution(object pUnkReserved, int fNewSolution)
        {
            return Microsoft.VisualStudio.VSConstants.S_OK;
        }

        public int OnQueryCloseSolution(object pUnkReserved, ref int pfCancel)
        {
            pfCancel = 0; // Allow closing
            return Microsoft.VisualStudio.VSConstants.S_OK;
        }

        public int OnBeforeCloseSolution(object pUnkReserved)
        {
            try
            {
                OnSolutionClosing();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in OnBeforeCloseSolution: {ex.Message}");
            }
            
            return Microsoft.VisualStudio.VSConstants.S_OK;
        }

        public int OnAfterCloseSolution(object pUnkReserved)
        {
            return Microsoft.VisualStudio.VSConstants.S_OK;
        }

        #endregion
    }
}