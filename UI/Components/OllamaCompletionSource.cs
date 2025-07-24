using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.ComponentModel.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Language.Intellisense.AsyncCompletion;
using Microsoft.VisualStudio.Language.Intellisense.AsyncCompletion.Data;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Text.Operations;
using Microsoft.VisualStudio.Utilities;
using OllamaAssistant.Infrastructure;
using OllamaAssistant.Models;
using OllamaAssistant.Services.Interfaces;

namespace OllamaAssistant.UI.Components
{
    /// <summary>
    /// Provides code completion items from Ollama AI suggestions
    /// </summary>
    [Export(typeof(IAsyncCompletionSourceProvider))]
    [ContentType("text")]
    [Name("OllamaCompletionSourceProvider")]
    internal class OllamaCompletionSourceProvider : IAsyncCompletionSourceProvider
    {
        [Import]
        internal ITextStructureNavigatorSelectorService NavigatorService { get; set; }

        [Import]
        internal IAsyncCompletionBroker CompletionBroker { get; set; }

        public IAsyncCompletionSource GetOrCreate(ITextView textView)
        {
            if (textView == null)
                throw new ArgumentNullException(nameof(textView));

            return textView.Properties.GetOrCreateSingletonProperty(
                typeof(OllamaCompletionSource),
                () => CreateCompletionSource(textView));
        }

        private IAsyncCompletionSource CreateCompletionSource(ITextView textView)
        {
            var services = ServiceLocator.Current;
            if (services == null)
                return new OllamaCompletionSource(); // Return empty source if services not available

            var ollamaService = services.Resolve<IOllamaService>();
            var contextService = services.Resolve<IContextCaptureService>();
            var settingsService = services.Resolve<ISettingsService>();
            var cursorHistoryService = services.Resolve<ICursorHistoryService>();
            var logger = services.Resolve<ILogger>();

            return new OllamaCompletionSource(
                textView,
                ollamaService,
                contextService,
                settingsService,
                cursorHistoryService,
                logger,
                NavigatorService.GetTextStructureNavigator(textView.TextBuffer));
        }
    }

    /// <summary>
    /// Completion source that provides Ollama AI suggestions
    /// </summary>
    internal class OllamaCompletionSource : IAsyncCompletionSource
    {
        private readonly ITextView _textView;
        private readonly IOllamaService _ollamaService;
        private readonly IContextCaptureService _contextService;
        private readonly ISettingsService _settingsService;
        private readonly ICursorHistoryService _cursorHistoryService;
        private readonly ILogger _logger;
        private readonly ITextStructureNavigator _navigator;
        private readonly object _lockObject = new object();
        private CompletionContext _lastContext;
        private DateTime _lastRequestTime = DateTime.MinValue;

        public OllamaCompletionSource()
        {
            // Empty constructor for when services aren't available
        }

        public OllamaCompletionSource(
            ITextView textView,
            IOllamaService ollamaService,
            IContextCaptureService contextService,
            ISettingsService settingsService,
            ICursorHistoryService cursorHistoryService,
            ILogger logger,
            ITextStructureNavigator navigator)
        {
            _textView = textView ?? throw new ArgumentNullException(nameof(textView));
            _ollamaService = ollamaService;
            _contextService = contextService;
            _settingsService = settingsService;
            _cursorHistoryService = cursorHistoryService;
            _logger = logger;
            _navigator = navigator;
        }

        public async Task<CompletionContext> GetCompletionContextAsync(
            IAsyncCompletionSession session,
            CompletionTrigger trigger,
            SnapshotPoint triggerLocation,
            SnapshotSpan applicableToSpan,
            CancellationToken cancellationToken)
        {
            try
            {
                // Check if services are available and extension is enabled
                if (!IsEnabled())
                {
                    return CompletionContext.Empty;
                }

                // Debounce rapid requests
                if (!ShouldRequestCompletion())
                {
                    return _lastContext ?? CompletionContext.Empty;
                }

                _lastRequestTime = DateTime.Now;

                // Capture code context
                var codeContext = await CaptureContextAsync(triggerLocation, cancellationToken);
                if (codeContext == null)
                {
                    return CompletionContext.Empty;
                }

                // Get AI suggestions
                var suggestions = await GetAISuggestionsAsync(codeContext, cancellationToken);
                if (suggestions == null || suggestions.Length == 0)
                {
                    return CompletionContext.Empty;
                }

                // Convert to completion items
                var items = ConvertToCompletionItems(suggestions, applicableToSpan);
                
                _lastContext = new CompletionContext(items);
                return _lastContext;
            }
            catch (Exception ex)
            {
                await _logger?.LogErrorAsync(ex, "Error getting completion context", "OllamaCompletionSource");
                return CompletionContext.Empty;
            }
        }

        public async Task<object> GetDescriptionAsync(
            IAsyncCompletionSession session,
            CompletionItem item,
            CancellationToken cancellationToken)
        {
            try
            {
                if (item.Properties.TryGetProperty(typeof(CodeSuggestion), out CodeSuggestion suggestion))
                {
                    return CreateDescriptionElement(suggestion);
                }
                
                return item.DisplayText;
            }
            catch (Exception ex)
            {
                await _logger?.LogErrorAsync(ex, "Error getting item description", "OllamaCompletionSource");
                return null;
            }
        }

        public CompletionStartData InitializeCompletion(
            CompletionTrigger trigger,
            SnapshotPoint triggerLocation,
            CancellationToken cancellationToken)
        {
            try
            {
                // Don't trigger on every character if not enabled
                if (!IsEnabled() || !ShouldTrigger(trigger, triggerLocation))
                {
                    return CompletionStartData.DoesNotParticipateInCompletion;
                }

                // Find the extent of the current word/token
                var extent = FindTokenExtent(triggerLocation);
                
                return new CompletionStartData(
                    CompletionParticipation.ProvidesItems,
                    extent);
            }
            catch (Exception ex)
            {
                _logger?.LogErrorAsync(ex, "Error initializing completion", "OllamaCompletionSource").Wait();
                return CompletionStartData.DoesNotParticipateInCompletion;
            }
        }

        #region Private Helper Methods

        private bool IsEnabled()
        {
            return _settingsService?.IsEnabled == true && 
                   _settingsService?.CodePredictionEnabled == true &&
                   _ollamaService != null;
        }

        private bool ShouldRequestCompletion()
        {
            var debounceDelay = _settingsService?.TypingDebounceDelay ?? 500;
            return (DateTime.Now - _lastRequestTime).TotalMilliseconds > debounceDelay;
        }

        private bool ShouldTrigger(CompletionTrigger trigger, SnapshotPoint triggerLocation)
        {
            // Always trigger on explicit invocation
            if (trigger.Reason == CompletionTriggerReason.Invoke ||
                trigger.Reason == CompletionTriggerReason.InvokeAndCommitIfUnique)
            {
                return true;
            }

            // For character triggers, check if it's a meaningful character
            if (trigger.Reason == CompletionTriggerReason.Insertion)
            {
                if (trigger.Character.HasValue)
                {
                    var ch = trigger.Character.Value;
                    // Trigger on identifier characters, dots, spaces after keywords
                    return char.IsLetterOrDigit(ch) || ch == '.' || ch == '_' || ch == ' ';
                }
            }

            return false;
        }

        private SnapshotSpan FindTokenExtent(SnapshotPoint point)
        {
            try
            {
                // Use the text structure navigator to find word extent
                var extent = _navigator.GetExtentOfWord(point);
                if (extent.IsSignificant)
                {
                    return extent.Span;
                }

                // Fallback to simple word boundary detection
                var line = point.GetContainingLine();
                var lineText = line.GetText();
                var lineStart = line.Start.Position;
                var position = point.Position - lineStart;

                // Find start of current token
                int start = position;
                while (start > 0 && IsTokenChar(lineText[start - 1]))
                {
                    start--;
                }

                // Find end of current token
                int end = position;
                while (end < lineText.Length && IsTokenChar(lineText[end]))
                {
                    end++;
                }

                return new SnapshotSpan(
                    point.Snapshot,
                    lineStart + start,
                    end - start);
            }
            catch
            {
                // Return empty span at trigger point if we can't determine extent
                return new SnapshotSpan(point, 0);
            }
        }

        private bool IsTokenChar(char ch)
        {
            return char.IsLetterOrDigit(ch) || ch == '_';
        }

        private async Task<CodeContext> CaptureContextAsync(
            SnapshotPoint triggerLocation,
            CancellationToken cancellationToken)
        {
            try
            {
                var linesUp = _settingsService?.SurroundingLinesUp ?? 3;
                var linesDown = _settingsService?.SurroundingLinesDown ?? 2;

                var context = await _contextService.CaptureContextAsync(
                    triggerLocation,
                    linesUp,
                    linesDown);

                // Add cursor history
                if (_settingsService?.IncludeCursorHistory == true)
                {
                    var historyDepth = _settingsService.CursorHistoryMemoryDepth;
                    context.CursorHistory = _cursorHistoryService
                        .GetRecentHistory(historyDepth)
                        .ToList();
                }

                return context;
            }
            catch (Exception ex)
            {
                await _logger?.LogErrorAsync(ex, "Error capturing context", "OllamaCompletionSource");
                return null;
            }
        }

        private async Task<CodeSuggestion[]> GetAISuggestionsAsync(
            CodeContext context,
            CancellationToken cancellationToken)
        {
            try
            {
                var suggestions = new List<CodeSuggestion>();

                // Check if streaming is enabled
                var useStreaming = _settingsService?.EnableStreamingCompletions ?? false;
                
                if (useStreaming)
                {
                    // Use streaming for faster perceived response time
                    var streamingSuggestions = new List<CodeSuggestion>();
                    var lastValidSuggestion = new CodeSuggestion();
                    
                    await foreach (var partialSuggestion in _ollamaService.GetStreamingCodeSuggestionAsync(context, cancellationToken))
                    {
                        if (partialSuggestion != null && !string.IsNullOrWhiteSpace(partialSuggestion.Text))
                        {
                            lastValidSuggestion = partialSuggestion;
                            
                            // Only add complete suggestions to avoid flickering
                            if (!partialSuggestion.IsPartial)
                            {
                                streamingSuggestions.Add(partialSuggestion);
                                break; // For completion UI, we typically want just the final result
                            }
                        }
                        
                        // Respect cancellation
                        cancellationToken.ThrowIfCancellationRequested();
                    }
                    
                    // Add the final suggestion if we have one
                    if (!string.IsNullOrWhiteSpace(lastValidSuggestion.Text))
                    {
                        suggestions.Add(lastValidSuggestion);
                    }
                }
                else
                {
                    // Use traditional completion
                    var suggestion = await _ollamaService.GetCodeSuggestionAsync(context, cancellationToken);
                    
                    if (suggestion != null && !string.IsNullOrWhiteSpace(suggestion.Text))
                    {
                        suggestions.Add(suggestion);
                    }
                }

                // Filter by confidence threshold
                var minConfidence = _settingsService?.MinimumConfidenceThreshold ?? 0.5;
                return suggestions
                    .Where(s => s.Confidence >= minConfidence)
                    .OrderByDescending(s => s.Confidence)
                    .Take(_settingsService?.MaxSuggestions ?? 5)
                    .ToArray();
            }
            catch (OperationCanceledException)
            {
                // Handle cancellation gracefully
                await _logger?.LogDebugAsync("AI suggestion request was cancelled", "OllamaCompletionSource");
                return Array.Empty<CodeSuggestion>();
            }
            catch (Exception ex)
            {
                await _logger?.LogErrorAsync(ex, "Error getting AI suggestions", "OllamaCompletionSource");
                return Array.Empty<CodeSuggestion>();
            }
        }

        private ImmutableArray<CompletionItem> ConvertToCompletionItems(
            CodeSuggestion[] suggestions,
            SnapshotSpan applicableToSpan)
        {
            var items = new List<CompletionItem>();

            foreach (var suggestion in suggestions)
            {
                try
                {
                    var item = CreateCompletionItem(suggestion, applicableToSpan);
                    items.Add(item);
                }
                catch (Exception ex)
                {
                    _logger?.LogErrorAsync(ex, "Error creating completion item", "OllamaCompletionSource").Wait();
                }
            }

            return items.ToImmutableArray();
        }

        private CompletionItem CreateCompletionItem(
            CodeSuggestion suggestion,
            SnapshotSpan applicableToSpan)
        {
            // Create display text with optional confidence
            var displayText = suggestion.Text;
            if (_settingsService?.ShowConfidenceScores == true)
            {
                displayText += $" ({suggestion.Confidence:P0})";
            }

            // Determine icon based on suggestion type
            var icon = GetIconForSuggestionType(suggestion.Type);

            // Create the completion item
            var item = new CompletionItem(
                displayText: displayText,
                source: this,
                icon: icon,
                filters: ImmutableArray<CompletionFilter>.Empty,
                suffix: GetSuffixForSuggestionType(suggestion.Type),
                insertText: suggestion.Text,
                sortText: $"!{suggestion.Priority:D3}_{suggestion.Text}", // ! prefix to sort at top
                filterText: suggestion.Text,
                automationText: null,
                attributeIcons: ImmutableArray<ImageElement>.Empty);

            // Store the suggestion in properties for later use
            item.Properties.AddProperty(typeof(CodeSuggestion), suggestion);
            
            // Add commit characters based on suggestion type
            if (suggestion.Type == SuggestionType.Method)
            {
                item.CommitCharacters = ImmutableArray.Create('(', '\t', '\n');
            }
            else if (suggestion.Type == SuggestionType.Type)
            {
                item.CommitCharacters = ImmutableArray.Create(' ', '.', '\t', '\n');
            }
            else
            {
                item.CommitCharacters = ImmutableArray.Create('\t', '\n', ' ', '.', ';', ',', ')', ']', '}');
            }

            return item;
        }

        private ImageElement GetIconForSuggestionType(SuggestionType type)
        {
            // Use VS standard icons based on suggestion type
            var iconName = type switch
            {
                SuggestionType.Method => "Method",
                SuggestionType.Variable => "Field",
                SuggestionType.Type => "Class",
                SuggestionType.Parameter => "Parameter",
                SuggestionType.Snippet => "Snippet",
                SuggestionType.Import => "Namespace",
                SuggestionType.Comment => "Comment",
                SuggestionType.Documentation => "Document",
                _ => "IntelliSense"
            };

            return new ImageElement(new ImageId(KnownImageIds.ImageCatalogGuid, KnownImageIds.GetImageId(iconName)));
        }

        private string GetSuffixForSuggestionType(SuggestionType type)
        {
            return type switch
            {
                SuggestionType.Method => "()",
                SuggestionType.Type => " ",
                SuggestionType.Variable => " ",
                _ => ""
            };
        }

        private object CreateDescriptionElement(CodeSuggestion suggestion)
        {
            // Create a rich description for the tooltip
            var description = new System.Text.StringBuilder();
            
            // Add type badge
            description.AppendLine($"[{suggestion.Type}] AI Suggestion");
            description.AppendLine();

            // Add description if available
            if (!string.IsNullOrWhiteSpace(suggestion.Description))
            {
                description.AppendLine(suggestion.Description);
                description.AppendLine();
            }

            // Add confidence score
            description.AppendLine($"Confidence: {suggestion.Confidence:P0}");

            // Add source context if available
            if (!string.IsNullOrWhiteSpace(suggestion.SourceContext))
            {
                description.AppendLine($"Based on: {suggestion.SourceContext}");
            }

            return description.ToString();
        }

        #endregion
    }

    /// <summary>
    /// Helper class for VS IntelliSense image IDs
    /// </summary>
    internal static class KnownImageIds
    {
        public static readonly Guid ImageCatalogGuid = new Guid("{ae27a6b0-e345-4288-96df-5eaf394ee369}");

        public static int GetImageId(string name)
        {
            // Map names to VS image catalog IDs
            return name switch
            {
                "Method" => 1935,          // MethodPublic
                "Field" => 2157,           // FieldPublic
                "Class" => 548,            // ClassPublic
                "Parameter" => 3214,       // Parameter
                "Snippet" => 3478,         // Snippet
                "Namespace" => 2972,       // Namespace
                "Comment" => 614,          // CommentCode
                "Document" => 1087,        // Document
                "IntelliSense" => 1804,    // IntelliSense
                _ => 1804                  // Default to IntelliSense icon
            };
        }
    }
}