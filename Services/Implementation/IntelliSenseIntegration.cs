using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Language.Intellisense;
using Microsoft.VisualStudio.Language.Intellisense.AsyncCompletion;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Utilities;
using OllamaAssistant.Models;
using OllamaAssistant.Services.Interfaces;

namespace OllamaAssistant.Services.Implementation
{
    /// <summary>
    /// Implementation of IntelliSense integration for displaying AI suggestions
    /// </summary>
    public class IntelliSenseIntegration : IIntelliSenseIntegration
    {
        private readonly IAsyncCompletionBroker _completionBroker;
        private readonly Dictionary<ITextView, CompletionSession> _activeSessions;
        private readonly object _lockObject = new object();
        private SuggestionDisplayOptions _displayOptions;
        private bool _disposed;

        public IntelliSenseIntegration(IAsyncCompletionBroker completionBroker)
        {
            _completionBroker = completionBroker ?? throw new ArgumentNullException(nameof(completionBroker));
            _activeSessions = new Dictionary<ITextView, CompletionSession>();
            _displayOptions = new SuggestionDisplayOptions();
        }

        #region Properties

        public bool IsSuggestionActive
        {
            get
            {
                lock (_lockObject)
                {
                    return _activeSessions.Any(kvp => kvp.Value.IsActive);
                }
            }
        }

        #endregion

        #region Events

        public event EventHandler<SuggestionAcceptedEventArgs> SuggestionAccepted;
        public event EventHandler<SuggestionDismissedEventArgs> SuggestionDismissed;

        #endregion

        #region Public Methods

        public async Task ShowSuggestionAsync(CodeSuggestion suggestion)
        {
            if (suggestion == null || !suggestion.ShouldShow(_displayOptions.MinimumConfidence))
                return;

            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            try
            {
                var textView = GetActiveTextView();
                if (textView == null)
                    return;

                var suggestions = new[] { suggestion };
                await ShowSuggestionsInternalAsync(textView, suggestions);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error showing suggestion: {ex.Message}");
            }
        }

        public async Task ShowSuggestionsAsync(CodeSuggestion[] suggestions)
        {
            if (suggestions == null || suggestions.Length == 0)
                return;

            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            try
            {
                var textView = GetActiveTextView();
                if (textView == null)
                    return;

                // Filter and limit suggestions
                var filteredSuggestions = suggestions
                    .Where(s => s.ShouldShow(_displayOptions.MinimumConfidence))
                    .OrderByDescending(s => s.Priority)
                    .Take(_displayOptions.MaxSuggestions)
                    .ToArray();

                if (filteredSuggestions.Length > 0)
                {
                    await ShowSuggestionsInternalAsync(textView, filteredSuggestions);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error showing suggestions: {ex.Message}");
            }
        }

        public void DismissSuggestion()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            try
            {
                lock (_lockObject)
                {
                    var sessionsToClose = _activeSessions.Values.Where(s => s.IsActive).ToList();
                    
                    foreach (var session in sessionsToClose)
                    {
                        session.Dismiss();
                    }
                    
                    _activeSessions.Clear();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error dismissing suggestion: {ex.Message}");
            }
        }

        public async Task UpdateSuggestionAsync(CodeSuggestion suggestion)
        {
            if (suggestion == null)
                return;

            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            try
            {
                // Dismiss current suggestion and show updated one
                DismissSuggestion();
                await ShowSuggestionAsync(suggestion);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error updating suggestion: {ex.Message}");
            }
        }

        public async Task ShowStreamingSuggestionAsync(IEnumerable<CodeSuggestion> suggestionStream)
        {
            if (suggestionStream == null)
                return;

            try
            {
                CodeSuggestion currentSuggestion = null;
                var cancellationToken = CancellationToken.None;

                foreach (var partialSuggestion in suggestionStream)
                {
                    await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

                    try
                    {
                        if (partialSuggestion == null)
                            continue;

                        // First suggestion - show it immediately
                        if (currentSuggestion == null)
                        {
                            currentSuggestion = partialSuggestion;
                            await ShowSuggestionAsync(partialSuggestion);
                        }
                        else
                        {
                            // Update existing suggestion with new content
                            currentSuggestion = partialSuggestion;
                            await UpdateSuggestionInPlaceAsync(partialSuggestion);
                        }

                        // Add a small delay to prevent UI thrashing
                        await Task.Delay(50, cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Error in streaming suggestion update: {ex.Message}");
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error updating suggestion: {ex.Message}");
            }
        }

        public void ConfigureDisplay(SuggestionDisplayOptions options)
        {
            _displayOptions = options ?? new SuggestionDisplayOptions();
        }

        #endregion

        #region Private Helper Methods

        private async Task UpdateSuggestionInPlaceAsync(CodeSuggestion updatedSuggestion)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            try
            {
                lock (_lockObject)
                {
                    // Find the active session for the current view
                    var activeSession = _activeSessions.Values.FirstOrDefault(s => s.IsActive);
                    if (activeSession != null)
                    {
                        // Update the session with new suggestion content
                        activeSession.UpdateSuggestion(updatedSuggestion);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error updating suggestion in place: {ex.Message}");
            }
        }

        #endregion

        #region Private Methods

        private async Task ShowSuggestionsInternalAsync(IWpfTextView textView, CodeSuggestion[] suggestions)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            try
            {
                // Dismiss any existing session for this view
                DismissSessionForView(textView);

                // Create and start new completion session
                var caretPosition = textView.Caret.Position.BufferPosition;
                var completionItems = CreateCompletionItems(suggestions, textView);

                if (completionItems.Length == 0)
                    return;

                var session = new CompletionSession(textView, completionItems, suggestions);
                
                lock (_lockObject)
                {
                    _activeSessions[textView] = session;
                }

                // Subscribe to session events
                session.SuggestionAccepted += OnSuggestionAccepted;
                session.SuggestionDismissed += OnSuggestionDismissed;
                session.SessionClosed += OnSessionClosed;

                // Start the session
                await session.StartAsync();

                // Set up auto-dismiss timer if configured
                if (_displayOptions.AutoDismissTimeout > 0)
                {
                    SetupAutoDismissTimer(session, _displayOptions.AutoDismissTimeout);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in ShowSuggestionsInternalAsync: {ex.Message}");
            }
        }

        private CompletionItem[] CreateCompletionItems(CodeSuggestion[] suggestions, IWpfTextView textView)
        {
            var items = new List<CompletionItem>();

            foreach (var suggestion in suggestions)
            {
                try
                {
                    var item = new CompletionItem(suggestion, textView, _displayOptions);
                    items.Add(item);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error creating completion item: {ex.Message}");
                }
            }

            return items.ToArray();
        }

        private void DismissSessionForView(IWpfTextView textView)
        {
            lock (_lockObject)
            {
                if (_activeSessions.TryGetValue(textView, out var existingSession))
                {
                    existingSession.Dismiss();
                    _activeSessions.Remove(textView);
                }
            }
        }

        private void SetupAutoDismissTimer(CompletionSession session, int timeoutMs)
        {
            var timer = new System.Threading.Timer(state =>
            {
                try
                {
                    ThreadHelper.JoinableTaskFactory.Run(async () =>
                    {
                        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                        
                        if (session.IsActive)
                        {
                            session.Dismiss(DismissalReason.Timeout);
                        }
                    });
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error in auto-dismiss timer: {ex.Message}");
                }
            }, null, timeoutMs, Timeout.Infinite);

            session.SetAutoDismissTimer(timer);
        }

        private IWpfTextView GetActiveTextView()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            try
            {
                // This would typically get the active text view from VS services
                // For now, we'll return null and let the calling service provide the view
                return null;
            }
            catch
            {
                return null;
            }
        }

        #endregion

        #region Event Handlers

        private void OnSuggestionAccepted(object sender, SuggestionAcceptedEventArgs e)
        {
            try
            {
                // Clean up the session
                if (sender is CompletionSession session)
                {
                    lock (_lockObject)
                    {
                        var viewToRemove = _activeSessions.FirstOrDefault(kvp => kvp.Value == session).Key;
                        if (viewToRemove != null)
                        {
                            _activeSessions.Remove(viewToRemove);
                        }
                    }
                }

                // Forward the event
                SuggestionAccepted?.Invoke(this, e);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in OnSuggestionAccepted: {ex.Message}");
            }
        }

        private void OnSuggestionDismissed(object sender, SuggestionDismissedEventArgs e)
        {
            try
            {
                // Clean up the session
                if (sender is CompletionSession session)
                {
                    lock (_lockObject)
                    {
                        var viewToRemove = _activeSessions.FirstOrDefault(kvp => kvp.Value == session).Key;
                        if (viewToRemove != null)
                        {
                            _activeSessions.Remove(viewToRemove);
                        }
                    }
                }

                // Forward the event
                SuggestionDismissed?.Invoke(this, e);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in OnSuggestionDismissed: {ex.Message}");
            }
        }

        private void OnSessionClosed(object sender, EventArgs e)
        {
            try
            {
                if (sender is CompletionSession session)
                {
                    // Unsubscribe from events
                    session.SuggestionAccepted -= OnSuggestionAccepted;
                    session.SuggestionDismissed -= OnSuggestionDismissed;
                    session.SessionClosed -= OnSessionClosed;

                    // Remove from active sessions
                    lock (_lockObject)
                    {
                        var viewToRemove = _activeSessions.FirstOrDefault(kvp => kvp.Value == session).Key;
                        if (viewToRemove != null)
                        {
                            _activeSessions.Remove(viewToRemove);
                        }
                    }

                    // Dispose the session
                    session.Dispose();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in OnSessionClosed: {ex.Message}");
            }
        }

        private void OnCaretPositionChanged(object sender, CaretPositionChangedEventArgs e)
        {
            try
            {
                // Dismiss if caret moves significantly
                var oldPos = e.OldPosition.BufferPosition.Position;
                var newPos = e.NewPosition.BufferPosition.Position;

                if (Math.Abs(newPos - oldPos) > 5)
                {
                    Dismiss(DismissalReason.NavigatedAway);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in OnCaretPositionChanged: {ex.Message}");
            }
        }

        private void OnTextBufferChanged(object sender, TextContentChangedEventArgs e)
        {
            try
            {
                // Dismiss on significant text changes
                if (e.Changes.Any(c => c.Delta > 1 || c.Delta < -1))
                {
                    Dismiss(DismissalReason.ContinuedTyping);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in OnTextBufferChanged: {ex.Message}");
            }
        }

        private void OnLostFocus(object sender, EventArgs e)
        {
            try
            {
                Dismiss(DismissalReason.NavigatedAway);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in OnLostFocus: {ex.Message}");
            }
        }

        #endregion

        #region IDisposable

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;

            try
            {
                // Dismiss all active sessions
                DismissSuggestion();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error disposing IntelliSenseIntegration: {ex.Message}");
            }
        }

        #endregion
    }

    /// <summary>
    /// Represents a completion session for AI suggestions
    /// </summary>
    internal class CompletionSession : IDisposable
    {
        private readonly IWpfTextView _textView;
        private readonly CompletionItem[] _completionItems;
        private readonly CodeSuggestion[] _suggestions;
        private System.Threading.Timer _autoDismissTimer;
        private bool _isActive;
        private bool _disposed;
        private DateTime _startTime;

        public CompletionSession(IWpfTextView textView, CompletionItem[] completionItems, CodeSuggestion[] suggestions)
        {
            _textView = textView ?? throw new ArgumentNullException(nameof(textView));
            _completionItems = completionItems ?? throw new ArgumentNullException(nameof(completionItems));
            _suggestions = suggestions ?? throw new ArgumentNullException(nameof(suggestions));
            _startTime = DateTime.Now;
        }

        public bool IsActive => _isActive && !_disposed;

        public event EventHandler<SuggestionAcceptedEventArgs> SuggestionAccepted;
        public event EventHandler<SuggestionDismissedEventArgs> SuggestionDismissed;
        public event EventHandler SessionClosed;

        public async Task StartAsync()
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            try
            {
                _isActive = true;

                // Subscribe to text view events for dismissal detection
                _textView.Caret.PositionChanged += OnCaretPositionChanged;
                _textView.TextBuffer.Changed += OnTextBufferChanged;
                _textView.LostAggregateFocus += OnLostFocus;

                // Show inline preview if enabled
                ShowInlinePreview();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error starting completion session: {ex.Message}");
                _isActive = false;
            }
        }

        public void Dismiss(DismissalReason reason = DismissalReason.UserDismissed)
        {
            if (!_isActive || _disposed)
                return;

            try
            {
                _isActive = false;

                // Calculate display duration
                var displayDuration = (int)(DateTime.Now - _startTime).TotalMilliseconds;

                // Fire dismissed event
                var dismissedArgs = new SuggestionDismissedEventArgs
                {
                    Suggestion = _suggestions.FirstOrDefault(),
                    Reason = reason,
                    DisplayDuration = displayDuration
                };

                SuggestionDismissed?.Invoke(this, dismissedArgs);

                // Clean up
                Cleanup();

                // Fire session closed event
                SessionClosed?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error dismissing completion session: {ex.Message}");
            }
        }

        public void AcceptSuggestion(CodeSuggestion suggestion, string insertedText = null)
        {
            if (!_isActive || _disposed)
                return;

            try
            {
                _isActive = false;

                // Fire accepted event
                var acceptedArgs = new SuggestionAcceptedEventArgs
                {
                    Suggestion = suggestion,
                    InsertionPosition = _textView.Caret.Position.BufferPosition.Position,
                    WasModified = !string.Equals(suggestion.Text, insertedText),
                    InsertedText = insertedText ?? suggestion.Text
                };

                SuggestionAccepted?.Invoke(this, acceptedArgs);

                // Clean up
                Cleanup();

                // Fire session closed event
                SessionClosed?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error accepting suggestion: {ex.Message}");
            }
        }

        public void SetAutoDismissTimer(System.Threading.Timer timer)
        {
            _autoDismissTimer?.Dispose();
            _autoDismissTimer = timer;
        }

        public void UpdateSuggestion(CodeSuggestion updatedSuggestion)
        {
            try
            {
                if (!_isActive || _disposed || updatedSuggestion == null)
                    return;

                // Update the first suggestion (assuming single suggestion updates for streaming)
                if (_suggestions.Length > 0)
                {
                    _suggestions[0] = updatedSuggestion;
                    
                    // Refresh the inline preview if it's being shown
                    ShowInlinePreview();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error updating suggestion: {ex.Message}");
            }
        }

        private void ShowInlinePreview()
        {
            try
            {
                if (_suggestions.Length > 0 && _suggestions[0].Text.Length < 100)
                {
                    // Show inline preview for short suggestions
                    // This would integrate with VS editor adornments
                    // For now, we'll just log the preview
                    System.Diagnostics.Debug.WriteLine($"Showing inline preview: {_suggestions[0].Text}");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error showing inline preview: {ex.Message}");
            }
        }

        private void OnCaretPositionChanged(object sender, CaretPositionChangedEventArgs e)
        {
            try
            {
                // Dismiss if caret moves significantly
                var oldPos = e.OldPosition.BufferPosition.Position;
                var newPos = e.NewPosition.BufferPosition.Position;

                if (Math.Abs(newPos - oldPos) > 5)
                {
                    Dismiss(DismissalReason.NavigatedAway);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in OnCaretPositionChanged: {ex.Message}");
            }
        }

        private void OnTextBufferChanged(object sender, TextContentChangedEventArgs e)
        {
            try
            {
                // Dismiss on significant text changes
                if (e.Changes.Any(c => c.Delta > 1 || c.Delta < -1))
                {
                    Dismiss(DismissalReason.ContinuedTyping);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in OnTextBufferChanged: {ex.Message}");
            }
        }

        private void OnLostFocus(object sender, EventArgs e)
        {
            try
            {
                Dismiss(DismissalReason.NavigatedAway);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in OnLostFocus: {ex.Message}");
            }
        }

        private void Cleanup()
        {
            try
            {
                // Unsubscribe from events
                if (_textView != null)
                {
                    _textView.Caret.PositionChanged -= OnCaretPositionChanged;
                    _textView.TextBuffer.Changed -= OnTextBufferChanged;
                    _textView.LostAggregateFocus -= OnLostFocus;
                }

                // Dispose timer
                _autoDismissTimer?.Dispose();
                _autoDismissTimer = null;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in Cleanup: {ex.Message}");
            }
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;

            try
            {
                if (_isActive)
                {
                    Dismiss(DismissalReason.Replaced);
                }
                else
                {
                    Cleanup();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error disposing CompletionSession: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Represents a completion item for display in IntelliSense
    /// </summary>
    internal class CompletionItem
    {
        public CompletionItem(CodeSuggestion suggestion, IWpfTextView textView, SuggestionDisplayOptions options)
        {
            Suggestion = suggestion ?? throw new ArgumentNullException(nameof(suggestion));
            TextView = textView ?? throw new ArgumentNullException(nameof(textView));
            DisplayOptions = options ?? throw new ArgumentNullException(nameof(options));

            // Create display text
            DisplayText = CreateDisplayText();
            Description = CreateDescription();
        }

        public CodeSuggestion Suggestion { get; }
        public IWpfTextView TextView { get; }
        public SuggestionDisplayOptions DisplayOptions { get; }
        public string DisplayText { get; }
        public string Description { get; }

        private string CreateDisplayText()
        {
            var text = Suggestion.Text;
            
            // Truncate if too long
            if (text.Length > 80)
            {
                text = text.Substring(0, 77) + "...";
            }

            // Add confidence score if enabled
            if (DisplayOptions.ShowConfidence)
            {
                text += $" ({Suggestion.Confidence:P0})";
            }

            return text;
        }

        private string CreateDescription()
        {
            var description = Suggestion.Description ?? "AI-generated suggestion";

            if (DisplayOptions.ShowDescriptions)
            {
                if (!string.IsNullOrEmpty(Suggestion.SourceContext))
                {
                    description += $" (from {Suggestion.SourceContext})";
                }
            }

            return description;
        }
    }
}
