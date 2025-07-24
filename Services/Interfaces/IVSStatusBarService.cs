using System;
using System.Threading.Tasks;

namespace OllamaAssistant.Services.Interfaces
{
    /// <summary>
    /// Interface for Visual Studio Status Bar integration
    /// </summary>
    public interface IVSStatusBarService : IDisposable
    {
        /// <summary>
        /// Gets or sets whether the status bar integration is enabled
        /// </summary>
        bool IsEnabled { get; set; }

        /// <summary>
        /// Initializes the status bar service
        /// </summary>
        Task InitializeAsync();

        /// <summary>
        /// Sets the status bar text
        /// </summary>
        Task SetTextAsync(string text);

        /// <summary>
        /// Shows AI processing status with animation
        /// </summary>
        Task ShowAIProcessingAsync(string operation = "Processing AI request...");

        /// <summary>
        /// Shows AI completion status
        /// </summary>
        Task ShowAICompletedAsync(string result = "AI request completed");

        /// <summary>
        /// Shows AI error status
        /// </summary>
        Task ShowAIErrorAsync(string error = "AI request failed");

        /// <summary>
        /// Shows progress with a progress bar
        /// </summary>
        Task ShowProgressAsync(string label, uint current, uint total);

        /// <summary>
        /// Updates existing progress
        /// </summary>
        Task UpdateProgressAsync(uint current, uint total);

        /// <summary>
        /// Clears the progress bar
        /// </summary>
        Task ClearProgressAsync();

        /// <summary>
        /// Clears the status bar
        /// </summary>
        Task ClearAsync();

        /// <summary>
        /// Shows Ollama connection status
        /// </summary>
        Task ShowConnectionStatusAsync(bool isConnected, string serverInfo = null);

        /// <summary>
        /// Shows suggestion acceptance status
        /// </summary>
        Task ShowSuggestionAcceptedAsync(string suggestionType = null);
    }
}