using System;
using System.Collections.Generic;

namespace OllamaAssistant.Models
{
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
}