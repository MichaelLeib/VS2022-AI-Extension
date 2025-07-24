using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.Text;
using OllamaAssistant.Models;
using OllamaAssistant.Services.Interfaces;

namespace OllamaAssistant.Services.Implementation
{
    /// <summary>
    /// Implementation of cursor history service for tracking positions across files
    /// </summary>
    public class CursorHistoryService : ICursorHistoryService
    {
        private readonly LinkedList<CursorHistoryEntry> _history;
        private readonly object _lockObject;
        private int _maxHistoryDepth;

        public CursorHistoryService(int maxHistoryDepth = 3)
        {
            _history = new LinkedList<CursorHistoryEntry>();
            _lockObject = new object();
            _maxHistoryDepth = maxHistoryDepth;
        }

        #region Properties

        public int MaxHistoryDepth
        {
            get
            {
                lock (_lockObject)
                {
                    return _maxHistoryDepth;
                }
            }
        }

        #endregion

        #region Events

        public event EventHandler<CursorHistoryChangedEventArgs> HistoryChanged;

        #endregion

        #region Public Methods

        public void RecordCursorPosition(string filePath, SnapshotPoint position, string contextSnippet)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                return;

            var entry = new CursorHistoryEntry
            {
                FilePath = filePath,
                LineNumber = position.GetContainingLine().LineNumber + 1, // Convert to 1-based
                Column = position.Position - position.GetContainingLine().Start.Position,
                ContextSnippet = contextSnippet ?? string.Empty,
                Timestamp = DateTime.Now,
                ChangeType = ChangeTypes.Navigation,
                Language = DetectLanguageFromFilePath(filePath)
            };

            RecordCursorPosition(entry);
        }

        public void RecordCursorPosition(CursorHistoryEntry entry)
        {
            if (entry == null)
                return;

            lock (_lockObject)
            {
                // Check if this is a duplicate of the most recent entry
                if (_history.Count > 0 && IsDuplicateEntry(_history.First.Value, entry))
                {
                    // Update the timestamp of the existing entry instead of adding a duplicate
                    _history.First.Value.Timestamp = entry.Timestamp;
                    return;
                }

                // Add the new entry at the beginning
                _history.AddFirst(entry);

                // Trim to max depth
                while (_history.Count > _maxHistoryDepth)
                {
                    _history.RemoveLast();
                }
            }

            // Fire event
            HistoryChanged?.Invoke(this, new CursorHistoryChangedEventArgs
            {
                Entry = entry,
                ChangeType = HistoryChangeType.Added,
                TotalEntries = _history.Count
            });
        }

        public IEnumerable<CursorHistoryEntry> GetRecentHistory(int maxEntries)
        {
            lock (_lockObject)
            {
                return _history.Take(Math.Min(maxEntries, _history.Count)).ToList();
            }
        }

        public IEnumerable<CursorHistoryEntry> GetFileHistory(string filePath, int maxEntries)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                return Enumerable.Empty<CursorHistoryEntry>();

            lock (_lockObject)
            {
                return _history
                    .Where(entry => string.Equals(entry.FilePath, filePath, StringComparison.OrdinalIgnoreCase))
                    .Take(maxEntries)
                    .ToList();
            }
        }

        public IEnumerable<CursorHistoryEntry> GetRelevantHistory(string currentFile, int currentPosition, int maxEntries)
        {
            if (string.IsNullOrWhiteSpace(currentFile))
                return Enumerable.Empty<CursorHistoryEntry>();

            lock (_lockObject)
            {
                var relevantEntries = new List<(CursorHistoryEntry entry, double relevance)>();

                foreach (var entry in _history)
                {
                    var relevance = CalculateRelevanceScore(entry, currentFile, currentPosition);
                    if (relevance > 0.1) // Only include entries with meaningful relevance
                    {
                        relevantEntries.Add((entry, relevance));
                    }
                }

                return relevantEntries
                    .OrderByDescending(x => x.relevance)
                    .ThenByDescending(x => x.entry.Timestamp)
                    .Take(maxEntries)
                    .Select(x => x.entry)
                    .ToList();
            }
        }

        public void ClearHistory()
        {
            lock (_lockObject)
            {
                _history.Clear();
            }

            HistoryChanged?.Invoke(this, new CursorHistoryChangedEventArgs
            {
                Entry = null,
                ChangeType = HistoryChangeType.Cleared,
                TotalEntries = 0
            });
        }

        public void ClearFileHistory(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                return;

            bool removed = false;
            lock (_lockObject)
            {
                var nodesToRemove = new List<LinkedListNode<CursorHistoryEntry>>();
                var node = _history.First;

                while (node != null)
                {
                    if (string.Equals(node.Value.FilePath, filePath, StringComparison.OrdinalIgnoreCase))
                    {
                        nodesToRemove.Add(node);
                        removed = true;
                    }
                    node = node.Next;
                }

                foreach (var nodeToRemove in nodesToRemove)
                {
                    _history.Remove(nodeToRemove);
                }
            }

            if (removed)
            {
                HistoryChanged?.Invoke(this, new CursorHistoryChangedEventArgs
                {
                    Entry = null,
                    ChangeType = HistoryChangeType.Cleared,
                    TotalEntries = _history.Count
                });
            }
        }

        public void SetMaxHistoryDepth(int depth)
        {
            if (depth < 1)
                depth = 1;

            lock (_lockObject)
            {
                _maxHistoryDepth = depth;

                // Trim existing history if necessary
                while (_history.Count > _maxHistoryDepth)
                {
                    _history.RemoveLast();
                }
            }
        }

        #endregion

        #region Private Methods

        private bool IsDuplicateEntry(CursorHistoryEntry existing, CursorHistoryEntry newEntry)
        {
            return string.Equals(existing.FilePath, newEntry.FilePath, StringComparison.OrdinalIgnoreCase) &&
                   existing.LineNumber == newEntry.LineNumber &&
                   Math.Abs(existing.Column - newEntry.Column) <= 2 && // Allow small column differences
                   (DateTime.Now - existing.Timestamp).TotalSeconds < 2; // Within 2 seconds
        }

        private double CalculateRelevanceScore(CursorHistoryEntry entry, string currentFile, int currentPosition)
        {
            double score = 0.0;

            // Same file gets higher relevance
            if (string.Equals(entry.FilePath, currentFile, StringComparison.OrdinalIgnoreCase))
            {
                score += 0.5;

                // Proximity to current position
                var lineDistance = Math.Abs(entry.LineNumber - GetLineFromPosition(currentPosition));
                if (lineDistance == 0)
                    score += 0.3;
                else if (lineDistance <= 5)
                    score += 0.2;
                else if (lineDistance <= 20)
                    score += 0.1;
            }
            else
            {
                // Related files (same directory, similar names, etc.)
                if (AreFilesRelated(entry.FilePath, currentFile))
                {
                    score += 0.3;
                }

                // Same language
                var currentLanguage = DetectLanguageFromFilePath(currentFile);
                if (string.Equals(entry.Language, currentLanguage, StringComparison.OrdinalIgnoreCase))
                {
                    score += 0.1;
                }
            }

            // Recent entries get higher relevance
            var ageInMinutes = (DateTime.Now - entry.Timestamp).TotalMinutes;
            if (ageInMinutes <= 1)
                score += 0.2;
            else if (ageInMinutes <= 5)
                score += 0.1;
            else if (ageInMinutes <= 30)
                score += 0.05;

            // Certain change types are more relevant
            switch (entry.ChangeType)
            {
                case ChangeTypes.Edit:
                    score += 0.2;
                    break;
                case ChangeTypes.SuggestionAccepted:
                    score += 0.15;
                    break;
                case ChangeTypes.Jump:
                    score += 0.1;
                    break;
                case ChangeTypes.FileSwitch:
                    score += 0.05;
                    break;
            }

            return Math.Min(1.0, score); // Cap at 1.0
        }

        private bool AreFilesRelated(string file1, string file2)
        {
            try
            {
                var dir1 = Path.GetDirectoryName(file1);
                var dir2 = Path.GetDirectoryName(file2);
                var name1 = Path.GetFileNameWithoutExtension(file1);
                var name2 = Path.GetFileNameWithoutExtension(file2);

                // Same directory
                if (string.Equals(dir1, dir2, StringComparison.OrdinalIgnoreCase))
                    return true;

                // Similar file names (header/implementation pairs, etc.)
                if (name1.Contains(name2) || name2.Contains(name1))
                    return true;

                // Common patterns like .h/.cpp, .cs/.designer.cs, etc.
                var baseName1 = name1.Split('.')[0];
                var baseName2 = name2.Split('.')[0];
                if (string.Equals(baseName1, baseName2, StringComparison.OrdinalIgnoreCase))
                    return true;

                return false;
            }
            catch
            {
                return false;
            }
        }

        private string DetectLanguageFromFilePath(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                return "unknown";

            var extension = Path.GetExtension(filePath).ToLowerInvariant();
            return extension switch
            {
                ".cs" => "csharp",
                ".cpp" or ".cc" or ".cxx" => "cpp",
                ".c" => "c",
                ".h" or ".hpp" => "c_header",
                ".js" => "javascript",
                ".ts" => "typescript",
                ".py" => "python",
                ".java" => "java",
                ".php" => "php",
                ".rb" => "ruby",
                ".go" => "go",
                ".rs" => "rust",
                ".swift" => "swift",
                ".kt" => "kotlin",
                ".scala" => "scala",
                ".vb" => "vbnet",
                ".fs" => "fsharp",
                ".xml" or ".xaml" => "xml",
                ".json" => "json",
                ".yaml" or ".yml" => "yaml",
                ".html" or ".htm" => "html",
                ".css" => "css",
                ".scss" or ".sass" => "scss",
                ".sql" => "sql",
                ".ps1" => "powershell",
                ".sh" => "bash",
                ".bat" or ".cmd" => "batch",
                _ => "text"
            };
        }

        private int GetLineFromPosition(int position)
        {
            // This is a simplified implementation
            // In a real scenario, you'd use the ITextSnapshot to get the line number
            return position / 80; // Rough estimate assuming 80 chars per line
        }

        #endregion
    }
}