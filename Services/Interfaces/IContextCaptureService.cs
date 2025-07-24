using System.Threading.Tasks;
using Microsoft.VisualStudio.Text;
using OllamaAssistant.Models;

namespace OllamaAssistant.Services.Interfaces
{
    /// <summary>
    /// Service for capturing and analyzing code context around the cursor
    /// </summary>
    public interface IContextCaptureService
    {
        /// <summary>
        /// Captures the code context around the specified caret position
        /// </summary>
        /// <param name="caretPosition">The current caret position</param>
        /// <param name="linesUp">Number of lines to capture above the cursor</param>
        /// <param name="linesDown">Number of lines to capture below the cursor</param>
        /// <returns>A CodeContext object containing the captured context</returns>
        Task<CodeContext> CaptureContextAsync(SnapshotPoint caretPosition, int linesUp, int linesDown);

        /// <summary>
        /// Analyzes the code structure to identify jump opportunities
        /// </summary>
        /// <param name="snapshot">The current text snapshot</param>
        /// <param name="caretPosition">The current caret position</param>
        /// <returns>A jump recommendation if one is identified</returns>
        Task<JumpRecommendation> AnalyzeJumpOpportunitiesAsync(ITextSnapshot snapshot, SnapshotPoint caretPosition);

        /// <summary>
        /// Captures a context snippet for cursor history
        /// </summary>
        /// <param name="caretPosition">The cursor position</param>
        /// <param name="contextSize">Number of characters to capture around the position</param>
        /// <returns>A string snippet of code around the position</returns>
        Task<string> CaptureContextSnippetAsync(SnapshotPoint caretPosition, int contextSize = 100);

        /// <summary>
        /// Analyzes the current scope (method, class, etc.) at the cursor position
        /// </summary>
        /// <param name="caretPosition">The cursor position</param>
        /// <returns>A string describing the current scope</returns>
        Task<string> AnalyzeCurrentScopeAsync(SnapshotPoint caretPosition);

        /// <summary>
        /// Detects the indentation settings at the current position
        /// </summary>
        /// <param name="snapshot">The text snapshot</param>
        /// <param name="caretPosition">The cursor position</param>
        /// <returns>Information about the indentation</returns>
        Task<IndentationInfo> DetectIndentationAsync(ITextSnapshot snapshot, SnapshotPoint caretPosition);

        /// <summary>
        /// Determines if the cursor is in a comment
        /// </summary>
        /// <param name="caretPosition">The cursor position</param>
        /// <returns>True if the cursor is in a comment</returns>
        Task<bool> IsInCommentAsync(SnapshotPoint caretPosition);

        /// <summary>
        /// Determines if the cursor is in a string literal
        /// </summary>
        /// <param name="caretPosition">The cursor position</param>
        /// <returns>True if the cursor is in a string</returns>
        Task<bool> IsInStringAsync(SnapshotPoint caretPosition);
    }
}