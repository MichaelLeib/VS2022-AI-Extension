using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Text;
using OllamaAssistant.Models;
using OllamaAssistant.Services.Interfaces;

namespace OllamaAssistant.Services.Implementation
{
    /// <summary>
    /// Implementation of context capture service for analyzing code context
    /// </summary>
    public class ContextCaptureService : IContextCaptureService
    {
        private readonly ICursorHistoryService _cursorHistoryService;
        private readonly ITextViewService _textViewService;

        public ContextCaptureService(ICursorHistoryService cursorHistoryService, ITextViewService textViewService)
        {
            _cursorHistoryService = cursorHistoryService ?? throw new ArgumentNullException(nameof(cursorHistoryService));
            _textViewService = textViewService ?? throw new ArgumentNullException(nameof(textViewService));
        }

        #region Public Methods

        public async Task<CodeContext> CaptureContextAsync(SnapshotPoint caretPosition, int linesUp, int linesDown)
        {
            try
            {
                var snapshot = caretPosition.Snapshot;
                var currentLine = caretPosition.GetContainingLine();
                var filePath = GetFilePathFromSnapshot(snapshot);
                var language = DetectLanguageFromFilePath(filePath);

                // Capture surrounding lines
                var startLine = Math.Max(0, currentLine.LineNumber - linesUp);
                var endLine = Math.Min(snapshot.LineCount - 1, currentLine.LineNumber + linesDown);

                var precedingLines = new List<string>();
                var followingLines = new List<string>();

                // Get preceding lines
                for (int i = startLine; i < currentLine.LineNumber; i++)
                {
                    var line = snapshot.GetLineFromLineNumber(i);
                    precedingLines.Add(line.GetText());
                }

                // Get following lines
                for (int i = currentLine.LineNumber + 1; i <= endLine; i++)
                {
                    var line = snapshot.GetLineFromLineNumber(i);
                    followingLines.Add(line.GetText());
                }

                // Get cursor history
                var history = _cursorHistoryService.GetRelevantHistory(filePath, caretPosition.Position, 5).ToList();

                // Analyze indentation
                var indentation = await DetectIndentationAsync(snapshot, caretPosition);

                // Analyze current scope
                var currentScope = await AnalyzeCurrentScopeAsync(caretPosition);

                var context = new CodeContext
                {
                    FileName = filePath,
                    Language = language,
                    PrecedingLines = precedingLines.ToArray(),
                    CurrentLine = currentLine.GetText(),
                    FollowingLines = followingLines.ToArray(),
                    CaretPosition = caretPosition.Position - currentLine.Start.Position,
                    LineNumber = currentLine.LineNumber + 1, // Convert to 1-based
                    CurrentScope = currentScope,
                    Indentation = indentation,
                    CursorHistory = history
                };

                return context;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error capturing context: {ex.Message}");
                return new CodeContext
                {
                    FileName = GetFilePathFromSnapshot(caretPosition.Snapshot),
                    Language = "text",
                    PrecedingLines = Array.Empty<string>(),
                    CurrentLine = string.Empty,
                    FollowingLines = Array.Empty<string>(),
                    CursorHistory = new List<CursorHistoryEntry>()
                };
            }
        }

        public async Task<JumpRecommendation> AnalyzeJumpOpportunitiesAsync(ITextSnapshot snapshot, SnapshotPoint caretPosition)
        {
            try
            {
                var language = DetectLanguageFromFilePath(GetFilePathFromSnapshot(snapshot));
                var currentLine = caretPosition.GetContainingLine();
                var currentLineText = currentLine.GetText().Trim();

                // Analyze based on language and context
                var recommendations = new List<JumpRecommendation>();

                // Check for common patterns
                await AnalyzeBlockStructure(snapshot, caretPosition, recommendations);
                await AnalyzeMethodStructure(snapshot, caretPosition, recommendations, language);
                await AnalyzeErrorLocations(snapshot, caretPosition, recommendations);
                await AnalyzeIncompleteCode(snapshot, caretPosition, recommendations);

                // Return the best recommendation
                var bestRecommendation = recommendations
                    .Where(r => r.Confidence > 0.5)
                    .OrderByDescending(r => r.Confidence)
                    .FirstOrDefault();

                return bestRecommendation ?? new JumpRecommendation { Direction = JumpDirection.None };
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error analyzing jump opportunities: {ex.Message}");
                return new JumpRecommendation { Direction = JumpDirection.None };
            }
        }

        public async Task<string> CaptureContextSnippetAsync(SnapshotPoint caretPosition, int contextSize = 100)
        {
            try
            {
                var currentLine = caretPosition.GetContainingLine();
                var lineText = currentLine.GetText();
                
                if (lineText.Length <= contextSize)
                    return lineText;

                // Try to capture around the caret position
                var caretColumn = caretPosition.Position - currentLine.Start.Position;
                var halfSize = contextSize / 2;
                
                var start = Math.Max(0, caretColumn - halfSize);
                var length = Math.Min(contextSize, lineText.Length - start);
                
                var snippet = lineText.Substring(start, length);
                
                // Add ellipsis if truncated
                if (start > 0)
                    snippet = "..." + snippet;
                if (start + length < lineText.Length)
                    snippet = snippet + "...";

                return snippet;
            }
            catch
            {
                return string.Empty;
            }
        }

        public async Task<string> AnalyzeCurrentScopeAsync(SnapshotPoint caretPosition)
        {
            try
            {
                var snapshot = caretPosition.Snapshot;
                var currentLine = caretPosition.GetContainingLine();
                var language = DetectLanguageFromFilePath(GetFilePathFromSnapshot(snapshot));

                var scopes = new List<string>();

                // Look backwards for scope indicators
                for (int lineNum = currentLine.LineNumber; lineNum >= 0; lineNum--)
                {
                    var line = snapshot.GetLineFromLineNumber(lineNum);
                    var lineText = line.GetText().Trim();

                    if (string.IsNullOrWhiteSpace(lineText))
                        continue;

                    // Language-specific scope detection
                    var scope = DetectScopeFromLine(lineText, language);
                    if (!string.IsNullOrEmpty(scope))
                    {
                        scopes.Add(scope);
                        
                        // Stop after finding a few scopes to avoid going too far back
                        if (scopes.Count >= 3)
                            break;
                    }
                }

                scopes.Reverse(); // Reverse to get outermost to innermost
                return scopes.Count > 0 ? string.Join(" > ", scopes) : "global";
            }
            catch
            {
                return "unknown";
            }
        }

        public async Task<IndentationInfo> DetectIndentationAsync(ITextSnapshot snapshot, SnapshotPoint caretPosition)
        {
            try
            {
                var currentLine = caretPosition.GetContainingLine();
                var lineText = currentLine.GetText();
                
                var indentationInfo = new IndentationInfo();

                // Detect indentation from current line
                var leadingWhitespace = GetLeadingWhitespace(lineText);
                
                if (leadingWhitespace.Contains('\t'))
                {
                    indentationInfo.UsesSpaces = false;
                    indentationInfo.Level = leadingWhitespace.Count(c => c == '\t');
                    indentationInfo.IndentString = new string('\t', indentationInfo.Level);
                }
                else
                {
                    indentationInfo.UsesSpaces = true;
                    var spaceCount = leadingWhitespace.Length;
                    
                    // Try to detect spaces per indent by looking at surrounding lines
                    var spacesPerIndent = DetectSpacesPerIndent(snapshot, currentLine.LineNumber);
                    indentationInfo.SpacesPerIndent = spacesPerIndent;
                    indentationInfo.Level = spacesPerIndent > 0 ? spaceCount / spacesPerIndent : 0;
                    indentationInfo.IndentString = leadingWhitespace;
                }

                return indentationInfo;
            }
            catch
            {
                return new IndentationInfo();
            }
        }

        public async Task<bool> IsInCommentAsync(SnapshotPoint caretPosition)
        {
            try
            {
                var line = caretPosition.GetContainingLine();
                var lineText = line.GetText();
                var caretColumn = caretPosition.Position - line.Start.Position;
                
                var language = DetectLanguageFromFilePath(GetFilePathFromSnapshot(caretPosition.Snapshot));
                
                return IsPositionInComment(lineText, caretColumn, language);
            }
            catch
            {
                return false;
            }
        }

        public async Task<bool> IsInStringAsync(SnapshotPoint caretPosition)
        {
            try
            {
                var line = caretPosition.GetContainingLine();
                var lineText = line.GetText();
                var caretColumn = caretPosition.Position - line.Start.Position;
                
                return IsPositionInString(lineText, caretColumn);
            }
            catch
            {
                return false;
            }
        }

        #endregion

        #region Private Analysis Methods

        private async Task AnalyzeBlockStructure(ITextSnapshot snapshot, SnapshotPoint caretPosition, List<JumpRecommendation> recommendations)
        {
            var currentLine = caretPosition.GetContainingLine();
            var currentLineText = currentLine.GetText().Trim();

            // Look for opening braces that need closing
            if (currentLineText.EndsWith("{") || currentLineText.EndsWith("{"))
            {
                // Find the matching closing brace
                var matchingBrace = FindMatchingBrace(snapshot, caretPosition, true);
                if (matchingBrace.HasValue)
                {
                    var targetLine = matchingBrace.Value.GetContainingLine();
                    recommendations.Add(new JumpRecommendation
                    {
                        TargetLine = targetLine.LineNumber + 1,
                        TargetColumn = 0,
                        Direction = targetLine.LineNumber > currentLine.LineNumber ? JumpDirection.Down : JumpDirection.Up,
                        Reason = "Jump to matching closing brace",
                        Confidence = 0.8,
                        Type = JumpType.BlockEnd,
                        TargetPreview = targetLine.GetText().Trim()
                    });
                }
            }

            // Look for incomplete if/for/while statements
            if (IsIncompleteControlStructure(currentLineText))
            {
                var targetLine = FindNextLogicalPosition(snapshot, currentLine);
                if (targetLine.HasValue)
                {
                    recommendations.Add(new JumpRecommendation
                    {
                        TargetLine = targetLine.Value.LineNumber + 1,
                        TargetColumn = GetIndentationLevel(targetLine.Value.GetText()) + 4,
                        Direction = JumpDirection.Down,
                        Reason = "Jump to body of control structure",
                        Confidence = 0.9,
                        Type = JumpType.NextLogicalPosition,
                        TargetPreview = targetLine.Value.GetText().Trim()
                    });
                }
            }
        }

        private async Task AnalyzeMethodStructure(ITextSnapshot snapshot, SnapshotPoint caretPosition, List<JumpRecommendation> recommendations, string language)
        {
            var currentLine = caretPosition.GetContainingLine();
            var currentLineText = currentLine.GetText().Trim();

            // Detect method signatures
            if (IsMethodSignature(currentLineText, language))
            {
                // Look for the method body start
                for (int i = currentLine.LineNumber + 1; i < snapshot.LineCount; i++)
                {
                    var line = snapshot.GetLineFromLineNumber(i);
                    var lineText = line.GetText().Trim();
                    
                    if (lineText == "{" || lineText.EndsWith("{"))
                    {
                        recommendations.Add(new JumpRecommendation
                        {
                            TargetLine = i + 2, // Jump inside the method body
                            TargetColumn = GetIndentationLevel(line.GetText()) + 4,
                            Direction = JumpDirection.Down,
                            Reason = "Jump to method body",
                            Confidence = 0.85,
                            Type = JumpType.NextLogicalPosition,
                            TargetPreview = "Method body"
                        });
                        break;
                    }
                }
            }
        }

        private async Task AnalyzeErrorLocations(ITextSnapshot snapshot, SnapshotPoint caretPosition, List<JumpRecommendation> recommendations)
        {
            // This would integrate with VS error list in a full implementation
            // For now, we'll look for common error patterns
            
            var currentLine = caretPosition.GetContainingLine();
            var currentLineText = currentLine.GetText();

            // Look for missing semicolons, unmatched brackets, etc.
            if (HasSyntaxError(currentLineText))
            {
                var fixLine = FindErrorFixLocation(snapshot, currentLine);
                if (fixLine.HasValue)
                {
                    recommendations.Add(new JumpRecommendation
                    {
                        TargetLine = fixLine.Value.LineNumber + 1,
                        TargetColumn = fixLine.Value.GetText().Length,
                        Direction = fixLine.Value.LineNumber > currentLine.LineNumber ? JumpDirection.Down : JumpDirection.Up,
                        Reason = "Jump to fix syntax error",
                        Confidence = 0.7,
                        Type = JumpType.ErrorLocation,
                        TargetPreview = fixLine.Value.GetText().Trim()
                    });
                }
            }
        }

        private async Task AnalyzeIncompleteCode(ITextSnapshot snapshot, SnapshotPoint caretPosition, List<JumpRecommendation> recommendations)
        {
            var currentLine = caretPosition.GetContainingLine();
            var currentLineText = currentLine.GetText().Trim();

            // Look for incomplete patterns
            if (currentLineText.EndsWith("=>") || currentLineText.EndsWith("="))
            {
                var nextLine = currentLine.LineNumber + 1 < snapshot.LineCount ? 
                    snapshot.GetLineFromLineNumber(currentLine.LineNumber + 1) : null;
                
                if (nextLine != null)
                {
                    recommendations.Add(new JumpRecommendation
                    {
                        TargetLine = nextLine.LineNumber + 1,
                        TargetColumn = GetIndentationLevel(currentLineText) + 4,
                        Direction = JumpDirection.Down,
                        Reason = "Complete assignment or lambda",
                        Confidence = 0.75,
                        Type = JumpType.PatternCompletion,
                        TargetPreview = "Complete expression"
                    });
                }
            }
        }

        #endregion

        #region Private Helper Methods

        private string GetFilePathFromSnapshot(ITextSnapshot snapshot)
        {
            try
            {
                if (snapshot.TextBuffer.Properties.TryGetProperty(typeof(ITextDocument), out ITextDocument document))
                {
                    return document.FilePath;
                }
                return null;
            }
            catch
            {
                return null;
            }
        }

        private string DetectLanguageFromFilePath(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                return "text";

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
                _ => "text"
            };
        }

        private string DetectScopeFromLine(string lineText, string language)
        {
            lineText = lineText.Trim();
            
            return language switch
            {
                "csharp" => DetectCSharpScope(lineText),
                "cpp" or "c" => DetectCppScope(lineText),
                "javascript" or "typescript" => DetectJavaScriptScope(lineText),
                "python" => DetectPythonScope(lineText),
                "java" => DetectJavaScope(lineText),
                _ => null
            };
        }

        private string DetectCSharpScope(string lineText)
        {
            if (Regex.IsMatch(lineText, @"^\s*(public|private|protected|internal)?\s*(class|interface|struct|enum)\s+(\w+)"))
                return Regex.Match(lineText, @"\b(class|interface|struct|enum)\s+(\w+)").Groups[2].Value;
            
            if (Regex.IsMatch(lineText, @"^\s*(public|private|protected|internal)?\s*(static\s+)?(async\s+)?\w+\s+(\w+)\s*\("))
                return Regex.Match(lineText, @"\b(\w+)\s*\(").Groups[1].Value + "()";
            
            if (Regex.IsMatch(lineText, @"^\s*namespace\s+(\w+(\.\w+)*)"))
                return Regex.Match(lineText, @"namespace\s+([\w\.]+)").Groups[1].Value;

            return null;
        }

        private string DetectCppScope(string lineText)
        {
            if (Regex.IsMatch(lineText, @"^\s*(class|struct)\s+(\w+)"))
                return Regex.Match(lineText, @"(class|struct)\s+(\w+)").Groups[2].Value;
            
            if (Regex.IsMatch(lineText, @"^\s*\w+\s+(\w+)\s*\("))
                return Regex.Match(lineText, @"\b(\w+)\s*\(").Groups[1].Value + "()";

            return null;
        }

        private string DetectJavaScriptScope(string lineText)
        {
            if (Regex.IsMatch(lineText, @"^\s*(function\s+(\w+)|(\w+)\s*:\s*function|(\w+)\s*=\s*function)"))
            {
                var match = Regex.Match(lineText, @"function\s+(\w+)|(\w+)\s*:\s*function|(\w+)\s*=\s*function");
                var functionName = match.Groups[1].Value ?? match.Groups[2].Value ?? match.Groups[3].Value;
                return functionName + "()";
            }
            
            if (Regex.IsMatch(lineText, @"^\s*(class|const|let|var)\s+(\w+)"))
                return Regex.Match(lineText, @"(class|const|let|var)\s+(\w+)").Groups[2].Value;

            return null;
        }

        private string DetectPythonScope(string lineText)
        {
            if (Regex.IsMatch(lineText, @"^\s*def\s+(\w+)"))
                return Regex.Match(lineText, @"def\s+(\w+)").Groups[1].Value + "()";
            
            if (Regex.IsMatch(lineText, @"^\s*class\s+(\w+)"))
                return Regex.Match(lineText, @"class\s+(\w+)").Groups[1].Value;

            return null;
        }

        private string DetectJavaScope(string lineText)
        {
            if (Regex.IsMatch(lineText, @"^\s*(public|private|protected)?\s*(class|interface|enum)\s+(\w+)"))
                return Regex.Match(lineText, @"(class|interface|enum)\s+(\w+)").Groups[2].Value;
            
            if (Regex.IsMatch(lineText, @"^\s*(public|private|protected)?\s*(static\s+)?\w+\s+(\w+)\s*\("))
                return Regex.Match(lineText, @"\b(\w+)\s*\(").Groups[1].Value + "()";

            return null;
        }

        private string GetLeadingWhitespace(string line)
        {
            var match = Regex.Match(line, @"^(\s*)");
            return match.Groups[1].Value;
        }

        private int DetectSpacesPerIndent(ITextSnapshot snapshot, int currentLineNumber)
        {
            var indentLevels = new List<int>();
            
            // Look at surrounding lines to detect consistent indentation
            var start = Math.Max(0, currentLineNumber - 10);
            var end = Math.Min(snapshot.LineCount - 1, currentLineNumber + 10);
            
            for (int i = start; i <= end; i++)
            {
                var line = snapshot.GetLineFromLineNumber(i);
                var text = line.GetText();
                var leadingSpaces = GetLeadingWhitespace(text);
                
                if (leadingSpaces.Length > 0 && !leadingSpaces.Contains('\t'))
                {
                    indentLevels.Add(leadingSpaces.Length);
                }
            }
            
            // Find the most common non-zero indent level
            var commonIndent = indentLevels
                .Where(x => x > 0)
                .GroupBy(x => x)
                .OrderByDescending(g => g.Count())
                .FirstOrDefault()?.Key ?? 4;
            
            return commonIndent;
        }

        private bool IsPositionInComment(string lineText, int position, string language)
        {
            if (position >= lineText.Length)
                return false;

            return language switch
            {
                "csharp" or "cpp" or "c" or "java" or "javascript" or "typescript" => 
                    IsInCStyleComment(lineText, position),
                "python" => IsInPythonComment(lineText, position),
                _ => false
            };
        }

        private bool IsInCStyleComment(string lineText, int position)
        {
            var singleLineComment = lineText.IndexOf("//");
            if (singleLineComment >= 0 && position >= singleLineComment)
                return true;

            // Multi-line comments would require more complex analysis across lines
            var multiLineStart = lineText.IndexOf("/*");
            var multiLineEnd = lineText.IndexOf("*/");
            
            if (multiLineStart >= 0 && multiLineEnd >= 0)
            {
                return position >= multiLineStart && position <= multiLineEnd;
            }
            else if (multiLineStart >= 0 && position >= multiLineStart)
            {
                return true; // Assuming we're in a multi-line comment
            }

            return false;
        }

        private bool IsInPythonComment(string lineText, int position)
        {
            var commentStart = lineText.IndexOf("#");
            return commentStart >= 0 && position >= commentStart;
        }

        private bool IsPositionInString(string lineText, int position)
        {
            if (position >= lineText.Length)
                return false;

            bool inSingleQuote = false;
            bool inDoubleQuote = false;
            bool escapeNext = false;

            for (int i = 0; i < position; i++)
            {
                var c = lineText[i];
                
                if (escapeNext)
                {
                    escapeNext = false;
                    continue;
                }

                if (c == '\\')
                {
                    escapeNext = true;
                    continue;
                }

                if (c == '\'' && !inDoubleQuote)
                {
                    inSingleQuote = !inSingleQuote;
                }
                else if (c == '"' && !inSingleQuote)
                {
                    inDoubleQuote = !inDoubleQuote;
                }
            }

            return inSingleQuote || inDoubleQuote;
        }

        private SnapshotPoint? FindMatchingBrace(ITextSnapshot snapshot, SnapshotPoint position, bool findClosing)
        {
            // Simplified brace matching - in a real implementation, this would be more sophisticated
            var currentLine = position.GetContainingLine();
            var searchDirection = findClosing ? 1 : -1;
            var targetChar = findClosing ? '}' : '{';
            var openCount = 0;

            for (int lineNum = currentLine.LineNumber; 
                 lineNum >= 0 && lineNum < snapshot.LineCount; 
                 lineNum += searchDirection)
            {
                var line = snapshot.GetLineFromLineNumber(lineNum);
                var text = line.GetText();

                foreach (char c in text)
                {
                    if (c == '{')
                        openCount += findClosing ? 1 : -1;
                    else if (c == '}')
                        openCount += findClosing ? -1 : 1;

                    if (openCount == 0 && c == targetChar)
                    {
                        return new SnapshotPoint(snapshot, line.Start.Position + text.IndexOf(c));
                    }
                }
            }

            return null;
        }

        private bool IsIncompleteControlStructure(string lineText)
        {
            var trimmed = lineText.Trim();
            return Regex.IsMatch(trimmed, @"^\s*(if\s*\(.*\)|for\s*\(.*\)|while\s*\(.*\)|foreach\s*\(.*\))\s*$");
        }

        private ITextSnapshotLine? FindNextLogicalPosition(ITextSnapshot snapshot, ITextSnapshotLine currentLine)
        {
            if (currentLine.LineNumber + 1 < snapshot.LineCount)
            {
                return snapshot.GetLineFromLineNumber(currentLine.LineNumber + 1);
            }
            return null;
        }

        private bool IsMethodSignature(string lineText, string language)
        {
            return language switch
            {
                "csharp" => Regex.IsMatch(lineText, @"^\s*(public|private|protected|internal)?\s*(static\s+)?(async\s+)?\w+\s+\w+\s*\([^)]*\)\s*$"),
                "java" => Regex.IsMatch(lineText, @"^\s*(public|private|protected)?\s*(static\s+)?\w+\s+\w+\s*\([^)]*\)\s*$"),
                "cpp" or "c" => Regex.IsMatch(lineText, @"^\s*\w+\s+\w+\s*\([^)]*\)\s*$"),
                "javascript" or "typescript" => Regex.IsMatch(lineText, @"^\s*(function\s+\w+|(\w+)\s*:\s*function|\w+\s*=\s*function)\s*\([^)]*\)\s*$"),
                "python" => Regex.IsMatch(lineText, @"^\s*def\s+\w+\s*\([^)]*\):\s*$"),
                _ => false
            };
        }

        private int GetIndentationLevel(string line)
        {
            var leadingWhitespace = GetLeadingWhitespace(line);
            return leadingWhitespace.Contains('\t') ? 
                leadingWhitespace.Count(c => c == '\t') * 4 : 
                leadingWhitespace.Length;
        }

        private bool HasSyntaxError(string lineText)
        {
            // Simple heuristics for common syntax errors
            var trimmed = lineText.Trim();
            
            // Missing semicolon in C-style languages
            if (Regex.IsMatch(trimmed, @"^\s*(return|break|continue|var|let|const)\s+.*[^;{}]\s*$"))
                return true;
            
            // Unmatched brackets
            var openBrackets = trimmed.Count(c => c == '(' || c == '[' || c == '{');
            var closeBrackets = trimmed.Count(c => c == ')' || c == ']' || c == '}');
            
            return openBrackets != closeBrackets;
        }

        private ITextSnapshotLine? FindErrorFixLocation(ITextSnapshot snapshot, ITextSnapshotLine currentLine)
        {
            // Look for the most likely place to fix the error
            // This is a simplified implementation
            
            if (currentLine.LineNumber > 0)
            {
                var previousLine = snapshot.GetLineFromLineNumber(currentLine.LineNumber - 1);
                var prevText = previousLine.GetText().Trim();
                
                // If previous line doesn't end with semicolon, that might be the issue
                if (!prevText.EndsWith(";") && !prevText.EndsWith("{") && !prevText.EndsWith("}") && !string.IsNullOrWhiteSpace(prevText))
                {
                    return previousLine;
                }
            }
            
            return currentLine;
        }

        #endregion
    }
}