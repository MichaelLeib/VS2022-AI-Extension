using System;

namespace OllamaAssistant.Models
{
    /// <summary>
    /// Represents a code completion suggestion from the AI
    /// </summary>
    public class CodeSuggestion
    {
        /// <summary>
        /// The suggested text to insert
        /// </summary>
        public string Text { get; set; }

        /// <summary>
        /// The position where the text should be inserted
        /// </summary>
        public int InsertionPoint { get; set; }

        /// <summary>
        /// Confidence score for this suggestion (0.0 to 1.0)
        /// </summary>
        public double Confidence { get; set; }

        /// <summary>
        /// The type of suggestion
        /// </summary>
        public SuggestionType Type { get; set; }

        /// <summary>
        /// Additional description or documentation for the suggestion
        /// </summary>
        public string Description { get; set; }

        /// <summary>
        /// The range of text that this suggestion would replace (if any)
        /// </summary>
        public TextRange ReplacementRange { get; set; }

        /// <summary>
        /// Whether this suggestion includes placeholders for user input
        /// </summary>
        public bool HasPlaceholders { get; set; }

        /// <summary>
        /// The source context that led to this suggestion
        /// </summary>
        public string SourceContext { get; set; }

        /// <summary>
        /// Priority for ordering when multiple suggestions are available
        /// </summary>
        public int Priority { get; set; }

        /// <summary>
        /// The programming language of the suggestion
        /// </summary>
        public string Language { get; set; }

        /// <summary>
        /// The metadata of the model
        /// </summary>
        public object Metadata { get; set; }

        /// <summary>
        /// Whether this is a partial suggestion being streamed
        /// </summary>
        public bool IsPartial { get; set; }

        /// <summary>
        /// Processing time in milliseconds for performance tracking
        /// </summary>
        public long ProcessingTime { get; set; }

        public CodeSuggestion()
        {
            Type = SuggestionType.General;
            Confidence = 0.0;
            Priority = 0;
        }

        /// <summary>
        /// Determines if this suggestion should be shown based on confidence threshold
        /// </summary>
        public bool ShouldShow(double minimumConfidence = 0.5)
        {
            return Confidence >= minimumConfidence && !string.IsNullOrWhiteSpace(Text);
        }
    }

    /// <summary>
    /// Types of code suggestions
    /// </summary>
    public enum SuggestionType
    {
        /// <summary>
        /// General code completion
        /// </summary>
        General,

        /// <summary>
        /// Method or function completion
        /// </summary>
        Method,

        /// <summary>
        /// Variable or property completion
        /// </summary>
        Variable,

        /// <summary>
        /// Comment completion
        /// </summary>
        Comment,

        /// <summary>
        /// Import or using statement
        /// </summary>
        Import,

        /// <summary>
        /// Class or type completion
        /// </summary>
        Type,

        /// <summary>
        /// Code snippet or template
        /// </summary>
        Snippet,

        /// <summary>
        /// Parameter hints
        /// </summary>
        Parameter,

        /// <summary>
        /// Documentation completion
        /// </summary>
        Documentation
    }

    /// <summary>
    /// Represents a range of text in the editor
    /// </summary>
    public class TextRange
    {
        /// <summary>
        /// Starting position of the range
        /// </summary>
        public int Start { get; set; }

        /// <summary>
        /// Ending position of the range
        /// </summary>
        public int End { get; set; }

        /// <summary>
        /// Length of the range
        /// </summary>
        public int Length => End - Start;

        public TextRange(int start, int end)
        {
            Start = start;
            End = end;
        }
    }
}