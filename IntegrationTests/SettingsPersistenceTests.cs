using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using OllamaAssistant.Services.Implementation;
using OllamaAssistant.Tests.TestHelpers;

namespace OllamaAssistant.Tests.IntegrationTests
{
    [TestClass]
    public class SettingsPersistenceTests
    {
        private SettingsService _settingsService;
        private Mock<Microsoft.VisualStudio.Shell.AsyncPackage> _mockPackage;

        [TestInitialize]
        public void Initialize()
        {
            _mockPackage = new Mock<Microsoft.VisualStudio.Shell.AsyncPackage>();
            _settingsService = new SettingsService(_mockPackage.Object);
        }

        [TestCleanup]
        public void Cleanup()
        {
            _settingsService?.Dispose();
        }

        [TestMethod]
        public async Task LoadSettingsAsync_WithDefaultValues_ShouldSetCorrectDefaults()
        {
            // Act
            await _settingsService.LoadSettingsAsync();

            // Assert
            _settingsService.IsEnabled.Should().BeTrue();
            _settingsService.OllamaEndpoint.Should().Be("http://localhost:11434");
            _settingsService.DefaultModel.Should().Be("codellama");
            _settingsService.SurroundingLinesUp.Should().Be(3);
            _settingsService.SurroundingLinesDown.Should().Be(2);
            _settingsService.CursorHistoryMemoryDepth.Should().Be(5);
            _settingsService.EnableAutoSuggestions.Should().BeTrue();
            _settingsService.EnableJumpRecommendations.Should().BeTrue();
            _settingsService.EnableVerboseLogging.Should().BeFalse();
            _settingsService.RequestTimeout.Should().Be(30000);
            _settingsService.JumpKeyBinding.Should().Be("Tab");
            _settingsService.JumpNotificationTimeout.Should().Be(5000);
            _settingsService.ShowJumpPreview.Should().BeTrue();
            _settingsService.NotificationOpacity.Should().Be(80);
            _settingsService.AnimateNotifications.Should().BeTrue();
        }

        [TestMethod]
        public async Task SaveSettingsAsync_AfterModification_ShouldPersistChanges()
        {
            // Arrange
            await _settingsService.LoadSettingsAsync();
            
            // Modify settings
            _settingsService.IsEnabled = false;
            _settingsService.OllamaEndpoint = "http://custom-host:12345";
            _settingsService.DefaultModel = "llama2";
            _settingsService.SurroundingLinesUp = 5;
            _settingsService.SurroundingLinesDown = 3;
            _settingsService.CursorHistoryMemoryDepth = 10;
            _settingsService.EnableAutoSuggestions = false;
            _settingsService.EnableJumpRecommendations = false;
            _settingsService.EnableVerboseLogging = true;
            _settingsService.RequestTimeout = 60000;
            _settingsService.JumpKeyBinding = "Ctrl+J";
            _settingsService.JumpNotificationTimeout = 10000;
            _settingsService.ShowJumpPreview = false;
            _settingsService.NotificationOpacity = 90;
            _settingsService.AnimateNotifications = false;

            // Act
            await _settingsService.SaveSettingsAsync();

            // Create new instance to test persistence
            var newSettingsService = new SettingsService(_mockPackage.Object);
            await newSettingsService.LoadSettingsAsync();

            // Assert
            newSettingsService.IsEnabled.Should().BeFalse();
            newSettingsService.OllamaEndpoint.Should().Be("http://custom-host:12345");
            newSettingsService.DefaultModel.Should().Be("llama2");
            newSettingsService.SurroundingLinesUp.Should().Be(5);
            newSettingsService.SurroundingLinesDown.Should().Be(3);
            newSettingsService.CursorHistoryMemoryDepth.Should().Be(10);
            newSettingsService.EnableAutoSuggestions.Should().BeFalse();
            newSettingsService.EnableJumpRecommendations.Should().BeFalse();
            newSettingsService.EnableVerboseLogging.Should().BeTrue();
            newSettingsService.RequestTimeout.Should().Be(60000);
            newSettingsService.JumpKeyBinding.Should().Be("Ctrl+J");
            newSettingsService.JumpNotificationTimeout.Should().Be(10000);
            newSettingsService.ShowJumpPreview.Should().BeFalse();
            newSettingsService.NotificationOpacity.Should().Be(90);
            newSettingsService.AnimateNotifications.Should().BeFalse();

            newSettingsService.Dispose();
        }

        [TestMethod]
        public async Task ResetToDefaultsAsync_ShouldRestoreAllDefaults()
        {
            // Arrange
            await _settingsService.LoadSettingsAsync();
            
            // Modify all settings
            _settingsService.IsEnabled = false;
            _settingsService.OllamaEndpoint = "http://custom-host:12345";
            _settingsService.DefaultModel = "custom-model";
            _settingsService.SurroundingLinesUp = 10;
            _settingsService.SurroundingLinesDown = 10;
            _settingsService.CursorHistoryMemoryDepth = 20;
            _settingsService.EnableAutoSuggestions = false;
            _settingsService.EnableJumpRecommendations = false;
            _settingsService.EnableVerboseLogging = true;
            _settingsService.RequestTimeout = 120000;
            _settingsService.JumpKeyBinding = "Custom";
            _settingsService.JumpNotificationTimeout = 15000;
            _settingsService.ShowJumpPreview = false;
            _settingsService.NotificationOpacity = 100;
            _settingsService.AnimateNotifications = false;

            // Act
            await _settingsService.ResetToDefaultsAsync();

            // Assert - All settings should be back to defaults
            _settingsService.IsEnabled.Should().BeTrue();
            _settingsService.OllamaEndpoint.Should().Be("http://localhost:11434");
            _settingsService.DefaultModel.Should().Be("codellama");
            _settingsService.SurroundingLinesUp.Should().Be(3);
            _settingsService.SurroundingLinesDown.Should().Be(2);
            _settingsService.CursorHistoryMemoryDepth.Should().Be(5);
            _settingsService.EnableAutoSuggestions.Should().BeTrue();
            _settingsService.EnableJumpRecommendations.Should().BeTrue();
            _settingsService.EnableVerboseLogging.Should().BeFalse();
            _settingsService.RequestTimeout.Should().Be(30000);
            _settingsService.JumpKeyBinding.Should().Be("Tab");
            _settingsService.JumpNotificationTimeout.Should().Be(5000);
            _settingsService.ShowJumpPreview.Should().BeTrue();
            _settingsService.NotificationOpacity.Should().Be(80);
            _settingsService.AnimateNotifications.Should().BeTrue();
        }

        [TestMethod]
        public void ValidateSettings_WithValidSettings_ShouldReturnTrue()
        {
            // Arrange
            _settingsService.OllamaEndpoint = "http://localhost:11434";
            _settingsService.DefaultModel = "codellama";
            _settingsService.SurroundingLinesUp = 3;
            _settingsService.SurroundingLinesDown = 2;
            _settingsService.CursorHistoryMemoryDepth = 5;
            _settingsService.RequestTimeout = 30000;
            _settingsService.JumpNotificationTimeout = 5000;
            _settingsService.NotificationOpacity = 80;

            // Act
            var isValid = _settingsService.ValidateSettings();

            // Assert
            isValid.Should().BeTrue();
        }

        [TestMethod]
        public void ValidateSettings_WithInvalidEndpoint_ShouldReturnFalse()
        {
            // Arrange
            _settingsService.OllamaEndpoint = "invalid-url";

            // Act
            var isValid = _settingsService.ValidateSettings();

            // Assert
            isValid.Should().BeFalse();
        }

        [TestMethod]
        public void ValidateSettings_WithNegativeLinesUp_ShouldReturnFalse()
        {
            // Arrange
            _settingsService.SurroundingLinesUp = -1;

            // Act
            var isValid = _settingsService.ValidateSettings();

            // Assert
            isValid.Should().BeFalse();
        }

        [TestMethod]
        public void ValidateSettings_WithNegativeLinesDown_ShouldReturnFalse()
        {
            // Arrange
            _settingsService.SurroundingLinesDown = -1;

            // Act
            var isValid = _settingsService.ValidateSettings();

            // Assert
            isValid.Should().BeFalse();
        }

        [TestMethod]
        public void ValidateSettings_WithZeroMemoryDepth_ShouldReturnFalse()
        {
            // Arrange
            _settingsService.CursorHistoryMemoryDepth = 0;

            // Act
            var isValid = _settingsService.ValidateSettings();

            // Assert
            isValid.Should().BeFalse();
        }

        [TestMethod]
        public void ValidateSettings_WithNegativeTimeout_ShouldReturnFalse()
        {
            // Arrange
            _settingsService.RequestTimeout = -1000;

            // Act
            var isValid = _settingsService.ValidateSettings();

            // Assert
            isValid.Should().BeFalse();
        }

        [TestMethod]
        public void ValidateSettings_WithInvalidOpacity_ShouldReturnFalse()
        {
            // Arrange
            _settingsService.NotificationOpacity = 150; // Over 100

            // Act
            var isValid = _settingsService.ValidateSettings();

            // Assert
            isValid.Should().BeFalse();
        }

        [TestMethod]
        public async Task SettingsChanged_Event_ShouldBeRaisedOnPropertyChange()
        {
            // Arrange
            await _settingsService.LoadSettingsAsync();
            
            string changedProperty = null;
            _settingsService.SettingsChanged += (sender, args) =>
            {
                changedProperty = args.SettingName;
            };

            // Act
            _settingsService.IsEnabled = false;

            // Assert
            changedProperty.Should().Be(nameof(_settingsService.IsEnabled));
        }

        [TestMethod]
        public async Task SettingsChanged_Event_ShouldNotBeRaisedForSameValue()
        {
            // Arrange
            await _settingsService.LoadSettingsAsync();
            
            var eventRaised = false;
            _settingsService.SettingsChanged += (sender, args) =>
            {
                eventRaised = true;
            };

            var currentValue = _settingsService.IsEnabled;

            // Act
            _settingsService.IsEnabled = currentValue; // Set to same value

            // Assert
            eventRaised.Should().BeFalse();
        }

        [TestMethod]
        public async Task ConcurrentSettingsOperations_ShouldHandleGracefully()
        {
            // Arrange
            await _settingsService.LoadSettingsAsync();

            // Act - Perform concurrent operations
            var tasks = new[]
            {
                Task.Run(async () =>
                {
                    for (int i = 0; i < 10; i++)
                    {
                        _settingsService.SurroundingLinesUp = i;
                        await Task.Delay(10);
                    }
                }),
                Task.Run(async () =>
                {
                    for (int i = 0; i < 10; i++)
                    {
                        _settingsService.SurroundingLinesDown = i;
                        await Task.Delay(10);
                    }
                }),
                Task.Run(async () =>
                {
                    for (int i = 0; i < 5; i++)
                    {
                        await _settingsService.SaveSettingsAsync();
                        await Task.Delay(20);
                    }
                })
            };

            // Assert - Should complete without throwing
            await TestUtilities.AssertDoesNotThrowAsync(async () =>
            {
                await Task.WhenAll(tasks);
            });
        }

        [TestMethod]
        public async Task LoadSettings_Performance_ShouldBeReasonablyFast()
        {
            // Act
            var duration = await TestUtilities.MeasureExecutionTimeAsync(async () =>
            {
                for (int i = 0; i < 100; i++)
                {
                    await _settingsService.LoadSettingsAsync();
                }
            });

            // Assert
            duration.Should().BeLessThan(System.TimeSpan.FromSeconds(2));
        }

        [TestMethod]
        public async Task SaveSettings_Performance_ShouldBeReasonablyFast()
        {
            // Arrange
            await _settingsService.LoadSettingsAsync();

            // Act
            var duration = await TestUtilities.MeasureExecutionTimeAsync(async () =>
            {
                for (int i = 0; i < 100; i++)
                {
                    _settingsService.SurroundingLinesUp = i % 10;
                    await _settingsService.SaveSettingsAsync();
                }
            });

            // Assert
            duration.Should().BeLessThan(System.TimeSpan.FromSeconds(5));
        }

        [TestMethod]
        public void ValidateSettings_WithBoundaryValues_ShouldHandleCorrectly()
        {
            // Test minimum valid values
            _settingsService.SurroundingLinesUp = 0;
            _settingsService.SurroundingLinesDown = 0;
            _settingsService.CursorHistoryMemoryDepth = 1;
            _settingsService.RequestTimeout = 1000;
            _settingsService.NotificationOpacity = 0;

            _settingsService.ValidateSettings().Should().BeTrue();

            // Test maximum reasonable values
            _settingsService.SurroundingLinesUp = 100;
            _settingsService.SurroundingLinesDown = 100;
            _settingsService.CursorHistoryMemoryDepth = 1000;
            _settingsService.RequestTimeout = 300000; // 5 minutes
            _settingsService.NotificationOpacity = 100;

            _settingsService.ValidateSettings().Should().BeTrue();
        }
    }
}