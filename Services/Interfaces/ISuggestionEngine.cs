using System.Threading.Tasks;
using OllamaAssistant.Models;

namespace OllamaAssistant.Services.Interfaces
{
    /// <summary>
    /// Engine for processing and filtering AI suggestions
    /// </summary>
    public interface ISuggestionEngine
    {
        /// <summary>
        /// Processes a raw AI response into structured code suggestions
        /// </summary>
        /// <param name="suggestion">The code suggestion to process</param>
        /// <param name="context">The code context that prompted the suggestion</param>
        /// <returns>A list of processed and validated code suggestions</returns>
        Task<List<CodeSuggestion>> ProcessSuggestionAsync(CodeSuggestion suggestion, CodeContext context);

        /// <summary>
        /// Determines whether a suggestion should be shown to the user
        /// </summary>
        /// <param name="suggestion">The suggestion to evaluate</param>
        /// <param name="context">The current code context</param>
        /// <returns>True if the suggestion should be displayed</returns>
        bool ShouldShowSuggestion(CodeSuggestion suggestion, CodeContext context);

        /// <summary>
        /// Processes and validates a jump suggestion
        /// </summary>
        /// <param name="context">The current code context</param>
        /// <returns>A processed jump recommendation</returns>
        Task<JumpRecommendation> ProcessJumpSuggestionAsync(CodeContext context);

        /// <summary>
        /// Ranks multiple suggestions by relevance
        /// </summary>
        /// <param name="suggestions">The suggestions to rank</param>
        /// <param name="context">The current code context</param>
        /// <returns>Suggestions ordered by relevance</returns>
        Task<CodeSuggestion[]> RankSuggestionsAsync(CodeSuggestion[] suggestions, CodeContext context);

        /// <summary>
        /// Filters out duplicate or redundant suggestions
        /// </summary>
        /// <param name="suggestions">The suggestions to filter</param>
        /// <returns>Filtered suggestions with duplicates removed</returns>
        CodeSuggestion[] FilterDuplicates(CodeSuggestion[] suggestions);

        /// <summary>
        /// Validates a suggestion for syntactic correctness
        /// </summary>
        /// <param name="suggestion">The suggestion to validate</param>
        /// <param name="context">The code context</param>
        /// <returns>True if the suggestion is syntactically valid</returns>
        Task<bool> ValidateSuggestionAsync(CodeSuggestion suggestion, CodeContext context);

        /// <summary>
        /// Adapts a suggestion to match the current code style
        /// </summary>
        /// <param name="suggestion">The suggestion to adapt</param>
        /// <param name="context">The code context with style information</param>
        /// <returns>The adapted suggestion</returns>
        Task<CodeSuggestion> AdaptToCodeStyleAsync(CodeSuggestion suggestion, CodeContext context);

        /// <summary>
        /// Gets the minimum confidence threshold for suggestions
        /// </summary>
        double MinimumConfidenceThreshold { get; }

        /// <summary>
        /// Sets the minimum confidence threshold for suggestions
        /// </summary>
        /// <param name="threshold">The threshold value (0.0 to 1.0)</param>
        void SetMinimumConfidenceThreshold(double threshold);

        /// <summary>
        /// Analyzes the code context to identify jump opportunities
        /// </summary>
        /// <param name="context">The current code context</param>
        /// <returns>A list of jump recommendations</returns>
        Task<List<JumpRecommendation>> AnalyzeJumpOpportunitiesAsync(CodeContext context);
    }
}