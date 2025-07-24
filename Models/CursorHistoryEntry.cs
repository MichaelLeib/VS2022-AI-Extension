using System;

namespace OllamaAssistant.Models
{
    /// <summary>
    /// Represents a single entry in the cursor position history
    /// </summary>
    public class CursorHistoryEntry
    {
        /// <summary>
        /// The full path of the file
        /// </summary>
        public string FilePath { get; set; }

        /// <summary>
        /// The line number where the cursor was positioned (1-based)
        /// </summary>
        public int LineNumber { get; set; }

        /// <summary>
        /// The column position within the line (0-based)
        /// </summary>
        public int Column { get; set; }

        /// <summary>
        /// A snippet of code around the cursor position for context
        /// </summary>
        public string ContextSnippet { get; set; }

        /// <summary>
        /// When this cursor position was recorded
        /// </summary>
        public DateTime Timestamp { get; set; }

        /// <summary>
        /// The type of change that occurred (e.g., "edit", "navigation", "jump")
        /// </summary>
        public string ChangeType { get; set; }

        /// <summary>
        /// The programming language of the file
        /// </summary>
        public string Language { get; set; }

        /// <summary>
        /// Whether this position resulted from accepting an AI suggestion
        /// </summary>
        public bool FromSuggestion { get; set; }

        public CursorHistoryEntry()
        {
            Timestamp = DateTime.Now;
            ChangeType = "navigation";
        }

        /// <summary>
        /// Creates a formatted string representation of the history entry
        /// </summary>
        public override string ToString()
        {
            var fileName = System.IO.Path.GetFileName(FilePath);
            return $"{fileName}:{LineNumber}:{Column} ({ChangeType} at {Timestamp:HH:mm:ss})";
        }
    }

    /// <summary>
    /// Types of changes that can be tracked in cursor history
    /// </summary>
    public static class ChangeTypes
    {
        public const string Edit = "edit";
        public const string Navigation = "navigation";
        public const string Jump = "jump";
        public const string FileSwitch = "file_switch";
        public const string SuggestionAccepted = "suggestion_accepted";
    }
}