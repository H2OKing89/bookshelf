using System;
using System.Collections.Generic;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.Books.Events;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.CustomFormats;
using NzbDrone.Core.CustomFormats.Events;
using NzbDrone.Core.Download;
using NzbDrone.Core.Download.Pending;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Jobs;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Parser;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Profiles;
using NzbDrone.Core.Profiles.Delay;
using NzbDrone.Core.Profiles.Qualities;
using NzbDrone.Core.Profiles.Releases;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.Queue;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Download.Pending.PendingReleaseServiceTests
{
    [TestFixture]
    public class PendingPolicyRefreshFixture : CoreTest<PendingReleaseService>
    {
        private readonly CustomFormat _format = new CustomFormat { Id = 1, Name = "Freeleech" };
        private Author _author;
        private Book _book;
        private DelayProfile _delay;
        private PendingRelease _pending;
        private bool _formatMatches;

        [SetUp]
        public void Setup()
        {
            _author = new Author
            {
                Id = 2,
                QualityProfileId = 3,
                AuthorMetadataId = 4,
                Monitored = true,
                QualityProfile = new QualityProfile
                {
                    UpgradeAllowed = true,
                    Cutoff = Quality.M4B.Id,
                    MinFormatScore = 5,
                    Items = new List<QualityProfileQualityItem>
                    {
                        new QualityProfileQualityItem { Quality = Quality.MP3, Allowed = true },
                        new QualityProfileQualityItem { Quality = Quality.M4B, Allowed = true }
                    },
                    FormatItems = new List<ProfileFormatItem>
                    {
                        new ProfileFormatItem { Format = _format, Score = 10 }
                    }
                },
                Tags = new HashSet<int> { 5 }
            };
            _book = new Book
            {
                Id = 6,
                AuthorMetadataId = _author.AuthorMetadataId,
                Monitored = true,
                BookFiles = new List<BookFile>()
            };
            _delay = new DelayProfile { EnableTorrent = true, TorrentDelay = 120 };
            _formatMatches = true;

            var indexer = new IndexerDefinition { Id = 7, EnableRss = true };
            _pending = new PendingRelease
            {
                Id = 8,
                AuthorId = _author.Id,
                Added = DateTime.UtcNow.AddMinutes(-25),
                ParsedBookInfo = new ParsedBookInfo { Quality = new QualityModel(Quality.M4B) },
                Release = new ReleaseInfo
                {
                    IndexerId = indexer.Id,
                    PublishDate = DateTime.UtcNow.AddMinutes(-30),
                    DownloadProtocol = DownloadProtocol.Torrent,
                    AudioDurationSeconds = 3600,
                    Size = 80 * 1024 * 1024
                },
                AdditionalInfo = new PendingReleaseAdditionalInfo
                {
                    BookIds = new List<int> { _book.Id },
                    IndexerFlags = IndexerFlags.Freeleech
                }
            };

            var repository = Mocker.GetMock<IPendingReleaseRepository>();
            repository.Setup(service => service.WithoutFallback()).Returns(new List<PendingRelease> { _pending });
            repository.Setup(service => service.GetAuthorIds()).Returns(new List<int> { _author.Id });
            Mocker.GetMock<IAuthorService>().Setup(service => service.FindAuthors(It.IsAny<IEnumerable<int>>()))
                .Returns(new List<Author> { _author });
            Mocker.GetMock<IParsingService>().Setup(service => service.GetBooks(_pending.ParsedBookInfo, _author, null))
                .Returns(() => new List<Book> { _book });
            Mocker.GetMock<ICustomFormatCalculationService>().Setup(service => service.ParseCustomFormat(It.IsAny<RemoteBook>(), It.IsAny<long>()))
                .Returns((RemoteBook book, long _) => _formatMatches && book.Release.IndexerFlags == IndexerFlags.Freeleech
                    ? new List<CustomFormat> { _format }
                    : new List<CustomFormat>());
            Mocker.GetMock<IDelayProfileService>().Setup(service => service.AllForTags(_author.Tags)).Returns(new List<DelayProfile> { _delay });
            Mocker.GetMock<IDelayProfileService>().Setup(service => service.BestForTags(_author.Tags)).Returns(_delay);
            Mocker.GetMock<ITaskManager>().Setup(service => service.GetNextExecution(typeof(RssSyncCommand))).Returns(DateTime.UtcNow.AddMinutes(2));
            Mocker.GetMock<IConfigService>().SetupGet(service => service.RssSyncInterval).Returns(5);

            Mocker.GetMock<IIndexerFactory>().Setup(factory => factory.Find(indexer.Id)).Returns(indexer);
            Mocker.GetMock<IIndexerFactory>().Setup(factory => factory.Get(indexer.Id)).Returns(indexer);
            var client = Mocker.GetMock<IDownloadClient>().Object;
            Mocker.GetMock<IProvideDownloadClient>()
                .Setup(service => service.GetDownloadClients(DownloadProtocol.Torrent, indexer.Id, false, _author.Tags))
                .Returns(() => _author.Tags.Contains(5) ? new[] { client } : Array.Empty<IDownloadClient>());
            Mocker.GetMock<IReleaseProfileService>()
                .Setup(service => service.EnabledForTags(_author.Tags, indexer.Id)).Returns(new List<ReleaseProfile>());
            Mocker.GetMock<IQueueService>().Setup(service => service.GetQueue()).Returns(new List<Queue.Queue>());
            Mocker.GetMock<ICustomFormatCalculationService>().Setup(service => service.ParseCustomFormat(It.IsAny<BookFile>()))
                .Returns(new List<CustomFormat>());
            Mocker.GetMock<ICustomFormatCalculationService>().Setup(service => service.ParseCustomFormat(It.IsAny<BookFile>(), _author))
                .Returns(new List<CustomFormat>());
            Mocker.SetConstant<IPendingReleasePolicy>(Mocker.Resolve<PendingReleasePolicy>());
        }

        [Test]
        public void score_and_condition_edits_revalidate_the_same_queue_item_without_restarting_the_delay()
        {
            var original = Subject.GetPendingQueue().Should().ContainSingle().Which;
            var added = _pending.Added;
            original.RemoteBook.CustomFormatScore.Should().Be(10);

            _author.QualityProfile.Value.FormatItems[0].Score = 4;
            Subject.Handle(new QualityProfileUpdatedEvent(_author.QualityProfileId));
            Subject.FindPendingQueueItem(original.Id).Should().BeNull();

            _author.QualityProfile.Value.FormatItems[0].Score = 10;
            _formatMatches = false;
            Subject.Handle(new CustomFormatUpdatedEvent(_format.Id));
            Subject.FindPendingQueueItem(original.Id).Should().BeNull();

            _formatMatches = true;
            Subject.Handle(new CustomFormatUpdatedEvent(_format.Id));
            var restored = Subject.FindPendingQueueItem(original.Id);
            restored.EstimatedCompletionTime.Should().Be(original.EstimatedCompletionTime);
            _pending.Added.Should().Be(added);
            Mocker.GetMock<IPendingReleaseRepository>().Verify(repository => repository.Insert(It.IsAny<PendingRelease>()), Times.Never());
            Mocker.GetMock<IPendingReleaseRepository>().Verify(repository => repository.Update(It.IsAny<PendingRelease>()), Times.Never());
        }

        [Test]
        public void delay_edits_use_the_original_publication_time()
        {
            var original = Subject.GetPendingQueue().Should().ContainSingle().Which;
            _delay.TorrentDelay += 45;
            Subject.Handle(new DelayProfilesUpdatedEvent(_author.Tags));

            var restored = Subject.FindPendingQueueItem(original.Id);
            restored.EstimatedCompletionTime.Should().Be(original.EstimatedCompletionTime.Value.AddMinutes(45));
        }

        [Test]
        public void current_cutoff_profile_size_and_client_tag_control_queue_eligibility()
        {
            var profile = _author.QualityProfile.Value;
            _book.BookFiles.Value.Add(new BookFile { Quality = new QualityModel(Quality.MP3) });
            var original = Subject.GetPendingQueue().Should().ContainSingle().Which;

            profile.Cutoff = Quality.MP3.Id;
            Subject.Handle(new QualityProfileUpdatedEvent(_author.QualityProfileId));
            Subject.FindPendingQueueItem(original.Id).Should().BeNull();

            profile.Cutoff = Quality.M4B.Id;
            profile.Items[1].MaxSize = 79;
            Subject.Handle(new QualityProfileUpdatedEvent(_author.QualityProfileId));
            Subject.FindPendingQueueItem(original.Id).Should().BeNull();

            profile.Items[1].MaxSize = 80;
            _author.Tags.Remove(5);
            Subject.Handle(new AuthorEditedEvent(_author, new Author { Id = _author.Id, Tags = new HashSet<int> { 5 } }));
            Subject.FindPendingQueueItem(original.Id).Should().BeNull();

            _author.Tags.Add(5);
            Subject.FindPendingQueueItem(original.Id).Should().NotBeNull();
        }

        [Test]
        public void a_deleted_book_cannot_reassign_a_pending_release_to_another_book()
        {
            var original = Subject.GetPendingQueue().Should().ContainSingle().Which;
            var deleted = _book;
            _book = new Book { Id = 9, AuthorMetadataId = _author.AuthorMetadataId };
            Subject.Handle(new BookDeletedEvent(deleted, false, false));

            Subject.GetPendingQueue().Should().BeEmpty();
            Subject.FindPendingQueueItem(original.Id).Should().BeNull();
        }

        [Test]
        public void rematching_a_stored_release_restores_structured_identity_before_parsing()
        {
            _pending.Release.Book = "Original recording";
            _pending.Release.BookAuthors.Add("Original author");
            Mocker.GetMock<IParsingService>().Setup(service => service.GetBooks(
                    It.Is<ParsedBookInfo>(parsed => parsed.StructuredRelease == _pending.Release),
                    _author,
                    null))
                .Returns(new List<Book> { _book });
            Mocker.GetMock<IParsingService>().Setup(service => service.GetBooks(
                    It.Is<ParsedBookInfo>(parsed => parsed.StructuredRelease == null),
                    _author,
                    null))
                .Returns(new List<Book> { new Book { Id = 9, AuthorMetadataId = _author.AuthorMetadataId } });

            Subject.GetPendingQueue().Should().ContainSingle().Which.Book.Id.Should().Be(_book.Id);
        }

        [Test]
        public void unrelated_policy_edits_do_not_rebuild_or_invalidate_pending_queue_items()
        {
            Subject.Handle(new QualityProfileUpdatedEvent(_author.QualityProfileId + 1));
            Subject.Handle(new CustomFormatUpdatedEvent(_format.Id + 1));
            Subject.Handle(new DelayProfilesUpdatedEvent(new HashSet<int> { 80 }));

            Mocker.GetMock<IPendingReleaseRepository>().Verify(repository => repository.WithoutFallback(), Times.Never());
            Mocker.GetMock<IEventAggregator>().Verify(events => events.PublishEvent(It.IsAny<PendingReleasesUpdatedEvent>()), Times.Never());
        }
    }
}
