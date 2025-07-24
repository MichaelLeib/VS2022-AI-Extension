using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using OllamaAssistant.Infrastructure;
using OllamaAssistant.Models;
using OllamaAssistant.Services.Implementation;
using OllamaAssistant.Services.Interfaces;

namespace OllamaAssistant.Tests.TestUtilities
{
    /// <summary>
    /// Factory for creating mock objects for testing
    /// </summary>
    public static class MockFactory
    {
        #region Service Mocks

        /// <summary>
        /// Creates a mock ISettingsService with default values
        /// </summary>
        public static Mock<ISettingsService> CreateMockSettingsService()
        {
            var mock = new Mock<ISettingsService>();
            
            // Set up default property values
            mock.SetupProperty(x => x.IsEnabled, true);
            mock.SetupProperty(x => x.OllamaEndpoint, "http://localhost:11434");
            mock.SetupProperty(x => x.OllamaModel, "codellama");
            mock.SetupProperty(x => x.SurroundingLinesUp, 3);
            mock.SetupProperty(x => x.SurroundingLinesDown, 2);
            mock.SetupProperty(x => x.CodePredictionEnabled, true);
            mock.SetupProperty(x => x.JumpRecommendationsEnabled, true);
            mock.SetupProperty(x => x.CursorHistoryMemoryDepth, 5);
            mock.SetupProperty(x => x.OllamaTimeout, 30000);
            mock.SetupProperty(x => x.MinimumConfidenceThreshold, 0.7);
            mock.SetupProperty(x => x.TypingDebounceDelay, 500);
            mock.SetupProperty(x => x.ShowConfidenceScores, false);
            mock.SetupProperty(x => x.EnableVerboseLogging, false);
            mock.SetupProperty(x => x.IncludeCursorHistory, true);
            mock.SetupProperty(x => x.MaxSuggestions, 5);
            mock.SetupProperty(x => x.EnableStreamingCompletions, false);
            mock.SetupProperty(x => x.FilterSensitiveData, true);
            mock.SetupProperty(x => x.MaxRequestSizeKB, 512);
            mock.SetupProperty(x => x.MaxConcurrentRequests, 3);
            mock.SetupProperty(x => x.MaxRetryAttempts, 3);

            // Set up methods
            mock.Setup(x => x.ValidateSettings()).Returns(true);
            mock.Setup(x => x.LoadSettings());
            mock.Setup(x => x.SaveSettings());
            mock.Setup(x => x.ResetToDefaults());

            return mock;
        }

        /// <summary>
        /// Creates a mock ILogger
        /// </summary>
        public static Mock<ILogger> CreateMockLogger()
        {
            var mock = new Mock<ILogger>();
            
            mock.Setup(x => x.LogInfoAsync(It.IsAny<string>(), It.IsAny<string>()))
                .Returns(Task.CompletedTask);
            mock.Setup(x => x.LogDebugAsync(It.IsAny<string>(), It.IsAny<string>()))
                .Returns(Task.CompletedTask);
            mock.Setup(x => x.LogWarningAsync(It.IsAny<string>(), It.IsAny<string>()))
                .Returns(Task.CompletedTask);
            mock.Setup(x => x.LogErrorAsync(It.IsAny<Exception>(), It.IsAny<string>(), It.IsAny<string>()))
                .Returns(Task.CompletedTask);
            mock.Setup(x => x.LogPerformanceAsync(It.IsAny<string>(), It.IsAny<TimeSpan>()))
                .Returns(Task.CompletedTask);

            return mock;
        }

        /// <summary>
        /// Creates a mock ICursorHistoryService
        /// </summary>
        public static Mock<ICursorHistoryService> CreateMockCursorHistoryService()
        {
            var mock = new Mock<ICursorHistoryService>();
            var testHistory = new List<CursorHistoryEntry>();

            mock.Setup(x => x.RecordCursorPosition(It.IsAny<CursorHistoryEntry>()))
                .Callback<CursorHistoryEntry>(entry => testHistory.Add(entry));

            mock.Setup(x => x.GetRecentHistory(It.IsAny<int>()))
                .Returns<int>(count => testHistory.GetRange(Math.Max(0, testHistory.Count - count), Math.Min(count, testHistory.Count)));

            mock.Setup(x => x.GetFileHistory(It.IsAny<string>(), It.IsAny<int>()))
                .Returns<string, int>((filePath, count) => 
                    testHistory.FindAll(h => h.FilePath == filePath)
                              .GetRange(0, Math.Min(count, testHistory.Count)));

            mock.Setup(x => x.ClearHistory());
            mock.Setup(x => x.ClearHistoryForFileAsync(It.IsAny<string>()))
                .Callback<string>(filePath => testHistory.RemoveAll(h => h.FilePath == filePath))
                .Returns(Task.CompletedTask);

            mock.Setup(x => x.SetMaxHistoryDepth(It.IsAny<int>()));

            return mock;
        }

        /// <summary>
        /// Creates a mock IOllamaService
        /// </summary>
        public static Mock<IOllamaService> CreateMockOllamaService()
        {
            var mock = new Mock<IOllamaService>();

            mock.Setup(x => x.GetCodeSuggestionAsync(It.IsAny<CodeContext>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(CreateTestCodeSuggestion());

            mock.Setup(x => x.GetCompletionAsync(It.IsAny<string>(), It.IsAny<string>(), 
                It.IsAny<List<CursorHistoryEntry>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync("Test completion result");

            mock.Setup(x => x.IsAvailableAsync())
                .ReturnsAsync(true);

            mock.Setup(x => x.GetHealthStatusAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new OllamaHealthStatus 
                { 
                    IsAvailable = true, 
                    ResponseTimeMs = 100, 
                    Timestamp = DateTime.UtcNow 
                });

            mock.Setup(x => x.GetModelInfoAsync())
                .ReturnsAsync(new ModelInfo 
                { 
                    Name = "codellama", 
                    Version = "1.0", 
                    SupportsCodeCompletion = true 
                });

            return mock;
        }

        /// <summary>
        /// Creates a mock IContextCaptureService
        /// </summary>
        public static Mock<IContextCaptureService> CreateMockContextCaptureService()
        {
            var mock = new Mock<IContextCaptureService>();

            mock.Setup(x => x.CaptureContextAsync(It.IsAny<object>(), It.IsAny<int>(), It.IsAny<int>()))
                .ReturnsAsync(CreateTestCodeContext());

            mock.Setup(x => x.GetContextSnippetAsync(It.IsAny<int>(), It.IsAny<int>()))
                .ReturnsAsync("Test context snippet");

            return mock;
        }

        /// <summary>
        /// Creates a mock ErrorHandler
        /// </summary>
        public static Mock<ErrorHandler> CreateMockErrorHandler()
        {
            var mock = new Mock<ErrorHandler>();

            mock.Setup(x => x.ExecuteWithErrorHandlingAsync(It.IsAny<Func<Task>>(), It.IsAny<string>()))
                .Returns<Func<Task>, string>(async (action, context) => await action());

            mock.Setup(x => x.ExecuteWithErrorHandlingAsync(It.IsAny<Func<Task<object>>>(), It.IsAny<string>()))
                .Returns<Func<Task<object>>, string>(async (action, context) => await action());

            mock.Setup(x => x.PerformHealthCheckAsync())
                .ReturnsAsync(new HealthCheckResult { IsHealthy = true });

            return mock;
        }

        /// <summary>
        /// Creates a mock IServiceProvider for VS services
        /// </summary>
        public static Mock<IServiceProvider> CreateMockServiceProvider()
        {
            var mock = new Mock<IServiceProvider>();
            
            // Set up VS service providers to return mock implementations
            mock.Setup(x => x.GetService(typeof(Microsoft.VisualStudio.Shell.Interop.IVsOutputWindow)))
                .Returns(CreateMockOutputWindow().Object);
            mock.Setup(x => x.GetService(typeof(Microsoft.VisualStudio.Shell.Interop.IVsStatusbar)))
                .Returns(CreateMockStatusBar().Object);
            mock.Setup(x => x.GetService(typeof(Microsoft.VisualStudio.Shell.Interop.SVsRunningDocumentTable)))
                .Returns(CreateMockRunningDocumentTable().Object);

            return mock;
        }

        /// <summary>
        /// Creates a mock IVsOutputWindow
        /// </summary>
        public static Mock<Microsoft.VisualStudio.Shell.Interop.IVsOutputWindow> CreateMockOutputWindow()
        {
            var mock = new Mock<Microsoft.VisualStudio.Shell.Interop.IVsOutputWindow>();
            var mockPane = new Mock<Microsoft.VisualStudio.Shell.Interop.IVsOutputWindowPane>();
            
            // Setup the output window to return a mock pane
            mock.Setup(x => x.CreatePane(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>()))
                .Returns(Microsoft.VisualStudio.VSConstants.S_OK);
            mock.Setup(x => x.GetPane(It.IsAny<Guid>(), out It.Ref<Microsoft.VisualStudio.Shell.Interop.IVsOutputWindowPane>.IsAny))
                .Returns(Microsoft.VisualStudio.VSConstants.S_OK);

            return mock;
        }

        /// <summary>
        /// Creates a mock IVsStatusbar
        /// </summary>
        public static Mock<Microsoft.VisualStudio.Shell.Interop.IVsStatusbar> CreateMockStatusBar()
        {
            var mock = new Mock<Microsoft.VisualStudio.Shell.Interop.IVsStatusbar>();
            
            mock.Setup(x => x.SetText(It.IsAny<string>()))
                .Returns(Microsoft.VisualStudio.VSConstants.S_OK);
            mock.Setup(x => x.Animation(It.IsAny<int>(), It.IsAny<object>()))
                .Returns(Microsoft.VisualStudio.VSConstants.S_OK);
            mock.Setup(x => x.Clear())
                .Returns(Microsoft.VisualStudio.VSConstants.S_OK);

            return mock;
        }

        /// <summary>
        /// Creates a mock SVsRunningDocumentTable
        /// </summary>
        public static Mock<Microsoft.VisualStudio.Shell.Interop.SVsRunningDocumentTable> CreateMockRunningDocumentTable()
        {
            var mock = new Mock<Microsoft.VisualStudio.Shell.Interop.SVsRunningDocumentTable>();
            
            // Setup basic RDT operations
            mock.Setup(x => x.GetHashCode()).Returns(42);

            return mock;
        }

        /// <summary>
        /// Creates a mock service provider that simulates corrupted settings store
        /// </summary>
        public static Mock<IServiceProvider> CreateCorruptedServiceProvider()
        {
            var mock = new Mock<IServiceProvider>();
            
            // Make all service requests throw exceptions to simulate corruption
            mock.Setup(x => x.GetService(It.IsAny<Type>()))
                .Throws(new System.ComponentModel.Win32Exception("Settings store is corrupted"));

            return mock;
        }

        /// <summary>
        /// Creates a mock service provider that simulates inaccessible settings store
        /// </summary>
        public static Mock<IServiceProvider> CreateInaccessibleServiceProvider()
        {
            var mock = new Mock<IServiceProvider>();
            
            // Make all service requests return null to simulate inaccessible store
            mock.Setup(x => x.GetService(It.IsAny<Type>()))
                .Returns((object)null);

            return mock;
        }

        #endregion

        #region Test Data Creators

        /// <summary>
        /// Creates a test CodeContext
        /// </summary>
        public static CodeContext CreateTestCodeContext()
        {
            return new CodeContext
            {
                FileName = "TestFile.cs",
                Language = "csharp",
                LineNumber = 10,
                CaretPosition = 15,
                CurrentLine = "    public void TestMethod()",
                PrecedingLines = new[]
                {
                    "using System;",
                    "namespace TestNamespace",
                    "{",
                    "    public class TestClass",
                    "    {"
                },
                FollowingLines = new[]
                {
                    "    {",
                    "        // TODO: Implement",
                    "    }",
                    "}"
                },
                CurrentScope = "TestClass.TestMethod",
                Indentation = new IndentationInfo { UsesSpaces = true, Size = 4 },
                CursorHistory = new List<CursorHistoryEntry>
                {
                    CreateTestCursorHistoryEntry()
                }
            };
        }

        /// <summary>
        /// Creates a test CodeSuggestion
        /// </summary>
        public static CodeSuggestion CreateTestCodeSuggestion()
        {
            return new CodeSuggestion
            {
                Text = "Console.WriteLine(\"Hello World\");",
                InsertionPoint = 15,
                Confidence = 0.85,
                Type = SuggestionType.Method,
                Description = "Test code suggestion",
                Priority = 85,
                SourceContext = "TestClass.TestMethod"
            };
        }

        /// <summary>
        /// Creates a test CursorHistoryEntry
        /// </summary>
        public static CursorHistoryEntry CreateTestCursorHistoryEntry()
        {
            return new CursorHistoryEntry
            {
                FilePath = "TestFile.cs",
                LineNumber = 8,
                ColumnNumber = 5,
                Timestamp = DateTime.Now.AddMinutes(-5),
                Context = "Previous cursor position",
                ChangeType = "Navigation"
            };
        }

        /// <summary>
        /// Creates a test JumpRecommendation
        /// </summary>
        public static JumpRecommendation CreateTestJumpRecommendation()
        {
            return new JumpRecommendation
            {
                TargetLine = 15,
                TargetColumn = 8,
                Direction = JumpDirection.Down,
                Reason = "Test jump recommendation",
                Confidence = 0.9,
                Type = JumpType.NextLogicalPosition,
                TargetPreview = "    return result;"
            };
        }

        /// <summary>
        /// Creates multiple test CursorHistoryEntries
        /// </summary>
        public static List<CursorHistoryEntry> CreateTestCursorHistory(int count = 5)
        {
            var history = new List<CursorHistoryEntry>();
            for (int i = 0; i < count; i++)
            {
                history.Add(new CursorHistoryEntry
                {
                    FilePath = $"TestFile{i % 3}.cs",
                    LineNumber = 10 + i,
                    ColumnNumber = 5 + (i % 10),
                    Timestamp = DateTime.Now.AddMinutes(-i * 2),
                    Context = $"Test context {i}",
                    ChangeType = i % 2 == 0 ? "Edit" : "Navigation"
                });
            }
            return history;
        }

        /// <summary>
        /// Creates test configuration for HTTP client
        /// </summary>
        public static OllamaHttpClientConfig CreateTestHttpClientConfig()
        {
            return new OllamaHttpClientConfig
            {
                TimeoutMs = 10000,
                HealthCheckTimeoutMs = 2000,
                MaxConcurrentRequests = 2,
                MaxConnectionsPerServer = 5,
                MaxRetryAttempts = 2,
                BaseRetryDelayMs = 500,
                MaxRetryDelayMs = 5000,
                RetryBackoffMultiplier = 1.5
            };
        }

        #endregion

        #region Assertion Helpers

        /// <summary>
        /// Verifies that a mock method was called with specific parameters
        /// </summary>
        public static void VerifyMethodCall<T>(Mock<T> mock, string methodName, int expectedCallCount = 1) 
            where T : class
        {
            // This would be implemented with specific verification logic
            // For now, it's a placeholder for custom verification methods
        }

        /// <summary>
        /// Creates a test scenario builder for complex test setups
        /// </summary>
        public static TestScenarioBuilder CreateTestScenario()
        {
            return new TestScenarioBuilder();
        }

        #endregion
    }

    /// <summary>
    /// Builder for creating complex test scenarios
    /// </summary>
    public class TestScenarioBuilder
    {
        private readonly Dictionary<Type, object> _mocks = new Dictionary<Type, object>();
        private CodeContext _codeContext;
        private List<CursorHistoryEntry> _cursorHistory;

        public TestScenarioBuilder WithMockSettingsService(Action<Mock<ISettingsService>> configure = null)
        {
            var mock = MockFactory.CreateMockSettingsService();
            configure?.Invoke(mock);
            _mocks[typeof(ISettingsService)] = mock;
            return this;
        }

        public TestScenarioBuilder WithMockLogger(Action<Mock<ILogger>> configure = null)
        {
            var mock = MockFactory.CreateMockLogger();
            configure?.Invoke(mock);
            _mocks[typeof(ILogger)] = mock;
            return this;
        }

        public TestScenarioBuilder WithCodeContext(CodeContext context)
        {
            _codeContext = context;
            return this;
        }

        public TestScenarioBuilder WithCursorHistory(List<CursorHistoryEntry> history)
        {
            _cursorHistory = history;
            return this;
        }

        public TestScenario Build()
        {
            return new TestScenario
            {
                Mocks = _mocks,
                CodeContext = _codeContext ?? MockFactory.CreateTestCodeContext(),
                CursorHistory = _cursorHistory ?? MockFactory.CreateTestCursorHistory()
            };
        }
    }

    /// <summary>
    /// Represents a complete test scenario with all necessary mocks and data
    /// </summary>
    public class TestScenario
    {
        public Dictionary<Type, object> Mocks { get; set; } = new Dictionary<Type, object>();
        public CodeContext CodeContext { get; set; }
        public List<CursorHistoryEntry> CursorHistory { get; set; }

        public Mock<T> GetMock<T>() where T : class
        {
            return (Mock<T>)Mocks[typeof(T)];
        }

        public T GetService<T>() where T : class
        {
            return GetMock<T>().Object;
        }
    }
}