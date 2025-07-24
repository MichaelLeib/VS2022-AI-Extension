# Ollama Assistant Troubleshooting Guide

## Overview

This comprehensive troubleshooting guide helps you resolve common issues with the Ollama Assistant VS2022 extension. Issues are organized by category with step-by-step solutions and diagnostic procedures.

## Quick Diagnosis

### Health Check Steps

1. **Extension Status**: Go to **View** → **Other Windows** → **Ollama Assistant Status**
2. **Server Connection**: Test connection in **Tools** → **Options** → **Ollama Assistant**
3. **Model Availability**: Verify models are loaded: `ollama list`
4. **Visual Studio Logs**: Check **View** → **Output** → **Ollama Assistant**

### Common Symptoms Quick Reference

| Symptom | Likely Cause | Quick Fix |
|---------|--------------|-----------|
| No suggestions appear | Server connection issue | Check Ollama service |
| Slow responses | Model loading/network | Restart Ollama service |
| Extension not visible | Installation issue | Reinstall extension |
| High memory usage | Context size too large | Reduce context settings |
| Crashes during use | Model compatibility | Switch to different model |

## Installation and Setup Issues

### Issue: Extension Not Loading

**Symptoms:**
- Extension not listed in **Extensions** → **Manage Extensions**
- No Ollama Assistant options in Tools menu
- Extension appears disabled

**Diagnostic Steps:**
1. Check installation status:
   ```
   Extensions → Manage Extensions → Installed → Search "Ollama"
   ```

2. Verify Visual Studio version compatibility:
   - Minimum: Visual Studio 2022 (17.0)
   - Check: **Help** → **About Microsoft Visual Studio**

3. Check Windows Event Viewer:
   - Open Event Viewer → Windows Logs → Application
   - Look for Visual Studio or .NET Framework errors

**Solutions:**

**Solution 1: Complete Reinstallation**
```bash
# 1. Uninstall extension
Extensions → Manage Extensions → Installed → Ollama Assistant → Uninstall

# 2. Clear Visual Studio cache
# Close Visual Studio, then delete:
%localappdata%\Microsoft\VisualStudio\17.0_*\ComponentModelCache

# 3. Restart Visual Studio and reinstall
```

**Solution 2: Repair Visual Studio**
```bash
# 1. Open Visual Studio Installer
# 2. Select your VS2022 installation
# 3. Click "More" → "Repair"
# 4. Reinstall extension after repair completes
```

**Solution 3: Check Dependencies**
```bash
# Verify .NET Framework 4.7.2 or later is installed
# Download from: https://dotnet.microsoft.com/download/dotnet-framework
```

### Issue: Extension Crashes on Startup

**Symptoms:**
- Visual Studio freezes when extension loads
- Error dialogs during VS startup
- Extension listed but non-functional

**Diagnostic Steps:**
1. Enable Visual Studio logging:
   ```bash
   devenv /log
   ```

2. Check Activity Log:
   ```
   %appdata%\Microsoft\VisualStudio\17.0\ActivityLog.xml
   ```

3. Safe mode test:
   ```bash
   devenv /safemode
   ```

**Solutions:**

**Solution 1: Reset Extension Settings**
```bash
# 1. Go to Tools → Options → Ollama Assistant
# 2. Click "Reset to Defaults"
# 3. Restart Visual Studio
```

**Solution 2: Clear Extension Data**
```bash
# Delete extension data folder:
%localappdata%\OllamaAssistant\

# Restart Visual Studio
```

**Solution 3: Compatibility Mode**
```bash
# 1. Disable other AI/IntelliSense extensions temporarily
# 2. Test Ollama Assistant in isolation
# 3. Re-enable other extensions one by one
```

## Server Connection Issues

### Issue: Cannot Connect to Ollama Server

**Symptoms:**
- "Connection failed" errors
- "Server unavailable" messages
- Test connection fails in settings

**Diagnostic Steps:**
1. Check Ollama service status:
   ```bash
   # Windows
   netstat -an | findstr 11434
   
   # Check if Ollama is running
   tasklist | findstr ollama
   ```

2. Test manual connection:
   ```bash
   curl http://localhost:11434/api/version
   ```

3. Check firewall and network:
   ```bash
   telnet localhost 11434
   ```

**Solutions:**

**Solution 1: Start Ollama Service**
```bash
# Method 1: Command line
ollama serve

# Method 2: Windows Service (if installed)
net start ollama

# Method 3: Direct execution
cd "C:\Program Files\Ollama"
ollama.exe serve
```

**Solution 2: Fix Network Configuration**
```bash
# 1. Check Windows Firewall
Control Panel → System and Security → Windows Defender Firewall
→ Allow an app or feature → Add Ollama

# 2. Check port availability
netstat -an | findstr 11434

# 3. Try different port
ollama serve --port 11435
# Update extension settings to use new port
```

**Solution 3: Proxy/VPN Issues**
```json
// In extension settings, configure proxy:
{
  "network": {
    "proxy": {
      "enabled": true,
      "host": "proxy.company.com",
      "port": 8080,
      "username": "user",
      "password": "pass"
    }
  }
}
```

### Issue: Server Connection Timeouts

**Symptoms:**
- Intermittent connection failures
- "Request timeout" errors
- Slow response times

**Diagnostic Steps:**
1. Monitor response times:
   ```bash
   # Test response time
   curl -w "%{time_total}" http://localhost:11434/api/version
   ```

2. Check system resources:
   ```bash
   # Monitor CPU and memory usage
   taskmgr
   
   # Check if system is under load
   ```

3. Network diagnostics:
   ```bash
   ping localhost
   tracert localhost
   ```

**Solutions:**

**Solution 1: Increase Timeout Settings**
```json
{
  "serverConnection": {
    "timeout": 60000,
    "retryAttempts": 5,
    "retryDelay": 2000
  }
}
```

**Solution 2: Optimize System Resources**
```bash
# 1. Close unnecessary applications
# 2. Increase system virtual memory
# 3. Consider using smaller/faster models
ollama pull llama2:7b  # Smaller model
```

**Solution 3: Network Optimization**
```json
{
  "network": {
    "keepAlive": true,
    "connectionPooling": true,
    "maxConnections": 2
  }
}
```

## Model and AI Issues

### Issue: Models Not Loading

**Symptoms:**
- "Model not found" errors
- Available models list is empty
- Specific model fails to load

**Diagnostic Steps:**
1. List installed models:
   ```bash
   ollama list
   ```

2. Check model status:
   ```bash
   ollama ps
   ```

3. Test model directly:
   ```bash
   ollama run codellama "Hello world"
   ```

**Solutions:**

**Solution 1: Install Missing Models**
```bash
# Install recommended models
ollama pull codellama
ollama pull mistral
ollama pull deepseek-coder

# Verify installation
ollama list
```

**Solution 2: Model Compatibility Check**
```bash
# Check model requirements
ollama show codellama

# Ensure sufficient system resources
# Minimum 8GB RAM for most models
```

**Solution 3: Clear Model Cache**
```bash
# Stop Ollama service
ollama stop

# Clear model cache (Windows)
rmdir /s "%USERPROFILE%\.ollama"

# Restart and reinstall models
ollama serve
ollama pull codellama
```

### Issue: Poor AI Response Quality

**Symptoms:**
- Irrelevant suggestions
- Incomplete code completions
- Nonsensical responses

**Diagnostic Steps:**
1. Test with minimal context:
   - Reduce context lines to 1-2
   - Test with simple code examples

2. Check model parameters:
   ```json
   {
     "temperature": 0.1,  // Lower = more focused
     "top_p": 0.9,       // Token selection threshold
     "top_k": 40         // Vocabulary size limit
   }
   ```

3. Verify model specialization:
   - Use `codellama` for code completion
   - Use `mistral` for general text
   - Use `deepseek-coder` for complex code

**Solutions:**

**Solution 1: Optimize Model Parameters**
```json
{
  "modelConfigurations": {
    "codellama": {
      "parameters": {
        "temperature": 0.05,    // Very focused
        "top_p": 0.95,          // High precision
        "top_k": 20,            // Limited vocabulary
        "repeat_penalty": 1.2,  // Avoid repetition
        "num_predict": 128      // Shorter responses
      }
    }
  }
}
```

**Solution 2: Improve Context Quality**
```json
{
  "context": {
    "surroundingLines": {
      "before": 5,      // More context
      "after": 3,       // Relevant following code
      "adaptive": true  // Smart context sizing
    },
    "includeImports": true,
    "includeComments": false  // Reduce noise
  }
}
```

**Solution 3: Model Selection Strategy**
```json
{
  "models": {
    "selectionStrategy": "adaptive",
    "languageSpecific": {
      "csharp": "codellama",
      "javascript": "mistral",
      "python": "deepseek-coder"
    }
  }
}
```

## Performance Issues

### Issue: Slow Response Times

**Symptoms:**
- Suggestions take >5 seconds to appear
- UI freezing during AI requests
- High CPU usage

**Diagnostic Steps:**
1. Monitor performance metrics:
   ```
   View → Other Windows → Ollama Assistant Status
   → Performance Tab
   ```

2. Check system resources:
   - Task Manager → Performance tab
   - Monitor GPU usage if applicable

3. Profile request timing:
   ```
   View → Output → Ollama Assistant
   Look for timing information in logs
   ```

**Solutions:**

**Solution 1: Optimize Context Size**
```json
{
  "context": {
    "surroundingLines": {
      "before": 2,  // Reduce from default 3
      "after": 1,   // Reduce from default 2
      "maxTotal": 10
    }
  }
}
```

**Solution 2: Enable Performance Features**
```json
{
  "performance": {
    "caching": {
      "enabled": true,
      "ttlMinutes": 10,
      "maxCacheSize": 200
    },
    "debouncing": {
      "enabled": true,
      "delayMs": 500    // Wait before sending request
    }
  }
}
```

**Solution 3: Use Faster Models**
```bash
# Switch to smaller, faster models
ollama pull llama2:7b-chat
ollama pull mistral:7b

# Configure in extension settings
```

### Issue: High Memory Usage

**Symptoms:**
- Visual Studio using >2GB RAM
- System slowdown
- Out of memory errors

**Diagnostic Steps:**
1. Monitor memory usage:
   ```
   Task Manager → Details → devenv.exe
   ```

2. Check extension memory:
   ```
   View → Other Windows → Ollama Assistant Status
   → Memory Usage
   ```

3. Profile memory leaks:
   - Use Visual Studio Diagnostic Tools
   - Monitor over extended usage

**Solutions:**

**Solution 1: Reduce Memory Footprint**
```json
{
  "memory": {
    "maxUsageMB": 150,
    "enableGarbageCollection": true,
    "contextCacheLimit": 50,
    "historyCacheLimit": 100
  }
}
```

**Solution 2: Optimize Caching**
```json
{
  "caching": {
    "maxCacheSize": 50,     // Reduce from default
    "ttlMinutes": 5,        // Shorter cache time
    "memoryPressureCleanup": true
  }
}
```

**Solution 3: Periodic Cleanup**
```json
{
  "cleanup": {
    "automaticCleanup": true,
    "cleanupIntervalMinutes": 30,
    "clearCacheOnLowMemory": true
  }
}
```

## Feature-Specific Issues

### Issue: Code Completion Not Working

**Symptoms:**
- No AI suggestions in IntelliSense
- Suggestions appear but are not relevant
- Completion suggestions are duplicated

**Diagnostic Steps:**
1. Test in different file types:
   - .cs files (C#)
   - .js files (JavaScript)
   - .py files (Python)

2. Check IntelliSense integration:
   ```
   Tools → Options → Text Editor → C# → IntelliSense
   Verify "Show completion list after a character is typed" is enabled
   ```

3. Test with simple code:
   ```csharp
   // Type this and pause after "Console."
   Console.
   ```

**Solutions:**

**Solution 1: Enable Code Completion**
```json
{
  "features": {
    "codeCompletion": {
      "enabled": true,
      "showInIntelliSense": true,
      "debounceDelay": 300,
      "minCharacters": 1
    }
  }
}
```

**Solution 2: Fix IntelliSense Integration**
```json
{
  "intelliSense": {
    "integration": {
      "priority": 5,           // Higher than default
      "showOllamaIcon": true,
      "filterDuplicates": true,
      "respectUserSettings": true
    }
  }
}
```

**Solution 3: Language-Specific Configuration**
```json
{
  "languageSettings": {
    "csharp": {
      "enableSemanticAnalysis": true,
      "includeUsings": true,
      "contextDepth": 5
    }
  }
}
```

### Issue: Jump Recommendations Not Working

**Symptoms:**
- No navigation suggestions appear
- Jump notifications don't show
- Key binding doesn't work

**Diagnostic Steps:**
1. Check feature enablement:
   ```
   Tools → Options → Ollama Assistant
   → Features → Jump Recommendations
   ```

2. Test key binding:
   - Default: Tab key
   - Check for conflicts: Tools → Options → Keyboard

3. Verify notifications:
   ```
   Tools → Options → Environment → Notifications
   ```

**Solutions:**

**Solution 1: Enable Jump Recommendations**
```json
{
  "features": {
    "jumpRecommendations": {
      "enabled": true,
      "keyBinding": "Tab",
      "showNotifications": true,
      "notificationTimeout": 3000
    }
  }
}
```

**Solution 2: Fix Key Binding Conflicts**
```bash
# 1. Go to Tools → Options → Keyboard
# 2. Search for "OllamaAssistant.ExecuteJump"
# 3. Assign different key combination if Tab is conflicting
# 4. Try Ctrl+Shift+J as alternative
```

**Solution 3: Notification Settings**
```json
{
  "notifications": {
    "jumpRecommendations": {
      "enabled": true,
      "position": "bottom-right",
      "duration": 5000,
      "showPreview": true
    }
  }
}
```

## Error Messages and Solutions

### Common Error Messages

#### "Failed to connect to Ollama server"

**Cause**: Ollama service not running or network issue

**Solution**:
```bash
# Start Ollama service
ollama serve

# Test connection
curl http://localhost:11434/api/version

# Check extension settings
Tools → Options → Ollama Assistant → Test Connection
```

#### "Model 'codellama' not found"

**Cause**: Model not installed or not loaded

**Solution**:
```bash
# Install model
ollama pull codellama

# Verify installation
ollama list

# Test model
ollama run codellama "test"
```

#### "Request timeout after 30000ms"

**Cause**: Server overloaded or model too large

**Solution**:
```json
{
  "serverConnection": {
    "timeout": 60000,
    "retryAttempts": 3
  }
}
```

#### "Insufficient memory to load model"

**Cause**: Not enough RAM for model

**Solution**:
```bash
# Use smaller model
ollama pull mistral:7b

# Check system RAM
wmic computersystem get TotalPhysicalMemory

# Close other applications
```

#### "Extension failed to initialize"

**Cause**: Corrupt installation or missing dependencies

**Solution**:
```bash
# 1. Uninstall extension
# 2. Clear Visual Studio cache
# 3. Repair Visual Studio
# 4. Reinstall extension
```

## Advanced Diagnostics

### Logging and Debugging

#### Enable Debug Logging
```json
{
  "logging": {
    "level": "debug",
    "enableFileLogging": true,
    "logPath": "%temp%\\OllamaAssistant\\logs",
    "maxLogSizeMB": 50
  }
}
```

#### Collect Diagnostic Information
```bash
# 1. Enable debug logging
# 2. Reproduce the issue
# 3. Collect these files:
#    - %temp%\OllamaAssistant\logs\*.log
#    - %appdata%\Microsoft\VisualStudio\17.0\ActivityLog.xml
#    - Extension settings export
```

#### Performance Profiling
```bash
# 1. Start Visual Studio with profiling
devenv /rootSuffix Exp /log

# 2. Use Visual Studio Diagnostic Tools
Debug → Start Diagnostic Tools Without Debugging

# 3. Monitor extension performance
View → Other Windows → Ollama Assistant Status
```

### Network Diagnostics

#### Test Network Connectivity
```bash
# Basic connectivity
ping localhost

# Port accessibility
telnet localhost 11434

# HTTP response
curl -v http://localhost:11434/api/version

# Trace network route
tracert localhost
```

#### Proxy Configuration Test
```json
{
  "network": {
    "proxy": {
      "enabled": true,
      "host": "proxy.company.com",
      "port": 8080,
      "testUrl": "http://httpbin.org/ip"
    }
  }
}
```

### System Resource Monitoring

#### Monitor Resource Usage
```bash
# CPU and Memory
Get-Counter "\Process(devenv)\% Processor Time"
Get-Counter "\Process(devenv)\Working Set"

# Disk I/O
Get-Counter "\Process(devenv)\IO Read Bytes/sec"
Get-Counter "\Process(devenv)\IO Write Bytes/sec"

# Network usage
netstat -e
```

## Getting Help

### Before Contacting Support

1. **Try Safe Mode**:
   ```bash
   devenv /safemode
   # Test if issue persists without other extensions
   ```

2. **Collect System Information**:
   - Windows version: `winver`
   - Visual Studio version: Help → About
   - Extension version: Extensions → Manage Extensions
   - Ollama version: `ollama --version`

3. **Export Configuration**:
   ```
   Tools → Options → Ollama Assistant
   → Export Settings → Save configuration
   ```

4. **Generate Diagnostic Report**:
   ```
   View → Other Windows → Ollama Assistant Status
   → Diagnostics → Generate Report
   ```

### Support Resources

- **GitHub Issues**: Report bugs and request features
- **Documentation**: Complete user guides and API docs
- **Community Forums**: User discussions and solutions
- **Video Tutorials**: Step-by-step problem solving

### Information to Include in Support Requests

1. **Problem Description**:
   - What you were trying to do
   - What happened instead
   - Steps to reproduce

2. **System Information**:
   - Operating system version
   - Visual Studio version and edition
   - Extension version
   - Ollama version and models

3. **Configuration**:
   - Current settings (exported configuration)
   - Custom modifications made
   - Network environment details

4. **Logs and Screenshots**:
   - Error messages (exact text)
   - Extension logs
   - Visual Studio Activity Log
   - Screenshots of issue

5. **Troubleshooting Attempted**:
   - Steps already tried
   - Temporary workarounds found
   - Results of diagnostic tests

---

**Still Need Help?** 🆘

If this guide doesn't resolve your issue, please create a detailed support request with the information outlined above. The development team is committed to helping you get the Ollama Assistant working perfectly in your environment.

*For additional resources, see the complete documentation at `/docs/user/`*