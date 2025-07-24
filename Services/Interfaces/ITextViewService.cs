using System;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using OllamaAssistant.Models.Events;

namespace OllamaAssistant.Services.Interfaces
{
    /// <summary>
    /// Service for interacting with Visual Studio text views and editors
    /// </summary>
    public interface ITextViewService
    {
        /// <summary>
        /// Fired when text changes in the active editor
        /// </summary>
        event EventHandler<TextChangedEventArgs> TextChanged;

        /// <summary>
        /// Fired when the caret position changes
        /// </summary>
        event EventHandler<CaretPositionChangedEventArgs> CaretPositionChanged;

        /// <summary>
        /// Gets the surrounding context around the current caret position
        /// </summary>
        /// <param name="linesUp">Number of lines to capture above the cursor</param>
        /// <param name="linesDown">Number of lines to capture below the cursor</param>
        /// <returns>The text content around the cursor</returns>
        Task<string> GetSurroundingContextAsync(int linesUp, int linesDown);

        /// <summary>
        /// Inserts text at the specified position
        /// </summary>
        /// <param name="text">Text to insert</param>
        /// <param name="position">Position to insert at</param>
        Task InsertTextAsync(string text, int position);

        /// <summary>
        /// Gets the current caret position
        /// </summary>
        SnapshotPoint GetCaretPosition();

        /// <summary>
        /// Gets the currently active text view
        /// </summary>
        IWpfTextView GetActiveTextView();

        /// <summary>
        /// Gets the file path of the current document
        /// </summary>
        string GetCurrentFilePath();

        /// <summary>
        /// Gets the programming language of the current document
        /// </summary>
        string GetCurrentLanguage();

        /// <summary>
        /// Moves the caret to a specific position
        /// </summary>
        /// <param name="line">Target line number (1-based)</param>
        /// <param name="column">Target column (0-based)</param>
        Task MoveCaretToAsync(int line, int column);

        /// <summary>
        /// Gets the current line number (1-based)
        /// </summary>
        int GetCurrentLineNumber();

        /// <summary>
        /// Gets the current column position (0-based)
        /// </summary>
        int GetCurrentColumn();

        /// <summary>
        /// Gets the current cursor position as a CursorPosition object
        /// </summary>
        CursorPosition GetCurrentPosition();
    }

    /// <summary>
    /// Event args for text change events
    /// </summary>
    public class TextChangedEventArgs : EventArgs
    {
        public ITextSnapshot Before { get; set; }
        public ITextSnapshot After { get; set; }
        public ITextChange Change { get; set; }
        public string FilePath { get; set; }
    }


}