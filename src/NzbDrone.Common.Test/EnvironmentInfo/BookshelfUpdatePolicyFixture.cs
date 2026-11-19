using System;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Common.EnvironmentInfo;

namespace NzbDrone.Common.Test.EnvironmentInfo
{
    [TestFixture]
    public class BookshelfUpdatePolicyFixture
    {
        [Test]
        public void fork_without_a_compatible_feed_should_never_enable_builtin_updates()
        {
            var policy = new BookshelfUpdatePolicy();

            policy.IsEnabled.Should().BeFalse();
            policy.UnavailableReason.Should().Be(BookshelfUpdatePolicy.DisabledReason);
            Assert.Throws<InvalidOperationException>(policy.EnsureEnabled).Message.Should().Be(BookshelfUpdatePolicy.DisabledReason);
        }
    }
}
