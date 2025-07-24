using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using OllamaAssistant.Infrastructure;
using OllamaAssistant.Models;
using OllamaAssistant.Services.Interfaces;
using ValidationResult = OllamaAssistant.Models.ValidationResult;

namespace OllamaAssistant.Services.Implementation
{
    /// <summary>
    /// Service for managing advanced configuration including model-specific parameters and custom templates
    /// </summary>
    public class AdvancedConfigurationService : IDisposable
    {
        private readonly SecurityValidator _securityValidator;
        private readonly string _configurationDirectory;
        private readonly Dictionary<string, ModelConfiguration> _modelConfigurations;
        private readonly Dictionary<string, PromptTemplate> _promptTemplates;
        private readonly ContextWindowConfiguration _contextWindowConfig;
        private readonly object _lockObject = new object();
        private bool _disposed;

        public AdvancedConfigurationService()
        {
            _securityValidator = new SecurityValidator();
            _configurationDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OllamaAssistant", "Config");
            _modelConfigurations = new Dictionary<string, ModelConfiguration>();
            _promptTemplates = new Dictionary<string, PromptTemplate>();
            _contextWindowConfig = new ContextWindowConfiguration();

            // Ensure configuration directory exists
            Directory.CreateDirectory(_configurationDirectory);

            // Initialize with defaults
            _ = Task.Run(InitializeDefaultConfigurationsAsync);
        }

        /// <summary>
        /// Gets model-specific configuration
        /// </summary>
        public ModelConfiguration GetModelConfiguration(string modelName)
        {
            if (_disposed || string.IsNullOrEmpty(modelName))
                return GetDefaultModelConfiguration();

            lock (_lockObject)
            {
                return _modelConfigurations.TryGetValue(modelName, out var config) 
                    ? config 
                    : GetDefaultModelConfiguration();
            }
        }

        /// <summary>
        /// Sets model-specific configuration
        /// </summary>
        public async Task SetModelConfigurationAsync(string modelName, ModelConfiguration configuration)
        {
            if (_disposed || string.IsNullOrEmpty(modelName) || configuration == null)
                return;

            // Validate configuration
            var validationResult = ValidateModelConfiguration(configuration);
            if (!validationResult.IsValid)
                throw new ArgumentException($"Invalid model configuration: {validationResult.ErrorMessage}");

            lock (_lockObject)
            {
                _modelConfigurations[modelName] = configuration;
            }

            // Save to disk
            await SaveModelConfigurationAsync(modelName, configuration);
        }

        /// <summary>
        /// Gets all available model configurations
        /// </summary>
        public Dictionary<string, ModelConfiguration> GetAllModelConfigurations()
        {
            lock (_lockObject)
            {
                return new Dictionary<string, ModelConfiguration>(_modelConfigurations);
            }
        }

        /// <summary>
        /// Creates a custom prompt template
        /// </summary>
        public async Task CreatePromptTemplateAsync(string templateName, PromptTemplate template)
        {
            if (_disposed || string.IsNullOrEmpty(templateName) || template == null)
                return;

            // Validate template
            var validationResult = ValidatePromptTemplate(template);
            if (!validationResult.IsValid)
                throw new ArgumentException($"Invalid prompt template: {validationResult.ErrorMessage}");

            // Sanitize template content
            template = SanitizePromptTemplate(template);

            lock (_lockObject)
            {
                _promptTemplates[templateName] = template;
            }

            // Save to disk
            await SavePromptTemplateAsync(templateName, template);
        }

        /// <summary>
        /// Gets a prompt template by name
        /// </summary>
        public PromptTemplate GetPromptTemplate(string templateName)
        {
            if (_disposed || string.IsNullOrEmpty(templateName))
                return GetDefaultPromptTemplate();

            lock (_lockObject)
            {
                return _promptTemplates.TryGetValue(templateName, out var template) 
                    ? template 
                    : GetDefaultPromptTemplate();
            }
        }

        /// <summary>
        /// Gets all available prompt templates
        /// </summary>
        public Dictionary<string, PromptTemplate> GetAllPromptTemplates()
        {
            lock (_lockObject)
            {
                return new Dictionary<string, PromptTemplate>(_promptTemplates);
            }
        }

        /// <summary>
        /// Updates context window configuration
        /// </summary>
        public async Task UpdateContextWindowConfigurationAsync(ContextWindowConfiguration configuration)
        {
            if (_disposed || configuration == null)
                return;

            // Validate configuration
            var validationResult = ValidateContextWindowConfiguration(configuration);
            if (!validationResult.IsValid)
                throw new ArgumentException($"Invalid context window configuration: {validationResult.ErrorMessage}");

            lock (_lockObject)
            {
                _contextWindowConfig.MaxTokens = configuration.MaxTokens;
                _contextWindowConfig.ReservedTokens = configuration.ReservedTokens;
                _contextWindowConfig.ContextStrategy = configuration.ContextStrategy;
                _contextWindowConfig.PriorityWeights = new Dictionary<string, double>(configuration.PriorityWeights);
            }

            // Save to disk
            await SaveContextWindowConfigurationAsync(configuration);
        }

        /// <summary>
        /// Gets current context window configuration
        /// </summary>
        public ContextWindowConfiguration GetContextWindowConfiguration()
        {
            lock (_lockObject)
            {
                return new ContextWindowConfiguration
                {
                    MaxTokens = _contextWindowConfig.MaxTokens,
                    ReservedTokens = _contextWindowConfig.ReservedTokens,
                    ContextStrategy = _contextWindowConfig.ContextStrategy,
                    PriorityWeights = new Dictionary<string, double>(_contextWindowConfig.PriorityWeights)
                };
            }
        }

        /// <summary>
        /// Exports all configurations to a file
        /// </summary>
        public async Task ExportConfigurationAsync(string filePath, ConfigurationExportOptions options = null)
        {
            if (_disposed || string.IsNullOrEmpty(filePath))
                return;

            options ??= new ConfigurationExportOptions();

            var exportData = new ConfigurationExport
            {
                Version = "1.0",
                ExportDate = DateTime.UtcNow,
                ExportedBy = Environment.UserName
            };

            lock (_lockObject)
            {
                if (options.IncludeModelConfigurations)
                {
                    exportData.ModelConfigurations = new Dictionary<string, ModelConfiguration>(_modelConfigurations);
                }

                if (options.IncludePromptTemplates)
                {
                    exportData.PromptTemplates = new Dictionary<string, PromptTemplate>(_promptTemplates);
                }

                if (options.IncludeContextWindowConfig)
                {
                    exportData.ContextWindowConfiguration = GetContextWindowConfiguration();
                }
            }

            // Sanitize sensitive information before export
            if (options.SanitizeSensitiveData)
            {
                SanitizeExportData(exportData);
            }

            // Serialize and save
            var json = JsonSerializer.Serialize(exportData, new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                Converters = { new JsonStringEnumConverter() }
            });

            await File.WriteAllTextAsync(filePath, json);
        }

        /// <summary>
        /// Imports configurations from a file
        /// </summary>
        public async Task<ConfigurationImportResult> ImportConfigurationAsync(string filePath, ConfigurationImportOptions options = null)
        {
            if (_disposed || string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
                return new ConfigurationImportResult { Success = false, ErrorMessage = "File not found or invalid path" };

            options ??= new ConfigurationImportOptions();
            var result = new ConfigurationImportResult();

            try
            {
                // Read and validate file
                var json = await File.ReadAllTextAsync(filePath);
                var importData = JsonSerializer.Deserialize<ConfigurationExport>(json, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    Converters = { new JsonStringEnumConverter() }
                });

                if (importData == null)
                {
                    result.ErrorMessage = "Invalid configuration file format";
                    return result;
                }

                // Validate import data
                var validationResult = ValidateImportData(importData);
                if (!validationResult.IsValid)
                {
                    result.ErrorMessage = validationResult.ErrorMessage;
                    return result;
                }

                // Import model configurations
                if (options.ImportModelConfigurations && importData.ModelConfigurations != null)
                {
                    foreach (var kvp in importData.ModelConfigurations)
                    {
                        try
                        {
                            await SetModelConfigurationAsync(kvp.Key, kvp.Value);
                            result.ImportedModelConfigurations.Add(kvp.Key);
                        }
                        catch (Exception ex)
                        {
                            result.Warnings.Add($"Failed to import model configuration '{kvp.Key}': {ex.Message}");
                        }
                    }
                }

                // Import prompt templates
                if (options.ImportPromptTemplates && importData.PromptTemplates != null)
                {
                    foreach (var kvp in importData.PromptTemplates)
                    {
                        try
                        {
                            await CreatePromptTemplateAsync(kvp.Key, kvp.Value);
                            result.ImportedPromptTemplates.Add(kvp.Key);
                        }
                        catch (Exception ex)
                        {
                            result.Warnings.Add($"Failed to import prompt template '{kvp.Key}': {ex.Message}");
                        }
                    }
                }

                // Import context window configuration
                if (options.ImportContextWindowConfig && importData.ContextWindowConfiguration != null)
                {
                    try
                    {
                        await UpdateContextWindowConfigurationAsync(importData.ContextWindowConfiguration);
                        result.ImportedContextWindowConfig = true;
                    }
                    catch (Exception ex)
                    {
                        result.Warnings.Add($"Failed to import context window configuration: {ex.Message}");
                    }
                }

                result.Success = true;
                result.ImportDate = DateTime.UtcNow;
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = $"Import failed: {ex.Message}";
            }

            return result;
        }

        /// <summary>
        /// Resets configurations to defaults
        /// </summary>
        public async Task ResetToDefaultsAsync()
        {
            if (_disposed)
                return;

            lock (_lockObject)
            {
                _modelConfigurations.Clear();
                _promptTemplates.Clear();
                _contextWindowConfig.MaxTokens = 4096;
                _contextWindowConfig.ReservedTokens = 512;
                _contextWindowConfig.ContextStrategy = ContextStrategy.Balanced;
                _contextWindowConfig.PriorityWeights.Clear();
            }

            await InitializeDefaultConfigurationsAsync();
        }

        /// <summary>
        /// Gets configuration statistics
        /// </summary>
        public ConfigurationStatistics GetStatistics()
        {
            lock (_lockObject)
            {
                return new ConfigurationStatistics
                {
                    ModelConfigurationCount = _modelConfigurations.Count,
                    PromptTemplateCount = _promptTemplates.Count,
                    ContextWindowMaxTokens = _contextWindowConfig.MaxTokens,
                    ConfigurationDirectory = _configurationDirectory,
                    LastModified = GetLastModifiedTime(),
                    TotalConfigurationFiles = GetConfigurationFileCount()
                };
            }
        }

        /// <summary>
        /// Initializes default configurations
        /// </summary>
        private async Task InitializeDefaultConfigurationsAsync()
        {
            try
            {
                // Initialize default model configurations
                await InitializeDefaultModelConfigurationsAsync();

                // Initialize default prompt templates
                await InitializeDefaultPromptTemplatesAsync();

                // Initialize default context window configuration
                InitializeDefaultContextWindowConfiguration();

                // Load existing configurations from disk
                await LoadExistingConfigurationsAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to initialize default configurations: {ex.Message}");
            }
        }

        /// <summary>
        /// Initializes default model configurations
        /// </summary>
        private async Task InitializeDefaultModelConfigurationsAsync()
        {
            var defaultModels = new[]
            {
                new { Name = "codellama", Config = CreateCodeLlamaConfiguration() },
                new { Name = "llama2", Config = CreateLlama2Configuration() },
                new { Name = "mistral", Config = CreateMistralConfiguration() },
                new { Name = "deepseek-coder", Config = CreateDeepSeekCoderConfiguration() }
            };

            foreach (var model in defaultModels)
            {
                await SetModelConfigurationAsync(model.Name, model.Config);
            }
        }

        /// <summary>
        /// Initializes default prompt templates
        /// </summary>
        private async Task InitializeDefaultPromptTemplatesAsync()
        {
            var defaultTemplates = new[]
            {
                new { Name = "code-completion", Template = CreateCodeCompletionTemplate() },
                new { Name = "code-explanation", Template = CreateCodeExplanationTemplate() },
                new { Name = "bug-fix", Template = CreateBugFixTemplate() },
                new { Name = "refactoring", Template = CreateRefactoringTemplate() }
            };

            foreach (var template in defaultTemplates)
            {
                await CreatePromptTemplateAsync(template.Name, template.Template);
            }
        }

        /// <summary>
        /// Initializes default context window configuration
        /// </summary>
        private void InitializeDefaultContextWindowConfiguration()
        {
            _contextWindowConfig.MaxTokens = 4096;
            _contextWindowConfig.ReservedTokens = 512;
            _contextWindowConfig.ContextStrategy = ContextStrategy.Balanced;
            _contextWindowConfig.PriorityWeights = new Dictionary<string, double>
            {
                ["current_file"] = 1.0,
                ["recent_changes"] = 0.8,
                ["related_files"] = 0.6,
                ["cursor_history"] = 0.4,
                ["project_context"] = 0.3
            };
        }

        /// <summary>
        /// Creates CodeLlama model configuration
        /// </summary>
        private ModelConfiguration CreateCodeLlamaConfiguration()
        {
            return new ModelConfiguration
            {
                ModelName = "codellama",
                Parameters = new Dictionary<string, object>
                {
                    ["temperature"] = 0.1,
                    ["top_p"] = 0.9,
                    ["top_k"] = 40,
                    ["repeat_penalty"] = 1.1,
                    ["num_predict"] = 256,
                    ["stop"] = new[] { "\n\n", "```" }
                },
                OptimizedFor = ModelOptimization.CodeGeneration,
                ContextLength = 4096,
                Description = "Optimized for code completion and generation",
                ConfidenceThreshold = 0.7,
                MaxRetries = 3,
                TimeoutSeconds = 30
            };
        }

        /// <summary>
        /// Creates Llama2 model configuration
        /// </summary>
        private ModelConfiguration CreateLlama2Configuration()
        {
            return new ModelConfiguration
            {
                ModelName = "llama2",
                Parameters = new Dictionary<string, object>
                {
                    ["temperature"] = 0.3,
                    ["top_p"] = 0.9,
                    ["top_k"] = 50,
                    ["repeat_penalty"] = 1.05,
                    ["num_predict"] = 512
                },
                OptimizedFor = ModelOptimization.General,
                ContextLength = 4096,
                Description = "General purpose language model",
                ConfidenceThreshold = 0.6,
                MaxRetries = 2,
                TimeoutSeconds = 45
            };
        }

        /// <summary>
        /// Creates Mistral model configuration
        /// </summary>
        private ModelConfiguration CreateMistralConfiguration()
        {
            return new ModelConfiguration
            {
                ModelName = "mistral",
                Parameters = new Dictionary<string, object>
                {
                    ["temperature"] = 0.2,
                    ["top_p"] = 0.95,
                    ["top_k"] = 30,
                    ["repeat_penalty"] = 1.08,
                    ["num_predict"] = 384
                },
                OptimizedFor = ModelOptimization.CodeAnalysis,
                ContextLength = 8192,
                Description = "Fast and efficient for code analysis",
                ConfidenceThreshold = 0.75,
                MaxRetries = 3,
                TimeoutSeconds = 25
            };
        }

        /// <summary>
        /// Creates DeepSeek Coder model configuration
        /// </summary>
        private ModelConfiguration CreateDeepSeekCoderConfiguration()
        {
            return new ModelConfiguration
            {
                ModelName = "deepseek-coder",
                Parameters = new Dictionary<string, object>
                {
                    ["temperature"] = 0.05,
                    ["top_p"] = 0.95,
                    ["top_k"] = 20,
                    ["repeat_penalty"] = 1.15,
                    ["num_predict"] = 512,
                    ["stop"] = new[] { "\n\n", "```", "///" }
                },
                OptimizedFor = ModelOptimization.CodeGeneration,
                ContextLength = 16384,
                Description = "Specialized for complex code generation",
                ConfidenceThreshold = 0.8,
                MaxRetries = 2,
                TimeoutSeconds = 60
            };
        }

        /// <summary>
        /// Creates code completion prompt template
        /// </summary>
        private PromptTemplate CreateCodeCompletionTemplate()
        {
            return new PromptTemplate
            {
                Name = "code-completion",
                Description = "Template for code completion requests",
                Template = @"Complete the following code based on the context provided.

Language: {language}
File: {filename}

Context before cursor:
```{language}
{lines_before}
```

Context after cursor:
```{language}
{lines_after}
```

Current line (cursor position marked with |):
{current_line}

Please provide a completion that:
1. Maintains consistent code style
2. Follows {language} best practices
3. Is contextually appropriate
4. Is concise and focused

Completion:",
                Variables = new List<string> { "language", "filename", "lines_before", "lines_after", "current_line" },
                Category = TemplateCategory.CodeCompletion,
                IsBuiltIn = true
            };
        }

        /// <summary>
        /// Creates code explanation prompt template
        /// </summary>
        private PromptTemplate CreateCodeExplanationTemplate()
        {
            return new PromptTemplate
            {
                Name = "code-explanation",
                Description = "Template for code explanation requests",
                Template = @"Explain the following code in clear, concise terms.

Language: {language}
Code to explain:
```{language}
{code}
```

Please provide:
1. What this code does
2. Key concepts used
3. Any potential issues or improvements
4. How it fits in the broader context

Explanation:",
                Variables = new List<string> { "language", "code" },
                Category = TemplateCategory.CodeAnalysis,
                IsBuiltIn = true
            };
        }

        /// <summary>
        /// Creates bug fix prompt template
        /// </summary>
        private PromptTemplate CreateBugFixTemplate()
        {
            return new PromptTemplate
            {
                Name = "bug-fix",
                Description = "Template for bug fix suggestions",
                Template = @"Analyze the following code for potential bugs and suggest fixes.

Language: {language}
Problematic code:
```{language}
{code}
```

Error message (if any): {error_message}

Please provide:
1. Identified issues
2. Recommended fixes
3. Explanation of why the fix works
4. Prevention strategies

Analysis and fix:",
                Variables = new List<string> { "language", "code", "error_message" },
                Category = TemplateCategory.BugFix,
                IsBuiltIn = true
            };
        }

        /// <summary>
        /// Creates refactoring prompt template
        /// </summary>
        private PromptTemplate CreateRefactoringTemplate()
        {
            return new PromptTemplate
            {
                Name = "refactoring",
                Description = "Template for code refactoring suggestions",
                Template = @"Suggest refactoring improvements for the following code.

Language: {language}
Code to refactor:
```{language}
{code}
```

Focus areas: {focus_areas}

Please provide:
1. Specific refactoring suggestions
2. Improved code examples
3. Benefits of each change
4. Potential trade-offs

Refactoring suggestions:",
                Variables = new List<string> { "language", "code", "focus_areas" },
                Category = TemplateCategory.Refactoring,
                IsBuiltIn = true
            };
        }

        /// <summary>
        /// Gets default model configuration
        /// </summary>
        private ModelConfiguration GetDefaultModelConfiguration()
        {
            return new ModelConfiguration
            {
                ModelName = "default",
                Parameters = new Dictionary<string, object>
                {
                    ["temperature"] = 0.2,
                    ["top_p"] = 0.9,
                    ["top_k"] = 40,
                    ["repeat_penalty"] = 1.1,
                    ["num_predict"] = 256
                },
                OptimizedFor = ModelOptimization.General,
                ContextLength = 4096,
                Description = "Default configuration",
                ConfidenceThreshold = 0.6,
                MaxRetries = 2,
                TimeoutSeconds = 30
            };
        }

        /// <summary>
        /// Gets default prompt template
        /// </summary>
        private PromptTemplate GetDefaultPromptTemplate()
        {
            return new PromptTemplate
            {
                Name = "default",
                Description = "Default prompt template",
                Template = "Context: {context}\n\nRequest: {request}\n\nResponse:",
                Variables = new List<string> { "context", "request" },
                Category = TemplateCategory.General,
                IsBuiltIn = true
            };
        }

        /// <summary>
        /// Validates model configuration
        /// </summary>
        private ValidationResult ValidateModelConfiguration(ModelConfiguration config)
        {
            if (config == null)
                return new ValidationResult { IsValid = false, ErrorMessage = "Configuration cannot be null" };

            if (string.IsNullOrEmpty(config.ModelName))
                return new ValidationResult { IsValid = false, ErrorMessage = "Model name is required" };

            if (config.ContextLength <= 0)
                return new ValidationResult { IsValid = false, ErrorMessage = "Context length must be positive" };

            if (config.ConfidenceThreshold < 0 || config.ConfidenceThreshold > 1)
                return new ValidationResult { IsValid = false, ErrorMessage = "Confidence threshold must be between 0 and 1" };

            return new ValidationResult { IsValid = true };
        }

        /// <summary>
        /// Validates prompt template
        /// </summary>
        private ValidationResult ValidatePromptTemplate(PromptTemplate template)
        {
            if (template == null)
                return new ValidationResult { IsValid = false, ErrorMessage = "Template cannot be null" };

            if (string.IsNullOrEmpty(template.Name))
                return new ValidationResult { IsValid = false, ErrorMessage = "Template name is required" };

            if (string.IsNullOrEmpty(template.Template))
                return new ValidationResult { IsValid = false, ErrorMessage = "Template content is required" };

            // Check for potential security issues
            var securityValidation = _securityValidator.ValidatePromptTemplate(template.Template);
            if (!securityValidation.IsValid)
                return securityValidation;

            return new ValidationResult { IsValid = true };
        }

        /// <summary>
        /// Validates context window configuration
        /// </summary>
        private ValidationResult ValidateContextWindowConfiguration(ContextWindowConfiguration config)
        {
            if (config == null)
                return new ValidationResult { IsValid = false, ErrorMessage = "Configuration cannot be null" };

            if (config.MaxTokens <= 0)
                return new ValidationResult { IsValid = false, ErrorMessage = "Max tokens must be positive" };

            if (config.ReservedTokens < 0)
                return new ValidationResult { IsValid = false, ErrorMessage = "Reserved tokens cannot be negative" };

            if (config.ReservedTokens >= config.MaxTokens)
                return new ValidationResult { IsValid = false, ErrorMessage = "Reserved tokens must be less than max tokens" };

            return new ValidationResult { IsValid = true };
        }

        /// <summary>
        /// Sanitizes prompt template for security
        /// </summary>
        private PromptTemplate SanitizePromptTemplate(PromptTemplate template)
        {
            return new PromptTemplate
            {
                Name = _securityValidator.SanitizeString(template.Name),
                Description = _securityValidator.SanitizeString(template.Description),
                Template = _securityValidator.SanitizePromptContent(template.Template),
                Variables = template.Variables?.Select(v => _securityValidator.SanitizeString(v)).ToList() ?? new List<string>(),
                Category = template.Category,
                IsBuiltIn = template.IsBuiltIn
            };
        }

        /// <summary>
        /// Validates import data
        /// </summary>
        private ValidationResult ValidateImportData(ConfigurationExport importData)
        {
            if (importData == null)
                return new ValidationResult { IsValid = false, ErrorMessage = "Import data cannot be null" };

            if (string.IsNullOrEmpty(importData.Version))
                return new ValidationResult { IsValid = false, ErrorMessage = "Version information is required" };

            // Additional validation can be added here

            return new ValidationResult { IsValid = true };
        }

        /// <summary>
        /// Sanitizes export data
        /// </summary>
        private void SanitizeExportData(ConfigurationExport exportData)
        {
            if (exportData.ModelConfigurations != null)
            {
                foreach (var config in exportData.ModelConfigurations.Values)
                {
                    config.Description = _securityValidator.SanitizeString(config.Description);
                }
            }

            if (exportData.PromptTemplates != null)
            {
                foreach (var template in exportData.PromptTemplates.Values)
                {
                    template.Template = _securityValidator.SanitizePromptContent(template.Template);
                    template.Description = _securityValidator.SanitizeString(template.Description);
                }
            }

            exportData.ExportedBy = _securityValidator.SanitizeString(exportData.ExportedBy);
        }

        /// <summary>
        /// Saves model configuration to disk
        /// </summary>
        private async Task SaveModelConfigurationAsync(string modelName, ModelConfiguration configuration)
        {
            try
            {
                var filePath = Path.Combine(_configurationDirectory, $"model_{modelName}.json");
                var json = JsonSerializer.Serialize(configuration, new JsonSerializerOptions
                {
                    WriteIndented = true,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                });

                await File.WriteAllTextAsync(filePath, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to save model configuration: {ex.Message}");
            }
        }

        /// <summary>
        /// Saves prompt template to disk
        /// </summary>
        private async Task SavePromptTemplateAsync(string templateName, PromptTemplate template)
        {
            try
            {
                var filePath = Path.Combine(_configurationDirectory, $"template_{templateName}.json");
                var json = JsonSerializer.Serialize(template, new JsonSerializerOptions
                {
                    WriteIndented = true,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    Converters = { new JsonStringEnumConverter() }
                });

                await File.WriteAllTextAsync(filePath, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to save prompt template: {ex.Message}");
            }
        }

        /// <summary>
        /// Saves context window configuration to disk
        /// </summary>
        private async Task SaveContextWindowConfigurationAsync(ContextWindowConfiguration configuration)
        {
            try
            {
                var filePath = Path.Combine(_configurationDirectory, "context_window.json");
                var json = JsonSerializer.Serialize(configuration, new JsonSerializerOptions
                {
                    WriteIndented = true,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    Converters = { new JsonStringEnumConverter() }
                });

                await File.WriteAllTextAsync(filePath, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to save context window configuration: {ex.Message}");
            }
        }

        /// <summary>
        /// Loads existing configurations from disk
        /// </summary>
        private async Task LoadExistingConfigurationsAsync()
        {
            try
            {
                var configFiles = Directory.GetFiles(_configurationDirectory, "*.json");

                foreach (var file in configFiles)
                {
                    var fileName = Path.GetFileNameWithoutExtension(file);
                    
                    if (fileName.StartsWith("model_"))
                    {
                        await LoadModelConfigurationAsync(file);
                    }
                    else if (fileName.StartsWith("template_"))
                    {
                        await LoadPromptTemplateAsync(file);
                    }
                    else if (fileName == "context_window")
                    {
                        await LoadContextWindowConfigurationAsync(file);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load existing configurations: {ex.Message}");
            }
        }

        /// <summary>
        /// Loads model configuration from file
        /// </summary>
        private async Task LoadModelConfigurationAsync(string filePath)
        {
            try
            {
                var json = await File.ReadAllTextAsync(filePath);
                var config = JsonSerializer.Deserialize<ModelConfiguration>(json, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                });

                if (config != null && !string.IsNullOrEmpty(config.ModelName))
                {
                    lock (_lockObject)
                    {
                        _modelConfigurations[config.ModelName] = config;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load model configuration from {filePath}: {ex.Message}");
            }
        }

        /// <summary>
        /// Loads prompt template from file
        /// </summary>
        private async Task LoadPromptTemplateAsync(string filePath)
        {
            try
            {
                var json = await File.ReadAllTextAsync(filePath);
                var template = JsonSerializer.Deserialize<PromptTemplate>(json, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    Converters = { new JsonStringEnumConverter() }
                });

                if (template != null && !string.IsNullOrEmpty(template.Name))
                {
                    lock (_lockObject)
                    {
                        _promptTemplates[template.Name] = template;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load prompt template from {filePath}: {ex.Message}");
            }
        }

        /// <summary>
        /// Loads context window configuration from file
        /// </summary>
        private async Task LoadContextWindowConfigurationAsync(string filePath)
        {
            try
            {
                var json = await File.ReadAllTextAsync(filePath);
                var config = JsonSerializer.Deserialize<ContextWindowConfiguration>(json, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    Converters = { new JsonStringEnumConverter() }
                });

                if (config != null)
                {
                    lock (_lockObject)
                    {
                        _contextWindowConfig.MaxTokens = config.MaxTokens;
                        _contextWindowConfig.ReservedTokens = config.ReservedTokens;
                        _contextWindowConfig.ContextStrategy = config.ContextStrategy;
                        _contextWindowConfig.PriorityWeights = new Dictionary<string, double>(config.PriorityWeights);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load context window configuration from {filePath}: {ex.Message}");
            }
        }

        /// <summary>
        /// Gets last modified time of configurations
        /// </summary>
        private DateTime GetLastModifiedTime()
        {
            try
            {
                var configFiles = Directory.GetFiles(_configurationDirectory, "*.json");
                return configFiles.Length > 0
                    ? configFiles.Max(f => File.GetLastWriteTime(f))
                    : DateTime.MinValue;
            }
            catch
            {
                return DateTime.MinValue;
            }
        }

        /// <summary>
        /// Gets count of configuration files
        /// </summary>
        private int GetConfigurationFileCount()
        {
            try
            {
                return Directory.GetFiles(_configurationDirectory, "*.json").Length;
            }
            catch
            {
                return 0;
            }
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            _securityValidator?.Dispose();
        }
    }

    /// <summary>
    /// Model-specific configuration
    /// </summary>
    public class ModelConfiguration
    {
        public string ModelName { get; set; }
        public Dictionary<string, object> Parameters { get; set; }
        public ModelOptimization OptimizedFor { get; set; }
        public int ContextLength { get; set; }
        public string Description { get; set; }
        public double ConfidenceThreshold { get; set; }
        public int MaxRetries { get; set; }
        public int TimeoutSeconds { get; set; }
    }

    /// <summary>
    /// Custom prompt template
    /// </summary>
    public class PromptTemplate
    {
        public string Name { get; set; }
        public string Description { get; set; }
        public string Template { get; set; }
        public List<string> Variables { get; set; }
        public TemplateCategory Category { get; set; }
        public bool IsBuiltIn { get; set; }
    }

    /// <summary>
    /// Context window configuration
    /// </summary>
    public class ContextWindowConfiguration
    {
        public int MaxTokens { get; set; } = 4096;
        public int ReservedTokens { get; set; } = 512;
        public ContextStrategy ContextStrategy { get; set; } = ContextStrategy.Balanced;
        public Dictionary<string, double> PriorityWeights { get; set; }
    }

    /// <summary>
    /// Configuration export structure
    /// </summary>
    public class ConfigurationExport
    {
        public string Version { get; set; }
        public DateTime ExportDate { get; set; }
        public string ExportedBy { get; set; }
        public Dictionary<string, ModelConfiguration> ModelConfigurations { get; set; }
        public Dictionary<string, PromptTemplate> PromptTemplates { get; set; }
        public ContextWindowConfiguration ContextWindowConfiguration { get; set; }
    }

    /// <summary>
    /// Configuration export options
    /// </summary>
    public class ConfigurationExportOptions
    {
        public bool IncludeModelConfigurations { get; set; } = true;
        public bool IncludePromptTemplates { get; set; } = true;
        public bool IncludeContextWindowConfig { get; set; } = true;
        public bool SanitizeSensitiveData { get; set; } = true;
    }

    /// <summary>
    /// Configuration import options
    /// </summary>
    public class ConfigurationImportOptions
    {
        public bool ImportModelConfigurations { get; set; } = true;
        public bool ImportPromptTemplates { get; set; } = true;
        public bool ImportContextWindowConfig { get; set; } = true;
        public bool OverwriteExisting { get; set; } = false;
    }

    /// <summary>
    /// Configuration import result
    /// </summary>
    public class ConfigurationImportResult
    {
        public bool Success { get; set; }
        public string ErrorMessage { get; set; }
        public DateTime ImportDate { get; set; }
        public List<string> ImportedModelConfigurations { get; set; }
        public List<string> ImportedPromptTemplates { get; set; }
        public bool ImportedContextWindowConfig { get; set; }
        public List<string> Warnings { get; set; }
    }

    /// <summary>
    /// Configuration statistics
    /// </summary>
    public class ConfigurationStatistics
    {
        public int ModelConfigurationCount { get; set; }
        public int PromptTemplateCount { get; set; }
        public int ContextWindowMaxTokens { get; set; }
        public string ConfigurationDirectory { get; set; }
        public DateTime LastModified { get; set; }
        public int TotalConfigurationFiles { get; set; }
    }

    /// <summary>
    /// Model optimization types
    /// </summary>
    public enum ModelOptimization
    {
        General,
        CodeGeneration,
        CodeAnalysis,
        Documentation,
        Debugging
    }

    /// <summary>
    /// Template categories
    /// </summary>
    public enum TemplateCategory
    {
        General,
        CodeCompletion,
        CodeAnalysis,
        BugFix,
        Refactoring,
        Documentation
    }

    /// <summary>
    /// Context strategies
    /// </summary>
    public enum ContextStrategy
    {
        Conservative,
        Balanced,
        Aggressive,
        Custom
    }

}