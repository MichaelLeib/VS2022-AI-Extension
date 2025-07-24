using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.Text;
using OllamaAssistant.Models;

namespace OllamaAssistant.Services.Interfaces
{
    /// <summary>
    /// Service interface for AI-powered code refactoring suggestions and analysis
    /// </summary>
    public interface ICodeRefactoringService
    {
        /// <summary>
        /// Analyzes code and provides AI-powered refactoring suggestions
        /// </summary>
        /// <param name="filePath">Path to the file being analyzed</param>
        /// <param name="codeContent">The code content to analyze</param>
        /// <param name="selectedSpan">Optional text span for targeted refactoring (default analyzes entire code)</param>
        /// <param name="cancellationToken">Cancellation token for async operation</param>
        /// <returns>List of refactoring suggestions</returns>
        Task<List<RefactoringSuggestion>> GetRefactoringSuggestionsAsync(
            string filePath,
            string codeContent,
            TextSpan selectedSpan = default,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Applies a specific refactoring suggestion to the code
        /// </summary>
        /// <param name="filePath">Path to the file being refactored</param>
        /// <param name="codeContent">The original code content</param>
        /// <param name="suggestion">The refactoring suggestion to apply</param>
        /// <param name="cancellationToken">Cancellation token for async operation</param>
        /// <returns>Result of the refactoring operation</returns>
        Task<RefactoringResult> ApplyRefactoringAsync(
            string filePath,
            string codeContent,
            RefactoringSuggestion suggestion,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets performance metrics for refactoring operations
        /// </summary>
        /// <returns>Metrics about refactoring operations</returns>
        RefactoringMetrics GetRefactoringMetrics();
    }
}