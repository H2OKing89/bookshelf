using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Authentication;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Datastore.Migration.Framework;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Test.Datastore.Migration;

namespace NzbDrone.Core.Test.Authentication
{
    [TestFixture]
    [NonParallelizable]
    public class AccountRenameConcurrencyFixture : MigrationCompatibilityTestBase
    {
        public AccountRenameConcurrencyFixture()
            : base(DatabaseType.PostgreSQL)
        {
        }

        [Test]
        public async Task concurrent_account_renames_should_not_assign_the_same_username_to_two_identities()
        {
            Migrate(MigrationType.Main);
            var database = new MainDatabase(new Database("Owned account rename proof", () => Open()));
            var repository = new UserRepository(database, Mock.Of<IEventAggregator>());
            var first = repository.Insert(new User { Identifier = Guid.NewGuid(), Username = "first", Password = "owned first hash" });
            var second = repository.Insert(new User { Identifier = Guid.NewGuid(), Username = "second", Password = "owned second hash" });
            var firstUpdate = new User { Username = "requested" };
            var secondUpdate = new User { Username = "requested" };
            using var barrier = new CountdownEvent(2);
            using var blocker = Open();
            blocker.Execute("BEGIN; SELECT pg_catalog.pg_advisory_xact_lock(1800133326)");
            var attempts = new[] { (first, firstUpdate), (second, secondUpdate) }.Select(pair => Task.Run(() =>
            {
                barrier.Signal();
                return repository.TryUpdateCredentials(pair.Item1, pair.Item2, true, false);
            })).ToArray();
            try
            {
                barrier.Wait(TimeSpan.FromSeconds(5)).Should().BeTrue();
                await Task.Delay(100);
                blocker.Execute("COMMIT");
                var updated = await Task.WhenAll(attempts).WaitAsync(TimeSpan.FromSeconds(10));

                updated.Count(value => value).Should().Be(1);
                repository.All().Count(user => user.Username == "requested").Should().Be(1);
                repository.Count().Should().Be(2);
                repository.Get(first.Id).Identifier.Should().Be(first.Identifier);
                repository.Get(second.Id).Identifier.Should().Be(second.Identifier);
            }
            finally
            {
                // Releasing this connection releases an owned advisory lock even
                // if an assertion fails before the explicit COMMIT.
                blocker.Close();
                await Task.WhenAll(attempts).WaitAsync(TimeSpan.FromSeconds(10));
            }
        }
    }
}
