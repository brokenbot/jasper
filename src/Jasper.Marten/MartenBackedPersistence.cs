using Jasper.Bus.Runtime;
using Jasper.Bus.Transports;
using Jasper.Configuration;
using Jasper.Marten.Persistence;
using Jasper.Marten.Persistence.Resiliency;
using Marten;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Jasper.Marten
{

    /// <summary>
    /// Opts into using Marten as the backing message store
    /// </summary>
    public class MartenBackedPersistence : IJasperExtension
    {
        public void Configure(JasperRegistry registry)
        {
            // Override an OOTB service in Jasper
            registry.Services.AddSingleton<IPersistence, MartenBackedMessagePersistence>();

            // Jasper works *with* ASP.Net Core, even without a web server,
            // so you can use their IHostedService model for long running tasks
            registry.Services.AddSingleton<IHostedService, SchedulingAgent>();

            // Customizes the Marten integration a little bit with
            // some custom schema objects this extension needs
            registry.Settings.ConfigureMarten(options =>
            {
                options.Storage.Add<PostgresqlEnvelopeStorage>();
            });
        }
    }
}
