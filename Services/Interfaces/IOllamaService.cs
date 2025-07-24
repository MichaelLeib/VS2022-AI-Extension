using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using OllamaAssistant.Models;

namespace OllamaAssistant.Services.Interfaces
{
    /// <summary>
    /// Service for communicating with the Ollama AI model
    /// </summary>
    public interface IOllamaService
    {
        /// <summary>
        /// Gets a code completion from the Ollama model
        /// </summary>
        /// <param name="prompt">The prompt describing what completion is needed</param>
        /// <param name="context">The current code context</param>
        /// <param name="history">Recent cursor position history for additional context</param>
        /// <param name="cancellationToken">Cancellation token for the operation</param>
        /// <returns>The AI-generated completion text</returns>
        Task<string> GetCompletionAsync(string prompt, string context, List<CursorHistoryEntry> history, CancellationToken cancellationToken);

        /// <summary>
        /// Gets a structured code suggestion from the Ollama model
        /// </summary>
        /// <param name="codeContext">The full code context object</param>
        /// <param name="cancellationToken">Cancellation token for the operation</param>
        /// <returns>A structured code suggestion</returns>
        Task<CodeSuggestion> GetCodeSuggestionAsync(CodeContext codeContext, CancellationToken cancellationToken);

        /// <summary>
        /// Gets a jump recommendation from the Ollama model
        /// </summary>
        /// <param name="codeContext">The current code context</param>
        /// <param name="cancellationToken">Cancellation token for the operation</param>
        /// <returns>A jump recommendation if one is suggested</returns>
        Task<JumpRecommendation> GetJumpRecommendationAsync(CodeContext codeContext, CancellationToken cancellationToken);

        /// <summary>
        /// Gets a streaming completion from the Ollama model
        /// </summary>
        /// <param name="prompt">The prompt describing what completion is needed</param>
        /// <param name="context">The current code context</param>
        /// <param name="history">Recent cursor position history for additional context</param>
        /// <param name="cancellationToken">Cancellation token for the operation</param>
        /// <returns>An async enumerable of completion text chunks</returns>
        IAsyncEnumerable<string> GetStreamingCompletionAsync(string prompt, string context, List<CursorHistoryEntry> history, CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets a streaming code suggestion from the Ollama model
        /// </summary>
        /// <param name="codeContext">The full code context object</param>
        /// <param name="cancellationToken">Cancellation token for the operation</param>
        /// <returns>An async enumerable of partial code suggestions</returns>
        IAsyncEnumerable<CodeSuggestion> GetStreamingCodeSuggestionAsync(CodeContext codeContext, CancellationToken cancellationToken = default);

        /// <summary>
        /// Checks if the Ollama service is available
        /// </summary>
        /// <returns>True if the service is reachable and responding</returns>
        Task<bool> IsAvailableAsync();

        /// <summary>
        /// Gets information about the current Ollama model
        /// </summary>
        /// <returns>Model information including name and capabilities</returns>
        Task<ModelInfo> GetModelInfoAsync();

        /// <summary>
        /// Sets the Ollama model to use
        /// </summary>
        /// <param name="modelName">The name of the model</param>
        Task SetModelAsync(string modelName);

        /// <summary>
        /// Gets the current Ollama endpoint URL
        /// </summary>
        string Endpoint { get; }

        /// <summary>
        /// Sets the Ollama endpoint URL
        /// </summary>
        /// <param name="endpoint">The endpoint URL</param>
        void SetEndpoint(string endpoint);
    }

    /// <summary>
    /// Information about an Ollama model
    /// </summary>
    public class ModelInfo
    {
        /// <summary>
        /// The name of the model
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// The model's version
        /// </summary>
        public string Version { get; set; }

        /// <summary>
        /// Description of the model's capabilities
        /// </summary>
        public string Description { get; set; }

        /// <summary>
        /// Maximum context size the model supports
        /// </summary>
        public int MaxContextSize { get; set; }

        /// <summary>
        /// Whether the model supports code completion
        /// </summary>
        public bool SupportsCodeCompletion { get; set; }

        /// <summary>
        /// The model's parameter count
        /// </summary>
        public string ParameterCount { get; set; }
    }
}