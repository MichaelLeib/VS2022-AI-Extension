using System;
using System.Collections.Generic;
using Microsoft.CodeAnalysis.Text;

namespace OllamaAssistant.Models
{
    /// <summary>
    /// Represents a code refactoring suggestion from AI analysis
    /// </summary>
    public class RefactoringSuggestion
    {
        /// <summary>
        /// Unique identifier for the suggestion
        /// </summary>
        public string Id { get; set; } = Guid.NewGuid().ToString();

        /// <summary>
        /// Type of refactoring being suggested
        /// </summary>
        public RefactoringType Type { get; set; }

        /// <summary>
        /// Brief title describing the refactoring
        /// </summary>
        public string Title { get; set; } = string.Empty;

        /// <summary>
        /// Detailed description of what the refactoring does
        /// </summary>
        public string Description { get; set; } = string.Empty;

        /// <summary>
        /// Impact level of applying this refactoring
        /// </summary>
        public ImpactLevel Impact { get; set; }

        /// <summary>
        /// AI confidence score for this suggestion (0.0 to 1.0)
        /// </summary>
        public double Confidence { get; set; }

        /// <summary>
        /// Text span indicating where to apply the refactoring
        /// </summary>
        public TextSpan TargetSpan { get; set; }

        /// <summary>
        /// Original code that will be replaced
        /// </summary>
        public string OriginalCode { get; set; } = string.Empty;

        /// <summary>
        /// Refactored code that will replace the original
        /// </summary>
        public string RefactoredCode { get; set; } = string.Empty;

        /// <summary>
        /// List of benefits this refactoring provides
        /// </summary>
        public List<string> Benefits { get; set; } = new List<string>();

        /// <summary>
        /// Estimated benefits and savings from applying this refactoring
        /// </summary>
        public RefactoringBenefits EstimatedSavings { get; set; } = new RefactoringBenefits();

        /// <summary>
        /// Timestamp when this suggestion was generated
        /// </summary>
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Priority score for ranking suggestions (higher is better)
        /// </summary>
        public double Priority { get; set; }

        /// <summary>
        /// Whether this suggestion requires user confirmation before applying
        /// </summary>
        public bool RequiresConfirmation { get; set; } = true;

        /// <summary>
        /// Any prerequisites or warnings for applying this refactoring
        /// </summary>
        public List<string> Prerequisites { get; set; } = new List<string>();

        /// <summary>
        /// Estimated time to apply this refactoring (in seconds)
        /// </summary>
        public int EstimatedApplyTimeSeconds { get; set; } = 1;
    }

    /// <summary>
    /// Types of code refactoring that can be suggested
    /// </summary>
    public enum RefactoringType
    {
        /// <summary>
        /// General refactoring not fitting other categories
        /// </summary>
        General,

        /// <summary>
        /// Extract method from large code blocks
        /// </summary>
        ExtractMethod,

        /// <summary>
        /// Rename variables for better clarity
        /// </summary>
        RenameVariable,

        /// <summary>
        /// Simplify complex expressions
        /// </summary>
        SimplifyExpression,

        /// <summary>
        /// Optimize import/using statements
        /// </summary>
        OptimizeImports,

        /// <summary>
        /// Convert loops to LINQ expressions
        /// </summary>
        ConvertToLinq,

        /// <summary>
        /// Add null checking where needed
        /// </summary>
        AddNullChecks,

        /// <summary>
        /// Improve naming conventions
        /// </summary>
        ImproveNaming,

        /// <summary>
        /// Reduce cyclomatic complexity
        /// </summary>
        ReduceComplexity,

        /// <summary>
        /// Extract constants from magic numbers
        /// </summary>
        ExtractConstants,

        /// <summary>
        /// Inline temporary variables
        /// </summary>
        InlineVariable,

        /// <summary>
        /// Move code to more appropriate class
        /// </summary>
        MoveCode,

        /// <summary>
        /// Replace conditional with polymorphism
        /// </summary>
        ReplaceConditional,

        /// <summary>
        /// Introduce design patterns
        /// </summary>
        IntroducePattern,

        /// <summary>
        /// Remove dead code
        /// </summary>
        RemoveDeadCode,

        /// <summary>
        /// Optimize performance
        /// </summary>
        OptimizePerformance
    }

    /// <summary>
    /// Impact level of applying a refactoring
    /// </summary>
    public enum ImpactLevel
    {
        /// <summary>
        /// Low impact - safe to apply automatically
        /// </summary>
        Low,

        /// <summary>
        /// Medium impact - should ask user for confirmation
        /// </summary>
        Medium,

        /// <summary>
        /// High impact - requires careful review
        /// </summary>
        High
    }

    /// <summary>
    /// Quantified benefits of applying a refactoring
    /// </summary>
    public class RefactoringBenefits
    {
        /// <summary>
        /// Estimated improvement in code readability (0.0 to 1.0)
        /// </summary>
        public double ReadabilityImprovement { get; set; }

        /// <summary>
        /// Estimated performance gain (0.0 to 1.0)
        /// </summary>
        public double PerformanceGain { get; set; }

        /// <summary>
        /// Estimated increase in maintainability (0.0 to 1.0)
        /// </summary>
        public double MaintainabilityIncrease { get; set; }

        /// <summary>
        /// Estimated reduction in bug likelihood (0.0 to 1.0)
        /// </summary>
        public double BugReductionFactor { get; set; }

        /// <summary>
        /// Estimated time saved in future development (hours)
        /// </summary>
        public double TimeSavingsHours { get; set; }

        /// <summary>
        /// Lines of code reduction (negative if code increases)
        /// </summary>
        public int CodeReduction { get; set; }

        /// <summary>
        /// Cyclomatic complexity reduction
        /// </summary>
        public int ComplexityReduction { get; set; }
    }

    /// <summary>
    /// Result of applying a refactoring operation
    /// </summary>
    public class RefactoringResult
    {
        /// <summary>
        /// Whether the refactoring was successful
        /// </summary>
        public bool IsSuccess { get; set; }

        /// <summary>
        /// Error message if refactoring failed
        /// </summary>
        public string ErrorMessage { get; set; } = string.Empty;

        /// <summary>
        /// The refactored code content
        /// </summary>
        public string RefactoredCode { get; set; } = string.Empty;

        /// <summary>
        /// List of changes made to the code
        /// </summary>
        public List<TextChange> Changes { get; set; } = new List<TextChange>();

        /// <summary>
        /// Warnings about the applied refactoring
        /// </summary>
        public List<string> Warnings { get; set; } = new List<string>();

        /// <summary>
        /// Time taken to apply the refactoring
        /// </summary>
        public TimeSpan Duration { get; set; }

        /// <summary>
        /// Additional metadata about the refactoring
        /// </summary>
        public Dictionary<string, object> Metadata { get; set; } = new Dictionary<string, object>();

        /// <summary>
        /// Creates a successful refactoring result
        /// </summary>
        public static RefactoringResult Success(string refactoredCode, List<TextChange> changes = null)
        {
            return new RefactoringResult
            {
                IsSuccess = true,
                RefactoredCode = refactoredCode,
                Changes = changes ?? new List<TextChange>()
            };
        }

        /// <summary>
        /// Creates a failed refactoring result
        /// </summary>
        public static RefactoringResult Failed(string errorMessage)
        {
            return new RefactoringResult
            {
                IsSuccess = false,
                ErrorMessage = errorMessage
            };
        }
    }

    /// <summary>
    /// Represents a single text change in the refactored code
    /// </summary>
    public class TextChange
    {
        /// <summary>
        /// The span of text being changed
        /// </summary>
        public TextSpan Span { get; set; }

        /// <summary>
        /// The new text to replace the span
        /// </summary>
        public string NewText { get; set; } = string.Empty;

        /// <summary>
        /// Description of what this change does
        /// </summary>
        public string Description { get; set; } = string.Empty;

        /// <summary>
        /// Type of change being made
        /// </summary>
        public ChangeType Type { get; set; }
    }

    /// <summary>
    /// Types of text changes
    /// </summary>
    public enum ChangeType
    {
        /// <summary>
        /// Adding new text
        /// </summary>
        Insert,

        /// <summary>
        /// Removing existing text
        /// </summary>
        Delete,

        /// <summary>
        /// Replacing existing text
        /// </summary>
        Replace,

        /// <summary>
        /// Moving text to a different location
        /// </summary>
        Move
    }

    /// <summary>
    /// Structural analysis of code for refactoring purposes
    /// </summary>
    public class CodeStructuralAnalysis
    {
        /// <summary>
        /// Programming language of the analyzed code
        /// </summary>
        public string Language { get; set; } = string.Empty;

        /// <summary>
        /// Total lines of code
        /// </summary>
        public int LinesOfCode { get; set; }

        /// <summary>
        /// Cyclomatic complexity score
        /// </summary>
        public int CyclomaticComplexity { get; set; }

        /// <summary>
        /// Number of methods in the code
        /// </summary>
        public int MethodCount { get; set; }

        /// <summary>
        /// Number of classes in the code
        /// </summary>
        public int ClassCount { get; set; }

        /// <summary>
        /// Number of methods that are considered too long
        /// </summary>
        public int LongMethods { get; set; }

        /// <summary>
        /// Detected code duplication issues
        /// </summary>
        public List<string> CodeDuplication { get; set; } = new List<string>();

        /// <summary>
        /// Detected naming convention issues
        /// </summary>
        public List<string> NamingIssues { get; set; } = new List<string>();

        /// <summary>
        /// Detected performance issues
        /// </summary>
        public List<string> PerformanceIssues { get; set; } = new List<string>();

        /// <summary>
        /// Detected security issues
        /// </summary>
        public List<string> SecurityIssues { get; set; } = new List<string>();

        /// <summary>
        /// Code quality score (0.0 to 10.0)
        /// </summary>
        public double QualityScore { get; set; }

        /// <summary>
        /// Maintainability index (0.0 to 100.0)
        /// </summary>
        public double MaintainabilityIndex { get; set; }

        /// <summary>
        /// Technical debt indicators
        /// </summary>
        public List<string> TechnicalDebt { get; set; } = new List<string>();
    }

    /// <summary>
    /// Performance metrics for refactoring operations
    /// </summary>
    public class RefactoringMetrics
    {
        /// <summary>
        /// Total number of refactoring suggestions generated
        /// </summary>
        public int TotalSuggestionsGenerated { get; set; }

        /// <summary>
        /// Number of suggestions that were successfully applied
        /// </summary>
        public int SuggestionsApplied { get; set; }

        /// <summary>
        /// Average time to analyze code and generate suggestions (milliseconds)
        /// </summary>
        public double AverageAnalysisTimeMs { get; set; }

        /// <summary>
        /// Success rate of refactoring operations (0.0 to 1.0)
        /// </summary>
        public double SuccessRate { get; set; }

        /// <summary>
        /// Most common refactoring types suggested
        /// </summary>
        public Dictionary<RefactoringType, int> TopRefactoringTypes { get; set; } = new Dictionary<RefactoringType, int>();

        /// <summary>
        /// Average confidence score of suggestions
        /// </summary>
        public double AverageConfidence { get; set; }

        /// <summary>
        /// Languages most frequently analyzed
        /// </summary>
        public Dictionary<string, int> LanguageDistribution { get; set; } = new Dictionary<string, int>();

        /// <summary>
        /// User acceptance rate of suggestions
        /// </summary>
        public double UserAcceptanceRate { get; set; }

        /// <summary>
        /// Time saved through refactoring (estimated hours)
        /// </summary>
        public double TotalTimeSavedHours { get; set; }

        /// <summary>
        /// Code quality improvement metrics
        /// </summary>
        public RefactoringQualityMetrics QualityImprovements { get; set; } = new RefactoringQualityMetrics();
    }

    /// <summary>
    /// Quality improvement metrics from refactoring
    /// </summary>
    public class RefactoringQualityMetrics
    {
        /// <summary>
        /// Average complexity reduction per refactoring
        /// </summary>
        public double AverageComplexityReduction { get; set; }

        /// <summary>
        /// Total lines of code reduced
        /// </summary>
        public int TotalLinesReduced { get; set; }

        /// <summary>
        /// Number of code smells eliminated
        /// </summary>
        public int CodeSmellsEliminated { get; set; }

        /// <summary>
        /// Performance improvements achieved
        /// </summary>
        public double PerformanceImprovementFactor { get; set; }

        /// <summary>
        /// Maintainability index improvements
        /// </summary>
        public double MaintainabilityImprovement { get; set; }
    }

}