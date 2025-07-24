using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Text;
using OllamaAssistant.Models;

namespace OllamaAssistant.Services.Interfaces
{
    /// <summary>
    /// Service for tracking cursor position history across files
    /// </summary>
    public interface ICursorHistoryService
    {
        /// <summary>
        /// Records a cursor position in the history
        /// </summary>
        /// <param name="filePath">The file path where the cursor is located</param>
        /// <param name="position">The cursor position</param>
        /// <param name="contextSnippet">A snippet of code around the position</param>
        void RecordCursorPosition(string filePath, SnapshotPoint position, string contextSnippet);

        /// <summary>
        /// Records a cursor position with additional metadata
        /// </summary>
        /// <param name="entry">The history entry to record</param>
        void RecordCursorPosition(CursorHistoryEntry entry);

        /// <summary>
        /// Gets the most recent cursor history entries
        /// </summary>
        /// <param name="maxEntries">Maximum number of entries to return</param>
        /// <returns>A collection of recent cursor history entries</returns>
        IEnumerable<CursorHistoryEntry> GetRecentHistory(int maxEntries);

        /// <summary>
        /// Gets cursor history entries related to a specific file
        /// </summary>
        /// <param name="filePath">The file path to filter by</param>
        /// <param name="maxEntries">Maximum number of entries to return</param>
        /// <returns>History entries for the specified file</returns>
        IEnumerable<CursorHistoryEntry> GetFileHistory(string filePath, int maxEntries);

        /// <summary>
        /// Gets cursor history entries that might be relevant to the current context
        /// </summary>
        /// <param name="currentFile">The current file path</param>
        /// <param name="currentPosition">The current cursor position</param>
        /// <param name="maxEntries">Maximum number of entries to return</param>
        /// <returns>Relevant history entries based on context analysis</returns>
        IEnumerable<CursorHistoryEntry> GetRelevantHistory(string currentFile, int currentPosition, int maxEntries);

        /// <summary>
        /// Clears all cursor history
        /// </summary>
        void ClearHistory();

        /// <summary>
        /// Clears history for a specific file
        /// </summary>
        /// <param name="filePath">The file path to clear history for</param>
        void ClearFileHistory(string filePath);

        /// <summary>
        /// Event fired when cursor history changes
        /// </summary>
        event EventHandler<CursorHistoryChangedEventArgs> HistoryChanged;

        /// <summary>
        /// Gets the maximum number of history entries to maintain
        /// </summary>
        int MaxHistoryDepth { get; }

        /// <summary>
        /// Sets the maximum number of history entries to maintain
        /// </summary>
        /// <param name="depth">The maximum depth</param>
        void SetMaxHistoryDepth(int depth);

        /// <summary>
        /// Asynchronously adds a cursor history entry
        /// </summary>
        /// <param name="entry">The history entry to add</param>
        /// <returns>Task representing the async operation</returns>
        Task AddEntryAsync(CursorHistoryEntry entry);

        /// <summary>
        /// Asynchronously clears all cursor history
        /// </summary>
        /// <returns>Task representing the async operation</returns>
        Task ClearHistoryAsync();

        /// <summary>
        /// Asynchronously clears history for a specific file
        /// </summary>
        /// <param name="filePath">The file path to clear history for</param>
        /// <returns>Task representing the async operation</returns>
        Task ClearHistoryForFileAsync(string filePath);

        /// <summary>
        /// Asynchronously cleans up file-specific data
        /// </summary>
        /// <param name="filePath">The file path to clean up data for</param>
        /// <returns>Task representing the async operation</returns>
        Task CleanupFileDataAsync(string filePath);
    }

    /// <summary>
    /// Event args for cursor history change events
    /// </summary>
    public class CursorHistoryChangedEventArgs : EventArgs
    {
        /// <summary>
        /// The entry that was added or modified
        /// </summary>
        public CursorHistoryEntry Entry { get; set; }

        /// <summary>
        /// The type of change that occurred
        /// </summary>
        public HistoryChangeType ChangeType { get; set; }

        /// <summary>
        /// The total number of entries in history after the change
        /// </summary>
        public int TotalEntries { get; set; }
    }

    /// <summary>
    /// Types of changes to cursor history
    /// </summary>
    public enum HistoryChangeType
    {
        /// <summary>
        /// A new entry was added
        /// </summary>
        Added,

        /// <summary>
        /// History was cleared
        /// </summary>
        Cleared,

        /// <summary>
        /// An old entry was removed due to size limits
        /// </summary>
        Trimmed
    }
}