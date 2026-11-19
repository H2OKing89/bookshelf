using System;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.Authentication;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Authentication
{
    [TestFixture]
    public class AccountRecoveryFixture : DbTest<UserRepository, User>
    {
        private UserService _users;
        private User _first;
        private User _second;

        [SetUp]
        public void Setup()
        {
            _first = Subject.Insert(new User { Identifier = Guid.NewGuid(), Username = "first", Password = "first password".SHA256Hash() });
            _second = Subject.Insert(new User { Identifier = Guid.NewGuid(), Username = "second", Password = "second password".SHA256Hash() });
            _users = new UserService(Subject, Mock.Of<IAppFolderInfo>(), Mock.Of<IDiskProvider>());
        }

        [Test]
        public void password_recovery_should_change_only_the_selected_existing_account()
        {
            var updated = _users.ResetPassword(_second.Identifier, "new private password");

            updated.Id.Should().Be(_second.Id);
            updated.Identifier.Should().Be(_second.Identifier);
            updated.Username.Should().Be(_second.Username);
            updated.Iterations.Should().Be(220000);
            updated.Salt.Should().NotBeNullOrEmpty();
            Subject.Get(_first.Id).Should().BeEquivalentTo(_first);
            Subject.Count().Should().Be(2);
            _users.FindUser("second", "second password").Should().BeNull();
            _users.FindUser("second", "new private password").Identifier.Should().Be(_second.Identifier);
        }

        [Test]
        public void unknown_recovery_identity_should_not_create_an_account()
        {
            Assert.Throws<ModelNotFoundException>(() => _users.ResetPassword(Guid.NewGuid(), "new private password"));

            Subject.Count().Should().Be(2);
            Subject.Get(_first.Id).Should().BeEquivalentTo(_first);
            Subject.Get(_second.Id).Should().BeEquivalentTo(_second);
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("  ")]
        public void recovery_should_not_install_an_unusable_blank_password(string password)
        {
            Assert.Catch<ArgumentException>(() => _users.ResetPassword(_first.Identifier, password));

            Subject.Get(_first.Id).Should().BeEquivalentTo(_first);
            Subject.Get(_second.Id).Should().BeEquivalentTo(_second);
        }

        [Test]
        public void explicit_account_update_should_preserve_the_other_account_and_identity()
        {
            var updated = _users.Upsert(_second.Identifier, "RENAMED", null);

            updated.Username.Should().Be("renamed");
            updated.Id.Should().Be(_second.Id);
            updated.Identifier.Should().Be(_second.Identifier);
            updated.Password.Should().Be(_second.Password);
            Subject.Get(_first.Id).Should().BeEquivalentTo(_first);
        }

        [Test]
        public void explicit_account_update_should_reject_an_existing_username_without_changing_any_account()
        {
            Assert.Throws<ModelConflictException>(() => _users.Upsert(_second.Identifier, "FIRST", null));

            Subject.Get(_first.Id).Should().BeEquivalentTo(_first);
            Subject.Get(_second.Id).Should().BeEquivalentTo(_second);
        }

        [Test]
        public void recovery_conflicts_should_never_overwrite_a_newer_credential()
        {
            var repository = new Mock<IUserRepository>();
            repository.Setup(item => item.FindUser(_first.Identifier)).Returns(_first);
            repository.Setup(item => item.TryUpdateCredentials(It.IsAny<User>(), It.IsAny<User>(), false, true)).Returns(false);
            var service = new UserService(repository.Object, Mock.Of<IAppFolderInfo>(), Mock.Of<IDiskProvider>());

            Assert.Throws<ModelConflictException>(() => service.ResetPassword(_first.Identifier, "replacement password"));

            Subject.Get(_first.Id).Should().BeEquivalentTo(_first);
            repository.Verify(item => item.Insert(It.IsAny<User>()), Times.Never());
        }

        [Test]
        public void adding_an_existing_username_should_not_replace_or_duplicate_an_account()
        {
            Assert.Throws<ModelConflictException>(() => _users.Add("FIRST", "new password"));

            Subject.Count().Should().Be(2);
            Subject.Get(_first.Id).Should().BeEquivalentTo(_first);
        }

        [Test]
        public void unqualified_legacy_upsert_should_not_choose_the_first_of_multiple_accounts()
        {
            Assert.Throws<InvalidOperationException>(() => _users.Upsert("unselected", "replacement password"));

            Subject.Count().Should().Be(2);
            Subject.Get(_first.Id).Should().BeEquivalentTo(_first);
            Subject.Get(_second.Id).Should().BeEquivalentTo(_second);
        }
    }
}
