using System;
using System.Collections.Generic;
using System.Linq;
using Dapper;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.ImportLists;
using NzbDrone.Core.ImportLists.Readarr;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.ImportListTests
{
    public class ImportListMembershipRepositoryFixture : DbTest<ImportListMembershipRepository, ImportListDefinition>
    {
        private ImportListDefinition _first;
        private ImportListDefinition _second;

        [SetUp]
        public void Setup()
        {
            _first = AddList("First");
            _second = AddList("Second");
        }

        [Test]
        public void should_replace_only_the_successful_list_and_keep_exact_recordings()
        {
            Subject.Get(_first.Id).LastSuccessfulSync.Should().BeNull();
            Subject.Complete(Subject.Begin(_first), Entries("us", "uk")).Should().BeTrue();
            Subject.Complete(Subject.Begin(_second), Entries("uk")).Should().BeTrue();
            var snapshot = Subject.Get(_first.Id);
            snapshot.IsCurrent.Should().BeTrue();
            snapshot.Items.Select(x => x.BookId).Should().Equal("audible:us:B000000002", "audible:uk:B000000002");
            var failed = Subject.Begin(_first);
            failed.Should().NotBeNull();
            Subject.Get(_first.Id).LastSuccessfulSync.Should().Be(snapshot.LastSuccessfulSync);
            Subject.Get(_first.Id).Items.Should().HaveCount(2);
            Subject.Complete(Subject.Begin(_first), Array.Empty<ImportListMembershipItem>()).Should().BeTrue();
            Subject.Get(_first.Id).Items.Should().BeEmpty();
            Subject.Get(_first.Id).LastSuccessfulSync.Should().NotBeNull();
            Subject.Get(_second.Id).Items.Should().ContainSingle();
        }

        [Test]
        public void should_reject_late_duplicate_and_superseded_attempts()
        {
            var older = Subject.Begin(_first);
            var newer = Subject.Begin(_first);
            Subject.Complete(older, Entries("us")).Should().BeFalse();
            Subject.Complete(newer, Entries("uk")).Should().BeTrue();
            Subject.Complete(newer, Entries("us")).Should().BeFalse();
            Subject.Get(_first.Id).Items.Single().BookId.Should().Be("audible:uk:B000000002");
        }

        [Test]
        public void should_keep_previous_snapshot_but_reject_attempts_across_edits_and_disabling()
        {
            Subject.Complete(Subject.Begin(_first), Entries("us")).Should().BeTrue();
            var pending = Subject.Begin(_first);
            var repository = Mocker.Resolve<ImportListRepository>();
            _first.EnableAutomaticAdd = false;
            repository.Update(_first);
            Subject.Get(_first.Id).IsCurrent.Should().BeFalse();
            Subject.Begin(_first).Should().BeNull();
            Subject.Complete(pending, Entries("uk")).Should().BeFalse();
            _first.EnableAutomaticAdd = true;
            repository.Update(_first);
            Subject.Complete(pending, Entries("uk")).Should().BeFalse();
            Subject.Begin(_first).Should().BeNull("old definition revisions must not start an attempt");
            var current = repository.Get(_first.Id);
            current.MembershipRevision.Should().Be(2);
            Subject.Get(_first.Id).Items.Single().BookId.Should().Be("audible:us:B000000002");
            Subject.Complete(Subject.Begin(current), Entries("uk")).Should().BeTrue();
            Subject.Get(_first.Id).IsCurrent.Should().BeTrue();
        }

        [Test]
        public void should_remove_deleted_list_membership_and_prevent_late_recreation()
        {
            Subject.Complete(Subject.Begin(_first), Entries("us")).Should().BeTrue();
            Subject.Complete(Subject.Begin(_second), Entries("uk")).Should().BeTrue();
            var pending = Subject.Begin(_first);
            Mocker.Resolve<ImportListRepository>().Delete(_first.Id);
            Subject.Complete(pending, Entries("uk")).Should().BeFalse();
            Assert.Throws<ModelNotFoundException>(() => Subject.Get(_first.Id));
            Subject.Get(_second.Id).Items.Should().ContainSingle();
            using var connection = Mocker.Resolve<IMainDatabase>().OpenConnection();
            connection.ExecuteScalar<int>("SELECT COUNT(*) FROM \"ImportListMembershipSnapshots\"").Should().Be(1);
            connection.ExecuteScalar<int>("SELECT COUNT(*) FROM \"ImportListMembershipItems\"").Should().Be(1);
        }

        [Test]
        public void should_keep_raw_membership_on_exclusion_and_remove_exclusion_dynamically()
        {
            Subject.Complete(Subject.Begin(_first), Entries("us", "uk")).Should().BeTrue();
            using var connection = Mocker.Resolve<IMainDatabase>().OpenConnection();
            connection.Execute("INSERT INTO \"ImportListExclusions\" (\"ForeignId\", \"Name\") VALUES (@Id, 'Owned exclusion')", new { Id = "audible:us:B000000002" });
            var snapshot = Subject.Get(_first.Id);
            snapshot.Items.Should().HaveCount(2);
            snapshot.Items.Single(item => item.BookId.StartsWith("audible:us:", StringComparison.Ordinal)).Excluded.Should().BeTrue();
            snapshot.Items.Single(item => item.BookId.StartsWith("audible:uk:", StringComparison.Ordinal)).Excluded.Should().BeFalse();
            connection.Execute("DELETE FROM \"ImportListExclusions\"");
            Subject.Get(_first.Id).Items.Should().OnlyContain(item => !item.Excluded);
        }

        private ImportListDefinition AddList(string name)
        {
            var definition = new ImportListDefinition
            {
                Name = name,
                Implementation = "ReadarrImport",
                Settings = new ReadarrSettings { BaseUrl = "http://127.0.0.1:9000", ApiKey = "fixture" },
                EnableAutomaticAdd = true,
                RootFolderPath = "/owned",
                ProfileId = 1,
                MetadataProfileId = 1
            };
            return Mocker.Resolve<ImportListRepository>().Insert(definition);
        }

        private static List<ImportListMembershipItem> Entries(params string[] regions) => regions.Select(region => new ImportListMembershipItem
        {
            SourceTitle = "Same title",
            SourceAuthor = "Same author",
            SourceBookId = $"audible:{region}:B000000002",
            SourceEditionId = $"audible:{region}:B000000002",
            SourceAuthorId = $"audible:{region}:B000000001",
            BookId = $"audible:{region}:B000000002",
            EditionId = $"audible:{region}:B000000002",
            AuthorId = $"audible:{region}:B000000001"
        }).ToList();
    }
}
