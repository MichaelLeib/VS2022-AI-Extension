using System;
using System.Collections.Generic;
using System.Windows.Forms;
using Microsoft.VisualStudio.Settings;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Settings;
using OllamaAssistant.Services.Interfaces;

namespace OllamaAssistant.Services.Implementation
{
    /// <summary>
    /// Implementation of the settings service using Visual Studio settings store
    /// </summary>
    public class SettingsService : Interfaces.ISettingsService
    {
        private const string CollectionPath = "OllamaAssistant";
        private readonly WritableSettingsStore _settingsStore;
        private readonly Dictionary<string, object> _cachedSettings;

        // Default values
        private const string DefaultOllamaEndpoint = "http://localhost:11434";
        private const string DefaultOllamaModel = "codellama";
        private const int DefaultSurroundingLinesUp = 3;
        private const int DefaultSurroundingLinesDown = 2;
        private const int DefaultCursorHistoryDepth = 3;
        private const int DefaultOllamaTimeout = 120000;
        private const double DefaultMinimumConfidence = 0.7;
        private const int DefaultTypingDebounce = 500;
        private const int DefaultMaxSuggestions = 5;
        private const int DefaultMaxRequestSizeKB = 512;
        private const int DefaultMaxConcurrentRequests = 3;
        private const int DefaultMaxRetryAttempts = 3;

        public SettingsService(IServiceProvider serviceProvider)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            var settingsManager = new ShellSettingsManager(serviceProvider);
            _settingsStore = settingsManager.GetWritableSettingsStore(SettingsScope.UserSettings);
            _cachedSettings = new Dictionary<string, object>();

            EnsureCollectionExists();
            LoadSettings();
        }

        #region Properties

        public string OllamaEndpoint
        {
            get => GetSetting<string>(nameof(OllamaEndpoint));
            set => SetSetting(nameof(OllamaEndpoint), value);
        }

        public int SurroundingLinesUp
        {
            get => GetSetting<int>(nameof(SurroundingLinesUp));
            set => SetSetting(nameof(SurroundingLinesUp), Math.Max(0, Math.Min(50, value)));
        }

        public int SurroundingLinesDown
        {
            get => GetSetting<int>(nameof(SurroundingLinesDown));
            set => SetSetting(nameof(SurroundingLinesDown), Math.Max(0, Math.Min(50, value)));
        }

        public Keys JumpKey
        {
            get => (Keys)GetSetting<int>(nameof(JumpKey));
            set => SetSetting(nameof(JumpKey), (int)value);
        }

        public bool CodePredictionEnabled
        {
            get => GetSetting<bool>(nameof(CodePredictionEnabled));
            set => SetSetting(nameof(CodePredictionEnabled), value);
        }

        public bool JumpRecommendationsEnabled
        {
            get => GetSetting<bool>(nameof(JumpRecommendationsEnabled));
            set => SetSetting(nameof(JumpRecommendationsEnabled), value);
        }

        public int CursorHistoryMemoryDepth
        {
            get => GetSetting<int>(nameof(CursorHistoryMemoryDepth));
            set => SetSetting(nameof(CursorHistoryMemoryDepth), Math.Max(1, Math.Min(10, value)));
        }

        public string OllamaModel
        {
            get => GetSetting<string>(nameof(OllamaModel));
            set => SetSetting(nameof(OllamaModel), value);
        }

        public int OllamaTimeout
        {
            get => GetSetting<int>(nameof(OllamaTimeout));
            set => SetSetting(nameof(OllamaTimeout), Math.Max(5000, Math.Min(300000, value)));
        }

        public double MinimumConfidenceThreshold
        {
            get => GetSetting<double>(nameof(MinimumConfidenceThreshold));
            set => SetSetting(nameof(MinimumConfidenceThreshold), Math.Max(0.0, Math.Min(1.0, value)));
        }

        public int TypingDebounceDelay
        {
            get => GetSetting<int>(nameof(TypingDebounceDelay));
            set => SetSetting(nameof(TypingDebounceDelay), Math.Max(100, Math.Min(2000, value)));
        }

        public bool ShowConfidenceScores
        {
            get => GetSetting<bool>(nameof(ShowConfidenceScores));
            set => SetSetting(nameof(ShowConfidenceScores), value);
        }

        public bool EnableVerboseLogging
        {
            get => GetSetting<bool>(nameof(EnableVerboseLogging));
            set => SetSetting(nameof(EnableVerboseLogging), value);
        }

        public bool IncludeCursorHistory
        {
            get => GetSetting<bool>(nameof(IncludeCursorHistory));
            set => SetSetting(nameof(IncludeCursorHistory), value);
        }

        public int MaxSuggestions
        {
            get => GetSetting<int>(nameof(MaxSuggestions));
            set => SetSetting(nameof(MaxSuggestions), Math.Max(1, Math.Min(20, value)));
        }

        public bool EnableStreamingCompletions
        {
            get => GetSetting<bool>(nameof(EnableStreamingCompletions));
            set => SetSetting(nameof(EnableStreamingCompletions), value);
        }

        public bool FilterSensitiveData
        {
            get => GetSetting<bool>(nameof(FilterSensitiveData));
            set => SetSetting(nameof(FilterSensitiveData), value);
        }

        public int MaxRequestSizeKB
        {
            get => GetSetting<int>(nameof(MaxRequestSizeKB));
            set => SetSetting(nameof(MaxRequestSizeKB), Math.Max(64, Math.Min(2048, value)));
        }

        public int MaxConcurrentRequests
        {
            get => GetSetting<int>(nameof(MaxConcurrentRequests));
            set => SetSetting(nameof(MaxConcurrentRequests), Math.Max(1, Math.Min(10, value)));
        }

        public int MaxRetryAttempts
        {
            get => GetSetting<int>(nameof(MaxRetryAttempts));
            set => SetSetting(nameof(MaxRetryAttempts), Math.Max(0, Math.Min(10, value)));
        }

        // Aliases for backward compatibility
        public bool IsEnabled
        {
            get => GetSetting<bool>(nameof(IsEnabled));
            set => SetSetting(nameof(IsEnabled), value);
        }

        public bool EnableAutoSuggestions
        {
            get => CodePredictionEnabled;
            set => CodePredictionEnabled = value;
        }

        public bool EnableJumpRecommendations
        {
            get => JumpRecommendationsEnabled;
            set => JumpRecommendationsEnabled = value;
        }

        #endregion

        #region Events

        public event EventHandler<SettingsChangedEventArgs> SettingsChanged;

        #endregion

        #region Public Methods

        public void LoadSettings()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            _cachedSettings.Clear();

            // Load all settings with defaults
            LoadSetting(nameof(IsEnabled), true);
            LoadSetting(nameof(OllamaEndpoint), DefaultOllamaEndpoint);
            LoadSetting(nameof(OllamaModel), DefaultOllamaModel);
            LoadSetting(nameof(SurroundingLinesUp), DefaultSurroundingLinesUp);
            LoadSetting(nameof(SurroundingLinesDown), DefaultSurroundingLinesDown);
            LoadSetting(nameof(JumpKey), (int)Keys.Tab);
            LoadSetting(nameof(CodePredictionEnabled), true);
            LoadSetting(nameof(JumpRecommendationsEnabled), true);
            LoadSetting(nameof(CursorHistoryMemoryDepth), DefaultCursorHistoryDepth);
            LoadSetting(nameof(OllamaTimeout), DefaultOllamaTimeout);
            LoadSetting(nameof(MinimumConfidenceThreshold), DefaultMinimumConfidence);
            LoadSetting(nameof(TypingDebounceDelay), DefaultTypingDebounce);
            LoadSetting(nameof(ShowConfidenceScores), false);
            LoadSetting(nameof(EnableVerboseLogging), false);
            LoadSetting(nameof(IncludeCursorHistory), true);
            LoadSetting(nameof(MaxSuggestions), DefaultMaxSuggestions);
            LoadSetting(nameof(EnableStreamingCompletions), false);
            LoadSetting(nameof(FilterSensitiveData), true);
            LoadSetting(nameof(MaxRequestSizeKB), DefaultMaxRequestSizeKB);
            LoadSetting(nameof(MaxConcurrentRequests), DefaultMaxConcurrentRequests);
            LoadSetting(nameof(MaxRetryAttempts), DefaultMaxRetryAttempts);
        }

        public void SaveSettings()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            foreach (var kvp in _cachedSettings)
            {
                SaveSettingToStore(kvp.Key, kvp.Value);
            }
        }

        public void ResetToDefaults()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            _cachedSettings.Clear();
            _settingsStore.DeleteCollection(CollectionPath);
            EnsureCollectionExists();
            LoadSettings();

            SettingsChanged?.Invoke(this, new SettingsChangedEventArgs
            {
                SettingName = "All",
                Category = SettingCategory.Connection
            });
        }

        public bool ValidateSettings()
        {
            try
            {
                // Validate Ollama endpoint
                if (string.IsNullOrWhiteSpace(OllamaEndpoint))
                    return false;

                if (!Uri.TryCreate(OllamaEndpoint, UriKind.Absolute, out var uri))
                    return false;

                // Validate model name
                if (string.IsNullOrWhiteSpace(OllamaModel))
                    return false;

                // Validate numeric ranges
                if (SurroundingLinesUp < 0 || SurroundingLinesUp > 50)
                    return false;

                if (SurroundingLinesDown < 0 || SurroundingLinesDown > 50)
                    return false;

                if (CursorHistoryMemoryDepth < 1 || CursorHistoryMemoryDepth > 10)
                    return false;

                if (MinimumConfidenceThreshold < 0.0 || MinimumConfidenceThreshold > 1.0)
                    return false;

                return true;
            }
            catch
            {
                return false;
            }
        }

        public T GetSetting<T>(string settingName)
        {
            if (_cachedSettings.TryGetValue(settingName, out var value))
            {
                return (T)value;
            }

            return default(T);
        }

        public void SetSetting<T>(string settingName, T value)
        {
            var oldValue = _cachedSettings.ContainsKey(settingName) ? _cachedSettings[settingName] : null;
            _cachedSettings[settingName] = value;

            ThreadHelper.ThrowIfNotOnUIThread();
            SaveSettingToStore(settingName, value);

            var category = GetSettingCategory(settingName);
            var requiresRestart = IsRestartRequired(settingName);

            SettingsChanged?.Invoke(this, new SettingsChangedEventArgs
            {
                SettingName = settingName,
                OldValue = oldValue,
                NewValue = value,
                Category = category,
                RequiresRestart = requiresRestart
            });
        }

        #endregion

        #region Private Methods

        private void EnsureCollectionExists()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (!_settingsStore.CollectionExists(CollectionPath))
            {
                _settingsStore.CreateCollection(CollectionPath);
            }
        }

        private void LoadSetting<T>(string name, T defaultValue)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            try
            {
                if (typeof(T) == typeof(string))
                {
                    var value = _settingsStore.GetString(CollectionPath, name, defaultValue as string);
                    _cachedSettings[name] = value;
                }
                else if (typeof(T) == typeof(int))
                {
                    var value = _settingsStore.GetInt32(CollectionPath, name, Convert.ToInt32(defaultValue));
                    _cachedSettings[name] = value;
                }
                else if (typeof(T) == typeof(bool))
                {
                    var value = _settingsStore.GetBoolean(CollectionPath, name, Convert.ToBoolean(defaultValue));
                    _cachedSettings[name] = value;
                }
                else if (typeof(T) == typeof(double))
                {
                    // Store doubles as strings to preserve precision
                    var stringValue = _settingsStore.GetString(CollectionPath, name, defaultValue.ToString());
                    if (double.TryParse(stringValue, out var value))
                    {
                        _cachedSettings[name] = value;
                    }
                    else
                    {
                        _cachedSettings[name] = defaultValue;
                    }
                }
            }
            catch
            {
                _cachedSettings[name] = defaultValue;
            }
        }

        private void SaveSettingToStore(string name, object value)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (value is string stringValue)
            {
                _settingsStore.SetString(CollectionPath, name, stringValue);
            }
            else if (value is int intValue)
            {
                _settingsStore.SetInt32(CollectionPath, name, intValue);
            }
            else if (value is bool boolValue)
            {
                _settingsStore.SetBoolean(CollectionPath, name, boolValue);
            }
            else if (value is double doubleValue)
            {
                _settingsStore.SetString(CollectionPath, name, doubleValue.ToString());
            }
        }

        private SettingCategory GetSettingCategory(string settingName)
        {
            switch (settingName)
            {
                case nameof(OllamaEndpoint):
                case nameof(OllamaModel):
                case nameof(OllamaTimeout):
                case nameof(MaxConcurrentRequests):
                case nameof(MaxRetryAttempts):
                    return SettingCategory.Connection;

                case nameof(SurroundingLinesUp):
                case nameof(SurroundingLinesDown):
                case nameof(CursorHistoryMemoryDepth):
                case nameof(IncludeCursorHistory):
                case nameof(MaxRequestSizeKB):
                    return SettingCategory.Context;

                case nameof(ShowConfidenceScores):
                case nameof(JumpKey):
                    return SettingCategory.Display;

                case nameof(IsEnabled):
                case nameof(CodePredictionEnabled):
                case nameof(JumpRecommendationsEnabled):
                case nameof(EnableStreamingCompletions):
                case nameof(FilterSensitiveData):
                    return SettingCategory.Features;

                case nameof(MinimumConfidenceThreshold):
                case nameof(TypingDebounceDelay):
                case nameof(MaxSuggestions):
                    return SettingCategory.Performance;

                case nameof(EnableVerboseLogging):
                    return SettingCategory.Debugging;

                default:
                    return SettingCategory.Features;
            }
        }

        private bool IsRestartRequired(string settingName)
        {
            // Most settings can be applied immediately
            // Only fundamental changes require restart
            return false;
        }

        #endregion
    }
}