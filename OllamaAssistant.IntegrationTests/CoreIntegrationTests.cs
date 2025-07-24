using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OllamaAssistant.Models;
using OllamaAssistant.Services.Implementation;
using OllamaAssistant.Services.Interfaces;
using OllamaAssistant.Tests.TestUtilities;

namespace OllamaAssistant.IntegrationTests
{
    /// <summary>
    /// Core integration tests for validating end-to-end Ollama integration
    /// </summary>
    [TestClass]
    [TestCategory(TestCategories.Integration)]
    public class CoreIntegrationTests : BaseIntegrationTest
    {
        private IOllamaService _ollamaService;
        private IContextCaptureService _contextCaptureService;
        private ICursorHistoryService _cursorHistoryService;
        private ISettingsService _settingsService;

        [TestInitialize]
        public async Task TestInitialize()
        {
            // Initialize real services for integration testing
            _settingsService = new SettingsService();
            await _settingsService.LoadSettings();

            _cursorHistoryService = new CursorHistoryService(_settingsService);
            _contextCaptureService = new ContextCaptureService(_settingsService, null);
            _ollamaService = new OllamaService(_settingsService, null, null);

            // Ensure test environment is ready
            await EnsureOllamaServerIsAvailable();
        }

        [TestCleanup]
        public void TestCleanup()
        {
            _ollamaService?.Dispose();
            _contextCaptureService?.Dispose();
            _cursorHistoryService?.Dispose();
        }

        #region Core Integration Tests

        [TestMethod]
        [IntegrationTest(RequiredService = "Ollama Server")]
        public async Task EndToEndCodeCompletion_WithRealServer_ShouldProvideValidSuggestions()
        {
            // Arrange
            var codeContext = new CodeContext
            {
                FilePath = @"C:\TestProject\Program.cs",
                Language = "csharp",
                Code = @"using System;

namespace TestProject
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine(""Hello, World!"");
            // Add more code here:
            ",
                CurrentLine = "            ",
                LineNumber = 10,
                ColumnNumber = 12,
                CursorPosition = new CursorPosition
                {
                    Line = 10,
                    Column = 12,
                    FilePath = @"C:\TestProject\Program.cs",
                    Timestamp = DateTime.UtcNow
                }
            };

            // Act
            var suggestion = await _ollamaService.GetCodeSuggestionAsync(codeContext, CancellationToken.None);

            // Assert
            suggestion.Should().NotBeNull();
            suggestion.Text.Should().NotBeNullOrWhiteSpace();
            suggestion.Confidence.Should().BeGreaterThan(0);
            suggestion.Type.Should().NotBe(SuggestionType.None);
            suggestion.ModelUsed.Should().NotBeNullOrEmpty();
            
            WriteTestOutput($"Received suggestion: {suggestion.Text}");
            WriteTestOutput($"Confidence: {suggestion.Confidence:F2}");
            WriteTestOutput($"Model used: {suggestion.ModelUsed}");
        }

        [TestMethod]
        [IntegrationTest(RequiredService = "Ollama Server")]
        public async Task ContextCapture_WithRealFile_ShouldProvideRichContext()
        {
            // Arrange
            var filePath = @"C:\TestProject\TestClass.cs";
            var line = 15;
            var column = 8;

            // Act
            var context = await _contextCaptureService.CaptureContextAsync(filePath, line, column);

            // Assert
            context.Should().NotBeNull();
            context.FilePath.Should().Be(filePath);
            context.Language.Should().NotBeEmpty();
            context.LineNumber.Should().Be(line);
            context.ColumnNumber.Should().Be(column);
            
            WriteTestOutput($"Captured context language: {context.Language}");
            WriteTestOutput($"Context code length: {context.Code?.Length ?? 0} characters");
        }

        [TestMethod]
        [IntegrationTest(RequiredService = "Ollama Server")]
        public async Task JumpRecommendation_WithRealCode_ShouldProvideValidRecommendations()
        {
            // Arrange
            var codeContext = new CodeContext
            {
                FilePath = @"C:\TestProject\Calculator.cs",
                Language = "csharp",
                Code = @"public class Calculator
{
    public int Add(int a, int b)
    {
        return a + b; // <- Current cursor position
    }

    public int Multiply(int a, int b)
    {
        return a * b; // Jump target
    }
}",
                CurrentLine = "        return a + b; // <- Current cursor position",
                LineNumber = 5,
                ColumnNumber = 20
            };

            // Act
            var jumpRecommendation = await _ollamaService.GetJumpRecommendationAsync(codeContext, CancellationToken.None);

            // Assert
            jumpRecommendation.Should().NotBeNull();
            jumpRecommendation.Direction.Should().NotBe(JumpDirection.None);
            jumpRecommendation.Confidence.Should().BeGreaterThan(0);
            
            if (jumpRecommendation.Direction != JumpDirection.None)
            {
                jumpRecommendation.TargetLine.Should().BeGreaterThan(0);
                jumpRecommendation.TargetColumn.Should().BeGreaterOrEqualTo(0);
            }
            
            WriteTestOutput($"Jump recommendation: {jumpRecommendation.Direction}");
            WriteTestOutput($"Target: Line {jumpRecommendation.TargetLine}, Column {jumpRecommendation.TargetColumn}");
            WriteTestOutput($"Reason: {jumpRecommendation.Reason}");
        }

        [TestMethod]
        [IntegrationTest(RequiredService = "Ollama Server")]
        public async Task CursorHistoryIntegration_WithMultipleEntries_ShouldTrackAccurately()
        {
            // Arrange
            var positions = new[]
            {
                new CursorPosition { Line = 1, Column = 1, FilePath = "File1.cs", Timestamp = DateTime.UtcNow.AddMinutes(-5) },
                new CursorPosition { Line = 10, Column = 5, FilePath = "File1.cs", Timestamp = DateTime.UtcNow.AddMinutes(-4) },
                new CursorPosition { Line = 5, Column = 3, FilePath = "File2.cs", Timestamp = DateTime.UtcNow.AddMinutes(-3) },
                new CursorPosition { Line = 15, Column = 8, FilePath = "File1.cs", Timestamp = DateTime.UtcNow.AddMinutes(-2) }
            };

            // Act
            foreach (var position in positions)
            {
                _cursorHistoryService.AddCursorPosition(position);
            }

            var recentHistory = _cursorHistoryService.GetRecentHistory(10);
            var file1History = _cursorHistoryService.GetHistoryForFile("File1.cs", 10);

            // Assert
            recentHistory.Should().HaveCount(4);
            recentHistory[0].Line.Should().Be(15); // Most recent first
            
            file1History.Should().HaveCount(3);
            file1History.Should().OnlyContain(p => p.FilePath == "File1.cs");
            
            WriteTestOutput($"Total history entries: {recentHistory.Count}");
            WriteTestOutput($"File1.cs specific entries: {file1History.Count}");
        }

        [TestMethod]
        [IntegrationTest(RequiredService = "Ollama Server")]
        public async Task StreamingCodeCompletion_WithRealServer_ShouldStreamResults()
        {
            // Arrange
            var codeContext = new CodeContext
            {
                FilePath = @"C:\TestProject\StreamTest.cs",
                Language = "csharp",
                Code = "public class StreamTest { public void Method() { ",
                CurrentLine = "public void Method() { ",
                LineNumber = 1,
                ColumnNumber = 25
            };

            var receivedResults = new List<CodeSuggestion>();
            var startTime = DateTime.UtcNow;

            // Act
            await foreach (var suggestion in _ollamaService.GetStreamingCodeSuggestionAsync(codeContext, CancellationToken.None))
            {
                receivedResults.Add(suggestion);
                WriteTestOutput($"Received streaming result {receivedResults.Count}: {suggestion.Text}");
                
                // Limit test duration
                if (DateTime.UtcNow - startTime > TimeSpan.FromSeconds(10))
                    break;
            }

            // Assert
            receivedResults.Should().NotBeEmpty();
            receivedResults.Should().OnlyContain(s => s != null);
            
            WriteTestOutput($"Total streaming results received: {receivedResults.Count}");
            WriteTestOutput($"Test duration: {(DateTime.UtcNow - startTime).TotalSeconds:F1} seconds");
        }

        #endregion

        #region Error Handling Integration Tests

        [TestMethod]
        [IntegrationTest(RequiredService = "Ollama Server")]
        public async Task ErrorHandling_WithInvalidModel_ShouldHandleGracefully()
        {
            // Arrange
            var originalModel = _settingsService.OllamaModel;
            _settingsService.SetSetting("OllamaModel", "nonexistent-model-12345");
            
            var codeContext = new CodeContext
            {
                Language = "csharp",
                Code = "public class Test { }",
                FilePath = "Test.cs"
            };

            try
            {
                // Act
                var suggestion = await _ollamaService.GetCodeSuggestionAsync(codeContext, CancellationToken.None);

                // Assert
                suggestion.Should().NotBeNull();
                // Should return empty suggestion rather than throwing
                suggestion.Text.Should().BeNullOrEmpty();
                suggestion.Type.Should().Be(SuggestionType.None);
                
                WriteTestOutput("Invalid model handled gracefully");
            }
            finally
            {
                // Cleanup
                _settingsService.SetSetting("OllamaModel", originalModel);
            }
        }

        [TestMethod]
        [IntegrationTest(RequiredService = "Ollama Server")]
        public async Task ErrorHandling_WithNetworkTimeout_ShouldRespectCancellation()
        {
            // Arrange
            var codeContext = new CodeContext
            {
                Language = "csharp",
                Code = "public class TimeoutTest { }",
                FilePath = "TimeoutTest.cs"
            };

            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100)); // Very short timeout

            // Act
            var suggestion = await _ollamaService.GetCodeSuggestionAsync(codeContext, cts.Token);

            // Assert
            // Should handle cancellation gracefully without throwing
            suggestion.Should().NotBeNull();
            
            WriteTestOutput("Cancellation handled gracefully");
        }

        #endregion

        #region Performance Integration Tests

        [TestMethod]
        [IntegrationTest(RequiredService = "Ollama Server")]
        public async Task Performance_CodeCompletion_ShouldCompleteWithinReasonableTime()
        {
            // Arrange
            var codeContext = new CodeContext
            {
                Language = "csharp",
                Code = "public class PerformanceTest { public void Method() { Console.",
                FilePath = "PerformanceTest.cs",
                CurrentLine = "Console.",
                LineNumber = 1,
                ColumnNumber = 8
            };

            const int maxAcceptableTimeMs = 5000; // 5 seconds
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            // Act
            var suggestion = await _ollamaService.GetCodeSuggestionAsync(codeContext, CancellationToken.None);
            stopwatch.Stop();

            // Assert
            suggestion.Should().NotBeNull();
            stopwatch.ElapsedMilliseconds.Should().BeLessThan(maxAcceptableTimeMs);
            
            WriteTestOutput($"Code completion time: {stopwatch.ElapsedMilliseconds}ms");
            WriteTestOutput($"Performance target: < {maxAcceptableTimeMs}ms");
        }

        [TestMethod]
        [IntegrationTest(RequiredService = "Ollama Server")]
        public async Task Performance_ConcurrentRequests_ShouldHandleMultipleRequests()
        {
            // Arrange
            const int numberOfRequests = 3;
            var tasks = new Task<CodeSuggestion>[numberOfRequests];
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            // Act
            for (int i = 0; i < numberOfRequests; i++)
            {
                var context = new CodeContext
                {
                    Language = "csharp",
                    Code = $"public class ConcurrentTest{i} {{ public void Method{i}() {{ ",
                    FilePath = $"ConcurrentTest{i}.cs"
                };
                
                tasks[i] = _ollamaService.GetCodeSuggestionAsync(context, CancellationToken.None);
            }

            var results = await Task.WhenAll(tasks);
            stopwatch.Stop();

            // Assert
            results.Should().HaveCount(numberOfRequests);
            results.Should().OnlyContain(r => r != null);
            
            WriteTestOutput($"Concurrent requests completed in: {stopwatch.ElapsedMilliseconds}ms");
            WriteTestOutput($"Average time per request: {stopwatch.ElapsedMilliseconds / numberOfRequests}ms");

            for (int i = 0; i < results.Length; i++)
            {
                WriteTestOutput($"Request {i}: {(string.IsNullOrEmpty(results[i].Text) ? "Empty" : "Has content")}");
            }
        }

        #endregion

        #region Helper Methods

        private async Task EnsureOllamaServerIsAvailable()
        {
            try
            {
                // Try to connect to Ollama server
                using var httpClient = new System.Net.Http.HttpClient();
                httpClient.Timeout = TimeSpan.FromSeconds(5);
                
                var serverUrl = _settingsService.OllamaEndpoint;
                var response = await httpClient.GetAsync($"{serverUrl}/api/version");
                
                if (!response.IsSuccessStatusCode)
                {
                    Assert.Inconclusive($"Ollama server not available at {serverUrl}. Status: {response.StatusCode}");
                }
                
                WriteTestOutput($"Ollama server is available at {serverUrl}");
            }
            catch (Exception ex)
            {
                Assert.Inconclusive($"Cannot connect to Ollama server: {ex.Message}. Ensure Ollama is running.");
            }
        }

        private void WriteTestOutput(string message)
        {
            TestContext.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] {message}");
        }

        #endregion
    }
}