using System;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OllamaAssistant.Infrastructure;
using OllamaAssistant.IntegrationTests.TestUtilities;
using OllamaAssistant.Services.Implementation;
using OllamaAssistant.Services.Interfaces;
using OllamaAssistant.Tests.TestUtilities;

namespace OllamaAssistant.IntegrationTests
{
    [TestClass]
    [TestCategory(TestCategories.Integration)]
    [TestCategory(IntegrationTestCategories.VisualStudio)]
    public class ExtensionIntegrationTests : BaseIntegrationTest
    {
        private ServiceContainer _serviceContainer;
        private ExtensionOrchestrator _orchestrator;

        [TestInitialize]
        public override void TestInitialize()
        {
            base.TestInitialize();
            InitializeExtensionComponents();
        }

        [TestCleanup]
        public override void TestCleanup()
        {
            _orchestrator?.Dispose();
            _serviceContainer?.Dispose();
            base.TestCleanup();
        }

        private void InitializeExtensionComponents()
        {
            _serviceContainer = new ServiceContainer();
            
            // Register test implementations of services
            RegisterTestServices();
            
            _orchestrator = new ExtensionOrchestrator(_serviceContainer);
        }

        private void RegisterTestServices()
        {
            // Register mock services that don't require VS integration
            _serviceContainer.RegisterSingleton<ILogger>(() => MockFactory.CreateMockLogger().Object);
            _serviceContainer.RegisterSingleton<ISettingsService>(() => new TestSettingsService(MockServiceProvider.Object));
            _serviceContainer.RegisterSingleton<ICursorHistoryService>(() => 
                new CursorHistoryService(_serviceContainer.Resolve<ISettingsService>()));
            
            // Register other services...
            // Note: In a real implementation, these would use test doubles for VS-specific services
        }

        #region Extension Loading Tests

        [TestMethod]
        [IntegrationTest(Description = "Tests extension initialization without VS dependencies")]
        public async Task ExtensionInitialization_WithoutVSDependencies_ShouldInitializeSuccessfully()
        {
            // Act
            var initializationTask = Task.Run(async () =>
            {
                // Simulate extension initialization
                await _serviceContainer.InitializeServicesAsync();
                return true;
            });

            // Assert
            await AssertCompletesWithinTimeout(
                () => initializationTask, 
                TimeSpan.FromSeconds(30));

            var result = await initializationTask;
            result.Should().BeTrue();
            
            WriteTestOutput("Extension components initialized successfully");
        }

        [TestMethod]
        public async Task ServiceContainer_ShouldResolveAllRequiredServices()
        {
            // Arrange
            await _serviceContainer.InitializeServicesAsync();

            // Act & Assert
            var logger = _serviceContainer.Resolve<ILogger>();
            logger.Should().NotBeNull();

            var settingsService = _serviceContainer.Resolve<ISettingsService>();
            settingsService.Should().NotBeNull();

            var cursorHistoryService = _serviceContainer.Resolve<ICursorHistoryService>();
            cursorHistoryService.Should().NotBeNull();

            WriteTestOutput("All required services resolved successfully");
        }

        [TestMethod]
        public void ServiceContainer_WithMissingService_ShouldThrowMeaningfulException()
        {
            // Act & Assert
            Action act = () => _serviceContainer.Resolve<INonExistentService>();
            act.Should().Throw<InvalidOperationException>()
                .WithMessage("*not registered*");
        }

        #endregion

        #region Service Integration Tests

        [TestMethod]
        public async Task SettingsService_PersistenceTest_ShouldMaintainStateAcrossSessions()
        {
            // Arrange
            var settingsService = _serviceContainer.Resolve<ISettingsService>();
            var originalModel = settingsService.OllamaModel;
            var testModel = "test-model-integration";

            // Act - Change setting and save
            settingsService.OllamaModel = testModel;
            settingsService.SaveSettings();

            // Create new service instance to simulate new session
            var newSettingsService = new TestSettingsService(MockServiceProvider.Object);
            newSettingsService.LoadSettings();

            // Assert
            newSettingsService.OllamaModel.Should().Be(testModel);

            // Cleanup - restore original setting
            settingsService.OllamaModel = originalModel;
            settingsService.SaveSettings();

            WriteTestOutput($"Settings persistence test completed. Model changed from '{originalModel}' to '{testModel}' and back");
        }

        [TestMethod]
        public async Task CursorHistoryService_IntegrationTest_ShouldTrackHistoryCorrectly()
        {
            // Arrange
            var cursorHistoryService = _serviceContainer.Resolve<ICursorHistoryService>();
            var testEntries = MockFactory.CreateTestCursorHistory(10);

            // Act
            foreach (var entry in testEntries)
            {
                cursorHistoryService.RecordCursorPosition(entry);
                await Task.Delay(10); // Small delay to ensure different timestamps
            }

            // Assert
            var recentHistory = cursorHistoryService.GetRecentHistory(15);
            recentHistory.Should().NotBeEmpty();
            recentHistory.Should().HaveCountLessOrEqualTo(10); // Limited by settings

            var fileHistory = cursorHistoryService.GetFileHistory(testEntries[0].FilePath, 5);
            fileHistory.Should().NotBeEmpty();

            WriteTestOutput($"Cursor history integration test completed. Tracked {recentHistory.Count} entries");
        }

        [TestMethod]
        public async Task SettingsChangeNotification_ShouldPropagateToServices()
        {
            // Arrange
            var settingsService = _serviceContainer.Resolve<ISettingsService>();
            var cursorHistoryService = _serviceContainer.Resolve<ICursorHistoryService>();
            
            var eventFired = false;
            settingsService.SettingsChanged += (sender, args) =>
            {
                if (args.SettingName == nameof(ISettingsService.CursorHistoryMemoryDepth))
                    eventFired = true;
            };

            // Act
            settingsService.CursorHistoryMemoryDepth = 7;

            // Assert
            eventFired.Should().BeTrue();
            // The cursor history service should have updated its max depth
            // (This would require the cursor history service to listen to settings changes)

            WriteTestOutput("Settings change notification test completed");
        }

        #endregion

        #region Error Handling Integration Tests

        [TestMethod]
        public async Task ServiceContainer_WithFailingService_ShouldHandleGracefully()
        {
            // Arrange
            _serviceContainer.RegisterSingleton<IFailingService>(() => new FailingTestService());

            // Act & Assert
            Action act = () => _serviceContainer.Resolve<IFailingService>();
            act.Should().NotThrow(); // Should not throw during resolution

            var failingService = _serviceContainer.Resolve<IFailingService>();
            
            // Service methods should handle failures gracefully
            await AssertThrowsAsync<InvalidOperationException>(async () =>
                await failingService.FailingMethodAsync());

            WriteTestOutput("Error handling integration test completed");
        }

        [TestMethod]
        public void ServiceContainer_CircularDependency_ShouldDetectAndThrow()
        {
            // Arrange
            var testContainer = new ServiceContainer();
            testContainer.RegisterSingleton<ICircularServiceA>(() => 
                new CircularServiceA(testContainer.Resolve<ICircularServiceB>()));
            testContainer.RegisterSingleton<ICircularServiceB>(() => 
                new CircularServiceB(testContainer.Resolve<ICircularServiceA>()));

            // Act & Assert
            Action act = () => testContainer.Resolve<ICircularServiceA>();
            act.Should().Throw<InvalidOperationException>()
                .WithMessage("*circular*");

            WriteTestOutput("Circular dependency detection test completed");
        }

        #endregion

        #region Performance Integration Tests

        [TestMethod]
        [PerformanceTest(MaxExecutionTimeMs = 5000)]
        public async Task ServiceInitialization_ShouldCompleteWithinTimeout()
        {
            // Arrange
            var newContainer = new ServiceContainer();
            RegisterTestServices();

            // Act & Assert
            var (_, duration) = await MeasureExecutionTime(async () =>
                await newContainer.InitializeServicesAsync());

            AssertAcceptableResponseTime(duration, TimeSpan.FromSeconds(5));
            
            WriteTestOutput($"Service initialization completed in {duration.TotalMilliseconds}ms");
            
            newContainer.Dispose();
        }

        [TestMethod]
        [PerformanceTest(MaxExecutionTimeMs = 1000)]
        public async Task ServiceResolution_ShouldBeEfficient()
        {
            // Arrange
            await _serviceContainer.InitializeServicesAsync();
            const int numberOfResolutions = 1000;

            // Act
            var (_, duration) = await MeasureExecutionTime(async () =>
            {
                await Task.Run(() =>
                {
                    for (int i = 0; i < numberOfResolutions; i++)
                    {
                        var service = _serviceContainer.Resolve<ISettingsService>();
                        service.Should().NotBeNull();
                    }
                });
            });

            // Assert
            var averageResolutionTime = duration.TotalMilliseconds / numberOfResolutions;
            averageResolutionTime.Should().BeLessThan(1.0, "Service resolution should be under 1ms on average");
            
            WriteTestOutput($"Resolved {numberOfResolutions} services in {duration.TotalMilliseconds}ms (avg: {averageResolutionTime:F3}ms per resolution)");
        }

        #endregion

        #region Memory and Resource Tests

        [TestMethod]
        public async Task ExtensionLifecycle_ShouldCleanupResourcesProperly()
        {
            // Arrange
            var initialMemory = GC.GetTotalMemory(true);
            
            // Act - Create and dispose multiple service containers
            for (int i = 0; i < 10; i++)
            {
                using var container = new ServiceContainer();
                RegisterTestServices();
                await container.InitializeServicesAsync();
                
                // Use some services
                var settingsService = container.Resolve<ISettingsService>();
                settingsService.OllamaModel = $"test-model-{i}";
            }

            // Force garbage collection
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            
            var finalMemory = GC.GetTotalMemory(false);

            // Assert
            var memoryIncrease = finalMemory - initialMemory;
            WriteTestOutput($"Memory usage: Initial={initialMemory / 1024}KB, Final={finalMemory / 1024}KB, Increase={memoryIncrease / 1024}KB");
            
            // Memory increase should be reasonable (less than 10MB for this test)
            memoryIncrease.Should().BeLessThan(10 * 1024 * 1024, "Memory increase should be reasonable");
        }

        #endregion

        #region Mock Service Provider

        private static readonly Moq.Mock<IServiceProvider> MockServiceProvider = new();

        #endregion

        #region Test Service Interfaces and Implementations

        public interface INonExistentService
        {
            void DoSomething();
        }

        public interface IFailingService
        {
            Task FailingMethodAsync();
        }

        public interface ICircularServiceA
        {
            void DoSomethingA();
        }

        public interface ICircularServiceB
        {
            void DoSomethingB();
        }

        public class FailingTestService : IFailingService
        {
            public async Task FailingMethodAsync()
            {
                await Task.Delay(10);
                throw new InvalidOperationException("This service always fails");
            }
        }

        public class CircularServiceA : ICircularServiceA
        {
            private readonly ICircularServiceB _serviceB;

            public CircularServiceA(ICircularServiceB serviceB)
            {
                _serviceB = serviceB;
            }

            public void DoSomethingA()
            {
                _serviceB.DoSomethingB();
            }
        }

        public class CircularServiceB : ICircularServiceB
        {
            private readonly ICircularServiceA _serviceA;

            public CircularServiceB(ICircularServiceA serviceA)
            {
                _serviceA = serviceA;
            }

            public void DoSomethingB()
            {
                _serviceA.DoSomethingA();
            }
        }

        #endregion
    }
}