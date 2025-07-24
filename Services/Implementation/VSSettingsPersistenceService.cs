using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Settings;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Settings;
using OllamaAssistant.Infrastructure;
using OllamaAssistant.Services.Interfaces;

namespace OllamaAssistant.Services.Implementation
{
    /// <summary>
    /// Service for persisting settings to Visual Studio settings store
    /// </summary>
    [Export(typeof(IVSSettingsPersistenceService))]
    public class VSSettingsPersistenceService : IVSSettingsPersistenceService
    {
        private const string COLLECTION_PATH = "OllamaAssistant";
        
        private SettingsManager _settingsManager;
        private WritableSettingsStore _settingsStore;
        private readonly object _lockObject = new object();
        private bool _isInitialized;
        private readonly ILogger _logger;

        [ImportingConstructor]
        public VSSettingsPersistenceService()
        {
            var services = ServiceLocator.Current;
            _logger = services?.Resolve<ILogger>();
        }

        /// <summary>
        /// Gets whether the service is initialized
        /// </summary>
        public bool IsInitialized => _isInitialized;

        /// <summary>
        /// Initializes the settings persistence service
        /// </summary>
        public async Task InitializeAsync()
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            try
            {
                if (_isInitialized)
                    return;

                _settingsManager = new ShellSettingsManager(ServiceProvider.GlobalProvider);
                _settingsStore = _settingsManager.GetWritableSettingsStore(SettingsScope.UserSettings);

                // Ensure collection exists
                if (!_settingsStore.CollectionExists(COLLECTION_PATH))
                {
                    _settingsStore.CreateCollection(COLLECTION_PATH);
                }

                _isInitialized = true;
                await _logger?.LogInfoAsync("VS settings persistence service initialized", "SettingsPersistence");
            }
            catch (Exception ex)
            {
                await _logger?.LogErrorAsync(ex, "Failed to initialize settings persistence service", "SettingsPersistence");
                throw;
            }
        }

        /// <summary>
        /// Saves a setting value
        /// </summary>
        public async Task SaveSettingAsync<T>(string key, T value)
        {
            if (string.IsNullOrEmpty(key))
                return;

            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            try
            {
                await EnsureInitializedAsync();

                lock (_lockObject)
                {
                    switch (value)
                    {
                        case bool boolValue:
                            _settingsStore.SetBoolean(COLLECTION_PATH, key, boolValue);
                            break;
                        case int intValue:
                            _settingsStore.SetInt32(COLLECTION_PATH, key, intValue);
                            break;
                        case uint uintValue:
                            _settingsStore.SetUInt32(COLLECTION_PATH, key, uintValue);
                            break;
                        case long longValue:
                            _settingsStore.SetInt64(COLLECTION_PATH, key, longValue);
                            break;
                        case double doubleValue:
                            _settingsStore.SetString(COLLECTION_PATH, key, doubleValue.ToString("G17"));
                            break;
                        case string stringValue:
                            _settingsStore.SetString(COLLECTION_PATH, key, stringValue ?? string.Empty);
                            break;
                        default:
                            // Serialize complex objects as JSON
                            var json = System.Text.Json.JsonSerializer.Serialize(value);
                            _settingsStore.SetString(COLLECTION_PATH, key, json);
                            break;
                    }
                }

                await _logger?.LogDebugAsync($"Setting saved: {key} = {value}", "SettingsPersistence");
            }
            catch (Exception ex)
            {
                await _logger?.LogErrorAsync(ex, $"Failed to save setting: {key}", "SettingsPersistence");
            }
        }

        /// <summary>
        /// Loads a setting value
        /// </summary>
        public async Task<T> LoadSettingAsync<T>(string key, T defaultValue = default(T))
        {
            if (string.IsNullOrEmpty(key))
                return defaultValue;

            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            try
            {
                await EnsureInitializedAsync();

                lock (_lockObject)
                {
                    if (!_settingsStore.PropertyExists(COLLECTION_PATH, key))
                    {
                        return defaultValue;
                    }

                    var type = typeof(T);
                    var underlyingType = Nullable.GetUnderlyingType(type) ?? type;

                    if (underlyingType == typeof(bool))
                    {
                        return (T)(object)_settingsStore.GetBoolean(COLLECTION_PATH, key, Convert.ToBoolean(defaultValue));
                    }
                    else if (underlyingType == typeof(int))
                    {
                        return (T)(object)_settingsStore.GetInt32(COLLECTION_PATH, key, Convert.ToInt32(defaultValue));
                    }
                    else if (underlyingType == typeof(uint))
                    {
                        return (T)(object)_settingsStore.GetUInt32(COLLECTION_PATH, key, Convert.ToUInt32(defaultValue));
                    }
                    else if (underlyingType == typeof(long))
                    {
                        return (T)(object)_settingsStore.GetInt64(COLLECTION_PATH, key, Convert.ToInt64(defaultValue));
                    }
                    else if (underlyingType == typeof(double))
                    {
                        var stringValue = _settingsStore.GetString(COLLECTION_PATH, key, defaultValue?.ToString() ?? "0");
                        return (T)(object)double.Parse(stringValue);
                    }
                    else if (underlyingType == typeof(string))
                    {
                        return (T)(object)_settingsStore.GetString(COLLECTION_PATH, key, defaultValue?.ToString() ?? string.Empty);
                    }
                    else if (underlyingType.IsEnum)
                    {
                        var stringValue = _settingsStore.GetString(COLLECTION_PATH, key, defaultValue?.ToString() ?? string.Empty);
                        if (Enum.TryParse(underlyingType, stringValue, true, out var enumValue))
                        {
                            return (T)enumValue;
                        }
                        return defaultValue;
                    }
                    else
                    {
                        // Deserialize complex objects from JSON
                        var json = _settingsStore.GetString(COLLECTION_PATH, key, string.Empty);
                        if (!string.IsNullOrEmpty(json))
                        {
                            return System.Text.Json.JsonSerializer.Deserialize<T>(json);
                        }
                        return defaultValue;
                    }
                }
            }
            catch (Exception ex)
            {
                await _logger?.LogErrorAsync(ex, $"Failed to load setting: {key}", "SettingsPersistence");
                return defaultValue;
            }
        }

        /// <summary>
        /// Checks if a setting exists
        /// </summary>
        public async Task<bool> SettingExistsAsync(string key)
        {
            if (string.IsNullOrEmpty(key))
                return false;

            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            try
            {
                await EnsureInitializedAsync();

                lock (_lockObject)
                {
                    return _settingsStore.PropertyExists(COLLECTION_PATH, key);
                }
            }
            catch (Exception ex)
            {
                await _logger?.LogErrorAsync(ex, $"Failed to check setting existence: {key}", "SettingsPersistence");
                return false;
            }
        }

        /// <summary>
        /// Deletes a setting
        /// </summary>
        public async Task DeleteSettingAsync(string key)
        {
            if (string.IsNullOrEmpty(key))
                return;

            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            try
            {
                await EnsureInitializedAsync();

                lock (_lockObject)
                {
                    if (_settingsStore.PropertyExists(COLLECTION_PATH, key))
                    {
                        _settingsStore.DeleteProperty(COLLECTION_PATH, key);
                        await _logger?.LogDebugAsync($"Setting deleted: {key}", "SettingsPersistence");
                    }
                }
            }
            catch (Exception ex)
            {
                await _logger?.LogErrorAsync(ex, $"Failed to delete setting: {key}", "SettingsPersistence");
            }
        }

        /// <summary>
        /// Gets all setting keys
        /// </summary>
        public async Task<string[]> GetAllKeysAsync()
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            try
            {
                await EnsureInitializedAsync();

                lock (_lockObject)
                {
                    if (_settingsStore.CollectionExists(COLLECTION_PATH))
                    {
                        return _settingsStore.GetPropertyNames(COLLECTION_PATH);
                    }
                    return Array.Empty<string>();
                }
            }
            catch (Exception ex)
            {
                await _logger?.LogErrorAsync(ex, "Failed to get all setting keys", "SettingsPersistence");
                return Array.Empty<string>();
            }
        }

        /// <summary>
        /// Clears all settings
        /// </summary>
        public async Task ClearAllSettingsAsync()
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            try
            {
                await EnsureInitializedAsync();

                lock (_lockObject)
                {
                    if (_settingsStore.CollectionExists(COLLECTION_PATH))
                    {
                        _settingsStore.DeleteCollection(COLLECTION_PATH);
                        _settingsStore.CreateCollection(COLLECTION_PATH);
                        await _logger?.LogInfoAsync("All settings cleared", "SettingsPersistence");
                    }
                }
            }
            catch (Exception ex)
            {
                await _logger?.LogErrorAsync(ex, "Failed to clear all settings", "SettingsPersistence");
            }
        }

        /// <summary>
        /// Exports settings to a string (JSON format)
        /// </summary>
        public async Task<string> ExportSettingsAsync()
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            try
            {
                await EnsureInitializedAsync();

                var settings = new Dictionary<string, object>();
                var keys = await GetAllKeysAsync();

                foreach (var key in keys)
                {
                    var value = _settingsStore.GetString(COLLECTION_PATH, key, string.Empty);
                    settings[key] = value;
                }

                return System.Text.Json.JsonSerializer.Serialize(settings, new System.Text.Json.JsonSerializerOptions
                {
                    WriteIndented = true
                });
            }
            catch (Exception ex)
            {
                await _logger?.LogErrorAsync(ex, "Failed to export settings", "SettingsPersistence");
                return string.Empty;
            }
        }

        /// <summary>
        /// Imports settings from a string (JSON format)
        /// </summary>
        public async Task ImportSettingsAsync(string settingsJson)
        {
            if (string.IsNullOrEmpty(settingsJson))
                return;

            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            try
            {
                await EnsureInitializedAsync();

                var settings = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(settingsJson);
                if (settings != null)
                {
                    lock (_lockObject)
                    {
                        foreach (var kvp in settings)
                        {
                            if (kvp.Value is System.Text.Json.JsonElement jsonElement)
                            {
                                var stringValue = jsonElement.GetString();
                                _settingsStore.SetString(COLLECTION_PATH, kvp.Key, stringValue);
                            }
                            else
                            {
                                _settingsStore.SetString(COLLECTION_PATH, kvp.Key, kvp.Value?.ToString() ?? string.Empty);
                            }
                        }
                    }

                    await _logger?.LogInfoAsync($"Imported {settings.Count} settings", "SettingsPersistence");
                }
            }
            catch (Exception ex)
            {
                await _logger?.LogErrorAsync(ex, "Failed to import settings", "SettingsPersistence");
            }
        }

        /// <summary>
        /// Ensures the service is initialized
        /// </summary>
        private async Task EnsureInitializedAsync()
        {
            if (!_isInitialized)
            {
                await InitializeAsync();
            }
        }

        /// <summary>
        /// Disposes the service
        /// </summary>
        public void Dispose()
        {
            try
            {
                lock (_lockObject)
                {
                    _settingsStore = null;
                    _settingsManager = null;
                    _isInitialized = false;
                }
            }
            catch
            {
                // Ignore disposal errors
            }
        }
    }
}