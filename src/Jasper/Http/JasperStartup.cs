﻿using System;
using System.Linq;
using Baseline;
using BlueMilk;
using Jasper.Http.Routing;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace Jasper.Http
{
    internal class JasperStartup : IStartup
    {

        public static IStartup Build(IServiceProvider provider, ServiceDescriptor descriptor)
        {
            if (descriptor.ImplementationInstance != null)
            {
                return descriptor.ImplementationInstance.As<IStartup>();
            }

            if (descriptor.ImplementationType != null)
            {
                return provider.GetService(descriptor.ServiceType).As<IStartup>();
            }

            return descriptor.ImplementationFactory(provider).As<IStartup>();
        }

        public static Func<IServiceProvider, IStartup> Build(ServiceDescriptor service)
        {
            return sp => Build(sp, service);
        }

        public static void Register(Container container, IServiceCollection services, Router router)
        {
            var startups = services
                .Where(x => x.ServiceType == typeof(IStartup))
                .Select(Build)
                .ToArray();

            services.AddTransient<IStartup>(sp =>
            {
                var others = startups.Select(x => x(sp)).ToArray();
                return new JasperStartup(container, others, router);
            });
        }

        private readonly Container _container;
        private readonly IStartup[] _others;
        private readonly Router _router;

        public JasperStartup(Container container, IStartup[] others, Router router)
        {
            _container = container;
            _others = others;
            _router = router ?? throw new ArgumentNullException(nameof(router));
        }

        public IServiceProvider ConfigureServices(IServiceCollection services)
        {
            foreach (var startup in _others)
            {
                startup.ConfigureServices(services);
            }

            var registry = new ServiceRegistry();
            registry.AddRange(services);


            //_container.Configure(x => x.AddRegistry(registry));

            return _container;
        }

        public void Configure(IApplicationBuilder app)
        {
            app.StoreRouter(_router);

            foreach (var startup in _others)
            {
                startup.Configure(app);
            }

            if (!app.HasJasperBeenApplied())
            {
                app.Run(_router.Invoke);
            }
        }
    }



}
