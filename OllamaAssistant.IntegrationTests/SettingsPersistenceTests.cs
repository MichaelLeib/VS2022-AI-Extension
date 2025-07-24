using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OllamaAssistant.IntegrationTests.TestUtilities;
using OllamaAssistant.Services.Implementation;
using OllamaAssistant.Tests.TestUtilities;

namespace OllamaAssistant.IntegrationTests
{
    [TestClass]
    [TestCategory(TestCategories.Integration)]
    [TestCategory(IntegrationTestCategories.FileSystem)]
    public class SettingsPersistenceTests : BaseIntegrationTest
    {
        private string _testSettingsDirectory;
        private List<TestSettingsService> _createdServices;

        [TestInitialize]
        public override void TestInitialize()
        {
            base.TestInitialize();
            
            // Create temporary directory for test settings
            _testSettingsDirectory = Path.Combine(Path.GetTempPath(), "OllamaAssistant.Tests", Guid.NewGuid().ToString());
            Directory.CreateDirectory(_testSettingsDirectory);
            
            _createdServices = new List<TestSettingsService>();
        }

        [TestCleanup]
        public override void TestCleanup()
        {
            // Dispose all created services
            foreach (var service in _createdServices)
            {
                service?.Dispose();
            }
            _createdServices.Clear();

            // Clean up test directory
            if (Directory.Exists(_testSettingsDirectory))
            {
                try
                {
                    Directory.Delete(_testSettingsDirectory, true);
                }
                catch
                {
                    // Ignore cleanup errors
                }
            }

            base.TestCleanup();
        }

        #region Settings Persistence Tests

        [TestMethod]
        [IntegrationTest(Description = "Tests basic settings persistence")]
        public void SettingsPersistence_BasicProperties_ShouldPersistCorrectly()
        {
            // Arrange
            var service1 = CreateTestSettingsService();
            var originalValues = CaptureCurrentSettings(service1);

            // Modify settings
            service1.OllamaServerUrl = "http://test-server:8080";
            service1.OllamaModel = "test-model-v2";
            service1.SurroundingLinesUp = 7;
            service1.SurroundingLinesDown = 5;
            service1.CursorHistoryMemoryDepth = 25;
            service1.EnableCodeCompletions = false;
            service1.EnableCursorJumps = false;

            // Act - Save and create new service instance
            service1.SaveSettings();
            var service2 = CreateTestSettingsService();
            service2.LoadSettings();

            // Assert
            service2.OllamaServerUrl.Should().Be("http://test-server:8080");
            service2.OllamaModel.Should().Be("test-model-v2");
            service2.SurroundingLinesUp.Should().Be(7);
            service2.SurroundingLinesDown.Should().Be(5);
            service2.CursorHistoryMemoryDepth.Should().Be(25);
            service2.EnableCodeCompletions.Should().BeFalse();
            service2.EnableCursorJumps.Should().BeFalse();

            // Cleanup - restore original values
            RestoreSettings(service1, originalValues);
            service1.SaveSettings();

            WriteTestOutput("Basic settings persistence test completed");
        }

        [TestMethod]
        [IntegrationTest(Description = "Tests settings persistence with default values")]
        public void SettingsPersistence_DefaultValues_ShouldPersistCorrectly()
        {
            // Arrange
            var service1 = CreateTestSettingsService();

            // Act - Save defaults and create new service
            service1.SaveSettings();
            var service2 = CreateTestSettingsService();
            service2.LoadSettings();

            // Assert - Should have default values
            service2.OllamaServerUrl.Should().Be("http://localhost:11434");
            service2.OllamaModel.Should().Be("codellama");
            service2.SurroundingLinesUp.Should().Be(3);
            service2.SurroundingLinesDown.Should().Be(2);
            service2.CursorHistoryMemoryDepth.Should().Be(10);
            service2.EnableCodeCompletions.Should().BeTrue();
            service2.EnableCursorJumps.Should().BeTrue();

            WriteTestOutput("Default values persistence test completed");
        }

        [TestMethod]
        [IntegrationTest(Description = "Tests settings persistence across multiple sessions")]
        public void SettingsPersistence_MultipleSessions_ShouldMaintainConsistency()
        {
            // Arrange
            var testValues = new Dictionary<string, object>
            {
                { "OllamaServerUrl", "http://session-test:9999" },
                { "OllamaModel", "session-test-model" },
                { "SurroundingLinesUp", 12 },
                { "SurroundingLinesDown", 8 },
                { "CursorHistoryMemoryDepth", 30 }
            };

            // Act & Assert - Simulate multiple VS sessions
            for (int session = 1; session <= 5; session++)
            {
                WriteTestOutput($"Testing session {session}");

                var service = CreateTestSettingsService();
                service.LoadSettings();

                if (session == 1)
                {
                    // First session - set values
                    service.OllamaServerUrl = (string)testValues["OllamaServerUrl"];
                    service.OllamaModel = (string)testValues["OllamaModel"];
                    service.SurroundingLinesUp = (int)testValues["SurroundingLinesUp"];
                    service.SurroundingLinesDown = (int)testValues["SurroundingLinesDown"];
                    service.CursorHistoryMemoryDepth = (int)testValues["CursorHistoryMemoryDepth"];
                    service.SaveSettings();
                }
                else
                {
                    // Subsequent sessions - verify persistence
                    service.OllamaServerUrl.Should().Be((string)testValues["OllamaServerUrl"]);
                    service.OllamaModel.Should().Be((string)testValues["OllamaModel"]);
                    service.SurroundingLinesUp.Should().Be((int)testValues["SurroundingLinesUp"]);
                    service.SurroundingLinesDown.Should().Be((int)testValues["SurroundingLinesDown"]);
                    service.CursorHistoryMemoryDepth.Should().Be((int)testValues["CursorHistoryMemoryDepth"]);
                }

                service.Dispose();
            }

            WriteTestOutput("Multiple sessions persistence test completed");
        }

        [TestMethod]
        [IntegrationTest(Description = "Tests concurrent settings access")]
        public async Task SettingsPersistence_ConcurrentAccess_ShouldHandleCorrectly()
        {
            // Arrange
            const int numberOfServices = 5;
            var services = new TestSettingsService[numberOfServices];
            var tasks = new Task[numberOfServices];

            // Create multiple service instances
            for (int i = 0; i < numberOfServices; i++)
            {
                services[i] = CreateTestSettingsService();
            }

            // Act - Concurrent access
            for (int i = 0; i < numberOfServices; i++)
            {
                int serviceIndex = i;
                tasks[i] = Task.Run(() =>
                {
                    var service = services[serviceIndex];
                    service.LoadSettings();
                    
                    // Modify different settings per service
                    service.OllamaModel = $"concurrent-model-{serviceIndex}";
                    service.SurroundingLinesUp = 5 + serviceIndex;
                    
                    service.SaveSettings();
                });
            }

            await Task.WhenAll(tasks);

            // Assert - Create new service to verify final state
            var verificationService = CreateTestSettingsService();
            verificationService.LoadSettings();

            // The final state should be one of the concurrent modifications
            verificationService.OllamaModel.Should().StartWith("concurrent-model-");
            verificationService.SurroundingLinesUp.Should().BeInRange(5, 5 + numberOfServices - 1);

            // Cleanup
            foreach (var service in services)
            {
                service.Dispose();
            }

            WriteTestOutput("Concurrent settings access test completed");
        }

        #endregion

        #region Settings Validation Persistence Tests

        [TestMethod]
        [IntegrationTest(Description = "Tests validation error handling during persistence")]
        public void SettingsPersistence_ValidationErrors_ShouldHandleGracefully()
        {
            // Arrange
            var service = CreateTestSettingsService();
            var originalModel = service.OllamaModel;

            // Act & Assert - Try to set invalid values
            Action setInvalidUrl = () => service.OllamaServerUrl = "not-a-valid-url";
            setInvalidUrl.Should().Throw<ArgumentException>();

            Action setNegativeLines = () => service.SurroundingLinesDown = -5;
            setNegativeLines.Should().Throw<ArgumentOutOfRangeException>();

            // Verify original values are preserved
            service.OllamaModel.Should().Be(originalModel);
            service.SurroundingLinesDown.Should().BeGreaterThan(0);

            WriteTestOutput("Settings validation error handling test completed");
        }

        [TestMethod]
        [IntegrationTest(Description = "Tests settings bounds validation")]
        public void SettingsPersistence_BoundsValidation_ShouldEnforceLimits()
        {
            // Arrange
            var service = CreateTestSettingsService();

            // Act & Assert - Test boundary conditions
            Action setMaxLines = () => service.SurroundingLinesUp = 1000;
            setMaxLines.Should().Throw<ArgumentOutOfRangeException>();

            Action setMaxMemoryDepth = () => service.CursorHistoryMemoryDepth = 10000;
            setMaxMemoryDepth.Should().Throw<ArgumentOutOfRangeException>();

            // Test valid boundaries
            service.SurroundingLinesUp = 20; // Should be valid
            service.CursorHistoryMemoryDepth = 100; // Should be valid

            service.SurroundingLinesUp.Should().Be(20);
            service.CursorHistoryMemoryDepth.Should().Be(100);

            WriteTestOutput("Settings bounds validation test completed");
        }

        #endregion

        #region Settings Change Notification Tests

        [TestMethod]
        [IntegrationTest(Description = "Tests settings change notifications")]
        public void SettingsPersistence_ChangeNotifications_ShouldFireCorrectly()
        {
            // Arrange
            var service = CreateTestSettingsService();
            var changeEvents = new List<string>();

            service.SettingsChanged += (sender, args) =>
            {
                changeEvents.Add(args.SettingName);
            };

            // Act
            service.OllamaModel = "notification-test-model";
            service.SurroundingLinesUp = 15;
            service.EnableCodeCompletions = false;

            // Assert
            changeEvents.Should().Contain(nameof(service.OllamaModel));
            changeEvents.Should().Contain(nameof(service.SurroundingLinesUp));
            changeEvents.Should().Contain(nameof(service.EnableCodeCompletions));

            WriteTestOutput($"Settings change notifications test completed. Received {changeEvents.Count} notifications");
        }

        [TestMethod]
        [IntegrationTest(Description = "Tests settings change notification persistence")]
        public void SettingsPersistence_NotificationAfterLoad_ShouldWork()
        {
            // Arrange
            var service1 = CreateTestSettingsService();
            service1.OllamaModel = "notification-persistence-test";
            service1.SaveSettings();

            var service2 = CreateTestSettingsService();
            var changeNotified = false;

            service2.SettingsChanged += (sender, args) =>
            {
                if (args.SettingName == nameof(service2.OllamaModel))
                    changeNotified = true;
            };

            // Act
            service2.LoadSettings(); // Should not fire change events for loading
            service2.OllamaModel = "modified-after-load"; // Should fire change event

            // Assert
            changeNotified.Should().BeTrue();

            WriteTestOutput("Settings change notification after load test completed");
        }

        #endregion

        #region Error Recovery Tests

        [TestMethod]
        [IntegrationTest(Description = "Tests recovery from corrupted settings")]
        public void SettingsPersistence_CorruptedSettings_ShouldRecoverToDefaults()
        {
            // Arrange
            var service1 = CreateTestSettingsService();
            
            // Save valid settings first
            service1.OllamaModel = "test-model-before-corruption";
            service1.SaveSettings();

            // Simulate corruption by creating service with corrupted backing store
            var service2 = CreateCorruptedSettingsService();

            // Act
            service2.LoadSettings(); // Should recover to defaults

            // Assert
            service2.OllamaServerUrl.Should().Be("http://localhost:11434"); // Default
            service2.OllamaModel.Should().Be("codellama"); // Default
            service2.SurroundingLinesUp.Should().Be(3); // Default

            WriteTestOutput("Corrupted settings recovery test completed");
        }

        [TestMethod]
        [IntegrationTest(Description = "Tests recovery from inaccessible settings store")]
        public void SettingsPersistence_InaccessibleStore_ShouldUseDefaults()
        {
            // Arrange & Act
            var service = CreateInaccessibleSettingsService();
            service.LoadSettings();

            // Assert - Should use defaults when store is inaccessible
            service.OllamaServerUrl.Should().Be("http://localhost:11434");
            service.OllamaModel.Should().Be("codellama");

            WriteTestOutput("Inaccessible settings store recovery test completed");
        }

        #endregion

        #region Performance Tests

        [TestMethod]
        [PerformanceTest(MaxExecutionTimeMs = 1000)]
        [IntegrationTest(Description = "Tests settings load performance")]
        public async Task SettingsPersistence_LoadPerformance_ShouldBeEfficient()
        {
            // Arrange
            var service = CreateTestSettingsService();

            // Act
            var (_, duration) = await MeasureExecutionTime(async () =>
            {
                await Task.Run(() =>
                {
                    for (int i = 0; i < 100; i++)
                    {
                        service.LoadSettings();
                    }
                });
            });

            // Assert
            AssertAcceptableResponseTime(duration, TimeSpan.FromSeconds(1));
            WriteTestOutput($"Settings load performance test completed in {duration.TotalMilliseconds}ms for 100 loads");
        }

        [TestMethod]
        [PerformanceTest(MaxExecutionTimeMs = 2000)]
        [IntegrationTest(Description = "Tests settings save performance")]
        public async Task SettingsPersistence_SavePerformance_ShouldBeEfficient()
        {
            // Arrange
            var service = CreateTestSettingsService();

            // Act
            var (_, duration) = await MeasureExecutionTime(async () =>
            {
                await Task.Run(() =>
                {
                    for (int i = 0; i < 50; i++)
                    {
                        service.OllamaModel = $"perf-test-model-{i}";
                        service.SaveSettings();
                    }
                });
            });

            // Assert
            AssertAcceptableResponseTime(duration, TimeSpan.FromSeconds(2));
            WriteTestOutput($"Settings save performance test completed in {duration.TotalMilliseconds}ms for 50 saves");
        }

        #endregion

        #region Helper Methods

        private TestSettingsService CreateTestSettingsService()
        {
            var mockServiceProvider = MockFactory.CreateMockServiceProvider().Object;
            var service = new TestSettingsService(mockServiceProvider);
            _createdServices.Add(service);
            return service;
        }

        private TestSettingsService CreateCorruptedSettingsService()
        {
            var mockServiceProvider = MockFactory.CreateCorruptedServiceProvider().Object;
            var service = new TestSettingsService(mockServiceProvider);
            _createdServices.Add(service);
            return service;
        }

        private TestSettingsService CreateInaccessibleSettingsService()
        {
            var mockServiceProvider = MockFactory.CreateInaccessibleServiceProvider().Object;
            var service = new TestSettingsService(mockServiceProvider);
            _createdServices.Add(service);
            return service;
        }

        private Dictionary<string, object> CaptureCurrentSettings(TestSettingsService service)
        {
            return new Dictionary<string, object>
            {
                { nameof(service.OllamaServerUrl), service.OllamaServerUrl },
                { nameof(service.OllamaModel), service.OllamaModel },
                { nameof(service.SurroundingLinesUp), service.SurroundingLinesUp },
                { nameof(service.SurroundingLinesDown), service.SurroundingLinesDown },
                { nameof(service.CursorHistoryMemoryDepth), service.CursorHistoryMemoryDepth },
                { nameof(service.EnableCodeCompletions), service.EnableCodeCompletions },
                { nameof(service.EnableCursorJumps), service.EnableCursorJumps }
            };
        }

        private void RestoreSettings(TestSettingsService service, Dictionary<string, object> values)
        {
            service.OllamaServerUrl = (string)values[nameof(service.OllamaServerUrl)];
            service.OllamaModel = (string)values[nameof(service.OllamaModel)];
            service.SurroundingLinesUp = (int)values[nameof(service.SurroundingLinesUp)];
            service.SurroundingLinesDown = (int)values[nameof(service.SurroundingLinesDown)];
            service.CursorHistoryMemoryDepth = (int)values[nameof(service.CursorHistoryMemoryDepth)];
            service.EnableCodeCompletions = (bool)values[nameof(service.EnableCodeCompletions)];
            service.EnableCursorJumps = (bool)values[nameof(service.EnableCursorJumps)];
        }

        #endregion
    }
}