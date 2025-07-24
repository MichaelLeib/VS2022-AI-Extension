using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using OllamaAssistant.Models;
using OllamaAssistant.Services.Interfaces;

namespace OllamaAssistant.Services.Implementation
{
    /// <summary>
    /// Service for learning from user interactions and adapting to coding patterns
    /// </summary>
    public class LearningAdaptationService : IDisposable
    {
        private readonly UserInteractionTracker _interactionTracker;
        private readonly CodingStyleAnalyzer _styleAnalyzer;
        private readonly PatternLearner _patternLearner;
        private readonly PersonalizationEngine _personalizationEngine;
        private readonly UsageAnalyticsCollector _analyticsCollector;
        private readonly string _learningDataDirectory;
        private readonly Timer _learningUpdateTimer;
        private bool _disposed;

        public LearningAdaptationService()
        {
            _interactionTracker = new UserInteractionTracker();
            _styleAnalyzer = new CodingStyleAnalyzer();
            _patternLearner = new PatternLearner();
            _personalizationEngine = new PersonalizationEngine();
            _analyticsCollector = new UsageAnalyticsCollector();
            
            _learningDataDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), 
                "OllamaAssistant", "Learning");
            
            Directory.CreateDirectory(_learningDataDirectory);

            // Update learning models every hour
            _learningUpdateTimer = new Timer(UpdateLearningModels, null, 
                TimeSpan.FromHours(1), TimeSpan.FromHours(1));

            // Initialize with existing data
            _ = Task.Run(LoadExistingLearningDataAsync);
        }

        /// <summary>
        /// Records user acceptance or rejection of a suggestion
        /// </summary>
        public async Task RecordSuggestionFeedbackAsync(SuggestionFeedback feedback)
        {
            if (_disposed || feedback == null)
                return;

            try
            {
                // Track the interaction
                await _interactionTracker.RecordInteractionAsync(feedback);

                // Update learning patterns
                await _patternLearner.UpdatePatternsAsync(feedback);

                // Update personalization
                await _personalizationEngine.UpdatePersonalizationAsync(feedback);

                // Collect analytics
                _analyticsCollector.RecordFeedback(feedback);

                // Save learning data
                await SaveLearningDataAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to record suggestion feedback: {ex.Message}");
            }
        }

        /// <summary>
        /// Analyzes and records coding style patterns
        /// </summary>
        public async Task AnalyzeCodingStyleAsync(string filePath, string content)
        {
            if (_disposed || string.IsNullOrEmpty(filePath) || string.IsNullOrEmpty(content))
                return;

            try
            {
                var styleAnalysis = await _styleAnalyzer.AnalyzeStyleAsync(filePath, content);
                await _personalizationEngine.UpdateCodingStyleAsync(styleAnalysis);
                _analyticsCollector.RecordStyleAnalysis(styleAnalysis);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to analyze coding style: {ex.Message}");
            }
        }

        /// <summary>
        /// Gets personalized suggestions based on learned patterns
        /// </summary>
        public async Task<PersonalizedSuggestions> GetPersonalizedSuggestionsAsync(CodeContext context)
        {
            if (_disposed || context == null)
                return new PersonalizedSuggestions();

            try
            {
                // Get user preferences
                var preferences = await _personalizationEngine.GetUserPreferencesAsync();

                // Get learned patterns relevant to this context
                var learnedPatterns = await _patternLearner.GetRelevantPatternsAsync(context);

                // Generate personalized suggestions
                var suggestions = await _personalizationEngine.GeneratePersonalizedSuggestionsAsync(
                    context, preferences, learnedPatterns);

                // Record analytics
                _analyticsCollector.RecordSuggestionRequest(context, suggestions);

                return suggestions;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to get personalized suggestions: {ex.Message}");
                return new PersonalizedSuggestions();
            }
        }

        /// <summary>
        /// Gets user interaction insights and analytics
        /// </summary>
        public async Task<UsageInsights> GetUsageInsightsAsync()
        {
            if (_disposed)
                return new UsageInsights();

            try
            {
                var interactions = await _interactionTracker.GetInteractionSummaryAsync();
                var styleProfile = await _styleAnalyzer.GetStyleProfileAsync();
                var learnedPatterns = await _patternLearner.GetPatternSummaryAsync();
                var analytics = _analyticsCollector.GetAnalyticsSummary();

                return new UsageInsights
                {
                    InteractionSummary = interactions,
                    CodingStyleProfile = styleProfile,
                    LearnedPatterns = learnedPatterns,
                    Analytics = analytics,
                    GeneratedAt = DateTime.UtcNow
                };
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to get usage insights: {ex.Message}");
                return new UsageInsights();
            }
        }

        /// <summary>
        /// Gets adaptation statistics
        /// </summary>
        public LearningStatistics GetLearningStatistics()
        {
            try
            {
                return new LearningStatistics
                {
                    TotalInteractions = _interactionTracker.TotalInteractions,
                    AcceptanceRate = _interactionTracker.AcceptanceRate,
                    LearnedPatterns = _patternLearner.PatternCount,
                    CodingStyleElements = _styleAnalyzer.StyleElementCount,
                    PersonalizationScore = _personalizationEngine.PersonalizationScore,
                    AnalyticsDataPoints = _analyticsCollector.DataPointCount,
                    LastLearningUpdate = _patternLearner.LastUpdateTime,
                    LearningDataSize = GetLearningDataSize()
                };
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to get learning statistics: {ex.Message}");
                return new LearningStatistics();
            }
        }

        /// <summary>
        /// Resets learning data (for privacy or testing purposes)
        /// </summary>
        public async Task ResetLearningDataAsync()
        {
            if (_disposed)
                return;

            try
            {
                // Clear in-memory data
                _interactionTracker.Clear();
                _styleAnalyzer.Clear();
                _patternLearner.Clear();
                _personalizationEngine.Clear();
                _analyticsCollector.Clear();

                // Clear persisted data
                if (Directory.Exists(_learningDataDirectory))
                {
                    var files = Directory.GetFiles(_learningDataDirectory, "*.json");
                    foreach (var file in files)
                    {
                        try
                        {
                            File.Delete(file);
                        }
                        catch
                        {
                            // Continue deleting other files
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to reset learning data: {ex.Message}");
            }
        }

        /// <summary>
        /// Exports learning data for backup or analysis
        /// </summary>
        public async Task<string> ExportLearningDataAsync(string exportPath)
        {
            if (_disposed || string.IsNullOrEmpty(exportPath))
                return null;

            try
            {
                var exportData = new LearningDataExport
                {
                    ExportDate = DateTime.UtcNow,
                    Version = "1.0",
                    InteractionData = await _interactionTracker.ExportDataAsync(),
                    StyleData = await _styleAnalyzer.ExportDataAsync(),
                    PatternData = await _patternLearner.ExportDataAsync(),
                    PersonalizationData = await _personalizationEngine.ExportDataAsync(),
                    AnalyticsData = _analyticsCollector.ExportData()
                };

                var json = JsonSerializer.Serialize(exportData, new JsonSerializerOptions
                {
                    WriteIndented = true,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    Converters = { new JsonStringEnumConverter() }
                });

                await Task.Run(() => File.WriteAllText(exportPath, json));
                return exportPath;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to export learning data: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Imports learning data from backup
        /// </summary>
        public async Task<bool> ImportLearningDataAsync(string importPath)
        {
            if (_disposed || string.IsNullOrEmpty(importPath) || !File.Exists(importPath))
                return false;

            try
            {
                var json = await Task.Run(() => File.ReadAllText(importPath));
                var importData = JsonSerializer.Deserialize<LearningDataExport>(json, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    Converters = { new JsonStringEnumConverter() }
                });

                if (importData == null)
                    return false;

                // Import data into each component
                await _interactionTracker.ImportDataAsync(importData.InteractionData);
                await _styleAnalyzer.ImportDataAsync(importData.StyleData);
                await _patternLearner.ImportDataAsync(importData.PatternData);
                await _personalizationEngine.ImportDataAsync(importData.PersonalizationData);
                _analyticsCollector.ImportData(importData.AnalyticsData);

                // Save imported data
                await SaveLearningDataAsync();

                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to import learning data: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Loads existing learning data from disk
        /// </summary>
        private async Task LoadExistingLearningDataAsync()
        {
            try
            {
                // Load interaction data
                var interactionFile = Path.Combine(_learningDataDirectory, "interactions.json");
                if (File.Exists(interactionFile))
                {
                    var interactionJson = await Task.Run(() => File.ReadAllText(interactionFile));
                    var interactionData = JsonSerializer.Deserialize<Dictionary<string, object>>(interactionJson);
                    await _interactionTracker.ImportDataAsync(interactionData);
                }

                // Load style data
                var styleFile = Path.Combine(_learningDataDirectory, "style.json");
                if (File.Exists(styleFile))
                {
                    var styleJson = await Task.Run(() => File.ReadAllText(styleFile));
                    var styleData = JsonSerializer.Deserialize<Dictionary<string, object>>(styleJson);
                    await _styleAnalyzer.ImportDataAsync(styleData);
                }

                // Load pattern data
                var patternFile = Path.Combine(_learningDataDirectory, "patterns.json");
                if (File.Exists(patternFile))
                {
                    var patternJson = await Task.Run(() => File.ReadAllText(patternFile));
                    var patternData = JsonSerializer.Deserialize<Dictionary<string, object>>(patternJson);
                    await _patternLearner.ImportDataAsync(patternData);
                }

                // Load personalization data
                var personalizationFile = Path.Combine(_learningDataDirectory, "personalization.json");
                if (File.Exists(personalizationFile))
                {
                    var personalizationJson = await Task.Run(() => File.ReadAllText(personalizationFile));
                    var personalizationData = JsonSerializer.Deserialize<Dictionary<string, object>>(personalizationJson);
                    await _personalizationEngine.ImportDataAsync(personalizationData);
                }

                // Load analytics data
                var analyticsFile = Path.Combine(_learningDataDirectory, "analytics.json");
                if (File.Exists(analyticsFile))
                {
                    var analyticsJson = await Task.Run(() => File.ReadAllText(analyticsFile));
                    var analyticsData = JsonSerializer.Deserialize<Dictionary<string, object>>(analyticsJson);
                    _analyticsCollector.ImportData(analyticsData);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load existing learning data: {ex.Message}");
            }
        }

        /// <summary>
        /// Saves learning data to disk
        /// </summary>
        private async Task SaveLearningDataAsync()
        {
            try
            {
                // Save interaction data
                var interactionData = await _interactionTracker.ExportDataAsync();
                var interactionJson = JsonSerializer.Serialize(interactionData, new JsonSerializerOptions
                {
                    WriteIndented = true,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                });
                await Task.Run(() => File.WriteAllText(Path.Combine(_learningDataDirectory, "interactions.json"), interactionJson));

                // Save style data
                var styleData = await _styleAnalyzer.ExportDataAsync();
                var styleJson = JsonSerializer.Serialize(styleData, new JsonSerializerOptions
                {
                    WriteIndented = true,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                });
                await Task.Run(() => File.WriteAllText(Path.Combine(_learningDataDirectory, "style.json"), styleJson));

                // Save pattern data
                var patternData = await _patternLearner.ExportDataAsync();
                var patternJson = JsonSerializer.Serialize(patternData, new JsonSerializerOptions
                {
                    WriteIndented = true,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                });
                await Task.Run(() => File.WriteAllText(Path.Combine(_learningDataDirectory, "patterns.json"), patternJson));

                // Save personalization data
                var personalizationData = await _personalizationEngine.ExportDataAsync();
                var personalizationJson = JsonSerializer.Serialize(personalizationData, new JsonSerializerOptions
                {
                    WriteIndented = true,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                });
                await Task.Run(() => File.WriteAllText(Path.Combine(_learningDataDirectory, "personalization.json"), personalizationJson));

                // Save analytics data
                var analyticsData = _analyticsCollector.ExportData();
                var analyticsJson = JsonSerializer.Serialize(analyticsData, new JsonSerializerOptions
                {
                    WriteIndented = true,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                });
                await Task.Run(() => File.WriteAllText(Path.Combine(_learningDataDirectory, "analytics.json"), analyticsJson));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to save learning data: {ex.Message}");
            }
        }

        /// <summary>
        /// Updates learning models periodically
        /// </summary>
        private void UpdateLearningModels(object state)
        {
            if (_disposed)
                return;

            try
            {
                _ = Task.Run(async () =>
                {
                    // Update pattern learning
                    await _patternLearner.UpdateModelsAsync();

                    // Update personalization
                    await _personalizationEngine.UpdateModelsAsync();

                    // Cleanup old data
                    await CleanupOldDataAsync();

                    // Save updated models
                    await SaveLearningDataAsync();
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Learning model update failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Cleans up old learning data to prevent unlimited growth
        /// </summary>
        private async Task CleanupOldDataAsync()
        {
            try
            {
                var cutoffDate = DateTime.UtcNow.AddDays(-30); // Keep 30 days of data
                
                await _interactionTracker.CleanupOldDataAsync(cutoffDate);
                await _styleAnalyzer.CleanupOldDataAsync(cutoffDate);
                await _patternLearner.CleanupOldDataAsync(cutoffDate);
                _analyticsCollector.CleanupOldData(cutoffDate);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Data cleanup failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Gets the size of learning data on disk
        /// </summary>
        private long GetLearningDataSize()
        {
            try
            {
                if (!Directory.Exists(_learningDataDirectory))
                    return 0;

                return Directory.GetFiles(_learningDataDirectory, "*.json")
                    .Sum(file => new FileInfo(file).Length);
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
            _learningUpdateTimer?.Dispose();
            _interactionTracker?.Dispose();
            _styleAnalyzer?.Dispose();
            _patternLearner?.Dispose();
            _personalizationEngine?.Dispose();
            _analyticsCollector?.Dispose();
        }
    }

    /// <summary>
    /// Tracks user interactions with suggestions
    /// </summary>
    internal class UserInteractionTracker : IDisposable
    {
        private readonly List<SuggestionFeedback> _interactions
        private readonly object _lock = new object();

        public int TotalInteractions => _interactions.Count;
        public double AcceptanceRate
        {
            get
            {
                lock (_lock)
                {
                    if (!_interactions.Any()) return 0;
                    return (double)_interactions.Count(i => i.WasAccepted) / _interactions.Count;
                }
            }
        }

        public async Task RecordInteractionAsync(SuggestionFeedback feedback)
        {
            await Task.Run(() =>
            {
                lock (_lock)
                {
                    _interactions.Add(feedback);
                    
                    // Limit to last 1000 interactions to prevent unlimited growth
                    while (_interactions.Count > 1000)
                    {
                        _interactions.RemoveAt(0);
                    }
                }
            });
        }

        public async Task<InteractionSummary> GetInteractionSummaryAsync()
        {
            return await Task.Run(() =>
            {
                lock (_lock)
                {
                    var summary = new InteractionSummary
                    {
                        TotalInteractions = _interactions.Count,
                        AcceptedSuggestions = _interactions.Count(i => i.WasAccepted),
                        RejectedSuggestions = _interactions.Count(i => !i.WasAccepted),
                        AcceptanceRate = AcceptanceRate
                    };

                    // Calculate acceptance by suggestion type
                    var byType = _interactions.GroupBy(i => i.SuggestionType)
                        .ToDictionary(g => g.Key, g => new
                        {
                            Total = g.Count(),
                            Accepted = g.Count(i => i.WasAccepted)
                        });

                    summary.AcceptanceByType = byType.ToDictionary(
                        kvp => kvp.Key,
                        kvp => kvp.Value.Total > 0 ? (double)kvp.Value.Accepted / kvp.Value.Total : 0);

                    // Recent trend (last 50 interactions)
                    var recentInteractions = _interactions.TakeLast(50).ToList();
                    summary.RecentAcceptanceRate = recentInteractions.Any()
                        ? (double)recentInteractions.Count(i => i.WasAccepted) / recentInteractions.Count
                        : 0;

                    return summary;
                }
            });
        }

        public async Task<Dictionary<string, object>> ExportDataAsync()
        {
            return await Task.Run(() =>
            {
                lock (_lock)
                {
                    return new Dictionary<string, object>
                    {
                        ["interactions"] = _interactions.Select(i => new
                        {
                            i.Timestamp,
                            i.WasAccepted,
                            i.SuggestionType,
                            i.Context?.Language,
                            i.ResponseTime
                        }).ToList(),
                        ["totalInteractions"] = TotalInteractions,
                        ["acceptanceRate"] = AcceptanceRate
                    };
                }
            });
        }

        public async Task ImportDataAsync(Dictionary<string, object> data)
        {
            if (data == null) return;

            await Task.Run(() =>
            {
                lock (_lock)
                {
                    _interactions.Clear();
                    
                    if (data.TryGetValue("interactions", out var interactionsObj) && 
                        interactionsObj is JsonElement interactionsElement &&
                        interactionsElement.ValueKind == JsonValueKind.Array)
                    {
                        // Import would deserialize interaction data
                        // For now, just create placeholder data
                        // Real implementation would properly deserialize the JSON
                    }
                }
            });
        }

        public async Task CleanupOldDataAsync(DateTime cutoffDate)
        {
            await Task.Run(() =>
            {
                lock (_lock)
                {
                    _interactions.RemoveAll(i => i.Timestamp < cutoffDate);
                }
            });
        }

        public void Clear()
        {
            lock (_lock)
            {
                _interactions.Clear();
            }
        }

        public void Dispose()
        {
            Clear();
        }
    }

    /// <summary>
    /// Analyzes coding style patterns
    /// </summary>
    internal class CodingStyleAnalyzer : IDisposable
    {
        private readonly Dictionary<string, CodingStyleElement> _styleElements
        private readonly object _lock = new object();

        public int StyleElementCount => _styleElements.Count;

        public async Task<CodingStyleAnalysis> AnalyzeStyleAsync(string filePath, string content)
        {
            return await Task.Run(() =>
            {
                var analysis = new CodingStyleAnalysis
                {
                    FilePath = filePath,
                    Language = DetectLanguage(filePath),
                    AnalyzedAt = DateTime.UtcNow
                };

                try
                {
                    // Analyze indentation
                    AnalyzeIndentation(content, analysis);

                    // Analyze naming conventions
                    AnalyzeNamingConventions(content, analysis);

                    // Analyze formatting preferences
                    AnalyzeFormatting(content, analysis);

                    // Update style elements
                    UpdateStyleElements(analysis);
                }
                catch (Exception ex)
                {
                    analysis.AnalysisErrors.Add($"Style analysis failed: {ex.Message}");
                }

                return analysis;
            });
        }

        public async Task<CodingStyleProfile> GetStyleProfileAsync()
        {
            return await Task.Run(() =>
            {
                lock (_lock)
                {
                    return new CodingStyleProfile
                    {
                        IndentationStyle = GetMostCommonStyle("indentation"),
                        NamingConvention = GetMostCommonStyle("naming"),
                        BraceStyle = GetMostCommonStyle("braces"),
                        PreferredLineLength = GetAverageValue("lineLength"),
                        ElementCount = _styleElements.Count,
                        LastAnalysis = _styleElements.Values.Max(e => e.LastSeen)
                    };
                }
            });
        }

        public async Task<Dictionary<string, object>> ExportDataAsync()
        {
            return await Task.Run(() =>
            {
                lock (_lock)
                {
                    return new Dictionary<string, object>
                    {
                        ["styleElements"] = _styleElements.ToDictionary(
                            kvp => kvp.Key,
                            kvp => new
                            {
                                kvp.Value.Type,
                                kvp.Value.Value,
                                kvp.Value.Frequency,
                                kvp.Value.LastSeen
                            }),
                        ["elementCount"] = StyleElementCount
                    };
                }
            });
        }

        public async Task ImportDataAsync(Dictionary<string, object> data)
        {
            if (data == null) return;

            await Task.Run(() =>
            {
                lock (_lock)
                {
                    _styleElements.Clear();
                    // Import would deserialize style data
                    // Placeholder implementation
                }
            });
        }

        public async Task CleanupOldDataAsync(DateTime cutoffDate)
        {
            await Task.Run(() =>
            {
                lock (_lock)
                {
                    var keysToRemove = _styleElements.Where(kvp => kvp.Value.LastSeen < cutoffDate)
                        .Select(kvp => kvp.Key).ToList();

                    foreach (var key in keysToRemove)
                    {
                        _styleElements.Remove(key);
                    }
                }
            });
        }

        private void AnalyzeIndentation(string content, CodingStyleAnalysis analysis)
        {
            var lines = content.Split('\n');
            var spaceIndents = 0;
            var tabIndents = 0;
            var indentSizes = new List<int>();

            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;

                var leadingWhitespace = line.TakeWhile(char.IsWhiteSpace).ToArray();
                if (leadingWhitespace.Length == 0) continue;

                if (leadingWhitespace[0] == '\t')
                {
                    tabIndents++;
                }
                else if (leadingWhitespace[0] == ' ')
                {
                    spaceIndents++;
                    indentSizes.Add(leadingWhitespace.Length);
                }
            }

            analysis.IndentationStyle = tabIndents > spaceIndents ? "tabs" : "spaces";
            analysis.IndentationSize = indentSizes.Any() ? 
                indentSizes.GroupBy(x => x).OrderByDescending(g => g.Count()).First().Key : 4;
        }

        private void AnalyzeNamingConventions(string content, CodingStyleAnalysis analysis)
        {
            // Simple regex-based naming analysis
            var camelCasePattern = @"\b[a-z][a-zA-Z0-9]*\b";
            var pascalCasePattern = @"\b[A-Z][a-zA-Z0-9]*\b";
            var snakeCasePattern = @"\b[a-z][a-z0-9_]*\b";

            var camelCaseMatches = System.Text.RegularExpressions.Regex.Matches(content, camelCasePattern);
            var pascalCaseMatches = System.Text.RegularExpressions.Regex.Matches(content, pascalCasePattern);
            var snakeCaseMatches = System.Text.RegularExpressions.Regex.Matches(content, snakeCasePattern);

            var mostCommon = new[] {
                ("camelCase", camelCaseMatches.Count),
                ("PascalCase", pascalCaseMatches.Count),
                ("snake_case", snakeCaseMatches.Count)
            }.OrderByDescending(x => x.Item2).First();

            analysis.PreferredNamingConvention = mostCommon.Item1;
        }

        private void AnalyzeFormatting(string content, CodingStyleAnalysis analysis)
        {
            var lines = content.Split('\n');
            analysis.AverageLineLength = lines.Where(l => !string.IsNullOrWhiteSpace(l))
                .Average(l => l.Length);

            // Analyze brace style
            var sameLine = content.Contains(") {");
            var nextLine = content.Contains(")\n{") || content.Contains(")\r\n{");

            analysis.BraceStyle = sameLine ? "same-line" : "next-line";
        }

        private void UpdateStyleElements(CodingStyleAnalysis analysis)
        {
            lock (_lock)
            {
                UpdateStyleElement("indentation", analysis.IndentationStyle);
                UpdateStyleElement("naming", analysis.PreferredNamingConvention);
                UpdateStyleElement("braces", analysis.BraceStyle);
                UpdateStyleElement("lineLength", analysis.AverageLineLength.ToString());
            }
        }

        private void UpdateStyleElement(string type, string value)
        {
            var key = $"{type}_{value}";
            if (_styleElements.TryGetValue(key, out var element))
            {
                element.Frequency++;
                element.LastSeen = DateTime.UtcNow;
            }
            else
            {
                _styleElements[key] = new CodingStyleElement
                {
                    Type = type,
                    Value = value,
                    Frequency = 1,
                    LastSeen = DateTime.UtcNow
                };
            }
        }

        private string GetMostCommonStyle(string type)
        {
            lock (_lock)
            {
                return _styleElements.Values
                    .Where(e => e.Type == type)
                    .OrderByDescending(e => e.Frequency)
                    .FirstOrDefault()?.Value ?? "unknown";
            }
        }

        private double GetAverageValue(string type)
        {
            lock (_lock)
            {
                var values = _styleElements.Values
                    .Where(e => e.Type == type && double.TryParse(e.Value, out _))
                    .Select(e => double.Parse(e.Value))
                    .ToList();

                return values.Any() ? values.Average() : 0;
            }
        }

        private string DetectLanguage(string filePath)
        {
            var extension = Path.GetExtension(filePath)?.ToLower();
            return extension switch
            {
                ".cs" => "C#",
                ".js" => "JavaScript",
                ".ts" => "TypeScript",
                ".py" => "Python",
                ".java" => "Java",
                _ => "Unknown"
            };
        }

        public void Clear()
        {
            lock (_lock)
            {
                _styleElements.Clear();
            }
        }

        public void Dispose()
        {
            Clear();
        }
    }

    /// <summary>
    /// Learns patterns from user interactions
    /// </summary>
    internal class PatternLearner : IDisposable
    {
        private readonly Dictionary<string, LearnedPattern> _patterns
        private readonly object _lock = new object();

        public int PatternCount => _patterns.Count;
        public DateTime LastUpdateTime { get; private set; } = DateTime.UtcNow;

        public async Task UpdatePatternsAsync(SuggestionFeedback feedback)
        {
            await Task.Run(() =>
            {
                if (feedback?.Context == null) return;

                lock (_lock)
                {
                    var patternKey = GeneratePatternKey(feedback.Context);
                    
                    if (_patterns.TryGetValue(patternKey, out var pattern))
                    {
                        pattern.Occurrences++;
                        if (feedback.WasAccepted)
                        {
                            pattern.SuccessCount++;
                        }
                        pattern.LastSeen = DateTime.UtcNow;
                    }
                    else
                    {
                        _patterns[patternKey] = new LearnedPattern
                        {
                            PatternKey = patternKey,
                            Context = feedback.Context,
                            Occurrences = 1,
                            SuccessCount = feedback.WasAccepted ? 1 : 0,
                            FirstSeen = DateTime.UtcNow,
                            LastSeen = DateTime.UtcNow
                        };
                    }
                }
            });
        }

        public async Task<List<LearnedPattern>> GetRelevantPatternsAsync(CodeContext context)
        {
            return await Task.Run(() =>
            {
                lock (_lock)
                {
                    var contextKey = GeneratePatternKey(context);
                    
                    return _patterns.Values
                        .Where(p => IsPatternRelevant(p, context))
                        .OrderByDescending(p => p.SuccessRate)
                        .Take(10)
                        .ToList();
                }
            });
        }

        public async Task<PatternSummary> GetPatternSummaryAsync()
        {
            return await Task.Run(() =>
            {
                lock (_lock)
                {
                    return new PatternSummary
                    {
                        TotalPatterns = _patterns.Count,
                        HighSuccessPatterns = _patterns.Values.Count(p => p.SuccessRate > 0.8),
                        RecentPatterns = _patterns.Values.Count(p => DateTime.UtcNow - p.LastSeen < TimeSpan.FromDays(7)),
                        AverageSuccessRate = _patterns.Values.Any() ? _patterns.Values.Average(p => p.SuccessRate) : 0,
                        LastUpdate = LastUpdateTime
                    };
                }
            });
        }

        public async Task UpdateModelsAsync()
        {
            await Task.Run(() =>
            {
                LastUpdateTime = DateTime.UtcNow;
                // Model update logic would go here
                // For now, just update the timestamp
            });
        }

        public async Task<Dictionary<string, object>> ExportDataAsync()
        {
            return await Task.Run(() =>
            {
                lock (_lock)
                {
                    return new Dictionary<string, object>
                    {
                        ["patterns"] = _patterns.Values.Select(p => new
                        {
                            p.PatternKey,
                            p.Occurrences,
                            p.SuccessCount,
                            p.FirstSeen,
                            p.LastSeen,
                            ContextLanguage = p.Context?.Language
                        }).ToList(),
                        ["patternCount"] = PatternCount,
                        ["lastUpdate"] = LastUpdateTime
                    };
                }
            });
        }

        public async Task ImportDataAsync(Dictionary<string, object> data)
        {
            if (data == null) return;

            await Task.Run(() =>
            {
                lock (_lock)
                {
                    _patterns.Clear();
                    // Import would deserialize pattern data
                    // Placeholder implementation
                }
            });
        }

        public async Task CleanupOldDataAsync(DateTime cutoffDate)
        {
            await Task.Run(() =>
            {
                lock (_lock)
                {
                    var keysToRemove = _patterns.Where(kvp => kvp.Value.LastSeen < cutoffDate)
                        .Select(kvp => kvp.Key).ToList();

                    foreach (var key in keysToRemove)
                    {
                        _patterns.Remove(key);
                    }
                }
            });
        }

        private string GeneratePatternKey(CodeContext context)
        {
            if (context == null) return "unknown";

            var components = new List<string>
            {
                context.Language ?? "unknown",
                context.CursorPosition?.Line.ToString() ?? "0"
            };

            // Add context from surrounding lines
            if (context.LinesBefore?.Any() == true)
            {
                var beforeHash = string.Join("", context.LinesBefore).GetHashCode().ToString();
                components.Add($"before_{beforeHash}");
            }

            if (context.LinesAfter?.Any() == true)
            {
                var afterHash = string.Join("", context.LinesAfter).GetHashCode().ToString();
                components.Add($"after_{afterHash}");
            }

            return string.Join("_", components);
        }

        private bool IsPatternRelevant(LearnedPattern pattern, CodeContext context)
        {
            if (pattern?.Context == null || context == null)
                return false;

            // Check language match
            if (!string.Equals(pattern.Context.Language, context.Language, StringComparison.OrdinalIgnoreCase))
                return false;

            // Check if pattern has reasonable success rate
            if (pattern.SuccessRate < 0.3)
                return false;

            // Check recency
            if (DateTime.UtcNow - pattern.LastSeen > TimeSpan.FromDays(30))
                return false;

            return true;
        }

        public void Clear()
        {
            lock (_lock)
            {
                _patterns.Clear();
            }
        }

        public void Dispose()
        {
            Clear();
        }
    }

    /// <summary>
    /// Generates personalized suggestions based on learned patterns
    /// </summary>
    internal class PersonalizationEngine : IDisposable
    {
        private readonly Dictionary<string, PersonalizationData> _personalizationData
        private readonly object _lock = new object();

        public double PersonalizationScore
        {
            get
            {
                lock (_lock)
                {
                    return _personalizationData.Values.Any() ? 
                        _personalizationData.Values.Average(p => p.Score) : 0;
                }
            }
        }

        public async Task UpdatePersonalizationAsync(SuggestionFeedback feedback)
        {
            await Task.Run(() =>
            {
                if (feedback?.Context == null) return;

                lock (_lock)
                {
                    var key = $"{feedback.Context.Language}_{feedback.SuggestionType}";
                    
                    if (_personalizationData.TryGetValue(key, out var data))
                    {
                        data.InteractionCount++;
                        if (feedback.WasAccepted)
                        {
                            data.AcceptanceCount++;
                        }
                        data.Score = (double)data.AcceptanceCount / data.InteractionCount;
                        data.LastUpdate = DateTime.UtcNow;
                    }
                    else
                    {
                        _personalizationData[key] = new PersonalizationData
                        {
                            Key = key,
                            InteractionCount = 1,
                            AcceptanceCount = feedback.WasAccepted ? 1 : 0,
                            Score = feedback.WasAccepted ? 1.0 : 0.0,
                            LastUpdate = DateTime.UtcNow
                        };
                    }
                }
            });
        }

        public async Task UpdateCodingStyleAsync(CodingStyleAnalysis styleAnalysis)
        {
            await Task.Run(() =>
            {
                // Update personalization based on coding style
                // This would integrate style preferences into suggestions
            });
        }

        public async Task<UserPreferences> GetUserPreferencesAsync()
        {
            return await Task.Run(() =>
            {
                lock (_lock)
                {
                    return new UserPreferences
                    {
                        PreferredLanguages = GetPreferredLanguages(),
                        SuggestionTypePreferences = GetSuggestionTypePreferences(),
                        PersonalizationScore = PersonalizationScore,
                        LastUpdate = _personalizationData.Values.Any() ? 
                            _personalizationData.Values.Max(p => p.LastUpdate) : DateTime.MinValue
                    };
                }
            });
        }

        public async Task<PersonalizedSuggestions> GeneratePersonalizedSuggestionsAsync(
            CodeContext context, UserPreferences preferences, List<LearnedPattern> patterns)
        {
            return await Task.Run(() =>
            {
                var suggestions = new PersonalizedSuggestions
                {
                    Context = context,
                    GeneratedAt = DateTime.UtcNow
                };

                // Generate suggestions based on learned patterns
                foreach (var pattern in patterns.Take(5))
                {
                    suggestions.Suggestions.Add(new PersonalizedSuggestion
                    {
                        Text = $"Pattern-based suggestion from {pattern.PatternKey}",
                        Confidence = pattern.SuccessRate,
                        Source = "LearnedPattern",
                        PatternMatch = pattern.PatternKey
                    });
                }

                // Add style-based suggestions
                if (preferences?.SuggestionTypePreferences?.Any() == true)
                {
                    var preferredType = preferences.SuggestionTypePreferences
                        .OrderByDescending(kvp => kvp.Value)
                        .First().Key;

                    suggestions.Suggestions.Add(new PersonalizedSuggestion
                    {
                        Text = $"Preferred {preferredType} suggestion",
                        Confidence = 0.8,
                        Source = "UserPreference",
                        PatternMatch = preferredType
                    });
                }

                return suggestions;
            });
        }

        public async Task UpdateModelsAsync()
        {
            await Task.Run(() =>
            {
                // Update personalization models
                // Placeholder implementation
            });
        }

        public async Task<Dictionary<string, object>> ExportDataAsync()
        {
            return await Task.Run(() =>
            {
                lock (_lock)
                {
                    return new Dictionary<string, object>
                    {
                        ["personalizationData"] = _personalizationData.Values.Select(p => new
                        {
                            p.Key,
                            p.InteractionCount,
                            p.AcceptanceCount,
                            p.Score,
                            p.LastUpdate
                        }).ToList(),
                        ["personalizationScore"] = PersonalizationScore
                    };
                }
            });
        }

        public async Task ImportDataAsync(Dictionary<string, object> data)
        {
            if (data == null) return;

            await Task.Run(() =>
            {
                lock (_lock)
                {
                    _personalizationData.Clear();
                    // Import would deserialize personalization data
                    // Placeholder implementation
                }
            });
        }

        private Dictionary<string, double> GetPreferredLanguages()
        {
            lock (_lock)
            {
                return _personalizationData.Keys
                    .Select(k => k.Split('_')[0])
                    .GroupBy(lang => lang)
                    .ToDictionary(g => g.Key, g => g.Count() / (double)_personalizationData.Count);
            }
        }

        private Dictionary<string, double> GetSuggestionTypePreferences()
        {
            lock (_lock)
            {
                return _personalizationData.Values
                    .GroupBy(p => p.Key.Split('_').Last())
                    .ToDictionary(g => g.Key, g => g.Average(p => p.Score));
            }
        }

        public void Clear()
        {
            lock (_lock)
            {
                _personalizationData.Clear();
            }
        }

        public void Dispose()
        {
            Clear();
        }
    }

    /// <summary>
    /// Collects usage analytics and insights
    /// </summary>
    internal class UsageAnalyticsCollector : IDisposable
    {
        private readonly List<AnalyticsDataPoint> _dataPoints
        private readonly object _lock = new object();

        public int DataPointCount => _dataPoints.Count;

        public void RecordFeedback(SuggestionFeedback feedback)
        {
            lock (_lock)
            {
                _dataPoints.Add(new AnalyticsDataPoint
                {
                    Type = "feedback",
                    Timestamp = DateTime.UtcNow,
                    Data = new Dictionary<string, object>
                    {
                        ["accepted"] = feedback.WasAccepted,
                        ["suggestionType"] = feedback.SuggestionType,
                        ["language"] = feedback.Context?.Language,
                        ["responseTime"] = feedback.ResponseTime.TotalMilliseconds
                    }
                });
            }
        }

        public void RecordStyleAnalysis(CodingStyleAnalysis analysis)
        {
            lock (_lock)
            {
                _dataPoints.Add(new AnalyticsDataPoint
                {
                    Type = "style",
                    Timestamp = DateTime.UtcNow,
                    Data = new Dictionary<string, object>
                    {
                        ["language"] = analysis.Language,
                        ["indentationStyle"] = analysis.IndentationStyle,
                        ["namingConvention"] = analysis.PreferredNamingConvention,
                        ["averageLineLength"] = analysis.AverageLineLength
                    }
                });
            }
        }

        public void RecordSuggestionRequest(CodeContext context, PersonalizedSuggestions suggestions)
        {
            lock (_lock)
            {
                _dataPoints.Add(new AnalyticsDataPoint
                {
                    Type = "suggestion_request",
                    Timestamp = DateTime.UtcNow,
                    Data = new Dictionary<string, object>
                    {
                        ["language"] = context?.Language,
                        ["suggestionCount"] = suggestions?.Suggestions?.Count ?? 0,
                        ["hasPersonalization"] = suggestions?.Suggestions?.Any(s => s.Source == "UserPreference") ?? false
                    }
                });
            }
        }

        public AnalyticsSummary GetAnalyticsSummary()
        {
            lock (_lock)
            {
                var summary = new AnalyticsSummary
                {
                    TotalDataPoints = _dataPoints.Count,
                    DataPointsByType = _dataPoints.GroupBy(d => d.Type)
                        .ToDictionary(g => g.Key, g => g.Count()),
                    LastDataPoint = _dataPoints.LastOrDefault()?.Timestamp ?? DateTime.MinValue
                };

                // Calculate feedback metrics
                var feedbackPoints = _dataPoints.Where(d => d.Type == "feedback").ToList();
                if (feedbackPoints.Any())
                {
                    summary.OverallAcceptanceRate = feedbackPoints
                        .Average(d => (bool)d.Data["accepted"] ? 1.0 : 0.0);

                    summary.AverageResponseTime = feedbackPoints
                        .Average(d => (double)d.Data["responseTime"]);
                }

                return summary;
            }
        }

        public Dictionary<string, object> ExportData()
        {
            lock (_lock)
            {
                return new Dictionary<string, object>
                {
                    ["dataPoints"] = _dataPoints.Select(d => new
                    {
                        d.Type,
                        d.Timestamp,
                        d.Data
                    }).ToList(),
                    ["dataPointCount"] = DataPointCount
                };
            }
        }

        public void ImportData(Dictionary<string, object> data)
        {
            if (data == null) return;

            lock (_lock)
            {
                _dataPoints.Clear();
                // Import would deserialize analytics data
                // Placeholder implementation
            }
        }

        public void CleanupOldData(DateTime cutoffDate)
        {
            lock (_lock)
            {
                _dataPoints.RemoveAll(d => d.Timestamp < cutoffDate);
            }
        }

        public void Clear()
        {
            lock (_lock)
            {
                _dataPoints.Clear();
            }
        }

        public void Dispose()
        {
            Clear();
        }
    }

    // Model classes for learning and adaptation
    public class SuggestionFeedback
    {
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        public bool WasAccepted { get; set; }
        public string SuggestionType { get; set; }
        public CodeContext Context { get; set; }
        public TimeSpan ResponseTime { get; set; }
        public string SuggestionText { get; set; }
        public double Confidence { get; set; }
    }

    public class CodingStyleAnalysis
    {
        public string FilePath { get; set; }
        public string Language { get; set; }
        public DateTime AnalyzedAt { get; set; }
        public string IndentationStyle { get; set; }
        public int IndentationSize { get; set; }
        public string PreferredNamingConvention { get; set; }
        public string BraceStyle { get; set; }
        public double AverageLineLength { get; set; }
        public List<string> AnalysisErrors { get; set; }
    }

    public class PersonalizedSuggestions
    {
        public CodeContext Context { get; set; }
        public List<PersonalizedSuggestion> Suggestions { get; set; }
        public DateTime GeneratedAt { get; set; }
    }

    public class PersonalizedSuggestion
    {
        public string Text { get; set; }
        public double Confidence { get; set; }
        public string Source { get; set; }
        public string PatternMatch { get; set; }
    }

    public class UsageInsights
    {
        public InteractionSummary InteractionSummary { get; set; }
        public CodingStyleProfile CodingStyleProfile { get; set; }
        public PatternSummary LearnedPatterns { get; set; }
        public AnalyticsSummary Analytics { get; set; }
        public DateTime GeneratedAt { get; set; }
    }

    public class LearningStatistics
    {
        public int TotalInteractions { get; set; }
        public double AcceptanceRate { get; set; }
        public int LearnedPatterns { get; set; }
        public int CodingStyleElements { get; set; }
        public double PersonalizationScore { get; set; }
        public int AnalyticsDataPoints { get; set; }
        public DateTime LastLearningUpdate { get; set; }
        public long LearningDataSize { get; set; }
    }

    public class LearningDataExport
    {
        public DateTime ExportDate { get; set; }
        public string Version { get; set; }
        public Dictionary<string, object> InteractionData { get; set; }
        public Dictionary<string, object> StyleData { get; set; }
        public Dictionary<string, object> PatternData { get; set; }
        public Dictionary<string, object> PersonalizationData { get; set; }
        public Dictionary<string, object> AnalyticsData { get; set; }
    }

    // Supporting model classes
    public class InteractionSummary
    {
        public int TotalInteractions { get; set; }
        public int AcceptedSuggestions { get; set; }
        public int RejectedSuggestions { get; set; }
        public double AcceptanceRate { get; set; }
        public Dictionary<string, double> AcceptanceByType { get; set; }
        public double RecentAcceptanceRate { get; set; }
    }

    public class CodingStyleProfile
    {
        public string IndentationStyle { get; set; }
        public string NamingConvention { get; set; }
        public string BraceStyle { get; set; }
        public double PreferredLineLength { get; set; }
        public int ElementCount { get; set; }
        public DateTime LastAnalysis { get; set; }
    }

    public class PatternSummary
    {
        public int TotalPatterns { get; set; }
        public int HighSuccessPatterns { get; set; }
        public int RecentPatterns { get; set; }
        public double AverageSuccessRate { get; set; }
        public DateTime LastUpdate { get; set; }
    }

    public class AnalyticsSummary
    {
        public int TotalDataPoints { get; set; }
        public Dictionary<string, int> DataPointsByType { get; set; }
        public double OverallAcceptanceRate { get; set; }
        public double AverageResponseTime { get; set; }
        public DateTime LastDataPoint { get; set; }
    }

    public class UserPreferences
    {
        public Dictionary<string, double> PreferredLanguages { get; set; }
        public Dictionary<string, double> SuggestionTypePreferences { get; set; }
        public double PersonalizationScore { get; set; }
        public DateTime LastUpdate { get; set; }
    }

    // Internal model classes
    internal class CodingStyleElement
    {
        public string Type { get; set; }
        public string Value { get; set; }
        public int Frequency { get; set; }
        public DateTime LastSeen { get; set; }
    }

    internal class LearnedPattern
    {
        public string PatternKey { get; set; }
        public CodeContext Context { get; set; }
        public int Occurrences { get; set; }
        public int SuccessCount { get; set; }
        public DateTime FirstSeen { get; set; }
        public DateTime LastSeen { get; set; }
        public double SuccessRate => Occurrences > 0 ? (double)SuccessCount / Occurrences : 0;
    }

    internal class PersonalizationData
    {
        public string Key { get; set; }
        public int InteractionCount { get; set; }
        public int AcceptanceCount { get; set; }
        public double Score { get; set; }
        public DateTime LastUpdate { get; set; }
    }

    internal class AnalyticsDataPoint
    {
        public string Type { get; set; }
        public DateTime Timestamp { get; set; }
        public Dictionary<string, object> Data { get; set; }
    }
}