using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
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
using NzbDrone.Core.Authentication;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Validation.Paths;
using NzbDrone.Test.Common;
using Readarr.Api.V1.Config;

namespace NzbDrone.App.Test
{
    [TestFixture]
    public class MultipleAccountHostConfigFixture : TestBase
    {
        private List<User> _accounts;
        private Mock<IUserRepository> _repository;
        private Mock<IConfigFileProvider> _file;
        private Mock<IConfigService> _config;
        private TestController _controller;
        private Dictionary<string, object> _saved;

        [SetUp]
        public void Setup()
        {
            _saved = null;
            _accounts = new List<User>
            {
                new User { Id = 11, Identifier = Guid.NewGuid(), Username = "first", Password = new string('a', 64) },
                new User { Id = 17, Identifier = Guid.NewGuid(), Username = "second", Password = new string('b', 64) }
            };
            _repository = new Mock<IUserRepository>();
            _repository.Setup(repository => repository.All()).Returns(() => _accounts);
            _repository.Setup(repository => repository.FindUser(It.IsAny<Guid>()))
                .Returns<Guid>(identifier => _accounts.SingleOrDefault(account => account.Identifier == identifier));
            _repository.Setup(repository => repository.TryUpdateCredentials(It.IsAny<User>(), It.IsAny<User>(), It.IsAny<bool>(), It.IsAny<bool>()))
                .Returns((User original, User updated, bool rename, bool password) =>
                {
                    _accounts[_accounts.FindIndex(account => account.Id == original.Id)] = updated;
                    return true;
                });
            var service = new UserService(_repository.Object, Mock.Of<IAppFolderInfo>(), Mock.Of<IDiskProvider>());
            _file = new Mock<IConfigFileProvider>();
            _file.SetupGet(file => file.AuthenticationMethod).Returns(AuthenticationType.Forms);
            _file.SetupGet(file => file.ApiKey).Returns("preserved-api-key");
            _file.Setup(file => file.SaveConfigDictionary(It.IsAny<Dictionary<string, object>>()))
                .Callback<Dictionary<string, object>>(settings => _saved = settings);
            _config = new Mock<IConfigService>();
            _controller = new TestController(_file.Object, _config.Object, service, new FileExistsValidator(Mock.Of<IDiskProvider>()))
            {
                ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
            };
        }

        [Test]
        public void api_key_settings_without_a_selected_account_should_return_safe_choices()
        {
            var resource = _controller.GetHostConfig();

            resource.UserIdentifier.Should().BeNull();
            resource.AccountSelectionRequired.Should().BeTrue();
            resource.Username.Should().BeEmpty();
            resource.Password.Should().BeEmpty();
            resource.Accounts.Select(account => account.Identifier).Should().Equal(_accounts.Select(account => account.Identifier));
            resource.Accounts.Should().OnlyContain(account => account.HasPassword);
            var json = resource.ToJson();
            foreach (var account in _accounts)
            {
                json.Should().NotContain(account.Password);
            }

            json.Should().NotContain("\"salt\"").And.NotContain("\"iterations\"");
            resource.ApiKey.Should().Be("preserved-api-key");
        }

        [TestCase(AuthenticationType.Forms)]
        [TestCase(AuthenticationType.Basic)]
        public void authenticated_account_should_be_selected_without_selecting_the_first_row(AuthenticationType method)
        {
            Actor(_accounts[1], method);

            var resource = _controller.GetHostConfig();

            resource.UserIdentifier.Should().Be(_accounts[1].Identifier);
            resource.Username.Should().Be("second");
            resource.Password.Should().Be("********");
            resource.AccountSelectionRequired.Should().BeFalse();
            resource.Accounts.Should().ContainSingle().Which.Identifier.Should().Be(_accounts[1].Identifier);
            Assert.Throws<ValidationException>(() => _controller.GetHostConfig(_accounts[0].Identifier));
        }

        [Test]
        public void explicit_api_key_selection_should_update_only_that_account()
        {
            var untouched = _accounts[0];
            var target = _accounts[1];
            var resource = Settings();
            resource.UserIdentifier = target.Identifier;
            resource.Username = "renamed-second";
            resource.Password = "********";

            _controller.SaveHostConfig(resource);

            _accounts[0].Should().BeSameAs(untouched);
            _accounts[1].Id.Should().Be(target.Id);
            _accounts[1].Identifier.Should().Be(target.Identifier);
            _accounts[1].Username.Should().Be("renamed-second");
            _accounts[1].Password.Should().Be(target.Password);
            _saved.Should().NotContainKey(nameof(resource.Password)).And.NotContainKey(nameof(resource.PasswordConfirmation));
            _saved.Should().NotContainKey(nameof(resource.UserIdentifier)).And.NotContainKey(nameof(resource.Accounts))
                .And.NotContainKey(nameof(resource.AccountSelectionRequired));
            _repository.Verify(repository => repository.Insert(It.IsAny<User>()), Times.Never());
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ambiguous_credential_or_authentication_changes_should_fail_before_other_settings_are_written(bool authentication)
        {
            var resource = Settings();
            if (authentication)
            {
                resource.AuthenticationMethod = AuthenticationType.None;
            }
            else
            {
                resource.Username = "unselected-account";
                resource.Password = "new private password";
                resource.PasswordConfirmation = resource.Password;
            }

            Assert.Throws<ValidationException>(() => _controller.SaveHostConfig(resource));

            NoWrites();
        }

        [Test]
        public void unselected_general_settings_save_should_preserve_all_credentials()
        {
            var originals = _accounts.ToArray();
            var resource = Settings();

            _controller.SaveHostConfig(resource);

            _saved.Should().NotBeNull();
            _accounts.Should().Equal(originals);
            _repository.Verify(repository => repository.TryUpdateCredentials(It.IsAny<User>(), It.IsAny<User>(), It.IsAny<bool>(), It.IsAny<bool>()), Times.Never());
        }

        [Test]
        public void a_stale_account_selector_should_fail_before_any_settings_are_written()
        {
            var resource = Settings();
            resource.UserIdentifier = Guid.NewGuid();
            resource.Username = "missing";

            Assert.Throws<ValidationException>(() => _controller.SaveHostConfig(resource));

            NoWrites();
        }

        [Test]
        public void authenticated_account_cannot_save_another_accounts_credentials()
        {
            Actor(_accounts[1], AuthenticationType.Forms);
            var resource = Settings();
            resource.UserIdentifier = _accounts[0].Identifier;
            resource.Username = "changed-first";
            resource.Password = "********";

            Assert.Throws<ValidationException>(() => _controller.SaveHostConfig(resource));

            NoWrites();
        }

        [Test]
        public void a_username_collision_should_fail_before_other_settings_are_written()
        {
            var resource = Settings();
            resource.UserIdentifier = _accounts[1].Identifier;
            resource.Username = "FIRST";
            resource.Password = "********";

            Assert.Throws<ValidationException>(() => _controller.SaveHostConfig(resource));

            NoWrites();
        }

        [Test]
        public void ambiguous_general_changes_should_also_pass_the_shared_credential_validation()
        {
            var resource = Settings();
            resource.BindAddress = "*";
            resource.Port = 8787;
            resource.SslPort = 8788;
            resource.UrlBase = "";
            resource.InstanceName = "Readarr";
            resource.Branch = "develop";
            resource.BackupFolder = "Backups";
            resource.BackupInterval = 7;
            resource.BackupRetention = 28;

            _controller.Validate(resource).IsValid.Should().BeTrue();
        }

        private HostConfigResource Settings()
        {
            return new HostConfigResource { Id = 1, AuthenticationMethod = AuthenticationType.Forms, Username = "", Password = "", PasswordConfirmation = "" };
        }

        private void Actor(User account, AuthenticationType type)
        {
            _controller.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity(
                new[]
                {
                    new Claim("identifier", account.Identifier.ToString()),
                    new Claim("user", account.Username),
                    new Claim("AuthType", type.ToString())
                },
                type.ToString(),
                "user",
                "identifier"));
        }

        private void NoWrites()
        {
            _file.Verify(file => file.SaveConfigDictionary(It.IsAny<Dictionary<string, object>>()), Times.Never());
            _config.Verify(config => config.SaveConfigDictionary(It.IsAny<Dictionary<string, object>>()), Times.Never());
            _repository.Verify(repository => repository.TryUpdateCredentials(It.IsAny<User>(), It.IsAny<User>(), It.IsAny<bool>(), It.IsAny<bool>()), Times.Never());
            _repository.Verify(repository => repository.Insert(It.IsAny<User>()), Times.Never());
        }

        private class TestController : HostConfigController
        {
            public TestController(IConfigFileProvider file, IConfigService config, IUserService users, FileExistsValidator exists)
                : base(file, config, users, exists)
            {
            }

            public ValidationResult Validate(HostConfigResource resource) => SharedValidator.Validate(resource);
        }
    }
}
