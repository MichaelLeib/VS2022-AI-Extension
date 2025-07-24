using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Language.Intellisense.AsyncCompletion;
using Microsoft.VisualStudio.Language.Intellisense.AsyncCompletion.Data;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Threading;
using OllamaAssistant.Infrastructure;
using OllamaAssistant.Models;
using OllamaAssistant.Services.Interfaces;

namespace OllamaAssistant.UI.Components
{
    /// <summary>
    /// Integrates Ollama suggestions with Visual Studio's async completion system
    /// </summary>
    [Export(typeof(IAsyncCompletionItemManager))]
    [Name("OllamaAsyncCompletionItemManager")]
    internal class OllamaAsyncCompletionItemManager : IAsyncCompletionItemManager
    {
        private readonly ISettingsService _settingsService;
        private readonly ILogger _logger;

        [ImportingConstructor]
        public OllamaAsyncCompletionItemManager()
        {
            var services = ServiceLocator.Current;
            _settingsService = services?.Resolve<ISettingsService>();
            _logger = services?.Resolve<ILogger>();
        }

        public Task<ImmutableArray<CompletionItem>> SortCompletionListAsync(
            IAsyncCompletionSession session,
            AsyncCompletionSessionInitialDataSnapshot data,
            CancellationToken cancellationToken)
        {
            return Task.Run(() =>
            {
                try
                {
                    // Sort Ollama suggestions to the top
                    var sorted = data.InitialList
                        .OrderBy(item =>
                        {
                            // Check if this is an Ollama suggestion
                            if (item.Properties.ContainsProperty(typeof(CodeSuggestion)))
                            {
                                return 0; // Ollama suggestions first
                            }
                            return 1; // Other suggestions after
                        })
                        .ThenBy(item => item.SortText)
                        .ToImmutableArray();

                    return sorted;
                }
                catch (Exception ex)
                {
                    _logger?.LogErrorAsync(ex, "Error sorting completion list", "AsyncCompletion").Wait();
                    return data.InitialList;
                }
            }, cancellationToken);
        }

        public Task<FilteredCompletionModel> UpdateCompletionListAsync(
            IAsyncCompletionSession session,
            AsyncCompletionSessionDataSnapshot data,
            CancellationToken cancellationToken)
        {
            return Task.Run(() =>
            {
                try
                {
                    var filterText = session.ApplicableToSpan.GetText(data.Snapshot);
                    
                    if (string.IsNullOrWhiteSpace(filterText))
                    {
                        // No filter, return all items
                        return new FilteredCompletionModel(
                            data.InitialSortedList,
                            0,
                            filters: ImmutableArray<CompletionFilter>.Empty);
                    }

                    // Filter items based on the current text
                    var filteredItems = data.InitialSortedList
                        .Where(item => MatchesFilter(item, filterText))
                        .ToImmutableArray();

                    // Find best match
                    var selectedIndex = 0;
                    for (int i = 0; i < filteredItems.Length; i++)
                    {
                        if (filteredItems[i].FilterText.StartsWith(filterText, StringComparison.OrdinalIgnoreCase))
                        {
                            selectedIndex = i;
                            break;
                        }
                    }

                    return new FilteredCompletionModel(
                        filteredItems,
                        selectedIndex,
                        filters: ImmutableArray<CompletionFilter>.Empty);
                }
                catch (Exception ex)
                {
                    _logger?.LogErrorAsync(ex, "Error updating completion list", "AsyncCompletion").Wait();
                    return new FilteredCompletionModel(
                        data.InitialSortedList,
                        0,
                        filters: ImmutableArray<CompletionFilter>.Empty);
                }
            }, cancellationToken);
        }

        private bool MatchesFilter(CompletionItem item, string filterText)
        {
            if (string.IsNullOrWhiteSpace(filterText))
                return true;

            // For Ollama suggestions, use fuzzy matching
            if (item.Properties.TryGetProperty(typeof(CodeSuggestion), out CodeSuggestion suggestion))
            {
                return FuzzyMatch(item.FilterText, filterText) ||
                       FuzzyMatch(item.DisplayText, filterText);
            }

            // For other items, use standard prefix matching
            return item.FilterText.IndexOf(filterText, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private bool FuzzyMatch(string text, string pattern)
        {
            if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(pattern))
                return false;

            int textIndex = 0;
            int patternIndex = 0;

            while (textIndex < text.Length && patternIndex < pattern.Length)
            {
                if (char.ToLowerInvariant(text[textIndex]) == char.ToLowerInvariant(pattern[patternIndex]))
                {
                    patternIndex++;
                }
                textIndex++;
            }

            return patternIndex == pattern.Length;
        }
    }

    /// <summary>
    /// Handles async completion commit operations for Ollama suggestions
    /// </summary>
    [Export(typeof(IAsyncCompletionCommitManager))]
    [Name("OllamaAsyncCompletionCommitManager")]
    internal class OllamaAsyncCompletionCommitManager : IAsyncCompletionCommitManager
    {
        private readonly ILogger _logger;
        private readonly IIntelliSenseIntegration _intelliSenseIntegration;

        [ImportingConstructor]
        public OllamaAsyncCompletionCommitManager()
        {
            var services = ServiceLocator.Current;
            _logger = services?.Resolve<ILogger>();
            _intelliSenseIntegration = services?.Resolve<IIntelliSenseIntegration>();
        }

        public bool ShouldCommitCompletion(
            IAsyncCompletionSession session,
            SnapshotPoint location,
            char typedChar,
            CancellationToken cancellationToken)
        {
            // Check if this is an Ollama suggestion
            if (session.SelectedCompletionItem?.Properties.ContainsProperty(typeof(CodeSuggestion)) == true)
            {
                // Check commit characters for Ollama suggestions
                var commitChars = session.SelectedCompletionItem.CommitCharacters;
                if (!commitChars.IsDefaultOrEmpty && commitChars.Contains(typedChar))
                {
                    return true;
                }
            }

            // Use default behavior for non-Ollama items
            return false;
        }

        public CommitResult TryCommit(
            IAsyncCompletionSession session,
            ITextBuffer buffer,
            CompletionItem item,
            char typedChar,
            CancellationToken cancellationToken)
        {
            try
            {
                // Check if this is an Ollama suggestion
                if (item.Properties.TryGetProperty(typeof(CodeSuggestion), out CodeSuggestion suggestion))
                {
                    // Get the applicable span
                    var span = session.ApplicableToSpan.GetSpan(buffer.CurrentSnapshot);
                    
                    // Perform the text replacement
                    using (var edit = buffer.CreateEdit())
                    {
                        edit.Replace(span, item.InsertText);
                        edit.Apply();
                    }

                    // Fire the suggestion accepted event
                    var acceptedArgs = new SuggestionAcceptedEventArgs
                    {
                        Suggestion = suggestion,
                        InsertionPosition = span.Start,
                        WasModified = false,
                        InsertedText = item.InsertText
                    };

                    Task.Run(async () =>
                    {
                        try
                        {
                            _intelliSenseIntegration?.SuggestionAccepted?.Invoke(this, acceptedArgs);
                            await _logger?.LogInfoAsync(
                                $"Ollama suggestion accepted: {suggestion.Type} with confidence {suggestion.Confidence:P0}",
                                "AsyncCompletion");
                        }
                        catch (Exception ex)
                        {
                            await _logger?.LogErrorAsync(ex, "Error firing suggestion accepted event", "AsyncCompletion");
                        }
                    });

                    return CommitResult.Handled;
                }

                // Not an Ollama suggestion, let default handling take over
                return CommitResult.Unhandled;
            }
            catch (Exception ex)
            {
                _logger?.LogErrorAsync(ex, "Error committing completion", "AsyncCompletion").Wait();
                return CommitResult.Unhandled;
            }
        }
    }

    /// <summary>
    /// Provides streaming async completion support for Ollama suggestions
    /// </summary>
    internal class StreamingCompletionUpdater
    {
        private readonly IAsyncCompletionSession _session;
        private readonly ITextView _textView;
        private readonly ILogger _logger;
        private readonly CancellationTokenSource _cancellationTokenSource;
        private CodeSuggestion _currentSuggestion;
        private bool _isUpdating;

        public StreamingCompletionUpdater(
            IAsyncCompletionSession session,
            ITextView textView,
            ILogger logger)
        {
            _session = session ?? throw new ArgumentNullException(nameof(session));
            _textView = textView ?? throw new ArgumentNullException(nameof(textView));
            _logger = logger;
            _cancellationTokenSource = new CancellationTokenSource();
        }

        public async Task UpdateWithStreamingAsync(IAsyncEnumerable<CodeSuggestion> suggestionStream)
        {
            if (_isUpdating)
                return;

            _isUpdating = true;

            try
            {
                await foreach (var partialSuggestion in suggestionStream
                    .WithCancellation(_cancellationTokenSource.Token))
                {
                    _currentSuggestion = partialSuggestion;

                    // Update the completion item in the session
                    await _textView.VisualElement.Dispatcher.InvokeAsync(() =>
                    {
                        try
                        {
                            // Update inline preview if enabled
                            var span = _session.ApplicableToSpan.GetSpan(_textView.TextSnapshot);
                            _textView.ShowInlinePreview(partialSuggestion, span);

                            // Trigger session update
                            _session.OpenOrUpdate(
                                trigger: new CompletionTrigger(CompletionTriggerReason.Invoke),
                                triggerLocation: span.Start,
                                cancellationToken: CancellationToken.None);
                        }
                        catch (Exception ex)
                        {
                            _logger?.LogErrorAsync(ex, "Error updating streaming completion", "StreamingCompletion").Wait();
                        }
                    });

                    // Add delay to prevent UI thrashing
                    await Task.Delay(50, _cancellationTokenSource.Token);
                }
            }
            catch (OperationCanceledException)
            {
                // Streaming was cancelled
            }
            catch (Exception ex)
            {
                await _logger?.LogErrorAsync(ex, "Error in streaming completion", "StreamingCompletion");
            }
            finally
            {
                _isUpdating = false;
            }
        }

        public void Cancel()
        {
            _cancellationTokenSource?.Cancel();
        }

        public void Dispose()
        {
            Cancel();
            _cancellationTokenSource?.Dispose();
        }
    }
}