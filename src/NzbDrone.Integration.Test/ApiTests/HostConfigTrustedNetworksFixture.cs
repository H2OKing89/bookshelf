using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Serializer;
using NzbDrone.Core;
using NzbDrone.Core.Authentication;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Validation.Paths;
using NzbDrone.Test.Common;
using Readarr.Api.V1.Config;

namespace NzbDrone.Integration.Test.ApiTests
{
    [TestFixture]
    public class HostConfigTrustedNetworksFixture : TestBase
    {
        private Mock<IConfigFileProvider> _configFile;
        private Mock<IConfigService> _config;
        private Mock<IUserService> _users;
        private TestHostConfigController _controller;
        private User _user;
        private string _storedNetworks;
        private bool _trustCgnat;
        private Dictionary<string, object> _fileSettings;
        private Dictionary<string, object> _databaseSettings;

        [SetUp]
        public void Setup()
        {
            _storedNetworks = "192.0.2.0/24";
            _trustCgnat = false;
            _user = new User
            {
                Id = 42,
                Identifier = Guid.NewGuid(),
                Username = "reader",
                Password = "existing password".SHA256Hash()
            };
            _users = new Mock<IUserService>();
            _users.Setup(x => x.FindUser()).Returns(_user);
            _users.Setup(x => x.Upsert(It.IsAny<string>(), It.IsAny<string>())).Returns(_user);
            _configFile = new Mock<IConfigFileProvider>();
            _configFile.SetupGet(x => x.TrustedNetworks).Returns(() => _storedNetworks);
            _configFile.SetupGet(x => x.TrustCgnatIpAddresses).Returns(() => _trustCgnat);
            _configFile.SetupGet(x => x.BindAddress).Returns("*");
            _configFile.SetupGet(x => x.Port).Returns(8787);
            _configFile.SetupGet(x => x.SslPort).Returns(8788);
            _configFile.SetupGet(x => x.UrlBase).Returns("");
            _configFile.SetupGet(x => x.InstanceName).Returns("Readarr");
            _configFile.SetupGet(x => x.Branch).Returns("develop");
            _configFile.SetupGet(x => x.AuthenticationMethod).Returns(AuthenticationType.Forms);
            _configFile.SetupGet(x => x.ApiKey).Returns("preserved-api-key");
            _configFile.Setup(x => x.SaveConfigDictionary(It.IsAny<Dictionary<string, object>>()))
                .Callback<Dictionary<string, object>>(settings =>
                {
                    _fileSettings = settings;
                    if (settings.TryGetValue(nameof(HostConfigResource.TrustedNetworks), out var value))
                    {
                        _storedNetworks = (string)value;
                    }
                });
            _config = new Mock<IConfigService>();
            _config.SetupGet(x => x.BackupFolder).Returns("Backups");
            _config.SetupGet(x => x.BackupInterval).Returns(7);
            _config.SetupGet(x => x.BackupRetention).Returns(28);
            _config.Setup(x => x.SaveConfigDictionary(It.IsAny<Dictionary<string, object>>()))
                .Callback<Dictionary<string, object>>(settings => _databaseSettings = settings);
            _controller = new TestHostConfigController(
                _configFile.Object,
                _config.Object,
                _users.Object,
                new FileExistsValidator(Mock.Of<IDiskProvider>()))
            {
                ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
            };
            _controller.Request.Method = "PUT";
        }

        [TestCase(false)]
        [TestCase(true)]
        public void should_return_configured_trusted_networks_and_the_runtime_cgnat_flag(bool trustCgnat)
        {
            _trustCgnat = trustCgnat;

            var resource = _controller.GetHostConfig();

            resource.TrustedNetworks.Should().Be(_storedNetworks);
            resource.TrustCgnatIpAddresses.Should().Be(trustCgnat);
            resource.Password.Should().Be("********");
            var roundTrip = Json.Deserialize<HostConfigResource>(resource.ToJson());
            roundTrip.TrustedNetworks.Should().Be(_storedNetworks);
            roundTrip.TrustCgnatIpAddresses.Should().Be(trustCgnat);
        }

        [TestCase("{}")]
        [TestCase("{\"trustedNetworks\":null}")]
        public void should_preserve_saved_networks_when_old_clients_omit_the_field(string json)
        {
            var resource = _controller.GetHostConfig();
            resource.TrustedNetworks = Json.Deserialize<HostConfigResource>(json).TrustedNetworks;

            _controller.ValidateSettings(resource);
            _controller.SaveHostConfig(resource);

            _storedNetworks.Should().Be("192.0.2.0/24");
            _fileSettings.Should().NotContainKey(nameof(HostConfigResource.TrustedNetworks));
            _databaseSettings.Should().NotContainKey(nameof(HostConfigResource.TrustedNetworks));
            AssertPreservedSettings();
        }

        [TestCase("")]
        [TestCase(" \t\r\n ")]
        public void should_clear_saved_networks_for_an_explicit_empty_value(string value)
        {
            var resource = _controller.GetHostConfig();
            resource.TrustedNetworks = value;

            _controller.ValidateSettings(resource);
            _controller.SaveHostConfig(resource);

            _storedNetworks.Should().BeEmpty();
            _fileSettings[nameof(HostConfigResource.TrustedNetworks)].Should().Be(string.Empty);
            _databaseSettings[nameof(HostConfigResource.TrustedNetworks)].Should().Be(string.Empty);
            _controller.GetHostConfig().TrustedNetworks.Should().BeEmpty();
            AssertPreservedSettings();
        }

        [TestCase(" 192.0.2.10 ", "192.0.2.10")]
        [TestCase(" 192.0.2.0/24 , 2001:DB8::/32 ", "192.0.2.0/24, 2001:DB8::/32")]
        [TestCase(" ::1 , 127.0.0.1 ", "::1, 127.0.0.1")]
        public void should_normalize_valid_supplied_networks_before_saving(string supplied, string expected)
        {
            var resource = _controller.GetHostConfig();
            resource.TrustedNetworks = supplied;

            _controller.ValidateSettings(resource);
            _controller.SaveHostConfig(resource);

            _storedNetworks.Should().Be(expected);
            _fileSettings[nameof(HostConfigResource.TrustedNetworks)].Should().Be(expected);
            _databaseSettings[nameof(HostConfigResource.TrustedNetworks)].Should().Be(expected);
            resource.TrustedNetworks.Should().Be(expected);
            AssertPreservedSettings();
        }

        [TestCase("not-a-network")]
        [TestCase("proxy.example.com")]
        [TestCase("192.0.2.0/not-a-prefix")]
        [TestCase("192.0.2.0/24, invalid")]
        [TestCase("192.0.2.10/33")]
        [TestCase("192.0.2.0/-1")]
        [TestCase("2001:db8::/129")]
        public void should_reject_invalid_networks_before_any_write(string invalid)
        {
            var resource = _controller.GetHostConfig();
            resource.TrustedNetworks = invalid;

            var validation = Assert.Throws<ValidationException>(() => _controller.ValidateSettings(resource));
            validation.Errors.Should().Contain(error => error.PropertyName == nameof(HostConfigResource.TrustedNetworks));
            validation.Errors.First(error => error.PropertyName == nameof(HostConfigResource.TrustedNetworks))
                .ErrorMessage.Should().Contain("CIDR");
            var save = Assert.Throws<ValidationException>(() => _controller.SaveHostConfig(resource));
            save.Errors.Should().Contain(error => error.PropertyName == nameof(HostConfigResource.TrustedNetworks));
            _configFile.Verify(x => x.SaveConfigDictionary(It.IsAny<Dictionary<string, object>>()), Times.Never());
            _config.Verify(x => x.SaveConfigDictionary(It.IsAny<Dictionary<string, object>>()), Times.Never());
            _users.Verify(x => x.Upsert(It.IsAny<string>(), It.IsAny<string>()), Times.Never());
            _storedNetworks.Should().Be("192.0.2.0/24");
        }

        [TestCase(false)]
        [TestCase(true)]
        public void should_preserve_the_configured_cgnat_flag_on_settings_round_trip(bool trustCgnat)
        {
            _trustCgnat = trustCgnat;
            var resource = _controller.GetHostConfig();

            _controller.ValidateSettings(resource);
            _controller.SaveHostConfig(resource);

            _fileSettings[nameof(HostConfigResource.TrustCgnatIpAddresses)].Should().Be(trustCgnat);
            _databaseSettings[nameof(HostConfigResource.TrustCgnatIpAddresses)].Should().Be(trustCgnat);
        }

        private void AssertPreservedSettings()
        {
            _fileSettings.Should().NotContainKey(nameof(HostConfigResource.Password))
                .And.NotContainKey(nameof(HostConfigResource.PasswordConfirmation));
            _databaseSettings.Should().NotContainKey(nameof(HostConfigResource.Password))
                .And.NotContainKey(nameof(HostConfigResource.PasswordConfirmation));
            _fileSettings[nameof(HostConfigResource.TrustCgnatIpAddresses)].Should().Be(false);
            _fileSettings[nameof(HostConfigResource.EnableSsl)].Should().Be(false);
            _fileSettings[nameof(HostConfigResource.ProxyPort)].Should().Be(0);
            _fileSettings[nameof(HostConfigResource.ApiKey)].Should().Be("preserved-api-key");
            _users.Verify(x => x.Upsert("reader", null), Times.Once());
            _user.Id.Should().Be(42);
            _user.Password.Should().Be("existing password".SHA256Hash());
            _user.Salt.Should().BeNull();
            _user.Iterations.Should().Be(0);
        }

        public class TestHostConfigController : HostConfigController
        {
            public TestHostConfigController(
                IConfigFileProvider configFileProvider,
                IConfigService configService,
                IUserService userService,
                FileExistsValidator fileExistsValidator)
                : base(configFileProvider, configService, userService, fileExistsValidator)
            {
            }

            public void ValidateSettings(HostConfigResource resource)
            {
                ValidateResource(resource);
            }
        }
    }
}
