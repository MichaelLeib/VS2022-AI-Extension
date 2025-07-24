using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using OllamaAssistant.Infrastructure;
using OllamaAssistant.Models;
using OllamaAssistant.Services.Interfaces;

namespace OllamaAssistant.Tests.TestHelpers
{
    /// <summary>
    /// Factory for creating mock services for testing
    /// </summary>
    public static class MockServices
    {
        /// <summary>
        /// Create a mock settings service with default values
        /// </summary>
        public static Mock<ISettingsService> CreateMockSettingsService()
        {
            var mock = new Mock<ISettingsService>();
            
            // Set up default property values
            mock.SetupProperty(x => x.IsEnabled, true);
            mock.SetupProperty(x => x.OllamaEndpoint, "http://localhost:11434");
            mock.SetupProperty(x => x.DefaultModel, "codellama");
            mock.SetupProperty(x => x.SurroundingLinesUp, 3);
            mock.SetupProperty(x => x.SurroundingLinesDown, 2);
            mock.SetupProperty(x => x.CursorHistoryMemoryDepth, 5);
            mock.SetupProperty(x => x.EnableAutoSuggestions, true);
            mock.SetupProperty(x => x.EnableJumpRecommendations, true);
            mock.SetupProperty(x => x.EnableVerboseLogging, false);
            mock.SetupProperty(x => x.RequestTimeout, 30000);
            mock.SetupProperty(x => x.JumpKeyBinding, "Tab");
            mock.SetupProperty(x => x.JumpNotificationTimeout, 5000);
            mock.SetupProperty(x => x.ShowJumpPreview, true);
            mock.SetupProperty(x => x.NotificationOpacity, 80);
            mock.SetupProperty(x => x.AnimateNotifications, true);

            // Set up methods
            mock.Setup(x => x.LoadSettingsAsync()).Returns(Task.CompletedTask);
            mock.Setup(x => x.SaveSettingsAsync()).Returns(Task.CompletedTask);
            mock.Setup(x => x.ResetToDefaultsAsync()).Returns(Task.CompletedTask);
            mock.Setup(x => x.ValidateSettings()).Returns(true);

            return mock;
        }

        /// <summary>
        /// Create a mock logger service
        /// </summary>
        public static Mock<ILogger> CreateMockLogger()
        {
            var mock = new Mock<ILogger>();
            
            mock.Setup(x => x.LogDebugAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<object>()))
                .Returns(Task.CompletedTask);
            mock.Setup(x => x.LogInfoAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<object>()))
                .Returns(Task.CompletedTask);
            mock.Setup(x => x.LogWarningAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<object>()))
                .Returns(Task.CompletedTask);
            mock.Setup(x => x.LogErrorAsync(It.IsAny<Exception>(), It.IsAny<string>(), It.IsAny<object>()))
                .Returns(Task.CompletedTask);
            mock.Setup(x => x.LogErrorAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<object>()))
                .Returns(Task.CompletedTask);
            mock.Setup(x => x.LogPerformanceAsync(It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<string>()))
                .Returns(Task.CompletedTask);
            mock.Setup(x => x.GetRecentLogsAsync(It.IsAny<int>()))
                .ReturnsAsync(new List<LogEntry>());
            mock.Setup(x => x.ClearLogsAsync())
                .Returns(Task.CompletedTask);

            return mock;
        }

        /// <summary>
        /// Create a mock cursor history service
        /// </summary>
        public static Mock<ICursorHistoryService> CreateMockCursorHistoryService()
        {
            var mock = new Mock<ICursorHistoryService>();
            
            mock.SetupProperty(x => x.MemoryDepth, 5);
            mock.SetupProperty(x => x.CurrentHistoryCount, 0);
            
            mock.Setup(x => x.AddEntryAsync(It.IsAny<CursorHistoryEntry>()))
                .Returns(Task.CompletedTask);
            mock.Setup(x => x.GetHistoryAsync(It.IsAny<int>()))
                .ReturnsAsync(new List<CursorHistoryEntry>());
            mock.Setup(x => x.GetRelevantHistoryAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>()))
                .ReturnsAsync(new List<CursorHistoryEntry>());
            mock.Setup(x => x.ClearHistoryAsync())
                .Returns(Task.CompletedTask);

            return mock;
        }

        /// <summary>
        /// Create a mock text view service
        /// </summary>
        public static Mock<ITextViewService> CreateMockTextViewService()
        {
            var mock = new Mock<ITextViewService>();
            
            mock.Setup(x => x.GetCurrentFilePath()).Returns("C:\\TestFile.cs");
            mock.Setup(x => x.GetCurrentLineNumber()).Returns(10);
            mock.Setup(x => x.GetCurrentColumn()).Returns(5);
            mock.Setup(x => x.GetCurrentPosition()).Returns(new CursorPosition 
            { 
                FilePath = "C:\\TestFile.cs", 
                Line = 10, 
                Column = 5 
            });
            mock.Setup(x => x.GetSurroundingTextAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>()))
                .ReturnsAsync("public class TestClass\n{\n    public void TestMethod()\n    {\n        // cursor here\n    }\n}");
            mock.Setup(x => x.InsertTextAsync(It.IsAny<string>()))
                .Returns(Task.CompletedTask);
            mock.Setup(x => x.MoveCaretToAsync(It.IsAny<int>(), It.IsAny<int>()))
                .Returns(Task.CompletedTask);

            return mock;
        }

        /// <summary>
        /// Create a mock context capture service
        /// </summary>
        public static Mock<IContextCaptureService> CreateMockContextCaptureService()
        {
            var mock = new Mock<IContextCaptureService>();
            
            mock.Setup(x => x.CaptureContextAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>()))
                .ReturnsAsync(new CodeContext
                {
                    FilePath = "C:\\TestFile.cs",
                    CaretLine = 10,
                    CaretColumn = 5,
                    LanguageId = "csharp",
                    SurroundingText = "public class TestClass\n{\n    public void TestMethod()\n    {\n        // cursor here\n    }\n}",
                    ProjectContext = "TestProject.csproj",
                    CursorHistory = new List<CursorHistoryEntry>()
                });
            
            mock.Setup(x => x.GetContextSnippetAsync(It.IsAny<int>(), It.IsAny<int>()))
                .ReturnsAsync("// cursor here");

            return mock;
        }

        /// <summary>
        /// Create a mock Ollama service
        /// </summary>
        public static Mock<IOllamaService> CreateMockOllamaService()
        {
            var mock = new Mock<IOllamaService>();
            
            mock.SetupProperty(x => x.Endpoint, "http://localhost:11434");
            
            mock.Setup(x => x.IsAvailableAsync()).ReturnsAsync(true);
            mock.Setup(x => x.GetModelsAsync()).ReturnsAsync(new[] { "codellama", "llama2" });
            mock.Setup(x => x.GetCompletionAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<List<CursorHistoryEntry>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync("Console.WriteLine(\"Hello, World!\");");
            mock.Setup(x => x.GetCodeSuggestionAsync(It.IsAny<CodeContext>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new CodeSuggestion
                {
                    CompletionText = "Console.WriteLine(\"Hello, World!\");",
                    DisplayText = "Console.WriteLine",
                    Description = "Write Hello World to console",
                    Confidence = 0.85,
                    StartPosition = 5,
                    EndPosition = 5,
                    ProcessingTime = 500
                });

            return mock;
        }

        /// <summary>
        /// Create a mock suggestion engine
        /// </summary>
        public static Mock<ISuggestionEngine> CreateMockSuggestionEngine()
        {
            var mock = new Mock<ISuggestionEngine>();
            
            mock.Setup(x => x.ProcessSuggestionAsync(It.IsAny<CodeSuggestion>(), It.IsAny<CodeContext>()))
                .ReturnsAsync(new List<CodeSuggestion>
                {
                    new CodeSuggestion
                    {
                        CompletionText = "Console.WriteLine(\"Hello, World!\");",
                        DisplayText = "Console.WriteLine",
                        Description = "Write Hello World to console",
                        Confidence = 0.85,
                        StartPosition = 5,
                        EndPosition = 5,
                        ProcessingTime = 500
                    }
                });
            
            mock.Setup(x => x.AnalyzeJumpOpportunitiesAsync(It.IsAny<CodeContext>()))
                .ReturnsAsync(new List<JumpRecommendation>
                {
                    new JumpRecommendation
                    {
                        Direction = JumpDirection.Down,
                        TargetLine = 15,
                        TargetColumn = 8,
                        Confidence = 0.75,
                        Reason = "Related method implementation",
                        TargetPreview = "public void RelatedMethod()",
                        IsCrossFile = false
                    }
                });

            return mock;
        }

        /// <summary>
        /// Create a mock error handler
        /// </summary>
        public static Mock<ErrorHandler> CreateMockErrorHandler(Mock<ILogger> mockLogger = null, Mock<ISettingsService> mockSettings = null)
        {
            var logger = mockLogger?.Object ?? CreateMockLogger().Object;
            var settings = mockSettings?.Object ?? CreateMockSettingsService().Object;
            
            var mock = new Mock<ErrorHandler>(logger, settings);
            
            mock.Setup(x => x.HandleExceptionAsync(It.IsAny<Exception>(), It.IsAny<string>(), It.IsAny<object>()))
                .ReturnsAsync(true);
            mock.Setup(x => x.HandleExceptionWithUserFeedbackAsync(It.IsAny<Exception>(), It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync(true);
            mock.Setup(x => x.ExecuteWithErrorHandlingAsync(It.IsAny<Func<Task<object>>>(), It.IsAny<string>(), It.IsAny<object>()))
                .Returns<Func<Task<object>>, string, object>((func, context, defaultValue) => func());
            mock.Setup(x => x.ExecuteWithErrorHandlingAsync(It.IsAny<Func<Task>>(), It.IsAny<string>()))
                .Returns<Func<Task>, string>((func, context) => func());
            mock.Setup(x => x.PerformHealthCheckAsync())
                .ReturnsAsync(new HealthCheckResult
                {
                    IsHealthy = true,
                    OllamaConnectivity = true,
                    SettingsValid = true,
                    MemoryUsage = 50 * 1024 * 1024, // 50MB
                    PerformanceMetrics = new PerformanceMetrics
                    {
                        AverageResponseTime = TimeSpan.FromMilliseconds(500),
                        SuccessRate = 0.95,
                        ErrorCount = 2
                    }
                });

            return mock;
        }
    }
}