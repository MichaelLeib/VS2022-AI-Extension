using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.VisualStudio.LanguageServices;
using OllamaAssistant.Models;
using OllamaAssistant.Services.Interfaces;

namespace OllamaAssistant.Services.Implementation
{
    /// <summary>
    /// Service for advanced context analysis including semantic analysis, project-wide understanding, and language-specific optimization
    /// </summary>
    public class AdvancedContextAnalysisService : IDisposable
    {
        private readonly SemanticAnalyzer _semanticAnalyzer;
        private readonly ProjectContextAnalyzer _projectAnalyzer;
        private readonly DependencyAnalyzer _dependencyAnalyzer;
        private readonly LanguageSpecificOptimizer _languageOptimizer;
        private readonly ContextCache _contextCache;
        private readonly VisualStudioWorkspace _workspace;
        private bool _disposed;

        public AdvancedContextAnalysisService(VisualStudioWorkspace workspace = null)
        {
            _workspace = workspace;
            _semanticAnalyzer = new SemanticAnalyzer();
            _projectAnalyzer = new ProjectContextAnalyzer();
            _dependencyAnalyzer = new DependencyAnalyzer();
            _languageOptimizer = new LanguageSpecificOptimizer();
            _contextCache = new ContextCache();
        }

        /// <summary>
        /// Performs comprehensive context analysis for a code position
        /// </summary>
        public async Task<AdvancedCodeContext> AnalyzeContextAsync(string filePath, int line, int column, CodeContext basicContext = null)
        {
            if (_disposed || string.IsNullOrEmpty(filePath))
                return CreateEmptyAdvancedContext();

            try
            {
                var cacheKey = $"{filePath}:{line}:{column}";
                
                // Check cache first
                if (_contextCache.TryGetCachedContext(cacheKey, out var cachedContext))
                    return cachedContext;

                var context = new AdvancedCodeContext
                {
                    FilePath = filePath,
                    Line = line,
                    Column = column,
                    BasicContext = basicContext,
                    AnalysisTimestamp = DateTime.UtcNow
                };

                // Perform semantic analysis
                context.SemanticInfo = await _semanticAnalyzer.AnalyzeSemanticContextAsync(filePath, line, column);

                // Analyze project-wide context
                context.ProjectContext = await _projectAnalyzer.AnalyzeProjectContextAsync(filePath);

                // Analyze dependencies and imports
                context.DependencyInfo = await _dependencyAnalyzer.AnalyzeDependenciesAsync(filePath, context.SemanticInfo);

                // Apply language-specific optimizations
                context.LanguageOptimizations = await _languageOptimizer.OptimizeForLanguageAsync(context);

                // Calculate relevance scores
                context.RelevanceScores = CalculateRelevanceScores(context);

                // Cache the result
                _contextCache.CacheContext(cacheKey, context);

                return context;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Context analysis failed for {filePath}:{line}:{column}: {ex.Message}");
                return CreateEmptyAdvancedContext();
            }
        }

        /// <summary>
        /// Gets project-wide symbols and their relationships
        /// </summary>
        public async Task<ProjectSymbolMap> GetProjectSymbolMapAsync(string projectPath)
        {
            if (_disposed || string.IsNullOrEmpty(projectPath))
                return new ProjectSymbolMap();

            return await _projectAnalyzer.BuildSymbolMapAsync(projectPath);
        }

        /// <summary>
        /// Analyzes code dependencies for a specific file
        /// </summary>
        public async Task<DependencyAnalysisResult> AnalyzeDependenciesAsync(string filePath)
        {
            if (_disposed || string.IsNullOrEmpty(filePath))
                return new DependencyAnalysisResult();

            return await _dependencyAnalyzer.AnalyzeFileDependenciesAsync(filePath);
        }

        /// <summary>
        /// Gets optimized context for a specific programming language
        /// </summary>
        public async Task<LanguageOptimizedContext> GetLanguageOptimizedContextAsync(AdvancedCodeContext context)
        {
            if (_disposed || context == null)
                return new LanguageOptimizedContext();

            return await _languageOptimizer.OptimizeForLanguageAsync(context);
        }

        /// <summary>
        /// Finds related code sections across the project
        /// </summary>
        public async Task<List<RelatedCodeSection>> FindRelatedCodeAsync(string filePath, int line, int column, int maxResults = 10)
        {
            if (_disposed || string.IsNullOrEmpty(filePath))
                return new List<RelatedCodeSection>();

            try
            {
                // Get semantic context first
                var semanticInfo = await _semanticAnalyzer.AnalyzeSemanticContextAsync(filePath, line, column);
                
                // Find related code based on semantic similarity
                var relatedSections = await _projectAnalyzer.FindRelatedCodeSectionsAsync(
                    semanticInfo, filePath, maxResults);

                // Sort by relevance
                return relatedSections.OrderByDescending(r => r.RelevanceScore).ToList();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Related code search failed: {ex.Message}");
                return new List<RelatedCodeSection>();
            }
        }

        /// <summary>
        /// Gets context-aware import suggestions
        /// </summary>
        public async Task<List<ImportSuggestion>> GetImportSuggestionsAsync(string filePath, string symbol)
        {
            if (_disposed || string.IsNullOrEmpty(filePath) || string.IsNullOrEmpty(symbol))
                return new List<ImportSuggestion>();

            return await _dependencyAnalyzer.GetImportSuggestionsAsync(filePath, symbol);
        }

        /// <summary>
        /// Analyzes code patterns and suggests improvements
        /// </summary>
        public async Task<CodePatternAnalysis> AnalyzeCodePatternsAsync(string filePath, int startLine, int endLine)
        {
            if (_disposed || string.IsNullOrEmpty(filePath))
                return new CodePatternAnalysis();

            return await _semanticAnalyzer.AnalyzeCodePatternsAsync(filePath, startLine, endLine);
        }

        /// <summary>
        /// Gets context analysis statistics
        /// </summary>
        public ContextAnalysisStatistics GetStatistics()
        {
            return new ContextAnalysisStatistics
            {
                CachedContexts = _contextCache.Count,
                SemanticAnalysisRequests = _semanticAnalyzer.AnalysisCount,
                ProjectAnalysisRequests = _projectAnalyzer.AnalysisCount,
                DependencyAnalysisRequests = _dependencyAnalyzer.AnalysisCount,
                LanguageOptimizationRequests = _languageOptimizer.OptimizationCount,
                CacheHitRate = _contextCache.HitRate,
                LastAnalysis = DateTime.UtcNow
            };
        }

        /// <summary>
        /// Calculates relevance scores for different context elements
        /// </summary>
        private ContextRelevanceScores CalculateRelevanceScores(AdvancedCodeContext context)
        {
            var scores = new ContextRelevanceScores();

            // Score current file context
            scores.CurrentFileRelevance = 1.0;

            // Score semantic context based on symbol usage
            if (context.SemanticInfo?.CurrentSymbol != null)
            {
                scores.SemanticRelevance = 0.9;
            }

            // Score project context based on file relationships
            if (context.ProjectContext?.RelatedFiles?.Any() == true)
            {
                scores.ProjectRelevance = 0.7;
            }

            // Score dependency context based on import usage
            if (context.DependencyInfo?.DirectDependencies?.Any() == true)
            {
                scores.DependencyRelevance = 0.8;
            }

            // Score language-specific optimizations
            if (context.LanguageOptimizations?.OptimizedElements?.Any() == true)
            {
                scores.LanguageOptimizationRelevance = 0.6;
            }

            return scores;
        }

        /// <summary>
        /// Creates empty advanced context for error cases
        /// </summary>
        private AdvancedCodeContext CreateEmptyAdvancedContext()
        {
            return new AdvancedCodeContext
            {
                AnalysisTimestamp = DateTime.UtcNow,
                SemanticInfo = new SemanticContextInfo(),
                ProjectContext = new ProjectContextInfo(),
                DependencyInfo = new DependencyContextInfo(),
                LanguageOptimizations = new LanguageOptimizedContext(),
                RelevanceScores = new ContextRelevanceScores()
            };
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            _semanticAnalyzer?.Dispose();
            _projectAnalyzer?.Dispose();
            _dependencyAnalyzer?.Dispose();
            _languageOptimizer?.Dispose();
            _contextCache?.Dispose();
        }
    }

    /// <summary>
    /// Semantic code analyzer using Roslyn
    /// </summary>
    internal class SemanticAnalyzer : IDisposable
    {
        private int _analysisCount;

        public int AnalysisCount => _analysisCount;

        public async Task<SemanticContextInfo> AnalyzeSemanticContextAsync(string filePath, int line, int column)
        {
            Interlocked.Increment(ref _analysisCount);

            var info = new SemanticContextInfo();

            try
            {
                if (!File.Exists(filePath))
                    return info;

                var content = await File.ReadAllTextAsync(filePath);
                var language = GetLanguageFromExtension(Path.GetExtension(filePath));

                if (language == "csharp")
                {
                    await AnalyzeCSharpSemanticAsync(content, line, column, info);
                }
                else
                {
                    await AnalyzeGenericSemanticAsync(content, line, column, language, info);
                }
            }
            catch (Exception ex)
            {
                info.AnalysisErrors.Add($"Semantic analysis failed: {ex.Message}");
            }

            return info;
        }

        public async Task<CodePatternAnalysis> AnalyzeCodePatternsAsync(string filePath, int startLine, int endLine)
        {
            var analysis = new CodePatternAnalysis { FilePath = filePath };

            try
            {
                if (!File.Exists(filePath))
                    return analysis;

                var content = await File.ReadAllTextAsync(filePath);
                var lines = content.Split('\n');

                if (startLine < 0 || endLine >= lines.Length || startLine > endLine)
                    return analysis;

                var codeSection = string.Join("\n", lines.Skip(startLine).Take(endLine - startLine + 1));
                var language = GetLanguageFromExtension(Path.GetExtension(filePath));

                // Analyze patterns based on language
                switch (language)
                {
                    case "csharp":
                        await AnalyzeCSharpPatternsAsync(codeSection, analysis);
                        break;
                    case "javascript":
                    case "typescript":
                        await AnalyzeJavaScriptPatternsAsync(codeSection, analysis);
                        break;
                    default:
                        await AnalyzeGenericPatternsAsync(codeSection, language, analysis);
                        break;
                }
            }
            catch (Exception ex)
            {
                analysis.AnalysisErrors.Add($"Pattern analysis failed: {ex.Message}");
            }

            return analysis;
        }

        private async Task AnalyzeCSharpSemanticAsync(string content, int line, int column, SemanticContextInfo info)
        {
            await Task.Run(() =>
            {
                try
                {
                    var tree = CSharpSyntaxTree.ParseText(content);
                    var root = tree.GetRoot();

                    // Find the node at the specified position
                    var position = GetPositionFromLineColumn(content, line, column);
                    var node = root.FindNode(new Microsoft.CodeAnalysis.Text.TextSpan(position, 0));

                    info.CurrentNode = node?.GetType().Name ?? "Unknown";
                    info.NodeKind = node?.Kind().ToString() ?? "Unknown";

                    // Analyze surrounding context
                    if (node != null)
                    {
                        AnalyzeCSharpNodeContext(node, info);
                    }

                    // Find symbols and references
                    ExtractCSharpSymbols(root, info);
                }
                catch (Exception ex)
                {
                    info.AnalysisErrors.Add($"C# semantic analysis failed: {ex.Message}");
                }
            });
        }

        private async Task AnalyzeGenericSemanticAsync(string content, int line, int column, string language, SemanticContextInfo info)
        {
            await Task.Run(() =>
            {
                try
                {
                    var lines = content.Split('\n');
                    if (line >= 0 && line < lines.Length)
                    {
                        var currentLine = lines[line];
                        info.CurrentLine = currentLine;

                        // Basic token analysis
                        AnalyzeTokens(currentLine, column, language, info);

                        // Context analysis
                        AnalyzeSurroundingLines(lines, line, info);
                    }
                }
                catch (Exception ex)
                {
                    info.AnalysisErrors.Add($"Generic semantic analysis failed: {ex.Message}");
                }
            });
        }

        private async Task AnalyzeCSharpPatternsAsync(string code, CodePatternAnalysis analysis)
        {
            await Task.Run(() =>
            {
                try
                {
                    var tree = CSharpSyntaxTree.ParseText(code);
                    var root = tree.GetRoot();

                    // Detect common patterns
                    DetectCSharpPatterns(root, analysis);
                }
                catch (Exception ex)
                {
                    analysis.AnalysisErrors.Add($"C# pattern analysis failed: {ex.Message}");
                }
            });
        }

        private async Task AnalyzeJavaScriptPatternsAsync(string code, CodePatternAnalysis analysis)
        {
            await Task.Run(() =>
            {
                try
                {
                    // Basic JavaScript pattern detection using regex
                    DetectJavaScriptPatterns(code, analysis);
                }
                catch (Exception ex)
                {
                    analysis.AnalysisErrors.Add($"JavaScript pattern analysis failed: {ex.Message}");
                }
            });
        }

        private async Task AnalyzeGenericPatternsAsync(string code, string language, CodePatternAnalysis analysis)
        {
            await Task.Run(() =>
            {
                try
                {
                    // Generic pattern detection
                    DetectGenericPatterns(code, language, analysis);
                }
                catch (Exception ex)
                {
                    analysis.AnalysisErrors.Add($"Generic pattern analysis failed: {ex.Message}");
                }
            });
        }

        private void AnalyzeCSharpNodeContext(SyntaxNode node, SemanticContextInfo info)
        {
            // Find parent method
            var method = node.FirstAncestorOrSelf<MethodDeclarationSyntax>();
            if (method != null)
            {
                info.ContainingMethod = method.Identifier.ValueText;
            }

            // Find parent class
            var classDeclaration = node.FirstAncestorOrSelf<ClassDeclarationSyntax>();
            if (classDeclaration != null)
            {
                info.ContainingClass = classDeclaration.Identifier.ValueText;
            }

            // Find parent namespace
            var namespaceDeclaration = node.FirstAncestorOrSelf<NamespaceDeclarationSyntax>();
            if (namespaceDeclaration != null)
            {
                info.ContainingNamespace = namespaceDeclaration.Name.ToString();
            }
        }

        private void ExtractCSharpSymbols(SyntaxNode root, SemanticContextInfo info)
        {
            // Extract class names
            var classes = root.DescendantNodes().OfType<ClassDeclarationSyntax>();
            foreach (var cls in classes)
            {
                info.AvailableSymbols.Add(new SymbolInfo
                {
                    Name = cls.Identifier.ValueText,
                    Type = "class",
                    Location = cls.GetLocation().GetLineSpan().StartLinePosition.Line
                });
            }

            // Extract method names
            var methods = root.DescendantNodes().OfType<MethodDeclarationSyntax>();
            foreach (var method in methods)
            {
                info.AvailableSymbols.Add(new SymbolInfo
                {
                    Name = method.Identifier.ValueText,
                    Type = "method",
                    Location = method.GetLocation().GetLineSpan().StartLinePosition.Line
                });
            }

            // Extract variable declarations
            var variables = root.DescendantNodes().OfType<VariableDeclaratorSyntax>();
            foreach (var variable in variables)
            {
                info.AvailableSymbols.Add(new SymbolInfo
                {
                    Name = variable.Identifier.ValueText,
                    Type = "variable",
                    Location = variable.GetLocation().GetLineSpan().StartLinePosition.Line
                });
            }
        }

        private void AnalyzeTokens(string line, int column, string language, SemanticContextInfo info)
        {
            // Basic tokenization based on language
            var tokens = line.Split(new[] { ' ', '\t', '(', ')', '{', '}', ';', ',' }, StringSplitOptions.RemoveEmptyEntries);
            
            if (column >= 0 && column < line.Length)
            {
                // Find current token at cursor position
                var beforeCursor = line.Substring(0, Math.Min(column, line.Length));
                var tokenMatch = Regex.Match(beforeCursor, @"\b(\w+)$");
                if (tokenMatch.Success)
                {
                    info.CurrentSymbol = tokenMatch.Groups[1].Value;
                }
            }

            info.LineTokens.AddRange(tokens);
        }

        private void AnalyzeSurroundingLines(string[] lines, int currentLine, SemanticContextInfo info)
        {
            // Analyze lines above and below for context
            var contextLines = 5;
            var startLine = Math.Max(0, currentLine - contextLines);
            var endLine = Math.Min(lines.Length - 1, currentLine + contextLines);

            for (int i = startLine; i <= endLine; i++)
            {
                if (i != currentLine && !string.IsNullOrWhiteSpace(lines[i]))
                {
                    info.SurroundingContext.Add(new ContextLine
                    {
                        LineNumber = i,
                        Content = lines[i].Trim(),
                        Distance = Math.Abs(i - currentLine)
                    });
                }
            }
        }

        private void DetectCSharpPatterns(SyntaxNode root, CodePatternAnalysis analysis)
        {
            // Detect LINQ patterns
            var linqQueries = root.DescendantNodes().OfType<QueryExpressionSyntax>();
            foreach (var query in linqQueries)
            {
                analysis.DetectedPatterns.Add(new CodePattern
                {
                    Type = "LINQ Query",
                    Description = "LINQ query expression detected",
                    Confidence = 0.9,
                    Location = query.GetLocation().GetLineSpan().StartLinePosition.Line
                });
            }

            // Detect async/await patterns
            var asyncMethods = root.DescendantNodes().OfType<MethodDeclarationSyntax>()
                .Where(m => m.Modifiers.Any(mod => mod.IsKind(SyntaxKind.AsyncKeyword)));
            
            foreach (var method in asyncMethods)
            {
                analysis.DetectedPatterns.Add(new CodePattern
                {
                    Type = "Async Method",
                    Description = $"Async method: {method.Identifier.ValueText}",
                    Confidence = 1.0,
                    Location = method.GetLocation().GetLineSpan().StartLinePosition.Line
                });
            }

            // Detect dependency injection patterns
            var constructors = root.DescendantNodes().OfType<ConstructorDeclarationSyntax>();
            foreach (var constructor in constructors)
            {
                if (constructor.ParameterList.Parameters.Count > 2)
                {
                    analysis.DetectedPatterns.Add(new CodePattern
                    {
                        Type = "Dependency Injection",
                        Description = "Constructor with multiple parameters (possible DI)",
                        Confidence = 0.7,
                        Location = constructor.GetLocation().GetLineSpan().StartLinePosition.Line
                    });
                }
            }
        }

        private void DetectJavaScriptPatterns(string code, CodePatternAnalysis analysis)
        {
            // Detect arrow functions
            if (Regex.IsMatch(code, @"=>\s*{"))
            {
                analysis.DetectedPatterns.Add(new CodePattern
                {
                    Type = "Arrow Function",
                    Description = "Arrow function pattern detected",
                    Confidence = 0.8
                });
            }

            // Detect async/await
            if (Regex.IsMatch(code, @"\basync\s+function|\basync\s*\("))
            {
                analysis.DetectedPatterns.Add(new CodePattern
                {
                    Type = "Async Function",
                    Description = "Async function pattern detected",
                    Confidence = 0.9
                });
            }

            // Detect Promise patterns
            if (Regex.IsMatch(code, @"\.then\s*\(|\.catch\s*\(|new\s+Promise"))
            {
                analysis.DetectedPatterns.Add(new CodePattern
                {
                    Type = "Promise",
                    Description = "Promise pattern detected",
                    Confidence = 0.85
                });
            }
        }

        private void DetectGenericPatterns(string code, string language, CodePatternAnalysis analysis)
        {
            // Detect function definitions
            var functionPatterns = new Dictionary<string, string>
            {
                ["python"] = @"\bdef\s+\w+\s*\(",
                ["java"] = @"\b(public|private|protected)?\s*(static\s+)?\w+\s+\w+\s*\(",
                ["cpp"] = @"\b\w+\s+\w+\s*\([^)]*\)\s*{",
                ["go"] = @"\bfunc\s+\w+\s*\("
            };

            if (functionPatterns.TryGetValue(language, out var pattern))
            {
                if (Regex.IsMatch(code, pattern))
                {
                    analysis.DetectedPatterns.Add(new CodePattern
                    {
                        Type = "Function Definition",
                        Description = $"Function definition in {language}",
                        Confidence = 0.8
                    });
                }
            }

            // Detect loops
            if (Regex.IsMatch(code, @"\b(for|while|do)\s*\("))
            {
                analysis.DetectedPatterns.Add(new CodePattern
                {
                    Type = "Loop",
                    Description = "Loop construct detected",
                    Confidence = 0.9
                });
            }

            // Detect conditionals
            if (Regex.IsMatch(code, @"\bif\s*\("))
            {
                analysis.DetectedPatterns.Add(new CodePattern
                {
                    Type = "Conditional",
                    Description = "Conditional statement detected",
                    Confidence = 0.9
                });
            }
        }

        private string GetLanguageFromExtension(string extension)
        {
            return extension?.ToLower() switch
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
                ".php" => "php",
                ".rb" => "ruby",
                _ => "text"
            };
        }

        private int GetPositionFromLineColumn(string content, int line, int column)
        {
            var lines = content.Split('\n');
            var position = 0;

            for (int i = 0; i < Math.Min(line, lines.Length); i++)
            {
                if (i == line)
                {
                    position += Math.Min(column, lines[i].Length);
                    break;
                }
                position += lines[i].Length + 1; // +1 for newline
            }

            return Math.Max(0, Math.Min(position, content.Length));
        }

        public void Dispose()
        {
            // Cleanup resources
        }
    }

    /// <summary>
    /// Advanced code context with semantic and project information
    /// </summary>
    public class AdvancedCodeContext
    {
        public string FilePath { get; set; }
        public int Line { get; set; }
        public int Column { get; set; }
        public CodeContext BasicContext { get; set; }
        public DateTime AnalysisTimestamp { get; set; }
        public SemanticContextInfo SemanticInfo { get; set; }
        public ProjectContextInfo ProjectContext { get; set; }
        public DependencyContextInfo DependencyInfo { get; set; }
        public LanguageOptimizedContext LanguageOptimizations { get; set; }
        public ContextRelevanceScores RelevanceScores { get; set; }
    }

    /// <summary>
    /// Semantic context information
    /// </summary>
    public class SemanticContextInfo
    {
        public string CurrentNode { get; set; }
        public string NodeKind { get; set; }
        public string CurrentLine { get; set; }
        public string CurrentSymbol { get; set; }
        public string ContainingMethod { get; set; }
        public string ContainingClass { get; set; }
        public string ContainingNamespace { get; set; }
        public List<string> LineTokens { get; set; } = new();
        public List<SymbolInfo> AvailableSymbols { get; set; } = new();
        public List<ContextLine> SurroundingContext { get; set; } = new();
        public List<string> AnalysisErrors { get; set; } = new();
    }

    /// <summary>
    /// Symbol information
    /// </summary>
    public class SymbolInfo
    {
        public string Name { get; set; }
        public string Type { get; set; }
        public int Location { get; set; }
        public string Description { get; set; }
    }

    /// <summary>
    /// Context line information
    /// </summary>
    public class ContextLine
    {
        public int LineNumber { get; set; }
        public string Content { get; set; }
        public int Distance { get; set; }
    }

    /// <summary>
    /// Context relevance scores
    /// </summary>
    public class ContextRelevanceScores
    {
        public double CurrentFileRelevance { get; set; }
        public double SemanticRelevance { get; set; }
        public double ProjectRelevance { get; set; }
        public double DependencyRelevance { get; set; }
        public double LanguageOptimizationRelevance { get; set; }
    }

    /// <summary>
    /// Code pattern analysis result
    /// </summary>
    public class CodePatternAnalysis
    {
        public string FilePath { get; set; }
        public List<CodePattern> DetectedPatterns { get; set; } = new();
        public List<string> AnalysisErrors { get; set; } = new();
    }

    /// <summary>
    /// Detected code pattern
    /// </summary>
    public class CodePattern
    {
        public string Type { get; set; }
        public string Description { get; set; }
        public double Confidence { get; set; }
        public int Location { get; set; }
    }

    /// <summary>
    /// Context analysis statistics
    /// </summary>
    public class ContextAnalysisStatistics
    {
        public int CachedContexts { get; set; }
        public int SemanticAnalysisRequests { get; set; }
        public int ProjectAnalysisRequests { get; set; }
        public int DependencyAnalysisRequests { get; set; }
        public int LanguageOptimizationRequests { get; set; }
        public double CacheHitRate { get; set; }
        public DateTime LastAnalysis { get; set; }
    }

    // Supporting classes (simplified implementations)
    internal class ProjectContextAnalyzer : IDisposable
    {
        private int _analysisCount;
        public int AnalysisCount => _analysisCount;

        public async Task<ProjectContextInfo> AnalyzeProjectContextAsync(string filePath)
        {
            Interlocked.Increment(ref _analysisCount);
            await Task.Delay(10); // Simulate analysis
            
            return new ProjectContextInfo
            {
                ProjectRoot = FindProjectRoot(filePath),
                RelatedFiles = FindRelatedFiles(filePath),
                ProjectType = DetectProjectType(filePath)
            };
        }

        public async Task<ProjectSymbolMap> BuildSymbolMapAsync(string projectPath)
        {
            await Task.Delay(50); // Simulate building symbol map
            return new ProjectSymbolMap { ProjectPath = projectPath };
        }

        public async Task<List<RelatedCodeSection>> FindRelatedCodeSectionsAsync(SemanticContextInfo semanticInfo, string filePath, int maxResults)
        {
            await Task.Delay(20); // Simulate search
            return new List<RelatedCodeSection>();
        }

        private string FindProjectRoot(string filePath)
        {
            var directory = Path.GetDirectoryName(filePath);
            while (directory != null)
            {
                if (Directory.GetFiles(directory, "*.csproj").Any() ||
                    Directory.GetFiles(directory, "*.sln").Any() ||
                    Directory.GetFiles(directory, "package.json").Any())
                {
                    return directory;
                }
                directory = Path.GetDirectoryName(directory);
            }
            return Path.GetDirectoryName(filePath);
        }

        private List<string> FindRelatedFiles(string filePath)
        {
            var directory = Path.GetDirectoryName(filePath);
            var fileName = Path.GetFileNameWithoutExtension(filePath);
            var extension = Path.GetExtension(filePath);

            var relatedFiles = new List<string>();

            if (Directory.Exists(directory))
            {
                // Find files with similar names
                var similarFiles = Directory.GetFiles(directory, $"*{fileName}*{extension}")
                    .Where(f => f != filePath)
                    .Take(5);
                
                relatedFiles.AddRange(similarFiles);
            }

            return relatedFiles;
        }

        private string DetectProjectType(string filePath)
        {
            var projectRoot = FindProjectRoot(filePath);
            
            if (Directory.GetFiles(projectRoot, "*.csproj").Any())
                return "C# Project";
            if (File.Exists(Path.Combine(projectRoot, "package.json")))
                return "Node.js Project";
            if (File.Exists(Path.Combine(projectRoot, "pom.xml")))
                return "Java Project";
            
            return "Unknown Project Type";
        }

        public void Dispose() { }
    }

    internal class DependencyAnalyzer : IDisposable
    {
        private int _analysisCount;
        public int AnalysisCount => _analysisCount;

        public async Task<DependencyContextInfo> AnalyzeDependenciesAsync(string filePath, SemanticContextInfo semanticInfo)
        {
            Interlocked.Increment(ref _analysisCount);
            await Task.Delay(10); // Simulate analysis
            
            return new DependencyContextInfo
            {
                DirectDependencies = ExtractDirectDependencies(filePath),
                IndirectDependencies = new List<string>(),
                UnresolvedReferences = new List<string>()
            };
        }

        public async Task<DependencyAnalysisResult> AnalyzeFileDependenciesAsync(string filePath)
        {
            await Task.Delay(20); // Simulate analysis
            return new DependencyAnalysisResult { FilePath = filePath };
        }

        public async Task<List<ImportSuggestion>> GetImportSuggestionsAsync(string filePath, string symbol)
        {
            await Task.Delay(10); // Simulate search
            return new List<ImportSuggestion>();
        }

        private List<string> ExtractDirectDependencies(string filePath)
        {
            var dependencies = new List<string>();
            
            try
            {
                var content = File.ReadAllText(filePath);
                var extension = Path.GetExtension(filePath).ToLower();

                switch (extension)
                {
                    case ".cs":
                        ExtractCSharpUsings(content, dependencies);
                        break;
                    case ".js":
                    case ".ts":
                        ExtractJavaScriptImports(content, dependencies);
                        break;
                    case ".py":
                        ExtractPythonImports(content, dependencies);
                        break;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Dependency extraction failed: {ex.Message}");
            }

            return dependencies;
        }

        private void ExtractCSharpUsings(string content, List<string> dependencies)
        {
            var usingPattern = @"using\s+([^;]+);";
            var usingMatches = Regex.Matches(content, usingPattern);
            
            foreach (Match match in usingMatches)
            {
                dependencies.Add(match.Groups[1].Value.Trim());
            }
        }

        private void ExtractJavaScriptImports(string content, List<string> dependencies)
        {
            var importPattern = @"import\s+.*?from\s+['""]([^'""]+)['""]";
            var requirePattern = @"require\s*\(\s*['""]([^'""]+)['""]\s*\)";
            
            var importMatches = Regex.Matches(content, importPattern);
            var requireMatches = Regex.Matches(content, requirePattern);
            
            foreach (Match match in importMatches)
            {
                dependencies.Add(match.Groups[1].Value);
            }
            
            foreach (Match match in requireMatches)
            {
                dependencies.Add(match.Groups[1].Value);
            }
        }

        private void ExtractPythonImports(string content, List<string> dependencies)
        {
            var importPattern = @"import\s+([^\s]+)";
            var fromImportPattern = @"from\s+([^\s]+)\s+import";
            
            var importMatches = Regex.Matches(content, importPattern);
            var fromImportMatches = Regex.Matches(content, fromImportPattern);
            
            foreach (Match match in importMatches)
            {
                dependencies.Add(match.Groups[1].Value);
            }
            
            foreach (Match match in fromImportMatches)
            {
                dependencies.Add(match.Groups[1].Value);
            }
        }

        public void Dispose() { }
    }

    internal class LanguageSpecificOptimizer : IDisposable
    {
        private int _optimizationCount;
        public int OptimizationCount => _optimizationCount;

        public async Task<LanguageOptimizedContext> OptimizeForLanguageAsync(AdvancedCodeContext context)
        {
            Interlocked.Increment(ref _optimizationCount);
            await Task.Delay(5); // Simulate optimization
            
            return new LanguageOptimizedContext
            {
                Language = DetectLanguage(context.FilePath),
                OptimizedElements = new List<string> { "Context optimized for language" }
            };
        }

        private string DetectLanguage(string filePath)
        {
            var extension = Path.GetExtension(filePath)?.ToLower();
            return extension switch
            {
                ".cs" => "C#",
                ".js" => "JavaScript",
                ".ts" => "TypeScript",
                ".py" => "Python",
                ".java" => "Java",
                ".cpp" or ".cc" => "C++",
                ".c" => "C",
                ".go" => "Go",
                _ => "Unknown"
            };
        }

        public void Dispose() { }
    }

    internal class ContextCache : IDisposable
    {
        private readonly Dictionary<string, AdvancedCodeContext> _cache = new();
        private readonly Dictionary<string, DateTime> _timestamps = new();
        private int _hits;
        private int _requests;

        public int Count => _cache.Count;
        public double HitRate => _requests > 0 ? (double)_hits / _requests : 0;

        public bool TryGetCachedContext(string key, out AdvancedCodeContext context)
        {
            _requests++;
            
            if (_cache.TryGetValue(key, out context) && 
                _timestamps.TryGetValue(key, out var timestamp) &&
                DateTime.UtcNow - timestamp < TimeSpan.FromMinutes(5))
            {
                _hits++;
                return true;
            }

            context = null;
            return false;
        }

        public void CacheContext(string key, AdvancedCodeContext context)
        {
            _cache[key] = context;
            _timestamps[key] = DateTime.UtcNow;
        }

        public void Dispose()
        {
            _cache.Clear();
            _timestamps.Clear();
        }
    }

    // Additional supporting classes
    public class ProjectContextInfo
    {
        public string ProjectRoot { get; set; }
        public List<string> RelatedFiles { get; set; } = new();
        public string ProjectType { get; set; }
    }

    public class DependencyContextInfo
    {
        public List<string> DirectDependencies { get; set; } = new();
        public List<string> IndirectDependencies { get; set; } = new();
        public List<string> UnresolvedReferences { get; set; } = new();
    }

    public class LanguageOptimizedContext
    {
        public string Language { get; set; }
        public List<string> OptimizedElements { get; set; } = new();
    }

    public class ProjectSymbolMap
    {
        public string ProjectPath { get; set; }
        public Dictionary<string, List<SymbolInfo>> Symbols { get; set; } = new();
    }

    public class DependencyAnalysisResult
    {
        public string FilePath { get; set; }
        public List<string> Dependencies { get; set; } = new();
    }

    public class RelatedCodeSection
    {
        public string FilePath { get; set; }
        public int StartLine { get; set; }
        public int EndLine { get; set; }
        public double RelevanceScore { get; set; }
        public string Description { get; set; }
    }

    public class ImportSuggestion
    {
        public string ImportStatement { get; set; }
        public string Description { get; set; }
        public double Confidence { get; set; }
    }
}