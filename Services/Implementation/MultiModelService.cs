using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OllamaAssistant.Infrastructure;
using OllamaAssistant.Models;
using OllamaAssistant.Services.Interfaces;

namespace OllamaAssistant.Services.Implementation
{
    /// <summary>
    /// Service for managing multiple AI models simultaneously with real-time switching and performance monitoring
    /// </summary>
    public class MultiModelService : IDisposable
    {
        private readonly ConcurrentDictionary<string, ModelInstance> _modelInstances;
        private readonly ModelPerformanceMonitor _performanceMonitor;
        private readonly ModelRouter _modelRouter;
        private readonly ConfidenceScorer _confidenceScorer;
        private readonly ModelHealthChecker _healthChecker;
        private readonly Timer _performanceTimer;
        private readonly Timer _healthCheckTimer;
        private readonly object _lockObject = new object();
        private bool _disposed;

        public MultiModelService()
        {
            _modelInstances = new ConcurrentDictionary<string, ModelInstance>();
            _performanceMonitor = new ModelPerformanceMonitor();
            _modelRouter = new ModelRouter();
            _confidenceScorer = new ConfidenceScorer();
            _healthChecker = new ModelHealthChecker();

            // Performance monitoring every 30 seconds
            _performanceTimer = new Timer(MonitorPerformance, null, 
                TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));

            // Health checks every 2 minutes
            _healthCheckTimer = new Timer(PerformHealthChecks, null,
                TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(2));

            // Initialize with default models
            _ = Task.Run(InitializeDefaultModelsAsync);
        }

        /// <summary>
        /// Registers a new model for use
        /// </summary>
        public async Task<ModelRegistrationResult> RegisterModelAsync(ModelRegistration registration)
        {
            if (_disposed || registration == null || string.IsNullOrEmpty(registration.ModelName))
                return new ModelRegistrationResult { Success = false, ErrorMessage = "Invalid registration" };

            try
            {
                // Validate model availability
                var healthCheck = await _healthChecker.CheckModelHealthAsync(registration.ModelName);
                if (!healthCheck.IsHealthy)
                {
                    return new ModelRegistrationResult 
                    { 
                        Success = false, 
                        ErrorMessage = $"Model health check failed: {healthCheck.ErrorMessage}" 
                    };
                }

                // Create model instance
                var instance = new ModelInstance
                {
                    ModelName = registration.ModelName,
                    DisplayName = registration.DisplayName ?? registration.ModelName,
                    Configuration = registration.Configuration,
                    Capabilities = registration.Capabilities,
                    Priority = registration.Priority,
                    IsEnabled = true,
                    RegistrationTime = DateTime.UtcNow,
                    LastUsed = DateTime.MinValue,
                    Statistics = new ModelStatistics()
                };

                // Register with the system
                _modelInstances.TryAdd(registration.ModelName, instance);

                // Initialize performance monitoring
                _performanceMonitor.InitializeModel(registration.ModelName);

                // Update routing configuration
                await _modelRouter.UpdateRoutingConfigurationAsync();

                return new ModelRegistrationResult 
                { 
                    Success = true, 
                    ModelName = registration.ModelName,
                    InstanceId = instance.InstanceId
                };
            }
            catch (Exception ex)
            {
                return new ModelRegistrationResult 
                { 
                    Success = false, 
                    ErrorMessage = $"Registration failed: {ex.Message}" 
                };
            }
        }

        /// <summary>
        /// Gets code suggestion using the optimal model for the context
        /// </summary>
        public async Task<MultiModelCodeSuggestion> GetCodeSuggestionAsync(CodeContext context, ModelSelectionHint hint = null, CancellationToken cancellationToken = default)
        {
            if (_disposed || context == null)
                return CreateEmptyMultiModelSuggestion();

            try
            {
                // Select optimal model(s) for this request
                var selectedModels = await _modelRouter.SelectModelsAsync(context, hint);
                
                if (!selectedModels.Any())
                {
                    return CreateEmptyMultiModelSuggestion("No suitable models available");
                }

                // Execute requests in parallel for multiple models if configured
                var tasks = selectedModels.Take(3).Select(async model => // Limit to 3 models max
                {
                    try
                    {
                        var startTime = DateTime.UtcNow;
                        var suggestion = await GetSuggestionFromModelAsync(model, context, cancellationToken);
                        var duration = DateTime.UtcNow - startTime;

                        // Record performance metrics
                        _performanceMonitor.RecordRequest(model, duration, suggestion != null);

                        // Calculate model-specific confidence
                        var confidence = _confidenceScorer.CalculateConfidence(model, suggestion, context);

                        return new ModelSuggestionResult
                        {
                            ModelName = model,
                            Suggestion = suggestion,
                            Confidence = confidence,
                            ResponseTime = duration,
                            Success = suggestion != null
                        };
                    }
                    catch (Exception ex)
                    {
                        _performanceMonitor.RecordRequest(model, TimeSpan.Zero, false);
                        return new ModelSuggestionResult
                        {
                            ModelName = model,
                            Success = false,
                            ErrorMessage = ex.Message,
                            ResponseTime = TimeSpan.Zero
                        };
                    }
                });

                var results = await Task.WhenAll(tasks);
                var successfulResults = results.Where(r => r.Success).ToList();

                // Select best result based on confidence and performance
                var bestResult = SelectBestResult(successfulResults);

                // Update model usage statistics
                foreach (var result in results)
                {
                    UpdateModelStatistics(result);
                }

                return new MultiModelCodeSuggestion
                {
                    PrimaryResult = bestResult,
                    AlternativeResults = successfulResults.Where(r => r != bestResult).ToList(),
                    TotalModelsQueried = results.Length,
                    SuccessfulModels = successfulResults.Count,
                    SelectionReason = GetSelectionReason(bestResult, successfulResults),
                    QueryTime = results.Max(r => r.ResponseTime)
                };
            }
            catch (Exception ex)
            {
                return CreateEmptyMultiModelSuggestion($"Multi-model query failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Switches to a different model in real-time
        /// </summary>
        public async Task<ModelSwitchResult> SwitchToModelAsync(string modelName, string reason = null)
        {
            if (_disposed || string.IsNullOrEmpty(modelName))
                return new ModelSwitchResult { Success = false, ErrorMessage = "Invalid model name" };

            try
            {
                // Check if model is available and healthy
                if (!_modelInstances.TryGetValue(modelName, out var instance))
                {
                    return new ModelSwitchResult 
                    { 
                        Success = false, 
                        ErrorMessage = $"Model '{modelName}' not registered" 
                    };
                }

                if (!instance.IsEnabled)
                {
                    return new ModelSwitchResult 
                    { 
                        Success = false, 
                        ErrorMessage = $"Model '{modelName}' is disabled" 
                    };
                }

                // Perform health check
                var healthCheck = await _healthChecker.CheckModelHealthAsync(modelName);
                if (!healthCheck.IsHealthy)
                {
                    return new ModelSwitchResult 
                    { 
                        Success = false, 
                        ErrorMessage = $"Model '{modelName}' failed health check: {healthCheck.ErrorMessage}" 
                    };
                }

                // Update routing preferences
                await _modelRouter.SetPreferredModelAsync(modelName, reason);

                // Update instance usage
                instance.LastUsed = DateTime.UtcNow;
                instance.Statistics.TotalSwitches++;

                return new ModelSwitchResult 
                { 
                    Success = true, 
                    ModelName = modelName,
                    PreviousModel = _modelRouter.GetCurrentPreferredModel(),
                    SwitchReason = reason,
                    SwitchTime = DateTime.UtcNow
                };
            }
            catch (Exception ex)
            {
                return new ModelSwitchResult 
                { 
                    Success = false, 
                    ErrorMessage = $"Model switch failed: {ex.Message}" 
                };
            }
        }

        /// <summary>
        /// Gets comprehensive performance statistics for all models
        /// </summary>
        public MultiModelPerformanceReport GetPerformanceReport()
        {
            lock (_lockObject)
            {
                var report = new MultiModelPerformanceReport
                {
                    GeneratedAt = DateTime.UtcNow,
                    TotalModelsRegistered = _modelInstances.Count,
                    ActiveModels = _modelInstances.Values.Count(m => m.IsEnabled),
                    ModelReports = new List<ModelPerformanceReport>()
                };

                foreach (var instance in _modelInstances.Values)
                {
                    var performanceData = _performanceMonitor.GetModelPerformance(instance.ModelName);
                    var healthData = _healthChecker.GetModelHealth(instance.ModelName);

                    report.ModelReports.Add(new ModelPerformanceReport
                    {
                        ModelName = instance.ModelName,
                        DisplayName = instance.DisplayName,
                        IsEnabled = instance.IsEnabled,
                        RegistrationTime = instance.RegistrationTime,
                        LastUsed = instance.LastUsed,
                        Statistics = instance.Statistics,
                        Performance = performanceData,
                        Health = healthData,
                        CurrentLoad = CalculateModelLoad(instance.ModelName),
                        Capabilities = instance.Capabilities
                    });
                }

                // Sort by performance score
                report.ModelReports = report.ModelReports
                    .OrderByDescending(r => CalculateOverallScore(r))
                    .ToList();

                return report;
            }
        }

        /// <summary>
        /// Gets available models with their current status
        /// </summary>
        public List<ModelInfo> GetAvailableModels()
        {
            lock (_lockObject)
            {
                return _modelInstances.Values.Select(instance => new ModelInfo
                {
                    ModelName = instance.ModelName,
                    DisplayName = instance.DisplayName,
                    IsEnabled = instance.IsEnabled,
                    Capabilities = instance.Capabilities,
                    Priority = instance.Priority,
                    LastUsed = instance.LastUsed,
                    AverageResponseTime = _performanceMonitor.GetAverageResponseTime(instance.ModelName),
                    SuccessRate = _performanceMonitor.GetSuccessRate(instance.ModelName),
                    CurrentLoad = CalculateModelLoad(instance.ModelName)
                }).ToList();
            }
        }

        /// <summary>
        /// Enables or disables a specific model
        /// </summary>
        public async Task<bool> SetModelEnabledAsync(string modelName, bool enabled, string reason = null)
        {
            if (_disposed || string.IsNullOrEmpty(modelName))
                return false;

            if (_modelInstances.TryGetValue(modelName, out var instance))
            {
                instance.IsEnabled = enabled;
                
                if (!enabled)
                {
                    // If disabling the currently preferred model, switch to another
                    if (_modelRouter.GetCurrentPreferredModel() == modelName)
                    {
                        var alternatives = GetAvailableModels()
                            .Where(m => m.IsEnabled && m.ModelName != modelName)
                            .OrderByDescending(m => m.Priority)
                            .ToList();

                        if (alternatives.Any())
                        {
                            await _modelRouter.SetPreferredModelAsync(alternatives.First().ModelName, 
                                $"Auto-switch due to {modelName} being disabled: {reason}");
                        }
                    }
                }

                await _modelRouter.UpdateRoutingConfigurationAsync();
                return true;
            }

            return false;
        }

        /// <summary>
        /// Initializes default models
        /// </summary>
        private async Task InitializeDefaultModelsAsync()
        {
            var defaultModels = new[]
            {
                new ModelRegistration
                {
                    ModelName = "codellama",
                    DisplayName = "CodeLlama",
                    Priority = ModelPriority.High,
                    Capabilities = new List<ModelCapability> 
                    { 
                        ModelCapability.CodeGeneration, 
                        ModelCapability.CodeCompletion,
                        ModelCapability.CodeAnalysis 
                    },
                    Configuration = new ModelConfiguration
                    {
                        ModelName = "codellama",
                        Parameters = new Dictionary<string, object>
                        {
                            ["temperature"] = 0.1,
                            ["top_p"] = 0.9,
                            ["num_predict"] = 256
                        }
                    }
                },
                new ModelRegistration
                {
                    ModelName = "deepseek-coder",
                    DisplayName = "DeepSeek Coder",
                    Priority = ModelPriority.High,
                    Capabilities = new List<ModelCapability> 
                    { 
                        ModelCapability.CodeGeneration, 
                        ModelCapability.CodeCompletion,
                        ModelCapability.Debugging 
                    }
                },
                new ModelRegistration
                {
                    ModelName = "mistral",
                    DisplayName = "Mistral",
                    Priority = ModelPriority.Medium,
                    Capabilities = new List<ModelCapability> 
                    { 
                        ModelCapability.CodeAnalysis, 
                        ModelCapability.Documentation,
                        ModelCapability.GeneralPurpose 
                    }
                }
            };

            foreach (var model in defaultModels)
            {
                await RegisterModelAsync(model);
            }
        }

        /// <summary>
        /// Gets suggestion from a specific model
        /// </summary>
        private async Task<CodeSuggestion> GetSuggestionFromModelAsync(string modelName, CodeContext context, CancellationToken cancellationToken)
        {
            if (!_modelInstances.TryGetValue(modelName, out var instance))
                return null;

            // This would integrate with the actual OllamaService
            // For now, return a mock suggestion
            await Task.Delay(100 + new Random().Next(200), cancellationToken); // Simulate network delay

            return new CodeSuggestion
            {
                Text = $"// Generated by {modelName}\nfunction example() {{\n    return 'hello world';\n}}",
                Confidence = 0.8 + (new Random().NextDouble() * 0.2), // 0.8-1.0
                Type = SuggestionType.Snippet,
                Language = context.Language ?? "javascript",
                Metadata = new Dictionary<string, object>
                {
                    ["model"] = modelName,
                    ["generation_time"] = DateTime.UtcNow
                }
            };
        }

        /// <summary>
        /// Selects the best result from multiple model responses
        /// </summary>
        private ModelSuggestionResult SelectBestResult(List<ModelSuggestionResult> results)
        {
            if (!results.Any())
                return null;

            // Score based on confidence, response time, and model priority
            return results.OrderByDescending(r => 
            {
                var modelPriority = GetModelPriority(r.ModelName);
                var responseTimeScore = Math.Max(0, 1 - (r.ResponseTime.TotalSeconds / 10)); // Penalize slow responses
                return (r.Confidence * 0.6) + (responseTimeScore * 0.2) + (((double)modelPriority / 3) * 0.2);
            }).First();
        }

        /// <summary>
        /// Gets model priority as numeric value
        /// </summary>
        private int GetModelPriority(string modelName)
        {
            if (_modelInstances.TryGetValue(modelName, out var instance))
            {
                return (int)instance.Priority;
            }
            return 1; // Default to low priority
        }

        /// <summary>
        /// Gets selection reason for the chosen result
        /// </summary>
        private string GetSelectionReason(ModelSuggestionResult bestResult, List<ModelSuggestionResult> allResults)
        {
            if (bestResult == null)
                return "No successful results";

            if (allResults.Count == 1)
                return $"Only {bestResult.ModelName} provided a successful response";

            var reasons = new List<string>();

            if (bestResult.Confidence == allResults.Max(r => r.Confidence))
                reasons.Add("highest confidence");

            if (bestResult.ResponseTime == allResults.Min(r => r.ResponseTime))
                reasons.Add("fastest response");

            var modelPriority = GetModelPriority(bestResult.ModelName);
            if (modelPriority == allResults.Max(r => GetModelPriority(r.ModelName)))
                reasons.Add("highest priority model");

            return reasons.Any() 
                ? $"{bestResult.ModelName} selected: {string.Join(", ", reasons)}"
                : $"{bestResult.ModelName} selected by composite scoring";
        }

        /// <summary>
        /// Updates model statistics based on result
        /// </summary>
        private void UpdateModelStatistics(ModelSuggestionResult result)
        {
            if (_modelInstances.TryGetValue(result.ModelName, out var instance))
            {
                instance.Statistics.TotalRequests++;
                instance.LastUsed = DateTime.UtcNow;

                if (result.Success)
                {
                    instance.Statistics.SuccessfulRequests++;
                    instance.Statistics.TotalResponseTime = instance.Statistics.TotalResponseTime.Add(result.ResponseTime);
                }
                else
                {
                    instance.Statistics.FailedRequests++;
                }
            }
        }

        /// <summary>
        /// Calculates current load for a model
        /// </summary>
        private double CalculateModelLoad(string modelName)
        {
            // This would calculate actual load based on active requests
            // For now, return a simulated value
            var random = new Random();
            return random.NextDouble() * 0.5; // 0-50% load
        }

        /// <summary>
        /// Calculates overall performance score for a model
        /// </summary>
        private double CalculateOverallScore(ModelPerformanceReport report)
        {
            var successRate = report.Statistics.TotalRequests > 0 
                ? (double)report.Statistics.SuccessfulRequests / report.Statistics.TotalRequests
                : 0;

            var avgResponseTime = report.Statistics.SuccessfulRequests > 0
                ? report.Statistics.TotalResponseTime.TotalSeconds / report.Statistics.SuccessfulRequests
                : 10; // Default to 10 seconds for unknown

            var responseTimeScore = Math.Max(0, 1 - (avgResponseTime / 10));
            var loadScore = Math.Max(0, 1 - report.CurrentLoad);
            var priorityScore = (bool)report.Performance?.Priority.HasValue ? (double)report.Performance?.Priority : 1 / 3;

            return (successRate * 0.4) + (responseTimeScore * 0.3) + (loadScore * 0.2) + (priorityScore * 0.1);
        }

        /// <summary>
        /// Creates empty multi-model suggestion
        /// </summary>
        private MultiModelCodeSuggestion CreateEmptyMultiModelSuggestion(string reason = null)
        {
            return new MultiModelCodeSuggestion
            {
                PrimaryResult = null,
                AlternativeResults = new List<ModelSuggestionResult>(),
                TotalModelsQueried = 0,
                SuccessfulModels = 0,
                SelectionReason = reason ?? "No models available",
                QueryTime = TimeSpan.Zero
            };
        }

        /// <summary>
        /// Monitors performance periodically
        /// </summary>
        private void MonitorPerformance(object state)
        {
            if (_disposed)
                return;

            try
            {
                _performanceMonitor.UpdateMetrics();
                
                // Auto-disable models that are consistently failing
                var failingModels = _modelInstances.Values
                    .Where(m => m.IsEnabled && ShouldDisableModel(m))
                    .ToList();

                foreach (var model in failingModels)
                {
                    _ = SetModelEnabledAsync(model.ModelName, false, "Auto-disabled due to poor performance");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Performance monitoring failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Performs health checks on all models
        /// </summary>
        private void PerformHealthChecks(object state)
        {
            if (_disposed)
                return;

            try
            {
                var healthCheckTasks = _modelInstances.Keys.Select(async modelName =>
                {
                    try
                    {
                        var health = await _healthChecker.CheckModelHealthAsync(modelName);
                        
                        if (_modelInstances.TryGetValue(modelName, out var instance))
                        {
                            // Auto-enable healthy models that were disabled
                            if (!instance.IsEnabled && health.IsHealthy)
                            {
                                await SetModelEnabledAsync(modelName, true, "Auto-enabled after health recovery");
                            }
                            // Auto-disable unhealthy models
                            else if (instance.IsEnabled && !health.IsHealthy)
                            {
                                await SetModelEnabledAsync(modelName, false, $"Auto-disabled due to health issue: {health.ErrorMessage}");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Health check failed for {modelName}: {ex.Message}");
                    }
                });

                _ = Task.WhenAll(healthCheckTasks);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Health check monitoring failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Determines if a model should be auto-disabled
        /// </summary>
        private bool ShouldDisableModel(ModelInstance model)
        {
            // Disable if success rate is below 20% and we have at least 10 requests
            if (model.Statistics.TotalRequests >= 10)
            {
                var successRate = (double)model.Statistics.SuccessfulRequests / model.Statistics.TotalRequests;
                return successRate < 0.2;
            }

            return false;
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            _performanceTimer?.Dispose();
            _healthCheckTimer?.Dispose();
            _performanceMonitor?.Dispose();
            _modelRouter?.Dispose();
            _confidenceScorer?.Dispose();
            _healthChecker?.Dispose();
        }
    }

    /// <summary>
    /// Model instance information
    /// </summary>
    public class ModelInstance
    {
        public string InstanceId { get; set; } = Guid.NewGuid().ToString();
        public string ModelName { get; set; }
        public string DisplayName { get; set; }
        public ModelConfiguration Configuration { get; set; }
        public List<ModelCapability> Capabilities { get; set; }
        public ModelPriority Priority { get; set; } = ModelPriority.Medium;
        public bool IsEnabled { get; set; } = true;
        public DateTime RegistrationTime { get; set; }
        public DateTime LastUsed { get; set; }
        public ModelStatistics Statistics { get; set; }
    }

    /// <summary>
    /// Model registration information
    /// </summary>
    public class ModelRegistration
    {
        public string ModelName { get; set; }
        public string DisplayName { get; set; }
        public ModelConfiguration Configuration { get; set; }
        public List<ModelCapability> Capabilities { get; set; }
        public ModelPriority Priority { get; set; } = ModelPriority.Medium;
    }

    /// <summary>
    /// Model registration result
    /// </summary>
    public class ModelRegistrationResult
    {
        public bool Success { get; set; }
        public string ModelName { get; set; }
        public string InstanceId { get; set; }
        public string ErrorMessage { get; set; }
    }

    /// <summary>
    /// Multi-model code suggestion result
    /// </summary>
    public class MultiModelCodeSuggestion
    {
        public ModelSuggestionResult PrimaryResult { get; set; }
        public List<ModelSuggestionResult> AlternativeResults { get; set; }
        public int TotalModelsQueried { get; set; }
        public int SuccessfulModels { get; set; }
        public string SelectionReason { get; set; }
        public TimeSpan QueryTime { get; set; }
    }

    /// <summary>
    /// Individual model suggestion result
    /// </summary>
    public class ModelSuggestionResult
    {
        public string ModelName { get; set; }
        public CodeSuggestion Suggestion { get; set; }
        public double Confidence { get; set; }
        public TimeSpan ResponseTime { get; set; }
        public bool Success { get; set; }
        public string ErrorMessage { get; set; }
    }

    /// <summary>
    /// Model selection hint
    /// </summary>
    public class ModelSelectionHint
    {
        public List<ModelCapability> PreferredCapabilities { get; set; }
        public List<string> PreferredModels { get; set; }
        public List<string> ExcludedModels { get; set; }
        public bool RequireHighConfidence { get; set; }
        public TimeSpan MaxResponseTime { get; set; } = TimeSpan.FromSeconds(10);
    }

    /// <summary>
    /// Model switch result
    /// </summary>
    public class ModelSwitchResult
    {
        public bool Success { get; set; }
        public string ModelName { get; set; }
        public string PreviousModel { get; set; }
        public string SwitchReason { get; set; }
        public DateTime SwitchTime { get; set; }
        public string ErrorMessage { get; set; }
    }

    /// <summary>
    /// Model information for display
    /// </summary>
    public class ModelInfo
    {
        public string ModelName { get; set; }
        public string DisplayName { get; set; }
        public string Version { get; set; }
        public string Description { get; set; }
        public bool IsEnabled { get; set; }
        public int MaxContextSize { get; set; }
        public string ParameterCount { get; set; }
        public bool SupportsCodeCompletion { get; set; }
        public List<ModelCapability> Capabilities { get; set; }
        public ModelPriority Priority { get; set; }
        public DateTime LastUsed { get; set; }
        public TimeSpan AverageResponseTime { get; set; }
        public double SuccessRate { get; set; }
        public double CurrentLoad { get; set; }
    }

    /// <summary>
    /// Multi-model performance report
    /// </summary>
    public class MultiModelPerformanceReport
    {
        public DateTime GeneratedAt { get; set; }
        public int TotalModelsRegistered { get; set; }
        public int ActiveModels { get; set; }
        public List<ModelPerformanceReport> ModelReports { get; set; }
    }

    /// <summary>
    /// Individual model performance report
    /// </summary>
    public class ModelPerformanceReport
    {
        public string ModelName { get; set; }
        public string DisplayName { get; set; }
        public bool IsEnabled { get; set; }
        public DateTime RegistrationTime { get; set; }
        public DateTime LastUsed { get; set; }
        public ModelStatistics Statistics { get; set; }
        public ModelPerformanceData Performance { get; set; }
        public ModelHealth Health { get; set; }
        public double CurrentLoad { get; set; }
        public List<ModelCapability> Capabilities { get; set; }
    }

    /// <summary>
    /// Model statistics
    /// </summary>
    public class ModelStatistics
    {
        public int TotalRequests { get; set; }
        public int SuccessfulRequests { get; set; }
        public int FailedRequests { get; set; }
        public int TotalSwitches { get; set; }
        public TimeSpan TotalResponseTime { get; set; }
        public DateTime FirstRequest { get; set; }
        public DateTime LastRequest { get; set; }
    }

    /// <summary>
    /// Model capabilities
    /// </summary>
    public enum ModelCapability
    {
        CodeGeneration,
        CodeCompletion,
        CodeAnalysis,
        Debugging,
        Documentation,
        Refactoring,
        GeneralPurpose
    }

    /// <summary>
    /// Model priority levels
    /// </summary>
    public enum ModelPriority
    {
        Low = 1,
        Medium = 2,
        High = 3
    }

    // Supporting service implementations (simplified)
    internal class ModelPerformanceMonitor : IDisposable
    {
        private readonly ConcurrentDictionary<string, ModelPerformanceData> _performanceData;

        public ModelPerformanceMonitor()
        {
            _performanceData = new ConcurrentDictionary<string, ModelPerformanceData>();
        }

        public void InitializeModel(string modelName)
        {
            _performanceData.TryAdd(modelName, new ModelPerformanceData { ModelName = modelName });
        }

        public void RecordRequest(string modelName, TimeSpan duration, bool success)
        {
            if (_performanceData.TryGetValue(modelName, out var data))
            {
                data.TotalRequests++;
                if (success)
                {
                    data.SuccessfulRequests++;
                    data.TotalResponseTime = data.TotalResponseTime.Add(duration);
                }
                else
                {
                    data.FailedRequests++;
                }
                data.LastRequest = DateTime.UtcNow;
            }
        }

        public ModelPerformanceData GetModelPerformance(string modelName)
        {
            return _performanceData.TryGetValue(modelName, out var data) ? data : new ModelPerformanceData();
        }

        public TimeSpan GetAverageResponseTime(string modelName)
        {
            var data = GetModelPerformance(modelName);
            return data.SuccessfulRequests > 0 
                ? TimeSpan.FromTicks(data.TotalResponseTime.Ticks / data.SuccessfulRequests)
                : TimeSpan.Zero;
        }

        public double GetSuccessRate(string modelName)
        {
            var data = GetModelPerformance(modelName);
            return data.TotalRequests > 0 ? (double)data.SuccessfulRequests / data.TotalRequests : 0;
        }

        public void UpdateMetrics()
        {
            // Update performance metrics
            foreach (var data in _performanceData.Values)
            {
                data.LastUpdate = DateTime.UtcNow;
            }
        }

        public void Dispose() => _performanceData.Clear();
    }

    internal class ModelRouter : IDisposable
    {
        private string _preferredModel = "codellama";
        
        public async Task<List<string>> SelectModelsAsync(CodeContext context, ModelSelectionHint hint)
        {
            await Task.Delay(1); // Placeholder
            
            var models = new List<string> { _preferredModel };
            
            // Add secondary models based on context and hint
            if (hint?.PreferredModels?.Any() == true)
            {
                models.AddRange(hint.PreferredModels.Where(m => m != _preferredModel));
            }
            else
            {
                // Default secondary models
                if (_preferredModel != "deepseek-coder") models.Add("deepseek-coder");
                if (_preferredModel != "mistral") models.Add("mistral");
            }

            return models.Distinct().ToList();
        }

        public async Task SetPreferredModelAsync(string modelName, string reason)
        {
            _preferredModel = modelName;
            await Task.Delay(1); // Placeholder
        }

        public string GetCurrentPreferredModel() => _preferredModel;

        public async Task UpdateRoutingConfigurationAsync()
        {
            await Task.Delay(1); // Placeholder
        }

        public void Dispose() { }
    }

    internal class ConfidenceScorer : IDisposable
    {
        public double CalculateConfidence(string modelName, CodeSuggestion suggestion, CodeContext context)
        {
            if (suggestion == null) return 0;

            var baseConfidence = suggestion.Confidence;
            Double modelBonus;

            // Adjust based on model-specific factors
            switch (modelName)
            {
                case "codellama":
                    modelBonus = 0.1;
                    break;
                case "deepseek-coder":
                    modelBonus= 0.15;
                    break;
                case "mistral":
                    modelBonus= 0.05;
                    break;
                default:
                    modelBonus = 0;
                    break;
            }
            ;

            return Math.Min(1.0, baseConfidence + modelBonus);
        }

        public void Dispose() { }
    }

    internal class ModelHealthChecker : IDisposable
    {
        private readonly ConcurrentDictionary<string, ModelHealth> _healthData;

        public ModelHealthChecker()
        {
            _healthData = new ConcurrentDictionary<string, ModelHealth>();
        }

        public async Task<ModelHealth> CheckModelHealthAsync(string modelName)
        {
            await Task.Delay(50); // Simulate health check
            
            var health = new ModelHealth
            {
                ModelName = modelName,
                IsHealthy = true, // Simulate healthy state
                LastCheck = DateTime.UtcNow,
                ResponseTime = TimeSpan.FromMilliseconds(100 + new Random().Next(200))
            };

            _healthData.AddOrUpdate(modelName, health, (k, v) => health);
            return health;
        }

        public ModelHealth GetModelHealth(string modelName)
        {
            return _healthData.TryGetValue(modelName, out var health) ? health : new ModelHealth { ModelName = modelName };
        }

        public void Dispose() => _healthData.Clear();
    }

    /// <summary>
    /// Model performance data
    /// </summary>
    public class ModelPerformanceData
    {
        public string ModelName { get; set; }
        public int TotalRequests { get; set; }
        public int SuccessfulRequests { get; set; }
        public int FailedRequests { get; set; }
        public TimeSpan TotalResponseTime { get; set; }
        public DateTime LastRequest { get; set; }
        public DateTime LastUpdate { get; set; }
        public ModelPriority? Priority { get; set; }
    }

    /// <summary>
    /// Model health information
    /// </summary>
    public class ModelHealth
    {
        public string ModelName { get; set; }
        public bool IsHealthy { get; set; }
        public string ErrorMessage { get; set; }
        public DateTime LastCheck { get; set; }
        public TimeSpan ResponseTime { get; set; }
    }
}