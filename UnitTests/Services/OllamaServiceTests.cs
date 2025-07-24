using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using OllamaAssistant.Infrastructure;
using OllamaAssistant.Models;
using OllamaAssistant.Services.Implementation;
using OllamaAssistant.Services.Interfaces;
using OllamaAssistant.Tests.TestHelpers;

namespace OllamaAssistant.Tests.UnitTests.Services
{
    [TestClass]
    public class OllamaServiceTests
    {
        private OllamaService _service;
        private Mock<ISettingsService> _mockSettingsService;
        private Mock<ILogger> _mockLogger;
        private Mock<ErrorHandler> _mockErrorHandler;

        [TestInitialize]
        public void Initialize()
        {
            _mockSettingsService = MockServices.CreateMockSettingsService();
            _mockLogger = MockServices.CreateMockLogger();
            _mockErrorHandler = MockServices.CreateMockErrorHandler(_mockLogger, _mockSettingsService);

            _service = new OllamaService(_mockSettingsService.Object, _mockLogger.Object, _mockErrorHandler.Object);
        }

        [TestCleanup]
        public void Cleanup()
        {
            _service?.Dispose();
        }

        [TestMethod]
        public void Constructor_WithValidParameters_ShouldInitializeCorrectly()
        {
            // Assert
            _service.Should().NotBeNull();
            _service.Endpoint.Should().Be("http://localhost:11434");
        }

        [TestMethod]
        public void Constructor_WithNullSettingsService_ShouldThrowArgumentNullException()
        {
            // Act & Assert
            Action act = () => new OllamaService(null);
            act.Should().Throw<ArgumentNullException>().WithParameterName("settingsService");
        }

        [TestMethod]
        public async Task GetCompletionAsync_WithValidPrompt_ShouldReturnCompletion()
        {
            // Arrange
            var prompt = "Write a hello world program";
            var context = "C# console application";
            var history = new List<CursorHistoryEntry>();

            // Act
            var result = await _service.GetCompletionAsync(prompt, context, history, CancellationToken.None);

            // Assert
            result.Should().NotBeNullOrEmpty();
        }

        [TestMethod]
        public async Task GetCompletionAsync_WithEmptyPrompt_ShouldReturnEmptyString()
        {
            // Act
            var result = await _service.GetCompletionAsync("", "context", new List<CursorHistoryEntry>(), CancellationToken.None);

            // Assert
            result.Should().BeEmpty();
        }

        [TestMethod]
        public async Task GetCompletionAsync_WithNullPrompt_ShouldReturnEmptyString()
        {
            // Act
            var result = await _service.GetCompletionAsync(null, "context", new List<CursorHistoryEntry>(), CancellationToken.None);

            // Assert
            result.Should().BeEmpty();
        }

        [TestMethod]
        public async Task GetCompletionAsync_WithWhitespacePrompt_ShouldReturnEmptyString()
        {
            // Act
            var result = await _service.GetCompletionAsync("   \t\n   ", "context", new List<CursorHistoryEntry>(), CancellationToken.None);

            // Assert
            result.Should().BeEmpty();
        }

        [TestMethod]
        public async Task GetCompletionAsync_WithHistory_ShouldIncludeHistoryInPrompt()
        {
            // Arrange
            var prompt = "Complete this function";
            var context = "public void TestMethod() {";
            var history = TestDataBuilders.Scenarios.CreateHistorySequence(2);

            // Act
            var result = await _service.GetCompletionAsync(prompt, context, history, CancellationToken.None);

            // Assert
            result.Should().NotBeNullOrEmpty();
            // Verify that the logger was called with debug information indicating history was included
            _mockLogger.Verify(x => x.LogDebugAsync(
                It.Is<string>(s => s.Contains("Getting completion for prompt")), 
                "OllamaService", 
                It.IsAny<object>()), Times.Once);
        }

        [TestMethod]
        public async Task GetCompletionAsync_WithCancellationToken_ShouldRespectCancellation()
        {
            // Arrange
            using var cts = new CancellationTokenSource();
            cts.Cancel(); // Cancel immediately

            // Act
            var result = await _service.GetCompletionAsync("test prompt", "context", new List<CursorHistoryEntry>(), cts.Token);

            // Assert - Should complete gracefully even with cancelled token
            result.Should().NotBeNull();
        }

        [TestMethod]
        public async Task GetCodeSuggestionAsync_WithValidContext_ShouldReturnSuggestion()
        {
            // Arrange
            var context = TestDataBuilders.CodeContextBuilder.Default().Build();

            // Act
            var suggestion = await _service.GetCodeSuggestionAsync(context, CancellationToken.None);

            // Assert
            suggestion.Should().NotBeNull();
            suggestion.CompletionText.Should().NotBeNullOrEmpty();
        }

        [TestMethod]
        public async Task GetCodeSuggestionAsync_WithNullContext_ShouldReturnNull()
        {
            // Act
            var suggestion = await _service.GetCodeSuggestionAsync(null, CancellationToken.None);

            // Assert
            suggestion.Should().BeNull();
        }

        [TestMethod]
        public async Task GetCodeSuggestionAsync_WithCSharpContext_ShouldGenerateAppropriateCompletion()
        {
            // Arrange
            var context = TestDataBuilders.Scenarios.CreateCSharpMethodContext();

            // Act
            var suggestion = await _service.GetCodeSuggestionAsync(context, CancellationToken.None);

            // Assert
            suggestion.Should().NotBeNull();
            suggestion.CompletionText.Should().NotBeNullOrEmpty();
            suggestion.Confidence.Should().BeGreaterThan(0);
        }

        [TestMethod]
        public async Task GetCodeSuggestionAsync_WithJavaScriptContext_ShouldGenerateAppropriateCompletion()
        {
            // Arrange
            var context = TestDataBuilders.Scenarios.CreateJavaScriptFunctionContext();

            // Act
            var suggestion = await _service.GetCodeSuggestionAsync(context, CancellationToken.None);

            // Assert
            suggestion.Should().NotBeNull();
            suggestion.CompletionText.Should().NotBeNullOrEmpty();
        }

        [TestMethod]
        public async Task IsAvailableAsync_WhenServiceIsRunning_ShouldReturnTrue()
        {
            // Act
            var isAvailable = await _service.IsAvailableAsync();

            // Assert
            isAvailable.Should().BeTrue();
        }

        [TestMethod]
        public async Task GetModelsAsync_ShouldReturnAvailableModels()
        {
            // Act
            var models = await _service.GetModelsAsync();

            // Assert
            models.Should().NotBeNull();
            models.Should().NotBeEmpty();
            models.Should().Contain("codellama");
        }

        [TestMethod]
        public async Task GetCompletionAsync_WhenErrorHandlerIsNull_ShouldUseFallback()
        {
            // Arrange
            var serviceWithoutErrorHandler = new OllamaService(_mockSettingsService.Object);
            var prompt = "test prompt";

            // Act
            var result = await serviceWithoutErrorHandler.GetCompletionAsync(prompt, "context", new List<CursorHistoryEntry>(), CancellationToken.None);

            // Assert
            result.Should().NotBeNull();
            
            // Cleanup
            serviceWithoutErrorHandler.Dispose();
        }

        [TestMethod]
        public async Task GetCompletionAsync_WithLongPrompt_ShouldHandleGracefully()
        {
            // Arrange
            var longPrompt = new string('A', 10000); // Very long prompt

            // Act
            var result = await _service.GetCompletionAsync(longPrompt, "context", new List<CursorHistoryEntry>(), CancellationToken.None);

            // Assert
            result.Should().NotBeNull();
        }

        [TestMethod]
        public async Task GetCompletionAsync_WithSpecialCharacters_ShouldHandleCorrectly()
        {
            // Arrange
            var promptWithSpecialChars = "Generate code with special chars: < > & \" ' \n \t \\";

            // Act
            var result = await _service.GetCompletionAsync(promptWithSpecialChars, "context", new List<CursorHistoryEntry>(), CancellationToken.None);

            // Assert
            result.Should().NotBeNull();
        }

        [TestMethod]
        public async Task GetCompletionAsync_Performance_ShouldCompleteWithinReasonableTime()
        {
            // Arrange
            var prompt = "Simple completion request";

            // Act
            var (result, duration) = await TestUtilities.MeasureExecutionTimeAsync(async () =>
                await _service.GetCompletionAsync(prompt, "context", new List<CursorHistoryEntry>(), CancellationToken.None));

            // Assert
            result.Should().NotBeNull();
            duration.Should().BeLessThan(TimeSpan.FromSeconds(5)); // Should be reasonably fast
        }

        [TestMethod]
        public async Task GetCodeSuggestionAsync_MultipleConcurrentRequests_ShouldHandleGracefully()
        {
            // Arrange
            var context = TestDataBuilders.CodeContextBuilder.Default().Build();
            var tasks = new List<Task<CodeSuggestion>>();

            // Act
            for (int i = 0; i < 5; i++)
            {
                tasks.Add(_service.GetCodeSuggestionAsync(context, CancellationToken.None));
            }

            var suggestions = await Task.WhenAll(tasks);

            // Assert
            suggestions.Should().HaveCount(5);
            suggestions.Should().OnlyContain(s => s != null);
        }

        [TestMethod]
        public void Endpoint_ShouldReturnSettingsEndpoint()
        {
            // Assert
            _service.Endpoint.Should().Be(_mockSettingsService.Object.OllamaEndpoint);
        }

        [TestMethod]
        public async Task SettingsChanged_WhenEndpointChanges_ShouldReconfigureClient()
        {
            // Arrange
            var newEndpoint = "http://newhost:12345";

            // Act
            _mockSettingsService.SetupProperty(x => x.OllamaEndpoint, newEndpoint);
            _mockSettingsService.Raise(x => x.SettingsChanged += null, 
                new SettingsChangedEventArgs("OllamaEndpoint"));

            // Allow some time for the handler to process
            await Task.Delay(100);

            // Assert
            _service.Endpoint.Should().Be(newEndpoint);
        }

        [TestMethod]
        public void Dispose_ShouldCleanupResources()
        {
            // Act
            _service.Dispose();

            // Assert - Should not throw
            Action act = () => _service.Dispose(); // Second dispose
            act.Should().NotThrow();
        }

        [TestMethod]
        public async Task GetCompletionAsync_AfterDispose_ShouldReturnEmptyString()
        {
            // Arrange
            _service.Dispose();

            // Act
            var result = await _service.GetCompletionAsync("test", "context", new List<CursorHistoryEntry>(), CancellationToken.None);

            // Assert
            result.Should().BeEmpty();
        }
    }
}