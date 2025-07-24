# Ollama Assistant Configuration Guide

## Overview

This guide covers all configuration options available in the Ollama Assistant extension, from basic setup to advanced customization. Learn how to optimize the extension for your specific development workflow and environment.

## Configuration Locations

### Global Settings
- **Location**: Visual Studio → **Tools** → **Options** → **Ollama Assistant**
- **Scope**: Applies to all projects and solutions
- **Storage**: Windows Registry under Visual Studio settings

### Project-Specific Settings
- **Location**: Project root directory → `.ollama-project.json`
- **Scope**: Applies only to the specific project
- **Version Control**: Can be committed to share team settings

### Team Settings
- **Location**: Project root directory → `.ollama-team.json`
- **Scope**: Shared team configuration
- **Purpose**: Standardize settings across development team

### User Configuration File
- **Location**: Project/Solution root → `.ollamarc`
- **Format**: Key-value pairs (similar to `.gitconfig`)
- **Priority**: Highest priority for local overrides

## Settings Hierarchy

Settings are applied in the following order (later overrides earlier):

1. **Global Settings** (lowest priority)
2. **Team Settings**
3. **Project Settings**
4. **User Configuration (.ollamarc)** (highest priority)

## Basic Configuration

### Server Connection

```json
{
  "serverConnection": {
    "url": "http://localhost:11434",
    "useHttps": false,
    "timeout": 30000,
    "retryAttempts": 3,
    "healthCheckInterval": 60000
  }
}
```

**Options:**
- `url`: Ollama server endpoint URL
- `useHttps`: Enable HTTPS (requires valid certificate)
- `timeout`: Request timeout in milliseconds
- `retryAttempts`: Number of retry attempts on failure
- `healthCheckInterval`: Health check frequency in milliseconds

### Model Configuration

```json
{
  "models": {
    "defaultModel": "codellama",
    "fallbackModel": "mistral",
    "autoModelSelection": true,
    "modelPriorities": {
      "codellama": 10,
      "deepseek-coder": 9,
      "mistral": 8,
      "llama2": 7
    }
  }
}
```

**Model Selection Rules:**
- `defaultModel`: Primary model for code completion
- `fallbackModel`: Used when primary model fails
- `autoModelSelection`: Enable intelligent model selection
- `modelPriorities`: Ranking for auto-selection (1-10 scale)

### Core Features

```json
{
  "features": {
    "codeCompletion": {
      "enabled": true,
      "debounceDelay": 300,
      "maxSuggestions": 5,
      "showInIntelliSense": true
    },
    "jumpRecommendations": {
      "enabled": true,
      "keyBinding": "Tab",
      "showNotifications": true,
      "notificationTimeout": 3000
    },
    "contextTracking": {
      "enabled": true,
      "historyDepth": 10,
      "crossFileTracking": true
    }
  }
}
```

## Context Configuration

### Code Context Settings

```json
{
  "context": {
    "surroundingLines": {
      "before": 3,
      "after": 2,
      "adaptive": true,
      "maxLines": 20
    },
    "cursorHistory": {
      "enabled": true,
      "maxEntries": 10,
      "retentionDays": 7,
      "crossFileTracking": true
    },
    "projectContext": {
      "includeRelatedFiles": true,
      "maxRelatedFiles": 5,
      "relationshipDepth": 2
    }
  }
}
```

**Context Options:**
- `adaptive`: Dynamically adjust context based on code complexity
- `maxLines`: Maximum total lines to include in context
- `retentionDays`: How long to keep cursor history
- `relationshipDepth`: How deep to traverse file relationships

### Language-Specific Settings

```json
{
  "languageSettings": {
    "csharp": {
      "contextLines": { "before": 5, "after": 3 },
      "includeUsings": true,
      "includeNamespace": true,
      "semanticAnalysis": true
    },
    "javascript": {
      "contextLines": { "before": 3, "after": 2 },
      "includeImports": true,
      "includeRequires": true,
      "nodeModulesDepth": 1
    },
    "python": {
      "contextLines": { "before": 4, "after": 2 },
      "includeImports": true,
      "includeDocstrings": true,
      "virtualEnvSupport": true
    }
  }
}
```

## Advanced Model Configuration

### Model-Specific Parameters

```json
{
  "modelConfigurations": {
    "codellama": {
      "parameters": {
        "temperature": 0.1,
        "top_p": 0.9,
        "top_k": 40,
        "repeat_penalty": 1.1,
        "num_predict": 256,
        "stop": ["\n\n", "```"]
      },
      "optimization": "code_generation",
      "contextLength": 4096,
      "confidenceThreshold": 0.7,
      "timeoutSeconds": 30
    },
    "mistral": {
      "parameters": {
        "temperature": 0.3,
        "top_p": 0.95,
        "top_k": 50,
        "repeat_penalty": 1.05,
        "num_predict": 512
      },
      "optimization": "general_purpose",
      "contextLength": 8192,
      "confidenceThreshold": 0.6,
      "timeoutSeconds": 45
    }
  }
}
```

### Custom Prompt Templates

```json
{
  "promptTemplates": {
    "codeCompletion": {
      "template": "Complete the following {language} code:\n\nContext:\n{context}\n\nCursor position: {cursor}\n\nCompletion:",
      "variables": ["language", "context", "cursor"]
    },
    "codeExplanation": {
      "template": "Explain this {language} code:\n\n{code}\n\nExplanation:",
      "variables": ["language", "code"]
    },
    "debugging": {
      "template": "Help debug this {language} code:\n\n{code}\n\nError: {error}\n\nSolution:",
      "variables": ["language", "code", "error"]
    }
  }
}
```

## Performance Configuration

### Performance Tuning

```json
{
  "performance": {
    "caching": {
      "enabled": true,
      "ttlMinutes": 5,
      "maxCacheSize": 100,
      "enablePersistentCache": false
    },
    "processing": {
      "maxConcurrentRequests": 2,
      "backgroundProcessing": true,
      "prioritizeUserRequests": true,
      "throttleOnHighLoad": true
    },
    "memory": {
      "maxMemoryUsageMB": 200,
      "enableMemoryPressureHandling": true,
      "garbageCollectionMode": "interactive"
    }
  }
}
```

### Network Optimization

```json
{
  "network": {
    "connectionPooling": {
      "enabled": true,
      "maxConnections": 5,
      "connectionTimeout": 30000,
      "keepAliveTimeout": 120000
    },
    "compression": {
      "enabled": true,
      "compressionThreshold": 1024
    },
    "retry": {
      "exponentialBackoff": true,
      "maxRetryDelay": 10000,
      "retryOnTimeout": true
    }
  }
}
```

## User Experience Configuration

### IntelliSense Integration

```json
{
  "intelliSense": {
    "integration": {
      "showOllamaIcon": true,
      "priorityBoost": 5,
      "filterDuplicates": true,
      "respectUserPreferences": true
    },
    "presentation": {
      "showConfidence": true,
      "showModelSource": false,
      "customSortOrder": true,
      "groupByType": false
    }
  }
}
```

### Notifications and Feedback

```json
{
  "userInterface": {
    "notifications": {
      "showProgressIndicators": true,
      "showStatusBarUpdates": true,
      "errorNotificationLevel": "important",
      "successNotificationDuration": 2000
    },
    "accessibility": {
      "screenReaderSupport": true,
      "highContrastSupport": true,
      "keyboardNavigation": true,
      "focusManagement": true
    }
  }
}
```

## Security Configuration

### Authentication and Authorization

```json
{
  "security": {
    "authentication": {
      "apiKeyRequired": false,
      "apiKey": "",
      "encryptApiKey": true,
      "tokenRefreshInterval": 3600
    },
    "communication": {
      "enforceHttps": false,
      "validateCertificates": true,
      "allowSelfSignedCerts": false,
      "enableRequestSigning": false
    },
    "dataPrivacy": {
      "logSensitiveData": false,
      "anonymizeErrors": true,
      "enableTelemetry": true,
      "dataRetentionDays": 30
    }
  }
}
```

### Input Validation

```json
{
  "validation": {
    "inputSanitization": {
      "enabled": true,
      "maxContextSize": 10000,
      "forbiddenPatterns": ["eval(", "exec(", "subprocess"],
      "pathTraversalProtection": true
    },
    "responseValidation": {
      "syntaxValidation": true,
      "malwareScanning": false,
      "contentFiltering": true
    }
  }
}
```

## Learning and Adaptation

### Machine Learning Settings

```json
{
  "learning": {
    "userFeedback": {
      "trackAcceptanceRates": true,
      "learnFromRejections": true,
      "adaptToUserStyle": true,
      "privacyMode": false
    },
    "patternRecognition": {
      "enablePatternLearning": true,
      "minPatternOccurrences": 3,
      "patternConfidenceThreshold": 0.7,
      "maxStoredPatterns": 1000
    },
    "personalization": {
      "adaptToNamingConventions": true,
      "learnFormattingPreferences": true,
      "rememberModelPreferences": true,
      "trackUsagePatterns": true
    }
  }
}
```

## Project Configuration Examples

### .ollamarc File Example

```ini
# Ollama Assistant Configuration
# Project: MyApplication

# Server Settings
server=http://localhost:11434
model=codellama

# Feature Settings
code_completion=true
jump_recommendations=true

# Context Settings
context_lines_up=3
context_lines_down=2
cursor_history_depth=5

# Performance Settings
max_concurrent_requests=1
enable_caching=true

# Custom Settings
debug_mode=false
log_level=info
```

### Team Configuration (.ollama-team.json)

```json
{
  "teamName": "Development Team Alpha",
  "sharedSettings": {
    "serverUrl": "https://ollama.company.com:11434",
    "defaultModel": "codellama",
    "allowedModels": ["codellama", "mistral", "deepseek-coder"],
    "enforcedSettings": {
      "security.dataPrivacy.enableTelemetry": false,
      "performance.maxConcurrentRequests": 1
    }
  },
  "policies": {
    "requireHttps": true,
    "forbidCustomServers": true,
    "maxContextSize": 8192
  }
}
```

### Project-Specific Configuration

```json
{
  "projectType": "web-application",
  "language": "csharp",
  "framework": "aspnet-core",
  "customSettings": {
    "context": {
      "includeConfigFiles": true,
      "includeViewFiles": true,
      "includeStaticFiles": false
    },
    "models": {
      "preferredModel": "codellama",
      "specializationHints": {
        "controllers": "deepseek-coder",
        "views": "mistral",
        "models": "codellama"
      }
    }
  }
}
```

## Configuration Validation

### Validation Rules

The extension automatically validates configuration files:

1. **Schema Validation**: JSON schema compliance
2. **Value Range Checks**: Numeric values within valid ranges
3. **Dependency Checks**: Related settings consistency
4. **Security Validation**: Potential security issues
5. **Performance Impact**: Settings that may affect performance

### Common Validation Errors

```json
{
  "validationErrors": [
    {
      "path": "performance.maxConcurrentRequests",
      "error": "Value must be between 1 and 10",
      "severity": "error"
    },
    {
      "path": "context.surroundingLines.before",
      "error": "Recommended maximum is 20 for performance",
      "severity": "warning"
    }
  ]
}
```

## Configuration Migration

### Upgrading from Previous Versions

The extension automatically migrates settings from previous versions:

1. **Backup Creation**: Automatic backup of existing settings
2. **Schema Updates**: Convert to new configuration format
3. **Default Values**: Apply new defaults for added settings
4. **Validation**: Ensure migrated settings are valid

### Migration Log Example

```
2024-01-15 10:30:00 - Starting configuration migration from v1.0 to v2.0
2024-01-15 10:30:01 - Backed up existing settings to backup_20240115_103000.json
2024-01-15 10:30:01 - Migrated serverUrl to serverConnection.url
2024-01-15 10:30:01 - Added new security.authentication section with defaults
2024-01-15 10:30:02 - Migration completed successfully
```

## Configuration Best Practices

### Performance Optimization

1. **Balance Context Size**
   - Start with default values (3 lines up, 2 lines down)
   - Increase gradually based on code complexity
   - Monitor performance impact

2. **Model Selection**
   - Use `codellama` for primary coding tasks
   - Use `mistral` for documentation and comments
   - Enable auto-selection for optimal performance

3. **Caching Strategy**
   - Enable caching for repeated operations
   - Set appropriate TTL based on code change frequency
   - Monitor cache hit rates

### Security Considerations

1. **Network Security**
   - Use HTTPS in production environments
   - Validate certificates properly
   - Consider VPN/firewall configurations

2. **Data Privacy**
   - Review telemetry settings
   - Consider privacy mode for sensitive projects
   - Understand data retention policies

3. **Access Control**
   - Use API keys when available
   - Restrict server access appropriately
   - Monitor usage logs

### Team Collaboration

1. **Shared Configuration**
   - Use team settings for common standards
   - Version control appropriate config files
   - Document team-specific customizations

2. **Individual Preferences**
   - Allow personal overrides via `.ollamarc`
   - Respect individual workflow preferences
   - Provide training on configuration options

## Troubleshooting Configuration Issues

### Common Problems

1. **Settings Not Applied**
   - Check configuration hierarchy
   - Verify JSON syntax
   - Restart Visual Studio

2. **Performance Issues**
   - Review context size settings
   - Check concurrent request limits
   - Monitor memory usage

3. **Connection Problems**
   - Validate server URL
   - Check network connectivity
   - Verify model availability

### Diagnostic Commands

```bash
# Test configuration
ollama-assistant --validate-config

# Show effective configuration
ollama-assistant --show-config

# Reset to defaults
ollama-assistant --reset-config
```

---

**Configuration Complete!** ⚙️

Your Ollama Assistant is now configured for optimal performance and seamless integration with your development workflow.

*For troubleshooting help, see `/docs/user/troubleshooting-guide.md`*