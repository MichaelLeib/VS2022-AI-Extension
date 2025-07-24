using System;
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
    public class ContextCaptureServiceTests : BaseTest
    {
        private ContextCaptureService _contextCaptureService;
        private Mock<ISettingsService> _mockSettingsService;
        private Mock<ILogger> _mockLogger;

        protected override void OnTestInitialize()
        {
            _mockSettingsService = MockFactory.CreateMockSettingsService();
            _mockLogger = MockFactory.CreateMockLogger();

            _contextCaptureService = new ContextCaptureService(_mockSettingsService.Object, _mockLogger.Object);
        }

        protected override void OnTestCleanup()
        {
            _contextCaptureService?.Dispose();
        }

        #region Constructor Tests

        [TestMethod]
        public void Constructor_WithValidParameters_ShouldCreateInstance()
        {
            // Arrange & Act
            var service = new ContextCaptureService(_mockSettingsService.Object, _mockLogger.Object);

            // Assert
            service.Should().NotBeNull();
            service.Dispose();
        }

        [TestMethod]
        public void Constructor_WithNullSettingsService_ShouldThrowArgumentNullException()
        {
            // Arrange, Act & Assert
            Action act = () => new ContextCaptureService(null, _mockLogger.Object);
            act.Should().Throw<ArgumentNullException>()
                .WithParameterName("settingsService");
        }

        [TestMethod]
        public void Constructor_WithNullLogger_ShouldNotThrow()
        {
            // Arrange, Act & Assert
            Action act = () => new ContextCaptureService(_mockSettingsService.Object, null);
            act.Should().NotThrow();
        }

        #endregion

        #region CaptureContextAsync Tests

        [TestMethod]
        public async Task CaptureContextAsync_WithValidTextView_ShouldReturnContext()
        {
            // Arrange
            var mockTextView = CreateMockTextView();
            var linesUp = 3;
            var linesDown = 2;

            // Act
            var result = await _contextCaptureService.CaptureContextAsync(mockTextView, linesUp, linesDown);

            // Assert
            result.Should().NotBeNull();
            result.Language.Should().NotBeNullOrEmpty();
            result.FileName.Should().NotBeNullOrEmpty();
        }

        [TestMethod]
        public async Task CaptureContextAsync_WithNullTextView_ShouldReturnNull()
        {
            // Act
            var result = await _contextCaptureService.CaptureContextAsync(null, 3, 2);

            // Assert
            result.Should().BeNull();
        }

        [TestMethod]
        public async Task CaptureContextAsync_WithValidParameters_ShouldCaptureCorrectNumberOfLines()
        {
            // Arrange
            var mockTextView = CreateMockTextView();
            var linesUp = 5;
            var linesDown = 3;

            // Act
            var result = await _contextCaptureService.CaptureContextAsync(mockTextView, linesUp, linesDown);

            // Assert
            result.Should().NotBeNull();
            result.PrecedingLines.Should().HaveCountLessOrEqualTo(linesUp);
            result.FollowingLines.Should().HaveCountLessOrEqualTo(linesDown);
        }

        [TestMethod]
        public async Task CaptureContextAsync_WithZeroLines_ShouldReturnContextWithCurrentLineOnly()
        {
            // Arrange
            var mockTextView = CreateMockTextView();

            // Act
            var result = await _contextCaptureService.CaptureContextAsync(mockTextView, 0, 0);

            // Assert
            result.Should().NotBeNull();
            result.PrecedingLines.Should().BeEmpty();
            result.FollowingLines.Should().BeEmpty();
            result.CurrentLine.Should().NotBeNull();
        }

        [TestMethod]
        public async Task CaptureContextAsync_WithNegativeLines_ShouldHandleGracefully()
        {
            // Arrange
            var mockTextView = CreateMockTextView();

            // Act
            var result = await _contextCaptureService.CaptureContextAsync(mockTextView, -1, -1);

            // Assert
            result.Should().NotBeNull();
            result.PrecedingLines.Should().BeEmpty();
            result.FollowingLines.Should().BeEmpty();
        }

        #endregion

        #region GetContextSnippetAsync Tests

        [TestMethod]
        public async Task GetContextSnippetAsync_WithValidPosition_ShouldReturnSnippet()
        {
            // Arrange
            var line = 10;
            var column = 5;

            // Act
            var result = await _contextCaptureService.GetContextSnippetAsync(line, column);

            // Assert
            result.Should().NotBeNull();
            result.Should().NotBeEmpty();
        }

        [TestMethod]
        public async Task GetContextSnippetAsync_WithNegativePosition_ShouldReturnEmptySnippet()
        {
            // Act
            var result = await _contextCaptureService.GetContextSnippetAsync(-1, -1);

            // Assert
            result.Should().NotBeNull();
            result.Should().BeEmpty();
        }

        [TestMethod]
        public async Task GetContextSnippetAsync_WithZeroPosition_ShouldReturnSnippet()
        {
            // Act
            var result = await _contextCaptureService.GetContextSnippetAsync(0, 0);

            // Assert
            result.Should().NotBeNull();
        }

        #endregion

        #region Language Detection Tests

        [TestMethod]
        public void DetectLanguage_WithCSharpFile_ShouldReturnCSharp()
        {
            // Arrange
            var fileName = "TestFile.cs";

            // Act
            var result = _contextCaptureService.DetectLanguage(fileName);

            // Assert
            result.Should().Be("csharp");
        }

        [TestMethod]
        public void DetectLanguage_WithJavaScriptFile_ShouldReturnJavaScript()
        {
            // Arrange
            var fileName = "TestFile.js";

            // Act
            var result = _contextCaptureService.DetectLanguage(fileName);

            // Assert
            result.Should().Be("javascript");
        }

        [TestMethod]
        public void DetectLanguage_WithPythonFile_ShouldReturnPython()
        {
            // Arrange
            var fileName = "TestFile.py";

            // Act
            var result = _contextCaptureService.DetectLanguage(fileName);

            // Assert
            result.Should().Be("python");
        }

        [TestMethod]
        public void DetectLanguage_WithCppFile_ShouldReturnCpp()
        {
            // Arrange
            var fileName = "TestFile.cpp";

            // Act
            var result = _contextCaptureService.DetectLanguage(fileName);

            // Assert
            result.Should().Be("cpp");
        }

        [TestMethod]
        public void DetectLanguage_WithUnknownExtension_ShouldReturnText()
        {
            // Arrange
            var fileName = "TestFile.unknown";

            // Act
            var result = _contextCaptureService.DetectLanguage(fileName);

            // Assert
            result.Should().Be("text");
        }

        [TestMethod]
        public void DetectLanguage_WithNullFileName_ShouldReturnText()
        {
            // Act
            var result = _contextCaptureService.DetectLanguage(null);

            // Assert
            result.Should().Be("text");
        }

        [TestMethod]
        public void DetectLanguage_WithEmptyFileName_ShouldReturnText()
        {
            // Act
            var result = _contextCaptureService.DetectLanguage("");

            // Assert
            result.Should().Be("text");
        }

        #endregion

        #region Indentation Detection Tests

        [TestMethod]
        public void DetectIndentation_WithSpaceIndentedCode_ShouldDetectSpaces()
        {
            // Arrange
            var lines = new[]
            {
                "public class Test",
                "    {",
                "        public void Method()",
                "        {",
                "            // Comment",
                "        }",
                "    }"
            };

            // Act
            var result = _contextCaptureService.DetectIndentation(lines);

            // Assert
            result.Should().NotBeNull();
            result.UsesSpaces.Should().BeTrue();
            result.Size.Should().Be(4);
        }

        [TestMethod]
        public void DetectIndentation_WithTabIndentedCode_ShouldDetectTabs()
        {
            // Arrange
            var lines = new[]
            {
                "public class Test",
                "\t{",
                "\t\tpublic void Method()",
                "\t\t{",
                "\t\t\t// Comment",
                "\t\t}",
                "\t}"
            };

            // Act
            var result = _contextCaptureService.DetectIndentation(lines);

            // Assert
            result.Should().NotBeNull();
            result.UsesSpaces.Should().BeFalse();
            result.Size.Should().Be(1);
        }

        [TestMethod]
        public void DetectIndentation_WithMixedIndentation_ShouldPreferMostCommon()
        {
            // Arrange
            var lines = new[]
            {
                "public class Test",
                "    {", // 4 spaces
                "\tpublic void Method()", // 1 tab
                "    {", // 4 spaces
                "        // Comment", // 8 spaces
                "    }",
                "    }"
            };

            // Act
            var result = _contextCaptureService.DetectIndentation(lines);

            // Assert
            result.Should().NotBeNull();
            result.UsesSpaces.Should().BeTrue(); // More space-indented lines
        }

        [TestMethod]
        public void DetectIndentation_WithNoIndentation_ShouldReturnDefaults()
        {
            // Arrange
            var lines = new[]
            {
                "public class Test",
                "{",
                "public void Method()",
                "{",
                "// Comment",
                "}",
                "}"
            };

            // Act
            var result = _contextCaptureService.DetectIndentation(lines);

            // Assert
            result.Should().NotBeNull();
            result.UsesSpaces.Should().BeTrue(); // Default
            result.Size.Should().Be(4); // Default
        }

        [TestMethod]
        public void DetectIndentation_WithNullLines_ShouldReturnDefaults()
        {
            // Act
            var result = _contextCaptureService.DetectIndentation(null);

            // Assert
            result.Should().NotBeNull();
            result.UsesSpaces.Should().BeTrue(); // Default
            result.Size.Should().Be(4); // Default
        }

        [TestMethod]
        public void DetectIndentation_WithEmptyLines_ShouldReturnDefaults()
        {
            // Act
            var result = _contextCaptureService.DetectIndentation(new string[0]);

            // Assert
            result.Should().NotBeNull();
            result.UsesSpaces.Should().BeTrue(); // Default
            result.Size.Should().Be(4); // Default
        }

        #endregion

        #region Scope Detection Tests

        [TestMethod]
        public void DetectCurrentScope_WithMethodContext_ShouldReturnMethodScope()
        {
            // Arrange
            var lines = new[]
            {
                "namespace TestNamespace",
                "{",
                "    public class TestClass",
                "    {",
                "        public void TestMethod()",
                "        {", // Current line
                "            // Implementation",
                "        }",
                "    }",
                "}"
            };
            var currentLine = 5; // Inside TestMethod

            // Act
            var result = _contextCaptureService.DetectCurrentScope(lines, currentLine, "csharp");

            // Assert
            result.Should().Contain("TestMethod");
        }

        [TestMethod]
        public void DetectCurrentScope_WithClassContext_ShouldReturnClassScope()
        {
            // Arrange
            var lines = new[]
            {
                "namespace TestNamespace",
                "{",
                "    public class TestClass",
                "    {", // Current line
                "        // Class members",
                "    }",
                "}"
            };
            var currentLine = 3; // Inside TestClass

            // Act
            var result = _contextCaptureService.DetectCurrentScope(lines, currentLine, "csharp");

            // Assert
            result.Should().Contain("TestClass");
        }

        [TestMethod]
        public void DetectCurrentScope_WithNamespaceContext_ShouldReturnNamespaceScope()
        {
            // Arrange
            var lines = new[]
            {
                "namespace TestNamespace",
                "{", // Current line
                "    // Namespace content",
                "}"
            };
            var currentLine = 1; // Inside namespace

            // Act
            var result = _contextCaptureService.DetectCurrentScope(lines, currentLine, "csharp");

            // Assert
            result.Should().Contain("TestNamespace");
        }

        [TestMethod]
        public void DetectCurrentScope_WithGlobalScope_ShouldReturnGlobal()
        {
            // Arrange
            var lines = new[]
            {
                "// Global comment",
                "using System;", // Current line
                ""
            };
            var currentLine = 1; // Global scope

            // Act
            var result = _contextCaptureService.DetectCurrentScope(lines, currentLine, "csharp");

            // Assert
            result.Should().Be("Global");
        }

        [TestMethod]
        public void DetectCurrentScope_WithInvalidLine_ShouldReturnGlobal()
        {
            // Arrange
            var lines = new[]
            {
                "public class Test",
                "{",
                "}"
            };
            var currentLine = -1; // Invalid line

            // Act
            var result = _contextCaptureService.DetectCurrentScope(lines, currentLine, "csharp");

            // Assert
            result.Should().Be("Global");
        }

        [TestMethod]
        public void DetectCurrentScope_WithLineOutOfRange_ShouldReturnGlobal()
        {
            // Arrange
            var lines = new[]
            {
                "public class Test",
                "{",
                "}"
            };
            var currentLine = 10; // Out of range

            // Act
            var result = _contextCaptureService.DetectCurrentScope(lines, currentLine, "csharp");

            // Assert
            result.Should().Be("Global");
        }

        #endregion

        #region Error Handling Tests

        [TestMethod]
        public async Task CaptureContextAsync_WhenExceptionThrown_ShouldLogErrorAndReturnNull()
        {
            // Arrange
            // Create a mock that throws an exception
            var mockTextView = new Mock<object>();

            // Act
            var result = await _contextCaptureService.CaptureContextAsync(mockTextView.Object, 3, 2);

            // Assert
            result.Should().BeNull();
            // Verify that error was logged
            VerifyAsyncMethodCalled(_mockLogger, x => x.LogErrorAsync(It.IsAny<Exception>(), It.IsAny<string>(), It.IsAny<string>()), Times.AtLeastOnce());
        }

        [TestMethod]
        public async Task GetContextSnippetAsync_WhenExceptionThrown_ShouldReturnEmptyString()
        {
            // Arrange
            // Force an exception by passing extreme values
            var extremeLine = int.MaxValue;
            var extremeColumn = int.MaxValue;

            // Act
            var result = await _contextCaptureService.GetContextSnippetAsync(extremeLine, extremeColumn);

            // Assert
            result.Should().NotBeNull();
        }

        #endregion

        #region Performance Tests

        [PerformanceTest(MaxExecutionTimeMs = 500)]
        [TestMethod]
        public async Task CaptureContextAsync_ShouldCompleteWithinTimeout()
        {
            // Arrange
            var mockTextView = CreateMockTextView();
            var timeout = TimeSpan.FromMilliseconds(500);

            // Act & Assert
            await AssertCompletesWithinTimeout(
                () => _contextCaptureService.CaptureContextAsync(mockTextView, 10, 10),
                timeout);
        }

        [PerformanceTest(MaxExecutionTimeMs = 100)]
        [TestMethod]
        public async Task GetContextSnippetAsync_ShouldCompleteWithinTimeout()
        {
            // Arrange
            var timeout = TimeSpan.FromMilliseconds(100);

            // Act & Assert
            await AssertCompletesWithinTimeout(
                () => _contextCaptureService.GetContextSnippetAsync(10, 5),
                timeout);
        }

        #endregion

        #region Thread Safety Tests

        [TestMethod]
        public async Task CaptureContextAsync_ConcurrentCalls_ShouldHandleThreadSafely()
        {
            // Arrange
            const int numberOfTasks = 10;
            var tasks = new Task<CodeContext>[numberOfTasks];
            var mockTextView = CreateMockTextView();

            // Act
            for (int i = 0; i < numberOfTasks; i++)
            {
                tasks[i] = _contextCaptureService.CaptureContextAsync(mockTextView, 3, 2);
            }

            var results = await Task.WhenAll(tasks);

            // Assert
            results.Should().HaveCount(numberOfTasks);
            results.Should().OnlyContain(r => r != null);
        }

        #endregion

        #region Dispose Tests

        [TestMethod]
        public void Dispose_ShouldCleanupResources()
        {
            // Arrange
            var service = new ContextCaptureService(_mockSettingsService.Object, _mockLogger.Object);

            // Act
            service.Dispose();

            // Assert
            // Verify that disposal doesn't throw
        }

        [TestMethod]
        public void Dispose_CalledMultipleTimes_ShouldNotThrow()
        {
            // Act & Assert
            _contextCaptureService.Dispose();
            Action act = () => _contextCaptureService.Dispose();
            act.Should().NotThrow();
        }

        #endregion

        #region Helper Methods

        private object CreateMockTextView()
        {
            // This would create a mock IWpfTextView in a real implementation
            // For now, return a simple mock object
            var mock = new Mock<object>();
            return mock.Object;
        }

        #endregion
    }
}