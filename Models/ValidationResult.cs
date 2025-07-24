using System.Collections.Generic;

namespace OllamaAssistant.Models
{
    /// <summary>
    /// Validation result for code analysis
    /// </summary>
    public class ValidationResult
    {
        /// <summary>
        /// Whether the validation passed
        /// </summary>
        public bool IsValid { get; set; }

        /// <summary>
        /// Error message if validation failed
        /// </summary>
        public string ErrorMessage { get; set; } = string.Empty;

        /// <summary>
        /// Warnings from validation
        /// </summary>
        public List<string> Warnings { get; set; } = new List<string>();

        /// <summary>
        /// Additional validation metadata
        /// </summary>
        public Dictionary<string, object> Metadata { get; set; } = new Dictionary<string, object>();

        /// <summary>
        /// Creates a valid result
        /// </summary>
        public static ValidationResult Valid()
        {
            return new ValidationResult { IsValid = true };
        }

        /// <summary>
        /// Creates an invalid result with error message
        /// </summary>
        public static ValidationResult Invalid(string errorMessage)
        {
            return new ValidationResult 
            { 
                IsValid = false, 
                ErrorMessage = errorMessage 
            };
        }
    }
}