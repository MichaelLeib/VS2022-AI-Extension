using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using OllamaAssistant.Models;
using OllamaAssistant.Services.Interfaces;

namespace OllamaAssistant.Services.Implementation
{
    /// <summary>
    /// Service for advanced suggestion filtering and quality assessment
    /// </summary>
    public class SuggestionFilterService
    {
        private readonly ISettingsService _settingsService;
        private readonly Dictionary<string, SuggestionQualityMetrics> _qualityCache;
        private readonly Dictionary<string, DateTime> _userInteractions;
        private readonly object _lockObject = new object();

        public SuggestionFilterService(ISettingsService settingsService)
        {
            _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
            _qualityCache = new Dictionary<string, SuggestionQualityMetrics>();
            _userInteractions = new Dictionary<string, DateTime>();
        }

        #region Public Methods

        /// <summary>
        /// Applies comprehensive filtering to a collection of suggestions
        /// </summary>
        public CodeSuggestion[] ApplyFilters(CodeSuggestion[] suggestions, CodeContext context, FilterCriteria criteria = null)
        {
            if (suggestions == null || suggestions.Length == 0)
                return Array.Empty<CodeSuggestion>();

            criteria = CreateDefaultFilterCriteria();

            var filtered = suggestions.AsEnumerable();

            // Apply each filter
            filtered = ApplyConfidenceFilter(filtered, criteria);
            filtered = ApplyLanguageFilter(filtered, context, criteria);
            filtered = ApplyContextualFilter(filtered, context, criteria);
            filtered = ApplyLengthFilter(filtered, criteria);
            filtered = ApplyComplexityFilter(filtered, criteria);
            filtered = ApplyRelevanceFilter(filtered, context, criteria);
            filtered = ApplyDuplicateFilter(filtered, criteria);
            filtered = ApplyBlacklistFilter(filtered, criteria);
            filtered = ApplyUserPreferenceFilter(filtered, context, criteria);

            return filtered.ToArray();
        }

        /// <summary>
        /// Analyzes suggestion quality and provides detailed metrics
        /// </summary>
        public SuggestionQualityMetrics AnalyzeQuality(CodeSuggestion suggestion, CodeContext context)
        {
            if (suggestion == null || context == null)
                return new SuggestionQualityMetrics { OverallScore = 0.0 };

            var cacheKey = GenerateCacheKey(suggestion, context);
            
            lock (_lockObject)
            {
                if (_qualityCache.TryGetValue(cacheKey, out var cached))
                {
                    return cached;
                }
            }

            var metrics = new SuggestionQualityMetrics();

            // Analyze different quality aspects
            metrics.SyntaxScore = AnalyzeSyntaxQuality(suggestion, context);
            metrics.SemanticScore = AnalyzeSemanticQuality(suggestion, context);
            metrics.StyleScore = AnalyzeStyleQuality(suggestion, context);
            metrics.RelevanceScore = AnalyzeRelevanceQuality(suggestion, context);
            metrics.CompletenessScore = AnalyzeCompletenessQuality(suggestion, context);
            metrics.UsabilityScore = AnalyzeUsabilityQuality(suggestion, context);

            // Calculate overall score
            metrics.OverallScore = CalculateOverallScore(metrics);

            // Identify issues
            metrics.Issues = IdentifyIssues(suggestion, context, metrics);

            // Cache the result
            lock (_lockObject)
            {
                _qualityCache[cacheKey] = metrics;
            }

            return metrics;
        }

        /// <summary>
        /// Records user interaction with a suggestion for learning
        /// </summary>
        public void RecordUserInteraction(CodeSuggestion suggestion, UserInteractionType interaction, CodeContext context)
        {
            if (suggestion == null)
                return;

            var key = GenerateInteractionKey(suggestion, context);
            
            lock (_lockObject)
            {
                _userInteractions[key] = DateTime.Now;
                
                // Update suggestion confidence based on interaction
                UpdateSuggestionConfidence(suggestion, interaction);
                
                // Clean up old interactions
                CleanupOldInteractions();
            }
        }

        /// <summary>
        /// Gets suggestions that are likely to be accepted based on historical data
        /// </summary>
        public CodeSuggestion[] GetHighProbabilitySuggestions(CodeSuggestion[] suggestions, CodeContext context, int maxCount = 5)
        {
            if (suggestions == null || suggestions.Length == 0)
                return Array.Empty<CodeSuggestion>();

            var scored = suggestions
                .Select(s => new { Suggestion = s, Score = CalculateAcceptanceProbability(s, context) })
                .Where(x => x.Score > 0.6)
                .OrderByDescending(x => x.Score)
                .Take(maxCount)
                .Select(x => x.Suggestion)
                .ToArray();

            return scored;
        }

        /// <summary>
        /// Applies language-specific filtering rules
        /// </summary>
        public CodeSuggestion[] ApplyLanguageSpecificFilters(CodeSuggestion[] suggestions, CodeContext context)
        {
            if (suggestions == null || context == null)
                return suggestions ?? Array.Empty<CodeSuggestion>();

            return context.Language?.ToLowerInvariant() switch
            {
                "csharp" => ApplyCSharpFilters(suggestions, context),
                "javascript" or "typescript" => ApplyJavaScriptFilters(suggestions, context),
                "python" => ApplyPythonFilters(suggestions, context),
                "java" => ApplyJavaFilters(suggestions, context),
                "cpp" or "c" => ApplyCppFilters(suggestions, context),
                _ => suggestions
            };
        }

        #endregion

        #region Private Methods - Core Filtering

        private IEnumerable<CodeSuggestion> ApplyConfidenceFilter(IEnumerable<CodeSuggestion> suggestions, FilterCriteria criteria)
        {
            return suggestions.Where(s => s.Confidence >= criteria.MinimumConfidence);
        }

        private IEnumerable<CodeSuggestion> ApplyLanguageFilter(IEnumerable<CodeSuggestion> suggestions, CodeContext context, FilterCriteria criteria)
        {
            if (!criteria.EnableLanguageFiltering)
                return suggestions;

            return suggestions.Where(s => IsLanguageAppropriate(s, context));
        }

        private IEnumerable<CodeSuggestion> ApplyContextualFilter(IEnumerable<CodeSuggestion> suggestions, CodeContext context, FilterCriteria criteria)
        {
            if (!criteria.EnableContextualFiltering)
                return suggestions;

            return suggestions.Where(s => IsContextuallyAppropriate(s, context));
        }

        private IEnumerable<CodeSuggestion> ApplyLengthFilter(IEnumerable<CodeSuggestion> suggestions, FilterCriteria criteria)
        {
            return suggestions.Where(s => 
                s.Text.Length >= criteria.MinimumLength && 
                s.Text.Length <= criteria.MaximumLength);
        }

        private IEnumerable<CodeSuggestion> ApplyComplexityFilter(IEnumerable<CodeSuggestion> suggestions, FilterCriteria criteria)
        {
            return suggestions.Where(s => CalculateComplexity(s.Text) <= criteria.MaximumComplexity);
        }

        private IEnumerable<CodeSuggestion> ApplyRelevanceFilter(IEnumerable<CodeSuggestion> suggestions, CodeContext context, FilterCriteria criteria)
        {
            return suggestions.Where(s => CalculateRelevance(s, context) >= criteria.MinimumRelevance);
        }

        private IEnumerable<CodeSuggestion> ApplyDuplicateFilter(IEnumerable<CodeSuggestion> suggestions, FilterCriteria criteria)
        {
            if (!criteria.RemoveDuplicates)
                return suggestions;

            var seen = new HashSet<string>();
            return suggestions.Where(s =>
            {
                var normalized = NormalizeSuggestion(s.Text);
                return seen.Add(normalized);
            });
        }

        private IEnumerable<CodeSuggestion> ApplyBlacklistFilter(IEnumerable<CodeSuggestion> suggestions, FilterCriteria criteria)
        {
            if (criteria.BlacklistedPatterns == null || criteria.BlacklistedPatterns.Length == 0)
                return suggestions;

            return suggestions.Where(s => !IsBlacklisted(s.Text, criteria.BlacklistedPatterns));
        }

        private IEnumerable<CodeSuggestion> ApplyUserPreferenceFilter(IEnumerable<CodeSuggestion> suggestions, CodeContext context, FilterCriteria criteria)
        {
            if (!criteria.UseUserPreferences)
                return suggestions;

            return suggestions.Where(s => MatchesUserPreferences(s, context));
        }

        #endregion

        #region Private Methods - Quality Analysis

        private double AnalyzeSyntaxQuality(CodeSuggestion suggestion, CodeContext context)
        {
            var score = 1.0;
            var text = suggestion.Text;

            // Check bracket matching
            var openBraces = text.Count(c => c == '{');
            var closeBraces = text.Count(c => c == '}');
            var openParens = text.Count(c => c == '(');
            var closeParens = text.Count(c => c == ')');

            if (Math.Abs(openBraces - closeBraces) > 1) score -= 0.3;
            if (Math.Abs(openParens - closeParens) > 1) score -= 0.3;

            // Check for incomplete statements
            if (IsIncompleteStatement(text, context.Language)) score -= 0.2;

            // Check for syntax errors
            if (ContainsSyntaxErrors(text, context.Language)) score -= 0.4;

            return Math.Max(0.0, score);
        }

        private double AnalyzeSemanticQuality(CodeSuggestion suggestion, CodeContext context)
        {
            var score = 1.0;
            var text = suggestion.Text;

            // Check if suggestion makes semantic sense in context
            if (!IsSemanticallySensible(text, context)) score -= 0.3;

            // Check for meaningful variable/method names
            if (ContainsGenericNames(text)) score -= 0.2;

            // Check for appropriate scope usage
            if (!HasAppropriateScope(text, context)) score -= 0.2;

            return Math.Max(0.0, score);
        }

        private double AnalyzeStyleQuality(CodeSuggestion suggestion, CodeContext context)
        {
            var score = 1.0;
            var text = suggestion.Text;

            // Check indentation consistency
            if (!HasConsistentIndentation(text, context.Indentation)) score -= 0.3;

            // Check naming conventions
            if (!FollowsNamingConventions(text, context.Language)) score -= 0.2;

            // Check bracket style consistency
            if (!HasConsistentBracketStyle(text)) score -= 0.1;

            return Math.Max(0.0, score);
        }

        private double AnalyzeRelevanceQuality(CodeSuggestion suggestion, CodeContext context)
        {
            var score = 1.0;

            // Check relevance to current line
            if (!IsRelevantToCurrentLine(suggestion.Text, context.CurrentLine)) score -= 0.2;

            // Check relevance to current scope
            if (!IsRelevantToScope(suggestion.Text, context.CurrentScope)) score -= 0.2;

            // Check relevance to file type
            if (!IsRelevantToFileType(suggestion.Text, context.Language)) score -= 0.1;

            return Math.Max(0.0, score);
        }

        private double AnalyzeCompletenessQuality(CodeSuggestion suggestion, CodeContext context)
        {
            var score = 1.0;
            var text = suggestion.Text;

            // Check if suggestion is complete
            if (IsIncompleteCode(text)) score -= 0.4;

            // Check if suggestion addresses the immediate need
            if (!AddressesImmediateNeed(text, context)) score -= 0.3;

            return Math.Max(0.0, score);
        }

        private double AnalyzeUsabilityQuality(CodeSuggestion suggestion, CodeContext context)
        {
            var score = 1.0;
            var text = suggestion.Text;

            // Prefer shorter, more focused suggestions
            if (text.Length > 200) score -= 0.2;
            if (text.Length > 500) score -= 0.4;

            // Check for appropriate complexity
            var complexity = CalculateComplexity(text);
            if (complexity > 0.8) score -= 0.3;

            // Check for immediate applicability
            if (!IsImmediatelyApplicable(text, context)) score -= 0.2;

            return Math.Max(0.0, score);
        }

        private double CalculateOverallScore(SuggestionQualityMetrics metrics)
        {
            // Weighted average of different quality aspects
            return (metrics.SyntaxScore * 0.25) +
                   (metrics.SemanticScore * 0.2) +
                   (metrics.StyleScore * 0.15) +
                   (metrics.RelevanceScore * 0.2) +
                   (metrics.CompletenessScore * 0.15) +
                   (metrics.UsabilityScore * 0.05);
        }

        private List<string> IdentifyIssues(CodeSuggestion suggestion, CodeContext context, SuggestionQualityMetrics metrics)
        {
            var issues = new List<string>();

            if (metrics.SyntaxScore < 0.7) issues.Add("Potential syntax issues");
            if (metrics.SemanticScore < 0.7) issues.Add("Semantic concerns");
            if (metrics.StyleScore < 0.7) issues.Add("Style inconsistencies");
            if (metrics.RelevanceScore < 0.7) issues.Add("Low relevance to context");
            if (metrics.CompletenessScore < 0.7) issues.Add("Incomplete suggestion");
            if (metrics.UsabilityScore < 0.7) issues.Add("Usability concerns");

            return issues;
        }

        #endregion

        #region Private Methods - Language-Specific Filters

        private CodeSuggestion[] ApplyCSharpFilters(CodeSuggestion[] suggestions, CodeContext context)
        {
            return suggestions.Where(s =>
            {
                var text = s.Text;
                
                // Filter out suggestions that don't follow C# conventions
                if (text.Contains(" then ") || text.Contains(" end if")) return false;
                
                // Ensure proper semicolon usage
                if (NeedsSemicolon(text) && !text.TrimEnd().EndsWith(";")) return false;
                
                // Check for proper using statement format
                if (text.StartsWith("using") && !text.Contains(";") && !text.Contains("{")) return false;
                
                return true;
            }).ToArray();
        }

        private CodeSuggestion[] ApplyJavaScriptFilters(CodeSuggestion[] suggestions, CodeContext context)
        {
            return suggestions.Where(s =>
            {
                var text = s.Text;
                
                // Filter out suggestions with C#-specific syntax
                if (text.Contains("public ") || text.Contains("private ")) return false;
                
                // Ensure proper function syntax
                if (text.Contains("function") && !Regex.IsMatch(text, @"function\s+\w+\s*\(")) return false;
                
                return true;
            }).ToArray();
        }

        private CodeSuggestion[] ApplyPythonFilters(CodeSuggestion[] suggestions, CodeContext context)
        {
            return suggestions.Where(s =>
            {
                var text = s.Text;
                
                // Filter out suggestions with semicolons (not pythonic)
                if (text.Contains(";")) return false;
                
                // Filter out suggestions with braces
                if (text.Contains("{") || text.Contains("}")) return false;
                
                // Ensure proper indentation for Python
                if (text.Contains("\n") && !HasValidPythonIndentation(text)) return false;
                
                return true;
            }).ToArray();
        }

        private CodeSuggestion[] ApplyJavaFilters(CodeSuggestion[] suggestions, CodeContext context)
        {
            return suggestions.Where(s =>
            {
                var text = s.Text;
                
                // Similar to C# but with Java-specific rules
                if (text.Contains("var ")) return false; // Java doesn't have var keyword (pre-Java 10)
                
                // Ensure proper class naming conventions
                if (Regex.IsMatch(text, @"class\s+[a-z]")) return false; // Classes should start with uppercase
                
                return true;
            }).ToArray();
        }

        private CodeSuggestion[] ApplyCppFilters(CodeSuggestion[] suggestions, CodeContext context)
        {
            return suggestions.Where(s =>
            {
                var text = s.Text;
                
                // Filter out suggestions with C#/Java-specific syntax
                if (text.Contains("public:") && text.Contains("class")) return false;
                
                // Ensure proper include syntax
                if (text.StartsWith("#include") && !text.Contains("<") && !text.Contains("\"")) return false;
                
                return true;
            }).ToArray();
        }

        #endregion

        #region Private Methods - Utilities

        private FilterCriteria CreateDefaultFilterCriteria()
        {
            return new FilterCriteria
            {
                MinimumConfidence = _settingsService.MinimumConfidenceThreshold,
                MinimumLength = 1,
                MaximumLength = 500,
                MaximumComplexity = 0.8,
                MinimumRelevance = 0.3,
                EnableLanguageFiltering = true,
                EnableContextualFiltering = true,
                RemoveDuplicates = true,
                UseUserPreferences = true,
                BlacklistedPatterns = new[] { "TODO", "FIXME", "XXX", "HACK" }
            };
        }

        private double CalculateAcceptanceProbability(CodeSuggestion suggestion, CodeContext context)
        {
            var base_probability = suggestion.Confidence;
            
            // Adjust based on historical user interactions
            var interactionKey = GenerateInteractionKey(suggestion, context);
            if (_userInteractions.ContainsKey(interactionKey))
            {
                base_probability += 0.1; // Boost for previously interacted suggestions
            }
            
            // Adjust based on suggestion type
            base_probability += suggestion.Type switch
            {
                SuggestionType.Method => 0.1,
                SuggestionType.Variable => 0.05,
                SuggestionType.Comment => -0.05,
                _ => 0.0
            };
            
            return Math.Max(0.0, Math.Min(1.0, base_probability));
        }

        private void UpdateSuggestionConfidence(CodeSuggestion suggestion, UserInteractionType interaction)
        {
            switch (interaction)
            {
                case UserInteractionType.Accepted:
                    suggestion.Confidence = Math.Min(1.0, suggestion.Confidence + 0.1);
                    break;
                case UserInteractionType.Rejected:
                    suggestion.Confidence = Math.Max(0.0, suggestion.Confidence - 0.05);
                    break;
                case UserInteractionType.Modified:
                    // Neutral adjustment - user found it useful but needed changes
                    break;
            }
        }

        private void CleanupOldInteractions()
        {
            var cutoff = DateTime.Now - TimeSpan.FromDays(7);
            var keysToRemove = _userInteractions
                .Where(kvp => kvp.Value < cutoff)
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var key in keysToRemove)
            {
                _userInteractions.Remove(key);
            }
        }

        private string GenerateCacheKey(CodeSuggestion suggestion, CodeContext context)
        {
            return $"{suggestion.Text.GetHashCode()}_{context.Language}_{context.CurrentScope?.GetHashCode()}";
        }

        private string GenerateInteractionKey(CodeSuggestion suggestion, CodeContext context)
        {
            return $"{NormalizeSuggestion(suggestion.Text)}_{context.Language}";
        }

        // Additional utility methods would go here...
        private bool IsLanguageAppropriate(CodeSuggestion suggestion, CodeContext context) => true; // Simplified
        private bool IsContextuallyAppropriate(CodeSuggestion suggestion, CodeContext context) => true; // Simplified
        private double CalculateComplexity(string text) => Math.Min(1.0, text.Length / 200.0); // Simplified
        private double CalculateRelevance(CodeSuggestion suggestion, CodeContext context) => suggestion.Confidence; // Simplified
        private string NormalizeSuggestion(string text) => text.Trim().ToLowerInvariant();
        private bool IsBlacklisted(string text, string[] patterns) => patterns.Any(p => text.Contains(p));
        private bool MatchesUserPreferences(CodeSuggestion suggestion, CodeContext context) => true; // Simplified
        private bool IsIncompleteStatement(string text, string language) => false; // Simplified
        private bool ContainsSyntaxErrors(string text, string language) => false; // Simplified
        private bool IsSemanticallySensible(string text, CodeContext context) => true; // Simplified
        private bool ContainsGenericNames(string text) => text.Contains("temp") || text.Contains("foo"); // Simplified
        private bool HasAppropriateScope(string text, CodeContext context) => true; // Simplified
        private bool HasConsistentIndentation(string text, IndentationInfo indentation) => true; // Simplified
        private bool FollowsNamingConventions(string text, string language) => true; // Simplified
        private bool HasConsistentBracketStyle(string text) => true; // Simplified
        private bool IsRelevantToCurrentLine(string suggestion, string currentLine) => true; // Simplified
        private bool IsRelevantToScope(string suggestion, string scope) => true; // Simplified
        private bool IsRelevantToFileType(string suggestion, string language) => true; // Simplified
        private bool IsIncompleteCode(string text) => text.EndsWith("...") || text.Contains("TODO"); // Simplified
        private bool AddressesImmediateNeed(string text, CodeContext context) => true; // Simplified
        private bool IsImmediatelyApplicable(string text, CodeContext context) => true; // Simplified
        private bool NeedsSemicolon(string text) => !text.Contains("{") && !text.StartsWith("//"); // Simplified
        private bool HasValidPythonIndentation(string text) => true; // Simplified

        #endregion
    }

    #region Data Models

    /// <summary>
    /// Criteria for filtering suggestions
    /// </summary>
    public class FilterCriteria
    {
        public double MinimumConfidence { get; set; } = 0.5;
        public int MinimumLength { get; set; } = 1;
        public int MaximumLength { get; set; } = 500;
        public double MaximumComplexity { get; set; } = 1.0;
        public double MinimumRelevance { get; set; } = 0.0;
        public bool EnableLanguageFiltering { get; set; } = true;
        public bool EnableContextualFiltering { get; set; } = true;
        public bool RemoveDuplicates { get; set; } = true;
        public bool UseUserPreferences { get; set; } = true;
        public string[] BlacklistedPatterns { get; set; } = Array.Empty<string>();
    }

    /// <summary>
    /// Detailed quality metrics for a suggestion
    /// </summary>
    public class SuggestionQualityMetrics
    {
        public double SyntaxScore { get; set; }
        public double SemanticScore { get; set; }
        public double StyleScore { get; set; }
        public double RelevanceScore { get; set; }
        public double CompletenessScore { get; set; }
        public double UsabilityScore { get; set; }
        public double OverallScore { get; set; }
        public List<string> Issues { get; set; } = new List<string>();
    }

    /// <summary>
    /// Types of user interactions with suggestions
    /// </summary>
    public enum UserInteractionType
    {
        Accepted,
        Rejected,
        Modified,
        Ignored
    }

    #endregion
}