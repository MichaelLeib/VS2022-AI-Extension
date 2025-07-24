using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using OllamaAssistant.Models;
using OllamaAssistant.Services.Implementation;
using OllamaAssistant.Services.Interfaces;
using OllamaAssistant.Tests.TestUtilities;

namespace OllamaAssistant.Tests.Performance
{
    [TestClass]
    [TestCategory(TestCategories.Performance)]
    [TestCategory(TestCategories.Service)]
    public class CursorHistoryServicePerformanceTests : PerformanceTestBase
    {
        private CursorHistoryService _cursorHistoryService;
        private Mock<ISettingsService> _mockSettingsService;

        protected override void OnTestInitialize()
        {
            _mockSettingsService = MockFactory.CreateMockSettingsService();
            // Set up for performance testing with higher limits
            _mockSettingsService.SetupProperty(x => x.CursorHistoryMemoryDepth, 1000);
            
            _cursorHistoryService = new CursorHistoryService(_mockSettingsService.Object);
        }

        protected override void OnTestCleanup()
        {
            _cursorHistoryService?.Dispose();
        }

        #region Record Cursor Position Performance Tests

        [TestMethod]
        [PerformanceTest(MaxExecutionTimeMs = 1000)]
        public async Task RecordCursorPosition_Performance_ShouldBeVeryFast()
        {
            // Arrange
            var entry = MockFactory.CreateTestCursorHistoryEntry();

            // Act
            var measurement = await MeasurePerformanceAsync(
                () => Task.Run(() => _cursorHistoryService.RecordCursorPosition(entry)),
                "RecordCursorPosition_SingleEntry");

            // Assert
            AssertPerformance(measurement, TimeSpan.FromMilliseconds(10), maxMemoryKB: 64);
        }

        [TestMethod]
        [PerformanceTest(MaxExecutionTimeMs = 5000)]
        public async Task RecordCursorPosition_Benchmark_ShouldHandleHighFrequency()
        {
            // Act
            var benchmark = await RunBenchmarkAsync(
                () => Task.Run(() =>
                {
                    var entry = new CursorHistoryEntry
                    {
                        FilePath = $"TestFile{Random.Shared.Next(1, 10)}.cs",
                        LineNumber = Random.Shared.Next(1, 1000),
                        ColumnNumber = Random.Shared.Next(1, 100),
                        Timestamp = DateTime.Now,
                        Context = $"Context {Random.Shared.Next()}",
                        ChangeType = "Edit"
                    };
                    _cursorHistoryService.RecordCursorPosition(entry);
                }),
                iterations: 1000,
                benchmarkName: "RecordCursorPosition_HighFrequency");

            // Assert
            AssertBenchmarkPerformance(benchmark,
                maxAverageDuration: TimeSpan.FromMilliseconds(1),
                minSuccessRate: 0.99,
                maxMemoryKB: 2048);
        }

        [TestMethod]
        [PerformanceTest(MaxExecutionTimeMs = 10000)]
        public async Task RecordCursorPosition_ConcurrentLoad_ShouldHandleThreadSafely()
        {
            // Act
            var loadTest = await RunLoadTestAsync(
                () => Task.Run(() =>
                {
                    var entry = new CursorHistoryEntry
                    {
                        FilePath = $"ConcurrentFile{Random.Shared.Next(1, 5)}.cs",
                        LineNumber = Random.Shared.Next(1, 500),
                        ColumnNumber = Random.Shared.Next(1, 80),
                        Timestamp = DateTime.Now,
                        Context = $"Concurrent context {Random.Shared.Next()}",
                        ChangeType = Random.Shared.Next(2) == 0 ? "Edit" : "Navigation"
                    };
                    _cursorHistoryService.RecordCursorPosition(entry);
                }),
                concurrentOperations: 20,
                testDuration: TimeSpan.FromSeconds(5),
                loadTestName: "RecordCursorPosition_ConcurrentLoad");

            // Assert
            AssertLoadTestPerformance(loadTest,
                minOperationsPerSecond: 500, // Should handle at least 500 operations per second
                maxAverageResponseTime: TimeSpan.FromMilliseconds(10),
                maxErrorRate: 0.01);
        }

        #endregion

        #region History Retrieval Performance Tests

        [TestMethod]
        [PerformanceTest(MaxExecutionTimeMs = 1000)]
        public async Task GetRecentHistory_Performance_ShouldBeVeryFast()
        {
            // Arrange - Fill with test data
            await PopulateWithTestData(500);

            // Act
            var measurement = await MeasurePerformanceAsync(
                () => Task.Run(() => _cursorHistoryService.GetRecentHistory(50)),
                "GetRecentHistory_RetrieveRecent");

            // Assert
            AssertPerformance(measurement, TimeSpan.FromMilliseconds(50), maxMemoryKB: 512);
            measurement.Result.Should().NotBeNull();
            measurement.Result.As<List<CursorHistoryEntry>>().Should().HaveCountLessOrEqualTo(50);
        }

        [TestMethod]
        [PerformanceTest(MaxExecutionTimeMs = 2000)]
        public async Task GetRecentHistory_Benchmark_ShouldScaleWell()
        {
            // Arrange - Fill with substantial test data
            await PopulateWithTestData(1000);

            // Act
            var benchmark = await RunBenchmarkAsync(
                () => Task.Run(() => _cursorHistoryService.GetRecentHistory(100)),
                iterations: 100,
                benchmarkName: "GetRecentHistory_ScalabilityTest");

            // Assert
            AssertBenchmarkPerformance(benchmark,
                maxAverageDuration: TimeSpan.FromMilliseconds(20),
                minSuccessRate: 0.99,
                maxMemoryKB: 1024);
        }

        [TestMethod]
        [PerformanceTest(MaxExecutionTimeMs = 1000)]
        public async Task GetFileHistory_Performance_ShouldFilterEfficiently()
        {
            // Arrange - Fill with mixed file data
            await PopulateWithMixedFileData(500);

            // Act
            var measurement = await MeasurePerformanceAsync(
                () => Task.Run(() => _cursorHistoryService.GetFileHistory("TestFile1.cs", 20)),
                "GetFileHistory_FilterByFile");

            // Assert
            AssertPerformance(measurement, TimeSpan.FromMilliseconds(25), maxMemoryKB: 256);
            measurement.Result.Should().NotBeNull();
            var results = measurement.Result.As<List<CursorHistoryEntry>>();
            results.Should().OnlyContain(e => e.FilePath == "TestFile1.cs");
        }

        #endregion

        #region Memory Management Performance Tests

        [TestMethod]
        [PerformanceTest(MaxExecutionTimeMs = 5000)]
        public async Task CursorHistoryService_MemoryManagement_ShouldEnforceMemoryLimits()
        {
            // Arrange - Set lower memory depth for this test
            _mockSettingsService.SetupProperty(x => x.CursorHistoryMemoryDepth, 100);
            var limitedService = new CursorHistoryService(_mockSettingsService.Object);

            var initialMemory = GC.GetTotalMemory(true);

            // Act - Add more entries than the limit
            var measurement = await MeasurePerformanceAsync(async () =>
            {
                for (int i = 0; i < 500; i++) // Add 5x more than limit
                {
                    var entry = new CursorHistoryEntry
                    {
                        FilePath = $"MemoryTest{i % 10}.cs",
                        LineNumber = i,
                        ColumnNumber = i % 80,
                        Timestamp = DateTime.Now.AddMinutes(-i),
                        Context = $"Memory test context {i} with some additional text to increase memory usage",
                        ChangeType = "Edit"
                    };
                    limitedService.RecordCursorPosition(entry);
                }

                // Force GC to get accurate measurement
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();

                return limitedService.GetRecentHistory(200).Count;
            }, "CursorHistoryService_MemoryLimitEnforcement");

            // Assert
            AssertPerformance(measurement, TimeSpan.FromSeconds(2), maxMemoryKB: 1024);
            
            // Should only keep the configured number of entries
            measurement.Result.Should().BeLessOrEqualTo(100);

            var finalMemory = GC.GetTotalMemory(false);
            var memoryUsed = (finalMemory - initialMemory) / 1024.0;
            WriteTestOutput($"Memory used for 500 entries (limit 100): {memoryUsed:F1}KB");

            limitedService.Dispose();
        }

        [TestMethod]
        [PerformanceTest(MaxExecutionTimeMs = 3000)]
        public async Task ClearHistory_Performance_ShouldBeEfficient()
        {
            // Arrange - Fill with test data
            await PopulateWithTestData(1000);

            // Act
            var measurement = await MeasurePerformanceAsync(
                () => Task.Run(() => _cursorHistoryService.ClearHistory()),
                "ClearHistory_ClearAllEntries");

            // Assert
            AssertPerformance(measurement, TimeSpan.FromMilliseconds(100), maxMemoryKB: 512);
            
            // Verify history is cleared
            var remainingEntries = _cursorHistoryService.GetRecentHistory(10);
            remainingEntries.Should().BeEmpty();
        }

        [TestMethod]
        [PerformanceTest(MaxExecutionTimeMs = 2000)]
        public async Task ClearHistoryForFileAsync_Performance_ShouldFilterEfficiently()
        {
            // Arrange - Fill with mixed file data
            await PopulateWithMixedFileData(1000);

            // Act
            var measurement = await MeasurePerformanceAsync(
                () => _cursorHistoryService.ClearHistoryForFileAsync("TestFile1.cs"),
                "ClearHistoryForFileAsync_SelectiveClearing");

            // Assert
            AssertPerformance(measurement, TimeSpan.FromMilliseconds(50), maxMemoryKB: 256);
            
            // Verify only the specified file's history is cleared
            var remainingEntries = _cursorHistoryService.GetRecentHistory(1000);
            remainingEntries.Should().NotContain(e => e.FilePath == "TestFile1.cs");
            remainingEntries.Should().Contain(e => e.FilePath != "TestFile1.cs");
        }

        #endregion

        #region Settings Change Performance Tests

        [TestMethod]
        [PerformanceTest(MaxExecutionTimeMs = 1000)]
        public async Task SetMaxHistoryDepth_Performance_ShouldAdjustQuickly()
        {
            // Arrange - Fill with test data
            await PopulateWithTestData(500);

            // Act
            var measurement = await MeasurePerformanceAsync(
                () => Task.Run(() => _cursorHistoryService.SetMaxHistoryDepth(200)),
                "SetMaxHistoryDepth_AdjustLimit");

            // Assert
            AssertPerformance(measurement, TimeSpan.FromMilliseconds(100), maxMemoryKB: 512);
            
            // Verify new limit is enforced
            var entries = _cursorHistoryService.GetRecentHistory(300);
            entries.Should().HaveCountLessOrEqualTo(200);
        }

        #endregion

        #region Thread Safety Performance Tests

        [TestMethod]
        [PerformanceTest(MaxExecutionTimeMs = 15000)]
        public async Task CursorHistoryService_ThreadSafety_ShouldHandleConcurrentOperations()
        {
            // Arrange
            const int concurrentThreads = 10;
            const int operationsPerThread = 50;

            // Act - Concurrent read/write operations
            var tasks = new List<Task<List<PerformanceMeasurement>>>();

            for (int i = 0; i < concurrentThreads; i++)
            {
                int threadId = i;
                var task = Task.Run(async () =>
                {
                    var measurements = new List<PerformanceMeasurement>();

                    for (int j = 0; j < operationsPerThread; j++)
                    {
                        // Mix of operations
                        if (j % 3 == 0)
                        {
                            // Write operation
                            var measurement = await MeasurePerformanceAsync(() => Task.Run(() =>
                            {
                                var entry = new CursorHistoryEntry
                                {
                                    FilePath = $"Thread{threadId}_File{j % 5}.cs",
                                    LineNumber = j,
                                    ColumnNumber = threadId * 10 + j,
                                    Timestamp = DateTime.Now,
                                    Context = $"Thread {threadId} operation {j}",
                                    ChangeType = "Concurrent"
                                };
                                _cursorHistoryService.RecordCursorPosition(entry);
                            }), $"Thread{threadId}_Write_{j}", false);
                            measurements.Add(measurement);
                        }
                        else
                        {
                            // Read operation
                            var measurement = await MeasurePerformanceAsync(() => Task.Run(() =>
                                _cursorHistoryService.GetRecentHistory(10)), $"Thread{threadId}_Read_{j}", false);
                            measurements.Add(measurement);
                        }
                    }

                    return measurements;
                });
                tasks.Add(task);
            }

            var allMeasurements = await Task.WhenAll(tasks);

            // Assert
            var flattenedMeasurements = allMeasurements.SelectMany(m => m).ToList();
            var successfulOperations = flattenedMeasurements.Count(m => m.Success);
            var totalOperations = flattenedMeasurements.Count;

            WriteTestOutput($"Thread safety test: {successfulOperations}/{totalOperations} operations succeeded");

            var successRate = (double)successfulOperations / totalOperations;
            successRate.Should().BeGreaterOrEqualTo(0.95, "At least 95% of concurrent operations should succeed");

            var averageResponseTime = flattenedMeasurements.Where(m => m.Success).Average(m => m.Duration.TotalMilliseconds);
            averageResponseTime.Should().BeLessThan(50, "Average response time under concurrent load should be under 50ms");
        }

        #endregion

        #region Data Volume Performance Tests

        [TestMethod]
        [PerformanceTest(MaxExecutionTimeMs = 10000)]
        public async Task CursorHistoryService_LargeDataset_ShouldMaintainPerformance()
        {
            // Arrange - Test with very large dataset
            const int largeDatasetSize = 5000;

            // Act - Populate with large dataset
            var populateTime = await MeasurePerformanceAsync(
                () => PopulateWithTestData(largeDatasetSize),
                "LargeDataset_Population");

            // Test retrieval performance with large dataset
            var retrievalTime = await MeasurePerformanceAsync(
                () => Task.Run(() => _cursorHistoryService.GetRecentHistory(100)),
                "LargeDataset_Retrieval");

            var fileFilterTime = await MeasurePerformanceAsync(
                () => Task.Run(() => _cursorHistoryService.GetFileHistory("TestFile1.cs", 50)),
                "LargeDataset_FileFilter");

            // Assert
            AssertPerformance(populateTime, TimeSpan.FromSeconds(5), maxMemoryKB: 5120);
            AssertPerformance(retrievalTime, TimeSpan.FromMilliseconds(100), maxMemoryKB: 1024);
            AssertPerformance(fileFilterTime, TimeSpan.FromMilliseconds(150), maxMemoryKB: 512);

            WriteTestOutput($"Large dataset performance: Population={populateTime.Duration.TotalMilliseconds}ms, " +
                          $"Retrieval={retrievalTime.Duration.TotalMilliseconds}ms, " +
                          $"FileFilter={fileFilterTime.Duration.TotalMilliseconds}ms");
        }

        #endregion

        #region Helper Methods

        private async Task PopulateWithTestData(int count)
        {
            await Task.Run(() =>
            {
                for (int i = 0; i < count; i++)
                {
                    var entry = new CursorHistoryEntry
                    {
                        FilePath = $"TestFile{i % 10}.cs",
                        LineNumber = i + 1,
                        ColumnNumber = (i % 80) + 1,
                        Timestamp = DateTime.Now.AddMinutes(-i),
                        Context = $"Test context {i}",
                        ChangeType = i % 2 == 0 ? "Edit" : "Navigation"
                    };
                    _cursorHistoryService.RecordCursorPosition(entry);
                }
            });
        }

        private async Task PopulateWithMixedFileData(int count)
        {
            await Task.Run(() =>
            {
                var files = new[] { "TestFile1.cs", "TestFile2.cs", "TestFile3.js", "TestFile4.py", "TestFile5.cpp" };

                for (int i = 0; i < count; i++)
                {
                    var entry = new CursorHistoryEntry
                    {
                        FilePath = files[i % files.Length],
                        LineNumber = (i / files.Length) + 1,
                        ColumnNumber = (i % 100) + 1,
                        Timestamp = DateTime.Now.AddMinutes(-i),
                        Context = $"Mixed file context {i}",
                        ChangeType = i % 3 == 0 ? "Edit" : "Navigation"
                    };
                    _cursorHistoryService.RecordCursorPosition(entry);
                }
            });
        }

        #endregion
    }
}