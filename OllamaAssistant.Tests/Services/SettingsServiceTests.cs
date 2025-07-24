using System;
using System.Windows.Forms;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using OllamaAssistant.Services.Implementation;
using OllamaAssistant.Services.Interfaces;
using OllamaAssistant.Tests.TestUtilities;

namespace OllamaAssistant.Tests.Services
{
    [TestClass]
    [TestCategory(TestCategories.Unit)]
    [TestCategory(TestCategories.Settings)]
    public class SettingsServiceTests : BaseTest
    {
        private Mock<IServiceProvider> _mockServiceProvider;
        private TestSettingsService _settingsService;

        protected override void OnTestInitialize()
        {
            _mockServiceProvider = new Mock<IServiceProvider>();
            _settingsService = new TestSettingsService(_mockServiceProvider.Object);
        }

        protected override void OnTestCleanup()
        {
            _settingsService?.Dispose();
        }

        #region Constructor Tests

        [TestMethod]
        public void Constructor_WithValidServiceProvider_ShouldCreateInstance()
        {
            // Arrange & Act
            var service = new TestSettingsService(_mockServiceProvider.Object);

            // Assert
            service.Should().NotBeNull();
            service.Dispose();
        }

        [TestMethod]
        public void Constructor_WithNullServiceProvider_ShouldThrowArgumentNullException()
        {
            // Arrange, Act & Assert
            Action act = () => new TestSettingsService(null);
            act.Should().Throw<ArgumentNullException>();
        }

        #endregion

        #region Property Tests

        [TestMethod]
        public void IsEnabled_DefaultValue_ShouldBeTrue()
        {
            // Act & Assert
            _settingsService.IsEnabled.Should().BeTrue();
        }

        [TestMethod]
        public void OllamaEndpoint_DefaultValue_ShouldBeLocalhost()
        {
            // Act & Assert
            _settingsService.OllamaEndpoint.Should().Be("http://localhost:11434");
        }

        [TestMethod]
        public void OllamaModel_DefaultValue_ShouldBeCodellama()
        {
            // Act & Assert
            _settingsService.OllamaModel.Should().Be("codellama");
        }

        [TestMethod]
        public void SurroundingLinesUp_DefaultValue_ShouldBe3()
        {
            // Act & Assert
            _settingsService.SurroundingLinesUp.Should().Be(3);
        }

        [TestMethod]
        public void SurroundingLinesDown_DefaultValue_ShouldBe2()
        {
            // Act & Assert
            _settingsService.SurroundingLinesDown.Should().Be(2);
        }

        [TestMethod]
        public void CursorHistoryMemoryDepth_DefaultValue_ShouldBe3()
        {
            // Act & Assert
            _settingsService.CursorHistoryMemoryDepth.Should().Be(3);
        }

        [TestMethod]
        public void MinimumConfidenceThreshold_DefaultValue_ShouldBe0Point7()
        {
            // Act & Assert
            _settingsService.MinimumConfidenceThreshold.Should().Be(0.7);
        }

        [TestMethod]
        public void EnableStreamingCompletions_DefaultValue_ShouldBeFalse()
        {
            // Act & Assert
            _settingsService.EnableStreamingCompletions.Should().BeFalse();
        }

        [TestMethod]
        public void FilterSensitiveData_DefaultValue_ShouldBeTrue()
        {
            // Act & Assert
            _settingsService.FilterSensitiveData.Should().BeTrue();
        }

        #endregion

        #region Property Validation Tests

        [TestMethod]
        public void SurroundingLinesUp_SetToNegativeValue_ShouldClampToZero()
        {
            // Act
            _settingsService.SurroundingLinesUp = -5;

            // Assert
            _settingsService.SurroundingLinesUp.Should().Be(0);
        }

        [TestMethod]
        public void SurroundingLinesUp_SetToValueAbove50_ShouldClampTo50()
        {
            // Act
            _settingsService.SurroundingLinesUp = 100;

            // Assert
            _settingsService.SurroundingLinesUp.Should().Be(50);
        }

        [TestMethod]
        public void SurroundingLinesDown_SetToNegativeValue_ShouldClampToZero()
        {
            // Act
            _settingsService.SurroundingLinesDown = -3;

            // Assert
            _settingsService.SurroundingLinesDown.Should().Be(0);
        }

        [TestMethod]
        public void SurroundingLinesDown_SetToValueAbove50_ShouldClampTo50()
        {
            // Act
            _settingsService.SurroundingLinesDown = 75;

            // Assert
            _settingsService.SurroundingLinesDown.Should().Be(50);
        }

        [TestMethod]
        public void CursorHistoryMemoryDepth_SetToZero_ShouldClampTo1()
        {
            // Act
            _settingsService.CursorHistoryMemoryDepth = 0;

            // Assert
            _settingsService.CursorHistoryMemoryDepth.Should().Be(1);
        }

        [TestMethod]
        public void CursorHistoryMemoryDepth_SetToValueAbove10_ShouldClampTo10()
        {
            // Act
            _settingsService.CursorHistoryMemoryDepth = 15;

            // Assert
            _settingsService.CursorHistoryMemoryDepth.Should().Be(10);
        }

        [TestMethod]
        public void OllamaTimeout_SetToValueBelow5000_ShouldClampTo5000()
        {
            // Act
            _settingsService.OllamaTimeout = 1000;

            // Assert
            _settingsService.OllamaTimeout.Should().Be(5000);
        }

        [TestMethod]
        public void OllamaTimeout_SetToValueAbove300000_ShouldClampTo300000()
        {
            // Act
            _settingsService.OllamaTimeout = 500000;

            // Assert
            _settingsService.OllamaTimeout.Should().Be(300000);
        }

        [TestMethod]
        public void MinimumConfidenceThreshold_SetToNegativeValue_ShouldClampToZero()
        {
            // Act
            _settingsService.MinimumConfidenceThreshold = -0.5;

            // Assert
            _settingsService.MinimumConfidenceThreshold.Should().Be(0.0);
        }

        [TestMethod]
        public void MinimumConfidenceThreshold_SetToValueAbove1_ShouldClampTo1()
        {
            // Act
            _settingsService.MinimumConfidenceThreshold = 1.5;

            // Assert
            _settingsService.MinimumConfidenceThreshold.Should().Be(1.0);
        }

        [TestMethod]
        public void TypingDebounceDelay_SetToValueBelow100_ShouldClampTo100()
        {
            // Act
            _settingsService.TypingDebounceDelay = 50;

            // Assert
            _settingsService.TypingDebounceDelay.Should().Be(100);
        }

        [TestMethod]
        public void TypingDebounceDelay_SetToValueAbove2000_ShouldClampTo2000()
        {
            // Act
            _settingsService.TypingDebounceDelay = 5000;

            // Assert
            _settingsService.TypingDebounceDelay.Should().Be(2000);
        }

        [TestMethod]
        public void MaxSuggestions_SetToZero_ShouldClampTo1()
        {
            // Act
            _settingsService.MaxSuggestions = 0;

            // Assert
            _settingsService.MaxSuggestions.Should().Be(1);
        }

        [TestMethod]
        public void MaxSuggestions_SetToValueAbove20_ShouldClampTo20()
        {
            // Act
            _settingsService.MaxSuggestions = 50;

            // Assert
            _settingsService.MaxSuggestions.Should().Be(20);
        }

        [TestMethod]
        public void MaxRequestSizeKB_SetToValueBelow64_ShouldClampTo64()
        {
            // Act
            _settingsService.MaxRequestSizeKB = 32;

            // Assert
            _settingsService.MaxRequestSizeKB.Should().Be(64);
        }

        [TestMethod]
        public void MaxRequestSizeKB_SetToValueAbove2048_ShouldClampTo2048()
        {
            // Act
            _settingsService.MaxRequestSizeKB = 4096;

            // Assert
            _settingsService.MaxRequestSizeKB.Should().Be(2048);
        }

        [TestMethod]
        public void MaxConcurrentRequests_SetToZero_ShouldClampTo1()
        {
            // Act
            _settingsService.MaxConcurrentRequests = 0;

            // Assert
            _settingsService.MaxConcurrentRequests.Should().Be(1);
        }

        [TestMethod]
        public void MaxConcurrentRequests_SetToValueAbove10_ShouldClampTo10()
        {
            // Act
            _settingsService.MaxConcurrentRequests = 20;

            // Assert
            _settingsService.MaxConcurrentRequests.Should().Be(10);
        }

        [TestMethod]
        public void MaxRetryAttempts_SetToNegativeValue_ShouldClampToZero()
        {
            // Act
            _settingsService.MaxRetryAttempts = -1;

            // Assert
            _settingsService.MaxRetryAttempts.Should().Be(0);
        }

        [TestMethod]
        public void MaxRetryAttempts_SetToValueAbove10_ShouldClampTo10()
        {
            // Act
            _settingsService.MaxRetryAttempts = 15;

            // Assert
            _settingsService.MaxRetryAttempts.Should().Be(10);
        }

        #endregion

        #region Settings Change Events Tests

        [TestMethod]
        public void PropertyChange_ShouldRaiseSettingsChangedEvent()
        {
            // Arrange
            SettingsChangedEventArgs capturedArgs = null;
            _settingsService.SettingsChanged += (sender, args) => capturedArgs = args;

            // Act
            _settingsService.OllamaModel = "llama2";

            // Assert
            capturedArgs.Should().NotBeNull();
            capturedArgs.SettingName.Should().Be(nameof(ISettingsService.OllamaModel));
            capturedArgs.NewValue.Should().Be("llama2");
            capturedArgs.Category.Should().Be(SettingCategory.Connection);
        }

        [TestMethod]
        public void MultiplePropertyChanges_ShouldRaiseMultipleEvents()
        {
            // Arrange
            var eventCount = 0;
            _settingsService.SettingsChanged += (sender, args) => eventCount++;

            // Act
            _settingsService.OllamaModel = "llama2";
            _settingsService.SurroundingLinesUp = 5;
            _settingsService.EnableVerboseLogging = true;

            // Assert
            eventCount.Should().Be(3);
        }

        #endregion

        #region Validation Tests

        [TestMethod]
        public void ValidateSettings_WithValidSettings_ShouldReturnTrue()
        {
            // Arrange
            _settingsService.OllamaEndpoint = "http://localhost:11434";
            _settingsService.OllamaModel = "codellama";
            _settingsService.SurroundingLinesUp = 3;
            _settingsService.SurroundingLinesDown = 2;

            // Act
            var isValid = _settingsService.ValidateSettings();

            // Assert
            isValid.Should().BeTrue();
        }

        [TestMethod]
        public void ValidateSettings_WithEmptyEndpoint_ShouldReturnFalse()
        {
            // Arrange
            _settingsService.OllamaEndpoint = "";

            // Act
            var isValid = _settingsService.ValidateSettings();

            // Assert
            isValid.Should().BeFalse();
        }

        [TestMethod]
        public void ValidateSettings_WithInvalidEndpointUrl_ShouldReturnFalse()
        {
            // Arrange
            _settingsService.OllamaEndpoint = "not-a-valid-url";

            // Act
            var isValid = _settingsService.ValidateSettings();

            // Assert
            isValid.Should().BeFalse();
        }

        [TestMethod]
        public void ValidateSettings_WithEmptyModel_ShouldReturnFalse()
        {
            // Arrange
            _settingsService.OllamaModel = "";

            // Act
            var isValid = _settingsService.ValidateSettings();

            // Assert
            isValid.Should().BeFalse();
        }

        #endregion

        #region Generic Setting Methods Tests

        [TestMethod]
        public void GetSetting_WithExistingSetting_ShouldReturnValue()
        {
            // Arrange
            _settingsService.OllamaModel = "test-model";

            // Act
            var value = _settingsService.GetSetting<string>(nameof(ISettingsService.OllamaModel));

            // Assert
            value.Should().Be("test-model");
        }

        [TestMethod]
        public void GetSetting_WithNonExistentSetting_ShouldReturnDefault()
        {
            // Act
            var value = _settingsService.GetSetting<string>("NonExistentSetting");

            // Assert
            value.Should().BeNull();
        }

        [TestMethod]
        public void SetSetting_WithValidValue_ShouldUpdateSetting()
        {
            // Act
            _settingsService.SetSetting(nameof(ISettingsService.OllamaModel), "new-model");

            // Assert
            _settingsService.OllamaModel.Should().Be("new-model");
        }

        [TestMethod]
        public void SetSetting_ShouldRaiseSettingsChangedEvent()
        {
            // Arrange
            SettingsChangedEventArgs capturedArgs = null;
            _settingsService.SettingsChanged += (sender, args) => capturedArgs = args;

            // Act
            _settingsService.SetSetting(nameof(ISettingsService.EnableVerboseLogging), true);

            // Assert
            capturedArgs.Should().NotBeNull();
            capturedArgs.SettingName.Should().Be(nameof(ISettingsService.EnableVerboseLogging));
            capturedArgs.NewValue.Should().Be(true);
        }

        #endregion

        #region Alias Properties Tests

        [TestMethod]
        public void EnableAutoSuggestions_ShouldMapToCodePredictionEnabled()
        {
            // Act
            _settingsService.EnableAutoSuggestions = false;

            // Assert
            _settingsService.CodePredictionEnabled.Should().BeFalse();
            _settingsService.EnableAutoSuggestions.Should().BeFalse();
        }

        [TestMethod]
        public void EnableJumpRecommendations_ShouldMapToJumpRecommendationsEnabled()
        {
            // Act
            _settingsService.EnableJumpRecommendations = false;

            // Assert
            _settingsService.JumpRecommendationsEnabled.Should().BeFalse();
            _settingsService.EnableJumpRecommendations.Should().BeFalse();
        }

        #endregion

        #region ResetToDefaults Tests

        [TestMethod]
        public void ResetToDefaults_ShouldRestoreAllDefaultValues()
        {
            // Arrange
            _settingsService.OllamaModel = "changed-model";
            _settingsService.SurroundingLinesUp = 10;
            _settingsService.EnableVerboseLogging = true;

            // Act
            _settingsService.ResetToDefaults();

            // Assert
            _settingsService.OllamaModel.Should().Be("codellama");
            _settingsService.SurroundingLinesUp.Should().Be(3);
            _settingsService.EnableVerboseLogging.Should().BeFalse();
        }

        [TestMethod]
        public void ResetToDefaults_ShouldRaiseSettingsChangedEvent()
        {
            // Arrange
            var eventRaised = false;
            _settingsService.SettingsChanged += (sender, args) => 
            {
                if (args.SettingName == "All")
                    eventRaised = true;
            };

            // Act
            _settingsService.ResetToDefaults();

            // Assert
            eventRaised.Should().BeTrue();
        }

        #endregion

        #region Settings Category Tests

        [TestMethod]
        public void ConnectionSettings_ShouldHaveCorrectCategory()
        {
            // Arrange
            SettingsChangedEventArgs capturedArgs = null;
            _settingsService.SettingsChanged += (sender, args) => capturedArgs = args;

            // Act
            _settingsService.OllamaEndpoint = "http://test:11434";

            // Assert
            capturedArgs.Category.Should().Be(SettingCategory.Connection);
        }

        [TestMethod]
        public void ContextSettings_ShouldHaveCorrectCategory()
        {
            // Arrange
            SettingsChangedEventArgs capturedArgs = null;
            _settingsService.SettingsChanged += (sender, args) => capturedArgs = args;

            // Act
            _settingsService.SurroundingLinesUp = 5;

            // Assert
            capturedArgs.Category.Should().Be(SettingCategory.Context);
        }

        [TestMethod]
        public void PerformanceSettings_ShouldHaveCorrectCategory()
        {
            // Arrange
            SettingsChangedEventArgs capturedArgs = null;
            _settingsService.SettingsChanged += (sender, args) => capturedArgs = args;

            // Act
            _settingsService.TypingDebounceDelay = 1000;

            // Assert
            capturedArgs.Category.Should().Be(SettingCategory.Performance);
        }

        [TestMethod]
        public void FeatureSettings_ShouldHaveCorrectCategory()
        {
            // Arrange
            SettingsChangedEventArgs capturedArgs = null;
            _settingsService.SettingsChanged += (sender, args) => capturedArgs = args;

            // Act
            _settingsService.CodePredictionEnabled = false;

            // Assert
            capturedArgs.Category.Should().Be(SettingCategory.Features);
        }

        #endregion
    }

    /// <summary>
    /// Test implementation of SettingsService that doesn't require VS services
    /// </summary>
    public class TestSettingsService : ISettingsService, IDisposable
    {
        private readonly System.Collections.Generic.Dictionary<string, object> _settings;
        private bool _disposed;

        public TestSettingsService(IServiceProvider serviceProvider)
        {
            _settings = new System.Collections.Generic.Dictionary<string, object>();
            LoadDefaults();
        }

        private void LoadDefaults()
        {
            IsEnabled = true;
            OllamaEndpoint = "http://localhost:11434";
            OllamaModel = "codellama";
            SurroundingLinesUp = 3;
            SurroundingLinesDown = 2;
            JumpKey = Keys.Tab;
            CodePredictionEnabled = true;
            JumpRecommendationsEnabled = true;
            CursorHistoryMemoryDepth = 3;
            OllamaTimeout = 30000;
            MinimumConfidenceThreshold = 0.7;
            TypingDebounceDelay = 500;
            ShowConfidenceScores = false;
            EnableVerboseLogging = false;
            IncludeCursorHistory = true;
            MaxSuggestions = 5;
            EnableStreamingCompletions = false;
            FilterSensitiveData = true;
            MaxRequestSizeKB = 512;
            MaxConcurrentRequests = 3;
            MaxRetryAttempts = 3;
        }

        public bool IsEnabled { get; set; }
        public string OllamaEndpoint { get; set; }
        public int SurroundingLinesUp 
        { 
            get => GetSetting<int>(nameof(SurroundingLinesUp));
            set => SetSetting(nameof(SurroundingLinesUp), Math.Max(0, Math.Min(50, value)));
        }
        public int SurroundingLinesDown 
        { 
            get => GetSetting<int>(nameof(SurroundingLinesDown));
            set => SetSetting(nameof(SurroundingLinesDown), Math.Max(0, Math.Min(50, value)));
        }
        public Keys JumpKey { get; set; }
        public bool CodePredictionEnabled { get; set; }
        public bool JumpRecommendationsEnabled { get; set; }
        public bool EnableAutoSuggestions 
        { 
            get => CodePredictionEnabled; 
            set => CodePredictionEnabled = value; 
        }
        public bool EnableJumpRecommendations 
        { 
            get => JumpRecommendationsEnabled; 
            set => JumpRecommendationsEnabled = value; 
        }
        public int CursorHistoryMemoryDepth 
        { 
            get => GetSetting<int>(nameof(CursorHistoryMemoryDepth));
            set => SetSetting(nameof(CursorHistoryMemoryDepth), Math.Max(1, Math.Min(10, value)));
        }
        public string OllamaModel { get; set; }
        public int OllamaTimeout 
        { 
            get => GetSetting<int>(nameof(OllamaTimeout));
            set => SetSetting(nameof(OllamaTimeout), Math.Max(5000, Math.Min(300000, value)));
        }
        public double MinimumConfidenceThreshold 
        { 
            get => GetSetting<double>(nameof(MinimumConfidenceThreshold));
            set => SetSetting(nameof(MinimumConfidenceThreshold), Math.Max(0.0, Math.Min(1.0, value)));
        }
        public int TypingDebounceDelay 
        { 
            get => GetSetting<int>(nameof(TypingDebounceDelay));
            set => SetSetting(nameof(TypingDebounceDelay), Math.Max(100, Math.Min(2000, value)));
        }
        public bool ShowConfidenceScores { get; set; }
        public bool EnableVerboseLogging { get; set; }
        public bool IncludeCursorHistory { get; set; }
        public int MaxSuggestions 
        { 
            get => GetSetting<int>(nameof(MaxSuggestions));
            set => SetSetting(nameof(MaxSuggestions), Math.Max(1, Math.Min(20, value)));
        }
        public bool EnableStreamingCompletions { get; set; }
        public bool FilterSensitiveData { get; set; }
        public int MaxRequestSizeKB 
        { 
            get => GetSetting<int>(nameof(MaxRequestSizeKB));
            set => SetSetting(nameof(MaxRequestSizeKB), Math.Max(64, Math.Min(2048, value)));
        }
        public int MaxConcurrentRequests 
        { 
            get => GetSetting<int>(nameof(MaxConcurrentRequests));
            set => SetSetting(nameof(MaxConcurrentRequests), Math.Max(1, Math.Min(10, value)));
        }
        public int MaxRetryAttempts 
        { 
            get => GetSetting<int>(nameof(MaxRetryAttempts));
            set => SetSetting(nameof(MaxRetryAttempts), Math.Max(0, Math.Min(10, value)));
        }

        public event EventHandler<SettingsChangedEventArgs> SettingsChanged;

        public void LoadSettings() { }
        public void SaveSettings() { }

        public void ResetToDefaults()
        {
            LoadDefaults();
            SettingsChanged?.Invoke(this, new SettingsChangedEventArgs
            {
                SettingName = "All",
                Category = SettingCategory.Connection
            });
        }

        public bool ValidateSettings()
        {
            if (string.IsNullOrWhiteSpace(OllamaEndpoint))
                return false;

            if (!Uri.TryCreate(OllamaEndpoint, UriKind.Absolute, out _))
                return false;

            if (string.IsNullOrWhiteSpace(OllamaModel))
                return false;

            return true;
        }

        public T GetSetting<T>(string settingName)
        {
            return _settings.TryGetValue(settingName, out var value) ? (T)value : default(T);
        }

        public void SetSetting<T>(string settingName, T value)
        {
            var oldValue = _settings.ContainsKey(settingName) ? _settings[settingName] : default(T);
            _settings[settingName] = value;

            var category = GetSettingCategory(settingName);
            SettingsChanged?.Invoke(this, new SettingsChangedEventArgs
            {
                SettingName = settingName,
                OldValue = oldValue,
                NewValue = value,
                Category = category
            });
        }

        private SettingCategory GetSettingCategory(string settingName)
        {
            return settingName switch
            {
                nameof(OllamaEndpoint) or nameof(OllamaModel) or nameof(OllamaTimeout) => SettingCategory.Connection,
                nameof(SurroundingLinesUp) or nameof(SurroundingLinesDown) or nameof(CursorHistoryMemoryDepth) => SettingCategory.Context,
                nameof(ShowConfidenceScores) or nameof(JumpKey) => SettingCategory.Display,
                nameof(CodePredictionEnabled) or nameof(JumpRecommendationsEnabled) => SettingCategory.Features,
                nameof(MinimumConfidenceThreshold) or nameof(TypingDebounceDelay) => SettingCategory.Performance,
                nameof(EnableVerboseLogging) => SettingCategory.Debugging,
                _ => SettingCategory.Features
            };
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _settings.Clear();
        }
    }
}