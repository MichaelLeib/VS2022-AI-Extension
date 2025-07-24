# Interface Method Signature Mismatches

**Impact:** Runtime errors, interface contract violations, compilation failures

## Critical Method Signature Issues

### 1. IOllamaHttpClient - SendStreamingCompletionAsync

**Interface Definition:** `Services/Interfaces/IOllamaHttpClient.cs`
```csharp
Task<IAsyncEnumerable<OllamaStreamResponse>> SendStreamingCompletionAsync(
    OllamaCompletionRequest request, 
    CancellationToken cancellationToken = default);
```

**Usage Pattern:** `Services/Implementation/OllamaService.cs` (line ~450)
```csharp
// Called as:
await foreach (var response in _httpClient.SendStreamingCompletionAsync(request, cancellationToken))
{
    // Process response
}
```

**Issue:** Interface returns `Task<IAsyncEnumerable<T>>` but used as `IAsyncEnumerable<T>`

**Fix Required:** Change interface to return `IAsyncEnumerable<OllamaStreamResponse>` directly:
```csharp
// Change from:
Task<IAsyncEnumerable<OllamaStreamResponse>> SendStreamingCompletionAsync(...)

// Change to:
IAsyncEnumerable<OllamaStreamResponse> SendStreamingCompletionAsync(...)
```

### 2. ICursorHistoryService - Missing Async Methods

**Interface Definition:** `Services/Interfaces/ICursorHistoryService.cs`
```csharp
// Current interface only has synchronous methods:
void AddPosition(CursorHistoryEntry entry);
List<CursorHistoryEntry> GetRecentPositions(int count);
```

**Usage Pattern:** Multiple files call async versions:
```csharp
// Called in various services:
await _cursorHistoryService.AddPositionAsync(entry);
var positions = await _cursorHistoryService.GetRecentPositionsAsync(count);
```

**Fix Required:** Add async methods to interface:
```csharp
public interface ICursorHistoryService
{
    // Existing synchronous methods
    void AddPosition(CursorHistoryEntry entry);
    List<CursorHistoryEntry> GetRecentPositions(int count);
    
    // Add missing async methods
    Task AddPositionAsync(CursorHistoryEntry entry);
    Task<List<CursorHistoryEntry>> GetRecentPositionsAsync(int count);
}
```

### 3. IContextCaptureService - Synchronous Method Calls

**Interface Definition:** `Services/Interfaces/IContextCaptureService.cs`
```csharp
// Interface only defines async methods:
Task<CodeContext> CaptureCurrentContextAsync(CaptureOptions options);
```

**Usage Pattern:** Some code calls synchronous version:
```csharp
// Called synchronously in some places:
var context = _contextCaptureService.CaptureCurrentContext(options);
```

**Fix Required:** Add synchronous method to interface:
```csharp
public interface IContextCaptureService
{
    // Existing async method
    Task<CodeContext> CaptureCurrentContextAsync(CaptureOptions options);
    
    // Add missing synchronous method
    CodeContext CaptureCurrentContext(CaptureOptions options);
}
```

## Method Implementation Missing Issues

### 4. IOllamaHttpClient - Missing Methods in Interface

**Implementation Class:** `OllamaHttpClient.cs` has these public methods:
```csharp
public async Task<HttpResponseMessage> PostAsync(string endpoint, StringContent content)
public async Task<HttpResponseMessage> GetAsync(string endpoint)  
public async Task<bool> TestConnectionAsync()
public async Task<string> GetHealthStatusAsync()
public void SetTimeout(TimeSpan timeout)
```

**Interface:** `IOllamaHttpClient.cs` is missing several methods:
```csharp
// Interface only defines:
Task<IAsyncEnumerable<OllamaStreamResponse>> SendStreamingCompletionAsync(...)
// Missing: PostAsync, GetAsync, TestConnectionAsync, GetHealthStatusAsync, SetTimeout
```

**Fix Required:** Update interface to include all public methods:
```csharp
public interface IOllamaHttpClient : IDisposable
{
    // Streaming completion
    IAsyncEnumerable<OllamaStreamResponse> SendStreamingCompletionAsync(
        OllamaCompletionRequest request, 
        CancellationToken cancellationToken = default);
    
    // HTTP methods
    Task<HttpResponseMessage> PostAsync(string endpoint, StringContent content);
    Task<HttpResponseMessage> GetAsync(string endpoint);
    
    // Connection testing
    Task<bool> TestConnectionAsync();
    Task<string> GetHealthStatusAsync();
    
    // Configuration
    void SetTimeout(TimeSpan timeout);
}
```

### 5. ISuggestionEngine - Method Signature Inconsistencies  

**Interface Usage:** Code calls methods with different signatures than defined:
```csharp
// Called with 4 parameters:
var suggestions = await _suggestionEngine.GenerateSuggestionsAsync(
    prompt, context, history, cancellationToken);

// Called with 3 parameters:
var filtered = _suggestionEngine.FilterSuggestions(suggestions, context, criteria);
```

**Interface Definition:** Methods may have different parameter counts or types.

**Fix Required:** Verify and standardize all method signatures in `ISuggestionEngine`.

## Event Handler Signature Issues

### 6. Event Handler Mismatches

**EventArgs Classes:** Multiple definitions cause ambiguity:
```csharp
// Defined in ITextViewService.cs:
public class CaretPositionChangedEventArgs : EventArgs { }

// Also defined in ExtensionOrchestrator.cs:
public class CaretPositionChangedEventArgs : EventArgs { }
```

**Usage:** Code may reference wrong EventArgs class depending on using statements.

**Fix Required:** 
1. Remove duplicate EventArgs classes
2. Consolidate into single definition in `Models/` directory
3. Update all references to use single definition

## Property Access Issues

### 7. Interface Properties Not Defined

**Usage Pattern:** Code accesses properties on interfaces that aren't defined:
```csharp
// Property access on interface:
if (_settingsService.IsEnabled) { ... }
var endpoint = _settingsService.OllamaEndpoint;
```

**Interface Definition:** `ISettingsService` may be missing some properties.

**Fix Required:** Add all accessed properties to interface definitions.

## Quick Fix Checklist

### Immediate Fixes (High Priority):
- [ ] Fix `IOllamaHttpClient.SendStreamingCompletionAsync` return type
- [ ] Add missing methods to `IOllamaHttpClient` interface  
- [ ] Add async methods to `ICursorHistoryService`
- [ ] Add synchronous methods to `IContextCaptureService`

### Medium Priority:
- [ ] Consolidate duplicate EventArgs classes
- [ ] Verify all `ISuggestionEngine` method signatures
- [ ] Add missing properties to service interfaces

### Testing:
- [ ] Update unit tests to match new interface signatures
- [ ] Verify all method calls compile without errors
- [ ] Test actual service functionality works as expected

## Implementation Order

1. **Fix streaming method signature** - Most critical for compilation
2. **Update `IOllamaHttpClient`** - Required for service registration
3. **Add missing async/sync methods** - Required for runtime functionality  
4. **Consolidate EventArgs** - Clean up duplicate definitions
5. **Verify all other interfaces** - Ensure completeness