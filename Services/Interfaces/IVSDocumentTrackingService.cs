using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Text.Editor;

namespace OllamaAssistant.Services.Interfaces
{
    /// <summary>
    /// Interface for tracking documents and text views in Visual Studio
    /// </summary>
    public interface IVSDocumentTrackingService : IDisposable
    {
        /// <summary>
        /// Gets the currently active document
        /// </summary>
        DocumentInfo ActiveDocument { get; }

        /// <summary>
        /// Event raised when the active document changes
        /// </summary>
        event EventHandler<DocumentChangedEventArgs> ActiveDocumentChanged;

        /// <summary>
        /// Event raised when a document is opened
        /// </summary>
        event EventHandler<DocumentEventArgs> DocumentOpened;

        /// <summary>
        /// Event raised when a document is closed
        /// </summary>
        event EventHandler<DocumentEventArgs> DocumentClosed;

        /// <summary>
        /// Event raised when a document is saved
        /// </summary>
        event EventHandler<DocumentEventArgs> DocumentSaved;

        /// <summary>
        /// Initializes the document tracking service
        /// </summary>
        Task InitializeAsync();

        /// <summary>
        /// Gets all currently tracked documents
        /// </summary>
        IEnumerable<DocumentInfo> GetAllDocuments();

        /// <summary>
        /// Gets a document by file path
        /// </summary>
        DocumentInfo GetDocument(string filePath);

        /// <summary>
        /// Gets text views for a specific file
        /// </summary>
        IEnumerable<IWpfTextView> GetTextViewsForFile(string filePath);

        /// <summary>
        /// Registers a text view for tracking
        /// </summary>
        void RegisterTextView(IWpfTextView textView, string filePath);

        /// <summary>
        /// Unregisters a text view from tracking
        /// </summary>
        void UnregisterTextView(IWpfTextView textView, string filePath);
    }

    /// <summary>
    /// Information about a document in Visual Studio
    /// </summary>
    public class DocumentInfo
    {
        /// <summary>
        /// Gets or sets the document cookie from the running document table
        /// </summary>
        public uint Cookie { get; set; }

        /// <summary>
        /// Gets or sets the file path
        /// </summary>
        public string FilePath { get; set; }

        /// <summary>
        /// Gets or sets the VS hierarchy
        /// </summary>
        public Microsoft.VisualStudio.Shell.Interop.IVsHierarchy Hierarchy { get; set; }

        /// <summary>
        /// Gets or sets the item ID in the hierarchy
        /// </summary>
        public uint ItemId { get; set; }

        /// <summary>
        /// Gets or sets whether the document has unsaved changes
        /// </summary>
        public bool IsDirty { get; set; }

        /// <summary>
        /// Gets or sets when the document was last accessed
        /// </summary>
        public DateTime LastAccessed { get; set; }

        /// <summary>
        /// Gets or sets when the document was last saved
        /// </summary>
        public DateTime LastSaved { get; set; }

        /// <summary>
        /// Gets the file name without path
        /// </summary>
        public string FileName => System.IO.Path.GetFileName(FilePath);

        /// <summary>
        /// Gets the file extension
        /// </summary>
        public string Extension => System.IO.Path.GetExtension(FilePath);

        /// <summary>
        /// Gets whether this is a code file
        /// </summary>
        public bool IsCodeFile
        {
            get
            {
                var ext = Extension?.ToLowerInvariant();
                switch (ext)
                {
                    case ".cs":
                    case ".vb":
                    case ".cpp":
                    case ".c":
                    case ".h":
                    case ".hpp":
                    case ".js":
                    case ".ts":
                    case ".py":
                    case ".java":
                    case ".php":
                    case ".go":
                    case ".rs":
                    case ".kt":
                    case ".swift":
                    case ".rb":
                    case ".fs":
                    case ".fsx":
                    case ".ml":
                    case ".scala":
                    case ".clj":
                    case ".hs":
                    case ".elm":
                    case ".dart":
                    case ".lua":
                    case ".r":
                    case ".sql":
                    case ".xml":
                    case ".json":
                    case ".yaml":
                    case ".yml":
                        return true;
                    default:
                        return false;
                }
            }
        }
    }

    /// <summary>
    /// Event arguments for document events
    /// </summary>
    public class DocumentEventArgs : EventArgs
    {
        public DocumentEventArgs(DocumentInfo document)
        {
            Document = document;
        }

        /// <summary>
        /// Gets the document involved in the event
        /// </summary>
        public DocumentInfo Document { get; }
    }

    /// <summary>
    /// Event arguments for active document changes
    /// </summary>
    public class DocumentChangedEventArgs : EventArgs
    {
        public DocumentChangedEventArgs(DocumentInfo oldDocument, DocumentInfo newDocument)
        {
            OldDocument = oldDocument;
            NewDocument = newDocument;
        }

        /// <summary>
        /// Gets the previously active document
        /// </summary>
        public DocumentInfo OldDocument { get; }

        /// <summary>
        /// Gets the newly active document
        /// </summary>
        public DocumentInfo NewDocument { get; }
    }
}