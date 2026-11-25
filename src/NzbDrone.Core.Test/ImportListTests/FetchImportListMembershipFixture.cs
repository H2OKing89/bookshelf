using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.ImportLists;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.ImportListTests
{
    public class FetchImportListMembershipFixture : CoreTest<FetchAndParseImportListService>
    {
        private ImportListDefinition _definition;
        private Mock<IImportList> _provider;

        [SetUp]
        public void Setup()
        {
            _definition = new ImportListDefinition { Id = 1, Name = "Owned list", Implementation = "ReadarrImport", EnableAutomaticAdd = true };
            _provider = new Mock<IImportList>();
            _provider.SetupGet(provider => provider.Definition).Returns(_definition);
            Mocker.GetMock<IImportListFactory>().Setup(factory => factory.GetInstance(_definition)).Returns(_provider.Object);
            Mocker.GetMock<IImportListFactory>().Setup(factory => factory.AutomaticAddEnabled(It.IsAny<bool>())).Returns(new List<IImportList> { _provider.Object });
            Mocker.GetMock<IImportListMembershipRepository>().Setup(repo => repo.Begin(_definition)).Returns(new ImportListMembershipAttempt(1, 0, "owned"));
        }

        [Test]
        public void should_preserve_exact_ids_instead_of_deduplicating_titles()
        {
            _provider.Setup(provider => provider.FetchWithStatus()).Returns(new ImportListFetchResult(Entries(), true));
            var batches = Subject.Fetch();
            batches.Should().ContainSingle();
            batches.Single().Items.Should().HaveCount(2);
            batches.Single().Items.Should().OnlyContain(item => item.ImportListId == 1);
        }

        [Test]
        public void should_return_a_successful_empty_batch()
        {
            _provider.Setup(provider => provider.FetchWithStatus()).Returns(new ImportListFetchResult(Array.Empty<ImportListItemInfo>(), true));
            Subject.FetchSingleList(_definition).Single().Items.Should().BeEmpty();
        }

        [Test]
        public void should_not_return_partial_results()
        {
            _provider.Setup(provider => provider.FetchWithStatus()).Returns(new ImportListFetchResult(Entries(), false));
            Subject.Fetch().Should().BeEmpty();
            Subject.FetchSingleList(_definition).Should().BeEmpty();
        }

        [Test]
        public void should_not_request_disabled_or_changed_definition()
        {
            _definition.EnableAutomaticAdd = false;
            Subject.FetchSingleList(_definition).Should().BeEmpty();
            _definition.EnableAutomaticAdd = true;
            Mocker.GetMock<IImportListMembershipRepository>().Setup(repo => repo.Begin(_definition)).Returns((ImportListMembershipAttempt)null);
            Subject.FetchSingleList(_definition).Should().BeEmpty();
            _provider.Verify(provider => provider.FetchWithStatus(), Times.Never);
        }

        private static List<ImportListItemInfo> Entries() => new[] { "us", "uk" }.Select(region => new ImportListItemInfo
        {
            Author = "Same author", Book = "Same title", BookGoodreadsId = $"audible:{region}:B000000002", EditionGoodreadsId = $"audible:{region}:B000000002"
        }).ToList();
    }
}
