using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Cache;
using NzbDrone.Core.AuthorStats;
using NzbDrone.Core.Books;
using NzbDrone.Core.Books.Events;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Profiles.Qualities;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Profiles
{
    [TestFixture]
    public class QualityAuthorAssignmentFixture : CoreTest<AuthorService>
    {
        [TestCase(false)]
        [TestCase(true)]
        public void should_only_publish_assignment_rank_change_after_a_single_profile_assignment_changes(bool profileChanged)
        {
            var original = new Author { Id = 12, QualityProfileId = 1 };
            var updated = new Author { Id = 12, QualityProfileId = profileChanged ? 2 : 1, Tags = new HashSet<int> { 8 } };
            var committed = false;
            Mocker.GetMock<IAuthorRepository>().Setup(repository => repository.Get(12)).Returns(original);
            Mocker.GetMock<IAuthorRepository>().Setup(repository => repository.Update(updated)).Returns(() =>
            {
                committed = true;
                return updated;
            });

            Mocker.GetMock<IEventAggregator>().Setup(events => events.PublishEvent(It.IsAny<QualityRankingChangedEvent>())).Callback((QualityRankingChangedEvent message) =>
            {
                committed.Should().BeTrue();
                message.AuthorAssignmentsChanged.Should().BeTrue();
            });

            Subject.UpdateAuthor(updated);

            Mocker.GetMock<IEventAggregator>().Verify(events => events.PublishEvent(It.IsAny<AuthorEditedEvent>()), Times.Once());
            Mocker.GetMock<IEventAggregator>().Verify(events => events.PublishEvent(It.IsAny<QualityRankingChangedEvent>()), Times.Exactly(profileChanged ? 1 : 0));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void should_publish_at_most_one_assignment_rank_change_for_bulk_edits_and_none_for_tag_only_changes(bool profileChanged)
        {
            var original = new List<Author> { new() { Id = 12, QualityProfileId = 1 }, new() { Id = 13, QualityProfileId = 2 } };
            var updated = new List<Author>
            {
                new() { Id = 12, QualityProfileId = profileChanged ? 2 : 1, Tags = new HashSet<int> { 8 } },
                new() { Id = 13, QualityProfileId = 2, Tags = new HashSet<int> { 9 } }
            };
            var committed = false;
            Mocker.GetMock<IAuthorRepository>().Setup(repository => repository.Get(It.IsAny<IEnumerable<int>>())).Returns(original);
            Mocker.GetMock<IAuthorRepository>().Setup(repository => repository.UpdateMany(updated)).Callback(() => committed = true);
            Mocker.GetMock<IEventAggregator>().Setup(events => events.PublishEvent(It.IsAny<QualityRankingChangedEvent>())).Callback((QualityRankingChangedEvent message) =>
            {
                committed.Should().BeTrue();
                message.AuthorAssignmentsChanged.Should().BeTrue();
            });

            Subject.UpdateAuthors(updated, false);

            Mocker.GetMock<IAuthorRepository>().Verify(repository => repository.Get(It.Is<IEnumerable<int>>(ids => ids.SequenceEqual(new[] { 12, 13 }))), Times.Once());
            Mocker.GetMock<IEventAggregator>().Verify(events => events.PublishEvent(It.IsAny<AuthorsEditedEvent>()), Times.Once());
            Mocker.GetMock<IEventAggregator>().Verify(events => events.PublishEvent(It.IsAny<QualityRankingChangedEvent>()), Times.Exactly(profileChanged ? 1 : 0));
        }

        [Test]
        public void should_not_publish_a_rank_event_after_a_failed_assignment_write()
        {
            var original = new Author { Id = 12, QualityProfileId = 1 };
            var updated = new Author { Id = 12, QualityProfileId = 2 };
            Mocker.GetMock<IAuthorRepository>().Setup(repository => repository.Get(12)).Returns(original);
            Mocker.GetMock<IAuthorRepository>().Setup(repository => repository.Update(updated)).Throws(new InvalidOperationException("Owned write failed"));
            Mocker.GetMock<IAuthorRepository>().Setup(repository => repository.Get(It.IsAny<IEnumerable<int>>())).Returns(new[] { original });
            Mocker.GetMock<IAuthorRepository>().Setup(repository => repository.UpdateMany(It.IsAny<IList<Author>>())).Throws(new InvalidOperationException("Owned bulk write failed"));

            Assert.Throws<InvalidOperationException>(() => Subject.UpdateAuthor(updated));
            Assert.Throws<InvalidOperationException>(() => Subject.UpdateAuthors(new List<Author> { updated }, false));
            Mocker.GetMock<IEventAggregator>().Verify(events => events.PublishEvent(It.IsAny<QualityRankingChangedEvent>()), Times.Never());
        }
    }

    [TestFixture]
    public class QualityAuthorStatisticsCacheFixture : CoreTest<AuthorStatisticsService>
    {
        [TestCase(false)]
        [TestCase(true)]
        public void should_invalidate_both_global_and_individual_rank_cache_on_author_edit(bool bulk)
        {
            var cache = new CacheManager();
            var repository = Mocker.GetMock<IAuthorStatisticsRepository>();
            var rank = 0.75;
            List<BookStatistics> ReadStats() => new() { new BookStatistics { AuthorId = 12, BookId = 7, FileQualityRank = rank } };
            repository.Setup(value => value.AuthorStatistics()).Returns(ReadStats);
            repository.Setup(value => value.AuthorStatistics(12)).Returns(ReadStats);
            var service = new AuthorStatisticsService(repository.Object, cache);

            service.AuthorStatistics().Single().BookStatistics.Single().FileQualityRank.Should().Be(0.75);
            service.AuthorStatistics(12).BookStatistics.Single().FileQualityRank.Should().Be(0.75);
            rank = 0;
            service.AuthorStatistics().Single().BookStatistics.Single().FileQualityRank.Should().Be(0.75);
            service.AuthorStatistics(12).BookStatistics.Single().FileQualityRank.Should().Be(0.75);

            if (bulk)
            {
                service.Handle(new AuthorsEditedEvent(new[] { 12 }));
            }
            else
            {
                service.Handle(new AuthorEditedEvent(new Author { Id = 12, QualityProfileId = 2 }, new Author { Id = 12, QualityProfileId = 1 }));
            }

            service.AuthorStatistics().Single().BookStatistics.Single().FileQualityRank.Should().Be(0);
            service.AuthorStatistics(12).BookStatistics.Single().FileQualityRank.Should().Be(0);
        }
    }
}
