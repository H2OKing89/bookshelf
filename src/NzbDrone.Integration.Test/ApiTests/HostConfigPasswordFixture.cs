using System;
using System.Collections.Generic;
using FluentAssertions;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Common.EnvironmentInfo;
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
    public class HostConfigPasswordFixture : TestBase
    {
        private const string Password = "original password";
        private const string Mask = "********";
        private Mock<IUserRepository> _repository;
        private Mock<IConfigFileProvider> _configFile;
        private Mock<IConfigService> _config;
        private UserService _users;
        private TestHostConfigController _controller;
        private User _user;
        private Dictionary<string, object> _fileSettings;
        private Dictionary<string, object> _databaseSettings;

        [SetUp]
        public void Setup()
        {
            _fileSettings = null;
            _databaseSettings = null;
            _user = new User { Id = 42, Identifier = Guid.NewGuid(), Username = "reader", Password = Password.SHA256Hash() };
            _repository = new Mock<IUserRepository>();
            _repository.Setup(x => x.SingleOrDefault()).Returns(() => _user);
            _repository.Setup(x => x.All()).Returns(() => _user == null ? new List<User>() : new List<User> { _user });
            _repository.Setup(x => x.FindUser(It.IsAny<Guid>())).Returns<Guid>(identifier => _user?.Identifier == identifier ? _user : null);
            _repository.Setup(x => x.FindUser(It.IsAny<string>()))
                .Returns<string>(username => username == _user?.Username ? _user : null);
            _repository.Setup(x => x.TryUpdateCredentials(It.IsAny<User>(), It.IsAny<User>(), It.IsAny<bool>(), It.IsAny<bool>()))
                .Returns((User original, User updated, bool updateUsername, bool updatePassword) =>
                {
                    _user = updated;
                    return true;
                });
            _repository.Setup(x => x.Insert(It.IsAny<User>())).Returns<User>(user => _user = user);
            _users = new UserService(_repository.Object, Mock.Of<IAppFolderInfo>(), Mock.Of<IDiskProvider>());
            _configFile = new Mock<IConfigFileProvider>();
            _configFile.SetupGet(x => x.ApiKey).Returns("preserved-api-key");
            _configFile.Setup(x => x.SaveConfigDictionary(It.IsAny<Dictionary<string, object>>()))
                .Callback<Dictionary<string, object>>(settings => _fileSettings = settings);
            _config = new Mock<IConfigService>();
            _config.Setup(x => x.SaveConfigDictionary(It.IsAny<Dictionary<string, object>>()))
                .Callback<Dictionary<string, object>>(settings => _databaseSettings = settings);
            _controller = new TestHostConfigController(
                _configFile.Object,
                _config.Object,
                _users,
                new FileExistsValidator(Mock.Of<IDiskProvider>()))
            {
                ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
            };
        }

        private HostConfigResource Settings(AuthenticationType method, string password, string confirmation = "")
        {
            return new HostConfigResource
            {
                Id = 1,
                BindAddress = "*",
                Port = 8787,
                SslPort = 8788,
                UrlBase = "",
                InstanceName = "Readarr",
                Branch = "develop",
                AuthenticationMethod = method,
                Username = "reader",
                Password = password,
                PasswordConfirmation = confirmation,
                ApiKey = "preserved-api-key",
                BackupFolder = "Backups",
                BackupInterval = 7,
                BackupRetention = 28
            };
        }

        [TestCase(false)]
        [TestCase(true)]
        public void should_return_only_a_mask_for_existing_credentials(bool salted)
        {
            if (salted)
            {
                _users.FindUser("reader", Password);
            }

            var resource = _controller.GetHostConfig();

            resource.Username.Should().Be("reader");
            resource.Password.Should().Be(Mask);
            resource.PasswordConfirmation.Should().BeEmpty();
            resource.ApiKey.Should().Be("preserved-api-key");
            var json = resource.ToJson();
            json.Should().NotContain(Password).And.NotContain(_user.Password);
            json.Should().NotContain("\"salt\"").And.NotContain("\"iterations\"");
            if (salted)
            {
                json.Should().NotContain(_user.Salt);
            }
        }

        [Test]
        public void should_return_empty_credentials_without_an_account()
        {
            _user = null;

            var resource = _controller.GetHostConfig();

            resource.Username.Should().BeEmpty();
            resource.Password.Should().BeEmpty();
            resource.PasswordConfirmation.Should().BeEmpty();
        }

        [TestCase(AuthenticationType.Basic, false, Mask)]
        [TestCase(AuthenticationType.Basic, true, Mask)]
        [TestCase(AuthenticationType.Forms, false, Mask)]
        [TestCase(AuthenticationType.Forms, true, Mask)]
        [TestCase(AuthenticationType.Basic, false, null)]
        [TestCase(AuthenticationType.Basic, true, null)]
        [TestCase(AuthenticationType.Forms, false, null)]
        [TestCase(AuthenticationType.Forms, true, null)]
        public void should_preserve_unchanged_password_while_renaming(AuthenticationType method, bool salted, string password)
        {
            if (salted)
            {
                _users.FindUser("reader", Password);
            }

            var identifier = _user.Identifier;
            var hash = _user.Password;
            var salt = _user.Salt;
            var iterations = _user.Iterations;
            var resource = Settings(method, password);
            resource.Username = "RENAMED";

            _controller.ValidateSettings(resource).IsValid.Should().BeTrue();
            _controller.SaveHostConfig(resource);

            _user.Id.Should().Be(42);
            _user.Identifier.Should().Be(identifier);
            _user.Username.Should().Be("renamed");
            _user.Password.Should().Be(hash);
            _user.Salt.Should().Be(salt);
            _user.Iterations.Should().Be(iterations);
            _users.FindUser("renamed", Password).Should().BeSameAs(_user);
            _fileSettings.Should().NotContainKey("Password").And.NotContainKey("PasswordConfirmation");
            _databaseSettings.Should().NotContainKey("Password").And.NotContainKey("PasswordConfirmation");
            _fileSettings["ApiKey"].Should().Be("preserved-api-key");
            _fileSettings["EnableSsl"].Should().Be(false);
            _fileSettings["ProxyPort"].Should().Be(0);
        }

        [Test]
        public void should_preserve_omitted_json_password()
        {
            var resource = Settings(AuthenticationType.Forms, Mask);
            var omitted = Json.Deserialize<HostConfigResource>("{\"username\":\"reader\"}");
            omitted.Password.Should().BeNull();
            resource.Password = omitted.Password;

            _controller.ValidateSettings(resource).IsValid.Should().BeTrue();
            _controller.SaveHostConfig(resource);

            _user.Password.Should().Be(Password.SHA256Hash());
            _user.Salt.Should().BeNull();
        }

        [TestCase(AuthenticationType.Basic)]
        [TestCase(AuthenticationType.Forms)]
        public void should_allow_confirmed_password_changes(AuthenticationType method)
        {
            var identifier = _user.Identifier;
            var resource = Settings(method, "changed password", "changed password");

            _controller.ValidateSettings(resource).IsValid.Should().BeTrue();
            _controller.SaveHostConfig(resource);

            _user.Identifier.Should().Be(identifier);
            _user.Iterations.Should().Be(220000);
            _users.FindUser("reader", Password).Should().BeNull();
            _users.FindUser("reader", "changed password").Should().BeSameAs(_user);
            _fileSettings.Should().NotContainKey("Password").And.NotContainKey("PasswordConfirmation");
        }

        [TestCase(AuthenticationType.Basic, "new password", "different")]
        [TestCase(AuthenticationType.Forms, "new password", "")]
        [TestCase(AuthenticationType.Basic, "", "")]
        [TestCase(AuthenticationType.Forms, " ", " ")]
        public void should_reject_invalid_password_changes(AuthenticationType method, string password, string confirmation)
        {
            _controller.ValidateSettings(Settings(method, password, confirmation)).IsValid.Should().BeFalse();

            _user.Password.Should().Be(Password.SHA256Hash());
            _repository.Verify(x => x.TryUpdateCredentials(It.IsAny<User>(), It.IsAny<User>(), It.IsAny<bool>(), It.IsAny<bool>()), Times.Never());
        }

        [TestCase(AuthenticationType.Basic, Mask, "")]
        [TestCase(AuthenticationType.Forms, Mask, Mask)]
        [TestCase(AuthenticationType.Basic, null, "")]
        [TestCase(AuthenticationType.Forms, "", "")]
        public void should_reject_missing_or_reserved_first_password(AuthenticationType method, string password, string confirmation)
        {
            _user = null;
            var resource = Settings(method, password, confirmation);

            _controller.ValidateSettings(resource).IsValid.Should().BeFalse();
            _controller.SaveHostConfig(resource);

            _user.Should().BeNull();
            _repository.Verify(x => x.Insert(It.IsAny<User>()), Times.Never());
        }

        [TestCase(AuthenticationType.Basic)]
        [TestCase(AuthenticationType.Forms)]
        public void should_create_an_account_with_a_confirmed_password(AuthenticationType method)
        {
            _user = null;
            var resource = Settings(method, "new password", "new password");

            _controller.ValidateSettings(resource).IsValid.Should().BeTrue();
            _controller.SaveHostConfig(resource);

            _user.Iterations.Should().Be(220000);
            _users.FindUser("reader", "new password").Should().BeSameAs(_user);
        }

        [Test]
        public void should_accept_legacy_digest_round_trip_without_resetting_password()
        {
            var digest = _user.Password;
            var resource = Settings(AuthenticationType.Forms, digest);

            _controller.ValidateSettings(resource).IsValid.Should().BeTrue();
            _controller.SaveHostConfig(resource);

            _user.Password.Should().Be(digest);
            _user.Salt.Should().BeNull();
            _users.FindUser("reader", digest).Should().BeNull();
            _users.FindUser("reader", Password).Should().BeSameAs(_user);
        }

        [Test]
        public void should_reject_a_stale_digest_after_password_upgrade()
        {
            var oldDigest = _user.Password;
            var resource = Settings(AuthenticationType.Forms, oldDigest);
            _controller.ValidateSettings(resource).IsValid.Should().BeTrue();
            _users.FindUser("reader", Password);
            var hash = _user.Password;

            _controller.ValidateSettings(resource).IsValid.Should().BeFalse();
            Assert.Throws<ValidationException>(() => _controller.SaveHostConfig(resource));

            _user.Password.Should().Be(hash);
            _fileSettings.Should().BeNull();
            _databaseSettings.Should().BeNull();
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

            public ValidationResult ValidateSettings(HostConfigResource resource)
            {
                return SharedValidator.Validate(resource);
            }
        }
    }
}
