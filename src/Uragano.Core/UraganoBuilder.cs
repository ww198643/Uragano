﻿using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Uragano.Abstractions;
using Uragano.Abstractions.CircuitBreaker;
using Uragano.Abstractions.ServiceDiscovery;
using Uragano.Core.HostedService;
using Uragano.DynamicProxy;
using Uragano.DynamicProxy.Interceptor;
using Uragano.Remoting;
using Uragano.Remoting.LoadBalancing;

namespace Uragano.Core
{
    public class UraganoBuilder : IUraganoBuilder
    {
        public IServiceCollection ServiceCollection { get; }

        internal UraganoSettings UraganoSettings { get; } = new UraganoSettings();

        public UraganoBuilder(IServiceCollection serviceCollection)
        {
            ServiceCollection = serviceCollection;
            AddHostedService<InfrastructureStartup>();
        }

        #region Server

        public void AddServer(string address, int port = 5730, string certUrl = "", string certPwd = "", int? weight = default)
        {
            UraganoSettings.ServerSettings = new ServerSettings
            {
                Address = address.ReplaceIpPlaceholder(),
                Port = port,
                Weight = weight
            };
            if (!string.IsNullOrWhiteSpace(certUrl))
            {
                if (!File.Exists(certUrl))
                    throw new FileNotFoundException($"Certificate file {certUrl} not found.");
                UraganoSettings.ServerSettings.X509Certificate2 =
                    new X509Certificate2(certUrl, certPwd);
            }

            RegisterServerServices();
        }

        public void AddServer(IConfigurationSection configurationSection)
        {
            UraganoSettings.ServerSettings = new ServerSettings();
            if (configurationSection.Exists())
            {
                var addressSection = configurationSection.GetSection("address");
                if (addressSection.Exists())
                    UraganoSettings.ServerSettings.Address = addressSection.Value.ReplaceIpPlaceholder();

                var portSection = configurationSection.GetSection("port");
                if (portSection.Exists())
                    UraganoSettings.ServerSettings.Port = int.Parse(portSection.Value);

                var weightSection = configurationSection.GetSection("weight");
                if (weightSection.Exists())
                    UraganoSettings.ServerSettings.Weight = int.Parse(weightSection.Value);

                var certUrlSection = configurationSection.GetSection("certurl");
                if (certUrlSection.Exists() && !string.IsNullOrWhiteSpace(certUrlSection.Value))
                {
                    if (!File.Exists(certUrlSection.Value))
                        throw new FileNotFoundException($"Certificate file {certUrlSection.Value} not found.");
                    UraganoSettings.ServerSettings.X509Certificate2 = new X509Certificate2(certUrlSection.Value, configurationSection.GetValue<string>("certpwd"));
                }
            }
            RegisterServerServices();
        }

        #endregion

        #region Client

        public void AddClient<TLoadBalancing>(ClientSettings settings) where TLoadBalancing : class, ILoadBalancing
        {
            AddClient(typeof(TLoadBalancing), settings);
        }

        public void AddClient<TLoadBalancing>(IConfigurationSection configurationSection)
        {
            if (configurationSection != null && configurationSection.Exists())
            {
                var settings = configurationSection.Get<ClientSettings>();
                AddClient(typeof(TLoadBalancing), settings);
            }
            else
                AddClient(typeof(TLoadBalancing));
        }


        public void AddClient(Type loadBalancing)
        {
            RegisterSingletonService(typeof(ILoadBalancing), loadBalancing);
            RegisterClientServices();
        }

        public void AddClient(Type loadBalancing, ClientSettings settings)
        {
            UraganoSettings.ClientSettings = settings;
            AddClient(loadBalancing);
        }


        #endregion

        #region Global interceptor
        public void AddClientGlobalInterceptor<TInterceptor>() where TInterceptor : class, IInterceptor
        {
            if (UraganoSettings.ClientGlobalInterceptors.Any(p => p == typeof(TInterceptor)))
                return;
            UraganoSettings.ClientGlobalInterceptors.Add(typeof(TInterceptor));
            RegisterScopedService(typeof(TInterceptor));
        }

        public void AddServerGlobalInterceptor<TInterceptor>() where TInterceptor : class, IInterceptor
        {
            if (UraganoSettings.ServerGlobalInterceptors.Any(p => p == typeof(TInterceptor)))
                return;
            UraganoSettings.ServerGlobalInterceptors.Add(typeof(TInterceptor));
            RegisterScopedService(typeof(TInterceptor));
        }
        #endregion

        #region Service discovery
        /// <summary>
        /// For client
        /// </summary>
        /// <typeparam name="TServiceDiscovery"></typeparam>
        /// <param name="serviceDiscoveryClientConfiguration"></param>
        public void AddServiceDiscovery<TServiceDiscovery>(IServiceDiscoveryClientConfiguration serviceDiscoveryClientConfiguration) where TServiceDiscovery : class, IServiceDiscovery
        {
            AddServiceDiscovery(typeof(TServiceDiscovery), serviceDiscoveryClientConfiguration);
        }

        public void AddServiceDiscovery(Type serviceDiscovery,
            IServiceDiscoveryClientConfiguration serviceDiscoveryClientConfiguration)
        {
            if (serviceDiscoveryClientConfiguration == null) throw new ArgumentNullException(nameof(serviceDiscoveryClientConfiguration));
            ServiceCollection.AddSingleton(serviceDiscoveryClientConfiguration);
            ServiceCollection.AddSingleton(typeof(IServiceDiscovery), serviceDiscovery);
            AddHostedService<ServiceDiscoveryStartup>();
        }

        /// <summary>
        /// For server
        /// </summary>
        /// <typeparam name="TServiceDiscovery"></typeparam>
        /// <param name="serviceDiscoveryClientConfiguration"></param>
        /// <param name="serviceRegisterConfiguration"></param>
        public void AddServiceDiscovery<TServiceDiscovery>(IServiceDiscoveryClientConfiguration serviceDiscoveryClientConfiguration,
            IServiceRegisterConfiguration serviceRegisterConfiguration) where TServiceDiscovery : class, IServiceDiscovery
        {
            AddServiceDiscovery(typeof(TServiceDiscovery), serviceDiscoveryClientConfiguration, serviceRegisterConfiguration);
        }

        public void AddServiceDiscovery(Type serviceDiscovery,
            IServiceDiscoveryClientConfiguration serviceDiscoveryClientConfiguration,
            IServiceRegisterConfiguration serviceRegisterConfiguration)
        {
            if (serviceDiscoveryClientConfiguration == null) throw new ArgumentNullException(nameof(serviceDiscoveryClientConfiguration));
            if (serviceRegisterConfiguration == null) throw new ArgumentNullException(nameof(serviceRegisterConfiguration));
            ServiceCollection.AddSingleton(serviceDiscoveryClientConfiguration);
            ServiceCollection.AddSingleton(serviceRegisterConfiguration);
            ServiceCollection.AddSingleton(typeof(IServiceDiscovery), serviceDiscovery);
            AddHostedService<ServiceDiscoveryStartup>();
        }

        #endregion

        #region Option
        public void AddOption<T>(UraganoOption<T> option, T value)
        {
            UraganoOptions.SetOption(option, value);
        }

        public void AddOptions(IConfigurationSection configuration)
        {
            if (!configuration.Exists())
                return;
            foreach (var section in configuration.GetChildren())
            {
                switch (section.Key.ToLower())
                {
                    case "threadpool_minthreads":
                        UraganoOptions.SetOption(UraganoOptions.ThreadPool_MinThreads, configuration.GetValue<int>(section.Key));
                        break;
                    case "threadpool_completionportthreads":
                        UraganoOptions.SetOption(UraganoOptions.ThreadPool_CompletionPortThreads, configuration.GetValue<int>(section.Key));
                        break;
                    case "consul_node_status_refresh_interval":
                        UraganoOptions.SetOption(UraganoOptions.Consul_Node_Status_Refresh_Interval, TimeSpan.FromMilliseconds(configuration.GetValue<int>(section.Key)));
                        break;
                    case "server_dotnetty_channel_sobacklog":
                        UraganoOptions.SetOption(UraganoOptions.Server_DotNetty_Channel_SoBacklog, configuration.GetValue<int>(section.Key));
                        break;
                    case "dotnetty_connect_timeout":
                        UraganoOptions.SetOption(UraganoOptions.DotNetty_Connect_Timeout, TimeSpan.FromMilliseconds(configuration.GetValue<int>(section.Key)));
                        break;
                    case "dotnetty_enable_libuv":
                        UraganoOptions.SetOption(UraganoOptions.DotNetty_Enable_Libuv, configuration.GetValue<bool>(section.Key));
                        break;
                    case "dotnetty_event_loop_count":
                        UraganoOptions.SetOption(UraganoOptions.DotNetty_Event_Loop_Count, configuration.GetValue<int>(section.Key));
                        break;
                    case "remoting_invoke_cancellationtokensource_timeout":
                        UraganoOptions.SetOption(UraganoOptions.Remoting_Invoke_CancellationTokenSource_Timeout, TimeSpan.FromMilliseconds(configuration.GetValue<int>(section.Key)));
                        break;
                    case "output_dynamicproxy_sourcecode":
                        UraganoOptions.SetOption(UraganoOptions.Output_DynamicProxy_SourceCode, configuration.GetValue<bool>(section.Key));
                        break;
                }

            }
        }



        #endregion

        #region Circuit breaker
        public void AddCircuitBreaker<TCircuitBreakerEvent>(int timeout = 3000, int retry = 3,
            int exceptionsAllowedBeforeBreaking = 10, int durationOfBreak = 60000, int maxParallelization = 0, int maxQueuingActions = 0) where TCircuitBreakerEvent : ICircuitBreakerEvent
        {
            UraganoSettings.CircuitBreakerOptions = new CircuitBreakerOptions
            {
                Timeout = TimeSpan.FromMilliseconds(timeout),
                Retry = retry,
                ExceptionsAllowedBeforeBreaking = exceptionsAllowedBeforeBreaking,
                DurationOfBreak = TimeSpan.FromMilliseconds(durationOfBreak),
                MaxParallelization = maxParallelization,
                MaxQueuingActions = maxQueuingActions
            };
            RegisterSingletonService(typeof(ICircuitBreakerEvent), typeof(TCircuitBreakerEvent));
        }

        public void AddCircuitBreaker(int timeout = 3000, int retry = 3, int exceptionsAllowedBeforeBreaking = 10,
            int durationOfBreak = 60000, int maxParallelization = 0, int maxQueuingActions = 0)
        {
            UraganoSettings.CircuitBreakerOptions = new CircuitBreakerOptions
            {
                Timeout = TimeSpan.FromMilliseconds(timeout),
                Retry = retry,
                ExceptionsAllowedBeforeBreaking = exceptionsAllowedBeforeBreaking,
                DurationOfBreak = TimeSpan.FromMilliseconds(durationOfBreak),
                MaxParallelization = maxParallelization,
                MaxQueuingActions = maxQueuingActions
            };
        }

        public void AddCircuitBreaker(IConfigurationSection configurationSection)
        {
            UraganoSettings.CircuitBreakerOptions = new CircuitBreakerOptions
            {
                Timeout = TimeSpan.FromMilliseconds(configurationSection.GetValue<int>("timeout")),
                Retry = configurationSection.GetValue<int>("retry"),
                ExceptionsAllowedBeforeBreaking = configurationSection.GetValue<int>("ExceptionsAllowedBeforeBreaking"),
                DurationOfBreak = TimeSpan.FromMilliseconds(configurationSection.GetValue<int>("DurationOfBreak")),
                MaxParallelization = configurationSection.GetValue<int>("MaxParallelization"),
                MaxQueuingActions = configurationSection.GetValue<int>("MaxQueuingActions")
            };
        }

        public void AddCircuitBreaker<TCircuitBreakerEvent>(IConfigurationSection configurationSection) where TCircuitBreakerEvent : ICircuitBreakerEvent
        {
            AddCircuitBreaker<TCircuitBreakerEvent>(configurationSection.GetValue<int>("timeout"), configurationSection.GetValue<int>("retry"), configurationSection.GetValue<int>("ExceptionsAllowedBeforeBreaking"), configurationSection.GetValue<int>("DurationOfBreak"), configurationSection.GetValue<int>("MaxParallelization"), configurationSection.GetValue<int>("MaxQueuingActions"));
        }

        #endregion

        #region Codec
        public void AddCodec<TCodec>() where TCodec : ICodec
        {
            ServiceCollection.AddSingleton(typeof(ICodec), typeof(TCodec));

        }

        #endregion

        #region Caching

        public void AddCaching<TCaching>(ICachingOptions cachingOptions) where TCaching : class, ICaching
        {
            AddCaching(typeof(TCaching), typeof(CachingKeyGenerator), cachingOptions);
        }

        public void AddCaching<TCaching, TKeyGenerator>(ICachingOptions cachingOptions) where TCaching : class, ICaching where TKeyGenerator : class, ICachingKeyGenerator
        {
            AddCaching(typeof(TCaching), typeof(TKeyGenerator), cachingOptions);
        }

        public void AddCaching(Type caching, ICachingOptions cachingOptions)
        {
            AddCaching(caching, typeof(CachingKeyGenerator), cachingOptions);
        }

        public void AddCaching(Type caching, Type keyGenerator, ICachingOptions cachingOptions)
        {
            UraganoSettings.CachingOptions = cachingOptions;
            ServiceCollection.AddSingleton(typeof(ICaching), caching);
            ServiceCollection.AddSingleton(typeof(ICachingKeyGenerator), keyGenerator);
            RegisterSingletonService<CachingDefaultInterceptor>();
        }

        #endregion

        #region Logging

        public void AddLogger(ILoggerProvider loggerProvider)
        {
            UraganoSettings.LoggerProviders.Add(loggerProvider);
        }

        #endregion

        #region Hosted service
        public void AddHostedService<THostedService>() where THostedService : class, IHostedService
        {
            if (ServiceCollection.Any(p => p.ServiceType == typeof(IHostedService) && p.ImplementationType == typeof(THostedService)))
                return;
            ServiceCollection.AddHostedService<THostedService>();
        }
        #endregion

        #region Private methods

        private void RegisterServerServices()
        {
            if (!RegisterSingletonService<ServerDefaultInterceptor>())
                return;
            RegisterSingletonService<IBootstrap, ServerBootstrap>();
            AddHostedService<BootstrapStartup>();
            var types = ReflectHelper.GetDependencyTypes();
            var services = types.Where(t => t.IsInterface && typeof(IService).IsAssignableFrom(t)).Select(@interface => new
            {
                Interface = @interface,
                Implementation = types.FirstOrDefault(p => p.IsClass && p.IsPublic && !p.IsAbstract && !p.Name.EndsWith("_____UraganoClientProxy") && @interface.IsAssignableFrom(p))
            }).Where(p => p.Implementation != null);

            foreach (var service in services)
            {
                //此处不使用接口来注册是避免同时启用服务器端和客户端冲突
                RegisterScopedService(service.Implementation);
                //RegisterScopedService(service.Interface, service.Implementation);
            }

            RegisterInterceptors();
        }


        private void RegisterClientServices()
        {
            if (!RegisterSingletonService<IClientFactory, ClientFactory>())
                return;
            AddHostedService<RemotingClientStartup>();
            RegisterSingletonService<ClientDefaultInterceptor>();
            RegisterSingletonService<IRemotingInvoke, RemotingInvoke>();
            RegisterSingletonService<ICircuitBreaker, PollyCircuitBreaker>();

            var types = ReflectHelper.GetDependencyTypes();
            var services = types.Where(t => t.IsInterface && typeof(IService).IsAssignableFrom(t)).ToList();

            //Generate client proxy
            var proxies = ProxyGenerator.GenerateProxy(services);
            foreach (var service in services)
            {
                //Register proxy class,For support meta data using scope.
                RegisterScopedService(service, proxies.FirstOrDefault(p => service.IsAssignableFrom(p)));
            }

            RegisterInterceptors();
        }

        private void RegisterInterceptors()
        {
            var interceptors = ReflectHelper.GetDependencyTypes().FindAll(t => typeof(IInterceptor).IsAssignableFrom(t));
            foreach (var interceptor in interceptors)
            {
                RegisterScopedService(interceptor);
            }
        }


        #endregion

        #region Dependency Injection
        private bool RegisterScopedService<TService, TImplementation>()
        {
            if (ServiceCollection.Any(p => p.ServiceType == typeof(TService) && p.ImplementationType == typeof(TImplementation)))
                return false;
            ServiceCollection.AddScoped(typeof(TService), typeof(TImplementation));
            return true;
        }

        private bool RegisterScopedService(Type serviceType, Type implementationType)
        {
            if (ServiceCollection.Any(p => p.ServiceType == serviceType && p.ImplementationType == implementationType))
                return false;
            ServiceCollection.AddScoped(serviceType, implementationType);
            return true;
        }

        private bool RegisterScopedService<TService>()
        {
            if (ServiceCollection.Any(p => p.ServiceType == typeof(TService)))
                return false;
            ServiceCollection.AddScoped(typeof(TService));
            return true;
        }

        private bool RegisterScopedService(Type serviceType)
        {
            if (ServiceCollection.Any(p => p.ServiceType == serviceType))
                return false;
            ServiceCollection.AddScoped(serviceType);
            return true;
        }



        private bool RegisterSingletonService<TService, TImplementation>()
        {
            if (ServiceCollection.Any(p => p.ServiceType == typeof(TService) && p.ImplementationType == typeof(TImplementation)))
                return false;
            ServiceCollection.AddSingleton(typeof(TService), typeof(TImplementation));
            return true;
        }

        private bool RegisterSingletonService(Type serviceType, Type implementationType)
        {
            if (ServiceCollection.Any(p => p.ServiceType == serviceType && p.ImplementationType == implementationType))
                return false;
            ServiceCollection.AddSingleton(serviceType, implementationType);
            return true;
        }

        private bool RegisterSingletonService<TService>()
        {
            if (ServiceCollection.Any(p => p.ServiceType == typeof(TService)))
                return false;
            ServiceCollection.AddSingleton(typeof(TService));
            return true;
        }

        private bool RegisterSingletonService(Type serviceType)
        {
            if (ServiceCollection.Any(p => p.ServiceType == serviceType))
                return false;
            ServiceCollection.AddSingleton(serviceType);
            return true;
        }
        #endregion
    }

    public class UraganoSampleBuilder : UraganoBuilder, IUraganoSampleBuilder
    {
        public IConfiguration Configuration { get; }

        public UraganoSampleBuilder(IServiceCollection serviceCollection, IConfiguration configuration) : base(serviceCollection)
        {
            Configuration = configuration;
        }

        public void AddServer()
        {
            AddServer(Configuration.GetSection("Uragano:Server"));
        }


        public void AddClient()
        {
            AddClient<LoadBalancingPolling>(Configuration.GetSection("Uragano:Client"));
        }

        public void AddClient<TLoadBalancing>() where TLoadBalancing : class, ILoadBalancing
        {
            AddClient<TLoadBalancing>(Configuration.GetSection("Uragano:Client"));

        }

        public void AddOptions()
        {
            AddOptions(Configuration.GetSection("Uragano:Options"));
        }

        public void AddCircuitBreaker()
        {
            AddCircuitBreaker(Configuration.GetSection("Uragano:CircuitBreaker:Polly"));
        }

        public void AddCircuitBreaker<TCircuitBreakerEvent>() where TCircuitBreakerEvent : ICircuitBreakerEvent
        {
            AddCircuitBreaker<TCircuitBreakerEvent>(Configuration.GetSection("Uragano:CircuitBreaker:Polly"));
        }
    }
}

