using System;
using System.Collections.Generic;
using FizzWare.NBuilder;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Download;
using NzbDrone.Core.Download.Clients;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Download
{
    [TestFixture]
    public class DownloadClientProviderFixture : CoreTest<DownloadClientProvider>
    {
        private List<IDownloadClient> _downloadClients;
        private List<DownloadClientStatus> _blockedProviders;
        private int _nextId;

        [SetUp]
        public void SetUp()
        {
            _downloadClients = new List<IDownloadClient>();
            _blockedProviders = new List<DownloadClientStatus>();
            _nextId = 1;

            Mocker.GetMock<IDownloadClientFactory>()
                  .Setup(v => v.GetAvailableProviders())
                  .Returns(_downloadClients);

            Mocker.GetMock<IDownloadClientStatusService>()
                  .Setup(v => v.GetBlockedProviders())
                  .Returns(_blockedProviders);
        }

        private Mock<IDownloadClient> WithUsenetClient(int priority = 0, HashSet<int> tags = null)
        {
            var mock = new Mock<IDownloadClient>(MockBehavior.Default);
            mock.SetupGet(s => s.Definition)
                .Returns(Builder<DownloadClientDefinition>
                    .CreateNew()
                    .With(v => v.Id = _nextId++)
                    .With(v => v.Priority = priority)
                    .With(v => v.Tags = tags ?? new HashSet<int>())
                    .Build());

            _downloadClients.Add(mock.Object);

            mock.SetupGet(v => v.Protocol).Returns(DownloadProtocol.Usenet);

            return mock;
        }

        private Mock<IDownloadClient> WithTorrentClient(int priority = 0, HashSet<int> tags = null)
        {
            var mock = new Mock<IDownloadClient>(MockBehavior.Default);
            mock.SetupGet(s => s.Definition)
                .Returns(Builder<DownloadClientDefinition>
                    .CreateNew()
                    .With(v => v.Id = _nextId++)
                    .With(v => v.Priority = priority)
                    .With(v => v.Tags = tags ?? new HashSet<int>())
                    .Build());

            _downloadClients.Add(mock.Object);

            mock.SetupGet(v => v.Protocol).Returns(DownloadProtocol.Torrent);

            return mock;
        }

        private void WithTorrentIndexer(int downloadClientId)
        {
            Mocker.GetMock<IIndexerFactory>()
                .Setup(v => v.Find(It.IsAny<int>()))
                .Returns(Builder<IndexerDefinition>
                    .CreateNew()
                    .With(v => v.Id = _nextId++)
                    .With(v => v.DownloadClientId = downloadClientId)
                    .Build());
        }

        private void GivenBlockedClient(int id)
        {
            _blockedProviders.Add(new DownloadClientStatus
            {
                ProviderId = id,
                DisabledTill = DateTime.UtcNow.AddHours(3)
            });
        }

        private Mock<IDownloadClient> WithClient(DownloadProtocol protocol, int priority = 0, HashSet<int> tags = null)
        {
            return protocol == DownloadProtocol.Torrent ? WithTorrentClient(priority, tags) : WithUsenetClient(priority, tags);
        }

        [TestCase(DownloadProtocol.Usenet, false)]
        [TestCase(DownloadProtocol.Usenet, true)]
        [TestCase(DownloadProtocol.Torrent, false)]
        [TestCase(DownloadProtocol.Torrent, true)]
        public void untagged_author_should_only_select_an_untagged_client(DownloadProtocol protocol, bool omittedTags)
        {
            WithClient(protocol, 0, new HashSet<int> { 1 });
            var untagged = WithClient(protocol, 10);

            var client = Subject.GetDownloadClient(protocol, tags: omittedTags ? null : new HashSet<int>());

            client.Should().BeSameAs(untagged.Object);
        }

        [TestCase(DownloadProtocol.Usenet, false)]
        [TestCase(DownloadProtocol.Usenet, true)]
        [TestCase(DownloadProtocol.Torrent, false)]
        [TestCase(DownloadProtocol.Torrent, true)]
        public void untagged_author_should_fail_when_only_tagged_clients_exist(DownloadProtocol protocol, bool omittedTags)
        {
            WithClient(protocol, tags: new HashSet<int> { 1 });

            Assert.Throws<DownloadClientUnavailableException>(() =>
                Subject.GetDownloadClient(protocol, tags: omittedTags ? null : new HashSet<int>()))
                .Message.Should().Contain("matching author tag");
        }

        [TestCase(DownloadProtocol.Usenet)]
        [TestCase(DownloadProtocol.Torrent)]
        public void no_client_for_protocol_should_keep_the_existing_null_result(DownloadProtocol protocol)
        {
            var otherProtocol = protocol == DownloadProtocol.Torrent ? DownloadProtocol.Usenet : DownloadProtocol.Torrent;
            WithClient(otherProtocol, tags: new HashSet<int> { 1 });

            Subject.GetDownloadClient(protocol, tags: new HashSet<int> { 1 }).Should().BeNull();
        }

        [Test]
        public void any_matching_author_tag_should_take_precedence_over_default_client_priority()
        {
            WithTorrentClient(0);
            WithTorrentClient(0, new HashSet<int> { 4 });
            var matching = WithTorrentClient(10, new HashSet<int> { 2, 3 });

            var client = Subject.GetDownloadClient(DownloadProtocol.Torrent, tags: new HashSet<int> { 1, 2 });

            client.Should().BeSameAs(matching.Object);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void indexer_override_should_not_authorize_a_tagged_client_for_an_untagged_author(bool omittedTags)
        {
            var tagged = WithTorrentClient(tags: new HashSet<int> { 1 });
            WithTorrentClient();
            WithTorrentIndexer(tagged.Object.Definition.Id);

            Assert.Throws<DownloadClientUnavailableException>(() =>
                Subject.GetDownloadClient(DownloadProtocol.Torrent, 1, tags: omittedTags ? null : new HashSet<int>()));
        }

        [Test]
        public void indexer_override_should_not_authorize_a_client_with_unmatched_tags()
        {
            var unmatched = WithTorrentClient(tags: new HashSet<int> { 2 });
            WithTorrentClient();
            WithTorrentIndexer(unmatched.Object.Definition.Id);

            Assert.Throws<DownloadClientUnavailableException>(() =>
                Subject.GetDownloadClient(DownloadProtocol.Torrent, 1, tags: new HashSet<int> { 1 }));
        }

        [Test]
        public void indexer_override_should_keep_the_existing_matching_tag_preference()
        {
            var untagged = WithTorrentClient();
            WithTorrentClient(tags: new HashSet<int> { 1 });
            WithTorrentIndexer(untagged.Object.Definition.Id);

            Assert.Throws<DownloadClientUnavailableException>(() =>
                Subject.GetDownloadClient(DownloadProtocol.Torrent, 1, tags: new HashSet<int> { 1 }));
        }

        [Test]
        public void indexer_override_should_select_an_untagged_default_when_no_tag_matches()
        {
            var untagged = WithTorrentClient(10);
            WithTorrentClient(0, new HashSet<int> { 2 });
            WithTorrentIndexer(untagged.Object.Definition.Id);

            Subject.GetDownloadClient(DownloadProtocol.Torrent, 1, tags: new HashSet<int> { 1 })
                .Should().BeSameAs(untagged.Object);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void eligible_indexer_override_should_retain_blocked_client_policy(bool filterBlockedClients)
        {
            var matching = WithTorrentClient(tags: new HashSet<int> { 1 });
            WithTorrentClient(tags: new HashSet<int> { 1 });
            WithTorrentIndexer(matching.Object.Definition.Id);
            GivenBlockedClient(matching.Object.Definition.Id);

            if (filterBlockedClients)
            {
                Assert.Throws<DownloadClientUnavailableException>(() =>
                    Subject.GetDownloadClient(DownloadProtocol.Torrent, 1, true, new HashSet<int> { 1 }));
            }
            else
            {
                Subject.GetDownloadClient(DownloadProtocol.Torrent, 1, false, new HashSet<int> { 1 })
                    .Should().BeSameAs(matching.Object);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void blocked_matching_client_should_not_fall_back_outside_the_existing_tag_selection(bool filterBlockedClients)
        {
            WithTorrentClient();
            var matching = WithTorrentClient(tags: new HashSet<int> { 1 });
            GivenBlockedClient(matching.Object.Definition.Id);

            if (filterBlockedClients)
            {
                Assert.Throws<DownloadClientUnavailableException>(() =>
                    Subject.GetDownloadClient(DownloadProtocol.Torrent, filterBlockedClients: true, tags: new HashSet<int> { 1 }));
            }
            else
            {
                Subject.GetDownloadClient(DownloadProtocol.Torrent, tags: new HashSet<int> { 1 })
                    .Should().BeSameAs(matching.Object);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void blocked_default_should_not_fall_back_to_a_tagged_client_for_an_untagged_author(bool filterBlockedClients)
        {
            var untagged = WithTorrentClient();
            WithTorrentClient(tags: new HashSet<int> { 1 });
            GivenBlockedClient(untagged.Object.Definition.Id);

            if (filterBlockedClients)
            {
                Assert.Throws<DownloadClientUnavailableException>(() =>
                    Subject.GetDownloadClient(DownloadProtocol.Torrent, filterBlockedClients: true));
            }
            else
            {
                Subject.GetDownloadClient(DownloadProtocol.Torrent).Should().BeSameAs(untagged.Object);
            }
        }

        [TestCase(DownloadProtocol.Usenet)]
        [TestCase(DownloadProtocol.Torrent)]
        public void untagged_author_should_roundrobin_only_the_eligible_first_priority_clients(DownloadProtocol protocol)
        {
            WithClient(protocol, 0, new HashSet<int> { 1 });
            var first = WithClient(protocol, 1);
            WithClient(protocol, 2);
            var second = WithClient(protocol, 1);

            Subject.GetDownloadClient(protocol).Should().BeSameAs(first.Object);
            Subject.GetDownloadClient(protocol).Should().BeSameAs(second.Object);
            Subject.GetDownloadClient(protocol).Should().BeSameAs(first.Object);
        }

        [Test]
        public void should_roundrobin_over_usenet_client()
        {
            WithUsenetClient();
            WithUsenetClient();
            WithUsenetClient();
            WithTorrentClient();

            var client1 = Subject.GetDownloadClient(DownloadProtocol.Usenet);
            var client2 = Subject.GetDownloadClient(DownloadProtocol.Usenet);
            var client3 = Subject.GetDownloadClient(DownloadProtocol.Usenet);
            var client4 = Subject.GetDownloadClient(DownloadProtocol.Usenet);
            var client5 = Subject.GetDownloadClient(DownloadProtocol.Usenet);

            client1.Definition.Id.Should().Be(1);
            client2.Definition.Id.Should().Be(2);
            client3.Definition.Id.Should().Be(3);
            client4.Definition.Id.Should().Be(1);
            client5.Definition.Id.Should().Be(2);
        }

        [Test]
        public void should_roundrobin_over_torrent_client()
        {
            WithUsenetClient();
            WithTorrentClient();
            WithTorrentClient();
            WithTorrentClient();

            var client1 = Subject.GetDownloadClient(DownloadProtocol.Torrent);
            var client2 = Subject.GetDownloadClient(DownloadProtocol.Torrent);
            var client3 = Subject.GetDownloadClient(DownloadProtocol.Torrent);
            var client4 = Subject.GetDownloadClient(DownloadProtocol.Torrent);
            var client5 = Subject.GetDownloadClient(DownloadProtocol.Torrent);

            client1.Definition.Id.Should().Be(2);
            client2.Definition.Id.Should().Be(3);
            client3.Definition.Id.Should().Be(4);
            client4.Definition.Id.Should().Be(2);
            client5.Definition.Id.Should().Be(3);
        }

        [Test]
        public void should_roundrobin_over_protocol_separately()
        {
            WithUsenetClient();
            WithTorrentClient();
            WithTorrentClient();

            var client1 = Subject.GetDownloadClient(DownloadProtocol.Usenet);
            var client2 = Subject.GetDownloadClient(DownloadProtocol.Torrent);
            var client3 = Subject.GetDownloadClient(DownloadProtocol.Torrent);
            var client4 = Subject.GetDownloadClient(DownloadProtocol.Torrent);

            client1.Definition.Id.Should().Be(1);
            client2.Definition.Id.Should().Be(2);
            client3.Definition.Id.Should().Be(3);
            client4.Definition.Id.Should().Be(2);
        }

        [Test]
        public void should_roundrobin_over_clients_with_matching_tags()
        {
            var seriesTags = new HashSet<int> { 1 };
            var clientTags = new HashSet<int> { 1 };

            WithTorrentClient();
            WithTorrentClient(0, clientTags);
            WithTorrentClient();
            WithTorrentClient(0, clientTags);

            var client1 = Subject.GetDownloadClient(DownloadProtocol.Torrent, 0, false, seriesTags);
            var client2 = Subject.GetDownloadClient(DownloadProtocol.Torrent, 0, false, seriesTags);
            var client3 = Subject.GetDownloadClient(DownloadProtocol.Torrent, 0, false, seriesTags);
            var client4 = Subject.GetDownloadClient(DownloadProtocol.Torrent, 0, false, seriesTags);

            client1.Definition.Id.Should().Be(2);
            client2.Definition.Id.Should().Be(4);
            client3.Definition.Id.Should().Be(2);
            client4.Definition.Id.Should().Be(4);
        }

        [Test]
        public void should_roundrobin_over_non_tagged_when_no_matching_tags()
        {
            var seriesTags = new HashSet<int> { 2 };
            var clientTags = new HashSet<int> { 1 };

            WithTorrentClient();
            WithTorrentClient(0, clientTags);
            WithTorrentClient();
            WithTorrentClient(0, clientTags);

            var client1 = Subject.GetDownloadClient(DownloadProtocol.Torrent, 0, false, seriesTags);
            var client2 = Subject.GetDownloadClient(DownloadProtocol.Torrent, 0, false, seriesTags);
            var client3 = Subject.GetDownloadClient(DownloadProtocol.Torrent, 0, false, seriesTags);
            var client4 = Subject.GetDownloadClient(DownloadProtocol.Torrent, 0, false, seriesTags);

            client1.Definition.Id.Should().Be(1);
            client2.Definition.Id.Should().Be(3);
            client3.Definition.Id.Should().Be(1);
            client4.Definition.Id.Should().Be(3);
        }

        [Test]
        public void should_fail_to_choose_when_clients_have_tags_but_no_match()
        {
            var seriesTags = new HashSet<int> { 2 };
            var clientTags = new HashSet<int> { 1 };

            WithTorrentClient(0, clientTags);

            Assert.Throws<DownloadClientUnavailableException>(() => Subject.GetDownloadClient(DownloadProtocol.Torrent, 0, false, seriesTags));
        }

        [Test]
        public void should_skip_blocked_torrent_client()
        {
            WithUsenetClient();
            WithTorrentClient();
            WithTorrentClient();
            WithTorrentClient();

            GivenBlockedClient(3);

            var client1 = Subject.GetDownloadClient(DownloadProtocol.Torrent);
            var client2 = Subject.GetDownloadClient(DownloadProtocol.Torrent);
            var client3 = Subject.GetDownloadClient(DownloadProtocol.Torrent);
            var client4 = Subject.GetDownloadClient(DownloadProtocol.Torrent);

            client1.Definition.Id.Should().Be(2);
            client2.Definition.Id.Should().Be(4);
            client3.Definition.Id.Should().Be(2);
            client4.Definition.Id.Should().Be(4);
        }

        [Test]
        public void should_not_skip_blocked_torrent_client_if_all_blocked()
        {
            WithUsenetClient();
            WithTorrentClient();
            WithTorrentClient();
            WithTorrentClient();

            GivenBlockedClient(2);
            GivenBlockedClient(3);
            GivenBlockedClient(4);

            var client1 = Subject.GetDownloadClient(DownloadProtocol.Torrent);
            var client2 = Subject.GetDownloadClient(DownloadProtocol.Torrent);
            var client3 = Subject.GetDownloadClient(DownloadProtocol.Torrent);
            var client4 = Subject.GetDownloadClient(DownloadProtocol.Torrent);
            var client5 = Subject.GetDownloadClient(DownloadProtocol.Torrent);

            client1.Definition.Id.Should().Be(2);
            client2.Definition.Id.Should().Be(3);
            client3.Definition.Id.Should().Be(4);
            client4.Definition.Id.Should().Be(2);
        }

        [Test]
        public void should_skip_secondary_prio_torrent_client()
        {
            WithUsenetClient();
            WithTorrentClient(2);
            WithTorrentClient();
            WithTorrentClient();

            var client1 = Subject.GetDownloadClient(DownloadProtocol.Torrent);
            var client2 = Subject.GetDownloadClient(DownloadProtocol.Torrent);
            var client3 = Subject.GetDownloadClient(DownloadProtocol.Torrent);
            var client4 = Subject.GetDownloadClient(DownloadProtocol.Torrent);
            var client5 = Subject.GetDownloadClient(DownloadProtocol.Torrent);

            client1.Definition.Id.Should().Be(3);
            client2.Definition.Id.Should().Be(4);
            client3.Definition.Id.Should().Be(3);
            client4.Definition.Id.Should().Be(4);
        }

        [Test]
        public void should_not_skip_secondary_prio_torrent_client_if_primary_blocked()
        {
            WithUsenetClient();
            WithTorrentClient(2);
            WithTorrentClient(2);
            WithTorrentClient();

            GivenBlockedClient(4);

            var client1 = Subject.GetDownloadClient(DownloadProtocol.Torrent);
            var client2 = Subject.GetDownloadClient(DownloadProtocol.Torrent);
            var client3 = Subject.GetDownloadClient(DownloadProtocol.Torrent);
            var client4 = Subject.GetDownloadClient(DownloadProtocol.Torrent);
            var client5 = Subject.GetDownloadClient(DownloadProtocol.Torrent);

            client1.Definition.Id.Should().Be(2);
            client2.Definition.Id.Should().Be(3);
            client3.Definition.Id.Should().Be(2);
            client4.Definition.Id.Should().Be(3);
        }

        [Test]
        public void should_always_choose_indexer_client()
        {
            WithUsenetClient();
            WithTorrentClient();
            WithTorrentClient();
            WithTorrentClient();
            WithTorrentIndexer(3);

            var client1 = Subject.GetDownloadClient(DownloadProtocol.Torrent, 1);
            var client2 = Subject.GetDownloadClient(DownloadProtocol.Torrent, 1);
            var client3 = Subject.GetDownloadClient(DownloadProtocol.Torrent, 1);
            var client4 = Subject.GetDownloadClient(DownloadProtocol.Torrent, 1);
            var client5 = Subject.GetDownloadClient(DownloadProtocol.Torrent, 1);

            client1.Definition.Id.Should().Be(3);
            client2.Definition.Id.Should().Be(3);
            client3.Definition.Id.Should().Be(3);
            client4.Definition.Id.Should().Be(3);
            client5.Definition.Id.Should().Be(3);
        }

        [Test]
        public void should_fail_to_choose_client_when_indexer_reference_does_not_exist()
        {
            WithUsenetClient();
            WithTorrentClient();
            WithTorrentClient();
            WithTorrentClient();
            WithTorrentIndexer(5);

            Assert.Throws<DownloadClientUnavailableException>(() => Subject.GetDownloadClient(DownloadProtocol.Torrent, 1));
        }
    }
}
