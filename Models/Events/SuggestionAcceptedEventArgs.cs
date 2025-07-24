using System;
using OllamaAssistant.Models;

namespace OllamaAssistant.Models.Events
{
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
}