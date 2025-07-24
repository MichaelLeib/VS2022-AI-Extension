using System.Collections.Generic;
using System.Threading.Tasks;
using OllamaAssistant.Models;

namespace OllamaAssistant.Services.Interfaces
{
    /// <summary>
    /// Service for advanced context analysis and code insights
    /// </summary>
    public interface IAdvancedContextAnalysisService
    {
        /// <summary>
        /// Analyzes code context to provide detailed insights
        /// </summary>
        Task<AdvancedContextAnalysis> AnalyzeContextAsync(string code, string language, string filePath);

        /// <summary>
        /// Calculates complexity metrics for the given code
        /// </summary>
        Task<CodeComplexityMetrics> CalculateComplexityAsync(string code);

        /// <summary>
        /// Provides contextual suggestions based on code and cursor position
        /// </summary>
        Task<List<CodeSuggestion>> GetContextualSuggestionsAsync(string code, int cursorPosition);

        /// <summary>
        /// Validates if the given context is valid for analysis
        /// </summary>
        Task<bool> IsValidContextAsync(string code, int startPosition, int endPosition);
    }
}