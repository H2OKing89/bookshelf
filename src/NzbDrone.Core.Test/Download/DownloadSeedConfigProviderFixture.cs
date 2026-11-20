using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Download;
using NzbDrone.Core.Download.Clients;
using NzbDrone.Core.Download.History;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Download
{
    [TestFixture]
    public class DownloadSeedConfigProviderFixture : CoreTest<DownloadSeedConfigProvider>
    {
        private void GivenGrab(int clientId, int indexerId)
        {
            Mocker.GetMock<IDownloadHistoryService>().Setup(history => history.GetLatestGrab("ABC123", clientId)).Returns(new DownloadHistory
            {
                DownloadId = "ABC123", DownloadClientId = clientId, IndexerId = indexerId,
                Release = new ReleaseInfo { Title = "Audio Author - Audio Book [2018 - FLAC]" }
            });
        }

        [Test]
        public void the_same_hash_uses_separate_client_history_and_cached_indexer_policies()
        {
            GivenGrab(1, 11);
            GivenGrab(2, 22);
            var first = new TorrentSeedConfiguration { Ratio = 1 };
            var second = new TorrentSeedConfiguration { Ratio = 2 };
            Mocker.GetMock<ISeedConfigProvider>().Setup(provider => provider.GetSeedConfiguration(11, false)).Returns(first);
            Mocker.GetMock<ISeedConfigProvider>().Setup(provider => provider.GetSeedConfiguration(22, false)).Returns(second);

            Subject.GetSeedConfiguration("abc123", 1).Should().BeSameAs(first);
            Subject.GetSeedConfiguration("ABC123", 2).Should().BeSameAs(second);
            Subject.GetSeedConfiguration("ABC123", 1).Should().BeSameAs(first);

            Mocker.GetMock<IDownloadHistoryService>().Verify(history => history.GetLatestGrab("ABC123", 1), Times.Once());
            Mocker.GetMock<IDownloadHistoryService>().Verify(history => history.GetLatestGrab("ABC123", 2), Times.Once());
            Mocker.GetMock<IDownloadHistoryService>().Verify(history => history.GetLatestGrab(It.IsAny<string>()), Times.Never());
        }

        [Test]
        public void missing_client_ownership_cannot_borrow_a_global_seed_policy()
        {
            Subject.GetSeedConfiguration("ABC123").Should().BeNull();
            Subject.GetSeedConfiguration("ABC123", 0).Should().BeNull();
            Subject.GetSeedConfiguration("ABC123", -1).Should().BeNull();

            Mocker.GetMock<IDownloadHistoryService>().Verify(history => history.GetLatestGrab(It.IsAny<string>()), Times.Never());
            Mocker.GetMock<IDownloadHistoryService>().Verify(history => history.GetLatestGrab(It.IsAny<string>(), It.IsAny<int>()), Times.Never());
        }

        [Test]
        public void a_new_grab_invalidates_only_its_own_client_hash_policy()
        {
            GivenGrab(1, 11);
            GivenGrab(2, 22);
            Subject.GetSeedConfiguration("ABC123", 1);
            Subject.GetSeedConfiguration("ABC123", 2);

            Subject.Handle(new BookGrabbedEvent(null) { DownloadId = "abc123", DownloadClientId = 1 });
            Subject.GetSeedConfiguration("ABC123", 1);
            Subject.GetSeedConfiguration("ABC123", 2);

            Mocker.GetMock<IDownloadHistoryService>().Verify(history => history.GetLatestGrab("ABC123", 1), Times.Exactly(2));
            Mocker.GetMock<IDownloadHistoryService>().Verify(history => history.GetLatestGrab("ABC123", 2), Times.Once());
        }
    }
}
