using System;
using System.Collections.Generic;

namespace OllamaAssistant.Models
{
    /// <summary>
    /// Represents a cursor position in the editor
    /// </summary>
    public class CursorPosition
    {
        /// <summary>
        /// Line number (1-based)
        /// </summary>
        public int Line { get; set; }

        /// <summary>
        /// Column number (0-based)
        /// </summary>
        public int Column { get; set; }

        /// <summary>
        /// File path
        /// </summary>
        public string FilePath { get; set; } = string.Empty;

        /// <summary>
        /// Timestamp when the position was recorded
        /// </summary>
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Additional context information
        /// </summary>
        public string Context { get; set; } = string.Empty;
    }

    /// <summary>
    /// Represents surrounding code context
    /// </summary>
    public class SurroundingCodeContext
    {
        /// <summary>
        /// Lines above the current line
        /// </summary>
        public string[] LinesAbove { get; set; } = Array.Empty<string>();

        /// <summary>
        /// Lines below the current line
        /// </summary>
        public string[] LinesBelow { get; set; } = Array.Empty<string>();

        /// <summary>
        /// Indentation style information
        /// </summary>
        public IndentationInfo IndentationInfo { get; set; } = new IndentationInfo();
    }

    /// <summary>
    /// Information about indentation style
    /// </summary>
    public class IndentationInfo
    {
        /// <summary>
        /// Whether the code uses spaces for indentation
        /// </summary>
        public bool UsesSpaces { get; set; } = true;

        /// <summary>
        /// Size of indentation (spaces or tab width)
        /// </summary>
        public int Size { get; set; } = 4;
    }

    /// <summary>
    /// Represents project context information
    /// </summary>
    public class ProjectContext
    {
        /// <summary>
        /// Project name
        /// </summary>
        public string ProjectName { get; set; } = string.Empty;

        /// <summary>
        /// Project type (e.g., "Console Application", "Web Application")
        /// </summary>
        public string ProjectType { get; set; } = string.Empty;

        /// <summary>
        /// Target framework
        /// </summary>
        public string TargetFramework { get; set; } = string.Empty;

        /// <summary>
        /// Project dependencies
        /// </summary>
        public List<string> Dependencies { get; set; } = new List<string>();
    }

    /// <summary>
    /// Jump recommendation from AI
    /// </summary>
    public class JumpRecommendation
    {
        /// <summary>
        /// Direction to jump
        /// </summary>
        public JumpDirection Direction { get; set; }

        /// <summary>
        /// Target line number
        /// </summary>
        public int TargetLine { get; set; }

        /// <summary>
        /// Target column number
        /// </summary>
        public int TargetColumn { get; set; }

        /// <summary>
        /// Confidence score (0.0 to 1.0)
        /// </summary>
        public double Confidence { get; set; }

        /// <summary>
        /// Reason for the jump recommendation
        /// </summary>
        public string Reason { get; set; } = string.Empty;

        /// <summary>
        /// File path for the jump target
        /// </summary>
        public string FilePath { get; set; } = string.Empty;
    }

    /// <summary>
    /// Direction for cursor jumps
    /// </summary>
    public enum JumpDirection
    {
        /// <summary>
        /// No jump recommended
        /// </summary>
        None,

        /// <summary>
        /// Jump up in the file
        /// </summary>
        Up,

        /// <summary>
        /// Jump down in the file
        /// </summary>
        Down,

        /// <summary>
        /// Jump to beginning of line
        /// </summary>
        LineStart,

        /// <summary>
        /// Jump to end of line
        /// </summary>
        LineEnd,

        /// <summary>
        /// Jump to different file
        /// </summary>
        File,

        /// <summary>
        /// Jump to method definition
        /// </summary>
        MethodDefinition,

        /// <summary>
        /// Jump to method implementation
        /// </summary>
        MethodImplementation
    }

    /// <summary>
    /// Model information from Ollama
    /// </summary>
    public class ModelInfo
    {
        /// <summary>
        /// Model name
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Model size (e.g., "7B", "13B")
        /// </summary>
        public string Size { get; set; } = string.Empty;

        /// <summary>
        /// Model format
        /// </summary>
        public string Format { get; set; } = string.Empty;

        /// <summary>
        /// Model family
        /// </summary>
        public string Family { get; set; } = string.Empty;

        /// <summary>
        /// Model families
        /// </summary>
        public string[] Families { get; set; } = Array.Empty<string>();

        /// <summary>
        /// Parameter size
        /// </summary>
        public string ParameterSize { get; set; } = string.Empty;

        /// <summary>
        /// Quantization level
        /// </summary>
        public string QuantizationLevel { get; set; } = string.Empty;

        /// <summary>
        /// When the model was last modified
        /// </summary>
        public DateTime ModifiedAt { get; set; }

        /// <summary>
        /// Detailed model information
        /// </summary>
        public ModelDetails Details { get; set; } = new ModelDetails();

        /// <summary>
        /// Whether this model supports code completion
        /// </summary>
        public bool SupportsCodeCompletion => Name.Contains("code") || Name.Contains("coder");
    }

    /// <summary>
    /// Detailed model information
    /// </summary>
    public class ModelDetails
    {
        /// <summary>
        /// Parent model name
        /// </summary>
        public string ParentModel { get; set; } = string.Empty;

        /// <summary>
        /// Model format
        /// </summary>
        public string Format { get; set; } = string.Empty;

        /// <summary>
        /// Model family
        /// </summary>
        public string Family { get; set; } = string.Empty;

        /// <summary>
        /// Model families
        /// </summary>
        public string[] Families { get; set; } = Array.Empty<string>();

        /// <summary>
        /// Parameter size
        /// </summary>
        public string ParameterSize { get; set; } = string.Empty;

        /// <summary>
        /// Quantization level
        /// </summary>
        public string QuantizationLevel { get; set; } = string.Empty;
    }

    /// <summary>
    /// Response from Ollama API
    /// </summary>
    public class OllamaResponse
    {
        /// <summary>
        /// Model used for the response
        /// </summary>
        public string Model { get; set; } = string.Empty;

        /// <summary>
        /// Response text
        /// </summary>
        public string Response { get; set; } = string.Empty;

        /// <summary>
        /// Whether the response is complete
        /// </summary>
        public bool Done { get; set; }

        /// <summary>
        /// Context tokens
        /// </summary>
        public int[] Context { get; set; } = Array.Empty<int>();

        /// <summary>
        /// Total duration in nanoseconds
        /// </summary>
        public long TotalDuration { get; set; }

        /// <summary>
        /// Load duration in nanoseconds
        /// </summary>
        public long LoadDuration { get; set; }

        /// <summary>
        /// Prompt evaluation count
        /// </summary>
        public int PromptEvalCount { get; set; }

        /// <summary>
        /// Prompt evaluation duration in nanoseconds
        /// </summary>
        public long PromptEvalDuration { get; set; }

        /// <summary>
        /// Evaluation count
        /// </summary>
        public int EvalCount { get; set; }

        /// <summary>
        /// Evaluation duration in nanoseconds
        /// </summary>
        public long EvalDuration { get; set; }
    }

    /// <summary>
    /// Cursor history entry
    /// </summary>
    public class CursorHistoryEntry
    {
        /// <summary>
        /// File path
        /// </summary>
        public string FilePath { get; set; } = string.Empty;

        /// <summary>
        /// Line number
        /// </summary>
        public int LineNumber { get; set; }

        /// <summary>
        /// Column number
        /// </summary>
        public int ColumnNumber { get; set; }

        /// <summary>
        /// Timestamp
        /// </summary>
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Context around the cursor
        /// </summary>
        public string Context { get; set; } = string.Empty;

        /// <summary>
        /// Project name
        /// </summary>
        public string ProjectName { get; set; } = string.Empty;

        /// <summary>
        /// Additional metadata
        /// </summary>
        public Dictionary<string, object> Metadata { get; set; } = new Dictionary<string, object>();
    }

    /// <summary>
    /// History statistics
    /// </summary>
    public class HistoryStatistics
    {
        /// <summary>
        /// Total number of entries
        /// </summary>
        public int TotalEntries { get; set; }

        /// <summary>
        /// Number of unique files
        /// </summary>
        public int UniqueFiles { get; set; }

        /// <summary>
        /// Average entries per file
        /// </summary>
        public double AverageEntriesPerFile { get; set; }

        /// <summary>
        /// Most active file
        /// </summary>
        public string MostActiveFile { get; set; } = string.Empty;

        /// <summary>
        /// File activity distribution
        /// </summary>
        public Dictionary<string, int> FileActivity { get; set; } = new Dictionary<string, int>();
    }

    /// <summary>
    /// Settings changed event arguments
    /// </summary>
    public class SettingsChangedEventArgs : EventArgs
    {
        /// <summary>
        /// Name of the setting that changed
        /// </summary>
        public string SettingName { get; set; } = string.Empty;

        /// <summary>
        /// New value
        /// </summary>
        public object NewValue { get; set; } = null!;

        /// <summary>
        /// Old value
        /// </summary>
        public object OldValue { get; set; } = null!;
    }
}