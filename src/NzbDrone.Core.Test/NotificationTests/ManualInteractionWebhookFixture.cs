using System.Collections.Generic;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Serializer;
using NzbDrone.Core.Books;
using NzbDrone.Core.DecisionEngine;
using NzbDrone.Core.Download;
using NzbDrone.Core.Download.TrackedDownloads;
using NzbDrone.Core.Notifications;
using NzbDrone.Core.Notifications.Webhook;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.NotificationTests
{
    [TestFixture]
    public class ManualInteractionWebhookFixture : CoreTest<Webhook>
    {
        [Test]
        public void should_send_a_versioned_manual_interaction_payload_using_the_existing_opt_in()
        {
            Subject.Definition = new NotificationDefinition { Settings = new WebhookSettings() };
            var message = new BookDownloadMessage
            {
                Author = new Author { Id = 7, Name = "Owned Author" },
                BookIdentities = new List<ManualInteractionBook>
                {
                    new ManualInteractionBook
                    {
                        Id = 11, Title = "Owned recording",
                        Editions = new List<ManualInteractionEdition>
                        {
                            new ManualInteractionEdition { Id = 17, Asin = "B000000001", AudibleRegion = "us" },
                            new ManualInteractionEdition { Id = 18, Asin = "B000000002", AudibleRegion = "uk" }
                        }
                    }
                },
                Message = "Review the import in Activity", DownloadId = "owned-id",
                DownloadClientInfo = new DownloadClientItemClientInfo { Id = 3, Name = "Owned client", Type = "QBittorrent" },
                SourceTitle = "Owned recording", SourcePath = "/remote/source", OutputPath = "/local/source",
                DownloadStatus = "Completed", ImportStatus = "ImportBlocked",
                StatusMessages = new List<TrackedDownloadStatusMessage>
                {
                    new TrackedDownloadStatusMessage(
                        "recording.m4b",
                        new List<string> { "Source changed" },
                        new[] { new Rejection("Source changed", RejectionType.Temporary, RejectionCode.SourceCleanupPending) })
                }
            };
            WebhookManualInteractionPayload sent = null;
            Mocker.GetMock<IWebhookProxy>().Setup(proxy => proxy.SendWebhook(It.IsAny<WebhookPayload>(), It.IsAny<WebhookSettings>()))
                .Callback<WebhookPayload, WebhookSettings>((payload, _) => sent = payload as WebhookManualInteractionPayload);

            Subject.OnImportFailure(message);

            sent.Should().NotBeNull();
            sent.PayloadVersion.Should().Be(1);
            sent.EventType.Should().Be(WebhookEventType.ManualInteractionRequired);
            sent.Author.Id.Should().Be(7);
            sent.DownloadClientId.Should().Be(3);
            sent.DownloadId.Should().Be("owned-id");
            sent.SourcePath.Should().Be("/remote/source");
            sent.OutputPath.Should().Be("/local/source");
            sent.Books[0].Editions[1].AudibleRegion.Should().Be("uk");
            var json = sent.ToJson();
            json.Should().Contain("ManualInteractionRequired").And.Contain(RejectionCode.SourceCleanupPending).And.Contain("B000000002");
            json.Should().NotContain("password").And.NotContain("username");
            Subject.SupportsOnImportFailure.Should().BeTrue();
            ((NotificationDefinition)Subject.Definition).OnImportFailure.Should().BeFalse();
            ((NotificationDefinition)Subject.Definition).Enable.Should().BeFalse();
        }

        [Test]
        public void unmatched_downloads_should_build_a_payload_without_inventing_author_or_edition_identity()
        {
            var payload = Subject.BuildManualInteractionPayload(new BookDownloadMessage
            {
                DownloadId = "unmatched-id", DownloadClientInfo = new DownloadClientItemClientInfo { Id = 3 },
                SourceTitle = "Unmatched recording"
            });

            payload.Author.Should().BeNull();
            payload.Books.Should().BeEmpty();
            payload.DownloadId.Should().Be("unmatched-id");
        }
    }
}
