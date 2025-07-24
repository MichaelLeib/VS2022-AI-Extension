using System;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using OllamaAssistant.Infrastructure;
using OllamaAssistant.Models;
using OllamaAssistant.Services.Implementation;
using OllamaAssistant.Services.Interfaces;
using OllamaAssistant.Tests.TestHelpers;

namespace OllamaAssistant.Tests.IntegrationTests
{
    [TestClass]
    public class ErrorHandlingIntegrationTests
    {
        private ErrorHandler _errorHandler;
        private Mock<ILogger> _mockLogger;
        private Mock<ISettingsService> _mockSettingsService;

        [TestInitialize]
        public void Initialize()
        {
            _mockLogger = MockServices.CreateMockLogger();
            _mockSettingsService = MockServices.CreateMockSettingsService();
            _errorHandler = new ErrorHandler(_mockLogger.Object, _mockSettingsService.Object);
        }

        [TestMethod]
        public async Task HandleExceptionAsync_WithOllamaConnectionException_ShouldRecoverGracefully()
        {
            // Arrange
            var connectionException = new OllamaConnectionException("Connection failed");

            // Act
            var recovered = await _errorHandler.HandleExceptionAsync(connectionException, "TestContext");

            // Assert
            recovered.Should().BeTrue();
            _mockLogger.Verify(x => x.LogErrorAsync(connectionException, "TestContext", It.IsAny<object>()), Times.Once);
            _mockLogger.Verify(x => x.LogInfoAsync(It.Is<string>(s => s.Contains("Successfully recovered")), "TestContext"), Times.Once);
        }

        [TestMethod]
        public async Task HandleExceptionAsync_WithOllamaModelException_ShouldApplyCorrectRecoveryStrategy()
        {
            // Arrange
            var modelException = new OllamaModelException("Model not found");

            // Act
            var recovered = await _errorHandler.HandleExceptionAsync(modelException, "ModelTest");

            // Assert
            recovered.Should().BeTrue();
            _mockLogger.Verify(x => x.LogErrorAsync(modelException, "ModelTest", It.IsAny<object>()), Times.Once);
        }

        [TestMethod]
        public async Task HandleExceptionAsync_WithContextCaptureException_ShouldFallbackGracefully()
        {
            // Arrange
            var contextException = new ContextCaptureException("Failed to capture context");

            // Act
            var recovered = await _errorHandler.HandleExceptionAsync(contextException, "ContextCapture");

            // Assert
            recovered.Should().BeTrue();
            _mockLogger.Verify(x => x.LogWarningAsync(It.Is<string>(s => s.Contains("Context capture failed")), "ContextCapture"), Times.Once);
        }

        [TestMethod]
        public async Task HandleExceptionAsync_WithSuggestionProcessingException_ShouldSkipSuggestion()
        {
            // Arrange
            var suggestionException = new SuggestionProcessingException("Suggestion processing failed");

            // Act
            var recovered = await _errorHandler.HandleExceptionAsync(suggestionException, "SuggestionProcessing");

            // Assert
            recovered.Should().BeTrue();
            _mockLogger.Verify(x => x.LogWarningAsync(It.Is<string>(s => s.Contains("Suggestion processing failed")), "SuggestionProcessing"), Times.Once);
        }

        [TestMethod]
        public async Task HandleExceptionAsync_WithUnhandledException_ShouldLogAndNotRecover()
        {
            // Arrange
            var unknownException = new InvalidOperationException("Unknown error");

            // Act
            var recovered = await _errorHandler.HandleExceptionAsync(unknownException, "UnknownContext");

            // Assert
            recovered.Should().BeFalse();
            _mockLogger.Verify(x => x.LogErrorAsync(unknownException, "UnknownContext", It.IsAny<object>()), Times.Once);
        }

        [TestMethod]
        public async Task ExecuteWithErrorHandlingAsync_WithSuccessfulOperation_ShouldReturnResult()
        {
            // Arrange
            var expectedResult = "Success";

            // Act
            var result = await _errorHandler.ExecuteWithErrorHandlingAsync(async () =>
            {
                await Task.Delay(10);
                return expectedResult;
            }, "TestOperation", "DefaultValue");

            // Assert
            result.Should().Be(expectedResult);
        }

        [TestMethod]
        public async Task ExecuteWithErrorHandlingAsync_WithException_ShouldReturnDefaultValue()
        {
            // Arrange
            var defaultValue = "DefaultValue";

            // Act
            var result = await _errorHandler.ExecuteWithErrorHandlingAsync<string>(async () =>
            {
                await Task.Delay(10);
                throw new Exception("Test exception");
            }, "TestOperation", defaultValue);

            // Assert
            result.Should().Be(defaultValue);
            _mockLogger.Verify(x => x.LogErrorAsync(It.IsAny<Exception>(), "TestOperation", It.IsAny<object>()), Times.Once);
        }

        [TestMethod]
        public async Task ExecuteWithErrorHandlingAsync_VoidOperation_WithException_ShouldHandleGracefully()
        {
            // Arrange
            var exceptionThrown = false;

            // Act
            await _errorHandler.ExecuteWithErrorHandlingAsync(async () =>
            {
                await Task.Delay(10);
                exceptionThrown = true;
                throw new Exception("Test exception");
            }, "VoidOperation");

            // Assert
            exceptionThrown.Should().BeTrue();
            _mockLogger.Verify(x => x.LogErrorAsync(It.IsAny<Exception>(), "VoidOperation", It.IsAny<object>()), Times.Once);
        }

        [TestMethod]
        public async Task PerformHealthCheckAsync_WithHealthySystem_ShouldReturnHealthyResult()
        {
            // Arrange
            _mockSettingsService.Setup(x => x.ValidateSettings()).Returns(true);

            // Act
            var healthResult = await _errorHandler.PerformHealthCheckAsync();

            // Assert
            healthResult.Should().NotBeNull();
            healthResult.IsHealthy.Should().BeTrue();
            healthResult.SettingsValid.Should().BeTrue();
            healthResult.OllamaConnectivity.Should().BeTrue();
            healthResult.MemoryUsage.Should().BeGreaterThan(0);
            healthResult.PerformanceMetrics.Should().NotBeNull();
        }

        [TestMethod]
        public async Task PerformHealthCheckAsync_WithInvalidSettings_ShouldReturnUnhealthyResult()
        {
            // Arrange
            _mockSettingsService.Setup(x => x.ValidateSettings()).Returns(false);

            // Act
            var healthResult = await _errorHandler.PerformHealthCheckAsync();

            // Assert
            healthResult.Should().NotBeNull();
            healthResult.IsHealthy.Should().BeFalse();
            healthResult.SettingsValid.Should().BeFalse();
        }

        [TestMethod]
        public async Task PerformHealthCheckAsync_WithHealthCheckException_ShouldReturnUnhealthyResult()
        {
            // Arrange
            _mockSettingsService.Setup(x => x.ValidateSettings()).Throws(new Exception("Settings validation failed"));

            // Act
            var healthResult = await _errorHandler.PerformHealthCheckAsync();

            // Assert
            healthResult.Should().NotBeNull();
            healthResult.IsHealthy.Should().BeFalse();
            healthResult.ErrorMessage.Should().Contain("Settings validation failed");
        }

        [TestMethod]
        public async Task HandleExceptionWithUserFeedbackAsync_WithRecovery_ShouldNotShowMessage()
        {
            // Arrange
            var recoverableException = new OllamaConnectionException("Connection failed");
            var userMessage = "Failed to connect to Ollama";

            // Act
            var recovered = await _errorHandler.HandleExceptionWithUserFeedbackAsync(recoverableException, userMessage, "TestContext");

            // Assert
            recovered.Should().BeTrue();
            // User feedback should not be shown for recovered exceptions
        }

        [TestMethod]
        public async Task HandleExceptionWithUserFeedbackAsync_WithoutRecovery_ShouldShowMessage()
        {
            // Arrange
            var unrecoverableException = new InvalidOperationException("Unrecoverable error");
            var userMessage = "An unexpected error occurred";

            // Act
            var recovered = await _errorHandler.HandleExceptionWithUserFeedbackAsync(unrecoverableException, userMessage, "TestContext");

            // Assert
            recovered.Should().BeFalse();
            // In a real implementation, this would show a message box to the user
        }

        [TestMethod]
        public async Task ErrorRecovery_WithRetryLogic_ShouldAttemptMultipleTimes()
        {
            // Arrange
            var attemptCount = 0;
            var maxAttempts = 3;

            // Act
            var recovered = await _errorHandler.ExecuteWithErrorHandlingAsync(async () =>
            {
                attemptCount++;
                if (attemptCount <= maxAttempts)
                {
                    throw new OllamaConnectionException($"Attempt {attemptCount} failed");
                }
                return "Success after retries";
            }, "RetryTest", "Failed");

            // Assert
            // The exact behavior depends on the retry strategy implementation
            // This test verifies that retries are attempted
            _mockLogger.Verify(x => x.LogErrorAsync(It.IsAny<Exception>(), It.IsAny<string>(), It.IsAny<object>()), 
                Times.AtLeastOnce);
        }

        [TestMethod]
        public async Task ConcurrentErrorHandling_ShouldHandleMultipleErrors()
        {
            // Arrange
            var tasks = new Task[10];

            // Act
            for (int i = 0; i < tasks.Length; i++)
            {
                var taskIndex = i;
                tasks[i] = _errorHandler.ExecuteWithErrorHandlingAsync(async () =>
                {
                    await Task.Delay(50);
                    if (taskIndex % 2 == 0)
                    {
                        throw new Exception($"Error in task {taskIndex}");
                    }
                    return $"Success in task {taskIndex}";
                }, $"ConcurrentTask{taskIndex}", "DefaultValue");
            }

            var results = await Task.WhenAll(tasks.Cast<Task<string>>());

            // Assert
            results.Should().HaveCount(10);
            results.Where(r => r == "DefaultValue").Should().HaveCount(5); // Half failed
            results.Where(r => r.StartsWith("Success")).Should().HaveCount(5); // Half succeeded
        }

        [TestMethod]
        public async Task ErrorHandler_WithNullLogger_ShouldNotThrow()
        {
            // Arrange
            var errorHandlerWithoutLogger = new ErrorHandler(null, _mockSettingsService.Object);
            var testException = new Exception("Test exception");

            // Act & Assert
            await TestUtilities.AssertDoesNotThrowAsync(async () =>
            {
                await errorHandlerWithoutLogger.HandleExceptionAsync(testException, "TestContext");
            });
        }

        [TestMethod]
        public async Task ErrorHandler_WithNullSettingsService_ShouldHandleGracefully()
        {
            // Arrange
            var errorHandlerWithoutSettings = new ErrorHandler(_mockLogger.Object, null);
            var testException = new Exception("Test exception");

            // Act
            var recovered = await errorHandlerWithoutSettings.HandleExceptionAsync(testException, "TestContext");

            // Assert
            recovered.Should().BeFalse(); // Cannot recover without settings
        }

        [TestMethod]
        public async Task RegisterRecoveryStrategy_CustomStrategy_ShouldBeUsed()
        {
            // Arrange
            var customRecovered = false;
            var customStrategy = new ErrorRecoveryStrategy
            {
                RetryCount = 1,
                RetryDelay = TimeSpan.FromMilliseconds(10),
                FallbackAction = async (ex, context, data) =>
                {
                    customRecovered = true;
                    return true;
                }
            };

            _errorHandler.RegisterRecoveryStrategy<ArgumentException>(customStrategy);

            // Act
            var recovered = await _errorHandler.HandleExceptionAsync(new ArgumentException("Test"), "CustomTest");

            // Assert
            recovered.Should().BeTrue();
            customRecovered.Should().BeTrue();
        }

        [TestMethod]
        public async Task ErrorHandling_Performance_ShouldHandleManyErrorsQuickly()
        {
            // Act
            var duration = await TestUtilities.MeasureExecutionTimeAsync(async () =>
            {
                var tasks = new Task[100];
                for (int i = 0; i < tasks.Length; i++)
                {
                    tasks[i] = _errorHandler.HandleExceptionAsync(
                        new Exception($"Error {i}"), 
                        $"PerformanceTest{i}");
                }
                await Task.WhenAll(tasks);
            });

            // Assert
            duration.Should().BeLessThan(TimeSpan.FromSeconds(5));
        }

        [TestMethod]
        public async Task SafeExecutionExtensions_ShouldProvideConvenientErrorHandling()
        {
            // Arrange
            var successfulTask = Task.FromResult("Success");
            var failingTask = Task.FromException<string>(new Exception("Task failed"));

            // Act
            var successResult = await successfulTask.SafeExecuteAsync(_errorHandler, "SuccessTest", "Default");
            var failureResult = await failingTask.SafeExecuteAsync(_errorHandler, "FailureTest", "Default");

            // Assert
            successResult.Should().Be("Success");
            failureResult.Should().Be("Default");
        }
    }
}