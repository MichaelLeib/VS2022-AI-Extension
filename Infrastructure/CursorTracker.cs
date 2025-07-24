using System;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using OllamaAssistant.Models;
using OllamaAssistant.Services.Interfaces;

namespace OllamaAssistant.Infrastructure
{
    /// <summary>
    /// Tracks cursor position changes in a specific text view
    /// </summary>
    public class CursorTracker : IDisposable
    {
        private readonly IWpfTextView _textView;
        private readonly ICursorHistoryService _cursorHistoryService;
        private readonly ILogger _logger;
        private readonly DebounceService _debounceService;
        private bool _disposed;

        /// <summary>
        /// Gets the file path associated with this tracker
        /// </summary>
        public string FilePath { get; }

        public CursorTracker(
            IWpfTextView textView, 
            string filePath, 
            ICursorHistoryService cursorHistoryService,
            ILogger logger)
        {
            _textView = textView ?? throw new ArgumentNullException(nameof(textView));
            FilePath = filePath ?? throw new ArgumentNullException(nameof(filePath));
            _cursorHistoryService = cursorHistoryService ?? throw new ArgumentNullException(nameof(cursorHistoryService));
            _logger = logger;

            // Create debounce service for cursor movements (250ms delay)
            _debounceService = new DebounceService(250);

            SubscribeToEvents();
        }

        private void SubscribeToEvents()
        {
            try
            {
                // Subscribe to caret position changes
                _textView.Caret.PositionChanged += OnCaretPositionChanged;
                
                // Subscribe to selection changes
                _textView.Selection.SelectionChanged += OnSelectionChanged;
                
                // Subscribe to text buffer changes to record edit locations
                _textView.TextBuffer.Changed += OnTextBufferChanged;

                // Subscribe to focus events
                _textView.GotAggregateFocus += OnTextViewGotFocus;
                _textView.LostAggregateFocus += OnTextViewLostFocus;
            }
            catch (Exception ex)
            {
                _logger?.LogErrorAsync(ex, "Error subscribing to text view events", "CursorTracker").Wait(1000);
            }
        }

        private void OnCaretPositionChanged(object sender, CaretPositionChangedEventArgs e)
        {
            if (_disposed)
                return;

            // Debounce cursor position changes to avoid too many records
            _debounceService.Debounce(() =>
            {
                try
                {
                    var position = e.NewPosition;
                    var line = position.BufferPosition.GetContainingLine();
                    
                    var entry = new CursorHistoryEntry
                    {
                        FilePath = FilePath,
                        LineNumber = line.LineNumber + 1, // Convert to 1-based
                        ColumnNumber = position.BufferPosition.Position - line.Start.Position + 1, // Convert to 1-based
                        Timestamp = DateTime.Now,
                        Context = "Cursor Movement"
                    };

                    _cursorHistoryService.RecordCursorPosition(entry);
                }
                catch (Exception ex)
                {
                    _logger?.LogErrorAsync(ex, "Error recording cursor position", "CursorTracker").Wait(1000);
                }
            });
        }

        private void OnSelectionChanged(object sender, EventArgs e)
        {
            if (_disposed || _textView.Selection.IsEmpty)
                return;

            try
            {
                var selection = _textView.Selection;
                var startLine = selection.Start.Position.GetContainingLine();
                var endLine = selection.End.Position.GetContainingLine();

                // Record selection start position
                var entry = new CursorHistoryEntry
                {
                    FilePath = FilePath,
                    LineNumber = startLine.LineNumber + 1,
                    ColumnNumber = selection.Start.Position.Position - startLine.Start.Position + 1,
                    Timestamp = DateTime.Now,
                    Context = $"Selection ({startLine.LineNumber + 1}:{selection.Start.Position.Position - startLine.Start.Position + 1} to {endLine.LineNumber + 1}:{selection.End.Position.Position - endLine.Start.Position + 1})"
                };

                _cursorHistoryService.RecordCursorPosition(entry);
            }
            catch (Exception ex)
            {
                _logger?.LogErrorAsync(ex, "Error recording selection", "CursorTracker").Wait(1000);
            }
        }

        private void OnTextBufferChanged(object sender, TextContentChangedEventArgs e)
        {
            if (_disposed || e.Changes.Count == 0)
                return;

            try
            {
                // Record the position of the first significant change
                var firstChange = e.Changes[0];
                var position = firstChange.NewPosition;
                var line = e.After.GetLineFromPosition(position);

                var changeType = DetermineChangeType(firstChange);
                var entry = new CursorHistoryEntry
                {
                    FilePath = FilePath,
                    LineNumber = line.LineNumber + 1,
                    ColumnNumber = position - line.Start.Position + 1,
                    Timestamp = DateTime.Now,
                    Context = $"Text {changeType} ({e.Changes.Count} changes)"
                };

                _cursorHistoryService.RecordCursorPosition(entry);
            }
            catch (Exception ex)
            {
                _logger?.LogErrorAsync(ex, "Error recording text change position", "CursorTracker").Wait(1000);
            }
        }

        private void OnTextViewGotFocus(object sender, EventArgs e)
        {
            if (_disposed)
                return;

            Task.Run(async () =>
            {
                try
                {
                    var caretPosition = _textView.Caret.Position.BufferPosition;
                    var line = caretPosition.GetContainingLine();

                    var entry = new CursorHistoryEntry
                    {
                        FilePath = FilePath,
                        LineNumber = line.LineNumber + 1,
                        ColumnNumber = caretPosition.Position - line.Start.Position + 1,
                        Timestamp = DateTime.Now,
                        Context = "Focus Gained"
                    };

                    _cursorHistoryService.RecordCursorPosition(entry);
                    await _logger?.LogDebugAsync($"Text view gained focus: {System.IO.Path.GetFileName(FilePath)}", "CursorTracker");
                }
                catch (Exception ex)
                {
                    await _logger?.LogErrorAsync(ex, "Error recording focus gain", "CursorTracker");
                }
            });
        }

        private void OnTextViewLostFocus(object sender, EventArgs e)
        {
            if (_disposed)
                return;

            Task.Run(async () =>
            {
                try
                {
                    await _logger?.LogDebugAsync($"Text view lost focus: {System.IO.Path.GetFileName(FilePath)}", "CursorTracker");
                }
                catch (Exception ex)
                {
                    await _logger?.LogErrorAsync(ex, "Error handling focus loss", "CursorTracker");
                }
            });
        }

        private string DetermineChangeType(ITextChange change)
        {
            if (string.IsNullOrEmpty(change.OldText) && !string.IsNullOrEmpty(change.NewText))
            {
                return "Insert";
            }
            else if (!string.IsNullOrEmpty(change.OldText) && string.IsNullOrEmpty(change.NewText))
            {
                return "Delete";
            }
            else if (!string.IsNullOrEmpty(change.OldText) && !string.IsNullOrEmpty(change.NewText))
            {
                return "Replace";
            }
            else
            {
                return "Unknown";
            }
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;

            try
            {
                // Unsubscribe from all events
                if (_textView != null)
                {
                    _textView.Caret.PositionChanged -= OnCaretPositionChanged;
                    _textView.Selection.SelectionChanged -= OnSelectionChanged;
                    _textView.TextBuffer.Changed -= OnTextBufferChanged;
                    _textView.GotAggregateFocus -= OnTextViewGotFocus;
                    _textView.LostAggregateFocus -= OnTextViewLostFocus;
                }

                _debounceService?.Dispose();
            }
            catch (Exception ex)
            {
                _logger?.LogErrorAsync(ex, "Error disposing cursor tracker", "CursorTracker").Wait(1000);
            }
        }
    }
}