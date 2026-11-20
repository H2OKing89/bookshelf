using System;
using System.Collections.Generic;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Download;
using NzbDrone.Core.Download.History;
using NzbDrone.Core.Download.TrackedDownloads;
using NzbDrone.Core.History;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Download.FailedDownloadServiceTests
{
    [TestFixture]
    public class ClientScopedFailureFixture : CoreTest<FailedDownloadService>
    {
        private EntityHistory _legacy;

        [SetUp]
        public void Setup()
        {
            _legacy = new EntityHistory { Id = 1, BookId = 11, DownloadId = "same-hash", EventType = EntityHistoryEventType.Grabbed };
            Mocker.GetMock<IHistoryService>().Setup(history => history.Get(1)).Returns(_legacy);
            Mocker.GetMock<IHistoryService>().Setup(history => history.Find("same-hash", EntityHistoryEventType.Grabbed)).Returns(new List<EntityHistory> { _legacy });
            Mocker.GetMock<IDownloadHistoryRepository>().Setup(history => history.FindByDownloadId("same-hash")).Returns(new List<DownloadHistory>
            {
                new DownloadHistory { DownloadId = "same-hash", DownloadClientId = 1 },
                new DownloadHistory { DownloadId = "same-hash", DownloadClientId = 2 }
            });
        }

        [TestCase(false)]
        [TestCase(true)]
        public void unknown_legacy_entity_scope_cannot_fail_multiple_bound_clients_without_a_live_tracker(bool historyId)
        {
            Action mark = () =>
            {
                if (historyId)
                {
                    Subject.MarkAsFailed(1);
                }
                else
                {
                    Subject.MarkAsFailed("same-hash");
                }
            };

            mark.Should().Throw<AmbiguousTrackedDownloadException>();
            Mocker.GetMock<IEventAggregator>().Verify(events => events.PublishEvent(It.IsAny<DownloadFailedEvent>()), Times.Never());
        }

        [Test]
        public void an_explicit_client_scope_fails_only_its_history_and_preserves_skip_redownload()
        {
            Mocker.GetMock<IHistoryService>().Setup(history => history.Find("same-hash", EntityHistoryEventType.Grabbed, 1)).Returns(new List<EntityHistory> { _legacy });

            Subject.MarkAsFailed("same-hash", 1, true);

            Mocker.GetMock<IEventAggregator>().Verify(events => events.PublishEvent(It.Is<DownloadFailedEvent>(failed =>
                failed.BookIds.Count == 1 && failed.BookIds[0] == 11 && failed.SkipRedownload)), Times.Once());
            Mocker.GetMock<ITrackedDownloadService>().Verify(tracker => tracker.Find("same-hash"), Times.Never());
            Mocker.GetMock<IHistoryService>().Verify(history => history.Find("same-hash", EntityHistoryEventType.Grabbed), Times.Never());
        }

        [Test]
        public void a_single_known_client_binding_scopes_legacy_history_before_publishing_failure()
        {
            Mocker.GetMock<IDownloadHistoryRepository>().Setup(history => history.FindByDownloadId("same-hash")).Returns(new List<DownloadHistory>
            {
                new DownloadHistory { DownloadId = "same-hash", DownloadClientId = 1 }
            });
            Mocker.GetMock<IHistoryService>().Setup(history => history.Find("same-hash", EntityHistoryEventType.Grabbed, 1)).Returns(new List<EntityHistory> { _legacy });

            Subject.MarkAsFailed("same-hash", true);

            Mocker.GetMock<IEventAggregator>().Verify(events => events.PublishEvent(It.Is<DownloadFailedEvent>(failed => failed.SkipRedownload)), Times.Once());
            Mocker.GetMock<IHistoryService>().Verify(history => history.Find("same-hash", EntityHistoryEventType.Grabbed, 1), Times.Once());
        }

        [TestCase(0)]
        [TestCase(-1)]
        public void invalid_explicit_client_scope_cannot_publish_a_failure(int clientId)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => Subject.MarkAsFailed("same-hash", clientId));

            Mocker.GetMock<IEventAggregator>().Verify(events => events.PublishEvent(It.IsAny<DownloadFailedEvent>()), Times.Never());
        }
    }
}
