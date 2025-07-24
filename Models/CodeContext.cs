using System;
using System.Collections.Generic;

namespace OllamaAssistant.Models
{
    /// <summary>
    /// Represents the code context around the current cursor position
    /// </summary>
    public class CodeContext
    {
        /// <summary>
        /// The full path of the file being edited
        /// </summary>
        public string FileName { get; set; }

        /// <summary>
        /// The programming language of the file
        /// </summary>
        public string Language { get; set; }

        /// <summary>
        /// Lines of code before the cursor position
        /// </summary>
        public string[] PrecedingLines { get; set; }

        /// <summary>
        /// The current line where the cursor is positioned
        /// </summary>
        public string CurrentLine { get; set; }

        /// <summary>
        /// Lines of code after the cursor position
        /// </summary>
        public string[] FollowingLines { get; set; }

        /// <summary>
        /// The character position of the cursor within the current line
        /// </summary>
        public int CaretPosition { get; set; }

        /// <summary>
        /// The line number of the cursor (1-based)
        /// </summary>
        public int LineNumber { get; set; }

        /// <summary>
        /// The current scope (method, class, namespace) where the cursor is located
        /// </summary>
        public string CurrentScope { get; set; }

        /// <summary>
        /// Information about the current indentation
        /// </summary>
        public IndentationInfo Indentation { get; set; }

        /// <summary>
        /// History of recent cursor positions across files
        /// </summary>
        public List<CursorHistoryEntry> CursorHistory { get; set; }

        /// <summary>
        /// The text that is currently selected (if any)
        /// </summary>
        public string SelectedText { get; set; }

        /// <summary>
        /// The project name or workspace name
        /// </summary>
        public string ProjectName { get; set; }

        /// <summary>
        /// Related files that might provide additional context
        /// </summary>
        public List<string> RelatedFiles { get; set; }

        /// <summary>
        /// Semantic information about the current position (classes, methods, etc.)
        /// </summary>
        public SemanticInfo SemanticContext { get; set; }

        /// <summary>
        /// Timestamp when the context was captured
        /// </summary>
        public DateTime CaptureTime { get; set; }

        /// <summary>
        /// The type of operation being performed (typing, navigation, etc.)
        /// </summary>
        public ContextCaptureReason CaptureReason { get; set; }

        public CodeContext()
        {
            PrecedingLines = Array.Empty<string>();
            FollowingLines = Array.Empty<string>();
            CursorHistory = new List<CursorHistoryEntry>();
            RelatedFiles = new List<string>();
            Indentation = new IndentationInfo();
            SemanticContext = new SemanticInfo();
            CaptureTime = DateTime.Now;
            CaptureReason = ContextCaptureReason.Unknown;
        }
    }

    /// <summary>
    /// Information about code indentation at the cursor position
    /// </summary>
    public class IndentationInfo
    {
        /// <summary>
        /// The indentation level (number of indents)
        /// </summary>
        public int Level { get; set; }

        /// <summary>
        /// Whether spaces or tabs are used
        /// </summary>
        public bool UsesSpaces { get; set; }

        /// <summary>
        /// Number of spaces per indent (if spaces are used)
        /// </summary>
        public int SpacesPerIndent { get; set; }

        /// <summary>
        /// The actual indentation string for the current line
        /// </summary>
        public string IndentString { get; set; }

        public IndentationInfo()
        {
            UsesSpaces = true;
            SpacesPerIndent = 4;
            IndentString = string.Empty;
        }
    }

    /// <summary>
    /// Semantic information about the code context
    /// </summary>
    public class SemanticInfo
    {
        /// <summary>
        /// The current namespace
        /// </summary>
        public string Namespace { get; set; }

        /// <summary>
        /// The current class name
        /// </summary>
        public string ClassName { get; set; }

        /// <summary>
        /// The current method name
        /// </summary>
        public string MethodName { get; set; }

        /// <summary>
        /// Local variables in scope
        /// </summary>
        public List<VariableInfo> LocalVariables { get; set; }

        /// <summary>
        /// Using statements or imports
        /// </summary>
        public List<string> Imports { get; set; }

        /// <summary>
        /// The current code block type (if, for, while, etc.)
        /// </summary>
        public string CurrentBlock { get; set; }

        public SemanticInfo()
        {
            LocalVariables = new List<VariableInfo>();
            Imports = new List<string>();
        }
    }

    /// <summary>
    /// Information about a variable in scope
    /// </summary>
    public class VariableInfo
    {
        /// <summary>
        /// Variable name
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// Variable type
        /// </summary>
        public string Type { get; set; }

        /// <summary>
        /// Whether the variable is a parameter
        /// </summary>
        public bool IsParameter { get; set; }

        /// <summary>
        /// Line where variable was declared
        /// </summary>
        public int DeclarationLine { get; set; }
    }

    /// <summary>
    /// Reasons for capturing context
    /// </summary>
    public enum ContextCaptureReason
    {
        /// <summary>
        /// Unknown or unspecified reason
        /// </summary>
        Unknown,

        /// <summary>
        /// User is typing
        /// </summary>
        Typing,

        /// <summary>
        /// Cursor position changed
        /// </summary>
        CaretMoved,

        /// <summary>
        /// Manual request for suggestions
        /// </summary>
        ManualRequest,

        /// <summary>
        /// File was opened
        /// </summary>
        FileOpened,

        /// <summary>
        /// File was saved
        /// </summary>
        FileSaved,

        /// <summary>
        /// Triggering jump recommendation
        /// </summary>
        JumpAnalysis,

        /// <summary>
        /// Background analysis
        /// </summary>
        BackgroundAnalysis
    }
}