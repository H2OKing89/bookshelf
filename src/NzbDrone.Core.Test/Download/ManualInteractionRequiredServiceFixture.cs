using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Core.Datastore.Converters;
using NzbDrone.Core.DecisionEngine;
using NzbDrone.Core.Download;
using NzbDrone.Core.Download.History;
using NzbDrone.Core.Download.TrackedDownloads;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Download
{
    [TestFixture]
    public class ManualInteractionRequiredServiceFixture : CoreTest<ManualInteractionRequiredService>
    {
        private List<DownloadHistory> _history;
        private TrackedDownload _tracked;
        private int _nextId;

        [SetUp]
        public void Setup()
        {
            _history = new List<DownloadHistory>();
            _nextId = 0;
            _tracked = Download();
            Mocker.GetMock<IDownloadHistoryRepository>().Setup(repository => repository.FindByDownloadId(It.IsAny<string>()))
                .Returns(() => _history.ToList());
            Mocker.GetMock<IDownloadHistoryRepository>().Setup(repository => repository.Insert(It.IsAny<DownloadHistory>()))
                .Returns<DownloadHistory>(history =>
                {
                    history.Id = ++_nextId;
                    _history.Add(history);
                    return history;
                });
        }

        private static TrackedDownload Download(int client = 3)
        {
            var tracked = new TrackedDownload
            {
                DownloadClient = client,
                State = TrackedDownloadState.ImportBlocked,
                DownloadItem = new DownloadClientItem
                {
                    DownloadId = "owned-id", Title = "Owned recording",
                    OutputPath = new OsPath("/owned/download"), Status = DownloadItemStatus.Completed,
                    DownloadClientInfo = new DownloadClientItemClientInfo { Id = client }
                }
            };
            tracked.WarnCode(RejectionCode.NoEligibleFiles, "Review this owned recording");
            return tracked;
        }

        private void AssertAlerts(int count)
        {
            Mocker.GetMock<IEventAggregator>().Verify(events => events.PublishEvent(It.IsAny<ManualInteractionRequiredEvent>()), Times.Exactly(count));
        }

        [Test]
        public void repeated_polls_changed_reasons_metadata_and_mapping_should_not_repeat_an_alert()
        {
            Subject.Required(_tracked);
            _tracked.WarnCode(RejectionCode.InvalidImportPath, "A different reason");
            _tracked.ImportItem = new DownloadClientItem { OutputPath = new OsPath("/another/mapping") };
            _tracked.RemoteBook = new RemoteBook();
            _tracked.HasNotifiedManualInteractionRequired = false;

            Subject.Required(_tracked);

            AssertAlerts(1);
            _history.Should().ContainSingle();
            _tracked.HasNotifiedManualInteractionRequired.Should().BeTrue();
        }

        [Test]
        public void restart_should_restore_the_attempt_from_durable_client_scoped_history()
        {
            Subject.Required(_tracked);
            var restarted = new ManualInteractionRequiredService(Mocker.Resolve<IDownloadHistoryRepository>(), Mocker.Resolve<IEventAggregator>());
            var refreshed = Download();

            restarted.Required(refreshed);

            refreshed.HasNotifiedManualInteractionRequired.Should().BeTrue();
            AssertAlerts(1);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void durable_dictionary_roundtrip_and_case_aliases_should_preserve_deduplication(bool uppercaseKeys)
        {
            Subject.Required(_tracked);
            var marker = _history.Single();
            if (uppercaseKeys)
            {
                marker.Data = marker.Data.ToDictionary(pair => pair.Key.ToUpperInvariant(), pair => pair.Value);
            }

            var converter = new EmbeddedDocumentConverter<Dictionary<string, string>>();
            var parameter = new Mock<IDbDataParameter>();
            parameter.SetupProperty(value => value.Value);
            converter.SetValue(parameter.Object, marker.Data);
            marker.Data = converter.Parse(parameter.Object.Value);
            var restarted = new ManualInteractionRequiredService(Mocker.Resolve<IDownloadHistoryRepository>(), Mocker.Resolve<IEventAggregator>());

            restarted.Required(Download());

            AssertAlerts(1);
        }

        [Test]
        public void ambiguous_marker_key_aliases_should_not_authorize_another_attempt()
        {
            Subject.Required(_tracked);
            var marker = _history.Single();
            marker.Data[ManualInteractionRequiredService.ContextKey.ToUpperInvariant()] = "conflicting-context";

            Assert.Throws<InvalidOperationException>(() => Subject.Required(Download()));

            AssertAlerts(1);
        }

        [Test]
        public void same_download_id_on_two_clients_should_have_separate_episodes()
        {
            Subject.Required(_tracked);
            Subject.Required(Download(4));

            AssertAlerts(2);
            _history.Select(item => item.DownloadClientId).Should().BeEquivalentTo(new[] { 3, 4 });
        }

        [Test]
        public void new_grab_or_source_context_should_allow_another_alert()
        {
            Subject.Required(_tracked);
            _history.Add(new DownloadHistory
            {
                Id = ++_nextId, DownloadId = "owned-id", DownloadClientId = 3,
                EventType = DownloadHistoryEventType.DownloadGrabbed, Date = DateTime.UtcNow
            });
            Subject.Required(_tracked);
            _tracked.DownloadItem.OutputPath = new OsPath("/owned/different-source");
            Subject.Required(_tracked);

            AssertAlerts(3);
        }

        [Test]
        public void other_clients_grabs_and_completion_receipts_should_not_reset_the_episode()
        {
            Subject.Required(_tracked);
            _history.Add(new DownloadHistory
            {
                Id = ++_nextId, DownloadId = "owned-id", DownloadClientId = 4,
                EventType = DownloadHistoryEventType.DownloadGrabbed, Date = DateTime.UtcNow
            });
            _history.Add(new DownloadHistory
            {
                Id = ++_nextId, DownloadId = "owned-id", DownloadClientId = 3,
                EventType = DownloadHistoryEventType.DownloadImported, Date = DateTime.UtcNow
            });

            Subject.Required(_tracked);

            AssertAlerts(1);
        }

        [Test]
        public void verified_recovery_should_open_a_later_blocked_episode_once()
        {
            Subject.Required(_tracked);
            Subject.Recovered(_tracked);
            Subject.Required(_tracked);
            AssertAlerts(1);

            _tracked.State = TrackedDownloadState.Imported;
            Subject.Recovered(_tracked);
            Subject.Recovered(_tracked);
            _tracked.HasNotifiedManualInteractionRequired.Should().BeFalse();
            _tracked.State = TrackedDownloadState.ImportBlocked;
            Subject.Required(_tracked);
            Subject.Required(_tracked);

            AssertAlerts(2);
            _history.Count(history => history.Data[ManualInteractionRequiredService.PhaseKey] == "recovered").Should().Be(1);
        }

        [Test]
        public void marker_should_be_durable_before_dispatch()
        {
            Mocker.GetMock<IEventAggregator>().Setup(events => events.PublishEvent(It.IsAny<ManualInteractionRequiredEvent>()))
                .Callback(() =>
                {
                    _history.Should().ContainSingle();
                    _history[0].Data[ManualInteractionRequiredService.PhaseKey].Should().Be("attempted");
                });

            Subject.Required(_tracked);

            AssertAlerts(1);
        }

        [Test]
        public void failed_marker_write_should_not_send_or_acknowledge_an_attempt_and_can_retry()
        {
            var fail = true;
            Mocker.GetMock<IDownloadHistoryRepository>().Setup(repository => repository.Insert(It.IsAny<DownloadHistory>()))
                .Returns<DownloadHistory>(history =>
                {
                    if (fail)
                    {
                        throw new InvalidOperationException("Owned write failure");
                    }

                    history.Id = ++_nextId;
                    _history.Add(history);
                    return history;
                });

            Assert.Throws<InvalidOperationException>(() => Subject.Required(_tracked));
            AssertAlerts(0);
            _tracked.HasNotifiedManualInteractionRequired.Should().BeFalse();
            fail = false;

            Subject.Required(_tracked);

            AssertAlerts(1);
        }

        [Test]
        public void an_unconfirmed_insert_should_not_acknowledge_or_publish()
        {
            Mocker.GetMock<IDownloadHistoryRepository>().Setup(repository => repository.Insert(It.IsAny<DownloadHistory>()))
                .Returns((DownloadHistory)null);

            Assert.Throws<InvalidOperationException>(() => Subject.Required(_tracked));

            AssertAlerts(0);
            _tracked.HasNotifiedManualInteractionRequired.Should().BeFalse();
        }

        [Test]
        public void dispatch_failure_should_not_repeat_the_persisted_attempt()
        {
            Mocker.GetMock<IEventAggregator>().Setup(events => events.PublishEvent(It.IsAny<ManualInteractionRequiredEvent>()))
                .Throws(new InvalidOperationException("Owned dispatch failure"));
            Assert.Throws<InvalidOperationException>(() => Subject.Required(_tracked));

            Subject.Required(_tracked);

            AssertAlerts(1);
        }

        [Test]
        public void concurrent_checks_should_record_and_dispatch_one_attempt()
        {
            var subject = Subject;

            Parallel.For(0, 10, _ => subject.Required(_tracked));

            _history.Should().ContainSingle();
            AssertAlerts(1);
        }

        [TestCase(TrackedDownloadState.Downloading)]
        [TestCase(TrackedDownloadState.Imported)]
        [TestCase(TrackedDownloadState.ImportPending)]
        public void nonblocked_states_should_not_alert(TrackedDownloadState state)
        {
            _tracked.State = state;

            Subject.Required(_tracked);

            AssertAlerts(0);
            _history.Should().BeEmpty();
        }

        [TestCase(0)]
        [TestCase(4)]
        public void unknown_or_conflicting_client_identity_should_not_alert(int client)
        {
            _tracked.DownloadClient = client;

            Subject.Required(_tracked);

            AssertAlerts(0);
            _history.Should().BeEmpty();
        }
    }
}
