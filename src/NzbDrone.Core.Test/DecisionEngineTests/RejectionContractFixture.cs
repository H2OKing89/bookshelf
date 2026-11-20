using FluentAssertions;
using Newtonsoft.Json;
using NUnit.Framework;
using NzbDrone.Common.Serializer;
using NzbDrone.Core.DecisionEngine;

namespace NzbDrone.Core.Test.DecisionEngineTests
{
    [TestFixture]
    public class RejectionContractFixture
    {
        [Test]
        public void wording_and_localization_should_not_change_the_machine_reason()
        {
            var original = Decision.RejectCode(RejectionCode.QualityNotUpgrade, "Not an upgrade");
            var translated = Decision.RejectCode(RejectionCode.QualityNotUpgrade, "Qualité insuffisante pour {0}", "recording");

            original.Code.Should().Be(translated.Code);
            translated.Reason.Should().Be("Qualité insuffisante pour recording");
            translated.Accepted.Should().BeFalse();
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase(" ")]
        public void unspecified_codes_should_be_explicitly_unknown(string code)
        {
            new Rejection("Legacy rejection", code: code).Code.Should().Be(RejectionCode.Unknown);
            Decision.RejectCode(code, "Legacy rejection").Code.Should().Be(RejectionCode.Unknown);
            Decision.Reject("Legacy {0}", 42).Code.Should().Be(RejectionCode.Unknown);
        }

        [Test]
        public void accepted_decisions_should_not_invent_a_rejection_code()
        {
            Decision.Accept().Accepted.Should().BeTrue();
            Decision.Accept().Code.Should().BeNull();
        }

        [TestCase(false)]
        [TestCase(true)]
        public void unfamiliar_codes_and_permanence_should_round_trip_without_enum_translation(bool legacySerializer)
        {
            var expected = new Rejection("Publisher-specific explanation", RejectionType.Temporary, "publisher.future.Rule-v2");
            var actual = legacySerializer
                ? JsonConvert.DeserializeObject<Rejection>(JsonConvert.SerializeObject(expected))
                : STJson.Deserialize<Rejection>(STJson.ToJson(expected));

            actual.Code.Should().Be(expected.Code);
            actual.Reason.Should().Be(expected.Reason);
            actual.Type.Should().Be(RejectionType.Temporary);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void saved_rejections_without_codes_should_still_load(bool legacySerializer)
        {
            const string json = "{\"reason\":\"Legacy rejection\"}";
            var actual = legacySerializer ? JsonConvert.DeserializeObject<Rejection>(json) : STJson.Deserialize<Rejection>(json);

            actual.Code.Should().Be(RejectionCode.Unknown);
            actual.Reason.Should().Be("Legacy rejection");
            actual.Type.Should().Be(RejectionType.Permanent);
            actual.ToString().Should().Be("[Permanent] Legacy rejection");
        }

        [TestCase(RejectionType.Permanent, false, true)]
        [TestCase(RejectionType.Temporary, true, false)]
        public void severity_should_remain_independent_of_known_or_future_codes(RejectionType type, bool temporary, bool permanent)
        {
            var decision = new DownloadDecision(null,
                new Rejection("Known reason", type, RejectionCode.MinimumAge),
                new Rejection("Future reason", type, "future-code"));

            decision.Approved.Should().BeFalse();
            decision.TemporarilyRejected.Should().Be(temporary);
            decision.Rejected.Should().Be(permanent);
        }
    }
}
