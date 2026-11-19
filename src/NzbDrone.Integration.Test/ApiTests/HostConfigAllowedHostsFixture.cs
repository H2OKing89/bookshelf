using System;
using System.Collections.Generic;
using FluentAssertions;
using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Serializer;
using NzbDrone.Core.Authentication;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Validation.Paths;
using NzbDrone.Test.Common;
using Readarr.Api.V1.Config;

namespace NzbDrone.Integration.Test.ApiTests
{
    [TestFixture]
    public class HostConfigAllowedHostsFixture : TestBase
    {
        private Mock<IConfigFileProvider> _file;
        private Mock<IConfigService> _config;
        private Mock<IUserService> _users;
        private TestController _controller;
        private string _storedHosts;
        private Dictionary<string, object> _saved;

        [SetUp]
        public void Setup()
        {
            _storedHosts = "books.test";
            _saved = null;
            var user = new User { Id = 42, Identifier = Guid.NewGuid(), Username = "reader", Password = "owned-digest" };
            _users = new Mock<IUserService>();
            _users.Setup(service => service.GetUsers()).Returns(new List<User> { user });
            _users.Setup(service => service.Upsert(user.Identifier, user.Username, null)).Returns(user);
            _file = new Mock<IConfigFileProvider>();
            _file.SetupGet(config => config.AllowedHosts).Returns(() => _storedHosts);
            _file.SetupGet(config => config.ApiKey).Returns("preserved-key");
            _file.SetupGet(config => config.BindAddress).Returns("*");
            _file.SetupGet(config => config.Port).Returns(8787);
            _file.SetupGet(config => config.SslPort).Returns(8788);
            _file.SetupGet(config => config.InstanceName).Returns("Readarr");
            _file.SetupGet(config => config.Branch).Returns("develop");
            _file.Setup(config => config.SaveConfigDictionary(It.IsAny<Dictionary<string, object>>()))
                .Callback<Dictionary<string, object>>(values =>
                {
                    _saved = values;
                    if (values.TryGetValue(nameof(HostConfigResource.AllowedHosts), out var value))
                    {
                        _storedHosts = (string)value;
                    }
                });
            _config = new Mock<IConfigService>();
            _config.SetupGet(config => config.BackupFolder).Returns("Backups");
            _config.SetupGet(config => config.BackupInterval).Returns(7);
            _config.SetupGet(config => config.BackupRetention).Returns(28);
            _controller = new TestController(_file.Object, _config.Object, _users.Object)
            {
                ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
            };
            _controller.Request.Method = "PUT";
        }

        [Test]
        public void configured_hosts_should_round_trip_with_safe_account_and_api_metadata()
        {
            var result = Json.Deserialize<HostConfigResource>(_controller.GetHostConfig().ToJson());

            result.AllowedHosts.Should().Be("books.test");
            result.ApiKey.Should().Be("preserved-key");
            result.Password.Should().Be("********");
        }

        [TestCase("{}")]
        [TestCase("{\"allowedHosts\":null}")]
        public void old_clients_should_preserve_the_saved_host_filter(string json)
        {
            var resource = _controller.GetHostConfig();
            resource.AllowedHosts = Json.Deserialize<HostConfigResource>(json).AllowedHosts;

            _controller.ValidateSettings(resource);
            _controller.SaveHostConfig(resource);

            _storedHosts.Should().Be("books.test");
            _saved.Should().NotContainKey(nameof(HostConfigResource.AllowedHosts));
            _saved[nameof(HostConfigResource.ApiKey)].Should().Be("preserved-key");
        }

        [TestCase("", "")]
        [TestCase(" \t\r\n ", "")]
        [TestCase(" Books.TEST; ::1; books.test ", "books.test, [::1]")]
        public void explicit_host_settings_should_normalize_and_clear_deliberately(string supplied, string expected)
        {
            var resource = _controller.GetHostConfig();
            resource.AllowedHosts = supplied;

            _controller.ValidateSettings(resource);
            _controller.SaveHostConfig(resource);

            _storedHosts.Should().Be(expected);
            _saved[nameof(HostConfigResource.AllowedHosts)].Should().Be(expected);
            _saved[nameof(HostConfigResource.EnableSsl)].Should().Be(false);
            _saved[nameof(HostConfigResource.ProxyPort)].Should().Be(0);
            _saved.Should().NotContainKey(nameof(HostConfigResource.Password));
        }

        [TestCase("*")]
        [TestCase("books.test:8787")]
        [TestCase("0.0.0.0")]
        [TestCase("books.test, invalid/host")]
        public void invalid_hosts_should_reject_validation_and_direct_saves_before_any_write(string invalid)
        {
            var resource = _controller.GetHostConfig();
            resource.AllowedHosts = invalid;

            Assert.Throws<ValidationException>(() => _controller.ValidateSettings(resource));
            Assert.Throws<ValidationException>(() => _controller.SaveHostConfig(resource));

            _file.Verify(config => config.SaveConfigDictionary(It.IsAny<Dictionary<string, object>>()), Times.Never());
            _config.Verify(config => config.SaveConfigDictionary(It.IsAny<Dictionary<string, object>>()), Times.Never());
            _users.Verify(service => service.Upsert(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never());
            _storedHosts.Should().Be("books.test");
        }

        private sealed class TestController : HostConfigController
        {
            public TestController(IConfigFileProvider file, IConfigService config, IUserService users)
                : base(file, config, users, new FileExistsValidator(Mock.Of<IDiskProvider>()))
            {
            }

            public void ValidateSettings(HostConfigResource resource)
            {
                ValidateResource(resource);
            }
        }
    }
}
