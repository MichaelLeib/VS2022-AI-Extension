using System;
using System.Threading.Tasks;

namespace OllamaAssistant.Services.Interfaces
{
    /// <summary>
    /// Interface for persisting settings to Visual Studio settings store
    /// </summary>
    public interface IVSSettingsPersistenceService : IDisposable
    {
        /// <summary>
        /// Gets whether the service is initialized
        /// </summary>
        bool IsInitialized { get; }

        /// <summary>
        /// Initializes the settings persistence service
        /// </summary>
        Task InitializeAsync();

        /// <summary>
        /// Saves a setting value
        /// </summary>
        Task SaveSettingAsync<T>(string key, T value);

        /// <summary>
        /// Loads a setting value
        /// </summary>
        Task<T> LoadSettingAsync<T>(string key, T defaultValue = default(T));

        /// <summary>
        /// Checks if a setting exists
        /// </summary>
        Task<bool> SettingExistsAsync(string key);

        /// <summary>
        /// Deletes a setting
        /// </summary>
        Task DeleteSettingAsync(string key);

        /// <summary>
        /// Gets all setting keys
        /// </summary>
        Task<string[]> GetAllKeysAsync();

        /// <summary>
        /// Clears all settings
        /// </summary>
        Task ClearAllSettingsAsync();

        /// <summary>
        /// Exports settings to a string (JSON format)
        /// </summary>
        Task<string> ExportSettingsAsync();

        /// <summary>
        /// Imports settings from a string (JSON format)
        /// </summary>
        Task ImportSettingsAsync(string settingsJson);
    }
}