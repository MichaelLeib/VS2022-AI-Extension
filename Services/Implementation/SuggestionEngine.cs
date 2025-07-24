using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using OllamaAssistant.Infrastructure;
using OllamaAssistant.Models;
using OllamaAssistant.Services.Interfaces;

namespace OllamaAssistant.Services.Implementation
{
    /// <summary>
    /// Implementation of suggestion engine for processing and filtering AI suggestions
    /// </summary>
    public class SuggestionEngine : ISuggestionEngine, IDisposable
    {
        private readonly ISettingsService _settingsService;
        private readonly IContextCaptureService _contextCaptureService;
        private readonly Dictionary<string, DateTime> _recentSuggestions;
        private readonly object _lockObject = new object();
        private readonly CacheService<string, CodeSuggestion> _suggestionCache;
        private readonly DebounceService _debounceService;

        private double _minimumConfidenceThreshold;
        private bool _disposed;

        public SuggestionEngine(ISettingsService settingsService, IContextCaptureService contextCaptureService)
        {
            _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
            _contextCaptureService = contextCaptureService ?? throw new ArgumentNullException(nameof(contextCaptureService));
            _recentSuggestions = new Dictionary<string, DateTime>();
            
            // Initialize caching with configurable expiration
            var cacheExpiration = TimeSpan.FromMinutes(_settingsService.CacheExpirationMinutes);
            _suggestionCache = new CacheService<string, CodeSuggestion>(cacheExpiration, _settingsService.CacheSize);
            _debounceService = new DebounceService();
            
            _minimumConfidenceThreshold = _settingsService.MinimumConfidenceThreshold;
            
            // Subscribe to settings changes
            _settingsService.SettingsChanged += OnSettingsChanged;
        }

        #region Properties

        public double MinimumConfidenceThreshold => _minimumConfidenceThreshold;

        #endregion

        #region Public Methods

        public async Task<CodeSuggestion> ProcessSuggestionAsync(string aiResponse, CodeContext context)
        {
            if (string.IsNullOrWhiteSpace(aiResponse) || context == null)
            {
                return new CodeSuggestion { Confidence = 0.0 };
            }

            // Generate cache key based on AI response and context
            var cacheKey = GenerateCacheKey(aiResponse, context);
            
            // Try to get from cache first
            if (_settingsService.EnableSuggestionCache && _suggestionCache.TryGet(cacheKey, out var cachedSuggestion))
            {
                return cachedSuggestion;
            }

            try
            {
                // Clean and parse the AI response
                var cleanedText = CleanAIResponse(aiResponse);
                if (string.IsNullOrWhiteSpace(cleanedText))
                {
                    return new CodeSuggestion { Confidence = 0.0 };
                }

                // Create initial suggestion
                var suggestion = new CodeSuggestion
                {
                    Text = cleanedText,
                    InsertionPoint = context.CaretPosition,
                    Type = DetermineSuggestionType(cleanedText, context),
                    SourceContext = context.CurrentScope,
                    HasPlaceholders = ContainsPlaceholders(cleanedText)
                };

                // Calculate confidence score
                suggestion.Confidence = await CalculateConfidenceAsync(suggestion, context);

                // Set priority based on confidence and type
                suggestion.Priority = CalculatePriority(suggestion, context);

                // Generate description
                suggestion.Description = GenerateDescription(suggestion, context);

                // Determine replacement range if applicable
                suggestion.ReplacementRange = await DetermineReplacementRangeAsync(suggestion, context);

                // Cache the suggestion if caching is enabled
                if (_settingsService.EnableSuggestionCache && suggestion.Confidence >= _minimumConfidenceThreshold)
                {
                    _suggestionCache.Set(cacheKey, suggestion);
                }

                return suggestion;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error processing suggestion: {ex.Message}");
                return new CodeSuggestion { Confidence = 0.0 };
            }
        }

        public bool ShouldShowSuggestion(CodeSuggestion suggestion, CodeContext context)
        {
            if (suggestion == null || context == null)
                return false;

            // Check minimum confidence threshold
            if (suggestion.Confidence < _minimumConfidenceThreshold)
                return false;

            // Don't show suggestions in comments unless it's a comment suggestion
            if (IsInComment(context) && suggestion.Type != SuggestionType.Comment)
                return false;

            // Don't show suggestions in strings unless it's a string-related suggestion
            if (IsInString(context) && suggestion.Type != SuggestionType.General)
                return false;

            // Check for recent duplicates
            if (IsRecentDuplicate(suggestion))
                return false;

            // Language-specific filtering
            if (!IsLanguageAppropriate(suggestion, context))
                return false;

            // Context-specific filtering
            if (!IsContextAppropriate(suggestion, context))
                return false;

            // Check minimum suggestion length
            if (suggestion.Text.Trim().Length < 2)
                return false;

            // Don't show suggestions that are just whitespace or basic characters
            if (IsTriviaSuggestion(suggestion.Text))
                return false;

            return true;
        }

        public async Task<JumpRecommendation> ProcessJumpSuggestionAsync(CodeContext context)
        {
            if (context == null)
                return new JumpRecommendation { Direction = JumpDirection.None };

            try
            {
                // Use context capture service to analyze jump opportunities
                var caretPosition = new Microsoft.VisualStudio.Text.SnapshotPoint(
                    context.FileName != null ? 
                    Microsoft.VisualStudio.Text.BufferExtensions.GetTextBuffer(context.FileName)?.CurrentSnapshot ?? 
                    throw new InvalidOperationException("Cannot get snapshot") : 
                    throw new InvalidOperationException("No filename"), 
                    context.CaretPosition);

                return await _contextCaptureService.AnalyzeJumpOpportunitiesAsync(
                    caretPosition.Snapshot, caretPosition);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error processing jump suggestion: {ex.Message}");
                
                // Fallback: simple heuristic-based jump analysis
                return AnalyzeJumpHeuristic(context);
            }
        }

        public async Task<CodeSuggestion[]> RankSuggestionsAsync(CodeSuggestion[] suggestions, CodeContext context)
        {
            if (suggestions == null || suggestions.Length == 0)
                return Array.Empty<CodeSuggestion>();

            var rankedSuggestions = new List<(CodeSuggestion suggestion, double score)>();

            foreach (var suggestion in suggestions)
            {
                var score = await CalculateRankingScoreAsync(suggestion, context);
                rankedSuggestions.Add((suggestion, score));
            }

            return rankedSuggestions
                .OrderByDescending(x => x.score)
                .Select(x => x.suggestion)
                .ToArray();
        }

        public CodeSuggestion[] FilterDuplicates(CodeSuggestion[] suggestions)
        {
            if (suggestions == null || suggestions.Length == 0)
                return Array.Empty<CodeSuggestion>();

            var filtered = new List<CodeSuggestion>();
            var seen = new HashSet<string>();

            foreach (var suggestion in suggestions.OrderByDescending(s => s.Confidence))
            {
                var normalizedText = NormalizeSuggestionText(suggestion.Text);
                
                if (!seen.Contains(normalizedText))
                {
                    seen.Add(normalizedText);
                    filtered.Add(suggestion);
                }
            }

            return filtered.ToArray();
        }

        public async Task<bool> ValidateSuggestionAsync(CodeSuggestion suggestion, CodeContext context)
        {
            if (suggestion == null || context == null)
                return false;

            try
            {
                // Basic validation
                if (string.IsNullOrWhiteSpace(suggestion.Text))
                    return false;

                // Syntax validation (basic)
                if (!IsValidSyntax(suggestion.Text, context.Language))
                    return false;

                // Contextual validation
                if (!await IsContextuallyValidAsync(suggestion, context))
                    return false;

                // Indentation validation
                if (!ValidateIndentation(suggestion, context))
                    return false;

                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error validating suggestion: {ex.Message}");
                return false;
            }
        }

        public async Task<CodeSuggestion> AdaptToCodeStyleAsync(CodeSuggestion suggestion, CodeContext context)
        {
            if (suggestion == null || context == null)
                return suggestion;

            try
            {
                var adaptedText = suggestion.Text;

                // Adapt indentation
                adaptedText = AdaptIndentation(adaptedText, context.Indentation);

                // Adapt naming conventions
                adaptedText = AdaptNamingConventions(adaptedText, context.Language);

                // Adapt bracket style
                adaptedText = AdaptBracketStyle(adaptedText, context);

                // Create adapted suggestion
                var adaptedSuggestion = new CodeSuggestion
                {
                    Text = adaptedText,
                    InsertionPoint = suggestion.InsertionPoint,
                    Confidence = suggestion.Confidence,
                    Type = suggestion.Type,
                    Description = suggestion.Description,
                    ReplacementRange = suggestion.ReplacementRange,
                    HasPlaceholders = suggestion.HasPlaceholders,
                    SourceContext = suggestion.SourceContext,
                    Priority = suggestion.Priority
                };

                return adaptedSuggestion;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error adapting suggestion to code style: {ex.Message}");
                return suggestion;
            }
        }

        public void SetMinimumConfidenceThreshold(double threshold)
        {
            _minimumConfidenceThreshold = Math.Max(0.0, Math.Min(1.0, threshold));
        }

        #endregion

        #region Private Methods - Core Processing

        private string CleanAIResponse(string response)
        {
            if (string.IsNullOrWhiteSpace(response))
                return string.Empty;

            var cleaned = response.Trim();

            // Remove markdown code blocks
            cleaned = Regex.Replace(cleaned, @"^```\w*\n?", "", RegexOptions.Multiline);
            cleaned = Regex.Replace(cleaned, @"\n?```$", "", RegexOptions.Multiline);

            // Remove common AI response prefixes
            var prefixPatterns = new[]
            {
                @"^(Here's|Here is|I suggest|I recommend|You could|Try this|Consider this).*?:\s*",
                @"^The (completion|suggestion) is:\s*",
                @"^Based on.*?:\s*"
            };

            foreach (var pattern in prefixPatterns)
            {
                cleaned = Regex.Replace(cleaned, pattern, "", RegexOptions.IgnoreCase);
            }

            // Remove explanatory suffixes
            var suffixPatterns = new[]
            {
                @"\s*(This will|This should|This completes).*$",
                @"\s*\/\/.*explanation.*$",
                @"\s*\/\*.*explanation.*\*\/$"
            };

            foreach (var pattern in suffixPatterns)
            {
                cleaned = Regex.Replace(cleaned, pattern, "", RegexOptions.IgnoreCase);
            }

            return cleaned.Trim();
        }

        private SuggestionType DetermineSuggestionType(string text, CodeContext context)
        {
            var trimmed = text.Trim();

            // Check for comments
            if (trimmed.StartsWith("//") || trimmed.StartsWith("/*") || trimmed.StartsWith("#"))
                return SuggestionType.Comment;

            // Check for imports/using statements
            if (Regex.IsMatch(trimmed, @"^(import|using|#include|require|from)\s+"))
                return SuggestionType.Import;

            // Check for method calls
            if (Regex.IsMatch(trimmed, @"\w+\s*\([^)]*\)"))
                return SuggestionType.Method;

            // Check for type declarations
            if (Regex.IsMatch(trimmed, @"^(class|interface|struct|enum|type)\s+\w+"))
                return SuggestionType.Type;

            // Check for variable declarations
            if (Regex.IsMatch(trimmed, @"^(var|let|const|int|string|bool|double|float)\s+\w+"))
                return SuggestionType.Variable;

            // Check for documentation comments
            if (trimmed.StartsWith("///") || trimmed.StartsWith("/**"))
                return SuggestionType.Documentation;

            return SuggestionType.General;
        }

        private async Task<double> CalculateConfidenceAsync(CodeSuggestion suggestion, CodeContext context)
        {
            double confidence = 0.5; // Base confidence

            // Language appropriateness
            if (IsLanguageAppropriate(suggestion, context))
                confidence += 0.2;

            // Indentation correctness
            if (ValidateIndentation(suggestion, context))
                confidence += 0.15;

            // Syntax validity
            if (IsValidSyntax(suggestion.Text, context.Language))
                confidence += 0.2;

            // Context relevance
            if (await IsContextuallyValidAsync(suggestion, context))
                confidence += 0.15;

            // Length appropriateness
            var length = suggestion.Text.Length;
            if (length > 5 && length < 200)
                confidence += 0.1;
            else if (length < 3 || length > 500)
                confidence -= 0.2;

            // Complexity appropriateness
            var complexity = CalculateComplexity(suggestion.Text);
            if (complexity > 0.3 && complexity < 0.8)
                confidence += 0.05;

            // Scope relevance
            if (!string.IsNullOrEmpty(context.CurrentScope) && 
                suggestion.Text.Contains(ExtractScopeName(context.CurrentScope)))
                confidence += 0.1;

            return Math.Max(0.0, Math.Min(1.0, confidence));
        }

        private int CalculatePriority(CodeSuggestion suggestion, CodeContext context)
        {
            var priority = (int)(suggestion.Confidence * 100);

            // Boost priority for certain types
            switch (suggestion.Type)
            {
                case SuggestionType.Method:
                    priority += 10;
                    break;
                case SuggestionType.Type:
                    priority += 8;
                    break;
                case SuggestionType.Variable:
                    priority += 5;
                    break;
                case SuggestionType.Comment:
                    priority -= 5; // Lower priority for comments
                    break;
            }

            // Boost priority for shorter suggestions (more likely to be immediately useful)
            if (suggestion.Text.Length < 50)
                priority += 5;

            return Math.Max(0, Math.Min(100, priority));
        }

        private string GenerateDescription(CodeSuggestion suggestion, CodeContext context)
        {
            return suggestion.Type switch
            {
                SuggestionType.Method => "Method call suggestion",
                SuggestionType.Variable => "Variable declaration or reference",
                SuggestionType.Type => "Type declaration or reference",
                SuggestionType.Comment => "Comment suggestion",
                SuggestionType.Import => "Import or using statement",
                SuggestionType.Documentation => "Documentation comment",
                SuggestionType.Snippet => "Code snippet",
                SuggestionType.Parameter => "Parameter suggestion",
                _ => "Code completion suggestion"
            };
        }

        private async Task<TextRange> DetermineReplacementRangeAsync(CodeSuggestion suggestion, CodeContext context)
        {
            // For now, return null (insert mode)
            // In a full implementation, this would analyze the current word being typed
            // and determine if it should be replaced vs inserted
            
            var currentLine = context.CurrentLine;
            var caretPos = context.CaretPosition;
            
            // Simple heuristic: if we're in the middle of a word, replace it
            if (caretPos > 0 && caretPos < currentLine.Length)
            {
                var charBefore = currentLine[caretPos - 1];
                var charAfter = caretPos < currentLine.Length ? currentLine[caretPos] : ' ';
                
                if (char.IsLetterOrDigit(charBefore) || char.IsLetterOrDigit(charAfter))
                {
                    // Find word boundaries
                    var start = caretPos;
                    while (start > 0 && (char.IsLetterOrDigit(currentLine[start - 1]) || currentLine[start - 1] == '_'))
                        start--;
                    
                    var end = caretPos;
                    while (end < currentLine.Length && (char.IsLetterOrDigit(currentLine[end]) || currentLine[end] == '_'))
                        end++;
                    
                    if (end > start)
                    {
                        return new TextRange(start, end);
                    }
                }
            }
            
            return null;
        }

        #endregion

        #region Private Methods - Validation and Filtering

        private bool IsInComment(CodeContext context)
        {
            // This is a simplified check - in reality, we'd use the context capture service
            var line = context.CurrentLine;
            var pos = context.CaretPosition;
            
            if (pos >= line.Length)
                return false;
            
            var beforeCaret = line.Substring(0, pos);
            
            // Check for single-line comments
            var commentStart = beforeCaret.LastIndexOf("//");
            if (commentStart >= 0)
            {
                // Make sure it's not in a string
                var inString = false;
                for (int i = 0; i < commentStart; i++)
                {
                    if (line[i] == '"' || line[i] == '\'')
                        inString = !inString;
                }
                return !inString;
            }
            
            return false;
        }

        private bool IsInString(CodeContext context)
        {
            var line = context.CurrentLine;
            var pos = context.CaretPosition;
            
            if (pos >= line.Length)
                return false;
            
            bool inDoubleQuotes = false;
            bool inSingleQuotes = false;
            bool escapeNext = false;
            
            for (int i = 0; i < pos; i++)
            {
                var c = line[i];
                
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
                
                if (c == '"' && !inSingleQuotes)
                    inDoubleQuotes = !inDoubleQuotes;
                else if (c == '\'' && !inDoubleQuotes)
                    inSingleQuotes = !inSingleQuotes;
            }
            
            return inDoubleQuotes || inSingleQuotes;
        }

        private bool IsRecentDuplicate(CodeSuggestion suggestion)
        {
            lock (_lockObject)
            {
                var key = NormalizeSuggestionText(suggestion.Text);
                
                if (_recentSuggestions.TryGetValue(key, out var lastSeen))
                {
                    if (DateTime.Now - lastSeen < TimeSpan.FromSeconds(5))
                        return true;
                }
                
                _recentSuggestions[key] = DateTime.Now;
                
                // Clean up old entries
                var cutoff = DateTime.Now - TimeSpan.FromMinutes(5);
                var keysToRemove = _recentSuggestions
                    .Where(kvp => kvp.Value < cutoff)
                    .Select(kvp => kvp.Key)
                    .ToList();
                
                foreach (var keyToRemove in keysToRemove)
                {
                    _recentSuggestions.Remove(keyToRemove);
                }
                
                return false;
            }
        }

        private bool IsLanguageAppropriate(CodeSuggestion suggestion, CodeContext context)
        {
            var language = context.Language?.ToLowerInvariant() ?? "text";
            var text = suggestion.Text;

            return language switch
            {
                "csharp" => text.Contains(";") || text.Contains("var ") || text.Contains("public ") || 
                           text.Contains("private ") || text.Contains("namespace ") || text.Contains("class "),
                "javascript" or "typescript" => text.Contains("function") || text.Contains("=>") || 
                           text.Contains("const ") || text.Contains("let ") || text.Contains("var "),
                "python" => !text.Contains(";") || text.Contains("def ") || text.Contains("class ") || 
                           text.Contains("import ") || text.Contains("from "),
                "java" => text.Contains(";") || text.Contains("public ") || text.Contains("private ") || 
                         text.Contains("class ") || text.Contains("import "),
                "cpp" or "c" => text.Contains(";") || text.Contains("#include") || text.Contains("::") || 
                               text.Contains("std::"),
                _ => true // Accept all for unknown languages
            };
        }

        private bool IsContextAppropriate(CodeSuggestion suggestion, CodeContext context)
        {
            // Check if suggestion makes sense in current scope
            if (string.IsNullOrEmpty(context.CurrentScope))
                return true;

            var scope = context.CurrentScope.ToLowerInvariant();
            var text = suggestion.Text.ToLowerInvariant();

            // Don't suggest class-level constructs inside methods
            if (scope.Contains("()") && (text.Contains("class ") || text.Contains("namespace ")))
                return false;

            // Don't suggest method implementations inside interfaces
            if (scope.Contains("interface") && text.Contains("{"))
                return false;

            return true;
        }

        private bool IsTriviaSuggestion(string text)
        {
            var trimmed = text.Trim();
            
            // Single characters that aren't meaningful
            if (trimmed.Length == 1 && "{}();,".Contains(trimmed))
                return true;
            
            // Just whitespace or basic punctuation
            if (Regex.IsMatch(trimmed, @"^[\s\{\}\(\);,\.]*$"))
                return true;
            
            return false;
        }

        private bool IsValidSyntax(string text, string language)
        {
            try
            {
                return language?.ToLowerInvariant() switch
                {
                    "csharp" => IsValidCSharpSyntax(text),
                    "javascript" or "typescript" => IsValidJavaScriptSyntax(text),
                    "python" => IsValidPythonSyntax(text),
                    "java" => IsValidJavaSyntax(text),
                    "cpp" or "c" => IsValidCppSyntax(text),
                    _ => true // Assume valid for unknown languages
                };
            }
            catch
            {
                return false;
            }
        }

        private async Task<bool> IsContextuallyValidAsync(CodeSuggestion suggestion, CodeContext context)
        {
            // Check if the suggestion makes sense given the cursor history
            if (context.CursorHistory?.Any() == true)
            {
                var recentFiles = context.CursorHistory
                    .Take(3)
                    .Select(h => System.IO.Path.GetFileName(h.FilePath))
                    .Distinct()
                    .ToList();

                // If we've been working in similar files, boost confidence for related suggestions
                if (recentFiles.Count > 1 && suggestion.Text.Length > 10)
                {
                    return true; // More lenient for multi-file contexts
                }
            }

            return true;
        }

        private bool ValidateIndentation(CodeSuggestion suggestion, CodeContext context)
        {
            if (context.Indentation == null)
                return true;

            var lines = suggestion.Text.Split('\n');
            if (lines.Length <= 1)
                return true;

            var expectedIndent = context.Indentation.UsesSpaces ? 
                new string(' ', context.Indentation.SpacesPerIndent) : 
                "\t";

            foreach (var line in lines.Skip(1)) // Skip first line
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                var leadingWhitespace = GetLeadingWhitespace(line);
                if (leadingWhitespace.Length > 0)
                {
                    var usesExpectedType = context.Indentation.UsesSpaces ? 
                        leadingWhitespace.All(c => c == ' ') : 
                        leadingWhitespace.All(c => c == '\t');

                    if (!usesExpectedType)
                        return false;
                }
            }

            return true;
        }

        #endregion

        #region Private Methods - Adaptation

        private string AdaptIndentation(string text, IndentationInfo indentation)
        {
            if (indentation == null)
                return text;

            var lines = text.Split('\n');
            var adaptedLines = new List<string>();

            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    adaptedLines.Add(line);
                    continue;
                }

                var leadingWhitespace = GetLeadingWhitespace(line);
                var content = line.Substring(leadingWhitespace.Length);

                if (leadingWhitespace.Length > 0)
                {
                    // Convert indentation to match project style
                    var indentLevel = EstimateIndentLevel(leadingWhitespace);
                    var newIndentation = indentation.UsesSpaces ? 
                        new string(' ', indentLevel * indentation.SpacesPerIndent) : 
                        new string('\t', indentLevel);

                    adaptedLines.Add(newIndentation + content);
                }
                else
                {
                    adaptedLines.Add(line);
                }
            }

            return string.Join("\n", adaptedLines);
        }

        private string AdaptNamingConventions(string text, string language)
        {
            // This is a simplified implementation
            // In a full version, this would analyze existing code to determine naming patterns
            return text;
        }

        private string AdaptBracketStyle(string text, CodeContext context)
        {
            // Analyze existing code to determine bracket style preferences
            // This is a simplified implementation
            return text;
        }

        #endregion

        #region Private Methods - Utilities

        private async Task<double> CalculateRankingScoreAsync(CodeSuggestion suggestion, CodeContext context)
        {
            var score = suggestion.Confidence;

            // Boost score based on suggestion type relevance
            if (IsCurrentlyInContext(suggestion.Type, context))
                score += 0.1;

            // Boost score for shorter, more immediately useful suggestions
            if (suggestion.Text.Length < 30)
                score += 0.05;

            // Boost score based on priority
            score += (suggestion.Priority / 100.0) * 0.1;

            return Math.Min(1.0, score);
        }

        private bool IsCurrentlyInContext(SuggestionType type, CodeContext context)
        {
            var currentLine = context.CurrentLine.Trim();

            return type switch
            {
                SuggestionType.Method => currentLine.Contains("(") && !currentLine.EndsWith(")"),
                SuggestionType.Comment => currentLine.StartsWith("//") || currentLine.StartsWith("/*"),
                SuggestionType.Import => currentLine.StartsWith("using") || currentLine.StartsWith("import"),
                SuggestionType.Type => currentLine.Contains("class") || currentLine.Contains("interface"),
                _ => true
            };
        }

        private string NormalizeSuggestionText(string text)
        {
            return Regex.Replace(text.Trim(), @"\s+", " ").ToLowerInvariant();
        }

        private double CalculateComplexity(string text)
        {
            var complexity = 0.0;
            var lines = text.Split('\n');

            complexity += lines.Length * 0.1; // More lines = more complex
            complexity += Regex.Matches(text, @"[{}()\[\]]").Count * 0.05; // Brackets add complexity
            complexity += Regex.Matches(text, @"\b(if|for|while|switch|try|catch)\b").Count * 0.1; // Control structures

            return Math.Min(1.0, complexity);
        }

        private string ExtractScopeName(string scope)
        {
            if (string.IsNullOrEmpty(scope))
                return string.Empty;

            var parts = scope.Split(' ', '>', '(');
            return parts.LastOrDefault(p => !string.IsNullOrWhiteSpace(p))?.Trim() ?? string.Empty;
        }

        private bool ContainsPlaceholders(string text)
        {
            return Regex.IsMatch(text, @"\$\{\w+\}|\$\d+|\[.*?\]|\<.*?\>");
        }

        private JumpRecommendation AnalyzeJumpHeuristic(CodeContext context)
        {
            // Simplified heuristic-based jump analysis
            var currentLine = context.CurrentLine.Trim();

            if (currentLine.EndsWith("{"))
            {
                return new JumpRecommendation
                {
                    TargetLine = context.LineNumber + 1,
                    TargetColumn = GetIndentationLevel(context.CurrentLine) + 4,
                    Direction = JumpDirection.Down,
                    Reason = "Jump inside code block",
                    Confidence = 0.7,
                    Type = JumpType.NextLogicalPosition
                };
            }

            if (currentLine.EndsWith(";") && !currentLine.Contains("="))
            {
                return new JumpRecommendation
                {
                    TargetLine = context.LineNumber + 1,
                    TargetColumn = 0,
                    Direction = JumpDirection.Down,
                    Reason = "Jump to next statement",
                    Confidence = 0.6,
                    Type = JumpType.NextLogicalPosition
                };
            }

            return new JumpRecommendation { Direction = JumpDirection.None };
        }

        private string GetLeadingWhitespace(string line)
        {
            var match = Regex.Match(line, @"^(\s*)");
            return match.Success ? match.Groups[1].Value : string.Empty;
        }

        private int EstimateIndentLevel(string whitespace)
        {
            if (whitespace.Contains('\t'))
                return whitespace.Count(c => c == '\t');
            else
                return whitespace.Length / 4; // Assume 4 spaces per indent
        }

        private int GetIndentationLevel(string line)
        {
            var leadingWhitespace = GetLeadingWhitespace(line);
            return EstimateIndentLevel(leadingWhitespace) * 4; // Return character count
        }

        #endregion

        #region Language-Specific Syntax Validation

        private bool IsValidCSharpSyntax(string text)
        {
            // Basic C# syntax validation
            var openBraces = text.Count(c => c == '{');
            var closeBraces = text.Count(c => c == '}');
            var openParens = text.Count(c => c == '(');
            var closeParens = text.Count(c => c == ')');

            return Math.Abs(openBraces - closeBraces) <= 1 && 
                   Math.Abs(openParens - closeParens) <= 1;
        }

        private bool IsValidJavaScriptSyntax(string text)
        {
            // Basic JavaScript syntax validation
            return IsValidCSharpSyntax(text); // Similar brace/paren rules
        }

        private bool IsValidPythonSyntax(string text)
        {
            // Basic Python syntax validation
            var openParens = text.Count(c => c == '(');
            var closeParens = text.Count(c => c == ')');
            var openBrackets = text.Count(c => c == '[');
            var closeBrackets = text.Count(c => c == ']');

            return Math.Abs(openParens - closeParens) <= 1 && 
                   Math.Abs(openBrackets - closeBrackets) <= 1;
        }

        private bool IsValidJavaSyntax(string text)
        {
            return IsValidCSharpSyntax(text);
        }

        private bool IsValidCppSyntax(string text)
        {
            return IsValidCSharpSyntax(text);
        }

        /// <summary>
        /// Debounced suggestion processing to prevent excessive API calls
        /// </summary>
        public async Task<CodeSuggestion> ProcessSuggestionDebouncedAsync(string aiResponse, CodeContext context, TimeSpan? debounceDelay = null)
        {
            if (_disposed)
                return new CodeSuggestion { Confidence = 0.0 };

            var delay = debounceDelay ?? TimeSpan.FromMilliseconds(_settingsService.RequestDebounceDelay);
            var debounceKey = $"suggestion_{context.FileName}_{context.LineNumber}_{context.CaretPosition}";

            return await _debounceService.DebounceAsync(debounceKey, async () =>
            {
                return await ProcessSuggestionAsync(aiResponse, context);
            }, delay);
        }

        /// <summary>
        /// Generates a cache key based on AI response and context
        /// </summary>
        private string GenerateCacheKey(string aiResponse, CodeContext context)
        {
            var contextHash = $"{context.FileName}_{context.Language}_{context.CurrentLine}_{context.CaretPosition}";
            var responseHash = aiResponse.GetHashCode().ToString();
            return $"{contextHash}_{responseHash}";
        }

        /// <summary>
        /// Gets cache statistics for monitoring
        /// </summary>
        public CacheStatistics GetCacheStatistics()
        {
            return _suggestionCache?.GetStatistics() ?? new CacheStatistics();
        }

        /// <summary>
        /// Clears the suggestion cache
        /// </summary>
        public void ClearCache()
        {
            _suggestionCache?.Clear();
        }

        #endregion

        #region IDisposable

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;

            try
            {
                _settingsService.SettingsChanged -= OnSettingsChanged;
                _suggestionCache?.Dispose();
                _debounceService?.Dispose();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error disposing SuggestionEngine: {ex.Message}");
            }
        }

        #endregion

        #region Event Handlers

        private void OnSettingsChanged(object sender, SettingsChangedEventArgs e)
        {
            if (e.SettingName == nameof(ISettingsService.MinimumConfidenceThreshold))
            {
                _minimumConfidenceThreshold = _settingsService.MinimumConfidenceThreshold;
            }
        }

        #endregion
    }
}