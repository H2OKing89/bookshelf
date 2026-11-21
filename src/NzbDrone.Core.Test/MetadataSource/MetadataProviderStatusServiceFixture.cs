using System;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MetadataSource
{
    [TestFixture]
    public class MetadataProviderStatusServiceFixture : CoreTest<MetadataProviderStatusService>
    {
        [Test]
        public void retains_the_previous_complete_time_until_provider_recovery()
        {
            var first = DateTime.UtcNow.AddMinutes(-10);
            var failure = first.AddMinutes(5);
            var retry = failure.AddMinutes(1);
            var recovery = retry.AddMinutes(1);
            Subject.GetStatus().Should().BeEmpty();

            Subject.Report(new MetadataProviderStatus(MetadataProvider.Audnexus, MetadataProviderOperation.RecordingDetails, MetadataProviderState.Complete, null, first, first, null));
            Subject.Report(new MetadataProviderStatus(MetadataProvider.Audnexus, MetadataProviderOperation.RecordingDetails, MetadataProviderState.RateLimited, MetadataProviderReason.TooManyRequests, failure, null, retry));
            var observed = Subject.GetStatus().Should().ContainSingle().Which;
            observed.LastCompleteAtUtc.Should().Be(first);
            observed.RetryAfterUtc.Should().Be(retry);

            Subject.Report(new MetadataProviderStatus(MetadataProvider.Audnexus, MetadataProviderOperation.RecordingDetails, MetadataProviderState.Complete, null, recovery, recovery, null));
            observed = Subject.GetStatus().Should().ContainSingle().Which;
            observed.State.Should().Be(MetadataProviderState.Complete);
            observed.LastCompleteAtUtc.Should().Be(recovery);
            observed.RetryAfterUtc.Should().BeNull();
        }
    }
}
