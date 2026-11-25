using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.Download;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.Events;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Notifications;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.NotificationTests
{
    public class MediaServerNotificationFixture : CoreTest<NotificationService>
    {
        private Mock<INotification> _server;
        private Mock<IBatchedMediaServerNotification> _batch;
        private Mock<INotification> _ordinary;
        private List<NotificationStatus> _blocked;

        [SetUp]
        public void Setup()
        {
            _blocked = new List<NotificationStatus>();
            Mocker.GetMock<INotificationStatusService>().Setup(status => status.GetBlockedProviders()).Returns(_blocked);
            _server = new Mock<INotification>();
            _batch = _server.As<IBatchedMediaServerNotification>();
            _server.SetupGet(provider => provider.Definition).Returns(new NotificationDefinition
            {
                Id = 1, Name = "Owned server", Tags = new HashSet<int> { 12 }, OnReleaseImport = true
            });
            _ordinary = new Mock<INotification>();
            _ordinary.SetupGet(provider => provider.Definition).Returns(new NotificationDefinition
            {
                Id = 2, Name = "Owned ordinary notification", OnReleaseImport = true, OnUpgrade = true
            });
            var providers = new List<INotification> { _server.Object, _ordinary.Object };
            Mocker.GetMock<INotificationFactory>().Setup(factory => factory.OnReleaseImportEnabled(true)).Returns(providers);
            Mocker.GetMock<INotificationFactory>().Setup(factory => factory.GetAvailableProviders()).Returns(providers);
        }

        private static BookImportedEvent Imported(int id, int tag = 12, bool upgrade = false, bool newDownload = true)
        {
            var author = new Author { Id = 7, Name = "Owned author", Tags = new HashSet<int> { tag } };
            var book = new Book { Id = id, Title = "Owned book " + id };
            return new BookImportedEvent(author, book, new List<BookFile> { new BookFile(), new BookFile() }, upgrade ? new List<BookFile> { new BookFile() } : new List<BookFile>(), newDownload, null, true);
        }

        [Test]
        public void batch_filters_tags_upgrades_and_existing_files_and_regular_notifications_still_send_per_book()
        {
            var imports = new[] { Imported(1), Imported(2), Imported(3, tag: 13), Imported(4, upgrade: true), Imported(5, newDownload: false) };
            List<BookDownloadMessage> sent = null;
            _batch.Setup(provider => provider.QueueImportBatch(It.IsAny<IReadOnlyList<BookDownloadMessage>>(), true, false))
                .Callback<IReadOnlyList<BookDownloadMessage>, bool, bool>((messages, _, _) => sent = messages.ToList());

            foreach (var import in imports)
            {
                Subject.Handle(import);
            }

            Subject.HandleAsync(new BookImportBatchCompletedEvent(imports));

            _server.Verify(provider => provider.OnReleaseImport(It.IsAny<BookDownloadMessage>()), Times.Never());
            _ordinary.Verify(provider => provider.OnReleaseImport(It.IsAny<BookDownloadMessage>()), Times.Exactly(4));
            _batch.Verify(provider => provider.QueueImportBatch(It.IsAny<IReadOnlyList<BookDownloadMessage>>(), true, false), Times.Once());
            sent.Select(message => message.Book.Id).Should().Equal(1, 2);
            sent.SelectMany(message => message.BookFiles).Should().HaveCount(4);
            _batch.Verify(provider => provider.ProcessPendingUpdates(), Times.Once());
        }

        [Test]
        public void upgraded_file_deletion_opt_in_can_queue_a_scan_without_import_notifications()
        {
            var definition = (NotificationDefinition)_server.Object.Definition;
            definition.OnReleaseImport = false;
            definition.OnBookFileDelete = true;
            definition.OnBookFileDeleteForUpgrade = true;
            _server.SetupGet(provider => provider.SupportsOnBookFileDeleteForUpgrade).Returns(true);
            var author = new Author { Tags = new HashSet<int> { 12 } };
            var removed = new[] { new BookFile { EditionId = 9, Author = author }, new BookFile { EditionId = 9, Author = author } };

            Subject.HandleAsync(new BookImportBatchCompletedEvent(new[] { Imported(1, upgrade: true) }, removed));

            _batch.Verify(provider => provider.QueueImportBatch(It.Is<IReadOnlyList<BookDownloadMessage>>(messages => messages.Count == 0), true, true), Times.Once());
            _batch.Verify(provider => provider.ProcessPendingUpdates(), Times.Once());
        }

        [Test]
        public void blocked_connection_queues_without_sending_and_periodic_command_drains_after_backoff()
        {
            _blocked.Add(new NotificationStatus { ProviderId = 1 });
            Subject.HandleAsync(new BookImportBatchCompletedEvent(new[] { Imported(1) }));
            _batch.Verify(provider => provider.QueueImportBatch(It.IsAny<IReadOnlyList<BookDownloadMessage>>(), false, false), Times.Once());
            _batch.Verify(provider => provider.ProcessPendingUpdates(), Times.Never());

            _blocked.Clear();
            _batch.Setup(provider => provider.ProcessPendingUpdates()).Returns(true);
            Subject.HandleAsync(new CommandExecutedEvent(new CommandModel { Body = new RefreshMonitoredDownloadsCommand() }));
            _batch.Verify(provider => provider.ProcessPendingUpdates(), Times.Once());
            Mocker.GetMock<INotificationStatusService>().Verify(status => status.RecordSuccess(1), Times.Once());
        }

        [Test]
        public void a_failed_server_and_status_record_does_not_block_the_next_server()
        {
            var second = new Mock<INotification>();
            var secondBatch = second.As<IBatchedMediaServerNotification>();
            second.SetupGet(provider => provider.Definition).Returns(new NotificationDefinition { Id = 3, Name = "Second server" });
            Mocker.GetMock<INotificationFactory>().Setup(factory => factory.GetAvailableProviders())
                .Returns(new List<INotification> { _server.Object, second.Object });
            _batch.Setup(provider => provider.ProcessPendingUpdates()).Throws(new InvalidOperationException("Owned private credential"));
            Mocker.GetMock<INotificationStatusService>().Setup(status => status.RecordFailure(1, It.IsAny<TimeSpan>()))
                .Throws(new InvalidOperationException("Owned status failure"));
            secondBatch.Setup(provider => provider.ProcessPendingUpdates()).Returns(true);

            Subject.HandleAsync(new BookImportBatchCompletedEvent(new[] { Imported(1) }));

            secondBatch.Verify(provider => provider.ProcessPendingUpdates(), Times.Once());
            Mocker.GetMock<INotificationStatusService>().Verify(status => status.RecordSuccess(3), Times.Once());
            ExceptionVerification.ExpectedWarns(1);
        }
    }
}
