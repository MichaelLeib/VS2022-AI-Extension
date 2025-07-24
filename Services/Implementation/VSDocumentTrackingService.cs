using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.TextManager.Interop;
using OllamaAssistant.Infrastructure;
using OllamaAssistant.Services.Interfaces;

namespace OllamaAssistant.Services.Implementation
{
    /// <summary>
    /// Service for tracking documents and text views in Visual Studio
    /// </summary>
    [Export(typeof(IVSDocumentTrackingService))]
    public class VSDocumentTrackingService : IVSDocumentTrackingService, IVsRunningDocTableEvents, IVsSelectionEvents
    {
        private readonly Dictionary<uint, DocumentInfo> _documents = new Dictionary<uint, DocumentInfo>();
        private readonly Dictionary<string, List<IWpfTextView>> _fileToTextViews = new Dictionary<string, List<IWpfTextView>>();
        private readonly object _lockObject = new object();
        
        private IVsRunningDocumentTable _runningDocumentTable;
        private IVsMonitorSelection _selectionMonitor;
        private uint _rdtCookie;
        private uint _selectionCookie;
        private bool _isInitialized;
        
        private readonly ILogger _logger;
        private readonly ICursorHistoryService _cursorHistoryService;

        [ImportingConstructor]
        public VSDocumentTrackingService()
        {
            var services = ServiceLocator.Current;
            _logger = services?.Resolve<ILogger>();
            _cursorHistoryService = services?.Resolve<ICursorHistoryService>();
        }

        /// <summary>
        /// Gets the currently active document
        /// </summary>
        public DocumentInfo ActiveDocument { get; private set; }

        /// <summary>
        /// Event raised when the active document changes
        /// </summary>
        public event EventHandler<DocumentChangedEventArgs> ActiveDocumentChanged;

        /// <summary>
        /// Event raised when a document is opened
        /// </summary>
        public event EventHandler<DocumentEventArgs> DocumentOpened;

        /// <summary>
        /// Event raised when a document is closed
        /// </summary>
        public event EventHandler<DocumentEventArgs> DocumentClosed;

        /// <summary>
        /// Event raised when a document is saved
        /// </summary>
        public event EventHandler<DocumentEventArgs> DocumentSaved;

        /// <summary>
        /// Initializes the document tracking service
        /// </summary>
        public async Task InitializeAsync()
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            try
            {
                if (_isInitialized)
                    return;

                // Get running document table
                _runningDocumentTable = ServiceProvider.GlobalProvider.GetService(typeof(SVsRunningDocumentTable)) as IVsRunningDocumentTable;
                if (_runningDocumentTable != null)
                {
                    _runningDocumentTable.AdviseRunningDocTableEvents(this, out _rdtCookie);
                }

                // Get selection monitor
                _selectionMonitor = ServiceProvider.GlobalProvider.GetService(typeof(SVsShellMonitorSelection)) as IVsMonitorSelection;
                if (_selectionMonitor != null)
                {
                    _selectionMonitor.AdviseSelectionEvents(this, out _selectionCookie);
                }

                // Load currently open documents
                await LoadOpenDocumentsAsync();

                _isInitialized = true;
                await _logger?.LogInfoAsync("Document tracking service initialized", "DocumentTracking");
            }
            catch (Exception ex)
            {
                await _logger?.LogErrorAsync(ex, "Failed to initialize document tracking service", "DocumentTracking");
            }
        }

        /// <summary>
        /// Gets all currently tracked documents
        /// </summary>
        public IEnumerable<DocumentInfo> GetAllDocuments()
        {
            lock (_lockObject)
            {
                return _documents.Values.ToList();
            }
        }

        /// <summary>
        /// Gets a document by file path
        /// </summary>
        public DocumentInfo GetDocument(string filePath)
        {
            if (string.IsNullOrEmpty(filePath))
                return null;

            lock (_lockObject)
            {
                return _documents.Values.FirstOrDefault(d => 
                    string.Equals(d.FilePath, filePath, StringComparison.OrdinalIgnoreCase));
            }
        }

        /// <summary>
        /// Gets text views for a specific file
        /// </summary>
        public IEnumerable<IWpfTextView> GetTextViewsForFile(string filePath)
        {
            if (string.IsNullOrEmpty(filePath))
                return Enumerable.Empty<IWpfTextView>();

            lock (_lockObject)
            {
                return _fileToTextViews.TryGetValue(filePath, out var views) 
                    ? views.ToList() 
                    : Enumerable.Empty<IWpfTextView>();
            }
        }

        /// <summary>
        /// Registers a text view for tracking
        /// </summary>
        public void RegisterTextView(IWpfTextView textView, string filePath)
        {
            if (textView == null || string.IsNullOrEmpty(filePath))
                return;

            lock (_lockObject)
            {
                if (!_fileToTextViews.ContainsKey(filePath))
                {
                    _fileToTextViews[filePath] = new List<IWpfTextView>();
                }

                if (!_fileToTextViews[filePath].Contains(textView))
                {
                    _fileToTextViews[filePath].Add(textView);
                    
                    // Subscribe to text view events
                    textView.Closed += (s, e) => UnregisterTextView(textView, filePath);
                }
            }
        }

        /// <summary>
        /// Unregisters a text view from tracking
        /// </summary>
        public void UnregisterTextView(IWpfTextView textView, string filePath)
        {
            if (textView == null || string.IsNullOrEmpty(filePath))
                return;

            lock (_lockObject)
            {
                if (_fileToTextViews.TryGetValue(filePath, out var views))
                {
                    views.Remove(textView);
                    if (views.Count == 0)
                    {
                        _fileToTextViews.Remove(filePath);
                    }
                }
            }
        }

        #region IVsRunningDocTableEvents Implementation

        public int OnAfterFirstDocumentLock(uint docCookie, uint dwRDTLockType, uint dwReadLocksRemaining, uint dwEditLocksRemaining)
        {
            ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
            {
                try
                {
                    var docInfo = await GetDocumentInfoAsync(docCookie);
                    if (docInfo != null)
                    {
                        lock (_lockObject)
                        {
                            _documents[docCookie] = docInfo;
                        }

                        DocumentOpened?.Invoke(this, new DocumentEventArgs(docInfo));
                        await _logger?.LogDebugAsync($"Document opened: {docInfo.FilePath}", "DocumentTracking");
                    }
                }
                catch (Exception ex)
                {
                    await _logger?.LogErrorAsync(ex, "Error handling document open", "DocumentTracking");
                }
            });

            return VSConstants.S_OK;
        }

        public int OnBeforeLastDocumentUnlock(uint docCookie, uint dwRDTLockType, uint dwReadLocksRemaining, uint dwEditLocksRemaining)
        {
            ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
            {
                try
                {
                    lock (_lockObject)
                    {
                        if (_documents.TryGetValue(docCookie, out var docInfo))
                        {
                            _documents.Remove(docCookie);
                            
                            // Clean up text views
                            if (_fileToTextViews.ContainsKey(docInfo.FilePath))
                            {
                                _fileToTextViews.Remove(docInfo.FilePath);
                            }

                            // Clear cursor history for this file
                            _cursorHistoryService?.ClearHistoryForFileAsync(docInfo.FilePath);

                            DocumentClosed?.Invoke(this, new DocumentEventArgs(docInfo));
                            await _logger?.LogDebugAsync($"Document closed: {docInfo.FilePath}", "DocumentTracking");
                        }
                    }
                }
                catch (Exception ex)
                {
                    await _logger?.LogErrorAsync(ex, "Error handling document close", "DocumentTracking");
                }
            });

            return VSConstants.S_OK;
        }

        public int OnAfterSave(uint docCookie)
        {
            ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
            {
                try
                {
                    lock (_lockObject)
                    {
                        if (_documents.TryGetValue(docCookie, out var docInfo))
                        {
                            docInfo.LastSaved = DateTime.Now;
                            docInfo.IsDirty = false;

                            DocumentSaved?.Invoke(this, new DocumentEventArgs(docInfo));
                            await _logger?.LogDebugAsync($"Document saved: {docInfo.FilePath}", "DocumentTracking");
                        }
                    }
                }
                catch (Exception ex)
                {
                    await _logger?.LogErrorAsync(ex, "Error handling document save", "DocumentTracking");
                }
            });

            return VSConstants.S_OK;
        }

        public int OnAfterAttributeChange(uint docCookie, uint grfAttribs)
        {
            // Handle attribute changes like dirty state
            ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
            {
                try
                {
                    lock (_lockObject)
                    {
                        if (_documents.TryGetValue(docCookie, out var docInfo))
                        {
                            if ((grfAttribs & (uint)__VSRDTATTRIB.RDTA_DocDataIsDirty) != 0)
                            {
                                docInfo.IsDirty = true;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    await _logger?.LogErrorAsync(ex, "Error handling document attribute change", "DocumentTracking");
                }
            });

            return VSConstants.S_OK;
        }

        public int OnBeforeDocumentWindowShow(uint docCookie, int fFirstShow, IVsWindowFrame pFrame)
        {
            return VSConstants.S_OK;
        }

        public int OnAfterDocumentWindowHide(uint docCookie, IVsWindowFrame pFrame)
        {
            return VSConstants.S_OK;
        }

        #endregion

        #region IVsSelectionEvents Implementation

        public int OnSelectionChanged(IVsHierarchy pHierOld, uint itemidOld, IVsMultiItemSelect pMISOld, ISelectionContainer pSCOld, IVsHierarchy pHierNew, uint itemidNew, IVsMultiItemSelect pMISNew, ISelectionContainer pSCNew)
        {
            ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
            {
                try
                {
                    // Detect active document changes
                    var newActiveDoc = await GetActiveDocumentAsync();
                    if (newActiveDoc?.FilePath != ActiveDocument?.FilePath)
                    {
                        var oldDoc = ActiveDocument;
                        ActiveDocument = newActiveDoc;
                        
                        ActiveDocumentChanged?.Invoke(this, new DocumentChangedEventArgs(oldDoc, newActiveDoc));
                        await _logger?.LogDebugAsync($"Active document changed to: {newActiveDoc?.FilePath ?? "None"}", "DocumentTracking");
                    }
                }
                catch (Exception ex)
                {
                    await _logger?.LogErrorAsync(ex, "Error handling selection change", "DocumentTracking");
                }
            });

            return VSConstants.S_OK;
        }

        public int OnElementValueChanged(uint elementid, object varValueOld, object varValueNew)
        {
            return VSConstants.S_OK;
        }

        public int OnCmdUIContextChanged(uint dwCmdUICookie, int fActive)
        {
            return VSConstants.S_OK;
        }

        #endregion

        #region Private Methods

        private async Task LoadOpenDocumentsAsync()
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            try
            {
                if (_runningDocumentTable == null)
                    return;

                IEnumRunningDocuments enumDocs;
                _runningDocumentTable.GetRunningDocumentsEnum(out enumDocs);

                uint[] cookies = new uint[1];
                uint fetched;

                while (enumDocs.Next(1, cookies, out fetched) == VSConstants.S_OK && fetched > 0)
                {
                    var docInfo = await GetDocumentInfoAsync(cookies[0]);
                    if (docInfo != null)
                    {
                        lock (_lockObject)
                        {
                            _documents[cookies[0]] = docInfo;
                        }
                    }
                }

                // Set active document
                ActiveDocument = await GetActiveDocumentAsync();
            }
            catch (Exception ex)
            {
                await _logger?.LogErrorAsync(ex, "Error loading open documents", "DocumentTracking");
            }
        }

        private async Task<DocumentInfo> GetDocumentInfoAsync(uint docCookie)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            try
            {
                uint flags, readLocks, editLocks;
                string filePath;
                IVsHierarchy hierarchy;
                uint itemid;
                IntPtr docData;

                var hr = _runningDocumentTable.GetDocumentInfo(
                    docCookie, out flags, out readLocks, out editLocks,
                    out filePath, out hierarchy, out itemid, out docData);

                if (hr == VSConstants.S_OK && !string.IsNullOrEmpty(filePath))
                {
                    return new DocumentInfo
                    {
                        Cookie = docCookie,
                        FilePath = filePath,
                        Hierarchy = hierarchy,
                        ItemId = itemid,
                        IsDirty = (flags & (uint)_VSRDTFLAGS.RDT_DontAddToMRU) != 0,
                        LastAccessed = DateTime.Now
                    };
                }
            }
            catch (Exception ex)
            {
                await _logger?.LogErrorAsync(ex, "Error getting document info", "DocumentTracking");
            }

            return null;
        }

        private async Task<DocumentInfo> GetActiveDocumentAsync()
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            try
            {
                var textManager = ServiceProvider.GlobalProvider.GetService(typeof(SVsTextManager)) as IVsTextManager;
                if (textManager?.GetActiveView(1, null, out IVsTextView activeView) == VSConstants.S_OK)
                {
                    if (activeView.GetBuffer(out IVsTextLines buffer) == VSConstants.S_OK)
                    {
                        var userData = buffer as IVsUserData;
                        if (userData != null)
                        {
                            var guidIVsTextBuffer = typeof(IVsTextBuffer).GUID;
                            userData.GetData(ref guidIVSTextBuffer, out object textBufferObj);
                            
                            if (textBufferObj is IVsTextBuffer textBuffer)
                            {
                                // Find the document for this buffer
                                lock (_lockObject)
                                {
                                    return _documents.Values.FirstOrDefault(d => 
                                        d.FilePath != null && 
                                        activeView.GetBuffer(out IVsTextLines activeBuffer) == VSConstants.S_OK &&
                                        activeBuffer == buffer);
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                await _logger?.LogErrorAsync(ex, "Error getting active document", "DocumentTracking");
            }

            return null;
        }

        #endregion

        /// <summary>
        /// Disposes the service
        /// </summary>
        public void Dispose()
        {
            try
            {
                if (_isInitialized)
                {
                    ThreadHelper.JoinableTaskFactory.Run(async () =>
                    {
                        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

                        if (_runningDocumentTable != null && _rdtCookie != 0)
                        {
                            _runningDocumentTable.UnadviseRunningDocTableEvents(_rdtCookie);
                        }

                        if (_selectionMonitor != null && _selectionCookie != 0)
                        {
                            _selectionMonitor.UnadviseSelectionEvents(_selectionCookie);
                        }
                    });
                }

                lock (_lockObject)
                {
                    _documents.Clear();
                    _fileToTextViews.Clear();
                }

                _isInitialized = false;
            }
            catch
            {
                // Ignore disposal errors
            }
        }
    }
}