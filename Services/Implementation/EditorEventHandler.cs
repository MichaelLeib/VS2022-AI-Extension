using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Utilities;
using OllamaAssistant.Models;
using OllamaAssistant.Services.Interfaces;

namespace OllamaAssistant.Services.Implementation
{
    /// <summary>
    /// Handles editor events and integrates with cursor history tracking
    /// </summary>
    [Export(typeof(IWpfTextViewCreationListener))]
    [ContentType("text")]
    [TextViewRole(PredefinedTextViewRoles.Editable)]
    internal class EditorEventHandler : IWpfTextViewCreationListener
    {
        private readonly Dictionary<IWpfTextView, EditorEventSubscription> _subscriptions;
        private ICursorHistoryService _cursorHistoryService;
        private ISettingsService _settingsService;

        public EditorEventHandler()
        {
            _subscriptions = new Dictionary<IWpfTextView, EditorEventSubscription>();
        }

        public void SetServices(ICursorHistoryService cursorHistoryService, ISettingsService settingsService)
        {
            _cursorHistoryService = cursorHistoryService;
            _settingsService = settingsService;
        }

        public void TextViewCreated(IWpfTextView textView)
        {
            if (_cursorHistoryService == null || _settingsService == null)
                return;

            var subscription = new EditorEventSubscription(textView, _cursorHistoryService, _settingsService);
            _subscriptions[textView] = subscription;

            textView.Closed += (sender, e) =>
            {
                if (_subscriptions.TryGetValue(textView, out var sub))
                {
                    sub.Dispose();
                    _subscriptions.Remove(textView);
                }
            };
        }
    }

    /// <summary>
    /// Manages event subscriptions for a single text view
    /// </summary>
    internal class EditorEventSubscription : IDisposable
    {
        private readonly IWpfTextView _textView;
        private readonly ICursorHistoryService _cursorHistoryService;
        private readonly ISettingsService _settingsService;
        private bool _disposed;
        private DateTime _lastCaretMoveTime;
        private SnapshotPoint _lastCaretPosition;
        private string _currentFilePath;

        public EditorEventSubscription(IWpfTextView textView, ICursorHistoryService cursorHistoryService, ISettingsService settingsService)
        {
            _textView = textView ?? throw new ArgumentNullException(nameof(textView));
            _cursorHistoryService = cursorHistoryService ?? throw new ArgumentNullException(nameof(cursorHistoryService));
            _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));

            _lastCaretMoveTime = DateTime.MinValue;
            _currentFilePath = GetFilePath();

            SubscribeToEvents();
            
            // Record initial position
            RecordInitialPosition();
        }

        private void SubscribeToEvents()
        {
            _textView.Caret.PositionChanged += OnCaretPositionChanged;
            _textView.TextBuffer.Changed += OnTextBufferChanged;
            _textView.GotAggregateFocus += OnTextViewGotFocus;
            _textView.LostAggregateFocus += OnTextViewLostFocus;
        }

        private void UnsubscribeFromEvents()
        {
            if (_textView?.Caret != null)
                _textView.Caret.PositionChanged -= OnCaretPositionChanged;

            if (_textView?.TextBuffer != null)
                _textView.TextBuffer.Changed -= OnTextBufferChanged;

            if (_textView != null)
            {
                _textView.GotAggregateFocus -= OnTextViewGotFocus;
                _textView.LostAggregateFocus -= OnTextViewLostFocus;
            }
        }

        private void OnCaretPositionChanged(object sender, Microsoft.VisualStudio.Text.Editor.CaretPositionChangedEventArgs e)
        {
            if (_disposed)
                return;

            try
            {
                var newPosition = e.NewPosition.BufferPosition;
                var oldPosition = e.OldPosition.BufferPosition;

                // Ignore small movements (like single character navigation)
                if (Math.Abs(newPosition.Position - oldPosition.Position) <= 1)
                    return;

                // Debounce rapid caret movements
                var now = DateTime.Now;
                if ((now - _lastCaretMoveTime).TotalMilliseconds < 200)
                    return;

                _lastCaretMoveTime = now;
                _lastCaretPosition = newPosition;

                // Determine if this was a significant movement (jump vs navigation)
                var isJump = IsSignificantJump(oldPosition, newPosition);
                var changeType = isJump ? ChangeTypes.Jump : ChangeTypes.Navigation;

                RecordCursorPosition(newPosition, changeType);
            }
            catch (Exception ex)
            {
                // Log error but don't crash VS
                System.Diagnostics.Debug.WriteLine($"Error in OnCaretPositionChanged: {ex.Message}");
            }
        }

        private void OnTextBufferChanged(object sender, TextContentChangedEventArgs e)
        {
            if (_disposed)
                return;

            try
            {
                // Only record on significant changes, not every keystroke
                if (e.Changes.Count > 0)
                {
                    var change = e.Changes[0];
                    
                    // Record position for edits that add/remove substantial content
                    if (change.Delta > 10 || change.Delta < -10 || 
                        change.NewText.Contains("\n") || change.OldText.Contains("\n"))
                    {
                        var caretPosition = _textView.Caret.Position.BufferPosition;
                        RecordCursorPosition(caretPosition, ChangeTypes.Edit);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in OnTextBufferChanged: {ex.Message}");
            }
        }

        private void OnTextViewGotFocus(object sender, EventArgs e)
        {
            if (_disposed)
                return;

            try
            {
                var newFilePath = GetFilePath();
                
                // Check if we switched to a different file
                if (!string.Equals(_currentFilePath, newFilePath, StringComparison.OrdinalIgnoreCase))
                {
                    _currentFilePath = newFilePath;
                    var caretPosition = _textView.Caret.Position.BufferPosition;
                    RecordCursorPosition(caretPosition, ChangeTypes.FileSwitch);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in OnTextViewGotFocus: {ex.Message}");
            }
        }

        private void OnTextViewLostFocus(object sender, EventArgs e)
        {
            // Could be used for cleanup or final position recording
        }

        private void RecordInitialPosition()
        {
            try
            {
                var caretPosition = _textView.Caret.Position.BufferPosition;
                RecordCursorPosition(caretPosition, ChangeTypes.FileSwitch);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in RecordInitialPosition: {ex.Message}");
            }
        }

        private void RecordCursorPosition(SnapshotPoint position, string changeType)
        {
            try
            {
                var filePath = GetFilePath();
                if (string.IsNullOrWhiteSpace(filePath))
                    return;

                var contextSnippet = CaptureContextSnippet(position);
                var language = DetectLanguageFromFilePath(filePath);

                var entry = new CursorHistoryEntry
                {
                    FilePath = filePath,
                    LineNumber = position.GetContainingLine().LineNumber + 1, // Convert to 1-based
                    Column = position.Position - position.GetContainingLine().Start.Position,
                    ContextSnippet = contextSnippet,
                    Timestamp = DateTime.Now,
                    ChangeType = changeType,
                    Language = language,
                    FromSuggestion = false // Will be set to true when suggestions are accepted
                };

                _cursorHistoryService.RecordCursorPosition(entry);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in RecordCursorPosition: {ex.Message}");
            }
        }

        private string CaptureContextSnippet(SnapshotPoint position, int maxLength = 100)
        {
            try
            {
                var line = position.GetContainingLine();
                var lineText = line.GetText();
                
                // Get surrounding context
                var startLine = Math.Max(0, line.LineNumber - 1);
                var endLine = Math.Min(position.Snapshot.LineCount - 1, line.LineNumber + 1);
                
                var contextLines = new List<string>();
                for (int i = startLine; i <= endLine; i++)
                {
                    var contextLine = position.Snapshot.GetLineFromLineNumber(i);
                    contextLines.Add(contextLine.GetText());
                }

                var context = string.Join(" ", contextLines);
                
                // Truncate if too long
                if (context.Length > maxLength)
                {
                    context = context.Substring(0, maxLength) + "...";
                }

                return context;
            }
            catch
            {
                return string.Empty;
            }
        }

        private bool IsSignificantJump(SnapshotPoint oldPosition, SnapshotPoint newPosition)
        {
            try
            {
                var oldLine = oldPosition.GetContainingLine().LineNumber;
                var newLine = newPosition.GetContainingLine().LineNumber;
                
                // Consider it a jump if moving more than 5 lines
                return Math.Abs(newLine - oldLine) > 5;
            }
            catch
            {
                return false;
            }
        }

        private string GetFilePath()
        {
            try
            {
                if (_textView.TextBuffer.Properties.TryGetProperty(typeof(ITextDocument), out ITextDocument document))
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
                return "unknown";

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
                    return "text";
            }
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            UnsubscribeFromEvents();
        }
    }
}