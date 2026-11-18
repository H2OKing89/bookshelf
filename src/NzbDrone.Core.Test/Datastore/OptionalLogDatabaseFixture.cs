using DryIoc;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Datastore.Extensions;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Test.Datastore
{
    [TestFixture]
    public class OptionalLogDatabaseFixture
    {
        [TestCase(false)]
        [TestCase(true)]
        public void log_storage_selection_should_leave_main_and_cache_databases_registered(bool enabled)
        {
            var main = new Mock<IDatabase>();
            var cache = new Mock<IDatabase>();
            var log = new Mock<IDatabase>();
            var factory = new Mock<IDbFactory>(MockBehavior.Strict);
            factory.Setup(item => item.Create(MigrationType.Main)).Returns(main.Object);
            factory.Setup(item => item.Create(MigrationType.Cache)).Returns(cache.Object);
            if (enabled)
            {
                factory.Setup(item => item.Create(MigrationType.Log)).Returns(log.Object);
            }

            using var container = new Container();
            container.RegisterInstance(factory.Object);
            container.AddDatabase(enabled);
            container.Resolve<IMainDatabase>().Should().NotBeNull();
            container.Resolve<ICacheDatabase>().Should().NotBeNull();
            var effectiveLog = container.Resolve<ILogDatabase>();
            effectiveLog.IsEnabled.Should().Be(enabled);
            container.Resolve<ILogDatabase>().Should().BeSameAs(effectiveLog);
            factory.Verify(item => item.Create(MigrationType.Main), Times.Once());
            factory.Verify(item => item.Create(MigrationType.Cache), Times.Once());
            factory.Verify(item => item.Create(MigrationType.Log), enabled ? Times.Once() : Times.Never());
        }

        [Test]
        public void existing_parameterless_registration_should_keep_sql_logs_enabled()
        {
            var factory = new Mock<IDbFactory>();
            factory.Setup(item => item.Create(It.IsAny<MigrationType>())).Returns(new Mock<IDatabase>().Object);
            using var container = new Container();
            container.RegisterInstance(factory.Object);

            container.AddDatabase();

            container.Resolve<ILogDatabase>().IsEnabled.Should().BeTrue();
            factory.Verify(item => item.Create(MigrationType.Log), Times.Once());
        }
    }
}
