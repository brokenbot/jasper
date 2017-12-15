using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Baseline;
using Baseline.Dates;
using Baseline.Reflection;
using Jasper.Bus;
using Jasper.Bus.Logging;
using Jasper.Bus.Runtime.Subscriptions;
using Jasper.Bus.Transports;
using Jasper.Bus.Transports.Configuration;
using Jasper.Configuration;
using Jasper.EnvironmentChecks;
using Jasper.Http;
using Jasper.Internals.Codegen;
using Jasper.Util;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using StructureMap;
using StructureMap.Graph;
using StructureMap.Pipeline;
using ServiceCollectionExtensions = Jasper.Internals.Scanning.Conventions.ServiceCollectionExtensions;

namespace Jasper
{
    /// <summary>
    /// Strictly used to override the ASP.Net Core environment name on bootstrapping
    /// </summary>
    public static class JasperEnvironment
    {
        public static string Name { get; set; }
    }

    internal class TransportsAreSingletons : StructureMap.IInstancePolicy
    {
        public void Apply(Type pluginType, Instance instance)
        {
            if (pluginType == typeof(ITransport))
            {
                instance.SetLifecycleTo(Lifecycles.Singleton);
            }
        }
    }

    public class JasperRuntime : IDisposable
    {
        private bool isDisposing;

        private JasperRuntime(JasperRegistry registry, IServiceCollection services)
        {
            services.AddSingleton(this);

            Services = services.ToImmutableArray();

            Container = new Container(_ =>
            {
                _.Populate(services);
                _.For<ITransport>().Singleton();
                _.Policies.Add<TransportsAreSingletons>();

                _.For<CancellationToken>().Use(c => c.GetInstance<BusSettings>().Cancellation);
            })
            {
                DisposalLock = DisposalLock.Ignore
            };

            registry.Generation.Sources.Add(new NowTimeVariableSource());

            registry.Generation.Assemblies.Add(GetType().GetTypeInfo().Assembly);
            registry.Generation.Assemblies.Add(registry.ApplicationAssembly);

            Registry = registry;

            _bus = new Lazy<IServiceBus>(Get<IServiceBus>);

        }

        internal JasperRegistry Registry { get; }

        /// <summary>
        /// The known service registrations to the underlying IoC container
        /// </summary>
        public ImmutableArray<ServiceDescriptor> Services { get; }

        /// <summary>
        /// The running IWebHost for this applicastion
        /// </summary>
        public IWebHost Host => Registry.Features.For<AspNetCoreFeature>().Host;

        /// <summary>
        /// The main application assembly for the running application
        /// </summary>
        public Assembly ApplicationAssembly => Registry.ApplicationAssembly;

        /// <summary>
        /// The underlying StructureMap container
        /// </summary>
        public IContainer Container { get; }

        public bool IsDisposed { get; private set; }

        /// <summary>
        /// Summary of all the message handling, subscription, and message publishing
        /// capabilities of the running Jasper application
        /// </summary>
        public ServiceCapabilities Capabilities { get; internal set; }

        public void Dispose()
        {
            // Because StackOverflowException's are a drag
            if (IsDisposed || isDisposing) return;

            try
            {
                Get<INodeDiscovery>().UnregisterLocalNode().Wait(3.Seconds());
            }
            catch (Exception e)
            {
                Console.WriteLine($"Unable to un-register the running node");
                Get<CompositeLogger>().LogException(e);
            }

            Get<BusSettings>().StopAll();

            isDisposing = true;


            foreach (var feature in Registry.Features)
            {
                feature.Dispose();
            }

            Container.DisposalLock = DisposalLock.Unlocked;
            Container.Dispose();

            IsDisposed = true;
        }

        /// <summary>
        /// Creates a Jasper application for the current executing assembly
        /// using all the default Jasper configurations
        /// </summary>
        /// <returns></returns>
        public static JasperRuntime Basic()
        {
            return bootstrap(new JasperRegistry()).GetAwaiter().GetResult();
        }

        /// <summary>
        /// Builds and initializes a JasperRuntime for the registry
        /// </summary>
        /// <param name="registry"></param>
        /// <returns></returns>
        public static JasperRuntime For(JasperRegistry registry)
        {
            return bootstrap(registry).GetAwaiter().GetResult();
        }

        /// <summary>
        /// Builds and initializes a JasperRuntime for the JasperRegistry of
        /// type T
        /// </summary>
        /// <param name="configure"></param>
        /// <typeparam name="T">The type of your JasperRegistry</typeparam>
        /// <returns></returns>
        public static JasperRuntime For<T>(Action<T> configure = null) where T : JasperRegistry, new()
        {
            var registry = new T();
            configure?.Invoke(registry);

            return bootstrap(registry).GetAwaiter().GetResult();
        }

        /// <summary>
        /// Builds and initializes a JasperRuntime for the configured JasperRegistry
        /// </summary>
        /// <param name="configure"></param>
        /// <returns></returns>
        public static JasperRuntime For(Action<JasperRegistry> configure)
        {
            var registry = new JasperRegistry();
            configure?.Invoke(registry);

            return bootstrap(registry).GetAwaiter().GetResult();
        }

        /// <summary>
        ///     Shorthand to fetch a service from the application container by type
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        public T Get<T>()
        {
            return Container.GetInstance<T>();
        }

        /// <summary>
        /// Shorthand to fetch a service from the application container by type
        /// </summary>
        /// <param name="type"></param>
        /// <returns></returns>
        public object Get(Type type)
        {
            return Container.GetInstance(type);
        }


        private static async Task<JasperRuntime> bootstrap(JasperRegistry registry)
        {
            applyExtensions(registry);

            registry.Settings.Bootstrap();

            var features = registry.Features;

            var serviceRegistries = await Task.WhenAll(features.Select(x => x.Bootstrap(registry)))
                .ConfigureAwait(false);

            var collections = new List<IServiceCollection>();
            collections.AddRange(serviceRegistries);
            collections.Add(registry.ExtensionServices);
            collections.Add(registry.Services);


            var services = await ServiceCollectionExtensions.Combine(collections.ToArray());
            registry.Generation.ReadServices(services);


            var runtime = new JasperRuntime(registry, services);
            registry.Http.As<IWebHostBuilder>().UseSetting(WebHostDefaults.ApplicationKey, registry.ApplicationAssembly.FullName);

            runtime.HttpAddresses = registry.Http.As<IWebHostBuilder>().GetSetting(WebHostDefaults.ServerUrlsKey);

            await Task.WhenAll(features.Select(x => x.Activate(runtime, registry.Generation)))
                .ConfigureAwait(false);

            // Run environment checks
            var recorder = EnvironmentChecker.ExecuteAll(runtime);
            if (runtime.Get<BusSettings>().ThrowOnValidationErrors)
            {
                recorder.AssertAllSuccessful();
            }

            await registerRunningNode(runtime);

            return runtime;
        }

        private static async Task registerRunningNode(JasperRuntime runtime)
        {
            // TODO -- get a helper for this, it's ugly
            var settings = runtime.Get<BusSettings>();
            var nodes = runtime.Get<INodeDiscovery>();

            try
            {

                var local = new ServiceNode(settings);
                local.HttpEndpoints = runtime.HttpAddresses?.Split(';').Select(x => x.ToUri().ToMachineUri()).Distinct().ToArray();

                runtime.Node = local;

                await nodes.Register(local);
            }
            catch (Exception e)
            {
                runtime.Get<CompositeLogger>().LogException(e, message: "Failure when trying to register the node with " + nodes);
            }
        }

        public string HttpAddresses { get; internal set; }

        private static void applyExtensions(JasperRegistry registry)
        {
            var assemblies = AssemblyFinder
                .FindAssemblies(a => a.HasAttribute<JasperModuleAttribute>())
                .ToArray();

            if (!assemblies.Any()) return;

            var extensions = assemblies
                .Select(x => x.GetAttribute<JasperModuleAttribute>().ExtensionType)
                .Select(x => Activator.CreateInstance(x).As<IJasperExtension>())
                .ToArray();

            registry.ApplyExtensions(extensions);
        }

        /// <summary>
        /// Writes a textual report about the configured transports and servers
        /// for this application
        /// </summary>
        /// <param name="writer"></param>
        public void Describe(TextWriter writer)
        {
            writer.WriteLine($"Running service '{ServiceName}'");
            if (ApplicationAssembly != null)
            {
                writer.WriteLine("Application Assembly: " + ApplicationAssembly.FullName);
            }

            var hosting = Get<IHostingEnvironment>();
            writer.WriteLine($"Hosting environment: {hosting.EnvironmentName}");
            writer.WriteLine($"Content root path: {hosting.ContentRootPath}");


            foreach (var feature in Registry.Features)
            {
                feature.Describe(this, writer);
            }
        }

        private readonly Lazy<IServiceBus> _bus;

        /// <summary>
        /// Shortcut to retrieve an instance of the IServiceBus interface for the application
        /// </summary>
        public IServiceBus Bus => _bus.Value;

        /// <summary>
        /// The logical name of the application from JasperRegistry.ServiceName
        /// </summary>
        public string ServiceName => Registry.ServiceName;

        /// <summary>
        /// Information about the running service node as published to service discovery
        /// </summary>
        public IServiceNode Node { get; internal set; }

    }
}


