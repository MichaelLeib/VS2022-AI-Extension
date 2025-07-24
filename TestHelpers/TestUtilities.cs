using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OllamaAssistant.Tests.TestHelpers
{
    /// <summary>
    /// Utility methods for testing
    /// </summary>
    public static class TestUtilities
    {
        /// <summary>
        /// Executes an async method and waits for completion with timeout
        /// </summary>
        public static async Task<T> ExecuteWithTimeoutAsync<T>(Func<Task<T>> operation, int timeoutMs = 5000)
        {
            using (var cts = new CancellationTokenSource(timeoutMs))
            {
                try
                {
                    return await operation();
                }
                catch (OperationCanceledException) when (cts.Token.IsCancellationRequested)
                {
                    Assert.Fail($"Operation timed out after {timeoutMs}ms");
                    return default(T);
                }
            }
        }

        /// <summary>
        /// Executes an async method and waits for completion with timeout
        /// </summary>
        public static async Task ExecuteWithTimeoutAsync(Func<Task> operation, int timeoutMs = 5000)
        {
            using (var cts = new CancellationTokenSource(timeoutMs))
            {
                try
                {
                    await operation();
                }
                catch (OperationCanceledException) when (cts.Token.IsCancellationRequested)
                {
                    Assert.Fail($"Operation timed out after {timeoutMs}ms");
                }
            }
        }

        /// <summary>
        /// Waits for a condition to become true with timeout
        /// </summary>
        public static async Task WaitForConditionAsync(Func<bool> condition, int timeoutMs = 5000, int pollIntervalMs = 100)
        {
            var timeout = DateTime.Now.AddMilliseconds(timeoutMs);
            
            while (DateTime.Now < timeout)
            {
                if (condition())
                    return;
                    
                await Task.Delay(pollIntervalMs);
            }
            
            Assert.Fail($"Condition was not met within {timeoutMs}ms");
        }

        /// <summary>
        /// Waits for an async condition to become true with timeout
        /// </summary>
        public static async Task WaitForConditionAsync(Func<Task<bool>> condition, int timeoutMs = 5000, int pollIntervalMs = 100)
        {
            var timeout = DateTime.Now.AddMilliseconds(timeoutMs);
            
            while (DateTime.Now < timeout)
            {
                if (await condition())
                    return;
                    
                await Task.Delay(pollIntervalMs);
            }
            
            Assert.Fail($"Condition was not met within {timeoutMs}ms");
        }

        /// <summary>
        /// Measures the execution time of an operation
        /// </summary>
        public static async Task<TimeSpan> MeasureExecutionTimeAsync(Func<Task> operation)
        {
            var startTime = DateTime.UtcNow;
            await operation();
            return DateTime.UtcNow - startTime;
        }

        /// <summary>
        /// Measures the execution time of an operation with result
        /// </summary>
        public static async Task<(T result, TimeSpan duration)> MeasureExecutionTimeAsync<T>(Func<Task<T>> operation)
        {
            var startTime = DateTime.UtcNow;
            var result = await operation();
            var duration = DateTime.UtcNow - startTime;
            return (result, duration);
        }

        /// <summary>
        /// Asserts that an async operation throws a specific exception type
        /// </summary>
        public static async Task AssertThrowsAsync<TException>(Func<Task> operation, string expectedMessage = null)
            where TException : Exception
        {
            try
            {
                await operation();
                Assert.Fail($"Expected {typeof(TException).Name} to be thrown, but no exception was thrown.");
            }
            catch (TException ex)
            {
                if (!string.IsNullOrEmpty(expectedMessage) && !ex.Message.Contains(expectedMessage))
                {
                    Assert.Fail($"Expected exception message to contain '{expectedMessage}', but was '{ex.Message}'");
                }
                // Test passed
            }
            catch (Exception ex)
            {
                Assert.Fail($"Expected {typeof(TException).Name} to be thrown, but {ex.GetType().Name} was thrown instead: {ex.Message}");
            }
        }

        /// <summary>
        /// Asserts that an async operation does not throw any exception
        /// </summary>
        public static async Task AssertDoesNotThrowAsync(Func<Task> operation)
        {
            try
            {
                await operation();
            }
            catch (Exception ex)
            {
                Assert.Fail($"Expected no exception to be thrown, but {ex.GetType().Name} was thrown: {ex.Message}");
            }
        }

        /// <summary>
        /// Creates a temporary test file and returns its path
        /// </summary>
        public static string CreateTempTestFile(string content = "", string extension = ".cs")
        {
            var tempPath = System.IO.Path.GetTempFileName();
            var testPath = System.IO.Path.ChangeExtension(tempPath, extension);
            
            if (tempPath != testPath)
            {
                System.IO.File.Move(tempPath, testPath);
            }
            
            if (!string.IsNullOrEmpty(content))
            {
                System.IO.File.WriteAllText(testPath, content);
            }
            
            return testPath;
        }

        /// <summary>
        /// Cleans up a temporary test file
        /// </summary>
        public static void CleanupTempFile(string filePath)
        {
            try
            {
                if (System.IO.File.Exists(filePath))
                {
                    System.IO.File.Delete(filePath);
                }
            }
            catch (Exception ex)
            {
                // Log but don't fail tests due to cleanup issues
                System.Diagnostics.Debug.WriteLine($"Failed to clean up temp file {filePath}: {ex.Message}");
            }
        }

        /// <summary>
        /// Generates sample C# code for testing
        /// </summary>
        public static string GenerateSampleCSharpCode(int methodCount = 3)
        {
            var code = @"using System;
using System.Collections.Generic;
using System.Linq;

namespace TestNamespace
{
    public class TestClass
    {";
            
            for (int i = 1; i <= methodCount; i++)
            {
                code += $@"
        public void TestMethod{i}()
        {{
            Console.WriteLine(""Method {i}"");
            // Some sample code
            var list = new List<int> {{ 1, 2, 3 }};
            var result = list.Where(x => x > 1).ToList();
        }}";
            }
            
            code += @"
    }
}";
            
            return code;
        }

        /// <summary>
        /// Generates sample JavaScript code for testing
        /// </summary>
        public static string GenerateSampleJavaScriptCode()
        {
            return @"
function calculateSum(numbers) {
    return numbers.reduce((sum, num) => sum + num, 0);
}

function calculateAverage(numbers) {
    if (numbers.length === 0) return 0;
    return calculateSum(numbers) / numbers.length;
}

class Calculator {
    constructor() {
        this.history = [];
    }
    
    add(a, b) {
        const result = a + b;
        this.history.push(`${a} + ${b} = ${result}`);
        return result;
    }
    
    getHistory() {
        return this.history.slice();
    }
}";
        }

        /// <summary>
        /// Creates a test cancellation token that cancels after specified milliseconds
        /// </summary>
        public static CancellationToken CreateTestCancellationToken(int cancelAfterMs = 5000)
        {
            var cts = new CancellationTokenSource(cancelAfterMs);
            return cts.Token;
        }
    }
}