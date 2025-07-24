# Ollama Assistant VS2022 Extension - Installation Guide

## Overview

The Ollama Assistant is a powerful Visual Studio 2022 extension that provides AI-powered code completion and navigation assistance using Ollama AI models. This guide will walk you through the complete installation and setup process.

## Prerequisites

### System Requirements

- **Operating System**: Windows 10 (version 1903 or later) or Windows 11
- **Visual Studio**: Visual Studio 2022 (Community, Professional, or Enterprise)
- **Framework**: .NET Framework 4.7.2 or later
- **Memory**: Minimum 8GB RAM (16GB recommended for optimal performance)
- **Storage**: At least 2GB free disk space

### Ollama Server Requirements

- **Ollama Server**: Version 0.1.0 or later
- **Models**: At least one compatible model (CodeLlama, DeepSeek Coder, Mistral, etc.)
- **Network**: HTTP/HTTPS access to Ollama server
- **Default Port**: 11434 (configurable)

## Installation Methods

### Method 1: Visual Studio Marketplace (Recommended)

1. **Open Visual Studio 2022**
   - Launch Visual Studio 2022
   - Go to **Extensions** → **Manage Extensions**

2. **Search for the Extension**
   - Click on **Online** in the left panel
   - Search for "Ollama Assistant"
   - Look for the official extension by the development team

3. **Install the Extension**
   - Click **Download** next to the Ollama Assistant extension
   - Visual Studio will download the extension
   - **Restart Visual Studio** when prompted

4. **Verify Installation**
   - After restart, go to **Extensions** → **Manage Extensions**
   - Click **Installed** to see the Ollama Assistant listed

### Method 2: VSIX File Installation

1. **Download the VSIX File**
   - Download the latest `.vsix` file from the official releases
   - Ensure the file is from a trusted source

2. **Install via Double-Click**
   - Double-click the downloaded `.vsix` file
   - The Visual Studio Installer will launch
   - Follow the installation prompts
   - Restart Visual Studio when completed

3. **Alternative: Manual Installation**
   - In Visual Studio, go to **Extensions** → **Manage Extensions**
   - Click **Install from file...**
   - Browse and select the downloaded `.vsix` file
   - Restart Visual Studio when prompted

## Ollama Server Setup

### Installing Ollama

1. **Download Ollama**
   ```bash
   # Windows (PowerShell)
   winget install Ollama.Ollama
   
   # Or download from https://ollama.ai/download
   ```

2. **Verify Installation**
   ```bash
   ollama --version
   ```

3. **Start Ollama Service**
   ```bash
   # Start the Ollama service
   ollama serve
   
   # The service will start on http://localhost:11434
   ```

### Installing AI Models

1. **Install CodeLlama (Recommended for coding)**
   ```bash
   ollama pull codellama
   ```

2. **Install Additional Models**
   ```bash
   # For advanced code generation
   ollama pull deepseek-coder
   
   # For general purpose
   ollama pull mistral
   
   # For faster responses
   ollama pull llama2
   ```

3. **Verify Model Installation**
   ```bash
   ollama list
   ```

## Initial Configuration

### First Launch Setup

1. **Open Visual Studio 2022**
   - The extension will initialize on first launch
   - A welcome dialog may appear with setup options

2. **Configure Server Connection**
   - Go to **Tools** → **Options** → **Ollama Assistant**
   - Set **Server URL**: `http://localhost:11434` (default)
   - Choose **Default Model**: `codellama` (recommended)
   - Click **Test Connection** to verify setup

3. **Basic Settings Configuration**
   ```
   Code Completion: ✓ Enabled
   Jump Recommendations: ✓ Enabled
   Context Lines Up: 3
   Context Lines Down: 2
   Cursor History Depth: 5
   ```

### Testing the Installation

1. **Create a Test File**
   - Create a new C# console application
   - Add a new class file

2. **Test Code Completion**
   - Start typing code and pause
   - AI suggestions should appear within 2-3 seconds
   - Look for the Ollama Assistant icon in suggestions

3. **Test Jump Recommendations**
   - Move cursor around your code
   - Look for subtle notifications suggesting navigation jumps
   - Press **Tab** (default) to execute jumps

## Advanced Configuration

### Network Configuration

For custom network setups:

```json
{
  "serverUrl": "https://your-ollama-server.com:11434",
  "useHttps": true,
  "connectionTimeout": 30,
  "apiKey": "your-api-key-if-required"
}
```

### Performance Tuning

Optimize for your system:

```json
{
  "maxConcurrentRequests": 2,
  "requestTimeoutSeconds": 30,
  "enableCaching": true,
  "cacheTTLMinutes": 5,
  "enableDebouncing": true,
  "debounceDelayMs": 300
}
```

### Multi-Model Setup

Configure multiple models for different scenarios:

```json
{
  "models": {
    "primary": "codellama",
    "fallback": "mistral",
    "specialized": {
      "debugging": "deepseek-coder",
      "documentation": "llama2"
    }
  }
}
```

## Verification Steps

### Health Check

1. **Extension Status**
   - Go to **View** → **Other Windows** → **Ollama Assistant Status**
   - Verify all services show "Connected" or "Ready"

2. **Server Connectivity**
   - Check server response time (should be < 1000ms)
   - Verify model availability
   - Test sample completion request

3. **Feature Validation**
   - ✓ Code completion suggestions appear
   - ✓ Jump recommendations work
   - ✓ Context tracking is active
   - ✓ No error messages in output window

### Performance Benchmarks

Expected performance metrics:
- **First suggestion**: < 3 seconds
- **Subsequent suggestions**: < 1 second
- **Jump recommendations**: < 500ms
- **Memory usage**: < 200MB additional
- **VS startup impact**: < 2 seconds

## Common Installation Issues

### Issue: Extension Not Loading

**Symptoms**: Extension missing from Extensions list

**Solutions**:
1. Verify Visual Studio 2022 version compatibility
2. Check Windows Event Viewer for .NET errors
3. Clear Visual Studio component cache:
   ```
   %localappdata%\Microsoft\VisualStudio\17.0_[instance]\ComponentModelCache
   ```

### Issue: Server Connection Failed

**Symptoms**: "Unable to connect to Ollama server" error

**Solutions**:
1. Verify Ollama service is running: `ollama serve`
2. Check firewall settings for port 11434
3. Test connection manually: `curl http://localhost:11434/api/version`
4. Verify no proxy/VPN interference

### Issue: Models Not Found

**Symptoms**: "Model not available" warnings

**Solutions**:
1. List installed models: `ollama list`
2. Pull required models: `ollama pull codellama`
3. Restart Ollama service after model installation
4. Clear extension cache in VS Options

### Issue: Poor Performance

**Symptoms**: Slow suggestions, high memory usage

**Solutions**:
1. Reduce context window size in settings
2. Disable unnecessary features temporarily
3. Check system resources (CPU, RAM)
4. Consider using smaller/faster models

## Troubleshooting Commands

### Diagnostic Commands

```bash
# Check Ollama service status
ollama ps

# Test model response
ollama run codellama "Hello world"

# Check model memory usage
ollama show codellama

# Clear Ollama cache
ollama system info
```

### Visual Studio Diagnostics

1. **Activity Log**
   - Start VS with: `devenv /log`
   - Check: `%appdata%\Microsoft\VisualStudio\17.0\ActivityLog.xml`

2. **Extension Logs**
   - Go to **View** → **Output** → **Ollama Assistant**
   - Look for error messages and warnings

3. **Reset Extension Settings**
   - **Tools** → **Options** → **Ollama Assistant**
   - Click **Reset to Defaults**

## Getting Help

### Documentation Resources

- **User Guide**: `/docs/user/user-guide.md`
- **Configuration Reference**: `/docs/user/configuration-guide.md`
- **Troubleshooting**: `/docs/user/troubleshooting-guide.md`
- **FAQ**: `/docs/user/faq.md`

### Support Channels

- **GitHub Issues**: Submit bug reports and feature requests
- **Documentation**: In-app help system (**Help** → **Ollama Assistant**)
- **Community**: User forums and discussions

### Reporting Issues

When reporting installation issues, please include:

1. **System Information**
   - Windows version
   - Visual Studio version and edition
   - .NET Framework version

2. **Extension Information**
   - Extension version
   - Installation method used
   - Installation timestamp

3. **Error Details**
   - Complete error messages
   - Screenshots if applicable
   - Activity log excerpts
   - Steps to reproduce

4. **Environment Details**
   - Ollama version and models
   - Network configuration
   - Antivirus/security software

## Next Steps

After successful installation:

1. **Read the User Guide** → `user-guide.md`
2. **Configure Advanced Settings** → `configuration-guide.md`
3. **Learn Key Features** → `features-overview.md`
4. **Watch Video Tutorials** → `video-tutorials.md`

## Maintenance

### Regular Updates

- **Extension Updates**: Check Visual Studio Marketplace monthly
- **Ollama Updates**: Run `ollama --version` and update as needed
- **Model Updates**: Update models with `ollama pull <model>`

### Health Monitoring

- **Weekly**: Check performance metrics in extension status
- **Monthly**: Review error logs and clear cache if needed
- **Quarterly**: Verify configuration and optimize settings

---

**Installation Complete!** 🎉

Your Ollama Assistant extension is now ready to enhance your coding experience with AI-powered suggestions and navigation assistance.

*For additional help, see the complete documentation at `/docs/user/`*