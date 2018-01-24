using System;
using System.Threading.Tasks;
using BlueMilk;
using Jasper.Bus;
using Jasper.Bus.Model;
using Jasper.Bus.Runtime;
using Jasper.Bus.Runtime.Invocation;
using Xunit;

namespace Jasper.Testing.Bus.Compilation
{
    [Collection("compilation")]
    public abstract class CompilationContext : IDisposable
    {
        public CompilationContext()
        {
            theRegistry.Handlers.DisableConventionalDiscovery();
            _runtime = new Lazy<JasperRuntime>(() => { return JasperRuntime.For(theRegistry); });
        }

        public void Dispose()
        {
            if (_runtime.IsValueCreated) _runtime.Value.Dispose();
        }

        private Lazy<IContainer> _container;


        protected Envelope theEnvelope;


        public readonly JasperRegistry theRegistry = new JasperRegistry();
        private readonly Lazy<JasperRuntime> _runtime;


        public IContainer Container => _container.Value;

        public HandlerGraph Graph => _runtime.Value.Get<HandlerGraph>();

        public void AllHandlersCompileSuccessfully()
        {
            Graph.Chains.Length.ShouldBeGreaterThan(0);
        }

        public MessageHandler HandlerFor<TMessage>()
        {
            return Graph.HandlerFor(typeof(TMessage));
        }

        public async Task<IInvocationContext> Execute<TMessage>(TMessage message)
        {
            var handler = HandlerFor<TMessage>();
            theEnvelope = new Envelope(message);
            var context = new EnvelopeContext(null, theEnvelope, _runtime.Value.Get<IServiceBus>());

            await handler.Handle(context);

            return context;
        }

        [Fact]
        public void can_compile_all()
        {
            AllHandlersCompileSuccessfully();
        }
    }
}
