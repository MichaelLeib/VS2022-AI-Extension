using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OllamaAssistant.Tests.TestUtilities;

namespace OllamaAssistant.Tests.Performance
{
    /// <summary>
    /// Base class for performance tests with benchmarking utilities
    /// </summary>
    [TestClass]
    public abstract class PerformanceTestBase : BaseTest
    {
        protected PerformanceMetrics Metrics { get; private set; }
        private readonly List<PerformanceMeasurement> _measurements = new List<PerformanceMeasurement>();

        [TestInitialize]
        public override void TestInitialize()
        {
            base.TestInitialize();
            Metrics = new PerformanceMetrics();
            _measurements.Clear();
        }

        [TestCleanup]
        public override void TestCleanup()
        {
            // Output performance summary
            if (_measurements.Any())
            {
                WriteTestOutput($"Performance Summary for {TestContext.TestName}:");
                WriteTestOutput($"Total measurements: {_measurements.Count}");
                WriteTestOutput($"Average duration: {_measurements.Average(m => m.Duration.TotalMilliseconds):F2}ms");
                WriteTestOutput($"Min duration: {_measurements.Min(m => m.Duration.TotalMilliseconds):F2}ms");
                WriteTestOutput($"Max duration: {_measurements.Max(m => m.Duration.TotalMilliseconds):F2}ms");
                WriteTestOutput($"Memory usage range: {_measurements.Min(m => m.MemoryUsedKB):F0}KB - {_measurements.Max(m => m.MemoryUsedKB):F0}KB");
            }

            base.TestCleanup();
        }

        #region Performance Measurement Methods

        /// <summary>
        /// Measures the execution time and memory usage of an operation
        /// </summary>
        protected async Task<PerformanceMeasurement> MeasurePerformanceAsync<T>(
            Func<Task<T>> operation, 
            string operationName = null,
            bool forceGC = true)
        {
            if (forceGC)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
            }

            var initialMemory = GC.GetTotalMemory(false);
            var stopwatch = Stopwatch.StartNew();

            T result;
            Exception exception = null;

            try
            {
                result = await operation();
            }
            catch (Exception ex)
            {
                exception = ex;
                result = default(T);
            }

            stopwatch.Stop();
            
            if (forceGC)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
            }
            
            var finalMemory = GC.GetTotalMemory(false);
            var memoryUsed = (finalMemory - initialMemory) / 1024.0; // Convert to KB

            var measurement = new PerformanceMeasurement
            {
                OperationName = operationName ?? "Anonymous Operation",
                Duration = stopwatch.Elapsed,
                MemoryUsedKB = memoryUsed,
                Success = exception == null,
                Exception = exception,
                Result = result,
                Timestamp = DateTime.UtcNow
            };

            _measurements.Add(measurement);
            return measurement;
        }

        /// <summary>
        /// Measures the execution time of a synchronous operation
        /// </summary>
        protected PerformanceMeasurement MeasurePerformance<T>(
            Func<T> operation, 
            string operationName = null,
            bool forceGC = true)
        {
            if (forceGC)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
            }

            var initialMemory = GC.GetTotalMemory(false);
            var stopwatch = Stopwatch.StartNew();

            T result;
            Exception exception = null;

            try
            {
                result = operation();
            }
            catch (Exception ex)
            {
                exception = ex;
                result = default(T);
            }

            stopwatch.Stop();
            
            if (forceGC)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
            }
            
            var finalMemory = GC.GetTotalMemory(false);
            var memoryUsed = (finalMemory - initialMemory) / 1024.0; // Convert to KB

            var measurement = new PerformanceMeasurement
            {
                OperationName = operationName ?? "Anonymous Operation",
                Duration = stopwatch.Elapsed,
                MemoryUsedKB = memoryUsed,
                Success = exception == null,
                Exception = exception,
                Result = result,
                Timestamp = DateTime.UtcNow
            };

            _measurements.Add(measurement);
            return measurement;
        }

        /// <summary>
        /// Runs a performance benchmark with multiple iterations
        /// </summary>
        protected async Task<BenchmarkResult> RunBenchmarkAsync<T>(
            Func<Task<T>> operation,
            int iterations = 10,
            string benchmarkName = null,
            TimeSpan? warmupTime = null)
        {
            benchmarkName = benchmarkName ?? "Benchmark";
            warmupTime = warmupTime ?? TimeSpan.FromSeconds(1);

            WriteTestOutput($"Starting benchmark '{benchmarkName}' with {iterations} iterations");

            // Warmup phase
            var warmupEnd = DateTime.UtcNow.Add(warmupTime.Value);
            while (DateTime.UtcNow < warmupEnd)
            {
                try
                {
                    await operation();
                }
                catch
                {
                    // Ignore warmup failures
                }
            }

            WriteTestOutput($"Warmup completed. Starting {iterations} measured iterations...");

            var measurements = new List<PerformanceMeasurement>();

            for (int i = 0; i < iterations; i++)
            {
                var measurement = await MeasurePerformanceAsync(operation, $"{benchmarkName}_Iteration_{i + 1}");
                measurements.Add(measurement);

                if ((i + 1) % Math.Max(1, iterations / 4) == 0)
                {
                    WriteTestOutput($"Completed {i + 1}/{iterations} iterations");
                }
            }

            var result = new BenchmarkResult
            {
                BenchmarkName = benchmarkName,
                Iterations = iterations,
                Measurements = measurements,
                AverageDuration = TimeSpan.FromMilliseconds(measurements.Average(m => m.Duration.TotalMilliseconds)),
                MinDuration = measurements.Min(m => m.Duration),
                MaxDuration = measurements.Max(m => m.Duration),
                MedianDuration = CalculateMedianDuration(measurements),
                StandardDeviation = CalculateStandardDeviation(measurements),
                SuccessRate = (double)measurements.Count(m => m.Success) / measurements.Count,
                AverageMemoryUsageKB = measurements.Average(m => m.MemoryUsedKB),
                TotalExecutionTime = TimeSpan.FromMilliseconds(measurements.Sum(m => m.Duration.TotalMilliseconds))
            };

            WriteTestOutput($"Benchmark '{benchmarkName}' completed:");
            WriteTestOutput($"  Average: {result.AverageDuration.TotalMilliseconds:F2}ms");
            WriteTestOutput($"  Median: {result.MedianDuration.TotalMilliseconds:F2}ms");
            WriteTestOutput($"  Min: {result.MinDuration.TotalMilliseconds:F2}ms");
            WriteTestOutput($"  Max: {result.MaxDuration.TotalMilliseconds:F2}ms");
            WriteTestOutput($"  Std Dev: {result.StandardDeviation:F2}ms");
            WriteTestOutput($"  Success Rate: {result.SuccessRate:P1}");
            WriteTestOutput($"  Avg Memory: {result.AverageMemoryUsageKB:F1}KB");

            return result;
        }

        /// <summary>
        /// Performs a load test with concurrent operations
        /// </summary>
        protected async Task<LoadTestResult> RunLoadTestAsync<T>(
            Func<Task<T>> operation,
            int concurrentOperations = 10,
            TimeSpan? testDuration = null,
            string loadTestName = null)
        {
            loadTestName = loadTestName ?? "Load Test";
            testDuration = testDuration ?? TimeSpan.FromSeconds(30);

            WriteTestOutput($"Starting load test '{loadTestName}' with {concurrentOperations} concurrent operations for {testDuration.Value.TotalSeconds}s");

            var measurements = new List<PerformanceMeasurement>();
            var startTime = DateTime.UtcNow;
            var endTime = startTime.Add(testDuration.Value);
            var tasks = new List<Task>();

            // Start concurrent operations
            for (int i = 0; i < concurrentOperations; i++)
            {
                int operationId = i;
                var task = Task.Run(async () =>
                {
                    int iterationCount = 0;
                    while (DateTime.UtcNow < endTime)
                    {
                        try
                        {
                            var measurement = await MeasurePerformanceAsync(operation, 
                                $"{loadTestName}_Worker_{operationId}_Iteration_{++iterationCount}", false);
                            
                            lock (measurements)
                            {
                                measurements.Add(measurement);
                            }
                        }
                        catch (Exception ex)
                        {
                            lock (measurements)
                            {
                                measurements.Add(new PerformanceMeasurement
                                {
                                    OperationName = $"{loadTestName}_Worker_{operationId}_Iteration_{iterationCount}",
                                    Duration = TimeSpan.Zero,
                                    Success = false,
                                    Exception = ex,
                                    Timestamp = DateTime.UtcNow
                                });
                            }
                        }

                        // Small delay to prevent overwhelming the system
                        await Task.Delay(10);
                    }
                });
                tasks.Add(task);
            }

            // Wait for all tasks to complete
            await Task.WhenAll(tasks);

            var actualDuration = DateTime.UtcNow - startTime;
            var totalOperations = measurements.Count;
            var successfulOperations = measurements.Count(m => m.Success);

            var result = new LoadTestResult
            {
                LoadTestName = loadTestName,
                ConcurrentOperations = concurrentOperations,
                TotalOperations = totalOperations,
                SuccessfulOperations = successfulOperations,
                FailedOperations = totalOperations - successfulOperations,
                TestDuration = actualDuration,
                OperationsPerSecond = totalOperations / actualDuration.TotalSeconds,
                AverageResponseTime = TimeSpan.FromMilliseconds(
                    measurements.Where(m => m.Success).Average(m => m.Duration.TotalMilliseconds)),
                MinResponseTime = measurements.Where(m => m.Success).Min(m => m.Duration),
                MaxResponseTime = measurements.Where(m => m.Success).Max(m => m.Duration),
                ErrorRate = (double)(totalOperations - successfulOperations) / totalOperations,
                Measurements = measurements
            };

            WriteTestOutput($"Load test '{loadTestName}' completed:");
            WriteTestOutput($"  Total operations: {result.TotalOperations}");
            WriteTestOutput($"  Successful: {result.SuccessfulOperations}");
            WriteTestOutput($"  Failed: {result.FailedOperations}");
            WriteTestOutput($"  Operations/sec: {result.OperationsPerSecond:F2}");
            WriteTestOutput($"  Average response: {result.AverageResponseTime.TotalMilliseconds:F2}ms");
            WriteTestOutput($"  Error rate: {result.ErrorRate:P1}");

            return result;
        }

        #endregion

        #region Performance Assertions

        /// <summary>
        /// Asserts that an operation completes within a specified time
        /// </summary>
        protected void AssertPerformance(PerformanceMeasurement measurement, TimeSpan maxDuration, double maxMemoryKB = double.MaxValue)
        {
            measurement.Success.Should().BeTrue($"Operation '{measurement.OperationName}' should succeed, but failed with: {measurement.Exception?.Message}");
            measurement.Duration.Should().BeLessThan(maxDuration, 
                $"Operation '{measurement.OperationName}' took {measurement.Duration.TotalMilliseconds}ms, expected less than {maxDuration.TotalMilliseconds}ms");
            
            if (maxMemoryKB < double.MaxValue)
            {
                measurement.MemoryUsedKB.Should().BeLessThan(maxMemoryKB,
                    $"Operation '{measurement.OperationName}' used {measurement.MemoryUsedKB}KB, expected less than {maxMemoryKB}KB");
            }
        }

        /// <summary>
        /// Asserts that a benchmark meets performance requirements
        /// </summary>
        protected void AssertBenchmarkPerformance(BenchmarkResult benchmark, 
            TimeSpan maxAverageDuration, 
            double minSuccessRate = 0.95,
            double maxMemoryKB = double.MaxValue)
        {
            benchmark.AverageDuration.Should().BeLessThan(maxAverageDuration,
                $"Benchmark '{benchmark.BenchmarkName}' average duration {benchmark.AverageDuration.TotalMilliseconds}ms exceeds limit {maxAverageDuration.TotalMilliseconds}ms");
            
            benchmark.SuccessRate.Should().BeGreaterOrEqualTo(minSuccessRate,
                $"Benchmark '{benchmark.BenchmarkName}' success rate {benchmark.SuccessRate:P1} is below required {minSuccessRate:P1}");

            if (maxMemoryKB < double.MaxValue)
            {
                benchmark.AverageMemoryUsageKB.Should().BeLessThan(maxMemoryKB,
                    $"Benchmark '{benchmark.BenchmarkName}' average memory usage {benchmark.AverageMemoryUsageKB}KB exceeds limit {maxMemoryKB}KB");
            }
        }

        /// <summary>
        /// Asserts that a load test meets performance requirements
        /// </summary>
        protected void AssertLoadTestPerformance(LoadTestResult loadTest,
            double minOperationsPerSecond,
            TimeSpan maxAverageResponseTime,
            double maxErrorRate = 0.05)
        {
            loadTest.OperationsPerSecond.Should().BeGreaterOrEqualTo(minOperationsPerSecond,
                $"Load test '{loadTest.LoadTestName}' throughput {loadTest.OperationsPerSecond:F2} ops/sec is below required {minOperationsPerSecond} ops/sec");

            loadTest.AverageResponseTime.Should().BeLessThan(maxAverageResponseTime,
                $"Load test '{loadTest.LoadTestName}' average response time {loadTest.AverageResponseTime.TotalMilliseconds}ms exceeds limit {maxAverageResponseTime.TotalMilliseconds}ms");

            loadTest.ErrorRate.Should().BeLessOrEqualTo(maxErrorRate,
                $"Load test '{loadTest.LoadTestName}' error rate {loadTest.ErrorRate:P1} exceeds limit {maxErrorRate:P1}");
        }

        #endregion

        #region Helper Methods

        private TimeSpan CalculateMedianDuration(List<PerformanceMeasurement> measurements)
        {
            var sortedDurations = measurements.Select(m => m.Duration).OrderBy(d => d).ToList();
            int count = sortedDurations.Count;
            
            if (count % 2 == 0)
            {
                var mid1 = sortedDurations[count / 2 - 1];
                var mid2 = sortedDurations[count / 2];
                return TimeSpan.FromMilliseconds((mid1.TotalMilliseconds + mid2.TotalMilliseconds) / 2);
            }
            else
            {
                return sortedDurations[count / 2];
            }
        }

        private double CalculateStandardDeviation(List<PerformanceMeasurement> measurements)
        {
            var durations = measurements.Select(m => m.Duration.TotalMilliseconds).ToList();
            var average = durations.Average();
            var sumOfSquaredDifferences = durations.Sum(d => Math.Pow(d - average, 2));
            return Math.Sqrt(sumOfSquaredDifferences / durations.Count);
        }

        #endregion
    }

    /// <summary>
    /// Represents a single performance measurement
    /// </summary>
    public class PerformanceMeasurement
    {
        public string OperationName { get; set; }
        public TimeSpan Duration { get; set; }
        public double MemoryUsedKB { get; set; }
        public bool Success { get; set; }
        public Exception Exception { get; set; }
        public object Result { get; set; }
        public DateTime Timestamp { get; set; }
    }

    /// <summary>
    /// Represents the results of a performance benchmark
    /// </summary>
    public class BenchmarkResult
    {
        public string BenchmarkName { get; set; }
        public int Iterations { get; set; }
        public List<PerformanceMeasurement> Measurements { get; set; }
        public TimeSpan AverageDuration { get; set; }
        public TimeSpan MinDuration { get; set; }
        public TimeSpan MaxDuration { get; set; }
        public TimeSpan MedianDuration { get; set; }
        public double StandardDeviation { get; set; }
        public double SuccessRate { get; set; }
        public double AverageMemoryUsageKB { get; set; }
        public TimeSpan TotalExecutionTime { get; set; }
    }

    /// <summary>
    /// Represents the results of a load test
    /// </summary>
    public class LoadTestResult
    {
        public string LoadTestName { get; set; }
        public int ConcurrentOperations { get; set; }
        public int TotalOperations { get; set; }
        public int SuccessfulOperations { get; set; }
        public int FailedOperations { get; set; }
        public TimeSpan TestDuration { get; set; }
        public double OperationsPerSecond { get; set; }
        public TimeSpan AverageResponseTime { get; set; }
        public TimeSpan MinResponseTime { get; set; }
        public TimeSpan MaxResponseTime { get; set; }
        public double ErrorRate { get; set; }
        public List<PerformanceMeasurement> Measurements { get; set; }
    }

    /// <summary>
    /// Performance metrics collection
    /// </summary>
    public class PerformanceMetrics
    {
        public long TotalAllocatedBytes { get; set; }
        public TimeSpan TotalCpuTime { get; set; }
        public int ThreadCount { get; set; }
        public long HandleCount { get; set; }
    }
}