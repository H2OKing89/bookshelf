using System;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Common.Http;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Core.Update;

namespace NzbDrone.Core.Test.UpdateTests
{
    public class UpdatePackageProviderFixture : CoreTest<UpdatePackageProvider>
    {
        [SetUp]
        public void Setup()
        {
            Mocker.SetConstant<IBookshelfUpdatePolicy>(new BookshelfUpdatePolicy());
        }

        [TestCase("develop")]
        [TestCase("master")]
        [TestCase("nightly")]
        [TestCase("sonarr")]
        public void latest_update_should_not_contact_a_legacy_feed(string branch)
        {
            Subject.GetLatestUpdate(branch, new Version(0, 1)).Should().BeNull();

            Mocker.GetMock<IHttpClient>().VerifyNoOtherCalls();
        }

        [TestCase(null)]
        [TestCase("0.1")]
        public void changes_should_not_contact_a_legacy_feed(string previous)
        {
            var version = previous == null ? null : new Version(previous);

            Subject.GetRecentUpdates("develop", new Version(0, 2), version).Should().BeEmpty();

            Mocker.GetMock<IHttpClient>().VerifyNoOtherCalls();
        }
    }
}
