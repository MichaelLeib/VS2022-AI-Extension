using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OllamaAssistant.Services.Implementation;
using OllamaAssistant.Tests.TestHelpers;

namespace OllamaAssistant.Tests.UnitTests.Services
{
    [TestClass]
    public class CursorHistoryServiceTests
    {
        private CursorHistoryService _service;

        [TestInitialize]
        public void Initialize()
        {
            _service = new CursorHistoryService(memoryDepth: 5);
        }

        [TestCleanup]
        public void Cleanup()
        {
            _service?.Dispose();
        }

        [TestMethod]
        public async Task AddEntryAsync_ShouldAddEntryToHistory()
        {
            // Arrange
            var entry = TestDataBuilders.CursorHistoryEntryBuilder.Default().Build();

            // Act
            await _service.AddEntryAsync(entry);

            // Assert
            _service.CurrentHistoryCount.Should().Be(1);
            var history = await _service.GetHistoryAsync(10);
            history.Should().ContainSingle().Which.Should().BeEquivalentTo(entry);
        }

        [TestMethod]
        public async Task AddEntryAsync_WithNullEntry_ShouldNotThrow()
        {
            // Act & Assert
            await TestUtilities.AssertDoesNotThrowAsync(() => _service.AddEntryAsync(null));
            _service.CurrentHistoryCount.Should().Be(0);
        }

        [TestMethod]
        public async Task AddEntryAsync_WhenMemoryDepthExceeded_ShouldMaintainLimit()
        {
            // Arrange
            var entries = TestDataBuilders.Scenarios.CreateHistorySequence(7); // More than memory depth of 5

            // Act
            foreach (var entry in entries)
            {
                await _service.AddEntryAsync(entry);
            }

            // Assert
            _service.CurrentHistoryCount.Should().Be(5);
            var history = await _service.GetHistoryAsync(10);
            history.Should().HaveCount(5);
            
            // Should contain the most recent 5 entries
            history.Select(h => h.Context).Should().ContainInOrder(
                "// Context 6", "// Context 5", "// Context 4", "// Context 3", "// Context 2");
        }

        [TestMethod]
        public async Task GetHistoryAsync_WithLimitLessThanAvailable_ShouldReturnLimitedResults()
        {
            // Arrange
            var entries = TestDataBuilders.Scenarios.CreateHistorySequence(5);
            foreach (var entry in entries)
            {
                await _service.AddEntryAsync(entry);
            }

            // Act
            var history = await _service.GetHistoryAsync(3);

            // Assert
            history.Should().HaveCount(3);
            // Should return the 3 most recent entries
            history.Select(h => h.Context).Should().ContainInOrder(
                "// Context 4", "// Context 3", "// Context 2");
        }

        [TestMethod]
        public async Task GetRelevantHistoryAsync_WithMatchingFile_ShouldReturnRelevantEntries()
        {
            // Arrange
            var file1Entry = TestDataBuilders.CursorHistoryEntryBuilder.Default()
                .WithFilePath("C:\\File1.cs")
                .WithPosition(10, 5)
                .WithContext("File1 context")
                .Build();

            var file2Entry = TestDataBuilders.CursorHistoryEntryBuilder.Default()
                .WithFilePath("C:\\File2.cs")
                .WithPosition(20, 8)
                .WithContext("File2 context")
                .Build();

            var anotherFile1Entry = TestDataBuilders.CursorHistoryEntryBuilder.Default()
                .WithFilePath("C:\\File1.cs")
                .WithPosition(15, 3)
                .WithContext("Another File1 context")
                .Build();

            await _service.AddEntryAsync(file1Entry);
            await _service.AddEntryAsync(file2Entry);
            await _service.AddEntryAsync(anotherFile1Entry);

            // Act
            var relevantHistory = await _service.GetRelevantHistoryAsync("C:\\File1.cs", 12, 4);

            // Assert
            relevantHistory.Should().HaveCount(2);
            relevantHistory.Should().Contain(e => e.Context == "File1 context");
            relevantHistory.Should().Contain(e => e.Context == "Another File1 context");
            relevantHistory.Should().NotContain(e => e.Context == "File2 context");
        }

        [TestMethod]
        public async Task GetRelevantHistoryAsync_WithNearbyPositions_ShouldPrioritizeCloserPositions()
        {
            // Arrange
            var closeEntry = TestDataBuilders.CursorHistoryEntryBuilder.Default()
                .WithPosition(10, 5)
                .WithContext("Close entry")
                .Build();

            var farEntry = TestDataBuilders.CursorHistoryEntryBuilder.Default()
                .WithPosition(100, 5)
                .WithContext("Far entry")
                .Build();

            var veryCloseEntry = TestDataBuilders.CursorHistoryEntryBuilder.Default()
                .WithPosition(11, 6)
                .WithContext("Very close entry")
                .Build();

            await _service.AddEntryAsync(farEntry);
            await _service.AddEntryAsync(closeEntry);
            await _service.AddEntryAsync(veryCloseEntry);

            // Act
            var relevantHistory = await _service.GetRelevantHistoryAsync("C:\\TestFile.cs", 12, 5);

            // Assert
            relevantHistory.Should().HaveCount(3);
            // Should be ordered by relevance (proximity)
            relevantHistory.First().Context.Should().Be("Very close entry");
        }

        [TestMethod]
        public async Task ClearHistoryAsync_ShouldRemoveAllEntries()
        {
            // Arrange
            var entries = TestDataBuilders.Scenarios.CreateHistorySequence(3);
            foreach (var entry in entries)
            {
                await _service.AddEntryAsync(entry);
            }

            _service.CurrentHistoryCount.Should().Be(3);

            // Act
            await _service.ClearHistoryAsync();

            // Assert
            _service.CurrentHistoryCount.Should().Be(0);
            var history = await _service.GetHistoryAsync(10);
            history.Should().BeEmpty();
        }

        [TestMethod]
        public void MemoryDepth_ShouldBeSetCorrectly()
        {
            // Arrange & Act
            var service = new CursorHistoryService(memoryDepth: 10);

            // Assert
            service.MemoryDepth.Should().Be(10);
        }

        [TestMethod]
        public async Task AddEntryAsync_WithDuplicateEntry_ShouldNotAddDuplicate()
        {
            // Arrange
            var entry = TestDataBuilders.CursorHistoryEntryBuilder.Default()
                .WithPosition(10, 5)
                .WithTimestamp(DateTime.Now)
                .Build();

            // Act
            await _service.AddEntryAsync(entry);
            await _service.AddEntryAsync(entry); // Same entry

            // Assert
            _service.CurrentHistoryCount.Should().Be(1);
        }

        [TestMethod]
        public async Task AddEntryAsync_WithVeryQuickSuccession_ShouldHandleConcurrency()
        {
            // Arrange
            var entries = TestDataBuilders.Scenarios.CreateHistorySequence(10);

            // Act
            var tasks = entries.Select(entry => _service.AddEntryAsync(entry));
            await Task.WhenAll(tasks);

            // Assert
            _service.CurrentHistoryCount.Should().Be(5); // Memory depth limit
            var history = await _service.GetHistoryAsync(10);
            history.Should().HaveCount(5);
        }

        [TestMethod]
        public async Task GetRelevantHistoryAsync_WithEmptyHistory_ShouldReturnEmptyList()
        {
            // Act
            var relevantHistory = await _service.GetRelevantHistoryAsync("C:\\TestFile.cs", 10, 5);

            // Assert
            relevantHistory.Should().BeEmpty();
        }

        [TestMethod]
        public async Task Performance_AddingManyEntries_ShouldBeReasonablyFast()
        {
            // Arrange
            var entries = TestDataBuilders.Scenarios.CreateHistorySequence(1000);

            // Act
            var duration = await TestUtilities.MeasureExecutionTimeAsync(async () =>
            {
                foreach (var entry in entries)
                {
                    await _service.AddEntryAsync(entry);
                }
            });

            // Assert
            duration.Should().BeLessThan(TimeSpan.FromSeconds(1));
            _service.CurrentHistoryCount.Should().Be(5); // Should respect memory depth
        }

        [TestMethod]
        public async Task AddEntryAsync_AfterDispose_ShouldNotThrow()
        {
            // Arrange
            var entry = TestDataBuilders.CursorHistoryEntryBuilder.Default().Build();
            _service.Dispose();

            // Act & Assert
            await TestUtilities.AssertDoesNotThrowAsync(() => _service.AddEntryAsync(entry));
        }

        [TestMethod]
        public async Task GetHistoryAsync_WithNegativeLimit_ShouldReturnEmptyList()
        {
            // Arrange
            var entry = TestDataBuilders.CursorHistoryEntryBuilder.Default().Build();
            await _service.AddEntryAsync(entry);

            // Act
            var history = await _service.GetHistoryAsync(-1);

            // Assert
            history.Should().BeEmpty();
        }

        [TestMethod]
        public async Task GetHistoryAsync_WithZeroLimit_ShouldReturnEmptyList()
        {
            // Arrange
            var entry = TestDataBuilders.CursorHistoryEntryBuilder.Default().Build();
            await _service.AddEntryAsync(entry);

            // Act
            var history = await _service.GetHistoryAsync(0);

            // Assert
            history.Should().BeEmpty();
        }
    }
}