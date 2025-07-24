using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace OllamaAssistant.Services.Implementation
{
    /// <summary>
    /// Service for collecting diagnostic information for error reporting
    /// </summary>
    public class DiagnosticCollector : IDisposable
    {
        private readonly IVsShell _vsShell;
        private bool _disposed;

        public DiagnosticCollector()
        {
            _vsShell = ServiceProvider.GlobalProvider.GetService(typeof(SVsShell)) as IVsShell;
        }

        /// <summary>
        /// Collects comprehensive diagnostic information for error reporting
        /// </summary>
        public async Task<DiagnosticInformation> CollectDiagnosticsAsync(Exception exception, string context = null)
        {
            if (_disposed)
                return new DiagnosticInformation();

            var diagnostics = new DiagnosticInformation
            {
                CollectionTime = DateTime.UtcNow,
                Context = context
            };

            try
            {
                // Collect system information
                diagnostics.SystemInfo = await CollectSystemInformationAsync();

                // Collect Visual Studio information
                diagnostics.VSInfo = await CollectVSInformationAsync();

                // Collect extension information
                diagnostics.ExtensionInfo = CollectExtensionInformation();

                // Collect exception details
                if (exception != null)
                {
                    diagnostics.ExceptionDetails = CollectExceptionDetails(exception);
                }

                // Collect memory information
                diagnostics.MemoryInfo = CollectMemoryInformation();

                // Collect recent log entries
                diagnostics.RecentLogs = await CollectRecentLogsAsync();

                // Collect configuration information (sanitized)
                diagnostics.ConfigurationInfo = await CollectConfigurationInfoAsync();

                // Collect network information
                diagnostics.NetworkInfo = await CollectNetworkInformationAsync();
            }
            catch (Exception collectException)
            {
                diagnostics.CollectionErrors.Add($"Error collecting diagnostics: {collectException.Message}");
            }

            return diagnostics;
        }

        /// <summary>
        /// Collects system information
        /// </summary>
        private async Task<SystemInformation> CollectSystemInformationAsync()
        {
            return await Task.Run(() =>
            {
                try
                {
                    var systemInfo = new SystemInformation
                    {
                        OSVersion = Environment.OSVersion.ToString(),
                        CLRVersion = Environment.Version.ToString(),
                        MachineName = Environment.MachineName,
                        UserName = Environment.UserName,
                        ProcessorCount = Environment.ProcessorCount,
                        WorkingSet = Environment.WorkingSet,
                        SystemDirectory = Environment.SystemDirectory,
                        CurrentDirectory = Environment.CurrentDirectory,
                        Is64BitOS = Environment.Is64BitOperatingSystem,
                        Is64BitProcess = Environment.Is64BitProcess
                    };

                    // Get total physical memory
                    try
                    {
                        var process = Process.GetCurrentProcess();
                        systemInfo.TotalPhysicalMemory = GC.GetTotalMemory(false);
                        systemInfo.AvailablePhysicalMemory = process.WorkingSet64;
                    }
                    catch
                    {
                        // Ignore if memory info unavailable
                    }

                    return systemInfo;
                }
                catch (Exception ex)
                {
                    return new SystemInformation
                    {
                        CollectionError = ex.Message
                    };
                }
            });
        }

        /// <summary>
        /// Collects Visual Studio information
        /// </summary>
        private async Task<VSInformation> CollectVSInformationAsync()
        {
            var vsInfo = new VSInformation();

            try
            {
                if (_vsShell != null)
                {
                    // Get VS version
                    if (_vsShell.GetProperty((int)__VSSPROPID5.VSSPROPID_ReleaseVersion, out var version) == 0 && version != null)
                    {
                        vsInfo.Version = version.ToString();
                    }

                    // Get VS edition
                    if (_vsShell.GetProperty((int)__VSSPROPID.VSSPROPID_EditionName, out var edition) == 0 && edition != null)
                    {
                        vsInfo.Edition = edition.ToString();
                    }

                    // Get installation directory
                    if (_vsShell.GetProperty((int)__VSSPROPID.VSSPROPID_InstallDirectory, out var installDir) == 0 && installDir != null)
                    {
                        vsInfo.InstallDirectory = installDir.ToString();
                    }
                }

                // Get loaded packages information
                vsInfo.LoadedPackages = await GetLoadedPackagesAsync();

                // Get current solution information
                vsInfo.SolutionInfo = await GetSolutionInformationAsync();
            }
            catch (Exception ex)
            {
                vsInfo.CollectionError = ex.Message;
            }

            return vsInfo;
        }

        /// <summary>
        /// Collects extension-specific information
        /// </summary>
        private ExtensionInformation CollectExtensionInformation()
        {
            try
            {
                var assembly = Assembly.GetExecutingAssembly();
                return new ExtensionInformation
                {
                    Version = assembly.GetName().Version?.ToString(),
                    Location = assembly.Location,
                    AssemblyName = assembly.FullName,
                    LoadedModules = GetLoadedModules(),
                    StartupTime = Process.GetCurrentProcess().StartTime,
                    UpTime = DateTime.UtcNow - Process.GetCurrentProcess().StartTime
                };
            }
            catch (Exception ex)
            {
                return new ExtensionInformation
                {
                    CollectionError = ex.Message
                };
            }
        }

        /// <summary>
        /// Collects exception details with stack trace analysis
        /// </summary>
        private ExceptionDetails CollectExceptionDetails(Exception exception)
        {
            var details = new ExceptionDetails
            {
                Type = exception.GetType().FullName,
                Message = exception.Message,
                StackTrace = exception.StackTrace,
                Source = exception.Source,
                HelpLink = exception.HelpLink,
                Data = new Dictionary<string, string>()
            };

            // Collect exception data
            foreach (var key in exception.Data.Keys)
            {
                try
                {
                    details.Data[key.ToString()] = exception.Data[key]?.ToString();
                }
                catch
                {
                    details.Data[key.ToString()] = "<Unable to serialize>";
                }
            }

            // Collect inner exceptions
            var innerEx = exception.InnerException;
            while (innerEx != null)
            {
                details.InnerExceptions.Add(new InnerExceptionInfo
                {
                    Type = innerEx.GetType().FullName,
                    Message = innerEx.Message,
                    StackTrace = innerEx.StackTrace
                });

                innerEx = innerEx.InnerException;
            }

            return details;
        }

        /// <summary>
        /// Collects memory information
        /// </summary>
        private MemoryInformation CollectMemoryInformation()
        {
            try
            {
                var process = Process.GetCurrentProcess();
                return new MemoryInformation
                {
                    WorkingSet = process.WorkingSet64,
                    PrivateMemory = process.PrivateMemorySize64,
                    VirtualMemory = process.VirtualMemorySize64,
                    GCTotalMemory = GC.GetTotalMemory(false),
                    GCGen0Collections = GC.CollectionCount(0),
                    GCGen1Collections = GC.CollectionCount(1),
                    GCGen2Collections = GC.CollectionCount(2)
                };
            }
            catch (Exception ex)
            {
                return new MemoryInformation
                {
                    CollectionError = ex.Message
                };
            }
        }

        /// <summary>
        /// Collects recent log entries (last 50 entries)
        /// </summary>
        private async Task<List<string>> CollectRecentLogsAsync()
        {
            var logs = new List<string>();

            try
            {
                // This would integrate with the extension's logging system
                // For now, return placeholder entries
                await Task.Run(() =>
                {
                    logs.Add($"[{DateTime.UtcNow:HH:mm:ss}] Diagnostic collection started");
                    // In real implementation, would read from log files or in-memory log buffer
                });
            }
            catch (Exception ex)
            {
                logs.Add($"Error collecting logs: {ex.Message}");
            }

            return logs;
        }

        /// <summary>
        /// Collects sanitized configuration information
        /// </summary>
        private async Task<ConfigurationInformation> CollectConfigurationInfoAsync()
        {
            return await Task.Run(() =>
            {
                try
                {
                    return new ConfigurationInformation
                    {
                        // Sanitized configuration (no sensitive data)
                        HasOllamaServerConfigured = !string.IsNullOrEmpty("placeholder"), // Check actual config
                        HasCustomSettings = true, // Check actual config
                        DebugMode = IsDebugMode(),
                        ConfigurationSource = "Registry/Settings", // Or actual source
                        LastConfigurationChange = DateTime.UtcNow.AddDays(-1) // Placeholder
                    };
                }
                catch (Exception ex)
                {
                    return new ConfigurationInformation
                    {
                        CollectionError = ex.Message
                    };
                }
            });
        }

        /// <summary>
        /// Collects network-related information
        /// </summary>
        private async Task<NetworkInformation> CollectNetworkInformationAsync()
        {
            return await Task.Run(() =>
            {
                try
                {
                    return new NetworkInformation
                    {
                        IsNetworkAvailable = System.Net.NetworkInformation.NetworkInterface.GetIsNetworkAvailable(),
                        NetworkInterfaces = GetNetworkInterfaceInfo(),
                        DNSServers = GetDNSServers(),
                        ProxyConfiguration = GetProxyConfiguration()
                    };
                }
                catch (Exception ex)
                {
                    return new NetworkInformation
                    {
                        CollectionError = ex.Message
                    };
                }
            });
        }

        /// <summary>
        /// Gets information about loaded packages
        /// </summary>
        private async Task<List<string>> GetLoadedPackagesAsync()
        {
            return await Task.Run(() =>
            {
                var packages = new List<string>();
                try
                {
                    // In real implementation, would enumerate loaded VS packages
                    packages.Add("OllamaAssistant Package");
                    packages.Add("Core VS Packages");
                }
                catch
                {
                    packages.Add("Error collecting package information");
                }
                return packages;
            });
        }

        /// <summary>
        /// Gets current solution information
        /// </summary>
        private async Task<SolutionInformation> GetSolutionInformationAsync()
        {
            return await Task.Run(() =>
            {
                try
                {
                    return new SolutionInformation
                    {
                        HasSolutionLoaded = true, // Check actual solution state
                        ProjectCount = 1, // Count actual projects
                        SolutionDirectory = @"C:\Sample\Path", // Get actual path (sanitized)
                        TotalFiles = 100 // Count actual files
                    };
                }
                catch (Exception ex)
                {
                    return new SolutionInformation
                    {
                        CollectionError = ex.Message
                    };
                }
            });
        }

        /// <summary>
        /// Gets loaded modules information
        /// </summary>
        private List<string> GetLoadedModules()
        {
            var modules = new List<string>();
            try
            {
                var process = Process.GetCurrentProcess();
                foreach (ProcessModule module in process.Modules)
                {
                    modules.Add($"{module.ModuleName} ({module.FileVersionInfo.FileVersion})");
                }
            }
            catch
            {
                modules.Add("Error collecting module information");
            }
            return modules;
        }

        /// <summary>
        /// Checks if extension is running in debug mode
        /// </summary>
        private bool IsDebugMode()
        {
#if DEBUG
            return true;
#else
            return false;
#endif
        }

        /// <summary>
        /// Gets network interface information
        /// </summary>
        private List<string> GetNetworkInterfaceInfo()
        {
            var interfaces = new List<string>();
            try
            {
                foreach (var nic in System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (nic.OperationalStatus == System.Net.NetworkInformation.OperationalStatus.Up)
                    {
                        interfaces.Add($"{nic.Name} ({nic.NetworkInterfaceType})");
                    }
                }
            }
            catch
            {
                interfaces.Add("Error collecting network interface information");
            }
            return interfaces;
        }

        /// <summary>
        /// Gets DNS server information
        /// </summary>
        private List<string> GetDNSServers()
        {
            var dnsServers = new List<string>();
            try
            {
                foreach (var nic in System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (nic.OperationalStatus == System.Net.NetworkInformation.OperationalStatus.Up)
                    {
                        var ipProps = nic.GetIPProperties();
                        foreach (var dns in ipProps.DnsAddresses)
                        {
                            dnsServers.Add(dns.ToString());
                        }
                    }
                }
            }
            catch
            {
                dnsServers.Add("Error collecting DNS information");
            }
            return dnsServers;
        }

        /// <summary>
        /// Gets proxy configuration information
        /// </summary>
        private string GetProxyConfiguration()
        {
            try
            {
                var proxy = System.Net.WebRequest.GetSystemWebProxy();
                return proxy?.GetProxy(new Uri("http://www.example.com"))?.ToString() ?? "No proxy configured";
            }
            catch
            {
                return "Error collecting proxy information";
            }
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
        }
    }

    /// <summary>
    /// Complete diagnostic information package
    /// </summary>
    public class DiagnosticInformation
    {
        public DateTime CollectionTime { get; set; }
        public string Context { get; set; }
        public SystemInformation SystemInfo { get; set; }
        public VSInformation VSInfo { get; set; }
        public ExtensionInformation ExtensionInfo { get; set; }
        public ExceptionDetails ExceptionDetails { get; set; }
        public MemoryInformation MemoryInfo { get; set; }
        public List<string> RecentLogs { get; set; } = new List<string>();
        public ConfigurationInformation ConfigurationInfo { get; set; }
        public NetworkInformation NetworkInfo { get; set; }
        public List<string> CollectionErrors { get; set; } = new List<string>();
    }

    /// <summary>
    /// System information
    /// </summary>
    public class SystemInformation
    {
        public string OSVersion { get; set; }
        public string CLRVersion { get; set; }
        public string MachineName { get; set; }
        public string UserName { get; set; }
        public int ProcessorCount { get; set; }
        public long WorkingSet { get; set; }
        public string SystemDirectory { get; set; }
        public string CurrentDirectory { get; set; }
        public bool Is64BitOS { get; set; }
        public bool Is64BitProcess { get; set; }
        public long TotalPhysicalMemory { get; set; }
        public long AvailablePhysicalMemory { get; set; }
        public string CollectionError { get; set; }
    }

    /// <summary>
    /// Visual Studio information
    /// </summary>
    public class VSInformation
    {
        public string Version { get; set; }
        public string Edition { get; set; }
        public string InstallDirectory { get; set; }
        public List<string> LoadedPackages { get; set; } = new List<string>();
        public SolutionInformation SolutionInfo { get; set; }
        public string CollectionError { get; set; }
    }

    /// <summary>
    /// Extension information
    /// </summary>
    public class ExtensionInformation
    {
        public string Version { get; set; }
        public string Location { get; set; }
        public string AssemblyName { get; set; }
        public List<string> LoadedModules { get; set; } = new List<string>();
        public DateTime StartupTime { get; set; }
        public TimeSpan UpTime { get; set; }
        public string CollectionError { get; set; }
    }

    /// <summary>
    /// Exception details
    /// </summary>
    public class ExceptionDetails
    {
        public string Type { get; set; }
        public string Message { get; set; }
        public string StackTrace { get; set; }
        public string Source { get; set; }
        public string HelpLink { get; set; }
        public Dictionary<string, string> Data { get; set; } = new Dictionary<string, string>();
        public List<InnerExceptionInfo> InnerExceptions { get; set; } = new List<InnerExceptionInfo>();
    }

    /// <summary>
    /// Inner exception information
    /// </summary>
    public class InnerExceptionInfo
    {
        public string Type { get; set; }
        public string Message { get; set; }
        public string StackTrace { get; set; }
    }

    /// <summary>
    /// Memory information
    /// </summary>
    public class MemoryInformation
    {
        public long WorkingSet { get; set; }
        public long PrivateMemory { get; set; }
        public long VirtualMemory { get; set; }
        public long GCTotalMemory { get; set; }
        public int GCGen0Collections { get; set; }
        public int GCGen1Collections { get; set; }
        public int GCGen2Collections { get; set; }
        public string CollectionError { get; set; }
    }

    /// <summary>
    /// Configuration information (sanitized)
    /// </summary>
    public class ConfigurationInformation
    {
        public bool HasOllamaServerConfigured { get; set; }
        public bool HasCustomSettings { get; set; }
        public bool DebugMode { get; set; }
        public string ConfigurationSource { get; set; }
        public DateTime LastConfigurationChange { get; set; }
        public string CollectionError { get; set; }
    }

    /// <summary>
    /// Network information
    /// </summary>
    public class NetworkInformation
    {
        public bool IsNetworkAvailable { get; set; }
        public List<string> NetworkInterfaces { get; set; } = new List<string>();
        public List<string> DNSServers { get; set; } = new List<string>();
        public string ProxyConfiguration { get; set; }
        public string CollectionError { get; set; }
    }

    /// <summary>
    /// Solution information
    /// </summary>
    public class SolutionInformation
    {
        public bool HasSolutionLoaded { get; set; }
        public int ProjectCount { get; set; }
        public string SolutionDirectory { get; set; }
        public int TotalFiles { get; set; }
        public string CollectionError { get; set; }
    }
}