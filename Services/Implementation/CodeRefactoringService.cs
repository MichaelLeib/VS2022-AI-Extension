using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using OllamaAssistant.Services.Interfaces;
using OllamaAssistant.Models;
using ValidationResult = OllamaAssistant.Models.ValidationResult;

namespace OllamaAssistant.Services.Implementation
{
    /// <summary>
    /// Service for AI-powered code refactoring suggestions and analysis
    /// </summary>
    public class CodeRefactoringService : ICodeRefactoringService
    {
        private readonly IOllamaService _ollamaService;
        private readonly IAdvancedContextAnalysisService _contextAnalysisService;
        private readonly ILogger _logger;
        private readonly ISettingsService _settingsService;
        private readonly Dictionary<string, RefactoringAnalyzer> _languageAnalyzers;

        public CodeRefactoringService(
            IOllamaService ollamaService,
            IAdvancedContextAnalysisService contextAnalysisService,
            ILogger logger,
            ISettingsService settingsService)
        {
            _ollamaService = ollamaService ?? throw new ArgumentNullException(nameof(ollamaService));
            _contextAnalysisService = contextAnalysisService ?? throw new ArgumentNullException(nameof(contextAnalysisService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));

            _languageAnalyzers = InitializeLanguageAnalyzers();
        }

        /// <summary>
        /// Analyzes code and provides AI-powered refactoring suggestions
        /// </summary>
        public async Task<List<RefactoringSuggestion>> GetRefactoringSuggestionsAsync(
            string filePath, 
            string codeContent, 
            TextSpan selectedSpan = default,
            CancellationToken cancellationToken = default)
        {
            try
            {
                _logger.LogDebug("Starting refactoring analysis", new { FilePath = filePath, HasSelection = selectedSpan != default });

                var suggestions = new List<RefactoringSuggestion>();
                var language = DetermineLanguage(filePath);
                
                // Get semantic analysis of the code
                var context = await _contextAnalysisService.AnalyzeContextAsync(
                    filePath, 
                    GetLineFromSpan(codeContent, selectedSpan), 
                    GetColumnFromSpan(codeContent, selectedSpan));

                // Analyze code structure and patterns
                var structuralAnalysis = await AnalyzeCodeStructureAsync(codeContent, language, selectedSpan);
                
                // Get AI-powered suggestions
                var aiSuggestions = await GetAISuggestionsAsync(codeContent, language, selectedSpan, context, cancellationToken);
                suggestions.AddRange(aiSuggestions);

                // Add language-specific refactoring suggestions
                if (_languageAnalyzers.TryGetValue(language, out var analyzer))
                {
                    var languageSpecific = await analyzer.AnalyzeAsync(codeContent, selectedSpan, structuralAnalysis);
                    suggestions.AddRange(languageSpecific);
                }

                // Rank and filter suggestions by confidence and relevance
                var rankedSuggestions = RankSuggestions(suggestions, structuralAnalysis);

                _logger.LogInfo($"Generated {rankedSuggestions.Count} refactoring suggestions for {filePath}");
                return rankedSuggestions.Take(GetMaxSuggestions()).ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError("Error generating refactoring suggestions", ex);
                return new List<RefactoringSuggestion>();
            }
        }

        /// <summary>
        /// Applies a specific refactoring suggestion to the code
        /// </summary>
        public async Task<RefactoringResult> ApplyRefactoringAsync(
            string filePath,
            string codeContent,
            RefactoringSuggestion suggestion,
            CancellationToken cancellationToken = default)
        {
            try
            {
                _logger.LogDebug("Applying refactoring", new { FilePath = filePath, SuggestionType = suggestion.Type });

                var language = DetermineLanguage(filePath);
                
                // Validate the suggestion is still applicable
                var isValid = await ValidateRefactoringSuggestionAsync(codeContent, suggestion);
                if (!isValid)
                {
                    return RefactoringResult.Failed("The refactoring is no longer applicable due to code changes.");
                }

                // Apply the refactoring based on type
                var refactoredCode = suggestion.Type switch
                {
                    RefactoringType.ExtractMethod => await ApplyExtractMethodAsync(codeContent, suggestion, language),
                    RefactoringType.RenameVariable => await ApplyRenameVariableAsync(codeContent, suggestion, language),
                    RefactoringType.SimplifyExpression => await ApplySimplifyExpressionAsync(codeContent, suggestion, language),
                    RefactoringType.OptimizeImports => await ApplyOptimizeImportsAsync(codeContent, suggestion, language),
                    RefactoringType.ConvertToLinq => await ApplyConvertToLinqAsync(codeContent, suggestion, language),
                    RefactoringType.AddNullChecks => await ApplyAddNullChecksAsync(codeContent, suggestion, language),
                    RefactoringType.ImproveNaming => await ApplyImproveNamingAsync(codeContent, suggestion, language),
                    RefactoringType.ReduceComplexity => await ApplyReduceComplexityAsync(codeContent, suggestion, language),
                    _ => await ApplyGenericRefactoringAsync(codeContent, suggestion, language, cancellationToken)
                };

                if (string.IsNullOrEmpty(refactoredCode) || refactoredCode == codeContent)
                {
                    return RefactoringResult.Failed("Failed to apply refactoring transformation.");
                }

                // Validate the refactored code
                var validationResult = await ValidateRefactoredCodeAsync(refactoredCode, language);
                if (!validationResult.IsValid)
                {
                    return RefactoringResult.Failed($"Refactored code is invalid: {validationResult.ErrorMessage}");
                }

                _logger.LogInfo("Successfully applied refactoring", new { FilePath = filePath, SuggestionType = suggestion.Type });

                return RefactoringResult.Success(refactoredCode, CalculateChanges(codeContent, refactoredCode));
            }
            catch (Exception ex)
            {
                _logger.LogError("Error applying refactoring", ex);
                return RefactoringResult.Failed($"Error applying refactoring: {ex.Message}");
            }
        }

        /// <summary>
        /// Gets performance metrics for refactoring operations
        /// </summary>
        public RefactoringMetrics GetRefactoringMetrics()
        {
            return new RefactoringMetrics
            {
                TotalSuggestionsGenerated = _totalSuggestions,
                SuggestionsApplied = _appliedSuggestions,
                AverageAnalysisTimeMs = _averageAnalysisTime,
                SuccessRate = _totalSuggestions > 0 ? (double)_appliedSuggestions / _totalSuggestions : 0,
                TopRefactoringTypes = _refactoringTypeCounters.OrderByDescending(x => x.Value).Take(5).ToDictionary(x => x.Key, x => x.Value)
            };
        }

        #region Private Fields for Metrics
        private int _totalSuggestions = 0;
        private int _appliedSuggestions = 0;
        private double _averageAnalysisTime = 0;
        private readonly Dictionary<RefactoringType, int> _refactoringTypeCounters = new();
        #endregion

        #region Private Methods

        private Dictionary<string, RefactoringAnalyzer> InitializeLanguageAnalyzers()
        {
            return new Dictionary<string, RefactoringAnalyzer>
            {
                ["csharp"] = new CSharpRefactoringAnalyzer(_logger),
                ["javascript"] = new JavaScriptRefactoringAnalyzer(_logger),
                ["typescript"] = new TypeScriptRefactoringAnalyzer(_logger),
                ["python"] = new PythonRefactoringAnalyzer(_logger),
                ["java"] = new JavaRefactoringAnalyzer(_logger)
            };
        }

        private string DetermineLanguage(string filePath)
        {
            var extension = System.IO.Path.GetExtension(filePath).ToLowerInvariant();
            return extension switch
            {
                ".cs" => "csharp",
                ".js" => "javascript",
                ".ts" => "typescript",
                ".py" => "python",
                ".java" => "java",
                ".cpp" or ".cc" or ".cxx" => "cpp",
                ".c" => "c",
                ".go" => "go",
                ".rs" => "rust",
                _ => "unknown"
            };
        }

        private async Task<CodeStructuralAnalysis> AnalyzeCodeStructureAsync(
            string codeContent, 
            string language, 
            TextSpan selectedSpan)
        {
            // Analyze code metrics like complexity, maintainability, etc.
            var analysis = new CodeStructuralAnalysis
            {
                Language = language,
                LinesOfCode = codeContent.Split('\n').Length,
                CyclomaticComplexity = CalculateCyclomaticComplexity(codeContent, language),
                CodeDuplication = DetectCodeDuplication(codeContent),
                NamingIssues = DetectNamingIssues(codeContent, language),
                PerformanceIssues = DetectPerformanceIssues(codeContent, language),
                SecurityIssues = DetectSecurityIssues(codeContent, language)
            };

            if (language == "csharp")
            {
                await AnalyzeCSharpSpecifics(codeContent, analysis);
            }

            return analysis;
        }

        private async Task<List<RefactoringSuggestion>> GetAISuggestionsAsync(
            string codeContent,
            string language,
            TextSpan selectedSpan,
            AdvancedCodeContext context,
            CancellationToken cancellationToken)
        {
            var prompt = BuildRefactoringPrompt(codeContent, language, selectedSpan, context);
            
            var aiContext = new CodeContext
            {
                FilePath = context.FilePath,
                Language = language,
                Code = selectedSpan == default ? codeContent : codeContent.Substring(selectedSpan.Start, selectedSpan.Length),
                CursorPosition = new CursorPosition { Line = context.Line, Column = context.Column },
                SurroundingCode = codeContent
            };

            var response = await _ollamaService.GetCodeSuggestionAsync(aiContext, cancellationToken);
            
            return ParseAIRefactoringSuggestions(response, selectedSpan);
        }

        private string BuildRefactoringPrompt(string codeContent, string language, TextSpan selectedSpan, AdvancedCodeContext context)
        {
            var codeToAnalyze = selectedSpan == default ? codeContent : codeContent.Substring(selectedSpan.Start, selectedSpan.Length);
            
            return $@"Analyze the following {language} code and suggest specific refactoring improvements:

Code to analyze:
```{language}
{codeToAnalyze}
```

Context information:
- File: {context.FilePath}
- Semantic info: {string.Join(", ", context.SemanticInfo.Select(s => s.Name))}
- Dependencies: {string.Join(", ", context.Dependencies.Take(5))}

Please provide refactoring suggestions in this JSON format:
[
  {{
    ""type"": ""ExtractMethod|RenameVariable|SimplifyExpression|OptimizeImports|ConvertToLinq|AddNullChecks|ImproveNaming|ReduceComplexity"",
    ""title"": ""Brief description"",
    ""description"": ""Detailed explanation"",
    ""impact"": ""Low|Medium|High"",
    ""confidence"": 0.95,
    ""startPosition"": 0,
    ""endPosition"": 100,
    ""originalCode"": ""code to replace"",
    ""refactoredCode"": ""improved code"",
    ""benefits"": [""benefit1"", ""benefit2""]
  }}
]

Focus on:
1. Code readability and maintainability
2. Performance improvements
3. Following language best practices
4. Reducing complexity
5. Improving naming conventions";
        }

        private List<RefactoringSuggestion> ParseAIRefactoringSuggestions(CodeSuggestion aiResponse, TextSpan selectedSpan)
        {
            var suggestions = new List<RefactoringSuggestion>();
            
            try
            {
                // Parse JSON response from AI
                var jsonResponse = ExtractJsonFromResponse(aiResponse.Text);
                if (string.IsNullOrEmpty(jsonResponse))
                {
                    return suggestions;
                }

                dynamic suggestionsData = Newtonsoft.Json.JsonConvert.DeserializeObject(jsonResponse);
                
                foreach (var item in suggestionsData)
                {
                    var suggestion = new RefactoringSuggestion
                    {
                        Id = Guid.NewGuid().ToString(),
                        Type = ParseRefactoringType(item.type?.ToString()),
                        Title = item.title?.ToString() ?? "Refactoring Suggestion",
                        Description = item.description?.ToString() ?? "",
                        Impact = ParseImpactLevel(item.impact?.ToString()),
                        Confidence = (double)(item.confidence ?? 0.7),
                        TargetSpan = new TextSpan(
                            (int)(item.startPosition ?? selectedSpan.Start),
                            (int)(item.endPosition ?? selectedSpan.End) - (int)(item.startPosition ?? selectedSpan.Start)
                        ),
                        OriginalCode = item.originalCode?.ToString() ?? "",
                        RefactoredCode = item.refactoredCode?.ToString() ?? "",
                        Benefits = item.benefits?.ToObject<List<string>>() ?? new List<string>(),
                        EstimatedSavings = new RefactoringBenefits
                        {
                            ReadabilityImprovement = CalculateReadabilityImprovement(item),
                            PerformanceGain = CalculatePerformanceGain(item),
                            MaintainabilityIncrease = CalculateMaintainabilityIncrease(item)
                        }
                    };

                    suggestions.Add(suggestion);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Failed to parse AI refactoring suggestions", ex);
            }

            return suggestions;
        }

        private string ExtractJsonFromResponse(string response)
        {
            // Extract JSON from AI response that might contain additional text
            var jsonStart = response.IndexOf('[');
            var jsonEnd = response.LastIndexOf(']');
            
            if (jsonStart >= 0 && jsonEnd > jsonStart)
            {
                return response.Substring(jsonStart, jsonEnd - jsonStart + 1);
            }
            
            return null;
        }

        private RefactoringType ParseRefactoringType(string typeString)
        {
            return typeString?.ToLowerInvariant() switch
            {
                "extractmethod" => RefactoringType.ExtractMethod,
                "renamevariable" => RefactoringType.RenameVariable,
                "simplifyexpression" => RefactoringType.SimplifyExpression,
                "optimizeimports" => RefactoringType.OptimizeImports,
                "converttolinq" => RefactoringType.ConvertToLinq,
                "addnullchecks" => RefactoringType.AddNullChecks,
                "improvenaming" => RefactoringType.ImproveNaming,
                "reducecomplexity" => RefactoringType.ReduceComplexity,
                _ => RefactoringType.General
            };
        }

        private ImpactLevel ParseImpactLevel(string impactString)
        {
            return impactString?.ToLowerInvariant() switch
            {
                "high" => ImpactLevel.High,
                "medium" => ImpactLevel.Medium,
                "low" => ImpactLevel.Low,
                _ => ImpactLevel.Low
            };
        }

        private List<RefactoringSuggestion> RankSuggestions(
            List<RefactoringSuggestion> suggestions, 
            CodeStructuralAnalysis structuralAnalysis)
        {
            return suggestions
                .Where(s => s.Confidence >= GetMinimumConfidence())
                .OrderByDescending(s => CalculateSuggestionScore(s, structuralAnalysis))
                .ToList();
        }

        private double CalculateSuggestionScore(RefactoringSuggestion suggestion, CodeStructuralAnalysis analysis)
        {
            double score = suggestion.Confidence * 100;
            
            // Boost score based on impact
            score += suggestion.Impact switch
            {
                ImpactLevel.High => 30,
                ImpactLevel.Medium => 20,
                ImpactLevel.Low => 10,
                _ => 5
            };

            // Boost score based on code quality issues
            if (analysis.CyclomaticComplexity > 10 && suggestion.Type == RefactoringType.ReduceComplexity)
                score += 25;
            
            if (analysis.NamingIssues.Any() && suggestion.Type == RefactoringType.ImproveNaming)
                score += 20;

            return score;
        }

        private async Task<string> ApplyExtractMethodAsync(string codeContent, RefactoringSuggestion suggestion, string language)
        {
            // Implementation for extract method refactoring
            if (language == "csharp" && !string.IsNullOrEmpty(suggestion.RefactoredCode))
            {
                return ReplaceCodeSpan(codeContent, suggestion.TargetSpan, suggestion.RefactoredCode);
            }
            
            return await ApplyGenericRefactoringAsync(codeContent, suggestion, language);
        }

        private async Task<string> ApplyRenameVariableAsync(string codeContent, RefactoringSuggestion suggestion, string language)
        {
            // Implementation for rename variable refactoring
            if (!string.IsNullOrEmpty(suggestion.RefactoredCode))
            {
                return ReplaceCodeSpan(codeContent, suggestion.TargetSpan, suggestion.RefactoredCode);
            }
            
            return codeContent;
        }

        private async Task<string> ApplySimplifyExpressionAsync(string codeContent, RefactoringSuggestion suggestion, string language)
        {
            // Implementation for simplify expression refactoring
            return ReplaceCodeSpan(codeContent, suggestion.TargetSpan, suggestion.RefactoredCode);
        }

        private async Task<string> ApplyOptimizeImportsAsync(string codeContent, RefactoringSuggestion suggestion, string language)
        {
            // Implementation for optimize imports refactoring
            if (language == "csharp")
            {
                return await OptimizeCSharpUsings(codeContent);
            }
            
            return ReplaceCodeSpan(codeContent, suggestion.TargetSpan, suggestion.RefactoredCode);
        }

        private async Task<string> ApplyConvertToLinqAsync(string codeContent, RefactoringSuggestion suggestion, string language)
        {
            // Implementation for convert to LINQ refactoring
            return ReplaceCodeSpan(codeContent, suggestion.TargetSpan, suggestion.RefactoredCode);
        }

        private async Task<string> ApplyAddNullChecksAsync(string codeContent, RefactoringSuggestion suggestion, string language)
        {
            // Implementation for add null checks refactoring
            return ReplaceCodeSpan(codeContent, suggestion.TargetSpan, suggestion.RefactoredCode);
        }

        private async Task<string> ApplyImproveNamingAsync(string codeContent, RefactoringSuggestion suggestion, string language)
        {
            // Implementation for improve naming refactoring
            return ReplaceCodeSpan(codeContent, suggestion.TargetSpan, suggestion.RefactoredCode);
        }

        private async Task<string> ApplyReduceComplexityAsync(string codeContent, RefactoringSuggestion suggestion, string language)
        {
            // Implementation for reduce complexity refactoring
            return ReplaceCodeSpan(codeContent, suggestion.TargetSpan, suggestion.RefactoredCode);
        }

        private async Task<string> ApplyGenericRefactoringAsync(
            string codeContent, 
            RefactoringSuggestion suggestion, 
            string language, 
            CancellationToken cancellationToken = default)
        {
            // Use AI to apply generic refactoring
            var prompt = $@"Apply the following refactoring to the {language} code:

Original code:
{suggestion.OriginalCode}

Refactoring instruction: {suggestion.Description}

Please provide only the refactored code without explanation.";

            var aiContext = new CodeContext
            {
                Language = language,
                Code = suggestion.OriginalCode,
                CursorPosition = new CursorPosition { Line = 0, Column = 0 }
            };

            var response = await _ollamaService.GetCodeSuggestionAsync(aiContext, cancellationToken);
            
            if (!string.IsNullOrEmpty(response?.Text))
            {
                var refactoredCode = ExtractCodeFromResponse(response.Text, language);
                return ReplaceCodeSpan(codeContent, suggestion.TargetSpan, refactoredCode);
            }

            return codeContent;
        }

        private string ReplaceCodeSpan(string originalCode, TextSpan span, string newCode)
        {
            if (span.Start < 0 || span.End > originalCode.Length)
                return originalCode;

            return originalCode.Substring(0, span.Start) + newCode + originalCode.Substring(span.End);
        }

        private string ExtractCodeFromResponse(string response, string language)
        {
            // Extract code block from AI response
            var codeBlockStart = response.IndexOf($"```{language}");
            if (codeBlockStart == -1)
                codeBlockStart = response.IndexOf("```");
            
            if (codeBlockStart != -1)
            {
                var codeStart = response.IndexOf('\n', codeBlockStart) + 1;
                var codeEnd = response.IndexOf("```", codeStart);
                
                if (codeEnd != -1)
                {
                    return response.Substring(codeStart, codeEnd - codeStart).Trim();
                }
            }

            // If no code block found, return the entire response trimmed
            return response.Trim();
        }

        private async Task<ValidationResult> ValidateRefactoredCodeAsync(string code, string language)
        {
            try
            {
                if (language == "csharp")
                {
                    var syntaxTree = CSharpSyntaxTree.ParseText(code);
                    var diagnostics = syntaxTree.GetDiagnostics();
                    
                    var errors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
                    if (errors.Any())
                    {
                        return ValidationResult.Invalid($"Syntax errors: {string.Join(", ", errors.Select(e => e.GetMessage()))}");
                    }
                }

                return ValidationResult.Valid();
            }
            catch (Exception ex)
            {
                return ValidationResult.Invalid($"Validation error: {ex.Message}");
            }
        }

        private async Task<bool> ValidateRefactoringSuggestionAsync(string codeContent, RefactoringSuggestion suggestion)
        {
            // Check if the original code still matches what's expected
            if (suggestion.TargetSpan.End > codeContent.Length)
                return false;

            var currentCode = codeContent.Substring(suggestion.TargetSpan.Start, suggestion.TargetSpan.Length);
            return string.Equals(currentCode.Trim(), suggestion.OriginalCode.Trim(), StringComparison.OrdinalIgnoreCase);
        }

        private List<TextChange> CalculateChanges(string originalCode, string refactoredCode)
        {
            // Simple diff calculation - in production, use a proper diff algorithm
            var changes = new List<TextChange>();
            
            if (originalCode != refactoredCode)
            {
                changes.Add(new TextChange
                {
                    Span = new TextSpan(0, originalCode.Length),
                    NewText = refactoredCode
                });
            }

            return changes;
        }

        private int GetLineFromSpan(string code, TextSpan span)
        {
            if (span == default) return 0;
            return code.Take(span.Start).Count(c => c == '\n');
        }

        private int GetColumnFromSpan(string code, TextSpan span)
        {
            if (span == default) return 0;
            var lastNewLine = code.LastIndexOf('\n', span.Start);
            return span.Start - (lastNewLine + 1);
        }

        private double GetMinimumConfidence()
        {
            return _settingsService.GetSetting("RefactoringMinimumConfidence", 0.6);
        }

        private int GetMaxSuggestions()
        {
            return _settingsService.GetSetting("RefactoringMaxSuggestions", 10);
        }

        #region Code Analysis Methods

        private int CalculateCyclomaticComplexity(string code, string language)
        {
            // Simple complexity calculation based on control flow keywords
            var complexityKeywords = new[] { "if", "else", "while", "for", "foreach", "switch", "case", "catch", "&&", "||", "?" };
            
            return complexityKeywords.Sum(keyword => 
                System.Text.RegularExpressions.Regex.Matches(code, $@"\b{keyword}\b").Count) + 1;
        }

        private List<string> DetectCodeDuplication(string code)
        {
            var duplications = new List<string>();
            var lines = code.Split('\n').Select(l => l.Trim()).Where(l => !string.IsNullOrEmpty(l)).ToArray();
            
            for (int i = 0; i < lines.Length - 2; i++)
            {
                for (int j = i + 3; j < lines.Length - 2; j++)
                {
                    if (lines[i] == lines[j] && lines[i + 1] == lines[j + 1] && lines[i + 2] == lines[j + 2])
                    {
                        duplications.Add($"Duplicate code block at lines {i + 1} and {j + 1}");
                    }
                }
            }
            
            return duplications;
        }

        private List<string> DetectNamingIssues(string code, string language)
        {
            var issues = new List<string>();
            
            if (language == "csharp")
            {
                // Check for Hungarian notation
                var hungarianMatches = System.Text.RegularExpressions.Regex.Matches(code, @"\b[a-z]{1,3}[A-Z]\w*");
                foreach (System.Text.RegularExpressions.Match match in hungarianMatches)
                {
                    issues.Add($"Possible Hungarian notation: {match.Value}");
                }
                
                // Check for single letter variables (except common ones like i, j, k)
                var singleLetterMatches = System.Text.RegularExpressions.Regex.Matches(code, @"\b[a-z]\b")
                    .Cast<System.Text.RegularExpressions.Match>()
                    .Where(m => !new[] { "i", "j", "k", "x", "y", "z" }.Contains(m.Value));
                
                foreach (var match in singleLetterMatches)
                {
                    issues.Add($"Single letter variable: {match.Value}");
                }
            }
            
            return issues;
        }

        private List<string> DetectPerformanceIssues(string code, string language)
        {
            var issues = new List<string>();
            
            if (language == "csharp")
            {
                // String concatenation in loops
                if (code.Contains("for") || code.Contains("while") || code.Contains("foreach"))
                {
                    if (code.Contains("+=") && code.Contains("string"))
                    {
                        issues.Add("String concatenation in loop - consider StringBuilder");
                    }
                }
                
                // Multiple enumeration
                var linqChains = System.Text.RegularExpressions.Regex.Matches(code, @"\.Where\(.*?\)\..*?\.Where\(.*?\)");
                if (linqChains.Count > 0)
                {
                    issues.Add("Multiple LINQ enumerations - consider combining conditions");
                }
            }
            
            return issues;
        }

        private List<string> DetectSecurityIssues(string code, string language)
        {
            var issues = new List<string>();
            
            // SQL injection patterns
            if (code.Contains("SELECT") && code.Contains("+"))
            {
                issues.Add("Possible SQL injection vulnerability");
            }
            
            // Hardcoded credentials
            var credentialPatterns = new[] { "password", "pwd", "secret", "key", "token" };
            foreach (var pattern in credentialPatterns)
            {
                if (System.Text.RegularExpressions.Regex.IsMatch(code, $@"{pattern}\s*=\s*""[^""]+""", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                {
                    issues.Add($"Possible hardcoded {pattern}");
                }
            }
            
            return issues;
        }

        private async Task AnalyzeCSharpSpecifics(string code, CodeStructuralAnalysis analysis)
        {
            try
            {
                var syntaxTree = CSharpSyntaxTree.ParseText(code);
                var root = await syntaxTree.GetRootAsync();
                
                // Count methods and classes
                analysis.MethodCount = root.DescendantNodes().OfType<MethodDeclarationSyntax>().Count();
                analysis.ClassCount = root.DescendantNodes().OfType<ClassDeclarationSyntax>().Count();
                
                // Analyze method lengths
                var methods = root.DescendantNodes().OfType<MethodDeclarationSyntax>();
                analysis.LongMethods = methods.Where(m => m.ToString().Split('\n').Length > 20).Count();
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Failed to analyze C# specifics", ex);
            }
        }

        private async Task<string> OptimizeCSharpUsings(string code)
        {
            try
            {
                var syntaxTree = CSharpSyntaxTree.ParseText(code);
                var root = await syntaxTree.GetRootAsync();
                
                var usings = root.DescendantNodes().OfType<UsingDirectiveSyntax>().ToList();
                var sortedUsings = usings.OrderBy(u => u.Name.ToString()).ToList();
                
                // Remove duplicates and sort
                var uniqueUsings = sortedUsings.GroupBy(u => u.Name.ToString()).Select(g => g.First()).ToList();
                
                // Replace usings in code
                var newRoot = root.ReplaceNodes(usings, (original, replacement) => 
                    uniqueUsings.Contains(original) ? original : null);
                
                return newRoot.ToFullString();
            }
            catch
            {
                return code;
            }
        }

        private double CalculateReadabilityImprovement(dynamic item)
        {
            // Calculate based on suggestion type and content
            return (double)(item.readabilityScore ?? 0.1);
        }

        private double CalculatePerformanceGain(dynamic item)
        {
            return (double)(item.performanceScore ?? 0.05);
        }

        private double CalculateMaintainabilityIncrease(dynamic item)
        {
            return (double)(item.maintainabilityScore ?? 0.1);
        }

        #endregion
    }

    #region Helper Classes

    /// <summary>
    /// Base class for language-specific refactoring analyzers
    /// </summary>
    public abstract class RefactoringAnalyzer
    {
        protected readonly ILogger _logger;

        protected RefactoringAnalyzer(ILogger logger)
        {
            _logger = logger;
        }

        public abstract Task<List<RefactoringSuggestion>> AnalyzeAsync(
            string code, 
            TextSpan selectedSpan, 
            CodeStructuralAnalysis structuralAnalysis);
    }

    /// <summary>
    /// C#-specific refactoring analyzer
    /// </summary>
    public class CSharpRefactoringAnalyzer : RefactoringAnalyzer
    {
        public CSharpRefactoringAnalyzer(ILoggingService loggingService) : base(loggingService) { }

        public override async Task<List<RefactoringSuggestion>> AnalyzeAsync(
            string code, 
            TextSpan selectedSpan, 
            CodeStructuralAnalysis structuralAnalysis)
        {
            var suggestions = new List<RefactoringSuggestion>();

            try
            {
                var syntaxTree = CSharpSyntaxTree.ParseText(code);
                var root = await syntaxTree.GetRootAsync();

                // Analyze for extract method opportunities
                var longMethods = root.DescendantNodes().OfType<MethodDeclarationSyntax>()
                    .Where(m => m.ToString().Split('\n').Length > 15);

                foreach (var method in longMethods)
                {
                    suggestions.Add(new RefactoringSuggestion
                    {
                        Id = Guid.NewGuid().ToString(),
                        Type = RefactoringType.ExtractMethod,
                        Title = $"Extract method from {method.Identifier.ValueText}",
                        Description = "This method is long and could benefit from extracting smaller methods",
                        Impact = ImpactLevel.Medium,
                        Confidence = 0.8,
                        TargetSpan = method.Span,
                        Benefits = new List<string> { "Improved readability", "Better testability", "Reduced complexity" }
                    });
                }

                // Analyze for LINQ conversion opportunities
                var foreachLoops = root.DescendantNodes().OfType<ForEachStatementSyntax>();
                foreach (var loop in foreachLoops)
                {
                    if (IsLinqConvertible(loop))
                    {
                        suggestions.Add(new RefactoringSuggestion
                        {
                            Id = Guid.NewGuid().ToString(),
                            Type = RefactoringType.ConvertToLinq,
                            Title = "Convert foreach to LINQ",
                            Description = "This foreach loop can be converted to a more concise LINQ expression",
                            Impact = ImpactLevel.Low,
                            Confidence = 0.7,
                            TargetSpan = loop.Span,
                            Benefits = new List<string> { "More concise code", "Functional style", "Better performance" }
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Error in C# refactoring analysis", ex);
            }

            return suggestions;
        }

        private bool IsLinqConvertible(ForEachStatementSyntax loop)
        {
            // Simple heuristic - check if it's a filtering or mapping operation
            var body = loop.Statement.ToString();
            return body.Contains("if") || body.Contains("Add") || body.Contains("=");
        }
    }

    /// <summary>
    /// JavaScript-specific refactoring analyzer
    /// </summary>
    public class JavaScriptRefactoringAnalyzer : RefactoringAnalyzer
    {
        public JavaScriptRefactoringAnalyzer(ILoggingService loggingService) : base(loggingService) { }

        public override async Task<List<RefactoringSuggestion>> AnalyzeAsync(
            string code, 
            TextSpan selectedSpan, 
            CodeStructuralAnalysis structuralAnalysis)
        {
            var suggestions = new List<RefactoringSuggestion>();

            // Analyze for var to const/let conversion
            var varMatches = System.Text.RegularExpressions.Regex.Matches(code, @"\bvar\s+\w+");
            foreach (System.Text.RegularExpressions.Match match in varMatches)
            {
                suggestions.Add(new RefactoringSuggestion
                {
                    Id = Guid.NewGuid().ToString(),
                    Type = RefactoringType.ImproveNaming,
                    Title = "Replace var with const/let",
                    Description = "Use const or let instead of var for better scoping",
                    Impact = ImpactLevel.Low,
                    Confidence = 0.9,
                    TargetSpan = new TextSpan(match.Index, match.Length),
                    Benefits = new List<string> { "Block scoping", "Prevents hoisting issues", "Modern JavaScript" }
                });
            }

            return suggestions;
        }
    }

    /// <summary>
    /// TypeScript-specific refactoring analyzer
    /// </summary>
    public class TypeScriptRefactoringAnalyzer : RefactoringAnalyzer
    {
        public TypeScriptRefactoringAnalyzer(ILoggingService loggingService) : base(loggingService) { }

        public override async Task<List<RefactoringSuggestion>> AnalyzeAsync(
            string code, 
            TextSpan selectedSpan, 
            CodeStructuralAnalysis structuralAnalysis)
        {
            var suggestions = new List<RefactoringSuggestion>();

            // Analyze for missing type annotations
            var anyMatches = System.Text.RegularExpressions.Regex.Matches(code, @":\s*any\b");
            foreach (System.Text.RegularExpressions.Match match in anyMatches)
            {
                suggestions.Add(new RefactoringSuggestion
                {
                    Id = Guid.NewGuid().ToString(),
                    Type = RefactoringType.ImproveNaming,
                    Title = "Replace 'any' with specific type",
                    Description = "Using 'any' defeats the purpose of TypeScript. Consider using a more specific type",
                    Impact = ImpactLevel.Medium,
                    Confidence = 0.8,
                    TargetSpan = new TextSpan(match.Index, match.Length),
                    Benefits = new List<string> { "Type safety", "Better IntelliSense", "Catches errors at compile time" }
                });
            }

            return suggestions;
        }
    }

    /// <summary>
    /// Python-specific refactoring analyzer
    /// </summary>
    public class PythonRefactoringAnalyzer : RefactoringAnalyzer
    {
        public PythonRefactoringAnalyzer(ILoggingService loggingService) : base(loggingService) { }

        public override async Task<List<RefactoringSuggestion>> AnalyzeAsync(
            string code, 
            TextSpan selectedSpan, 
            CodeStructuralAnalysis structuralAnalysis)
        {
            var suggestions = new List<RefactoringSuggestion>();

            // Analyze for list comprehension opportunities
            var forLoops = System.Text.RegularExpressions.Regex.Matches(code, @"for\s+\w+\s+in\s+\w+:");
            foreach (System.Text.RegularExpressions.Match match in forLoops)
            {
                suggestions.Add(new RefactoringSuggestion
                {
                    Id = Guid.NewGuid().ToString(),
                    Type = RefactoringType.SimplifyExpression,
                    Title = "Convert to list comprehension",
                    Description = "This loop can be converted to a more Pythonic list comprehension",
                    Impact = ImpactLevel.Low,
                    Confidence = 0.7,
                    TargetSpan = new TextSpan(match.Index, match.Length),
                    Benefits = new List<string> { "More Pythonic", "Better performance", "Cleaner code" }
                });
            }

            return suggestions;
        }
    }

    /// <summary>
    /// Java-specific refactoring analyzer
    /// </summary>
    public class JavaRefactoringAnalyzer : RefactoringAnalyzer
    {
        public JavaRefactoringAnalyzer(ILoggingService loggingService) : base(loggingService) { }

        public override async Task<List<RefactoringSuggestion>> AnalyzeAsync(
            string code, 
            TextSpan selectedSpan, 
            CodeStructuralAnalysis structuralAnalysis)
        {
            var suggestions = new List<RefactoringSuggestion>();

            // Analyze for stream API opportunities
            var forLoops = System.Text.RegularExpressions.Regex.Matches(code, @"for\s*\([^)]+\)\s*\{");
            foreach (System.Text.RegularExpressions.Match match in forLoops)
            {
                suggestions.Add(new RefactoringSuggestion
                {
                    Id = Guid.NewGuid().ToString(),
                    Type = RefactoringType.ConvertToLinq,
                    Title = "Convert to Stream API",
                    Description = "This loop can be converted to use Java 8+ Stream API",
                    Impact = ImpactLevel.Medium,
                    Confidence = 0.6,
                    TargetSpan = new TextSpan(match.Index, match.Length),
                    Benefits = new List<string> { "Functional style", "Better readability", "Parallel processing support" }
                });
            }

            return suggestions;
        }
    }

    #endregion
}