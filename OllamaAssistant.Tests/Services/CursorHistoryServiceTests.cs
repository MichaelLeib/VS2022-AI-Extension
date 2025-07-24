using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using OllamaAssistant.Models;
using OllamaAssistant.Services.Implementation;
using OllamaAssistant.Services.Interfaces;
using OllamaAssistant.Tests.TestUtilities;

namespace OllamaAssistant.Tests.Services
{
    [TestClass]
    [TestCategory(TestCategories.Unit)]
    [TestCategory(TestCategories.Service)]
    public class CursorHistoryServiceTests : BaseTest
    {
        private CursorHistoryService _cursorHistoryService;
        private Mock<ISettingsService> _mockSettingsService;

        protected override void OnTestInitialize()
        {
            _mockSettingsService = MockFactory.CreateMockSettingsService();
            _mockSettingsService.SetupProperty(x => x.CursorHistoryMemoryDepth, 5);
            
            _cursorHistoryService = new CursorHistoryService(_mockSettingsService.Object);
        }

        protected override void OnTestCleanup()
        {
            _cursorHistoryService?.Dispose();
        }

        #region Constructor Tests

        [TestMethod]
        public void Constructor_WithValidSettingsService_ShouldCreateInstance()
        {
            // Arrange & Act
            var service = new CursorHistoryService(_mockSettingsService.Object);

            // Assert
            service.Should().NotBeNull();
        }

        [TestMethod]
        public void Constructor_WithNullSettingsService_ShouldThrowArgumentNullException()
        {
            // Arrange, Act & Assert
            Action act = () => new CursorHistoryService(null);
            act.Should().Throw<ArgumentNullException>()
                .WithParameterName("settingsService");
        }

        #endregion

        #region RecordCursorPosition Tests

        [TestMethod]
        public void RecordCursorPosition_WithValidEntry_ShouldAddToHistory()
        {
            // Arrange
            var entry = MockFactory.CreateTestCursorHistoryEntry();

            // Act
            _cursorHistoryService.RecordCursorPosition(entry);

            // Assert
            var history = _cursorHistoryService.GetRecentHistory(10);
            history.Should().HaveCount(1);
            history.First().Should().BeEquivalentTo(entry);
        }

        [TestMethod]
        public void RecordCursorPosition_WithNullEntry_ShouldNotAddToHistory()
        {
            // Arrange
            CursorHistoryEntry entry = null;

            // Act
            _cursorHistoryService.RecordCursorPosition(entry);

            // Assert
            var history = _cursorHistoryService.GetRecentHistory(10);
            history.Should().BeEmpty();
        }

        [TestMethod]
        public void RecordCursorPosition_WhenExceedingMaxDepth_ShouldRemoveOldestEntries()
        {
            // Arrange
            var maxDepth = 3;
            _mockSettingsService.SetupProperty(x => x.CursorHistoryMemoryDepth, maxDepth);
            var service = new CursorHistoryService(_mockSettingsService.Object);

            // Act - Add more entries than max depth
            for (int i = 0; i < maxDepth + 2; i++)
            {
                var entry = new CursorHistoryEntry
                {
                    FilePath = $"File{i}.cs",
                    LineNumber = i + 1,
                    ColumnNumber = 1,
                    Timestamp = DateTime.Now.AddMinutes(i),
                    Context = $"Context {i}"
                };
                service.RecordCursorPosition(entry);
            }

            // Assert
            var history = service.GetRecentHistory(10);
            history.Should().HaveCount(maxDepth);
            
            // Should keep the most recent entries
            history.First().FilePath.Should().Be($"File{maxDepth + 1}.cs");
            history.Last().FilePath.Should().Be($"File2.cs");

            service.Dispose();
        }

        [TestMethod]
        public void RecordCursorPosition_WithDuplicatePosition_ShouldNotAddDuplicate()
        {
            // Arrange
            var entry = MockFactory.CreateTestCursorHistoryEntry();

            // Act
            _cursorHistoryService.RecordCursorPosition(entry);
            _cursorHistoryService.RecordCursorPosition(entry); // Same entry again

            // Assert
            var history = _cursorHistoryService.GetRecentHistory(10);
            history.Should().HaveCount(1);
        }

        #endregion

        #region GetRecentHistory Tests

        [TestMethod]
        public void GetRecentHistory_WithEmptyHistory_ShouldReturnEmptyList()
        {
            // Act
            var history = _cursorHistoryService.GetRecentHistory(5);

            // Assert
            history.Should().NotBeNull();
            history.Should().BeEmpty();
        }

        [TestMethod]
        public void GetRecentHistory_WithCountGreaterThanHistorySize_ShouldReturnAllEntries()
        {
            // Arrange
            var entries = MockFactory.CreateTestCursorHistory(3);
            foreach (var entry in entries)
            {
                _cursorHistoryService.RecordCursorPosition(entry);
            }

            // Act
            var history = _cursorHistoryService.GetRecentHistory(10);

            // Assert
            history.Should().HaveCount(3);
        }

        [TestMethod]
        public void GetRecentHistory_WithCountLessThanHistorySize_ShouldReturnRequestedCount()
        {
            // Arrange
            var entries = MockFactory.CreateTestCursorHistory(5);
            foreach (var entry in entries)
            {
                _cursorHistoryService.RecordCursorPosition(entry);
            }

            // Act
            var history = _cursorHistoryService.GetRecentHistory(3);

            // Assert
            history.Should().HaveCount(3);
        }

        [TestMethod]
        public void GetRecentHistory_ShouldReturnMostRecentFirst()
        {
            // Arrange
            var entry1 = new CursorHistoryEntry { FilePath = "File1.cs", Timestamp = DateTime.Now.AddMinutes(-2) };
            var entry2 = new CursorHistoryEntry { FilePath = "File2.cs", Timestamp = DateTime.Now.AddMinutes(-1) };
            var entry3 = new CursorHistoryEntry { FilePath = "File3.cs", Timestamp = DateTime.Now };

            _cursorHistoryService.RecordCursorPosition(entry1);
            _cursorHistoryService.RecordCursorPosition(entry2);
            _cursorHistoryService.RecordCursorPosition(entry3);

            // Act
            var history = _cursorHistoryService.GetRecentHistory(3);

            // Assert
            history.First().FilePath.Should().Be("File3.cs");
            history.Last().FilePath.Should().Be("File1.cs");
        }

        #endregion

        #region GetFileHistory Tests

        [TestMethod]
        public void GetFileHistory_WithExistingFile_ShouldReturnFileSpecificEntries()
        {
            // Arrange
            var targetFile = "TargetFile.cs";
            var entry1 = new CursorHistoryEntry { FilePath = targetFile, LineNumber = 1 };
            var entry2 = new CursorHistoryEntry { FilePath = "OtherFile.cs", LineNumber = 2 };
            var entry3 = new CursorHistoryEntry { FilePath = targetFile, LineNumber = 3 };

            _cursorHistoryService.RecordCursorPosition(entry1);
            _cursorHistoryService.RecordCursorPosition(entry2);
            _cursorHistoryService.RecordCursorPosition(entry3);

            // Act
            var fileHistory = _cursorHistoryService.GetFileHistory(targetFile, 10);

            // Assert
            fileHistory.Should().HaveCount(2);
            fileHistory.Should().OnlyContain(entry => entry.FilePath == targetFile);
        }

        [TestMethod]
        public void GetFileHistory_WithNonExistentFile_ShouldReturnEmptyList()
        {
            // Arrange
            var entry = MockFactory.CreateTestCursorHistoryEntry();
            _cursorHistoryService.RecordCursorPosition(entry);

            // Act
            var fileHistory = _cursorHistoryService.GetFileHistory("NonExistent.cs", 10);

            // Assert
            fileHistory.Should().BeEmpty();
        }

        [TestMethod]
        public void GetFileHistory_WithNullFilePath_ShouldReturnEmptyList()
        {
            // Act
            var fileHistory = _cursorHistoryService.GetFileHistory(null, 10);

            // Assert
            fileHistory.Should().BeEmpty();
        }

        #endregion

        #region ClearHistory Tests

        [TestMethod]
        public void ClearHistory_ShouldRemoveAllEntries()
        {
            // Arrange
            var entries = MockFactory.CreateTestCursorHistory(5);
            foreach (var entry in entries)
            {
                _cursorHistoryService.RecordCursorPosition(entry);
            }

            // Act
            _cursorHistoryService.ClearHistory();

            // Assert
            var history = _cursorHistoryService.GetRecentHistory(10);
            history.Should().BeEmpty();
        }

        #endregion

        #region ClearHistoryForFileAsync Tests

        [TestMethod]
        public async Task ClearHistoryForFileAsync_WithExistingFile_ShouldRemoveOnlyFileEntries()
        {
            // Arrange
            var targetFile = "TargetFile.cs";
            var entry1 = new CursorHistoryEntry { FilePath = targetFile, LineNumber = 1 };
            var entry2 = new CursorHistoryEntry { FilePath = "OtherFile.cs", LineNumber = 2 };
            var entry3 = new CursorHistoryEntry { FilePath = targetFile, LineNumber = 3 };

            _cursorHistoryService.RecordCursorPosition(entry1);
            _cursorHistoryService.RecordCursorPosition(entry2);
            _cursorHistoryService.RecordCursorPosition(entry3);

            // Act
            await _cursorHistoryService.ClearHistoryForFileAsync(targetFile);

            // Assert
            var remainingHistory = _cursorHistoryService.GetRecentHistory(10);
            remainingHistory.Should().HaveCount(1);
            remainingHistory.First().FilePath.Should().Be("OtherFile.cs");
        }

        [TestMethod]
        public async Task ClearHistoryForFileAsync_WithNullFilePath_ShouldNotThrow()
        {
            // Arrange
            var entry = MockFactory.CreateTestCursorHistoryEntry();
            _cursorHistoryService.RecordCursorPosition(entry);

            // Act & Assert
            await _cursorHistoryService.Invoking(s => s.ClearHistoryForFileAsync(null))
                .Should().NotThrowAsync();

            var history = _cursorHistoryService.GetRecentHistory(10);
            history.Should().HaveCount(1); // Entry should still be there
        }

        #endregion

        #region SetMaxHistoryDepth Tests

        [TestMethod]
        public void SetMaxHistoryDepth_WithValidDepth_ShouldUpdateMaxDepth()
        {
            // Arrange
            var entries = MockFactory.CreateTestCursorHistory(5);
            foreach (var entry in entries)
            {
                _cursorHistoryService.RecordCursorPosition(entry);
            }

            // Act
            _cursorHistoryService.SetMaxHistoryDepth(3);

            // Add one more entry to trigger cleanup
            _cursorHistoryService.RecordCursorPosition(MockFactory.CreateTestCursorHistoryEntry());

            // Assert
            var history = _cursorHistoryService.GetRecentHistory(10);
            history.Should().HaveCount(3);
        }

        [TestMethod]
        public void SetMaxHistoryDepth_WithZeroDepth_ShouldClearAllHistory()
        {
            // Arrange
            var entries = MockFactory.CreateTestCursorHistory(3);
            foreach (var entry in entries)
            {
                _cursorHistoryService.RecordCursorPosition(entry);
            }

            // Act
            _cursorHistoryService.SetMaxHistoryDepth(0);

            // Assert
            var history = _cursorHistoryService.GetRecentHistory(10);
            history.Should().BeEmpty();
        }

        [TestMethod]
        public void SetMaxHistoryDepth_WithNegativeDepth_ShouldNotChangeDepth()
        {
            // Arrange
            var entries = MockFactory.CreateTestCursorHistory(3);
            foreach (var entry in entries)
            {
                _cursorHistoryService.RecordCursorPosition(entry);
            }

            // Act
            _cursorHistoryService.SetMaxHistoryDepth(-1);

            // Assert
            var history = _cursorHistoryService.GetRecentHistory(10);
            history.Should().HaveCount(3); // Should still have all entries
        }

        #endregion

        #region AddEntryAsync Tests

        [TestMethod]
        public async Task AddEntryAsync_WithValidEntry_ShouldAddToHistory()
        {
            // Arrange
            var entry = MockFactory.CreateTestCursorHistoryEntry();

            // Act
            await _cursorHistoryService.AddEntryAsync(entry);

            // Assert
            var history = _cursorHistoryService.GetRecentHistory(10);
            history.Should().HaveCount(1);
            history.First().Should().BeEquivalentTo(entry);
        }

        [TestMethod]
        public async Task AddEntryAsync_WithNullEntry_ShouldNotThrow()
        {
            // Act & Assert
            await _cursorHistoryService.Invoking(s => s.AddEntryAsync(null))
                .Should().NotThrowAsync();

            var history = _cursorHistoryService.GetRecentHistory(10);
            history.Should().BeEmpty();
        }

        #endregion

        #region CleanupFileDataAsync Tests

        [TestMethod]
        public async Task CleanupFileDataAsync_WithExistingFile_ShouldRemoveFileEntries()
        {
            // Arrange
            var targetFile = "TargetFile.cs";
            var entry1 = new CursorHistoryEntry { FilePath = targetFile, LineNumber = 1 };
            var entry2 = new CursorHistoryEntry { FilePath = "OtherFile.cs", LineNumber = 2 };

            _cursorHistoryService.RecordCursorPosition(entry1);
            _cursorHistoryService.RecordCursorPosition(entry2);

            // Act
            await _cursorHistoryService.CleanupFileDataAsync(targetFile);

            // Assert
            var remainingHistory = _cursorHistoryService.GetRecentHistory(10);
            remainingHistory.Should().HaveCount(1);
            remainingHistory.First().FilePath.Should().Be("OtherFile.cs");
        }

        #endregion

        #region ClearHistoryAsync Tests

        [TestMethod]
        public async Task ClearHistoryAsync_ShouldRemoveAllEntries()
        {
            // Arrange
            var entries = MockFactory.CreateTestCursorHistory(5);
            foreach (var entry in entries)
            {
                _cursorHistoryService.RecordCursorPosition(entry);
            }

            // Act
            await _cursorHistoryService.ClearHistoryAsync();

            // Assert
            var history = _cursorHistoryService.GetRecentHistory(10);
            history.Should().BeEmpty();
        }

        #endregion

        #region Thread Safety Tests

        [TestMethod]
        public async Task RecordCursorPosition_ConcurrentCalls_ShouldHandleThreadSafely()
        {
            // Arrange
            const int numberOfTasks = 10;
            const int entriesPerTask = 10;
            var tasks = new Task[numberOfTasks];

            // Act
            for (int i = 0; i < numberOfTasks; i++)
            {
                int taskId = i;
                tasks[i] = Task.Run(() =>
                {
                    for (int j = 0; j < entriesPerTask; j++)
                    {
                        var entry = new CursorHistoryEntry
                        {
                            FilePath = $"File{taskId}_{j}.cs",
                            LineNumber = j + 1,
                            ColumnNumber = 1,
                            Timestamp = DateTime.Now,
                            Context = $"Task {taskId}, Entry {j}"
                        };
                        _cursorHistoryService.RecordCursorPosition(entry);
                    }
                });
            }

            await Task.WhenAll(tasks);

            // Assert
            var history = _cursorHistoryService.GetRecentHistory(1000);
            history.Should().HaveCount(_mockSettingsService.Object.CursorHistoryMemoryDepth); // Limited by max depth
        }

        #endregion

        #region Settings Integration Tests

        [TestMethod]
        public void SettingsChanged_WhenHistoryDepthChanges_ShouldUpdateMaxDepth()
        {
            // Arrange
            var entries = MockFactory.CreateTestCursorHistory(5);
            foreach (var entry in entries)
            {
                _cursorHistoryService.RecordCursorPosition(entry);
            }

            // Act
            _mockSettingsService.SetupProperty(x => x.CursorHistoryMemoryDepth, 2);
            _mockSettingsService.Raise(s => s.SettingsChanged += null, 
                new SettingsChangedEventArgs 
                { 
                    SettingName = nameof(ISettingsService.CursorHistoryMemoryDepth),
                    NewValue = 2,
                    OldValue = 5
                });

            // Trigger cleanup by adding another entry
            _cursorHistoryService.RecordCursorPosition(MockFactory.CreateTestCursorHistoryEntry());

            // Assert
            var history = _cursorHistoryService.GetRecentHistory(10);
            history.Should().HaveCount(2);
        }

        #endregion
    }
}