﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Baseline.Dates;
using Jasper.EnvironmentChecks;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Jasper.Testing.EnvironmentChecks
{
    [Collection("integration")]
    public class EnvironmentChecksProcessing
    {
        [Fact]
        public void do_not_fail_if_advanced_says_not_to_blow_up()
        {
            using (var runtime = JasperRuntime.For(_ =>
            {
                _.Handlers.DisableConventionalDiscovery();
                _.EnvironmentChecks.Register<NegativeCheck>();
                _.Advanced.ThrowOnValidationErrors = false;
            }))
            {
            }
        }


        [Fact]
        public void fail_on_startup_with_negative_check()
        {
            var aggregate = Exception<AggregateException>.ShouldBeThrownBy(() =>
            {
                using (var runtime = JasperRuntime.For(_ =>
                {
                    _.Handlers.DisableConventionalDiscovery();

                    _.EnvironmentChecks.Register<NegativeCheck>();
                }))
                {
                }
            });

            aggregate.InnerExceptions.Single().Message
                .ShouldContain("Kaboom!");
        }

        [Fact]
        public void fail_with_lambda_check()
        {
            var aggregate = Exception<AggregateException>.ShouldBeThrownBy(() =>
            {
                using (var runtime = JasperRuntime.For(_ =>
                {
                    _.Handlers.DisableConventionalDiscovery();

                    _.EnvironmentChecks.Register("Bazinga!", () => throw new Exception("Bang"));
                }))
                {
                }
            });

            aggregate.InnerExceptions.Single().Message
                .ShouldContain("Bang");
        }

        [Fact]
        public void fail_with_lambda_check_with_service()
        {
            var aggregate = Exception<AggregateException>.ShouldBeThrownBy(() =>
            {
                using (var runtime = JasperRuntime.For(_ =>
                {
                    _.Handlers.DisableConventionalDiscovery();

                    _.EnvironmentChecks.Register<Thing>("Bazinga!", t => t.ThrowUp());
                }))
                {
                }
            });
        }

        [Fact]
        public void finds_checks_that_were_not_registered_as_environment_check()
        {
            var aggregate = Exception<AggregateException>.ShouldBeThrownBy(() =>
            {
                using (var runtime = JasperRuntime.For(_ =>
                {
                    _.Handlers.DisableConventionalDiscovery();
                    _.Services.AddTransient<ISomeService, BadService>();
                }))
                {
                }
            });

            aggregate.InnerExceptions.Single().Message.ShouldContain("I'm bad!");
        }

        [Fact]
        public void succeed_with_lambda_check()
        {
            using (var runtime = JasperRuntime.For(_ =>
            {
                _.Handlers.DisableConventionalDiscovery();

                _.EnvironmentChecks.Register("Bazinga!", () => { });
            }))
            {
            }
        }

        [Fact]
        public void succeed_with_lambda_check_using_service()
        {
            using (var runtime = JasperRuntime.For(_ =>
            {
                _.Handlers.DisableConventionalDiscovery();

                _.EnvironmentChecks.Register<Thing>("Bazinga!", t => t.AllGood());
            }))
            {
            }
        }

        [Fact]
        public void timeout_on_task()
        {
            var aggregate = Exception<AggregateException>.ShouldBeThrownBy(() =>
            {
                using (var runtime = JasperRuntime.For(_ =>
                {
                    _.Handlers.DisableConventionalDiscovery();

                    _.EnvironmentChecks.Register<Thing>("Bazinga!", t => t.TooLong(), 50.Milliseconds());
                }))
                {
                }
            });
        }
    }

    public class Thing
    {
        public void ThrowUp()
        {
            throw new Exception("I threw up");
        }

        public void AllGood()
        {
        }

        public Task TooLong()
        {
            return Task.Delay(3.Seconds());
        }
    }

    public interface ISomeService
    {
    }

    public class SomeService1 : ISomeService, IEnvironmentCheck
    {
        public void Assert(JasperRuntime runtime)
        {
            // all good
        }

        public string Description { get; } = "SomeService1";
    }

    public class BadService : ISomeService, IEnvironmentCheck
    {
        public void Assert(JasperRuntime runtime)
        {
            throw new Exception("I'm bad!");
        }

        public string Description { get; } = "BadService";
    }

    public class PositiveCheck : IEnvironmentCheck
    {
        public void Assert(JasperRuntime runtime)
        {
            // all good
        }

        public string Description { get; } = "PositiveCheck";
    }

    public class NegativeCheck : IEnvironmentCheck
    {
        public void Assert(JasperRuntime runtime)
        {
            throw new Exception("Kaboom!");
        }

        public string Description { get; } = "NegativeCheck";
    }

    public class StubEnvironmentRecorder : IEnvironmentRecorder
    {
        public readonly IDictionary<string, Exception> Failures = new Dictionary<string, Exception>();
        public readonly IList<string> Successes = new List<string>();

        public bool AssertAllWasCalled { get; set; }

        public void Success(string description)
        {
            Successes.Add(description);
        }

        public void Failure(string description, Exception exception)
        {
            Failures.Add(description, exception);
        }

        public void AssertAllSuccessful()
        {
            AssertAllWasCalled = true;
        }
    }

    // SAMPLE: registering-environment-checks
    public class AppWithEnvironmentChecks : JasperRegistry
    {
        public AppWithEnvironmentChecks()
        {
            // Register an IEnvironmentCheck object
            EnvironmentChecks.Register(new FileExistsCheck("settings.json"));

            // or declaratively say a file should exist (this is just syntactic sugar)
            EnvironmentChecks.FileShouldExist("settings.json");

            // or do it manually w/ a lambda
            EnvironmentChecks.Register("settings.json can be found", () =>
            {
                if (!File.Exists("settings.json")) throw new Exception("File cannot be found");
            });

            // Or register a check type
            EnvironmentChecks.Register<CustomCheck>();

            // The concrete Store class exposes IEnvironmentCheck
            Services.AddTransient<IStore, Store>();
        }
    }

    public interface IStore
    {
    }

    // Jasper will still use this as an environment check
    public class Store : IStore, IEnvironmentCheck
    {
        public void Assert(JasperRuntime runtime)
        {
            // do the assertion of valid state
        }

        public string Description { get; } = "Fake Store Environment Check";



    }
    // ENDSAMPLE


    public class CustomCheck : IEnvironmentCheck
    {
        public void Assert(JasperRuntime runtime)
        {
            // do something here
        }

        public string Description { get; } = "Some Description";
    }
}
