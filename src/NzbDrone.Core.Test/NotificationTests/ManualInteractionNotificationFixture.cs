using System;
using System.Collections.Generic;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Core.Books;
using NzbDrone.Core.DecisionEngine;
using NzbDrone.Core.Download;
using NzbDrone.Core.Download.TrackedDownloads;
using NzbDrone.Core.Notifications;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.NotificationTests
{
    [TestFixture]
    public class ManualInteractionNotificationFixture : CoreTest<NotificationService>
    {
        private TrackedDownload _tracked;
        private Mock<INotification> _provider;
        private BookDownloadMessage _sent;
        private List<INotification> _providers;

        [SetUp]
        public void Setup()
        {
            _sent = null;
            _provider = Provider(1);
            _providers = new List<INotification> { _provider.Object };
            Mocker.GetMock<INotificationFactory>().Setup(factory => factory.OnImportFailureEnabled(true)).Returns(_providers);
            _tracked = new TrackedDownload
            {
                DownloadClient = 3,
                State = TrackedDownloadState.ImportBlocked,
                DownloadItem = new DownloadClientItem
                {
                    DownloadId = "owned-id", Title = "Owned recording", Status = DownloadItemStatus.Completed,
                    OutputPath = new OsPath("/remote/owned-recording"),
                    DownloadClientInfo = new DownloadClientItemClientInfo { Id = 3, Name = "Owned client", Type = "QBittorrent" }
                },
                ImportItem = new DownloadClientItem { OutputPath = new OsPath("/local/owned-recording") },
                RemoteBook = new RemoteBook
                {
                    Author = new Author { Id = 7, Name = "Owned Author", Tags = new HashSet<int> { 12 } },
                    Books = new List<Book> { Book(11, 17, "us", "B000000001") }
                }
            };
            _tracked.WarnCode(RejectionCode.SourceCleanupPending, "The source needs review");
        }

        private Mock<INotification> Provider(int id, params int[] tags)
        {
            var provider = new Mock<INotification>();
            provider.SetupGet(notification => notification.Definition).Returns(new NotificationDefinition
            {
                Id = id, Name = "Owned notification " + id, OnImportFailure = true, Tags = new HashSet<int>(tags)
            });
            provider.Setup(notification => notification.OnImportFailure(It.IsAny<BookDownloadMessage>()))
                .Callback<BookDownloadMessage>(message => _sent = message);
            return provider;
        }

        private static Book Book(int bookId, int editionId, string region, string asin)
        {
            return new Book
            {
                Id = bookId, Title = "Same title", ForeignBookId = "work-" + bookId,
                Editions = new List<Edition>
                {
                    new Edition
                    {
                        Id = editionId, BookId = bookId, Monitored = true, Title = "Same title",
                        ForeignEditionId = "audible:" + region + ":" + asin, Asin = asin, AudibleRegion = region
                    }
                }
            };
        }

        [Test]
        public void should_include_actionable_source_client_typed_errors_and_explicit_recording_identity()
        {
            Subject.Handle(new ManualInteractionRequiredEvent(_tracked));

            _sent.Message.Should().Contain("manual intervention").And.Contain("Owned recording").And.Contain("Owned client")
                .And.Contain(RejectionCode.SourceCleanupPending).And.Contain("The source needs review");
            _sent.Author.Id.Should().Be(7);
            _sent.Book.Id.Should().Be(11);
            _sent.DownloadClientInfo.Id.Should().Be(3);
            _sent.DownloadId.Should().Be("owned-id");
            _sent.SourcePath.Should().Be("/remote/owned-recording");
            _sent.OutputPath.Should().Be("/local/owned-recording");
            _sent.ImportStatus.Should().Be("ImportBlocked");
            _sent.DownloadStatus.Should().Be("Completed");
            _sent.StatusMessages[0].Rejections[0].Code.Should().Be(RejectionCode.SourceCleanupPending);
            _sent.BookIdentities[0].Editions[0].Asin.Should().Be("B000000001");
            _sent.BookIdentities[0].Editions[0].AudibleRegion.Should().Be("us");
            Mocker.GetMock<INotificationStatusService>().Verify(status => status.RecordSuccess(1), Times.Once());
        }

        [Test]
        public void same_title_marketplaces_should_remain_separate_without_guessing_one_book()
        {
            _tracked.RemoteBook.Books.Add(Book(12, 18, "uk", "B000000002"));

            Subject.Handle(new ManualInteractionRequiredEvent(_tracked));

            _sent.Book.Should().BeNull();
            _sent.Books.Should().HaveCount(2);
            _sent.BookIdentities.Should().HaveCount(2);
            _sent.BookIdentities[0].Editions[0].Id.Should().Be(17);
            _sent.BookIdentities[1].Editions[0].Asin.Should().Be("B000000002");
            _sent.BookIdentities[1].Editions[0].AudibleRegion.Should().Be("uk");
        }

        [Test]
        public void a_committed_recording_should_use_its_proven_edition_rather_than_a_different_monitored_edition()
        {
            _tracked.RemoteBook.Books[0].Editions.Value.Add(new Edition
            {
                Id = 18, BookId = 11, Asin = "B000000002", AudibleRegion = "uk", Monitored = false
            });
            _tracked.ImportReceipt = new DownloadImportReceipt
            {
                Files = new List<DownloadImportFile> { new DownloadImportFile { BookId = 11, EditionId = 18 } }
            };

            Subject.Handle(new ManualInteractionRequiredEvent(_tracked));

            _sent.BookIdentities[0].Editions.Should().ContainSingle();
            _sent.BookIdentities[0].Editions[0].Id.Should().Be(18);
        }

        [Test]
        public void missing_or_foreign_edition_metadata_should_not_be_guessed_from_the_title()
        {
            _tracked.RemoteBook.Books[0].Editions.Value[0].BookId = 999;

            Subject.Handle(new ManualInteractionRequiredEvent(_tracked));

            _sent.BookIdentities[0].Id.Should().Be(11);
            _sent.BookIdentities[0].Editions.Should().BeEmpty();
        }

        [Test]
        public void local_recordings_without_an_audible_asin_should_retain_their_local_identity()
        {
            var edition = _tracked.RemoteBook.Books[0].Editions.Value[0];
            edition.ForeignEditionId = "local:owned-recording";
            edition.Asin = null;
            edition.AudibleRegion = null;

            Subject.Handle(new ManualInteractionRequiredEvent(_tracked));

            _sent.BookIdentities[0].Editions[0].ForeignEditionId.Should().Be("local:owned-recording");
            _sent.BookIdentities[0].Editions[0].Asin.Should().BeNull();
        }

        [TestCase(12, true)]
        [TestCase(13, false)]
        public void should_preserve_existing_author_tag_filters(int tag, bool allowed)
        {
            _provider = Provider(1, tag);
            _providers.Clear();
            _providers.Add(_provider.Object);

            Subject.Handle(new ManualInteractionRequiredEvent(_tracked));

            _provider.Verify(notification => notification.OnImportFailure(It.IsAny<BookDownloadMessage>()), allowed ? Times.Once() : Times.Never());
        }

        [Test]
        public void unmatched_downloads_should_reach_untagged_providers_and_skip_tagged_providers()
        {
            var tagged = Provider(2, 12);
            _providers.Add(tagged.Object);
            _tracked.RemoteBook = null;

            Subject.Handle(new ManualInteractionRequiredEvent(_tracked));

            _provider.Verify(notification => notification.OnImportFailure(It.IsAny<BookDownloadMessage>()), Times.Once());
            tagged.Verify(notification => notification.OnImportFailure(It.IsAny<BookDownloadMessage>()), Times.Never());
            _sent.Author.Should().BeNull();
            _sent.BookIdentities.Should().BeEmpty();
        }

        [Test]
        public void providers_not_opted_into_import_failures_should_not_be_dispatched()
        {
            _providers.Clear();

            Subject.Handle(new ManualInteractionRequiredEvent(_tracked));

            _provider.Verify(notification => notification.OnImportFailure(It.IsAny<BookDownloadMessage>()), Times.Never());
            Mocker.GetMock<INotificationFactory>().Verify(factory => factory.OnImportFailureEnabled(true), Times.Once());
        }

        [Test]
        public void provider_and_status_failures_should_be_isolated_from_later_providers()
        {
            var next = Provider(2);
            _providers.Add(next.Object);
            _provider.Setup(notification => notification.OnImportFailure(It.IsAny<BookDownloadMessage>()))
                .Throws(new InvalidOperationException("Owned provider failure"));
            Mocker.GetMock<INotificationStatusService>().Setup(status => status.RecordFailure(1, It.IsAny<TimeSpan>()))
                .Throws(new InvalidOperationException("Owned status failure"));

            Subject.Handle(new ManualInteractionRequiredEvent(_tracked));

            next.Verify(notification => notification.OnImportFailure(It.IsAny<BookDownloadMessage>()), Times.Once());
            Mocker.GetMock<INotificationStatusService>().Verify(status => status.RecordSuccess(2), Times.Once());
            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void legacy_status_reasons_should_be_preserved_with_unknown_codes()
        {
            _tracked.Warn(new TrackedDownloadStatusMessage("Owned recording", new List<string> { "Legacy reason" }));

            Subject.Handle(new ManualInteractionRequiredEvent(_tracked));

            _sent.StatusMessages[0].Rejections[0].Code.Should().Be(RejectionCode.Unknown);
            _sent.Message.Should().Contain("Legacy reason");
        }
    }
}
