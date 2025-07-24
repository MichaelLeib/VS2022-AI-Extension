using System;
using System.Collections.Generic;
using System.Threading;
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
    public class OllamaServicePerformanceTests : PerformanceTestBase
    {
        private OllamaService _ollamaService;
        private Mock<ISettingsService> _mockSettingsService;
        private Mock<ILogger> _mockLogger;
        private Mock<OllamaHttpClient> _mockHttpClient;

        protected override void OnTestInitialize()
        {
            _mockSettingsService = MockFactory.CreateMockSettingsService();
            _mockLogger = MockFactory.CreateMockLogger();
            _mockHttpClient = CreateMockHttpClient();

            _ollamaService = new OllamaService(
                _mockSettingsService.Object,
                _mockLogger.Object,
                _mockHttpClient.Object);
        }

        protected override void OnTestCleanup()
        {
            _ollamaService?.Dispose();
        }

        #region Code Suggestion Performance Tests

        [TestMethod]
        [PerformanceTest(MaxExecutionTimeMs = 2000)]
        public async Task GetCodeSuggestionAsync_Performance_ShouldMeetLatencyRequirements()
        {
            // Arrange
            var codeContext = MockFactory.CreateTestCodeContext();

            // Act
            var measurement = await MeasurePerformanceAsync(
                () => _ollamaService.GetCodeSuggestionAsync(codeContext, CancellationToken.None),
                "GetCodeSuggestionAsync_SingleRequest");

            // Assert
            AssertPerformance(measurement, TimeSpan.FromSeconds(2), maxMemoryKB: 1024);
            measurement.Result.Should().NotBeNull();
            measurement.Result.As<CodeSuggestion>().Text.Should().NotBeNullOrEmpty();
        }

        [TestMethod]
        [PerformanceTest(MaxExecutionTimeMs = 10000)]
        public async Task GetCodeSuggestionAsync_Benchmark_ShouldHandleMultipleRequests()
        {
            // Arrange
            var codeContext = MockFactory.CreateTestCodeContext();

            // Act
            var benchmark = await RunBenchmarkAsync(
                () => _ollamaService.GetCodeSuggestionAsync(codeContext, CancellationToken.None),
                iterations: 20,
                benchmarkName: "GetCodeSuggestionAsync_Benchmark");

            // Assert
            AssertBenchmarkPerformance(benchmark, 
                maxAverageDuration: TimeSpan.FromSeconds(2),
                minSuccessRate: 0.9,
                maxMemoryKB: 2048);
        }

        [TestMethod]
        [PerformanceTest(MaxExecutionTimeMs = 30000)]
        public async Task GetCodeSuggestionAsync_LoadTest_ShouldHandleConcurrentRequests()
        {
            // Arrange
            var codeContext = MockFactory.CreateTestCodeContext();

            // Act
            var loadTest = await RunLoadTestAsync(
                () => _ollamaService.GetCodeSuggestionAsync(codeContext, CancellationToken.None),
                concurrentOperations: 5,
                testDuration: TimeSpan.FromSeconds(15),
                loadTestName: "GetCodeSuggestionAsync_ConcurrentLoad");

            // Assert
            AssertLoadTestPerformance(loadTest,
                minOperationsPerSecond: 1.0, // At least 1 operation per second
                maxAverageResponseTime: TimeSpan.FromSeconds(3),
                maxErrorRate: 0.1);
        }

        #endregion

        #region Streaming Performance Tests

        [TestMethod]
        [PerformanceTest(MaxExecutionTimeMs = 5000)]
        public async Task GetStreamingCodeSuggestionAsync_Performance_ShouldStreamEfficiently()
        {
            // Arrange
            var codeContext = MockFactory.CreateTestCodeContext();
            var streamingResults = new List<CodeSuggestion>();

            // Act
            var measurement = await MeasurePerformanceAsync(async () =>
            {
                await foreach (var suggestion in _ollamaService.GetStreamingCodeSuggestionAsync(codeContext, CancellationToken.None))
                {
                    streamingResults.Add(suggestion);
                    if (!suggestion.IsPartial)
                        break;
                }
                return streamingResults.Count;
            }, "GetStreamingCodeSuggestionAsync_StreamingRequest");

            // Assert
            AssertPerformance(measurement, TimeSpan.FromSeconds(5), maxMemoryKB: 2048);
            streamingResults.Should().NotBeEmpty();
            streamingResults.Should().Contain(s => !s.IsPartial); // Should have final result
        }

        [TestMethod]
        [PerformanceTest(MaxExecutionTimeMs = 15000)]
        public async Task GetStreamingCodeSuggestionAsync_Benchmark_ShouldMaintainThroughput()
        {
            // Arrange
            var codeContext = MockFactory.CreateTestCodeContext();

            // Act
            var benchmark = await RunBenchmarkAsync(async () =>
            {
                var resultCount = 0;
                await foreach (var suggestion in _ollamaService.GetStreamingCodeSuggestionAsync(codeContext, CancellationToken.None))
                {
                    resultCount++;
                    if (!suggestion.IsPartial)
                        break;
                }
                return resultCount;
            },
            iterations: 10,
            benchmarkName: "GetStreamingCodeSuggestionAsync_Benchmark");

            // Assert
            AssertBenchmarkPerformance(benchmark,
                maxAverageDuration: TimeSpan.FromSeconds(5),
                minSuccessRate: 0.9,
                maxMemoryKB: 3072);
        }

        #endregion

        #region Health Check Performance Tests

        [TestMethod]
        [PerformanceTest(MaxExecutionTimeMs = 1000)]
        public async Task GetHealthStatusAsync_Performance_ShouldBeVeryFast()
        {
            // Act
            var measurement = await MeasurePerformanceAsync(
                () => _ollamaService.GetHealthStatusAsync(CancellationToken.None),
                "GetHealthStatusAsync_HealthCheck");

            // Assert
            AssertPerformance(measurement, TimeSpan.FromMilliseconds(500), maxMemoryKB: 256);
            measurement.Result.Should().NotBeNull();
            measurement.Result.As<OllamaHealthStatus>().IsAvailable.Should().BeTrue();
        }

        [TestMethod]
        [PerformanceTest(MaxExecutionTimeMs = 5000)]
        public async Task GetHealthStatusAsync_HighFrequency_ShouldHandleFrequentChecks()
        {
            // Act
            var benchmark = await RunBenchmarkAsync(
                () => _ollamaService.GetHealthStatusAsync(CancellationToken.None),
                iterations: 50,
                benchmarkName: "GetHealthStatusAsync_HighFrequency");

            // Assert
            AssertBenchmarkPerformance(benchmark,
                maxAverageDuration: TimeSpan.FromMilliseconds(100),
                minSuccessRate: 0.98,
                maxMemoryKB: 512);
        }

        #endregion

        #region Model Management Performance Tests

        [TestMethod]
        [PerformanceTest(MaxExecutionTimeMs = 2000)]
        public async Task GetModelInfoAsync_Performance_ShouldCacheEffectively()
        {
            // Act - First call should be slower (cache miss)
            var firstMeasurement = await MeasurePerformanceAsync(
                () => _ollamaService.GetModelInfoAsync(),
                "GetModelInfoAsync_FirstCall");

            // Act - Second call should be faster (cache hit)
            var secondMeasurement = await MeasurePerformanceAsync(
                () => _ollamaService.GetModelInfoAsync(),
                "GetModelInfoAsync_CachedCall");

            // Assert
            AssertPerformance(firstMeasurement, TimeSpan.FromSeconds(2), maxMemoryKB: 512);
            AssertPerformance(secondMeasurement, TimeSpan.FromMilliseconds(100), maxMemoryKB: 256);
            
            // Cache should make subsequent calls much faster
            secondMeasurement.Duration.Should().BeLessThan(firstMeasurement.Duration);
        }

        #endregion

        #region Memory Usage Tests

        [TestMethod]
        [PerformanceTest(MaxExecutionTimeMs = 10000)]
        public async Task OllamaService_MemoryUsage_ShouldNotLeak()
        {
            // Arrange
            var initialMemory = GC.GetTotalMemory(true);
            var codeContext = MockFactory.CreateTestCodeContext();

            // Act - Perform many operations that could cause memory leaks
            for (int i = 0; i < 100; i++)
            {
                await _ollamaService.GetCodeSuggestionAsync(codeContext, CancellationToken.None);
                await _ollamaService.GetHealthStatusAsync(CancellationToken.None);
                
                // Force GC every 20 iterations
                if (i % 20 == 0)
                {
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                    GC.Collect();
                }
            }

            // Force final garbage collection
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            var finalMemory = GC.GetTotalMemory(false);
            var memoryIncrease = (finalMemory - initialMemory) / 1024.0; // Convert to KB

            // Assert
            WriteTestOutput($"Memory usage: Initial={initialMemory / 1024}KB, Final={finalMemory / 1024}KB, Increase={memoryIncrease:F1}KB");
            memoryIncrease.Should().BeLessThan(5120, "Memory increase should be less than 5MB after 100 operations");
        }

        #endregion

        #region Concurrent Access Tests

        [TestMethod]
        [PerformanceTest(MaxExecutionTimeMs = 20000)]
        public async Task OllamaService_ConcurrentAccess_ShouldHandleThreadSafely()
        {
            // Arrange
            var codeContext = MockFactory.CreateTestCodeContext();
            const int concurrentTasks = 10;
            const int operationsPerTask = 5;

            // Act
            var tasks = new List<Task<List<PerformanceMeasurement>>>();

            for (int i = 0; i < concurrentTasks; i++)
            {
                int taskId = i;
                var task = Task.Run(async () =>
                {
                    var measurements = new List<PerformanceMeasurement>();
                    for (int j = 0; j < operationsPerTask; j++)
                    {
                        var measurement = await MeasurePerformanceAsync(
                            () => _ollamaService.GetCodeSuggestionAsync(codeContext, CancellationToken.None),
                            $"ConcurrentTask_{taskId}_Operation_{j}", false);
                        measurements.Add(measurement);
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

            WriteTestOutput($"Concurrent access test completed: {successfulOperations}/{totalOperations} operations succeeded");

            var successRate = (double)successfulOperations / totalOperations;
            successRate.Should().BeGreaterOrEqualTo(0.9, "At least 90% of concurrent operations should succeed");

            var averageResponseTime = flattenedMeasurements.Where(m => m.Success).Average(m => m.Duration.TotalMilliseconds);
            averageResponseTime.Should().BeLessThan(5000, "Average response time under concurrent load should be under 5 seconds");
        }

        #endregion

        #region Error Handling Performance Tests

        [TestMethod]
        [PerformanceTest(MaxExecutionTimeMs = 5000)]
        public async Task OllamaService_ErrorHandling_ShouldFailFast()
        {
            // Arrange
            var mockHttpClientWithErrors = CreateMockHttpClientWithErrors();
            var errorService = new OllamaService(
                _mockSettingsService.Object,
                _mockLogger.Object,
                mockHttpClientWithErrors.Object);

            var codeContext = MockFactory.CreateTestCodeContext();

            // Act
            var measurement = await MeasurePerformanceAsync(async () =>
            {
                try
                {
                    return await errorService.GetCodeSuggestionAsync(codeContext, CancellationToken.None);
                }
                catch (Exception)
                {
                    return null; // Expected to fail
                }
            }, "OllamaService_ErrorHandling_FastFailure");

            // Assert
            // Error handling should be fast (under 1 second)
            measurement.Duration.Should().BeLessThan(TimeSpan.FromSeconds(1), 
                "Error handling should fail fast, not hang or retry excessively");

            WriteTestOutput($"Error handling completed in {measurement.Duration.TotalMilliseconds}ms");

            errorService.Dispose();
        }

        #endregion

        #region Helper Methods

        private Mock<OllamaHttpClient> CreateMockHttpClient()
        {
            var mock = new Mock<OllamaHttpClient>("http://localhost:11434", MockFactory.CreateTestHttpClientConfig());

            // Setup successful responses
            mock.Setup(x => x.SendCompletionAsync(It.IsAny<OllamaRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new OllamaResponse
                {
                    Response = "Test completion response",
                    Done = true,
                    Model = "codellama",
                    EvalCount = 50,
                    TotalDuration = 1000
                });

            mock.Setup(x => x.SendStreamingCompletionAsync(It.IsAny<OllamaRequest>(), It.IsAny<CancellationToken>()))
                .Returns(CreateMockStreamingResponse());

            mock.Setup(x => x.CheckHealthAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new OllamaHealthStatus
                {
                    IsAvailable = true,
                    ResponseTimeMs = 50,
                    Timestamp = DateTime.UtcNow
                });

            mock.Setup(x => x.GetModelsAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new OllamaModelsResponse
                {
                    Models = new[]
                    {
                        new OllamaModel { Name = "codellama", Size = 1000000 }
                    }
                });

            return mock;
        }

        private Mock<OllamaHttpClient> CreateMockHttpClientWithErrors()
        {
            var mock = new Mock<OllamaHttpClient>("http://localhost:11434", MockFactory.CreateTestHttpClientConfig());

            // Setup to throw exceptions
            mock.Setup(x => x.SendCompletionAsync(It.IsAny<OllamaRequest>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new OllamaConnectionException("Simulated connection error"));

            mock.Setup(x => x.CheckHealthAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new OllamaHealthStatus
                {
                    IsAvailable = false,
                    Error = "Simulated health check error"
                });

            return mock;
        }

        private async IAsyncEnumerable<OllamaStreamResponse> CreateMockStreamingResponse()
        {
            // Simulate streaming response with multiple chunks
            yield return new OllamaStreamResponse { Response = "Test ", Done = false };
            await Task.Delay(10); // Simulate network delay
            yield return new OllamaStreamResponse { Response = "streaming ", Done = false };
            await Task.Delay(10);
            yield return new OllamaStreamResponse { Response = "response", Done = false };
            await Task.Delay(10);
            yield return new OllamaStreamResponse { Response = "", Done = true, Model = "codellama" };
        }

        #endregion
    }
}