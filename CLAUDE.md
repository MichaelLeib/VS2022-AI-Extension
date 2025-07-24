# VS2022 Ollama Assistant Extension

## Project Overview

This is a Visual Studio 2022 extension that provides intelligent code completion and navigation assistance using Ollama AI models. The extension analyzes the user's typing context and cursor position to provide predictive text suggestions and smart cursor jump recommendations.

## Key Features

### 1. AI-Powered Code Completion
- Captures configurable surrounding lines of code (e.g., 2 lines down, 3 lines up)
- Includes cursor position history from related files
- Sends enhanced context to Ollama for intelligent predictions
- Displays suggestions using VS IntelliSense UI patterns

### 2. Smart Cursor Navigation
- Analyzes code structure to recommend next cursor position
- Shows unobtrusive notifications for jump recommendations
- Tab key (configurable) executes the jump
- Context-aware based on recent navigation patterns

### 3. Cross-File Context Tracking
- Maintains history of cursor positions across files
- Tracks related changes (e.g., change in file A, then related change in file B)
- Configurable memory depth (e.g., last 3 cursor positions)
- Intelligently includes relevant history in AI context

### 4. Comprehensive Configuration
- Ollama server endpoint
- Surrounding lines configuration (separate up/down)
- Jump key customization
- Cursor history memory depth
- Feature toggles for predictions and jumps

## Technical Stack

- **Framework**: .NET Framework 4.7.2
- **IDE**: Visual Studio 2022
- **AI Backend**: Ollama (local instance)
- **Architecture**: Asynchronous, modular design
- **Testing**: MSTest with Moq

## Architecture Components

1. **Editor Integration Layer**: VS editor events and text manipulation
2. **Context Analysis Layer**: Configurable code context capture
3. **Cursor History Layer**: Cross-file position tracking
4. **AI Communication Layer**: Ollama API integration
5. **Suggestion Engine**: Context-aware AI response processing
6. **UI Presentation Layer**: IntelliSense and notifications
7. **Configuration Layer**: Settings management
8. **Infrastructure Layer**: Logging and error handling

## Key Implementation Notes

### Performance Considerations
- Typing debounce to reduce API calls
- Caching for suggestions and context
- Efficient circular buffer for cursor history
- Connection pooling for Ollama communication
- Intelligent context pruning based on relevance

### Error Handling
- Graceful degradation when Ollama is unavailable
- Custom exception hierarchy
- Structured logging with correlation IDs
- No crashes to Visual Studio on errors

### Security
- Input validation before sending to Ollama
- No sensitive data in logs or transmissions
- HTTPS when available
- Resource limits and timeouts

## Development Guidelines

### Code Style
- Follow existing VS extension patterns
- Use async/await throughout
- Implement proper cancellation token usage
- Use Visual Studio's JoinableTaskFactory for UI marshaling

### Testing Requirements
- Unit tests for all services
- Integration tests for VS and Ollama
- Performance benchmarks
- Manual UX testing

### Key Interfaces

```csharp
// Settings
ISettingsService - Manages all user configuration
ICursorHistoryService - Tracks cursor position history
IContextCaptureService - Captures code context with configurable lines
IOllamaService - Communicates with Ollama including history context

// UI
IIntelliSenseIntegration - Shows code suggestions
IJumpNotificationService - Shows navigation recommendations
```

## Common Tasks

### Running Tests
```bash
# Unit tests
dotnet test OllamaAssistant.Tests

# Integration tests (requires local Ollama)
dotnet test OllamaAssistant.IntegrationTests
```

### Debugging the Extension
1. Set the VSIX project as startup project
2. F5 launches experimental VS instance
3. Extension loads automatically in experimental instance

### Configuration File Location
User settings stored in VS settings store, accessible via:
Tools → Options → Ollama Assistant

## Important Considerations

1. **Context Quality**: The quality of suggestions depends heavily on:
   - Appropriate surrounding lines configuration
   - Relevant cursor history
   - Ollama model selection

2. **Performance**: Monitor and optimize:
   - API call frequency (debouncing)
   - Context size sent to Ollama
   - History memory usage

3. **User Experience**: Maintain unobtrusiveness:
   - Suggestions should enhance, not interrupt
   - Notifications must be subtle
   - Performance must not impact typing

## Future Enhancements

- Support for multiple Ollama models
- Project-specific context awareness
- Team sharing of optimal configurations
- Integration with VS refactoring tools
- Support for other AI backends beyond Ollama