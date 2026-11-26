using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Http;
using NzbDrone.Common.Serializer;
using NzbDrone.Core.Download.Clients;
using NzbDrone.Core.Download.Clients.QBittorrent;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Download.DownloadClientTests.QBittorrentTests
{
    [TestFixture]
    public class QBittorrentSeedingProxyFixture : CoreTest<QBittorrentProxyV2>
    {
        private readonly List<HttpRequest> _requests = new List<HttpRequest>();
        private QBittorrentSettings _settings;
        private string _version;

        [SetUp]
        public void Setup()
        {
            _requests.Clear();
            _version = "2.9.2";
            _settings = new QBittorrentSettings
            {
                Host = "127.0.0.1",
                ApiKey = "synthetic-test-key",
                InitialState = (int)QBittorrentState.ForceStart
            };
            Mocker.GetMock<IHttpClient>().Setup(client => client.Execute(It.IsAny<HttpRequest>()))
                .Returns<HttpRequest>(request =>
                {
                    _requests.Add(request);
                    var response = request.Url.FullUri.EndsWith("webapiVersion", StringComparison.Ordinal) ? _version : "Ok.";
                    return new HttpResponse(request, new HttpHeader(), Encoding.UTF8.GetBytes(response), HttpStatusCode.OK);
                });
        }

        private string Form => Encoding.UTF8.GetString(_requests.Single(request => request.Method == HttpMethod.Post).ContentData);

        [TestCase(false, "false")]
        [TestCase(true, "true")]
        public void removal_always_sends_explicit_delete_files_choice(bool deleteFiles, string expected)
        {
            Subject.RemoveTorrent("owned-native-handle", deleteFiles, _settings);
            Form.Split('&').Should().Contain("hashes=owned-native-handle").And.Contain($"deleteFiles={expected}");
        }

        [TestCase(QBittorrentSeedTimeType.Total, "seedingTimeLimit")]
        [TestCase(QBittorrentSeedTimeType.Inactive, "inactiveSeedingTimeLimit")]
        public void magnet_add_uses_only_selected_time_parameter(QBittorrentSeedTimeType mode, string parameter)
        {
            _settings.SeedTimeType = (int)mode;
            Subject.AddTorrentFromUrl("magnet:?xt=urn:btih:synthetic", new TorrentSeedConfiguration { Ratio = 1.5, SeedTime = TimeSpan.FromMinutes(45) }, _settings);
            var parameters = Form.Split('&');
            parameters.Should().Contain($"{parameter}=45").And.Contain("ratioLimit=1.5");
            parameters.Should().NotContain(mode == QBittorrentSeedTimeType.Total ? "inactiveSeedingTimeLimit=45" : "seedingTimeLimit=45");
            if (mode == QBittorrentSeedTimeType.Total)
            {
                _requests.Should().ContainSingle(); // Old clients need not make a new version request in default mode.
            }
        }

        [TestCase(QBittorrentSeedTimeType.Total, "seedingTimeLimit")]
        [TestCase(QBittorrentSeedTimeType.Inactive, "inactiveSeedingTimeLimit")]
        public void file_add_uses_selected_multipart_parameter(QBittorrentSeedTimeType mode, string parameter)
        {
            _settings.SeedTimeType = (int)mode;
            Subject.AddTorrentFromFile("owned.torrent", new byte[] { 1, 2 }, new TorrentSeedConfiguration { SeedTime = TimeSpan.FromMinutes(60) }, _settings);
            Form.Should().Contain($"name=\"{parameter}\"\r\n\r\n60\r\n");
            Form.Should().NotContain(mode == QBittorrentSeedTimeType.Total ? "name=\"inactiveSeedingTimeLimit\"" : "name=\"seedingTimeLimit\"");
        }

        [TestCase(null, -2)]
        [TestCase(-2, -2)]
        [TestCase(-1, -1)]
        [TestCase(0, 0)]
        [TestCase(120, 120)]
        public void explicit_share_limit_preserves_unlimited_and_global_semantics(int? minutes, int expected)
        {
            _settings.SeedTimeType = (int)QBittorrentSeedTimeType.Inactive;
            Subject.SetTorrentSeedingConfiguration("synthetic", new TorrentSeedConfiguration { SeedTime = minutes.HasValue ? TimeSpan.FromMinutes(minutes.Value) : null }, _settings);
            Form.Split('&').Should().Contain($"inactiveSeedingTimeLimit={expected}").And.Contain("ratioLimit=-2");
            Form.Split('&').Should().NotContain($"seedingTimeLimit={expected}");
        }

        [TestCase(null)]
        [TestCase(-2)]
        public void add_with_default_time_omits_both_time_parameters(int? minutes)
        {
            _settings.SeedTimeType = (int)QBittorrentSeedTimeType.Inactive;
            Subject.AddTorrentFromUrl("magnet:?xt=urn:btih:synthetic", new TorrentSeedConfiguration { Ratio = 2, SeedTime = minutes.HasValue ? TimeSpan.FromMinutes(minutes.Value) : null }, _settings);
            Form.Should().Contain("ratioLimit=2").And.NotContain("seedingTimeLimit").And.NotContain("inactiveSeedingTimeLimit");
        }

        [TestCase("2.9.1")]
        [TestCase("2.8.1")]
        public void incompatible_client_does_not_get_total_time_fallback(string version)
        {
            _version = version;
            _settings.SeedTimeType = (int)QBittorrentSeedTimeType.Inactive;
            var configuration = new TorrentSeedConfiguration { SeedTime = TimeSpan.FromMinutes(45) };
            Assert.Throws<DownloadClientException>(() => Subject.AddTorrentFromUrl("magnet:?xt=urn:btih:synthetic", configuration, _settings));
            Assert.Throws<DownloadClientException>(() => Subject.AddTorrentFromFile("owned.torrent", new byte[] { 1 }, configuration, _settings));
            Assert.Throws<DownloadClientException>(() => Subject.SetTorrentSeedingConfiguration("synthetic", configuration, _settings));
            _requests.Should().OnlyContain(request => request.Method == HttpMethod.Get);
        }

        [Test]
        public void settings_default_to_total_and_reject_unknown_values()
        {
            var settings = Json.Deserialize<QBittorrentSettings>("{\"host\":\"localhost\",\"port\":8080}");
            settings.SeedTimeType.Should().Be((int)QBittorrentSeedTimeType.Total);
            settings.Validate().IsValid.Should().BeTrue();
            settings.SeedTimeType = 99;
            settings.Validate().Errors.Should().Contain(error => error.PropertyName == nameof(settings.SeedTimeType));
        }
    }
}
