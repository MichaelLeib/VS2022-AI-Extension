using System;
using Microsoft.VisualStudio.Text.Editor;

namespace OllamaAssistant.Services.Interfaces
{
    /// <summary>
    /// Interface for cursor history integration with Visual Studio
    /// </summary>
    public interface ICursorHistoryIntegration : IDisposable
    {
        /// <summary>
        /// Starts tracking cursor positions for a text view
        /// </summary>
        void StartTrackingTextView(IWpfTextView textView, string filePath);

        /// <summary>
        /// Stops tracking cursor positions for a text view
        /// </summary> 
        void StopTrackingTextView(IWpfTextView textView);

        /// <summary>
        /// Handles solution closing events to clear history
        /// </summary>
        void OnSolutionClosing();

        /// <summary>
        /// Handles project unloading to clear file-specific history
        /// </summary>
        void OnProjectUnloading(string projectPath);

        /// <summary>
        /// Handles file deletion events
        /// </summary>
        void OnFileDeleted(string filePath);

        /// <summary>
        /// Handles file rename events
        /// </summary>
        void OnFileRenamed(string oldPath, string newPath);
    }
}