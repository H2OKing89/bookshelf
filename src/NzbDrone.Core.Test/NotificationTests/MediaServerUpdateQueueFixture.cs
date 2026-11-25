using System;
using System.Collections.Generic;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Common.Cache;
using NzbDrone.Core.Notifications;
using NzbDrone.Core.Notifications.Plex.Server;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.NotificationTests
{
    public class MediaServerUpdateQueueFixture : CoreTest
    {
        private ICacheManager _cache;
        private MediaServerUpdateQueue<PlexServer> _queue;
        private NotificationDefinition _definition;

        [SetUp]
        public void Setup()
        {
            _cache = Mocker.Resolve<CacheManager>();
            _queue = new MediaServerUpdateQueue<PlexServer>(_cache);
            _definition = Definition(1, 32400);
        }

        private static NotificationDefinition Definition(int id, int port)
        {
            return new NotificationDefinition { Id = id, Settings = new PlexServerSettings { Host = "owned", Port = port } };
        }

        [Test]
        public void deduplicates_targets_and_shares_work_between_provider_instances()
        {
            _queue.Add(_definition, new[] { "author", "author", "other" });
            var other = new MediaServerUpdateQueue<PlexServer>(_cache);
            other.Process(Definition(1, 32400), paths => paths.Should().BeEquivalentTo(new[] { "author", "other" })).Should().BeTrue();
            _queue.Process(_definition, _ => Assert.Fail("Already drained")).Should().BeFalse();
        }

        [Test]
        public void arrivals_during_refresh_are_retained_and_no_second_worker_claims_the_queue()
        {
            var processed = new List<string>();
            _queue.Add(_definition, new[] { "first" });

            _queue.Process(_definition, batch =>
            {
                processed.AddRange(batch);
                if (batch.Contains("first"))
                {
                    _queue.Add(_definition, new[] { "new" });
                    _queue.Process(_definition, _ => Assert.Fail("Duplicate worker")).Should().BeFalse();
                }
            }).Should().BeTrue();

            processed.Should().Equal("first", "new");
        }

        [Test]
        public void exception_releases_worker_and_restores_failed_targets_alongside_new_arrivals()
        {
            _queue.Add(_definition, new[] { "first", "second" });

            Assert.Throws<InvalidOperationException>(() => _queue.Process(_definition, _ =>
            {
                _queue.Add(_definition, new[] { "third" });
                throw new InvalidOperationException("Transient owned failure");
            }));

            _queue.Process(_definition, batch => batch.Should().BeEquivalentTo(new[] { "first", "second", "third" })).Should().BeTrue();
            _queue.Process(_definition, _ => Assert.Fail("Already drained")).Should().BeFalse();
        }

        [Test]
        public void connection_id_and_configuration_isolate_target_queues_even_with_same_host()
        {
            _queue.Add(_definition, new[] { "first" });
            _queue.Add(Definition(2, 32400), new[] { "second" });
            _queue.Add(Definition(1, 32401), new[] { "third" });

            _queue.Process(Definition(1, 32401), batch => batch.Should().Equal("third")).Should().BeTrue();
            _queue.Process(Definition(2, 32400), batch => batch.Should().Equal("second")).Should().BeTrue();
            _queue.Process(_definition, batch => batch.Should().Equal("first")).Should().BeTrue();
        }
    }
}
