using System;
using Microsoft.VisualStudio.Text;

namespace OllamaAssistant.Models.Events
{
    /// <summary>
    /// Event args for caret position change events
    /// </summary>
    public class CaretPositionChangedEventArgs : EventArgs
    {
        public SnapshotPoint OldPosition { get; set; }
        public SnapshotPoint NewPosition { get; set; }
        public string FilePath { get; set; }
        public bool IsUserInitiated { get; set; }
    }
}