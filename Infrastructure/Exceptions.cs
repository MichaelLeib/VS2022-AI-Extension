using System;

namespace OllamaAssistant.Infrastructure
{
    /// <summary>
    /// Base exception class for all Ollama Assistant extension errors
    /// </summary>
    public class OllamaExtensionException : Exception
    {
        public string Component { get; }
        public string CorrelationId { get; }

        public OllamaExtensionException(string message, string component = null, string correlationId = null)
            : base(message)
        {
            Component = component ?? "Unknown";
            CorrelationId = correlationId ?? Guid.NewGuid().ToString();
        }

        public OllamaExtensionException(string message, Exception innerException, string component = null, string correlationId = null)
            : base(message, innerException)
        {
            Component = component ?? "Unknown";
            CorrelationId = correlationId ?? Guid.NewGuid().ToString();
        }
    }

    /// <summary>
    /// Exception thrown when there are issues connecting to or communicating with Ollama
    /// </summary>
    public class OllamaConnectionException : OllamaExtensionException
    {
        public string EndpointUrl { get; }
        public int? StatusCode { get; }

        public OllamaConnectionException(string message, string endpointUrl = null, int? statusCode = null, string correlationId = null)
            : base(message, "OllamaConnection", correlationId)
        {
            EndpointUrl = endpointUrl;
            StatusCode = statusCode;
        }

        public OllamaConnectionException(string message, Exception innerException, string endpointUrl = null, int? statusCode = null, string correlationId = null)
            : base(message, innerException, "OllamaConnection", correlationId)
        {
            EndpointUrl = endpointUrl;
            StatusCode = statusCode;
        }
    }

    /// <summary>
    /// Exception thrown when there are issues with Ollama models
    /// </summary>
    public class OllamaModelException : OllamaExtensionException
    {
        public string ModelName { get; }
        public string Operation { get; }

        public OllamaModelException(string message, string modelName = null, string operation = null, string correlationId = null)
            : base(message, "OllamaModel", correlationId)
        {
            ModelName = modelName;
            Operation = operation;
        }

        public OllamaModelException(string message, Exception innerException, string modelName = null, string operation = null, string correlationId = null)
            : base(message, innerException, "OllamaModel", correlationId)
        {
            ModelName = modelName;
            Operation = operation;
        }
    }

    /// <summary>
    /// Exception thrown when there are issues capturing context from the editor
    /// </summary>
    public class ContextCaptureException : OllamaExtensionException
    {
        public string FileName { get; }
        public int? LineNumber { get; }
        public string Operation { get; }

        public ContextCaptureException(string message, string fileName = null, int? lineNumber = null, string operation = null, string correlationId = null)
            : base(message, "ContextCapture", correlationId)
        {
            FileName = fileName;
            LineNumber = lineNumber;
            Operation = operation;
        }

        public ContextCaptureException(string message, Exception innerException, string fileName = null, int? lineNumber = null, string operation = null, string correlationId = null)
            : base(message, innerException, "ContextCapture", correlationId)
        {
            FileName = fileName;
            LineNumber = lineNumber;
            Operation = operation;
        }
    }

    /// <summary>
    /// Exception thrown when there are issues processing AI suggestions
    /// </summary>
    public class SuggestionProcessingException : OllamaExtensionException
    {
        public string SuggestionType { get; }
        public string ProcessingStage { get; }

        public SuggestionProcessingException(string message, string suggestionType = null, string processingStage = null, string correlationId = null)
            : base(message, "SuggestionProcessing", correlationId)
        {
            SuggestionType = suggestionType;
            ProcessingStage = processingStage;
        }

        public SuggestionProcessingException(string message, Exception innerException, string suggestionType = null, string processingStage = null, string correlationId = null)
            : base(message, innerException, "SuggestionProcessing", correlationId)
        {
            SuggestionType = suggestionType;
            ProcessingStage = processingStage;
        }
    }

    /// <summary>
    /// Exception thrown when there are configuration or settings related issues
    /// </summary>
    public class SettingsException : OllamaExtensionException
    {
        public string SettingName { get; }
        public string SettingValue { get; }

        public SettingsException(string message, string settingName = null, string settingValue = null, string correlationId = null)
            : base(message, "Settings", correlationId)
        {
            SettingName = settingName;
            SettingValue = settingValue;
        }

        public SettingsException(string message, Exception innerException, string settingName = null, string settingValue = null, string correlationId = null)
            : base(message, innerException, "Settings", correlationId)
        {
            SettingName = settingName;
            SettingValue = settingValue;
        }
    }

    /// <summary>
    /// Exception thrown when there are Visual Studio integration issues
    /// </summary>
    public class VisualStudioIntegrationException : OllamaExtensionException
    {
        public string IntegrationPoint { get; }
        public string VSVersion { get; }

        public VisualStudioIntegrationException(string message, string integrationPoint = null, string vsVersion = null, string correlationId = null)
            : base(message, "VSIntegration", correlationId)
        {
            IntegrationPoint = integrationPoint;
            VSVersion = vsVersion;
        }

        public VisualStudioIntegrationException(string message, Exception innerException, string integrationPoint = null, string vsVersion = null, string correlationId = null)
            : base(message, innerException, "VSIntegration", correlationId)
        {
            IntegrationPoint = integrationPoint;
            VSVersion = vsVersion;
        }
    }
}