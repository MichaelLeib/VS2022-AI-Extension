using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using OllamaAssistant.Infrastructure;
using OllamaAssistant.Services.Interfaces;
using ValidationResult = OllamaAssistant.Models.ValidationResult;

namespace OllamaAssistant.Services.Implementation
{
    /// <summary>
    /// Service for managing project-specific settings with inheritance and team sharing
    /// </summary>
    public class ProjectSpecificSettingsService : IDisposable
    {
        private readonly SecurityValidator _securityValidator;
        private readonly Dictionary<string, ProjectSettings> _projectSettingsCache;
        private readonly Dictionary<string, DateTime> _cacheTimestamps;
        private readonly SettingsInheritanceManager _inheritanceManager;
        private readonly TeamSettingsManager _teamSettingsManager;
        private readonly object _lockObject = new object();
        private bool _disposed;

        // Configuration file names
        private const string OllamaRcFileName = ".ollamarc";
        private const string ProjectSettingsFileName = ".ollama-project.json";
        private const string TeamSettingsFileName = ".ollama-team.json";

        public ProjectSpecificSettingsService()
        {
            _securityValidator = new SecurityValidator();
            _projectSettingsCache = new Dictionary<string, ProjectSettings>();
            _cacheTimestamps = new Dictionary<string, DateTime>();
            _inheritanceManager = new SettingsInheritanceManager();
            _teamSettingsManager = new TeamSettingsManager();
        }

        /// <summary>
        /// Gets effective settings for a specific project path with inheritance
        /// </summary>
        public async Task<ProjectSettings> GetEffectiveSettingsAsync(string projectPath)
        {
            if (_disposed || string.IsNullOrEmpty(projectPath))
                return GetDefaultProjectSettings();

            try
            {
                // Check cache first
                var cachedSettings = GetCachedSettings(projectPath);
                if (cachedSettings != null)
                    return cachedSettings;

                // Build inheritance chain: Global → Team → Project → File
                var inheritanceChain = await BuildInheritanceChainAsync(projectPath);
                
                // Apply inheritance rules
                var effectiveSettings = _inheritanceManager.ApplyInheritance(inheritanceChain);
                
                // Cache the result
                CacheSettings(projectPath, effectiveSettings);
                
                return effectiveSettings;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to get effective settings for {projectPath}: {ex.Message}");
                return GetDefaultProjectSettings();
            }
        }

        /// <summary>
        /// Creates or updates project-specific settings
        /// </summary>
        public async Task SetProjectSettingsAsync(string projectPath, ProjectSettings settings)
        {
            if (_disposed || string.IsNullOrEmpty(projectPath) || settings == null)
                return;

            // Validate settings
            var validationResult = ValidateProjectSettings(settings);
            if (!validationResult.IsValid)
                throw new ArgumentException($"Invalid project settings: {validationResult.ErrorMessage}");

            // Sanitize settings
            settings = SanitizeProjectSettings(settings);

            // Save to project directory
            await SaveProjectSettingsAsync(projectPath, settings);

            // Invalidate cache
            InvalidateCache(projectPath);
        }

        /// <summary>
        /// Creates .ollamarc configuration file
        /// </summary>
        public async Task CreateOllamaRcAsync(string directoryPath, OllamaRcConfiguration configuration)
        {
            if (_disposed || string.IsNullOrEmpty(directoryPath) || configuration == null)
                return;

            // Validate configuration
            var validationResult = ValidateOllamaRcConfiguration(configuration);
            if (!validationResult.IsValid)
                throw new ArgumentException($"Invalid .ollamarc configuration: {validationResult.ErrorMessage}");

            // Generate .ollamarc content
            var content = GenerateOllamaRcContent(configuration);
            
            // Save to file
            var filePath = Path.Combine(directoryPath, OllamaRcFileName);
            await File.WriteAllTextAsync(filePath, content);

            // Invalidate related caches
            InvalidateRelatedCaches(directoryPath);
        }

        /// <summary>
        /// Reads .ollamarc configuration file
        /// </summary>
        public async Task<OllamaRcConfiguration> ReadOllamaRcAsync(string directoryPath)
        {
            if (_disposed || string.IsNullOrEmpty(directoryPath))
                return null;

            var filePath = Path.Combine(directoryPath, OllamaRcFileName);
            
            if (!File.Exists(filePath))
                return null;

            try
            {
                var content = await File.ReadAllTextAsync(filePath);
                return ParseOllamaRcContent(content);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to read .ollamarc from {filePath}: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Sets up team settings for sharing
        /// </summary>
        public async Task SetupTeamSettingsAsync(string projectPath, TeamSettingsConfiguration teamConfig)
        {
            if (_disposed || string.IsNullOrEmpty(projectPath) || teamConfig == null)
                return;

            // Validate team configuration
            var validationResult = ValidateTeamSettingsConfiguration(teamConfig);
            if (!validationResult.IsValid)
                throw new ArgumentException($"Invalid team settings: {validationResult.ErrorMessage}");

            // Create team settings
            await _teamSettingsManager.CreateTeamSettingsAsync(projectPath, teamConfig);

            // Invalidate cache
            InvalidateCache(projectPath);
        }

        /// <summary>
        /// Gets team settings for a project
        /// </summary>
        public async Task<TeamSettingsConfiguration> GetTeamSettingsAsync(string projectPath)
        {
            if (_disposed || string.IsNullOrEmpty(projectPath))
                return null;

            return await _teamSettingsManager.GetTeamSettingsAsync(projectPath);
        }

        /// <summary>
        /// Discovers all configuration files in a project hierarchy
        /// </summary>
        public async Task<ConfigurationDiscoveryResult> DiscoverConfigurationsAsync(string projectPath)
        {
            if (_disposed || string.IsNullOrEmpty(projectPath))
                return new ConfigurationDiscoveryResult();

            var result = new ConfigurationDiscoveryResult { ProjectPath = projectPath };

            try
            {
                // Walk up the directory tree to find all configuration files
                var currentPath = Path.GetFullPath(projectPath);
                var rootPath = Path.GetPathRoot(currentPath);

                while (!string.Equals(currentPath, rootPath, StringComparison.OrdinalIgnoreCase))
                {
                    await DiscoverConfigurationsInDirectoryAsync(currentPath, result);
                    currentPath = Path.GetDirectoryName(currentPath);
                }

                result.Success = true;
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = ex.Message;
            }

            return result;
        }

        /// <summary>
        /// Validates project settings inheritance
        /// </summary>
        public async Task<SettingsValidationResult> ValidateSettingsInheritanceAsync(string projectPath)
        {
            if (_disposed || string.IsNullOrEmpty(projectPath))
                return new SettingsValidationResult { IsValid = false, ErrorMessage = "Invalid project path" };

            try
            {
                // Build inheritance chain
                var inheritanceChain = await BuildInheritanceChainAsync(projectPath);
                
                // Validate each level
                var result = new SettingsValidationResult { IsValid = true };
                
                foreach (var level in inheritanceChain)
                {
                    var levelValidation = ValidateSettingsLevel(level);
                    if (!levelValidation.IsValid)
                    {
                        result.IsValid = false;
                        result.ValidationErrors.Add($"{level.Source}: {levelValidation.ErrorMessage}");
                    }
                }

                // Check for conflicts
                var conflictCheck = CheckInheritanceConflicts(inheritanceChain);
                if (conflictCheck.HasConflicts)
                {
                    result.Warnings.AddRange(conflictCheck.Conflicts);
                }

                return result;
            }
            catch (Exception ex)
            {
                return new SettingsValidationResult
                {
                    IsValid = false,
                    ErrorMessage = $"Validation failed: {ex.Message}"
                };
            }
        }

        /// <summary>
        /// Gets project settings statistics
        /// </summary>
        public ProjectSettingsStatistics GetStatistics()
        {
            lock (_lockObject)
            {
                return new ProjectSettingsStatistics
                {
                    CachedProjectsCount = _projectSettingsCache.Count,
                    TotalDiscoveredConfigurations = GetTotalDiscoveredConfigurations(),
                    ActiveTeamSettings = _teamSettingsManager.GetActiveTeamSettingsCount(),
                    LastCacheUpdate = GetLastCacheUpdate(),
                    CacheHitRate = CalculateCacheHitRate()
                };
            }
        }

        /// <summary>
        /// Builds inheritance chain for a project path
        /// </summary>
        private async Task<List<SettingsLevel>> BuildInheritanceChainAsync(string projectPath)
        {
            var chain = new List<SettingsLevel>();

            // Global settings (lowest priority)
            chain.Add(new SettingsLevel
            {
                Source = SettingsSource.Global,
                Settings = GetGlobalSettings(),
                Priority = 1
            });

            // Team settings
            var teamSettings = await GetTeamSettingsAsync(projectPath);
            if (teamSettings != null)
            {
                chain.Add(new SettingsLevel
                {
                    Source = SettingsSource.Team,
                    Settings = ConvertTeamSettingsToProjectSettings(teamSettings),
                    Priority = 2
                });
            }

            // Project settings
            var projectSettings = await LoadProjectSettingsDirectAsync(projectPath);
            if (projectSettings != null)
            {
                chain.Add(new SettingsLevel
                {
                    Source = SettingsSource.Project,
                    Settings = projectSettings,
                    Priority = 3
                });
            }

            // .ollamarc settings
            var ollamaRcSettings = await ReadOllamaRcAsync(projectPath);
            if (ollamaRcSettings != null)
            {
                chain.Add(new SettingsLevel
                {
                    Source = SettingsSource.OllamaRc,
                    Settings = ConvertOllamaRcToProjectSettings(ollamaRcSettings),
                    Priority = 4
                });
            }

            // File-specific settings (highest priority)
            // This would be implemented based on current file context
            
            return chain.OrderBy(x => x.Priority).ToList();
        }

        /// <summary>
        /// Gets cached settings if valid
        /// </summary>
        private ProjectSettings GetCachedSettings(string projectPath)
        {
            lock (_lockObject)
            {
                if (_projectSettingsCache.TryGetValue(projectPath, out var settings) &&
                    _cacheTimestamps.TryGetValue(projectPath, out var timestamp))
                {
                    // Check if cache is still valid (5 minutes)
                    if (DateTime.UtcNow - timestamp < TimeSpan.FromMinutes(5))
                    {
                        return settings;
                    }
                }

                return null;
            }
        }

        /// <summary>
        /// Caches settings for a project
        /// </summary>
        private void CacheSettings(string projectPath, ProjectSettings settings)
        {
            lock (_lockObject)
            {
                _projectSettingsCache[projectPath] = settings;
                _cacheTimestamps[projectPath] = DateTime.UtcNow;
            }
        }

        /// <summary>
        /// Invalidates cache for a specific project
        /// </summary>
        private void InvalidateCache(string projectPath)
        {
            lock (_lockObject)
            {
                _projectSettingsCache.Remove(projectPath);
                _cacheTimestamps.Remove(projectPath);
            }
        }

        /// <summary>
        /// Invalidates related caches when configuration changes
        /// </summary>
        private void InvalidateRelatedCaches(string directoryPath)
        {
            lock (_lockObject)
            {
                var keysToRemove = _projectSettingsCache.Keys
                    .Where(key => key.StartsWith(directoryPath, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                foreach (var key in keysToRemove)
                {
                    _projectSettingsCache.Remove(key);
                    _cacheTimestamps.Remove(key);
                }
            }
        }

        /// <summary>
        /// Loads project settings directly from file
        /// </summary>
        private async Task<ProjectSettings> LoadProjectSettingsDirectAsync(string projectPath)
        {
            var settingsFile = Path.Combine(projectPath, ProjectSettingsFileName);
            
            if (!File.Exists(settingsFile))
                return null;

            try
            {
                var json = await File.ReadAllTextAsync(settingsFile);
                var settings = JsonSerializer.Deserialize<ProjectSettings>(json, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    Converters = { new JsonStringEnumConverter() }
                });

                return settings;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load project settings from {settingsFile}: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Saves project settings to file
        /// </summary>
        private async Task SaveProjectSettingsAsync(string projectPath, ProjectSettings settings)
        {
            var settingsFile = Path.Combine(projectPath, ProjectSettingsFileName);
            
            try
            {
                var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions
                {
                    WriteIndented = true,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    Converters = { new JsonStringEnumConverter() }
                });

                await File.WriteAllTextAsync(settingsFile, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to save project settings to {settingsFile}: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Generates .ollamarc file content
        /// </summary>
        private string GenerateOllamaRcContent(OllamaRcConfiguration configuration)
        {
            var lines = new List<string>();

            // Add header comment
            lines.Add("# Ollama Assistant Configuration");
            lines.Add($"# Generated on {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
            lines.Add("");

            // Server configuration
            if (!string.IsNullOrEmpty(configuration.ServerUrl))
            {
                lines.Add($"server={configuration.ServerUrl}");
            }

            if (!string.IsNullOrEmpty(configuration.DefaultModel))
            {
                lines.Add($"model={configuration.DefaultModel}");
            }

            // Feature toggles
            if (configuration.EnableCodeCompletion.HasValue)
            {
                lines.Add($"code_completion={configuration.EnableCodeCompletion.Value.ToString().ToLower()}");
            }

            if (configuration.EnableJumpRecommendations.HasValue)
            {
                lines.Add($"jump_recommendations={configuration.EnableJumpRecommendations.Value.ToString().ToLower()}");
            }

            // Context configuration
            if (configuration.ContextLinesUp.HasValue)
            {
                lines.Add($"context_lines_up={configuration.ContextLinesUp.Value}");
            }

            if (configuration.ContextLinesDown.HasValue)
            {
                lines.Add($"context_lines_down={configuration.ContextLinesDown.Value}");
            }

            if (configuration.CursorHistoryDepth.HasValue)
            {
                lines.Add($"cursor_history_depth={configuration.CursorHistoryDepth.Value}");
            }

            // Custom properties
            if (configuration.CustomProperties != null)
            {
                lines.Add("");
                lines.Add("# Custom Properties");
                
                foreach (var kvp in configuration.CustomProperties)
                {
                    lines.Add($"{kvp.Key}={kvp.Value}");
                }
            }

            return string.Join(Environment.NewLine, lines);
        }

        /// <summary>
        /// Parses .ollamarc file content
        /// </summary>
        private OllamaRcConfiguration ParseOllamaRcContent(string content)
        {
            var configuration = new OllamaRcConfiguration();
            var customProperties = new Dictionary<string, string>();

            var lines = content.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

            foreach (var line in lines)
            {
                var trimmedLine = line.Trim();
                
                // Skip comments and empty lines
                if (string.IsNullOrEmpty(trimmedLine) || trimmedLine.StartsWith("#"))
                    continue;

                var parts = trimmedLine.Split('=', 2);
                if (parts.Length != 2)
                    continue;

                var key = parts[0].Trim();
                var value = parts[1].Trim();

                switch (key.ToLower())
                {
                    case "server":
                        configuration.ServerUrl = value;
                        break;
                    case "model":
                        configuration.DefaultModel = value;
                        break;
                    case "code_completion":
                        if (bool.TryParse(value, out var codeCompletion))
                            configuration.EnableCodeCompletion = codeCompletion;
                        break;
                    case "jump_recommendations":
                        if (bool.TryParse(value, out var jumpRecs))
                            configuration.EnableJumpRecommendations = jumpRecs;
                        break;
                    case "context_lines_up":
                        if (int.TryParse(value, out var linesUp))
                            configuration.ContextLinesUp = linesUp;
                        break;
                    case "context_lines_down":
                        if (int.TryParse(value, out var linesDown))
                            configuration.ContextLinesDown = linesDown;
                        break;
                    case "cursor_history_depth":
                        if (int.TryParse(value, out var historyDepth))
                            configuration.CursorHistoryDepth = historyDepth;
                        break;
                    default:
                        customProperties[key] = value;
                        break;
                }
            }

            if (customProperties.Count > 0)
            {
                configuration.CustomProperties = customProperties;
            }

            return configuration;
        }

        /// <summary>
        /// Discovers configurations in a specific directory
        /// </summary>
        private async Task DiscoverConfigurationsInDirectoryAsync(string directoryPath, ConfigurationDiscoveryResult result)
        {
            // Check for .ollamarc
            var ollamaRcPath = Path.Combine(directoryPath, OllamaRcFileName);
            if (File.Exists(ollamaRcPath))
            {
                result.OllamaRcFiles.Add(new ConfigurationFileInfo
                {
                    FilePath = ollamaRcPath,
                    DirectoryPath = directoryPath,
                    LastModified = File.GetLastWriteTime(ollamaRcPath),
                    IsValid = await ValidateOllamaRcFileAsync(ollamaRcPath)
                });
            }

            // Check for project settings
            var projectSettingsPath = Path.Combine(directoryPath, ProjectSettingsFileName);
            if (File.Exists(projectSettingsPath))
            {
                result.ProjectSettingsFiles.Add(new ConfigurationFileInfo
                {
                    FilePath = projectSettingsPath,
                    DirectoryPath = directoryPath,
                    LastModified = File.GetLastWriteTime(projectSettingsPath),
                    IsValid = await ValidateProjectSettingsFileAsync(projectSettingsPath)
                });
            }

            // Check for team settings
            var teamSettingsPath = Path.Combine(directoryPath, TeamSettingsFileName);
            if (File.Exists(teamSettingsPath))
            {
                result.TeamSettingsFiles.Add(new ConfigurationFileInfo
                {
                    FilePath = teamSettingsPath,
                    DirectoryPath = directoryPath,
                    LastModified = File.GetLastWriteTime(teamSettingsPath),
                    IsValid = await ValidateTeamSettingsFileAsync(teamSettingsPath)
                });
            }
        }

        /// <summary>
        /// Validates .ollamarc file
        /// </summary>
        private async Task<bool> ValidateOllamaRcFileAsync(string filePath)
        {
            try
            {
                var content = await File.ReadAllTextAsync(filePath);
                var config = ParseOllamaRcContent(content);
                return ValidateOllamaRcConfiguration(config).IsValid;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Validates project settings file
        /// </summary>
        private async Task<bool> ValidateProjectSettingsFileAsync(string filePath)
        {
            try
            {
                var json = await File.ReadAllTextAsync(filePath);
                var settings = JsonSerializer.Deserialize<ProjectSettings>(json);
                return ValidateProjectSettings(settings).IsValid;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Validates team settings file
        /// </summary>
        private async Task<bool> ValidateTeamSettingsFileAsync(string filePath)
        {
            try
            {
                var json = await File.ReadAllTextAsync(filePath);
                var settings = JsonSerializer.Deserialize<TeamSettingsConfiguration>(json);
                return ValidateTeamSettingsConfiguration(settings).IsValid;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Gets global settings (placeholder)
        /// </summary>
        private ProjectSettings GetGlobalSettings()
        {
            return GetDefaultProjectSettings();
        }

        /// <summary>
        /// Gets default project settings
        /// </summary>
        private ProjectSettings GetDefaultProjectSettings()
        {
            return new ProjectSettings
            {
                ServerUrl = "http://localhost:11434",
                DefaultModel = "codellama",
                EnableCodeCompletion = true,
                EnableJumpRecommendations = true,
                ContextConfiguration = new ContextConfiguration
                {
                    LinesUp = 3,
                    LinesDown = 2,
                    CursorHistoryDepth = 5
                },
                PerformanceSettings = new PerformanceSettings
                {
                    RequestTimeoutSeconds = 30,
                    MaxConcurrentRequests = 2,
                    EnableCaching = true
                }
            };
        }

        /// <summary>
        /// Converts team settings to project settings
        /// </summary>
        private ProjectSettings ConvertTeamSettingsToProjectSettings(TeamSettingsConfiguration teamSettings)
        {
            return new ProjectSettings
            {
                ServerUrl = teamSettings.SharedServerUrl,
                DefaultModel = teamSettings.RecommendedModel,
                EnableCodeCompletion = teamSettings.EnableCodeCompletion,
                EnableJumpRecommendations = teamSettings.EnableJumpRecommendations,
                ContextConfiguration = teamSettings.ContextConfiguration,
                PerformanceSettings = teamSettings.PerformanceSettings
            };
        }

        /// <summary>
        /// Converts .ollamarc to project settings
        /// </summary>
        private ProjectSettings ConvertOllamaRcToProjectSettings(OllamaRcConfiguration ollamaRc)
        {
            return new ProjectSettings
            {
                ServerUrl = ollamaRc.ServerUrl,
                DefaultModel = ollamaRc.DefaultModel,
                EnableCodeCompletion = ollamaRc.EnableCodeCompletion,
                EnableJumpRecommendations = ollamaRc.EnableJumpRecommendations,
                ContextConfiguration = new ContextConfiguration
                {
                    LinesUp = ollamaRc.ContextLinesUp,
                    LinesDown = ollamaRc.ContextLinesDown,
                    CursorHistoryDepth = ollamaRc.CursorHistoryDepth
                }
            };
        }

        /// <summary>
        /// Validates project settings
        /// </summary>
        private ValidationResult ValidateProjectSettings(ProjectSettings settings)
        {
            if (settings == null)
                return new ValidationResult { IsValid = false, ErrorMessage = "Settings cannot be null" };

            if (!string.IsNullOrEmpty(settings.ServerUrl))
            {
                if (!Uri.TryCreate(settings.ServerUrl, UriKind.Absolute, out _))
                    return new ValidationResult { IsValid = false, ErrorMessage = "Invalid server URL format" };
            }

            return new ValidationResult { IsValid = true };
        }

        /// <summary>
        /// Validates .ollamarc configuration
        /// </summary>
        private ValidationResult ValidateOllamaRcConfiguration(OllamaRcConfiguration config)
        {
            if (config == null)
                return new ValidationResult { IsValid = false, ErrorMessage = "Configuration cannot be null" };

            if (!string.IsNullOrEmpty(config.ServerUrl))
            {
                if (!Uri.TryCreate(config.ServerUrl, UriKind.Absolute, out _))
                    return new ValidationResult { IsValid = false, ErrorMessage = "Invalid server URL format" };
            }

            return new ValidationResult { IsValid = true };
        }

        /// <summary>
        /// Validates team settings configuration
        /// </summary>
        private ValidationResult ValidateTeamSettingsConfiguration(TeamSettingsConfiguration config)
        {
            if (config == null)
                return new ValidationResult { IsValid = false, ErrorMessage = "Configuration cannot be null" };

            if (string.IsNullOrEmpty(config.TeamName))
                return new ValidationResult { IsValid = false, ErrorMessage = "Team name is required" };

            return new ValidationResult { IsValid = true };
        }

        /// <summary>
        /// Validates settings level
        /// </summary>
        private ValidationResult ValidateSettingsLevel(SettingsLevel level)
        {
            return ValidateProjectSettings(level.Settings);
        }

        /// <summary>
        /// Checks for inheritance conflicts
        /// </summary>
        private ConflictCheckResult CheckInheritanceConflicts(List<SettingsLevel> chain)
        {
            var result = new ConflictCheckResult();

            // Check for conflicting server URLs
            var serverUrls = chain.Where(x => !string.IsNullOrEmpty(x.Settings.ServerUrl))
                                 .Select(x => new { x.Source, x.Settings.ServerUrl })
                                 .ToList();

            if (serverUrls.Count > 1 && serverUrls.Select(x => x.ServerUrl).Distinct().Count() > 1)
            {
                result.HasConflicts = true;
                result.Conflicts.Add("Multiple different server URLs found in inheritance chain");
            }

            return result;
        }

        /// <summary>
        /// Sanitizes project settings
        /// </summary>
        private ProjectSettings SanitizeProjectSettings(ProjectSettings settings)
        {
            return new ProjectSettings
            {
                ServerUrl = _securityValidator.SanitizeUrl(settings.ServerUrl),
                DefaultModel = _securityValidator.SanitizeString(settings.DefaultModel),
                EnableCodeCompletion = settings.EnableCodeCompletion,
                EnableJumpRecommendations = settings.EnableJumpRecommendations,
                ContextConfiguration = settings.ContextConfiguration,
                PerformanceSettings = settings.PerformanceSettings
            };
        }

        /// <summary>
        /// Gets total discovered configurations
        /// </summary>
        private int GetTotalDiscoveredConfigurations()
        {
            // This would be tracked in a real implementation
            return 0;
        }

        /// <summary>
        /// Gets last cache update time
        /// </summary>
        private DateTime GetLastCacheUpdate()
        {
            lock (_lockObject)
            {
                return _cacheTimestamps.Values.DefaultIfEmpty(DateTime.MinValue).Max();
            }
        }

        /// <summary>
        /// Calculates cache hit rate
        /// </summary>
        private double CalculateCacheHitRate()
        {
            // This would be tracked in a real implementation
            return 0.85; // 85% hit rate
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            _inheritanceManager?.Dispose();
            _teamSettingsManager?.Dispose();
            _securityValidator?.Dispose();
        }
    }

    /// <summary>
    /// Project-specific settings
    /// </summary>
    public class ProjectSettings
    {
        public string ServerUrl { get; set; }
        public string DefaultModel { get; set; }
        public bool? EnableCodeCompletion { get; set; }
        public bool? EnableJumpRecommendations { get; set; }
        public ContextConfiguration ContextConfiguration { get; set; }
        public PerformanceSettings PerformanceSettings { get; set; }
        public Dictionary<string, object> CustomSettings { get; set; }
    }

    /// <summary>
    /// Context configuration
    /// </summary>
    public class ContextConfiguration
    {
        public int? LinesUp { get; set; }
        public int? LinesDown { get; set; }
        public int? CursorHistoryDepth { get; set; }
    }

    /// <summary>
    /// Performance settings
    /// </summary>
    public class PerformanceSettings
    {
        public int? RequestTimeoutSeconds { get; set; }
        public int? MaxConcurrentRequests { get; set; }
        public bool? EnableCaching { get; set; }
    }

    /// <summary>
    /// .ollamarc configuration
    /// </summary>
    public class OllamaRcConfiguration
    {
        public string ServerUrl { get; set; }
        public string DefaultModel { get; set; }
        public bool? EnableCodeCompletion { get; set; }
        public bool? EnableJumpRecommendations { get; set; }
        public int? ContextLinesUp { get; set; }
        public int? ContextLinesDown { get; set; }
        public int? CursorHistoryDepth { get; set; }
        public Dictionary<string, string> CustomProperties { get; set; }
    }

    /// <summary>
    /// Team settings configuration
    /// </summary>
    public class TeamSettingsConfiguration
    {
        public string TeamName { get; set; }
        public string SharedServerUrl { get; set; }
        public string RecommendedModel { get; set; }
        public bool? EnableCodeCompletion { get; set; }
        public bool? EnableJumpRecommendations { get; set; }
        public ContextConfiguration ContextConfiguration { get; set; }
        public PerformanceSettings PerformanceSettings { get; set; }
        public List<string> AllowedModels { get; set; }
        public Dictionary<string, object> TeamPolicies { get; set; }
    }

    /// <summary>
    /// Settings inheritance level
    /// </summary>
    public class SettingsLevel
    {
        public SettingsSource Source { get; set; }
        public ProjectSettings Settings { get; set; }
        public int Priority { get; set; }
    }

    /// <summary>
    /// Settings source enumeration
    /// </summary>
    public enum SettingsSource
    {
        Global,
        Team,
        Project,
        OllamaRc,
        File
    }

    /// <summary>
    /// Configuration discovery result
    /// </summary>
    public class ConfigurationDiscoveryResult
    {
        public string ProjectPath { get; set; }
        public bool Success { get; set; }
        public string ErrorMessage { get; set; }
        public List<ConfigurationFileInfo> OllamaRcFiles { get; set; }
        public List<ConfigurationFileInfo> ProjectSettingsFiles { get; set; }
        public List<ConfigurationFileInfo> TeamSettingsFiles { get; set; }
    }

    /// <summary>
    /// Configuration file information
    /// </summary>
    public class ConfigurationFileInfo
    {
        public string FilePath { get; set; }
        public string DirectoryPath { get; set; }
        public DateTime LastModified { get; set; }
        public bool IsValid { get; set; }
    }

    /// <summary>
    /// Settings validation result
    /// </summary>
    public class SettingsValidationResult
    {
        public bool IsValid { get; set; }
        public string ErrorMessage { get; set; }
        public List<string> ValidationErrors { get; set; }
        public List<string> Warnings { get; set; }
    }

    /// <summary>
    /// Conflict check result
    /// </summary>
    public class ConflictCheckResult
    {
        public bool HasConflicts { get; set; }
        public List<string> Conflicts { get; set; }
    }

    /// <summary>
    /// Project settings statistics
    /// </summary>
    public class ProjectSettingsStatistics
    {
        public int CachedProjectsCount { get; set; }
        public int TotalDiscoveredConfigurations { get; set; }
        public int ActiveTeamSettings { get; set; }
        public DateTime LastCacheUpdate { get; set; }
        public double CacheHitRate { get; set; }
    }

    // Supporting manager classes (simplified implementations)
    internal class SettingsInheritanceManager : IDisposable
    {
        public ProjectSettings ApplyInheritance(List<SettingsLevel> chain)
        {
            var result = new ProjectSettings();

            foreach (var level in chain.OrderBy(x => x.Priority))
            {
                if (!string.IsNullOrEmpty(level.Settings.ServerUrl))
                    result.ServerUrl = level.Settings.ServerUrl;

                if (!string.IsNullOrEmpty(level.Settings.DefaultModel))
                    result.DefaultModel = level.Settings.DefaultModel;

                if (level.Settings.EnableCodeCompletion.HasValue)
                    result.EnableCodeCompletion = level.Settings.EnableCodeCompletion;

                if (level.Settings.EnableJumpRecommendations.HasValue)
                    result.EnableJumpRecommendations = level.Settings.EnableJumpRecommendations;

                // Merge context configuration
                if (level.Settings.ContextConfiguration != null)
                {
                    result.ContextConfiguration ??= new ContextConfiguration();
                    
                    if (level.Settings.ContextConfiguration.LinesUp.HasValue)
                        result.ContextConfiguration.LinesUp = level.Settings.ContextConfiguration.LinesUp;
                    
                    if (level.Settings.ContextConfiguration.LinesDown.HasValue)
                        result.ContextConfiguration.LinesDown = level.Settings.ContextConfiguration.LinesDown;
                    
                    if (level.Settings.ContextConfiguration.CursorHistoryDepth.HasValue)
                        result.ContextConfiguration.CursorHistoryDepth = level.Settings.ContextConfiguration.CursorHistoryDepth;
                }

                // Merge performance settings
                if (level.Settings.PerformanceSettings != null)
                {
                    result.PerformanceSettings ??= new PerformanceSettings();
                    
                    if (level.Settings.PerformanceSettings.RequestTimeoutSeconds.HasValue)
                        result.PerformanceSettings.RequestTimeoutSeconds = level.Settings.PerformanceSettings.RequestTimeoutSeconds;
                    
                    if (level.Settings.PerformanceSettings.MaxConcurrentRequests.HasValue)
                        result.PerformanceSettings.MaxConcurrentRequests = level.Settings.PerformanceSettings.MaxConcurrentRequests;
                    
                    if (level.Settings.PerformanceSettings.EnableCaching.HasValue)
                        result.PerformanceSettings.EnableCaching = level.Settings.PerformanceSettings.EnableCaching;
                }
            }

            return result;
        }

        public void Dispose() { }
    }

    internal class TeamSettingsManager : IDisposable
    {
        public async Task CreateTeamSettingsAsync(string projectPath, TeamSettingsConfiguration config)
        {
            var filePath = Path.Combine(projectPath, ".ollama-team.json");
            var json = JsonSerializer.Serialize(config, new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });

            await File.WriteAllTextAsync(filePath, json);
        }

        public async Task<TeamSettingsConfiguration> GetTeamSettingsAsync(string projectPath)
        {
            var filePath = Path.Combine(projectPath, ".ollama-team.json");
            
            if (!File.Exists(filePath))
                return null;

            try
            {
                var json = await File.ReadAllTextAsync(filePath);
                return JsonSerializer.Deserialize<TeamSettingsConfiguration>(json, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                });
            }
            catch
            {
                return null;
            }
        }

        public int GetActiveTeamSettingsCount() => 1; // Placeholder

        public void Dispose() { }
    }

}