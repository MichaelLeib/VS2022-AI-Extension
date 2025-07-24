using System;
using System.ComponentModel.Composition;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.TextManager.Interop;
using Microsoft.VisualStudio.Utilities;
using OllamaAssistant.Services.Interfaces;
using OllamaAssistant.Models;

namespace OllamaAssistant.Services.Implementation
{
    /// <summary>
    /// Implementation of text view service for Visual Studio editor integration
    /// </summary>
    [Export(typeof(ITextViewService))]
    [ContentType("text")]
    [TextViewRole(PredefinedTextViewRoles.Editable)]
    public class TextViewService : ITextViewService
    {
        private IWpfTextView _activeTextView;
        private readonly object _lockObject = new object();

        #region MEF Imports

        [Import]
        internal ITextEditorFactoryService TextEditorFactory { get; set; }

        [Import]
        internal ITextBufferFactoryService BufferFactory { get; set; }

        [Import]
        internal IContentTypeRegistryService ContentTypeRegistry { get; set; }

        #endregion

        #region Events

        public event EventHandler<TextChangedEventArgs> TextChanged;
        public event EventHandler<Microsoft.VisualStudio.Text.Editor.CaretPositionChangedEventArgs> CaretPositionChanged;

        private event EventHandler<CaretPositionChangedEventArgs> _customCaretPositionChanged;
        event EventHandler<CaretPositionChangedEventArgs> ITextViewService.CaretPositionChanged
        {
            add { _customCaretPositionChanged += value; }
            remove { _customCaretPositionChanged -= value; }
        }

        #endregion

        #region Public Methods

        public async Task<string> GetSurroundingContextAsync(int linesUp, int linesDown)
        {
            try
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

                var textView = await GetActiveTextViewWithRetryAsync();
                if (textView == null)
                    return string.Empty;

                var caretPosition = textView.Caret.Position.BufferPosition;
                var currentLine = caretPosition.GetContainingLine();
                var snapshot = textView.TextSnapshot;

                var startLine = Math.Max(0, currentLine.LineNumber - linesUp);
                var endLine = Math.Min(snapshot.LineCount - 1, currentLine.LineNumber + linesDown);

                var contextBuilder = new StringBuilder();

                for (int lineNumber = startLine; lineNumber <= endLine; lineNumber++)
                {
                    var line = snapshot.GetLineFromLineNumber(lineNumber);
                    var lineText = line.GetText();

                    // Mark the current line for context
                    if (lineNumber == currentLine.LineNumber)
                    {
                        var caretColumn = caretPosition.Position - line.Start.Position;
                        contextBuilder.AppendLine($"[CURSOR@{caretColumn}]{lineText}");
                    }
                    else
                    {
                        contextBuilder.AppendLine(lineText);
                    }
                }

                return contextBuilder.ToString();
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("text view") || ex.Message.Contains("buffer"))
            {
                System.Diagnostics.Debug.WriteLine($"Text view unavailable when getting context: {ex.Message}");
                return "[TEXT VIEW UNAVAILABLE]";
            }
            catch (COMException ex)
            {
                System.Diagnostics.Debug.WriteLine($"COM error getting surrounding context: {ex.Message}");
                return "[EDITOR SERVICE UNAVAILABLE]";
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error getting surrounding context: {ex.Message}");
                return string.Empty;
            }
        }

        public async Task InsertTextAsync(string text, int position)
        {
            if (string.IsNullOrEmpty(text))
                return;

            try
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

                var textView = await GetActiveTextViewWithRetryAsync();
                if (textView == null)
                {
                    System.Diagnostics.Debug.WriteLine("Cannot insert text - no active text view available");
                    return;
                }

                var textBuffer = textView.TextBuffer;
                if (textBuffer == null)
                {
                    System.Diagnostics.Debug.WriteLine("Cannot insert text - text buffer is null");
                    return;
                }

                var snapshot = textBuffer.CurrentSnapshot;

                // Validate position
                if (position < 0 || position > snapshot.Length)
                {
                    System.Diagnostics.Debug.WriteLine($"Invalid insertion position: {position}, buffer length: {snapshot.Length}");
                    return;
                }

                // Attempt to create edit transaction with retry
                ITextEdit edit = null;
                for (int attempt = 0; attempt < 3; attempt++)
                {
                    try
                    {
                        edit = textBuffer.CreateEdit();
                        break;
                    }
                    catch (InvalidOperationException ex) when (attempt < 2)
                    {
                        System.Diagnostics.Debug.WriteLine($"Failed to create edit (attempt {attempt + 1}): {ex.Message}");
                        await Task.Delay(50); // Brief delay before retry
                    }
                }

                if (edit == null)
                {
                    System.Diagnostics.Debug.WriteLine("Failed to create text edit after 3 attempts");
                    return;
                }

                using (edit)
                {
                    if (!edit.Insert(position, text))
                    {
                        System.Diagnostics.Debug.WriteLine("Failed to insert text into edit transaction");
                        return;
                    }

                    var result = edit.Apply();
                    if (result == null)
                    {
                        System.Diagnostics.Debug.WriteLine("Failed to apply text edit");
                        return;
                    }
                }

                // Move caret to end of inserted text (with error handling)
                try
                {
                    var newSnapshot = textBuffer.CurrentSnapshot;
                    var newPosition = Math.Min(position + text.Length, newSnapshot.Length);
                    var newCaretPosition = new SnapshotPoint(newSnapshot, newPosition);
                    textView.Caret.MoveTo(newCaretPosition);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to move caret after text insertion: {ex.Message}");
                    // Text was inserted successfully, caret movement is optional
                }
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("text view") || ex.Message.Contains("buffer"))
            {
                System.Diagnostics.Debug.WriteLine($"Text view/buffer unavailable during text insertion: {ex.Message}");
            }
            catch (COMException ex)
            {
                System.Diagnostics.Debug.WriteLine($"COM error during text insertion: {ex.Message}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error inserting text: {ex.Message}");
            }
        }

        public SnapshotPoint GetCaretPosition()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            var textView = GetActiveTextView();
            return textView?.Caret.Position.BufferPosition ?? default;
        }

        public IWpfTextView GetActiveTextView()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            lock (_lockObject)
            {
                if (_activeTextView != null && !_activeTextView.IsClosed)
                    return _activeTextView;

                // Try to get the active text view from VS
                _activeTextView = GetCurrentTextViewSafe();
                return _activeTextView;
            }
        }

        /// <summary>
        /// Gets the active text view with retry logic for better reliability
        /// </summary>
        private async Task<IWpfTextView> GetActiveTextViewWithRetryAsync()
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            for (int attempt = 0; attempt < 3; attempt++)
            {
                var textView = GetActiveTextView();
                if (textView != null)
                    return textView;

                if (attempt < 2)
                {
                    // Brief delay before retry
                    await Task.Delay(50);
                    
                    // Force refresh of cached view
                    lock (_lockObject)
                    {
                        _activeTextView = null;
                    }
                }
            }

            System.Diagnostics.Debug.WriteLine("Failed to get active text view after 3 attempts");
            return null;
        }

        public string GetCurrentFilePath()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            return GetCurrentFilePathSafe();
        }

        private string GetCurrentFilePathSafe()
        {
            try
            {
                var textView = GetActiveTextView();
                if (textView == null)
                    return null;

                if (textView.TextBuffer?.Properties.TryGetProperty(typeof(ITextDocument), out ITextDocument document) == true)
                {
                    return document?.FilePath;
                }
                return null;
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("buffer") || ex.Message.Contains("property"))
            {
                System.Diagnostics.Debug.WriteLine($"Buffer/property error getting file path: {ex.Message}");
                return null;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error getting current file path: {ex.Message}");
                return null;
            }
        }

        public string GetCurrentLanguage()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            var textView = GetActiveTextView();
            if (textView == null)
                return "text";

            try
            {
                var contentType = textView.TextBuffer.ContentType;
                var typeName = contentType.TypeName.ToLowerInvariant();

                // Map VS content types to language names
                switch (typeName)
                {
                    case "csharp":
                        return "csharp";
                    case "c/c++":
                        return "cpp";
                    case "javascript":
                        return "javascript";
                    case "typescript":
                        return "typescript";
                    case "python":
                        return "python";
                    case "java":
                        return "java";
                    case "xml":
                        return "xml";
                    case "json":
                        return "json";
                    case "html":
                        return "html";
                    case "css":
                        return "css";
                    case "sql":
                        return "sql";
                    default:
                        return DetectLanguageFromFilePath(GetCurrentFilePath() ?? "text");
                };
            }
            catch
            {
                return DetectLanguageFromFilePath(GetCurrentFilePath()) ?? "text";
            }
        }

        public async Task MoveCaretToAsync(int line, int column)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            var textView = GetActiveTextView();
            if (textView == null)
                return;

            try
            {
                var snapshot = textView.TextSnapshot;
                
                // Convert to 0-based line number
                var lineNumber = Math.Max(0, Math.Min(snapshot.LineCount - 1, line - 1));
                var targetLine = snapshot.GetLineFromLineNumber(lineNumber);
                
                // Clamp column to line length
                var targetColumn = Math.Max(0, Math.Min(targetLine.Length, column));
                var targetPosition = targetLine.Start.Position + targetColumn;

                var snapshotPoint = new SnapshotPoint(snapshot, targetPosition);
                textView.Caret.MoveTo(snapshotPoint);
                
                var snapShotSpan = new SnapshotSpan(snapshot, snapshotPoint, 0);

                // Ensure the caret is visible
                textView.ViewScroller.EnsureSpanVisible(snapShotSpan);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error moving caret: {ex.Message}");
            }
        }

        public int GetCurrentLineNumber()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            var textView = GetActiveTextView();
            if (textView == null)
                return 0;

            try
            {
                var caretPosition = textView.Caret.Position.BufferPosition;
                return caretPosition.GetContainingLine().LineNumber + 1; // Convert to 1-based
            }
            catch
            {
                return 0;
            }
        }

        public int GetCurrentColumn()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            var textView = GetActiveTextView();
            if (textView == null)
                return 0;

            try
            {
                var caretPosition = textView.Caret.Position.BufferPosition;
                var line = caretPosition.GetContainingLine();
                return caretPosition.Position - line.Start.Position;
            }
            catch
            {
                return 0;
            }
        }

        public Models.CursorPosition GetCurrentPosition()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            var textView = GetActiveTextView();
            if (textView == null)
                return null;

            try
            {
                var caretPosition = textView.Caret.Position.BufferPosition;
                var line = caretPosition.GetContainingLine();

                return new OllamaAssistant.Models.CursorPosition
                {
                    Line = line.LineNumber + 1, // Convert to 1-based
                    Column = caretPosition.Position - line.Start.Position, // Keep 0-based
                    FilePath = GetCurrentFilePath(),
                    Timestamp = DateTime.UtcNow
                };
            }
            catch
            {
                return null;
            }
        }

        #endregion

        #region Event Management

        public void SetActiveTextView(IWpfTextView textView)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            lock (_lockObject)
            {
                if (_activeTextView == textView)
                    return;

                // Unsubscribe from old view
                if (_activeTextView != null)
                {
                    UnsubscribeFromTextView(_activeTextView);
                }

                _activeTextView = textView;

                // Subscribe to new view
                if (_activeTextView != null)
                {
                    SubscribeToTextView(_activeTextView);
                }
            }
        }

        private void SubscribeToTextView(IWpfTextView textView)
        {
            if (textView == null)
                return;

            textView.TextBuffer.Changed += OnTextBufferChanged;
            textView.Caret.PositionChanged += OnCaretPositionChanged;
            textView.Closed += OnTextViewClosed;
        }

        private void UnsubscribeFromTextView(IWpfTextView textView)
        {
            if (textView == null)
                return;

            textView.TextBuffer.Changed -= OnTextBufferChanged;
            textView.Caret.PositionChanged -= OnCaretPositionChanged;
            textView.Closed -= OnTextViewClosed;
        }

        private void OnTextBufferChanged(object sender, TextContentChangedEventArgs e)
        {
            try
            {
                var textBuffer = sender as ITextBuffer;
                if (textBuffer == null)
                {
                    System.Diagnostics.Debug.WriteLine("Text buffer is null in OnTextBufferChanged");
                    return;
                }

                var filePath = GetFilePathFromTextBuffer(textBuffer);

                foreach (var change in e.Changes)
                {
                    try
                    {
                        TextChanged?.Invoke(this, new TextChangedEventArgs
                        {
                            Before = e.Before,
                            After = e.After,
                            Change = change,
                            FilePath = filePath
                        });
                    }
                    catch (Exception innerEx)
                    {
                        System.Diagnostics.Debug.WriteLine($"Error invoking TextChanged event: {innerEx.Message}");
                    }
                }
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("buffer") || ex.Message.Contains("snapshot"))
            {
                System.Diagnostics.Debug.WriteLine($"Buffer/snapshot error in OnTextBufferChanged: {ex.Message}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in OnTextBufferChanged: {ex.Message}");
            }
        }

        private void OnCaretPositionChanged(object sender, Microsoft.VisualStudio.Text.Editor.CaretPositionChangedEventArgs e)
        {
            try
            {
                // Validate event arguments
                if (e?.NewPosition == null || e?.OldPosition == null)
                {
                    System.Diagnostics.Debug.WriteLine("Invalid caret position event arguments");
                    return;
                }

                var textView = GetActiveTextView();
                var filePath = GetCurrentFilePathSafe();

                try
                {
                    _customCaretPositionChanged?.Invoke(this, new CaretPositionChangedEventArgs
                    {
                        OldPosition = e.OldPosition.BufferPosition,
                        NewPosition = e.NewPosition.BufferPosition,
                        FilePath = filePath,
                        IsUserInitiated = true // Simplified - could be enhanced to detect programmatic moves
                    });
                }
                catch (Exception innerEx)
                {
                    System.Diagnostics.Debug.WriteLine($"Error invoking CaretPositionChanged event: {innerEx.Message}");
                }
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("buffer") || ex.Message.Contains("position"))
            {
                System.Diagnostics.Debug.WriteLine($"Buffer/position error in OnCaretPositionChanged: {ex.Message}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in OnCaretPositionChanged: {ex.Message}");
            }
        }

        private void OnTextViewClosed(object sender, EventArgs e)
        {
            var textView = sender as IWpfTextView;
            if (textView == _activeTextView)
            {
                lock (_lockObject)
                {
                    UnsubscribeFromTextView(_activeTextView);
                    _activeTextView = null;
                }
            }
        }

        #endregion

        #region Private Helpers

        private IWpfTextView GetCurrentTextViewSafe()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            try
            {
                // Get the current text view from Visual Studio with enhanced error handling
                var textManager = Package.GetGlobalService(typeof(SVsTextManager)) as IVsTextManager;
                if (textManager == null)
                {
                    System.Diagnostics.Debug.WriteLine("Text manager service is not available");
                    return null;
                }

                var result = textManager.GetActiveView(1, null, out IVsTextView vsTextView);
                if (result != Microsoft.VisualStudio.VSConstants.S_OK)
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to get active view, HRESULT: {result}");
                    return null;
                }

                if (vsTextView == null)
                {
                    System.Diagnostics.Debug.WriteLine("Active view is null");
                    return null;
                }

                if (vsTextView is IVsUserData userData)
                {
                    var guidViewHost = new Guid("8C40265E-9FDB-4f54-A0FD-EBB72B7D0476");
                    var dataResult = userData.GetData(ref guidViewHost, out object viewHost);
                    
                    if (dataResult != Microsoft.VisualStudio.VSConstants.S_OK)
                    {
                        System.Diagnostics.Debug.WriteLine($"Failed to get view host data, HRESULT: {dataResult}");
                        return null;
                    }

                    if (viewHost is IWpfTextViewHost textViewHost)
                    {
                        var textView = textViewHost.TextView;
                        if (textView?.IsClosed == true)
                        {
                            System.Diagnostics.Debug.WriteLine("Retrieved text view is closed");
                            return null;
                        }
                        return textView;
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine($"View host is not IWpfTextViewHost: {viewHost?.GetType().Name ?? "null"}");
                    }
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"VsTextView is not IVsUserData: {vsTextView.GetType().Name}");
                }
            }
            catch (COMException ex)
            {
                System.Diagnostics.Debug.WriteLine($"COM error getting current text view: {ex.Message} (HRESULT: 0x{ex.HResult:X8})");
            }
            catch (InvalidOperationException ex)
            {
                System.Diagnostics.Debug.WriteLine($"Invalid operation getting current text view: {ex.Message}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Unexpected error getting current text view: {ex.Message}");
            }

            return null;
        }

        private string GetFilePathFromTextBuffer(ITextBuffer textBuffer)
        {
            try
            {
                if (textBuffer?.Properties.TryGetProperty(typeof(ITextDocument), out ITextDocument document) == true)
                {
                    return document.FilePath;
                }
                return null;
            }
            catch
            {
                return null;
            }
        }

        private string DetectLanguageFromFilePath(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                return null;

            var extension = Path.GetExtension(filePath).ToLowerInvariant();
            switch (extension)
            {
                case ".cs":
                    return "csharp";
                case ".cpp":
                case ".cc":
                case ".cxx":
                    return "cpp";
                case ".c":
                    return "c";
                case ".h":
                case ".hpp":
                    return "c_header";
                case ".js":
                    return "javascript";
                case ".ts":
                    return "typescript";
                case ".py":
                    return "python";
                case ".java":
                    return "java";
                case ".php":
                    return "php";
                case ".rb":
                    return "ruby";
                case ".go":
                    return "go";
                case ".rs":
                    return "rust";
                case ".swift":
                    return "swift";
                case ".kt":
                    return "kotlin";
                case ".scala":
                    return "scala";
                case ".vb":
                    return "vbnet";
                case ".fs":
                    return "fsharp";
                case ".xml":
                case ".xaml":
                    return "xml";
                case ".json":
                    return "json";
                case ".yaml":
                case ".yml":
                    return "yaml";
                case ".html":
                case ".htm":
                    return "html";
                case ".css":
                    return "css";
                case ".scss":
                case ".sass":
                    return "scss";
                case ".sql":
                    return "sql";
                case ".ps1":
                    return "powershell";
                case ".sh":
                    return "bash";
                case ".bat":
                case ".cmd":
                    return "batch";
                default:
                    return null;
            };
        }

        #endregion
    }
}