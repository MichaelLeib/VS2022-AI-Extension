using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Shell;
using OllamaAssistant.Services.Implementation;
using OllamaAssistant.Services.Interfaces;
using OllamaAssistant.Infrastructure;

namespace OllamaAssistant.Infrastructure
{
    /// <summary>
    /// Simple dependency injection container for the Ollama Assistant extension
    /// </summary>
    public class ServiceContainer : IDisposable
    {
        private readonly Dictionary<Type, object> _services = new Dictionary<Type, object>();
        private readonly Dictionary<Type, Func<object>> _factories = new Dictionary<Type, Func<object>>();
        private readonly List<IDisposable> _disposableServices = new List<IDisposable>();
        private readonly object _lockObject = new object();
        private bool _disposed;

        public ServiceContainer()
        {
        }

        #region Registration Methods

        /// <summary>
        /// Register a singleton service instance
        /// </summary>
        public void RegisterSingleton<TInterface, TImplementation>(TImplementation instance)
            where TImplementation : class, TInterface
        {
            lock (_lockObject)
            {
                _services[typeof(TInterface)] = instance;
                
                if (instance is IDisposable disposable)
                {
                    _disposableServices.Add(disposable);
                }
            }
        }

        /// <summary>
        /// Register a service factory
        /// </summary>
        public void RegisterFactory<TInterface>(Func<TInterface> factory)
        {
            lock (_lockObject)
            {
                _factories[typeof(TInterface)] = () => factory();
            }
        }

        /// <summary>
        /// Register a transient service type
        /// </summary>
        public void RegisterTransient<TInterface, TImplementation>()
            where TImplementation : class, TInterface, new()
        {
            lock (_lockObject)
            {
                _factories[typeof(TInterface)] = () => new TImplementation();
            }
        }

        #endregion

        #region Resolution Methods

        /// <summary>
        /// Resolve a service instance
        /// </summary>
        public T Resolve<T>()
        {
            return (T)Resolve(typeof(T));
        }

        /// <summary>
        /// Resolve a service instance by type
        /// </summary>
        public object Resolve(Type serviceType)
        {
            lock (_lockObject)
            {
                // Check for existing singleton
                if (_services.TryGetValue(serviceType, out var service))
                {
                    return service;
                }

                // Check for factory
                if (_factories.TryGetValue(serviceType, out var factory))
                {
                    var instance = factory();
                    
                    // Track disposable instances
                    if (instance is IDisposable disposable)
                    {
                        _disposableServices.Add(disposable);
                    }
                    
                    return instance;
                }

                throw new InvalidOperationException($"Service of type {serviceType.Name} is not registered");
            }
        }

        /// <summary>
        /// Try to resolve a service, returning null if not found
        /// </summary>
        public T TryResolve<T>() where T : class
        {
            try
            {
                return Resolve<T>();
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Check if a service is registered
        /// </summary>
        public bool IsRegistered<T>()
        {
            return IsRegistered(typeof(T));
        }

        /// <summary>
        /// Check if a service type is registered
        /// </summary>
        public bool IsRegistered(Type serviceType)
        {
            lock (_lockObject)
            {
                return _services.ContainsKey(serviceType) || _factories.ContainsKey(serviceType);
            }
        }

        #endregion

        #region Service Management

        /// <summary>
        /// Initialize all registered services
        /// </summary>
        public async Task InitializeServicesAsync()
        {
            // Services that need early initialization
            var initializationOrder = new[]
            {
                typeof(ISettingsService),
                typeof(ILogger),
                typeof(ErrorHandler),
                typeof(ICursorHistoryService),
                typeof(ITextViewService),
                typeof(IContextCaptureService),
                typeof(IOllamaService),
                typeof(ISuggestionEngine),
                typeof(IIntelliSenseIntegration),
                typeof(IJumpNotificationService)
            };

            foreach (var serviceType in initializationOrder)
            {
                if (IsRegistered(serviceType))
                {
                    try
                    {
                        var service = Resolve(serviceType);
                        
                        // Call initialization method if available
                        if (service is IAsyncInitializable asyncInit)
                        {
                            await asyncInit.InitializeAsync();
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Error initializing service {serviceType.Name}: {ex.Message}");
                    }
                }
            }
        }

        /// <summary>
        /// Get all services of a specific type
        /// </summary>
        public IEnumerable<T> ResolveAll<T>()
        {
            var results = new List<T>();
            
            lock (_lockObject)
            {
                foreach (var kvp in _services)
                {
                    if (kvp.Value is T service)
                    {
                        results.Add(service);
                    }
                }
            }
            
            return results;
        }

        #endregion

        #region IDisposable

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;

            lock (_lockObject)
            {
                // Dispose all disposable services in reverse order
                for (int i = _disposableServices.Count - 1; i >= 0; i--)
                {
                    try
                    {
                        _disposableServices[i]?.Dispose();
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Error disposing service: {ex.Message}");
                    }
                }

                _disposableServices.Clear();
                _services.Clear();
                _factories.Clear();
            }
        }

        #endregion
    }

    /// <summary>
    /// Interface for services that need async initialization
    /// </summary>
    public interface IAsyncInitializable
    {
        Task InitializeAsync();
    }

    /// <summary>
    /// Static service locator for global access
    /// </summary>
    public static class ServiceLocator
    {
        private static ServiceContainer _container;
        private static readonly object _lockObject = new object();

        public static ServiceContainer Container
        {
            get
            {
                lock (_lockObject)
                {
                    return _container;
                }
            }
            set
            {
                lock (_lockObject)
                {
                    _container = value;
                }
            }
        }

        public static T Resolve<T>()
        {
            var container = Container;
            if (container == null)
                throw new InvalidOperationException("Service container is not initialized");
                
            return container.Resolve<T>();
        }

        public static T TryResolve<T>() where T : class
        {
            var container = Container;
            return container?.TryResolve<T>();
        }

        public static void Initialize(ServiceContainer container)
        {
            Container = container;
        }

        public static void Cleanup()
        {
            lock (_lockObject)
            {
                _container?.Dispose();
                _container = null;
            }
        }
    }
}