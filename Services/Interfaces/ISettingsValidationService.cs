using OllamaAssistant.Infrastructure;

namespace OllamaAssistant.Services.Interfaces
{
    /// <summary>
    /// Service for validating and sanitizing user settings
    /// </summary>
    public interface ISettingsValidationService
    {
        /// <summary>
        /// Validates all settings before they are saved
        /// </summary>
        ValidationResult ValidateSettings(ISettingsService settings);

        /// <summary>
        /// Validates an individual setting
        /// </summary>
        ValidationResult ValidateSetting(string settingName, object value);

        /// <summary>
        /// Sanitizes a setting value to make it safe
        /// </summary>
        object SanitizeSetting(string settingName, object value);
    }
}