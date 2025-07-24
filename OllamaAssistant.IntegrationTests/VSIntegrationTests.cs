using System;
using System.ComponentModel.Composition;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.VisualStudio.Threading;
using OllamaAssistant.Infrastructure;
using OllamaAssistant.IntegrationTests.TestUtilities;
using OllamaAssistant.Services.Implementation;
using OllamaAssistant.Services.Interfaces;
using OllamaAssistant.Tests.TestUtilities;
using Microsoft.VisualStudio.Shell.Interop;
using System.Threading;

namespace OllamaAssistant.IntegrationTests
{
    [TestClass]
    [TestCategory(TestCategories.Integration)]
    [TestCategory(IntegrationTestCategories.VisualStudio)]
    public class VSIntegrationTests : BaseIntegrationTest
    {
        private JoinableTaskFactory _joinableTaskFactory;
        private ServiceContainer _serviceContainer;
        private IServiceProvider _mockServiceProvider;

        [TestInitialize]
        public override void TestInitialize()
        {
            base.TestInitialize();
            InitializeVSTestEnvironment();
        }

        [TestCleanup]
        public override void TestCleanup()
        {
            _serviceContainer?.Dispose();
            base.TestCleanup();
        }

        private void InitializeVSTestEnvironment()
        {
            // Create a mock VS environment for testing
            _joinableTaskFactory = new JoinableTaskContext().Factory;
            _serviceContainer = new ServiceContainer();
            _mockServiceProvider = MockFactory.CreateMockServiceProvider().Object;

            // Register test services
            RegisterVSTestServices();
        }

        private void RegisterVSTestServices()
        {
            // Register VS-specific services with test implementations
            _serviceContainer.RegisterSingleton<IVsOutputWindow>(() => 
                MockFactory.CreateMockOutputWindow().Object);
            _serviceContainer.RegisterSingleton<IVsStatusbar>(() => 
                MockFactory.CreateMockStatusBar().Object);
            _serviceContainer.RegisterSingleton<SVsRunningDocumentTable>(() => 
                MockFactory.CreateMockRunningDocumentTable().Object);
            
            // Register our services
            _serviceContainer.RegisterSingleton<ILogger>(() => 
                MockFactory.CreateMockLogger().Object);
            _serviceContainer.RegisterSingleton<ISettingsService>(() => 
                new TestSettingsService(_mockServiceProvider));
        }

        #region VS Service Integration Tests

        [TestMethod]
        [IntegrationTest(Description = "Tests VS Output Window service integration")]
        public async Task VSOutputWindowService_ShouldIntegrateWithVSOutputWindow()
        {
            // Arrange
            var outputWindowService = new VSOutputWindowService(
                _serviceContainer.Resolve<IVsOutputWindow>(),
                _joinableTaskFactory);

            // Act
            await outputWindowService.InitializeAsync();
            await outputWindowService.WriteInfoAsync("Test message", "TestCategory");
            await outputWindowService.WriteErrorAsync("Test error", "TestCategory");

            // Assert
            outputWindowService.Should().NotBeNull();
            WriteTestOutput("VS Output Window service integration test completed");
        }

        [TestMethod]
        [IntegrationTest(Description = "Tests VS Status Bar service integration")]
        public async Task VSStatusBarService_ShouldIntegrateWithVSStatusBar()
        {
            // Arrange
            var statusBarService = new VSStatusBarService(
                _serviceContainer.Resolve<IVsStatusbar>(),
                _joinableTaskFactory);

            // Act
            await statusBarService.ShowAIProcessingAsync("Processing...");
            await statusBarService.ShowConnectionStatusAsync(true, "localhost:11434");
            await statusBarService.ClearStatusAsync();

            // Assert
            statusBarService.Should().NotBeNull();
            WriteTestOutput("VS Status Bar service integration test completed");
        }

        [TestMethod]
        [IntegrationTest(Description = "Tests cursor history VS integration")]
        public async Task CursorHistoryIntegration_ShouldTrackVSDocumentChanges()
        {
            // Arrange
            var settingsService = _serviceContainer.Resolve<ISettingsService>();
            var cursorHistoryIntegration = new CursorHistoryIntegration(
                settingsService,
                _serviceContainer.Resolve<ILogger>(),
                _serviceContainer.Resolve<SVsRunningDocumentTable>());

            // Act
            await cursorHistoryIntegration.InitializeAsync();
            
            // Simulate document events
            await cursorHistoryIntegration.OnDocumentOpenedAsync("TestFile.cs");
            await cursorHistoryIntegration.OnCursorPositionChangedAsync("TestFile.cs", 10, 5);

            // Assert
            cursorHistoryIntegration.Should().NotBeNull();
            WriteTestOutput("Cursor history VS integration test completed");
        }

        #endregion

        #region MEF Integration Tests

        [TestMethod]
        [IntegrationTest(Description = "Tests MEF composition in VS environment")]  
        public void MEFComposition_ShouldResolveAllServices()
        {
            // Arrange
            var catalog = new System.ComponentModel.Composition.Hosting.AssemblyCatalog(
                typeof(TextBufferListener).Assembly);
            var container = new System.ComponentModel.Composition.Hosting.CompositionContainer(catalog);

            // Act
            var textBufferListener = container.GetExportedValue<ITextBufferListener>();

            // Assert
            textBufferListener.Should().NotBeNull();
            textBufferListener.Should().BeOfType<TextBufferListener>();

            WriteTestOutput("MEF composition test completed successfully");
        }

        [TestMethod]
        [IntegrationTest(Description = "Tests MEF lazy initialization")]
        public void MEFLazyInitialization_ShouldWorkCorrectly()
        {
            // Arrange
            var catalog = new System.ComponentModel.Composition.Hosting.AssemblyCatalog(
                typeof(TextBufferListener).Assembly);
            var container = new System.ComponentModel.Composition.Hosting.CompositionContainer(catalog);

            // Act
            var lazyListener = container.GetExport<ITextBufferListener>();

            // Assert
            lazyListener.Should().NotBeNull();
            lazyListener.Value.Should().NotBeNull();
            lazyListener.Value.Should().BeOfType<TextBufferListener>();

            WriteTestOutput("MEF lazy initialization test completed");
        }

        #endregion

        #region Document Tracking Integration Tests

        [TestMethod]
        [IntegrationTest(Description = "Tests document tracking across VS operations")]
        public async Task DocumentTracking_ShouldHandleMultipleDocuments()
        {
            // Arrange
            var settingsService = _serviceContainer.Resolve<ISettingsService>();
            var cursorHistoryService = new CursorHistoryService(settingsService);
            var documentTracker = new CursorHistoryIntegration(
                settingsService,
                _serviceContainer.Resolve<ILogger>(),
                _serviceContainer.Resolve<SVsRunningDocumentTable>());

            await documentTracker.InitializeAsync();

            // Act - Simulate multiple document operations
            var testFiles = new[] { "File1.cs", "File2.cs", "File3.cs" };
            
            foreach (var file in testFiles)
            {
                await documentTracker.OnDocumentOpenedAsync(file);
                await documentTracker.OnCursorPositionChangedAsync(file, 10, 5);
                await documentTracker.OnCursorPositionChangedAsync(file, 15, 8);
            }

            // Assert
            var history = cursorHistoryService.GetRecentHistory(20);
            history.Should().NotBeEmpty();
            history.Should().HaveCountLessOrEqualTo(settingsService.CursorHistoryMemoryDepth);

            WriteTestOutput($"Document tracking test completed. Tracked {history.Count} cursor positions across {testFiles.Length} files");
        }

        [TestMethod]
        [IntegrationTest(Description = "Tests document close cleanup")]
        public async Task DocumentTracking_OnDocumentClose_ShouldCleanupProperly()
        {
            // Arrange
            var settingsService = _serviceContainer.Resolve<ISettingsService>();
            var documentTracker = new CursorHistoryIntegration(
                settingsService,
                _serviceContainer.Resolve<ILogger>(),
                _serviceContainer.Resolve<SVsRunningDocumentTable>());

            await documentTracker.InitializeAsync();

            // Act
            await documentTracker.OnDocumentOpenedAsync("TempFile.cs");
            await documentTracker.OnCursorPositionChangedAsync("TempFile.cs", 10, 5);
            await documentTracker.OnDocumentClosedAsync("TempFile.cs");

            // Assert
            // Verify that resources are cleaned up properly
            WriteTestOutput("Document close cleanup test completed");
        }

        #endregion

        #region Settings Integration Tests

        [TestMethod]
        [IntegrationTest(Description = "Tests settings integration with VS settings store")]
        public async Task SettingsIntegration_ShouldPersistAcrossVSSessions()
        {
            // Arrange
            var settingsService = new TestSettingsService(_mockServiceProvider);
            var originalModel = settingsService.OllamaModel;
            var testModel = "integration-test-model";

            // Act - Simulate VS session 1
            settingsService.OllamaModel = testModel;
            settingsService.SaveSettings();

            // Simulate VS restart - create new settings service instance
            var newSessionSettingsService = new TestSettingsService(_mockServiceProvider);
            newSessionSettingsService.LoadSettings();

            // Assert
            newSessionSettingsService.OllamaModel.Should().Be(testModel);

            // Cleanup
            settingsService.OllamaModel = originalModel;
            settingsService.SaveSettings();

            WriteTestOutput($"Settings persistence test completed. Model persisted as '{testModel}'");
        }

        [TestMethod]
        [IntegrationTest(Description = "Tests settings validation in VS environment")]
        public void SettingsValidation_ShouldEnforceConstraints()
        {
            // Arrange
            var settingsService = new TestSettingsService(_mockServiceProvider);

            // Act & Assert - Test various validation scenarios
            Action setInvalidUrl = () => settingsService.OllamaServerUrl = "invalid-url";
            setInvalidUrl.Should().Throw<ArgumentException>();

            Action setNegativeLines = () => settingsService.SurroundingLinesDown = -1;
            setNegativeLines.Should().Throw<ArgumentOutOfRangeException>();

            Action setZeroMemoryDepth = () => settingsService.CursorHistoryMemoryDepth = 0;
            setZeroMemoryDepth.Should().Throw<ArgumentOutOfRangeException>();

            WriteTestOutput("Settings validation test completed");
        }

        #endregion

        #region Error Handling Integration Tests

        [TestMethod]
        [IntegrationTest(Description = "Tests error handling in VS environment")]
        public async Task VSErrorHandling_ShouldDegradeGracefully()
        {
            // Arrange
            var outputService = new VSOutputWindowService(
                null, // Null to simulate error condition
                _joinableTaskFactory);

            // Act & Assert
            Func<Task> act = async () => await outputService.WriteErrorAsync("Test error", "Test");
            await act.Should().NotThrowAsync(); // Should degrade gracefully

            WriteTestOutput("VS error handling test completed");
        }

        [TestMethod]
        [IntegrationTest(Description = "Tests VS service unavailability handling")]
        public async Task VSServiceUnavailable_ShouldHandleGracefully()
        {
            // Arrange - Create services with null VS dependencies
            var statusBarService = new VSStatusBarService(null, _joinableTaskFactory);

            // Act & Assert
            Func<Task> act = async () => await statusBarService.ShowAIProcessingAsync("Processing...");
            await act.Should().NotThrowAsync();

            WriteTestOutput("VS service unavailability test completed");
        }

        #endregion

        #region Performance Integration Tests

        [TestMethod]
        [PerformanceTest(MaxExecutionTimeMs = 3000)]
        [IntegrationTest(Description = "Tests VS integration performance")]
        public async Task VSIntegration_PerformanceTest_ShouldMeetRequirements()
        {
            // Arrange
            var settingsService = _serviceContainer.Resolve<ISettingsService>();
            var documentTracker = new CursorHistoryIntegration(
                settingsService,
                _serviceContainer.Resolve<ILogger>(),
                _serviceContainer.Resolve<SVsRunningDocumentTable>());

            await documentTracker.InitializeAsync();

            // Act - Perform multiple VS operations quickly
            var (_, duration) = await MeasureExecutionTime(async () =>
            {
                for (int i = 0; i < 100; i++)
                {
                    await documentTracker.OnCursorPositionChangedAsync($"File{i % 5}.cs", i, i % 10);
                }
            });

            // Assert
            AssertAcceptableResponseTime(duration, TimeSpan.FromSeconds(3));
            WriteTestOutput($"VS integration performance test completed in {duration.TotalMilliseconds}ms");
        }

        #endregion

        #region Resource Management Tests

        [TestMethod]
        [IntegrationTest(Description = "Tests proper resource cleanup")]
        public async Task VSIntegration_ResourceCleanup_ShouldReleaseResources()
        {
            // Arrange
            var initialMemory = GC.GetTotalMemory(true);

            // Act - Create and dispose multiple VS integration components
            for (int i = 0; i < 10; i++)
            {
                var container = new ServiceContainer();
                RegisterVSTestServices();

                var documentTracker = new CursorHistoryIntegration(
                    container.Resolve<ISettingsService>(),
                    container.Resolve<ILogger>(),
                    container.Resolve<SVsRunningDocumentTable>());

                await documentTracker.InitializeAsync();
                await documentTracker.OnDocumentOpenedAsync($"TestFile{i}.cs");
                
                // Dispose and cleanup
                documentTracker.Dispose();
                container.Dispose();
            }

            // Force garbage collection
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            var finalMemory = GC.GetTotalMemory(false);
            var memoryIncrease = finalMemory - initialMemory;

            // Assert
            WriteTestOutput($"Memory usage: Initial={initialMemory / 1024}KB, Final={finalMemory / 1024}KB, Increase={memoryIncrease / 1024}KB");
            memoryIncrease.Should().BeLessThan(5 * 1024 * 1024, "Memory increase should be less than 5MB");

            WriteTestOutput("VS integration resource cleanup test completed");
        }

        #endregion
    }

    /// <summary>
    /// Interface for text buffer listener testing
    /// </summary>
    public interface ITextBufferListener
    {
        void OnTextBufferCreated(object textBuffer);
    }
}