using System;
using System.Collections.Generic;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Download.History;
using NzbDrone.Core.History;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.HistoryTests
{
    [TestFixture]
    public class ClientScopedHistoryFixture : CoreTest<HistoryService>
    {
        [Test]
        public void explicit_client_history_should_never_cross_clients_or_accept_conflicting_aliases()
        {
            var own = Row("DownloadClientId", "12");
            var foreign = Row("downloadClientId", "13");
            var malformed = Row("downloadClientId", "invalid");
            var conflicting = Row("DownloadClientId", "12");
            conflicting.Data.Add("downloadClientId", "13");
            Mocker.GetMock<IHistoryRepository>().Setup(repository => repository.FindByDownloadId("shared"))
                .Returns(new List<EntityHistory> { foreign, malformed, conflicting, own, Row(null, null) });

            Subject.FindByDownloadId("shared", 12).Should().Equal(own);
        }

        [TestCase(true)]
        [TestCase(false)]
        public void legacy_history_requires_a_unique_persisted_client_binding(bool unique)
        {
            var legacy = Row(null, null);
            var bindings = new List<DownloadHistory> { new DownloadHistory { DownloadClientId = 12 } };
            if (!unique)
            {
                bindings.Add(new DownloadHistory { DownloadClientId = 13 });
            }

            Mocker.GetMock<IHistoryRepository>().Setup(repository => repository.FindByDownloadId("shared"))
                .Returns(new List<EntityHistory> { legacy });
            Mocker.GetMock<IDownloadHistoryRepository>().Setup(repository => repository.FindByDownloadId("shared")).Returns(bindings);

            Subject.FindByDownloadId("shared", 12).Count.Should().Be(unique ? 1 : 0);
            Subject.FindByDownloadId("shared", 13).Should().BeEmpty();
        }

        private static EntityHistory Row(string key, string value)
        {
            var row = new EntityHistory { DownloadId = "shared", EventType = EntityHistoryEventType.Grabbed, Date = DateTime.UtcNow };
            if (key != null)
            {
                row.Data.Add(key, value);
            }

            return row;
        }
    }
}
