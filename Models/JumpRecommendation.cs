using System;

namespace OllamaAssistant.Models
{
    /// <summary>
    /// Represents a recommendation for where the cursor should jump next
    /// </summary>
    public class JumpRecommendation
    {
        /// <summary>
        /// The target line number to jump to (1-based)
        /// </summary>
        public int TargetLine { get; set; }

        /// <summary>
        /// The target column position within the line (0-based)
        /// </summary>
        public int TargetColumn { get; set; }

        /// <summary>
        /// The direction of the jump relative to current position
        /// </summary>
        public JumpDirection Direction { get; set; }

        /// <summary>
        /// A human-readable reason for the recommendation
        /// </summary>
        public string Reason { get; set; }

        /// <summary>
        /// Confidence score for this recommendation (0.0 to 1.0)
        /// </summary>
        public double Confidence { get; set; }

        /// <summary>
        /// The type of jump being recommended
        /// </summary>
        public JumpType Type { get; set; }

        /// <summary>
        /// Preview text of the target location
        /// </summary>
        public string TargetPreview { get; set; }

        /// <summary>
        /// Whether this jump crosses file boundaries
        /// </summary>
        public bool IsCrossFile { get; set; }

        /// <summary>
        /// Target file path if this is a cross-file jump
        /// </summary>
        public string TargetFilePath { get; set; }

        public JumpRecommendation()
        {
            Direction = JumpDirection.None;
            Type = JumpType.NextLogicalPosition;
            Confidence = 0.0;
        }

        /// <summary>
        /// Determines if this recommendation should be shown based on confidence threshold
        /// </summary>
        public bool ShouldShow(double minimumConfidence = 0.7)
        {
            return Confidence >= minimumConfidence && Direction != JumpDirection.None;
        }
    }

    /// <summary>
    /// Direction of the jump relative to current cursor position
    /// </summary>
    public enum JumpDirection
    {
        /// <summary>
        /// No jump recommended
        /// </summary>
        None,

        /// <summary>
        /// Jump to a position above current cursor
        /// </summary>
        Up,

        /// <summary>
        /// Jump to a position below current cursor
        /// </summary>
        Down
    }

    /// <summary>
    /// Types of jump recommendations
    /// </summary>
    public enum JumpType
    {
        /// <summary>
        /// Jump to the next logical code position
        /// </summary>
        NextLogicalPosition,

        /// <summary>
        /// Jump to a method or function definition
        /// </summary>
        MethodDefinition,

        /// <summary>
        /// Jump to a variable declaration
        /// </summary>
        VariableDeclaration,

        /// <summary>
        /// Jump to the end of a code block
        /// </summary>
        BlockEnd,

        /// <summary>
        /// Jump to the beginning of a code block
        /// </summary>
        BlockStart,

        /// <summary>
        /// Jump to a related file (e.g., header/implementation)
        /// </summary>
        RelatedFile,

        /// <summary>
        /// Jump to an error or warning location
        /// </summary>
        ErrorLocation,

        /// <summary>
        /// Jump to complete a pattern (e.g., closing brace)
        /// </summary>
        PatternCompletion
    }
}