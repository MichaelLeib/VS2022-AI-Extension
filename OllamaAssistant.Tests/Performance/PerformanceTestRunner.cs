using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OllamaAssistant.Tests.TestUtilities;

namespace OllamaAssistant.Tests.Performance
{
    /// <summary>
    /// Test runner for executing performance tests and generating reports
    /// </summary>
    [TestClass]
    [TestCategory(TestCategories.Performance)]
    public class PerformanceTestRunner
    {
        private static readonly List<PerformanceTestResult> TestResults = new List<PerformanceTestResult>();

        [AssemblyInitialize]
        public static void AssemblyInitialize(TestContext context)
        {
            TestResults.Clear();
            Console.WriteLine("Performance Test Suite Starting...");
        }

        [AssemblyCleanup]
        public static void AssemblyCleanup()
        {
            GeneratePerformanceReport();
            Console.WriteLine("Performance Test Suite Completed.");
        }

        [TestMethod]
        [TestCategory(TestCategories.Performance)]
        public async Task RunAllPerformanceTests()
        {
            var performanceTestTypes = GetPerformanceTestTypes();
            var totalTestMethods = 0;
            var executedTests = 0;
            var failedTests = 0;

            Console.WriteLine($"Found {performanceTestTypes.Count} performance test classes");

            foreach (var testType in performanceTestTypes)
            {
                Console.WriteLine($"\nExecuting tests in {testType.Name}...");
                
                var testMethods = GetPerformanceTestMethods(testType);
                totalTestMethods += testMethods.Count;

                foreach (var testMethod in testMethods)
                {
                    var result = await ExecutePerformanceTest(testType, testMethod);
                    TestResults.Add(result);
                    executedTests++;

                    if (!result.Success)
                    {
                        failedTests++;
                        Console.WriteLine($"  ❌ {testMethod.Name}: {result.ErrorMessage}");
                    }
                    else
                    {
                        var duration = result.ExecutionTime.TotalMilliseconds;
                        var maxTime = GetMaxExecutionTime(testMethod);
                        var status = duration <= maxTime ? "✅" : "⚠️";
                        Console.WriteLine($"  {status} {testMethod.Name}: {duration:F1}ms (max: {maxTime}ms)");
                    }
                }
            }

            Console.WriteLine($"\nPerformance Test Summary:");
            Console.WriteLine($"  Total test methods: {totalTestMethods}");
            Console.WriteLine($"  Executed: {executedTests}");
            Console.WriteLine($"  Failed: {failedTests}");
            Console.WriteLine($"  Success rate: {(double)(executedTests - failedTests) / executedTests:P1}");
        }

        [TestMethod]
        [TestCategory(TestCategories.Performance)]
        public void ValidatePerformanceTestConfiguration()
        {
            var performanceTestTypes = GetPerformanceTestTypes();
            var issues = new List<string>();

            foreach (var testType in performanceTestTypes)
            {
                // Check if class inherits from PerformanceTestBase
                if (!typeof(PerformanceTestBase).IsAssignableFrom(testType))
                {
                    issues.Add($"{testType.Name} should inherit from PerformanceTestBase");
                }

                // Check test methods
                var testMethods = GetPerformanceTestMethods(testType);
                foreach (var method in testMethods)
                {
                    // Check for PerformanceTest attribute
                    var perfAttr = method.GetCustomAttribute<PerformanceTestAttribute>();
                    if (perfAttr == null)
                    {
                        issues.Add($"{testType.Name}.{method.Name} should have [PerformanceTest] attribute");
                    }

                    // Check return type
                    if (method.ReturnType != typeof(Task) && method.ReturnType != typeof(void))
                    {
                        issues.Add($"{testType.Name}.{method.Name} should return Task or void");
                    }

                    // Check naming convention
                    if (!method.Name.Contains("Performance") && !method.Name.Contains("Benchmark") && !method.Name.Contains("LoadTest"))
                    {
                        issues.Add($"{testType.Name}.{method.Name} should include 'Performance', 'Benchmark', or 'LoadTest' in the name");
                    }
                }
            }

            if (issues.Any())
            {
                var message = "Performance test configuration issues found:\n" + string.Join("\n", issues);
                Console.WriteLine(message);
                throw new InvalidOperationException(message);
            }

            Console.WriteLine($"Performance test configuration validated successfully. Found {performanceTestTypes.Count} test classes with performance tests.");
        }

        #region Performance Test Execution

        private static async Task<PerformanceTestResult> ExecutePerformanceTest(Type testType, MethodInfo testMethod)
        {
            var result = new PerformanceTestResult
            {
                TestClass = testType.Name,
                TestMethod = testMethod.Name,
                StartTime = DateTime.UtcNow
            };

            try
            {
                // Create test instance
                var testInstance = Activator.CreateInstance(testType);
                
                // Initialize if needed
                var initMethod = testType.GetMethod("TestInitialize");
                initMethod?.Invoke(testInstance, null);

                var startTime = DateTime.UtcNow;

                // Execute test method
                var task = testMethod.Invoke(testInstance, null);
                if (task is Task asyncTask)
                {
                    await asyncTask;
                }

                var endTime = DateTime.UtcNow;
                result.ExecutionTime = endTime - startTime;
                result.Success = true;

                // Cleanup if needed
                var cleanupMethod = testType.GetMethod("TestCleanup");
                cleanupMethod?.Invoke(testInstance, null);

                // Dispose if needed
                if (testInstance is IDisposable disposable)
                {
                    disposable.Dispose();
                }
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = ex.InnerException?.Message ?? ex.Message;
                result.Exception = ex.InnerException ?? ex;
            }
            finally
            {
                result.EndTime = DateTime.UtcNow;
            }

            return result;
        }

        #endregion

        #region Test Discovery

        private static List<Type> GetPerformanceTestTypes()
        {
            var assembly = Assembly.GetExecutingAssembly();
            return assembly.GetTypes()
                .Where(t => t.IsClass && !t.IsAbstract)
                .Where(t => t.GetCustomAttribute<TestClassAttribute>() != null)
                .Where(t => t.GetMethods().Any(m => m.GetCustomAttribute<PerformanceTestAttribute>() != null))
                .OrderBy(t => t.Name)
                .ToList();
        }

        private static List<MethodInfo> GetPerformanceTestMethods(Type testType)
        {
            return testType.GetMethods()
                .Where(m => m.GetCustomAttribute<TestMethodAttribute>() != null)
                .Where(m => m.GetCustomAttribute<PerformanceTestAttribute>() != null)
                .OrderBy(m => m.Name)
                .ToList();
        }

        private static double GetMaxExecutionTime(MethodInfo testMethod)
        {
            var perfAttr = testMethod.GetCustomAttribute<PerformanceTestAttribute>();
            return perfAttr?.MaxExecutionTimeMs ?? 5000; // Default 5 seconds
        }

        #endregion

        #region Report Generation

        private static void GeneratePerformanceReport()
        {
            if (!TestResults.Any())
            {
                Console.WriteLine("No performance test results to report.");
                return;
            }

            var reportBuilder = new StringBuilder();
            reportBuilder.AppendLine("# Performance Test Report");
            reportBuilder.AppendLine($"Generated: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
            reportBuilder.AppendLine();

            // Summary
            var totalTests = TestResults.Count;
            var successfulTests = TestResults.Count(r => r.Success);
            var failedTests = totalTests - successfulTests;

            reportBuilder.AppendLine("## Summary");
            reportBuilder.AppendLine($"- Total Tests: {totalTests}");
            reportBuilder.AppendLine($"- Successful: {successfulTests}");
            reportBuilder.AppendLine($"- Failed: {failedTests}");
            reportBuilder.AppendLine($"- Success Rate: {(double)successfulTests / totalTests:P1}");
            reportBuilder.AppendLine();

            // Performance Statistics
            var successfulResults = TestResults.Where(r => r.Success).ToList();
            if (successfulResults.Any())
            {
                reportBuilder.AppendLine("## Performance Statistics");
                reportBuilder.AppendLine($"- Average Execution Time: {successfulResults.Average(r => r.ExecutionTime.TotalMilliseconds):F1}ms");
                reportBuilder.AppendLine($"- Fastest Test: {successfulResults.Min(r => r.ExecutionTime.TotalMilliseconds):F1}ms ({successfulResults.OrderBy(r => r.ExecutionTime).First().TestName})");
                reportBuilder.AppendLine($"- Slowest Test: {successfulResults.Max(r => r.ExecutionTime.TotalMilliseconds):F1}ms ({successfulResults.OrderByDescending(r => r.ExecutionTime).First().TestName})");
                reportBuilder.AppendLine();
            }

            // Test Results by Class
            var resultsByClass = TestResults.GroupBy(r => r.TestClass).OrderBy(g => g.Key);

            reportBuilder.AppendLine("## Detailed Results");
            foreach (var classGroup in resultsByClass)
            {
                reportBuilder.AppendLine($"### {classGroup.Key}");
                reportBuilder.AppendLine();

                foreach (var result in classGroup.OrderBy(r => r.TestMethod))
                {
                    var status = result.Success ? "✅ PASS" : "❌ FAIL";
                    var duration = result.Success ? $"{result.ExecutionTime.TotalMilliseconds:F1}ms" : "N/A";
                    
                    reportBuilder.AppendLine($"- **{result.TestMethod}**: {status} ({duration})");
                    
                    if (!result.Success)
                    {
                        reportBuilder.AppendLine($"  - Error: {result.ErrorMessage}");
                    }
                }
                reportBuilder.AppendLine();
            }

            // Performance Alerts
            var alerts = GeneratePerformanceAlerts();
            if (alerts.Any())
            {
                reportBuilder.AppendLine("## Performance Alerts");
                foreach (var alert in alerts)
                {
                    reportBuilder.AppendLine($"- ⚠️ {alert}");
                }
                reportBuilder.AppendLine();
            }

            // Recommendations
            var recommendations = GenerateRecommendations();
            if (recommendations.Any())
            {
                reportBuilder.AppendLine("## Recommendations");
                foreach (var recommendation in recommendations)
                {
                    reportBuilder.AppendLine($"- 💡 {recommendation}");
                }
                reportBuilder.AppendLine();
            }

            var report = reportBuilder.ToString();
            
            // Output to console
            Console.WriteLine("\n" + report);

            // Save to file
            try
            {
                var reportPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "PerformanceTestReport.md");
                File.WriteAllText(reportPath, report);
                Console.WriteLine($"Performance report saved to: {reportPath}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to save performance report: {ex.Message}");
            }
        }

        private static List<string> GeneratePerformanceAlerts()
        {
            var alerts = new List<string>();
            var successfulResults = TestResults.Where(r => r.Success).ToList();

            if (!successfulResults.Any())
                return alerts;

            // Check for slow tests
            var slowTests = successfulResults.Where(r => r.ExecutionTime.TotalMilliseconds > 5000).ToList();
            if (slowTests.Any())
            {
                alerts.Add($"{slowTests.Count} tests took longer than 5 seconds to execute");
            }

            // Check for highly variable performance
            var avgExecutionTime = successfulResults.Average(r => r.ExecutionTime.TotalMilliseconds);
            var variableTests = successfulResults.Where(r => 
                Math.Abs(r.ExecutionTime.TotalMilliseconds - avgExecutionTime) > avgExecutionTime * 0.5).ToList();
            
            if (variableTests.Any())
            {
                alerts.Add($"{variableTests.Count} tests have highly variable performance (>50% deviation from average)");
            }

            // Check overall failure rate
            var failureRate = (double)TestResults.Count(r => !r.Success) / TestResults.Count;
            if (failureRate > 0.1) // More than 10% failures
            {
                alerts.Add($"High failure rate: {failureRate:P1} of performance tests failed");
            }

            return alerts;
        }

        private static List<string> GenerateRecommendations()
        {
            var recommendations = new List<string>();
            var successfulResults = TestResults.Where(r => r.Success).ToList();

            if (!successfulResults.Any())
                return recommendations;

            // Recommend adding more concurrent tests
            var concurrentTests = TestResults.Count(r => r.TestMethod.Contains("Concurrent") || r.TestMethod.Contains("LoadTest"));
            if (concurrentTests < 3)
            {
                recommendations.Add("Consider adding more concurrent/load tests to validate thread safety and scalability");
            }

            // Recommend memory testing
            var memoryTests = TestResults.Count(r => r.TestMethod.Contains("Memory"));
            if (memoryTests < 2)
            {
                recommendations.Add("Consider adding more memory usage tests to detect potential memory leaks");
            }

            // Recommend benchmark tests
            var benchmarkTests = TestResults.Count(r => r.TestMethod.Contains("Benchmark"));
            if (benchmarkTests < TestResults.GroupBy(r => r.TestClass).Count())
            {
                recommendations.Add("Consider adding benchmark tests for each service to track performance over time");
            }

            return recommendations;
        }

        #endregion
    }

    /// <summary>
    /// Represents the result of a performance test execution
    /// </summary>
    public class PerformanceTestResult
    {
        public string TestClass { get; set; }
        public string TestMethod { get; set; }
        public string TestName => $"{TestClass}.{TestMethod}";
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public TimeSpan ExecutionTime { get; set; }
        public bool Success { get; set; }
        public string ErrorMessage { get; set; }
        public Exception Exception { get; set; }
    }
}