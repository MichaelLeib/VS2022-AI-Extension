using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using OllamaAssistant.Infrastructure;
using OllamaAssistant.Models;
using OllamaAssistant.Services.Interfaces;

namespace OllamaAssistant.Tests.TestUtilities
{
    /// <summary>
    /// Factory for creating mock objects used in testing
    /// </summary>
    public static class MockFactory
    {
        #region Service Mocks

        /// <summary>
        /// Creates a mock settings service with default configuration
        /// </summary>
        public static Mock<ISettingsService> CreateMockSettingsService()
        {
            var mock = new Mock<ISettingsService>();
            
            // Setup default values
            mock.SetupProperty(x => x.OllamaEndpoint, "http://localhost:11434");
            mock.SetupProperty(x => x.OllamaModel, "codellama");
            mock.SetupProperty(x => x.ContextLinesUp, 3);
            mock.SetupProperty(x => x.ContextLinesDown, 2);
            mock.SetupProperty(x => x.CursorHistoryDepth, 5);
            mock.SetupProperty(x => x.RequestTimeoutSeconds, 30);
            mock.SetupProperty(x => x.MaxConcurrentRequests, 2);
            mock.SetupProperty(x => x.EnableCodeCompletion, true);
            mock.SetupProperty(x => x.EnableJumpRecommendations, true);
            mock.SetupProperty(x => x.EnableCaching, true);
            mock.SetupProperty(x => x.CacheTTLMinutes, 5);
            mock.SetupProperty(x => x.FilterSensitiveData, true);
            mock.SetupProperty(x => x.LogLevel, "Info");
            mock.SetupProperty(x => x.EnableTelemetry, false);

            // Setup methods
            mock.Setup(x => x.GetSetting<T>(It.IsAny<string>(), It.IsAny<T>()))
                .Returns<string, T>((key, defaultValue) => defaultValue);
            
            mock.Setup(x => x.SetSetting(It.IsAny<string>(), It.IsAny<object>()));
            mock.Setup(x => x.SaveSettings()).Returns(Task.CompletedTask);
            mock.Setup(x => x.LoadSettings()).Returns(Task.CompletedTask);
            mock.Setup(x => x.ResetToDefaults()).Returns(Task.CompletedTask);

            return mock;
        }

        /// <summary>
        /// Creates a mock logger
        /// </summary>
        public static Mock<ILogger> CreateMockLogger()
        {
            var mock = new Mock<ILogger>();
            
            mock.Setup(x => x.LogDebug(It.IsAny<string>(), It.IsAny<object>()));
            mock.Setup(x => x.LogInfo(It.IsAny<string>(), It.IsAny<object>()));
            mock.Setup(x => x.LogWarning(It.IsAny<string>(), It.IsAny<Exception>()));
            mock.Setup(x => x.LogError(It.IsAny<string>(), It.IsAny<Exception>()));
            
            return mock;
        }

        /// <summary>
        /// Creates a mock error handler
        /// </summary>
        public static Mock<ErrorHandler> CreateMockErrorHandler()
        {
            var mock = new Mock<ErrorHandler>();
            
            mock.Setup(x => x.HandleError(It.IsAny<Exception>(), It.IsAny<string>()))
                .Returns(Task.CompletedTask);
            
            mock.Setup(x => x.HandleErrorAsync(It.IsAny<Exception>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
                
            return mock;
        }

        /// <summary>
        /// Creates a mock Ollama HTTP client
        /// </summary>
        public static Mock<IOllamaHttpClient> CreateMockOllamaHttpClient()
        {
            var mock = new Mock<IOllamaHttpClient>();
            
            // Setup successful responses
            mock.Setup(x => x.PostAsync(It.IsAny<string>(), It.IsAny<object>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(CreateSampleOllamaResponse());
            
            mock.Setup(x => x.GetAsync<ModelInfo>(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(CreateSampleModelInfo());
            
            mock.Setup(x => x.GetAsync<object>(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(CreateSampleModelInfo());
            
            mock.Setup(x => x.TestConnectionAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);
            
            mock.Setup(x => x.GetHealthStatusAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new HealthStatus { IsHealthy = true, StatusMessage = "OK" });
            
            return mock;
        }

        /// <summary>
        /// Creates a mock context capture service
        /// </summary>
        public static Mock<IContextCaptureService> CreateMockContextCaptureService()
        {
            var mock = new Mock<IContextCaptureService>();
            
            mock.Setup(x => x.CaptureContextAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>()))
                .ReturnsAsync(CreateTestCodeContext());
            
            mock.Setup(x => x.OptimizeContext(It.IsAny<CodeContext>(), It.IsAny<int>()))
                .Returns<CodeContext, int>((context, maxTokens) => context);
            
            return mock;
        }

        /// <summary>
        /// Creates a mock cursor history service
        /// </summary>
        public static Mock<ICursorHistoryService> CreateMockCursorHistoryService()
        {
            var mock = new Mock<ICursorHistoryService>();
            
            mock.Setup(x => x.AddCursorPosition(It.IsAny<CursorPosition>()));
            mock.Setup(x => x.GetRecentHistory(It.IsAny<int>()))
                .Returns(CreateTestCursorHistory(5));
            mock.Setup(x => x.ClearHistory());
            
            return mock;
        }

        #endregion

        #region Test Data Creation

        /// <summary>
        /// Creates a test code context
        /// </summary>
        public static CodeContext CreateTestCodeContext()
        {
            return new CodeContext
            {
                FilePath = @"C:\TestProject\TestClass.cs",
                Language = "csharp",
                CurrentLine = "public class TestClass",
                LineNumber = 10,
                ColumnNumber = 5,
                Code = @"using System;

namespace TestProject
{
    public class TestClass
    {
        public void TestMethod()
        {
            // Test implementation
        }
    }
}",
                CursorPosition = new CursorPosition
                {
                    Line = 10,
                    Column = 5,
                    FilePath = @"C:\TestProject\TestClass.cs",
                    Timestamp = DateTime.UtcNow
                },
                SurroundingCode = new SurroundingCodeContext
                {
                    LinesAbove = new[] { "namespace TestProject", "{" },
                    LinesBelow = new[] { "{", "    public void TestMethod()" }
                },
                ProjectContext = new ProjectContext
                {
                    ProjectName = "TestProject",
                    ProjectType = "Console Application",
                    TargetFramework = ".NET 6.0"
                }
            };
        }

        /// <summary>
        /// Creates test cursor position history
        /// </summary>
        public static List<CursorPosition> CreateTestCursorHistory(int count)
        {
            var history = new List<CursorPosition>();
            var baseTime = DateTime.UtcNow.AddMinutes(-count);
            
            for (int i = 0; i < count; i++)
            {
                history.Add(new CursorPosition
                {
                    Line = 10 + i,
                    Column = 5 + (i * 2),
                    FilePath = $@"C:\TestProject\TestClass{i}.cs",
                    Timestamp = baseTime.AddMinutes(i),
                    Context = $"Context for position {i}"
                });
            }
            
            return history;
        }

        /// <summary>
        /// Creates a sample code suggestion
        /// </summary>
        public static CodeSuggestion CreateTestCodeSuggestion()
        {
            return new CodeSuggestion
            {
                Id = Guid.NewGuid().ToString(),
                Text = "Console.WriteLine(\"Hello, World!\");",
                Confidence = 0.85,
                Type = SuggestionType.CodeCompletion,
                ModelUsed = "codellama",
                Context = CreateTestCodeContext(),
                Metadata = new Dictionary<string, object>
                {
                    ["reasoning"] = "Method call completion",
                    ["tokens"] = 15
                }
            };
        }

        /// <summary>
        /// Creates a sample jump recommendation
        /// </summary>
        public static JumpRecommendation CreateTestJumpRecommendation()
        {
            return new JumpRecommendation
            {
                Direction = JumpDirection.Down,
                TargetLine = 15,
                TargetColumn = 8,
                Confidence = 0.75,
                Reason = "Method implementation follows declaration",
                FilePath = @"C:\TestProject\TestClass.cs"
            };
        }

        /// <summary>
        /// Creates a sample Ollama response
        /// </summary>
        public static OllamaResponse CreateSampleOllamaResponse()
        {
            return new OllamaResponse
            {
                Model = "codellama",
                Response = "Console.WriteLine(\"Hello, World!\");",
                Done = true,
                Context = new[] { 1, 2, 3, 4, 5 },
                TotalDuration = 1500000000, // 1.5 seconds in nanoseconds
                LoadDuration = 100000000,   // 0.1 seconds
                PromptEvalCount = 25,
                PromptEvalDuration = 200000000, // 0.2 seconds
                EvalCount = 15,
                EvalDuration = 300000000    // 0.3 seconds
            };
        }

        /// <summary>
        /// Creates sample model information
        /// </summary>
        public static ModelInfo CreateSampleModelInfo()
        {
            return new ModelInfo
            {
                Name = "codellama",
                Size = "7B",
                Format = "gguf",
                Family = "llama",
                Families = new[] { "llama" },
                ParameterSize = "7.0B",
                QuantizationLevel = "Q4_0",
                ModifiedAt = DateTime.UtcNow.AddDays(-1),
                Details = new ModelDetails
                {
                    ParentModel = "",
                    Format = "gguf",
                    Family = "llama",
                    Families = new[] { "llama" },
                    ParameterSize = "7.0B",
                    QuantizationLevel = "Q4_0"
                }
            };
        }

        /// <summary>
        /// Creates a test cursor history entry
        /// </summary>
        public static CursorHistoryEntry CreateTestCursorHistoryEntry()
        {
            return new CursorHistoryEntry
            {
                FilePath = @"C:\TestProject\TestClass.cs",
                LineNumber = 10,
                ColumnNumber = 5,
                Timestamp = DateTime.UtcNow,
                Context = "public class TestClass {",
                ProjectName = "TestProject",
                Metadata = new Dictionary<string, object>
                {
                    ["language"] = "csharp",
                    ["scope"] = "class"
                }
            };
        }

        /// <summary>
        /// Creates test cursor history entries
        /// </summary>
        public static List<CursorHistoryEntry> CreateTestCursorHistory(int count)
        {
            var history = new List<CursorHistoryEntry>();
            var baseTime = DateTime.UtcNow.AddMinutes(-count);
            
            for (int i = 0; i < count; i++)
            {
                history.Add(new CursorHistoryEntry
                {
                    FilePath = $@"C:\TestProject\TestClass{i}.cs",
                    LineNumber = 10 + i,
                    ColumnNumber = 5 + (i * 2),
                    Timestamp = baseTime.AddMinutes(i),
                    Context = $"Line {10 + i} context",
                    ProjectName = "TestProject",
                    Metadata = new Dictionary<string, object>
                    {
                        ["entryIndex"] = i
                    }
                });
            }
            
            return history;
        }

        /// <summary>
        /// Creates a test scenario builder
        /// </summary>
        public static TestScenarioBuilder CreateTestScenario()
        {
            return new TestScenarioBuilder();
        }

        #endregion

        #region HTTP Response Mocks

        /// <summary>
        /// Creates a mock HTTP response for successful completion
        /// </summary>
        public static string CreateMockCompletionResponse()
        {
            return @"{
                ""model"": ""codellama"",
                ""response"": ""Console.WriteLine(\""Hello, World!\"");"",
                ""done"": true,
                ""context"": [1, 2, 3, 4, 5],
                ""total_duration"": 1500000000,
                ""load_duration"": 100000000,
                ""prompt_eval_count"": 25,
                ""prompt_eval_duration"": 200000000,
                ""eval_count"": 15,
                ""eval_duration"": 300000000
            }";
        }

        /// <summary>
        /// Creates a mock HTTP response for streaming completion
        /// </summary>
        public static IEnumerable<string> CreateMockStreamingResponse()
        {
            yield return @"{""model"": ""codellama"", ""response"": ""Console"", ""done"": false}";
            yield return @"{""model"": ""codellama"", ""response"": "".WriteLine("", ""done"": false}";
            yield return @"{""model"": ""codellama"", ""response"": ""\""Hello, World!\"");"", ""done"": true}";
        }

        /// <summary>
        /// Creates a mock error response
        /// </summary>
        public static string CreateMockErrorResponse()
        {
            return @"{
                ""error"": ""model not found"",
                ""details"": ""The specified model 'nonexistent' was not found""
            }";
        }

        #endregion
    }

    /// <summary>
    /// Test scenario builder for creating complex test setups
    /// </summary>
    public class TestScenarioBuilder
    {
        private Mock<ISettingsService> _mockSettingsService;
        private Mock<ILogger> _mockLogger;
        private Mock<ErrorHandler> _mockErrorHandler;
        private Mock<IOllamaHttpClient> _mockHttpClient;

        public TestScenarioBuilder WithMockSettingsService(Action<Mock<ISettingsService>> configure = null)
        {
            _mockSettingsService = MockFactory.CreateMockSettingsService();
            configure?.Invoke(_mockSettingsService);
            return this;
        }

        public TestScenarioBuilder WithMockLogger(Action<Mock<ILogger>> configure = null)
        {
            _mockLogger = MockFactory.CreateMockLogger();
            configure?.Invoke(_mockLogger);
            return this;
        }

        public TestScenarioBuilder WithMockErrorHandler(Action<Mock<ErrorHandler>> configure = null)
        {
            _mockErrorHandler = MockFactory.CreateMockErrorHandler();
            configure?.Invoke(_mockErrorHandler);
            return this;
        }

        public TestScenarioBuilder WithMockHttpClient(Action<Mock<IOllamaHttpClient>> configure = null)
        {
            _mockHttpClient = MockFactory.CreateMockOllamaHttpClient();
            configure?.Invoke(_mockHttpClient);
            return this;
        }

        public TestScenario Build()
        {
            return new TestScenario
            {
                MockSettingsService = _mockSettingsService ?? MockFactory.CreateMockSettingsService(),
                MockLogger = _mockLogger ?? MockFactory.CreateMockLogger(),
                MockErrorHandler = _mockErrorHandler ?? MockFactory.CreateMockErrorHandler(),
                MockHttpClient = _mockHttpClient ?? MockFactory.CreateMockOllamaHttpClient()
            };
        }
    }

    /// <summary>
    /// Container for test scenario mocks
    /// </summary>
    public class TestScenario
    {
        public Mock<ISettingsService> MockSettingsService { get; set; }
        public Mock<ILogger> MockLogger { get; set; }
        public Mock<ErrorHandler> MockErrorHandler { get; set; }
        public Mock<IOllamaHttpClient> MockHttpClient { get; set; }
    }

    /// <summary>
    /// Extension methods for common test assertions
    /// </summary>
    public static class TestExtensions
    {
        /// <summary>
        /// Verifies that a code suggestion is valid
        /// </summary>
        public static void ShouldBeValidCodeSuggestion(this CodeSuggestion suggestion)
        {
            suggestion.Should().NotBeNull();
            suggestion.Id.Should().NotBeNullOrEmpty();
            suggestion.Text.Should().NotBeNull();
            suggestion.Confidence.Should().BeInRange(0.0, 1.0);
            suggestion.Type.Should().BeDefined();
        }

        /// <summary>
        /// Verifies that a jump recommendation is valid
        /// </summary>
        public static void ShouldBeValidJumpRecommendation(this JumpRecommendation recommendation)
        {
            recommendation.Should().NotBeNull();
            recommendation.Direction.Should().BeDefined();
            recommendation.Confidence.Should().BeInRange(0.0, 1.0);
            
            if (recommendation.Direction != JumpDirection.None)
            {
                recommendation.TargetLine.Should().BeGreaterThan(0);
                recommendation.TargetColumn.Should().BeGreaterOrEqualTo(0);
            }
        }

        /// <summary>
        /// Verifies that a code context is valid
        /// </summary>
        public static void ShouldBeValidCodeContext(this CodeContext context)
        {
            context.Should().NotBeNull();
            context.FilePath.Should().NotBeNullOrEmpty();
            context.Language.Should().NotBeNullOrEmpty();
            context.LineNumber.Should().BeGreaterThan(0);
            context.ColumnNumber.Should().BeGreaterOrEqualTo(0);
        }
    }
}