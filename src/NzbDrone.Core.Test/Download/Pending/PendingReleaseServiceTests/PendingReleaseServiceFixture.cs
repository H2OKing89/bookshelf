using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.CustomFormats;
using NzbDrone.Core.DecisionEngine;
using NzbDrone.Core.Download.Pending;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Parser;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Profiles;
using NzbDrone.Core.Profiles.Qualities;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.Queue;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Download.Pending.PendingReleaseServiceTests
{
    [TestFixture]
    public class PendingReleaseServiceFixture : CoreTest<PendingReleaseService>
    {
        private void GivenPendingRelease()
        {
            Mocker.GetMock<IIndexerFactory>().Setup(factory => factory.Find(1)).Returns(new IndexerDefinition { EnableRss = true });
            Mocker.GetMock<IPendingReleaseRepository>()
                              .Setup(v => v.All())
                              .Returns(new List<PendingRelease>
                                {
                                      new PendingRelease { Release = new ReleaseInfo { IndexerId = 1 } }
                                });
        }

        [Test]
        public void should_not_ignore_pending_items_from_available_indexer()
        {
            Mocker.GetMock<IIndexerStatusService>()
                .Setup(v => v.GetBlockedProviders())
                .Returns(new List<IndexerStatus>());

            GivenPendingRelease();

            var results = Subject.GetPending();

            results.Should().NotBeEmpty();
            Mocker.GetMock<IMakeDownloadDecision>()
                  .Verify(v => v.GetRssDecision(It.Is<List<ReleaseInfo>>(d => d.Count == 0), It.IsAny<bool>()), Times.Never());
        }

        [Test]
        public void should_ignore_pending_items_from_unavailable_indexer()
        {
            Mocker.GetMock<IIndexerStatusService>()
                .Setup(v => v.GetBlockedProviders())
                .Returns(new List<IndexerStatus> { new IndexerStatus { ProviderId = 1, DisabledTill = DateTime.UtcNow.AddHours(2) } });

            GivenPendingRelease();

            var results = Subject.GetPending();

            results.Should().BeEmpty();
        }

        [Test]
        public void threshold_change_rechecks_only_pending_releases_in_the_edited_profile()
        {
            var small = new CustomFormat { Id = 1 };
            var enough = new CustomFormat { Id = 2 };
            var author = new Author
            {
                Id = 21,
                QualityProfileId = 11,
                QualityProfile = new QualityProfile
                {
                    MinUpgradeFormatScore = 10,
                    Items = Qualities.QualityFixture.GetDefaultQualities(),
                    FormatItems = new List<ProfileFormatItem>
                    {
                        new ProfileFormatItem { Format = small, Score = 9 },
                        new ProfileFormatItem { Format = enough, Score = 10 }
                    }
                }
            };

            var tooSmall = MakePending(1, author.Id);
            var eligible = MakePending(2, author.Id);
            var unrelated = MakePending(3, 22);
            var current = new BookFile { Quality = new QualityModel(Quality.MP3) };
            Mocker.GetMock<IPendingReleaseRepository>().Setup(repository => repository.All())
                .Returns(new List<PendingRelease> { tooSmall, eligible, unrelated });
            Mocker.GetMock<IAuthorService>().Setup(service => service.FindAuthors(It.IsAny<IEnumerable<int>>()))
                .Returns(new List<Author> { author, new Author { Id = unrelated.AuthorId, QualityProfileId = 12 } });
            Mocker.GetMock<IParsingService>().Setup(service => service.GetBooks(It.IsAny<ParsedBookInfo>(), author))
                .Returns(new List<Book> { new Book { Id = 31, BookFiles = new List<BookFile> { current } } });

            var calculator = Mocker.GetMock<ICustomFormatCalculationService>();
            calculator.Setup(service => service.ParseCustomFormat(current)).Returns(new List<CustomFormat>());
            calculator.Setup(service => service.ParseCustomFormat(It.Is<RemoteBook>(book => book.Release == tooSmall.Release), It.IsAny<long>()))
                .Returns(new List<CustomFormat> { small });
            calculator.Setup(service => service.ParseCustomFormat(It.Is<RemoteBook>(book => book.Release == eligible.Release), It.IsAny<long>()))
                .Returns(new List<CustomFormat> { enough });
            Mocker.GetMock<IQueueService>().Setup(service => service.GetQueue()).Returns(new List<Queue.Queue>());

            Subject.Handle(new MinimumUpgradeFormatScoreChangedEvent(author.QualityProfileId));

            Mocker.GetMock<IPendingReleaseRepository>().Verify(repository => repository.DeleteMany(It.Is<IEnumerable<int>>(ids =>
                ids.SequenceEqual(new[] { tooSmall.Id }))), Times.Once());
            Mocker.GetMock<IEventAggregator>().Verify(events => events.PublishEvent(It.IsAny<PendingReleasesUpdatedEvent>()), Times.Once());
            calculator.Verify(service => service.ParseCustomFormat(It.Is<RemoteBook>(book => book.Release == unrelated.Release), It.IsAny<long>()), Times.Never());
        }

        private static PendingRelease MakePending(int id, int authorId)
        {
            return new PendingRelease
            {
                Id = id,
                AuthorId = authorId,
                ParsedBookInfo = new ParsedBookInfo { Quality = new QualityModel(Quality.MP3) },
                Release = new ReleaseInfo { Title = $"Owned release {id}" }
            };
        }
    }
}
