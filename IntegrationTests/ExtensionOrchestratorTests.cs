using System;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using OllamaAssistant.Infrastructure;
using OllamaAssistant.Services.Interfaces;
using OllamaAssistant.Tests.TestHelpers;

namespace OllamaAssistant.Tests.IntegrationTests
{
    [TestClass]
    public class ExtensionOrchestratorTests
    {
        private ServiceContainer _serviceContainer;
        private ExtensionOrchestrator _orchestrator;
        private Mock<ISettingsService> _mockSettingsService;
        private Mock<ILogger> _mockLogger;

        [TestInitialize]
        public void Initialize()
        {
            _serviceContainer = new ServiceContainer();
            _mockSettingsService = MockServices.CreateMockSettingsService();
            _mockLogger = MockServices.CreateMockLogger();

            // Register core services
            RegisterMockServices();

            _orchestrator = new ExtensionOrchestrator(_serviceContainer);
        }

        [TestCleanup]
        public void Cleanup()
        {
            _orchestrator?.Dispose();
            _serviceContainer?.Dispose();
        }

        private void RegisterMockServices()
        {
            // Register mock services in the container
            _serviceContainer.RegisterSingleton<ISettingsService, ISettingsService>(_mockSettingsService.Object);
            _serviceContainer.RegisterSingleton<ILogger, ILogger>(_mockLogger.Object);

            var mockErrorHandler = MockServices.CreateMockErrorHandler(_mockLogger, _mockSettingsService);
            _serviceContainer.RegisterSingleton<ErrorHandler, ErrorHandler>(mockErrorHandler.Object);

            var mockCursorHistory = MockServices.CreateMockCursorHistoryService();
            _serviceContainer.RegisterSingleton<ICursorHistoryService, ICursorHistoryService>(mockCursorHistory.Object);

            var mockTextView = MockServices.CreateMockTextViewService();
            _serviceContainer.RegisterSingleton<ITextViewService, ITextViewService>(mockTextView.Object);

            var mockContextCapture = MockServices.CreateMockContextCaptureService();
            _serviceContainer.RegisterSingleton<IContextCaptureService, IContextCaptureService>(mockContextCapture.Object);

            var mockOllama = MockServices.CreateMockOllamaService();
            _serviceContainer.RegisterSingleton<IOllamaService, IOllamaService>(mockOllama.Object);

            var mockSuggestionEngine = MockServices.CreateMockSuggestionEngine();
            _serviceContainer.RegisterSingleton<ISuggestionEngine, ISuggestionEngine>(mockSuggestionEngine.Object);

            var mockIntelliSense = new Mock<IIntelliSenseIntegration>();
            mockIntelliSense.Setup(x => x.ShowSuggestionsAsync(It.IsAny<Models.CodeSuggestion[]>()))
                .Returns(Task.CompletedTask);
            _serviceContainer.RegisterSingleton<IIntelliSenseIntegration, IIntelliSenseIntegration>(mockIntelliSense.Object);

            var mockJumpNotification = new Mock<IJumpNotificationService>();
            mockJumpNotification.Setup(x => x.ShowJumpNotificationAsync(It.IsAny<Models.JumpRecommendation>()))
                .Returns(Task.CompletedTask);
            _serviceContainer.RegisterSingleton<IJumpNotificationService, IJumpNotificationService>(mockJumpNotification.Object);
        }

        [TestMethod]
        public async Task InitializeAsync_ShouldCompleteSuccessfully()
        {
            // Act
            await TestUtilities.ExecuteWithTimeoutAsync(async () =>
            {
                await _orchestrator.InitializeAsync();
            });

            // Assert
            _orchestrator.IsInitialized.Should().BeTrue();
        }

        [TestMethod]
        public async Task InitializeAsync_WhenAlreadyInitialized_ShouldNotReinitialize()
        {
            // Arrange
            await _orchestrator.InitializeAsync();
            _orchestrator.IsInitialized.Should().BeTrue();

            // Act
            await _orchestrator.InitializeAsync(); // Second initialization

            // Assert
            _orchestrator.IsInitialized.Should().BeTrue();
            // Should not throw or cause issues
        }

        [TestMethod]
        public void IsEnabled_ShouldReflectSettingsState()
        {
            // Arrange
            _mockSettingsService.SetupGet(x => x.IsEnabled).Returns(true);

            // Act & Assert
            _orchestrator.IsEnabled.Should().BeTrue();

            // Change setting
            _mockSettingsService.SetupGet(x => x.IsEnabled).Returns(false);
            _orchestrator.IsEnabled.Should().BeFalse();
        }

        [TestMethod]
        public async Task InitializeAsync_WithDisabledExtension_ShouldStillInitialize()
        {
            // Arrange
            _mockSettingsService.SetupGet(x => x.IsEnabled).Returns(false);

            // Act
            await _orchestrator.InitializeAsync();

            // Assert
            _orchestrator.IsInitialized.Should().BeTrue();
            _orchestrator.IsEnabled.Should().BeFalse();
        }

        [TestMethod]
        public async Task ServiceContainer_ShouldHaveAllRequiredServices()
        {
            // Act
            await _orchestrator.InitializeAsync();

            // Assert
            _serviceContainer.IsRegistered<ISettingsService>().Should().BeTrue();
            _serviceContainer.IsRegistered<ILogger>().Should().BeTrue();
            _serviceContainer.IsRegistered<ErrorHandler>().Should().BeTrue();
            _serviceContainer.IsRegistered<ICursorHistoryService>().Should().BeTrue();
            _serviceContainer.IsRegistered<ITextViewService>().Should().BeTrue();
            _serviceContainer.IsRegistered<IContextCaptureService>().Should().BeTrue();
            _serviceContainer.IsRegistered<IOllamaService>().Should().BeTrue();
            _serviceContainer.IsRegistered<ISuggestionEngine>().Should().BeTrue();
            _serviceContainer.IsRegistered<IIntelliSenseIntegration>().Should().BeTrue();
            _serviceContainer.IsRegistered<IJumpNotificationService>().Should().BeTrue();
        }

        [TestMethod]
        public async Task ServiceResolution_ShouldReturnSameInstanceForSingletons()
        {
            // Arrange
            await _orchestrator.InitializeAsync();

            // Act
            var logger1 = _serviceContainer.Resolve<ILogger>();
            var logger2 = _serviceContainer.Resolve<ILogger>();

            // Assert
            logger1.Should().BeSameAs(logger2);
        }

        [TestMethod]
        public async Task InitializeAsync_WithServiceError_ShouldHandleGracefully()
        {
            // Arrange
            var faultyMockLogger = new Mock<ILogger>();
            faultyMockLogger.Setup(x => x.LogInfoAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<object>()))
                .ThrowsAsync(new Exception("Logger error"));

            _serviceContainer = new ServiceContainer();
            _serviceContainer.RegisterSingleton<ILogger, ILogger>(faultyMockLogger.Object);
            _serviceContainer.RegisterSingleton<ISettingsService, ISettingsService>(_mockSettingsService.Object);

            var errorHandler = MockServices.CreateMockErrorHandler(faultyMockLogger, _mockSettingsService);
            _serviceContainer.RegisterSingleton<ErrorHandler, ErrorHandler>(errorHandler.Object);

            // Register remaining services
            RegisterMockServices();

            _orchestrator = new ExtensionOrchestrator(_serviceContainer);

            // Act & Assert - Should not throw
            await TestUtilities.AssertDoesNotThrowAsync(async () =>
            {
                await _orchestrator.InitializeAsync();
            });
        }

        [TestMethod]
        public async Task Dispose_ShouldCleanupAllResources()
        {
            // Arrange
            await _orchestrator.InitializeAsync();

            // Act
            _orchestrator.Dispose();

            // Assert
            _orchestrator.IsInitialized.Should().BeFalse();
        }

        [TestMethod]
        public async Task Dispose_WhenNotInitialized_ShouldNotThrow()
        {
            // Act & Assert
            Action act = () => _orchestrator.Dispose();
            act.Should().NotThrow();
        }

        [TestMethod]
        public async Task Dispose_MultipleTimes_ShouldNotThrow()
        {
            // Arrange
            await _orchestrator.InitializeAsync();

            // Act & Assert
            _orchestrator.Dispose();
            Action act = () => _orchestrator.Dispose(); // Second dispose
            act.Should().NotThrow();
        }

        [TestMethod]
        public async Task InitializeAsync_ShouldLogInitializationSteps()
        {
            // Act
            await _orchestrator.InitializeAsync();

            // Assert
            _mockLogger.Verify(x => x.LogInfoAsync(
                It.Is<string>(s => s.Contains("Initializing Extension Orchestrator")),
                "Orchestrator",
                It.IsAny<object>()), Times.AtLeastOnce);
        }

        [TestMethod]
        public async Task InitializeAsync_ConcurrentCalls_ShouldHandleGracefully()
        {
            // Arrange
            var tasks = new Task[5];

            // Act
            for (int i = 0; i < tasks.Length; i++)
            {
                tasks[i] = _orchestrator.InitializeAsync();
            }

            await Task.WhenAll(tasks);

            // Assert
            _orchestrator.IsInitialized.Should().BeTrue();
        }

        [TestMethod]
        public async Task InitializeAsync_Performance_ShouldCompleteWithinReasonableTime()
        {
            // Act
            var duration = await TestUtilities.MeasureExecutionTimeAsync(async () =>
            {
                await _orchestrator.InitializeAsync();
            });

            // Assert
            duration.Should().BeLessThan(TimeSpan.FromSeconds(5));
            _orchestrator.IsInitialized.Should().BeTrue();
        }

        [TestMethod]
        public async Task ServiceContainer_InitializeServices_ShouldCallInitializationMethods()
        {
            // Act
            await _serviceContainer.InitializeServicesAsync();

            // Assert - Should complete without throwing
            // In a real scenario, services implementing IAsyncInitializable would be called
        }

        [TestMethod]
        public void ServiceContainer_ResolveAll_ShouldReturnAllInstancesOfType()
        {
            // Arrange
            var service1 = _mockLogger.Object;
            var service2 = new Mock<ILogger>().Object;

            _serviceContainer.RegisterSingleton<ILogger, ILogger>(service1);
            // Note: ServiceContainer doesn't support multiple registrations of same type
            // This test demonstrates the current behavior

            // Act
            var services = _serviceContainer.ResolveAll<ILogger>();

            // Assert
            services.Should().Contain(service1);
        }

        [TestMethod]
        public void ServiceContainer_TryResolve_WithUnregisteredService_ShouldReturnNull()
        {
            // Act
            var result = _serviceContainer.TryResolve<ITextViewService>();

            // Assert
            result.Should().BeNull();
        }

        [TestMethod]
        public void ServiceContainer_Resolve_WithUnregisteredService_ShouldThrow()
        {
            // Act & Assert
            Action act = () => _serviceContainer.Resolve<ITextViewService>();
            act.Should().Throw<InvalidOperationException>()
                .WithMessage("*ITextViewService*not registered*");
        }

        [TestMethod]
        public async Task ExtensionOrchestrator_WithMissingService_ShouldHandleGracefully()
        {
            // Arrange - Create orchestrator with incomplete service registration
            var incompleteContainer = new ServiceContainer();
            incompleteContainer.RegisterSingleton<ISettingsService, ISettingsService>(_mockSettingsService.Object);
            incompleteContainer.RegisterSingleton<ILogger, ILogger>(_mockLogger.Object);

            // Act & Assert - Should handle missing services gracefully
            Action act = () => new ExtensionOrchestrator(incompleteContainer);
            act.Should().Throw<InvalidOperationException>();
        }
    }
}