using Microsoft.VisualStudio.Shell;
using OllamaAssistant.Services.Interfaces;
using OllamaAssistant.UI.OptionPages;

namespace OllamaAssistant.Services.Implementation
{
    /// <summary>
    /// Extension methods for syncing settings service with options pages
    /// </summary>
    public static class SettingsServiceExtensions
    {
        /// <summary>
        /// Syncs the settings service with the general options page
        /// </summary>
        public static void SyncWithGeneralOptions(this ISettingsService settingsService, GeneralOptionsPage optionsPage)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            // Sync from options page to settings service
            settingsService.OllamaEndpoint = optionsPage.OllamaEndpoint;
            settingsService.OllamaModel = optionsPage.OllamaModel;
            settingsService.OllamaTimeout = optionsPage.OllamaTimeout;
            settingsService.SurroundingLinesUp = optionsPage.SurroundingLinesUp;
            settingsService.SurroundingLinesDown = optionsPage.SurroundingLinesDown;
            settingsService.CursorHistoryMemoryDepth = optionsPage.CursorHistoryMemoryDepth;
            settingsService.CodePredictionEnabled = optionsPage.CodePredictionEnabled;
            settingsService.JumpRecommendationsEnabled = optionsPage.JumpRecommendationsEnabled;
            settingsService.JumpKey = optionsPage.JumpKey;
            settingsService.ShowConfidenceScores = optionsPage.ShowConfidenceScores;
            settingsService.MinimumConfidenceThreshold = optionsPage.MinimumConfidenceThreshold;
            settingsService.TypingDebounceDelay = optionsPage.TypingDebounceDelay;
            settingsService.EnableVerboseLogging = optionsPage.EnableVerboseLogging;

            settingsService.SaveSettings();
        }

        /// <summary>
        /// Syncs the general options page with the settings service
        /// </summary>
        public static void SyncFromSettings(this GeneralOptionsPage optionsPage, ISettingsService settingsService)
        {
            optionsPage.OllamaEndpoint = settingsService.OllamaEndpoint;
            optionsPage.OllamaModel = settingsService.OllamaModel;
            optionsPage.OllamaTimeout = settingsService.OllamaTimeout;
            optionsPage.SurroundingLinesUp = settingsService.SurroundingLinesUp;
            optionsPage.SurroundingLinesDown = settingsService.SurroundingLinesDown;
            optionsPage.CursorHistoryMemoryDepth = settingsService.CursorHistoryMemoryDepth;
            optionsPage.CodePredictionEnabled = settingsService.CodePredictionEnabled;
            optionsPage.JumpRecommendationsEnabled = settingsService.JumpRecommendationsEnabled;
            optionsPage.JumpKey = settingsService.JumpKey;
            optionsPage.ShowConfidenceScores = settingsService.ShowConfidenceScores;
            optionsPage.MinimumConfidenceThreshold = settingsService.MinimumConfidenceThreshold;
            optionsPage.TypingDebounceDelay = settingsService.TypingDebounceDelay;
            optionsPage.EnableVerboseLogging = settingsService.EnableVerboseLogging;
        }
    }
}