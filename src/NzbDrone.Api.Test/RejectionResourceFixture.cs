using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Common.Serializer;
using NzbDrone.Core.CustomFormats;
using NzbDrone.Core.DecisionEngine;
using NzbDrone.Core.Download.TrackedDownloads;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Qualities;
using Readarr.Api.V1.Indexers;
using Readarr.Api.V1.ManualImport;
using Readarr.Api.V1.Queue;

namespace NzbDrone.Api.Test
{
    [TestFixture]
    public class RejectionResourceFixture
    {
        [TestCase(RejectionType.Temporary)]
        [TestCase(RejectionType.Permanent)]
        public void releases_should_keep_legacy_strings_and_add_typed_details(RejectionType type)
        {
            var rejection = new Rejection("Readable localized explanation", type, "future_recording_rule");
            var remote = new RemoteBook
            {
                Release = new ReleaseInfo { Title = "Owned synthetic release" },
                ParsedBookInfo = new ParsedBookInfo { Quality = new QualityModel(Quality.M4B) },
                CustomFormats = new List<CustomFormat>()
            };
            var resource = new DownloadDecision(remote, rejection).ToResource();

            using var json = JsonDocument.Parse(STJson.ToJson(resource));

            json.RootElement.GetProperty("rejections")[0].GetString().Should().Be(rejection.Reason);
            json.RootElement.GetProperty("rejectionDetails")[0].GetProperty("code").GetString().Should().Be(rejection.Code);
            resource.TemporarilyRejected.Should().Be(type == RejectionType.Temporary);
            resource.Rejected.Should().Be(type == RejectionType.Permanent);
        }

        [TestCase("future_recording_rule")]
        [TestCase(RejectionCode.CustomFormatDowngrade)]
        public void manual_import_updates_should_roundtrip_unknown_and_known_codes(string code)
        {
            var resource = new ManualImportUpdateResource
            {
                Rejections = new[] { new Rejection("Import explanation", RejectionType.Temporary, code) }
            };

            var roundtrip = STJson.Deserialize<ManualImportUpdateResource>(STJson.ToJson(resource));

            roundtrip.Rejections.Single().Code.Should().Be(code);
            roundtrip.Rejections.Single().Type.Should().Be(RejectionType.Temporary);
            roundtrip.Rejections.Single().Reason.Should().Be("Import explanation");
        }

        [Test]
        public void queue_should_keep_messages_and_structured_rejections_after_history_roundtrip()
        {
            var rejection = new Rejection("Import explanation", RejectionType.Temporary, "future_recording_rule");
            var messages = new List<TrackedDownloadStatusMessage>
            {
                new TrackedDownloadStatusMessage("Part 1", new List<string> { rejection.Reason }, new[] { rejection })
            };
            var restored = Json.Deserialize<List<TrackedDownloadStatusMessage>>(messages.ToJson());
            var model = new NzbDrone.Core.Queue.Queue { Status = "Warning", StatusMessages = restored };

            var resource = model.ToResource(false, false);
            var status = STJson.Deserialize<QueueResource>(STJson.ToJson(resource)).StatusMessages.Single();

            status.Messages.Should().Equal("Import explanation");
            status.Rejections.Single().Code.Should().Be("future_recording_rule");
            status.Rejections.Single().Type.Should().Be(RejectionType.Temporary);
        }

        [Test]
        public void legacy_queue_history_should_supply_unknown_codes_without_losing_messages()
        {
            var status = Json.Deserialize<TrackedDownloadStatusMessage>("{\"title\":\"Part 1\",\"messages\":[\"Existing warning\"]}");

            status.Messages.Should().Equal("Existing warning");
            status.Rejections.Single().Code.Should().Be(RejectionCode.Unknown);
        }
    }
}
