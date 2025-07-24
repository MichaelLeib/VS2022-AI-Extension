using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using OllamaAssistant.Models;

namespace OllamaAssistant.Services.Interfaces
{
    /// <summary>
    /// Service for integrating with Visual Studio's IntelliSense
    /// </summary>
    public interface IIntelliSenseIntegration
    {
        /// <summary>
        /// Shows a code suggestion in the IntelliSense UI
        /// </summary>
        /// <param name="suggestion">The suggestion to display</param>
        Task ShowSuggestionAsync(CodeSuggestion suggestion);

        /// <summary>
        /// Shows multiple suggestions in the IntelliSense UI
        /// </summary>
        /// <param name="suggestions">The suggestions to display</param>
        Task ShowSuggestionsAsync(CodeSuggestion[] suggestions);

        /// <summary>
        /// Dismisses the current suggestion display
        /// </summary>
        void DismissSuggestion();

        /// <summary>
        /// Checks if a suggestion is currently being displayed
        /// </summary>
        bool IsSuggestionActive { get; }

        /// <summary>
        /// Updates the current suggestion with new content
        /// </summary>
        /// <param name="suggestion">The updated suggestion</param>
        Task UpdateSuggestionAsync(CodeSuggestion suggestion);

        /// <summary>
        /// Shows a streaming suggestion that updates progressively
        /// </summary>
        /// <param name="suggestionStream">The stream of partial suggestions</param>
        Task ShowStreamingSuggestionAsync(IAsyncEnumerable<CodeSuggestion> suggestionStream);

        /// <summary>
        /// Fired when a suggestion is accepted by the user
        /// </summary>
        event EventHandler<SuggestionAcceptedEventArgs> SuggestionAccepted;

        /// <summary>
        /// Fired when a suggestion is dismissed by the user
        /// </summary>
        event EventHandler<SuggestionDismissedEventArgs> SuggestionDismissed;

        /// <summary>
        /// Configures the appearance of suggestions
        /// </summary>
        /// <param name="options">Display options for suggestions</param>
        void ConfigureDisplay(SuggestionDisplayOptions options);
    }

    /// <summary>
    /// Event args when a suggestion is accepted
    /// </summary>
    public class SuggestionAcceptedEventArgs : EventArgs
    {
        /// <summary>
        /// The suggestion that was accepted
        /// </summary>
        public CodeSuggestion Suggestion { get; set; }

        /// <summary>
        /// The position where the suggestion was inserted
        /// </summary>
        public int InsertionPosition { get; set; }

        /// <summary>
        /// Whether the user modified the suggestion before accepting
        /// </summary>
        public bool WasModified { get; set; }

        /// <summary>
        /// The actual text that was inserted (may differ from suggestion)
        /// </summary>
        public string InsertedText { get; set; }
    }

    /// <summary>
    /// Event args when a suggestion is dismissed
    /// </summary>
    public class SuggestionDismissedEventArgs : EventArgs
    {
        /// <summary>
        /// The suggestion that was dismissed
        /// </summary>
        public CodeSuggestion Suggestion { get; set; }

        /// <summary>
        /// The reason for dismissal
        /// </summary>
        public DismissalReason Reason { get; set; }

        /// <summary>
        /// How long the suggestion was displayed (in milliseconds)
        /// </summary>
        public int DisplayDuration { get; set; }
    }

    /// <summary>
    /// Reasons why a suggestion might be dismissed
    /// </summary>
    public enum DismissalReason
    {
        /// <summary>
        /// User explicitly dismissed (e.g., pressed Escape)
        /// </summary>
        UserDismissed,

        /// <summary>
        /// User continued typing without accepting
        /// </summary>
        ContinuedTyping,

        /// <summary>
        /// User navigated away
        /// </summary>
        NavigatedAway,

        /// <summary>
        /// Suggestion timed out
        /// </summary>
        Timeout,

        /// <summary>
        /// Replaced by a new suggestion
        /// </summary>
        Replaced
    }

    /// <summary>
    /// Options for how suggestions are displayed
    /// </summary>
    public class SuggestionDisplayOptions
    {
        /// <summary>
        /// Whether to show suggestion descriptions
        /// </summary>
        public bool ShowDescriptions { get; set; } = true;

        /// <summary>
        /// Whether to show confidence scores
        /// </summary>
        public bool ShowConfidence { get; set; } = false;

        /// <summary>
        /// Maximum number of suggestions to show at once
        /// </summary>
        public int MaxSuggestions { get; set; } = 5;

        /// <summary>
        /// Timeout in milliseconds before auto-dismissing
        /// </summary>
        public int AutoDismissTimeout { get; set; } = 0; // 0 = no auto-dismiss

        /// <summary>
        /// Whether to show inline previews
        /// </summary>
        public bool ShowInlinePreview { get; set; } = true;

        /// <summary>
        /// Minimum confidence score to show suggestions
        /// </summary>
        public double MinimumConfidence { get; set; } = 0.5;
    }
}