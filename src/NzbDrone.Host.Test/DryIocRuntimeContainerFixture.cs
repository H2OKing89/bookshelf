using System;
using System.Linq;
using DryIoc;
using DryIoc.Microsoft.DependencyInjection;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using NzbDrone.Common.Composition.Extensions;

namespace NzbDrone.App.Test
{
    [TestFixture]
    public class DryIocRuntimeContainerFixture
    {
        [Test]
        public void runtime_adapter_preserves_singleton_scoped_and_transient_lifetimes()
        {
            var services = new ServiceCollection();
            services.AddSingleton<SingletonProbe>();
            services.AddScoped<ScopedProbe>();
            services.AddTransient<TransientProbe>();

            var provider = BuildProvider(services);
            SingletonProbe singleton = null;
            ScopedProbe firstScoped;
            TransientProbe firstTransient;
            TransientProbe secondTransient;

            using ((IDisposable)provider)
            {
                using (var first = provider.CreateScope())
                {
                    var factory = first.ServiceProvider;
                    singleton = factory.GetRequiredService<SingletonProbe>();
                    firstScoped = factory.GetRequiredService<ScopedProbe>();
                    firstTransient = factory.GetRequiredService<TransientProbe>();
                    secondTransient = factory.GetRequiredService<TransientProbe>();

                    factory.GetRequiredService<SingletonProbe>().Should().BeSameAs(singleton);
                    factory.GetRequiredService<ScopedProbe>().Should().BeSameAs(firstScoped);
                    firstTransient.Should().NotBeSameAs(secondTransient);
                    firstScoped.DisposeCount.Should().Be(0);
                }

                firstScoped.DisposeCount.Should().Be(1);
                firstTransient.DisposeCount.Should().Be(1);
                secondTransient.DisposeCount.Should().Be(1);
                singleton.DisposeCount.Should().Be(0);

                ScopedProbe secondScoped;
                using (var second = provider.CreateScope())
                {
                    second.ServiceProvider.GetRequiredService<SingletonProbe>().Should().BeSameAs(singleton);
                    secondScoped = second.ServiceProvider.GetRequiredService<ScopedProbe>();
                    secondScoped.Should().NotBeSameAs(firstScoped);
                }

                secondScoped.DisposeCount.Should().Be(1);
                singleton.DisposeCount.Should().Be(0);
            }

            singleton.DisposeCount.Should().Be(1);
            firstScoped.DisposeCount.Should().Be(1);
            firstTransient.DisposeCount.Should().Be(1);
        }

        [Test]
        public void runtime_adapter_preserves_last_registration_and_ordered_collection_resolution()
        {
            var services = new ServiceCollection();
            services.AddSingleton<IProbeLabel, FirstProbeLabel>();
            services.AddSingleton<IProbeLabel, LastProbeLabel>();

            var provider = BuildProvider(services);
            using ((IDisposable)provider)
            {
                var labels = provider.GetServices<IProbeLabel>().ToArray();
                labels.Select(label => label.Value).Should().Equal("first", "last");
                provider.GetRequiredService<IProbeLabel>().Should().BeSameAs(labels[1]);
                provider.GetService<IMissingProbe>().Should().BeNull();
            }
        }

        private static IServiceProvider BuildProvider(IServiceCollection services)
        {
            var factory = new DryIocServiceProviderFactory(new Container(rules => rules.WithNzbDroneRules()));
            return factory.CreateServiceProvider(factory.CreateBuilder(services));
        }

        public abstract class DisposableProbe : IDisposable
        {
            public int DisposeCount { get; private set; }

            public void Dispose()
            {
                DisposeCount++;
            }
        }

        public sealed class SingletonProbe : DisposableProbe
        {
        }

        public sealed class ScopedProbe : DisposableProbe
        {
        }

        public sealed class TransientProbe : DisposableProbe
        {
        }

        public interface IProbeLabel
        {
            string Value { get; }
        }

        public sealed class FirstProbeLabel : IProbeLabel
        {
            public string Value => "first";
        }

        public sealed class LastProbeLabel : IProbeLabel
        {
            public string Value => "last";
        }

        public interface IMissingProbe
        {
        }
    }
}
