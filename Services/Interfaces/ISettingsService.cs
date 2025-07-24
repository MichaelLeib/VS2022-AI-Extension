using System;
using System.Windows.Forms;

namespace OllamaAssistant.Services.Interfaces
{
    /// <summary>
    /// Service for managing extension settings
    /// </summary>
    public interface ISettingsService
    {
        /// <summary>
        /// Whether the extension is enabled overall
        /// </summary>
        bool IsEnabled { get; set; }

        /// <summary>
        /// The Ollama server endpoint URL
        /// </summary>
        string OllamaEndpoint { get; set; }

        /// <summary>
        /// Number of lines to capture above the cursor
        /// </summary>
        int SurroundingLinesUp { get; set; }

        /// <summary>
        /// Number of lines to capture below the cursor
        /// </summary>
        int SurroundingLinesDown { get; set; }

        /// <summary>
        /// The key used to execute jump recommendations
        /// </summary>
        Keys JumpKey { get; set; }

        /// <summary>
        /// Whether code prediction is enabled
        /// </summary>
        bool CodePredictionEnabled { get; set; }

        /// <summary>
        /// Whether jump recommendations are enabled
        /// </summary>
        bool JumpRecommendationsEnabled { get; set; }

        /// <summary>
        /// Whether automatic suggestions are enabled (alias for CodePredictionEnabled)
        /// </summary>
        bool EnableAutoSuggestions { get; set; }

        /// <summary>
        /// Whether jump recommendations are enabled (alias for JumpRecommendationsEnabled)
        /// </summary>
        bool EnableJumpRecommendations { get; set; }

        /// <summary>
        /// Maximum number of cursor positions to remember
        /// </summary>
        int CursorHistoryMemoryDepth { get; set; }

        /// <summary>
        /// The Ollama model to use
        /// </summary>
        string OllamaModel { get; set; }

        /// <summary>
        /// Timeout for Ollama requests in milliseconds
        /// </summary>
        int OllamaTimeout { get; set; }

        /// <summary>
        /// Minimum confidence threshold for showing suggestions
        /// </summary>
        double MinimumConfidenceThreshold { get; set; }

        /// <summary>
        /// Debounce delay for typing in milliseconds
        /// </summary>
        int TypingDebounceDelay { get; set; }

        /// <summary>
        /// Whether to show confidence scores in suggestions
        /// </summary>
        bool ShowConfidenceScores { get; set; }

        /// <summary>
        /// Whether to enable verbose logging
        /// </summary>
        bool EnableVerboseLogging { get; set; }

        /// <summary>
        /// Whether to include cursor history in context
        /// </summary>
        bool IncludeCursorHistory { get; set; }

        /// <summary>
        /// Maximum number of suggestions to show
        /// </summary>
        int MaxSuggestions { get; set; }

        /// <summary>
        /// Whether to enable streaming completions for faster response
        /// </summary>
        bool EnableStreamingCompletions { get; set; }

        /// <summary>
        /// Whether to filter sensitive data from requests
        /// </summary>
        bool FilterSensitiveData { get; set; }

        /// <summary>
        /// Maximum request size in KB
        /// </summary>
        int MaxRequestSizeKB { get; set; }

        /// <summary>
        /// Maximum number of concurrent requests to Ollama
        /// </summary>
        int MaxConcurrentRequests { get; set; }

        /// <summary>
        /// Maximum number of retry attempts for failed requests
        /// </summary>
        int MaxRetryAttempts { get; set; }

        /// <summary>
        /// Fired when any setting changes
        /// </summary>
        event EventHandler<SettingsChangedEventArgs> SettingsChanged;

        /// <summary>
        /// Loads settings from storage
        /// </summary>
        void LoadSettings();

        /// <summary>
        /// Saves current settings to storage
        /// </summary>
        void SaveSettings();

        /// <summary>
        /// Resets all settings to defaults
        /// </summary>
        void ResetToDefaults();

        /// <summary>
        /// Validates the current settings
        /// </summary>
        /// <returns>True if all settings are valid</returns>
        bool ValidateSettings();

        /// <summary>
        /// Gets a specific setting value
        /// </summary>
        /// <typeparam name="T">The type of the setting</typeparam>
        /// <param name="settingName">The name of the setting</param>
        /// <returns>The setting value</returns>
        T GetSetting<T>(string settingName);

        /// <summary>
        /// Sets a specific setting value
        /// </summary>
        /// <typeparam name="T">The type of the setting</typeparam>
        /// <param name="settingName">The name of the setting</param>
        /// <param name="value">The value to set</param>
        void SetSetting<T>(string settingName, T value);
    }

    /// <summary>
    /// Event args for settings change events
    /// </summary>
    public class SettingsChangedEventArgs : EventArgs
    {
        /// <summary>
        /// The name of the setting that changed
        /// </summary>
        public string SettingName { get; set; }

        /// <summary>
        /// The old value of the setting
        /// </summary>
        public object OldValue { get; set; }

        /// <summary>
        /// The new value of the setting
        /// </summary>
        public object NewValue { get; set; }

        /// <summary>
        /// Whether this change requires a restart
        /// </summary>
        public bool RequiresRestart { get; set; }

        /// <summary>
        /// Category of the setting that changed
        /// </summary>
        public SettingCategory Category { get; set; }
    }

    /// <summary>
    /// Categories of settings
    /// </summary>
    public enum SettingCategory
    {
        /// <summary>
        /// Ollama connection settings
        /// </summary>
        Connection,

        /// <summary>
        /// Context capture settings
        /// </summary>
        Context,

        /// <summary>
        /// UI and display settings
        /// </summary>
        Display,

        /// <summary>
        /// Feature toggle settings
        /// </summary>
        Features,

        /// <summary>
        /// Performance-related settings
        /// </summary>
        Performance,

        /// <summary>
        /// Debugging and logging settings
        /// </summary>
        Debugging
    }
}