# Service Registration Fixes Required

**Target File:** `OllamaAssistantPackage.cs` - `RegisterServicesAsync()` method  
**Current Status:** 6 services implemented but not registered

## Missing Service Registrations

Add the following registrations to the `RegisterServicesAsync()` method:

```csharp
// Add these registrations to OllamaAssistantPackage.cs

// Code refactoring service
container.RegisterSingleton<ICodeRefactoringService, CodeRefactoringService>();

// Settings validation service  
container.RegisterSingleton<ISettingsValidationService, SettingsValidationService>();

// Connection management
container.RegisterSingleton<IOllamaConnectionManager, OllamaConnectionManager>();

// Visual Studio integration resilience
container.RegisterSingleton<IVSIntegrationResilienceService, VSIntegrationResilienceService>();

// Secure communication
container.RegisterSingleton<ISecureCommunicationService, SecureCommunicationService>();

// Rate limiting
container.RegisterSingleton<IRateLimitingService, RateLimitingService>();
```

## Dependencies for New Registrations

### CodeRefactoringService Dependencies
This service requires 2 interfaces that don't exist:
- `IAdvancedContextAnalysisService` - **CREATE INTERFACE**
- `ILoggingService` - **RESOLVE CONFLICT with ILogger**

**Fix:** Either create these interfaces or modify the constructor to use existing services.

### Service Dependency Chain
Ensure these services are registered in the correct order to resolve dependencies:

1. **Base Services First:**
   - `ILogger` (already registered)
   - `ISettingsService` (already registered)

2. **Connection Services:**
   - `ISecureCommunicationService`
   - `IRateLimitingService`
   - `IOllamaConnectionManager`

3. **Advanced Services:**
   - `ISettingsValidationService`
   - `IVSIntegrationResilienceService` 
   - `ICodeRefactoringService` (after creating missing interfaces)

## Interface Creation Required

### IAdvancedContextAnalysisService
**Location:** Create `Services/Interfaces/IAdvancedContextAnalysisService.cs`

```csharp
using System.Threading.Tasks;
using OllamaAssistant.Models;

namespace OllamaAssistant.Services.Interfaces
{
    public interface IAdvancedContextAnalysisService
    {
        Task<AdvancedContext> AnalyzeContextAsync(string code, string filePath);
        Task<SyntaxTree> ParseCodeAsync(string code, string language);
        Task<SemanticModel> GetSemanticModelAsync(SyntaxTree syntaxTree);
        // Add other methods as needed based on usage
    }
}
```

### ILoggingService vs ILogger Resolution
**Current Issue:** Services use both `ILogger` and `ILoggingService`

**Recommended Fix:** Standardize on `ILogger` (already implemented and registered)

Update `CodeRefactoringService` constructor:
```csharp
// Change from:
public CodeRefactoringService(
    IOllamaService ollamaService,
    IAdvancedContextAnalysisService contextAnalysisService,
    ILoggingService loggingService,  // ❌ Change this
    ISettingsService settingsService)

// Change to:
public CodeRefactoringService(
    IOllamaService ollamaService,
    IAdvancedContextAnalysisService contextAnalysisService,
    ILogger logger,  // ✅ Use existing interface
    ISettingsService settingsService)
```

## Implementation Notes

### OllamaHttpClient Interface Fix
**Critical:** `OllamaHttpClient` is registered as `IOllamaHttpClient` but doesn't implement the interface.

**Fix in `OllamaHttpClient.cs`:**
```csharp
// Change from:
public class OllamaHttpClient : IDisposable

// Change to:
public class OllamaHttpClient : IOllamaHttpClient, IDisposable
```

### Registration Order in RegisterServicesAsync()

```csharp
private async Task RegisterServicesAsync(ServiceContainer container, CancellationToken cancellationToken)
{
    // ... existing registrations ...

    // Add new registrations in dependency order:
    
    // Security and communication (no dependencies)
    container.RegisterSingleton<ISecureCommunicationService, SecureCommunicationService>();
    container.RegisterSingleton<IRateLimitingService, RateLimitingService>();
    
    // Connection management (depends on security services)
    container.RegisterSingleton<IOllamaConnectionManager, OllamaConnectionManager>();
    
    // Settings validation (minimal dependencies)
    container.RegisterSingleton<ISettingsValidationService, SettingsValidationService>();
    
    // VS integration resilience
    container.RegisterSingleton<IVSIntegrationResilienceService, VSIntegrationResilienceService>();
    
    // Code refactoring (depends on many services - register last)
    // NOTE: Only register after creating IAdvancedContextAnalysisService
    // container.RegisterSingleton<ICodeRefactoringService, CodeRefactoringService>();
}
```

## Testing the Fixes

After implementing these changes:

1. **Build the solution** - should compile without errors
2. **Run unit tests** - verify service resolution works
3. **Test service injection** - ensure all constructors can be satisfied
4. **Check for circular dependencies** - monitor service creation order

## Rollout Strategy

1. **Phase 1:** Register services with no missing dependencies
2. **Phase 2:** Create missing interfaces 
3. **Phase 3:** Register remaining services
4. **Phase 4:** Test and validate all service resolutions work