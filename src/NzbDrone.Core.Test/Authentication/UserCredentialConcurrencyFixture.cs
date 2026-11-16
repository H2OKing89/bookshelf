using System;
using System.Security.Cryptography;
using Dapper;
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
    public class UserCredentialConcurrencyFixture : DbTest<UserRepository, User>
    {
        private const string Password = "original password";
        private Mock<IUserRepository> _serviceRepository;
        private UserService _service;
        private User _original;
        private Action _beforeWrite;
        private int _credentialWrites;

        [SetUp]
        public void Setup()
        {
            _original = Db.Insert(new User
            {
                Identifier = Guid.NewGuid(),
                Username = "reader",
                Password = Password.SHA256Hash()
            });
            _beforeWrite = null;
            _credentialWrites = 0;
            _serviceRepository = new Mock<IUserRepository>();
            _serviceRepository.Setup(repository => repository.FindUser(It.IsAny<string>()))
                .Returns<string>(username => Subject.FindUser(username));
            _serviceRepository.Setup(repository => repository.SingleOrDefault())
                .Returns(() => Subject.SingleOrDefault());
            _serviceRepository.Setup(repository => repository.TryUpdateCredentials(
                    It.IsAny<User>(), It.IsAny<User>(), It.IsAny<bool>(), It.IsAny<bool>()))
                .Returns((User original, User updated, bool updateUsername, bool updatePassword) =>
                {
                    _credentialWrites++;
                    var beforeWrite = _beforeWrite;
                    _beforeWrite = null;
                    beforeWrite?.Invoke();
                    return Subject.TryUpdateCredentials(original, updated, updateUsername, updatePassword);
                });
            _service = new UserService(_serviceRepository.Object, Mock.Of<IAppFolderInfo>(), Mock.Of<IDiskProvider>());
        }

        [Test]
        public void password_upgrade_should_update_only_metadata_without_mutating_original_snapshot()
        {
            var original = Copy(_original);
            var updated = SaltedUser(_original, Password);
            updated.Username = "must-not-be-saved";
            updated.Identifier = Guid.NewGuid();
            updated.Id = int.MaxValue;

            Subject.TryUpdateCredentials(original, updated, false, true).Should().BeTrue();

            var stored = Subject.Get(_original.Id);
            stored.Id.Should().Be(_original.Id);
            stored.Identifier.Should().Be(_original.Identifier);
            stored.Username.Should().Be(_original.Username);
            AssertMetadata(stored, updated);
            original.Should().BeEquivalentTo(_original);
        }

        [TestCase("Id")]
        [TestCase("Identifier")]
        [TestCase("Username")]
        [TestCase("Password")]
        [TestCase("Salt")]
        [TestCase("Iterations")]
        public void password_upgrade_should_reject_a_stale_snapshot_without_overwriting_saved_values(string changedField)
        {
            var original = Copy(_original);
            switch (changedField)
            {
                case "Id":
                    original.Id = int.MaxValue;
                    break;
                case "Identifier":
                    original.Identifier = Guid.NewGuid();
                    break;
                case "Username":
                    original.Username = "previous-name";
                    break;
                case "Password":
                    original.Password = "previous password".SHA256Hash();
                    break;
                case "Salt":
                    original.Salt = "previous-salt";
                    break;
                case "Iterations":
                    original.Iterations = 1000;
                    break;
            }

            var snapshot = Copy(original);
            var updated = SaltedUser(_original, Password);

            Subject.TryUpdateCredentials(original, updated, false, true).Should().BeFalse();

            Subject.Single().Should().BeEquivalentTo(_original);
            original.Should().BeEquivalentTo(snapshot);
        }

        [Test]
        public void password_upgrade_should_match_legacy_null_salt_and_iterations()
        {
            using (var connection = Db.OpenConnection())
            {
                connection.Execute("UPDATE \"Users\" SET \"Salt\" = NULL, \"Iterations\" = NULL WHERE \"Id\" = @Id", new { _original.Id });
            }

            var original = Subject.Get(_original.Id);
            original.Salt.Should().BeNull();
            original.Iterations.Should().Be(0);
            var updated = SaltedUser(original, Password);

            Subject.TryUpdateCredentials(original, updated, false, true).Should().BeTrue();

            Subject.Single().Should().BeEquivalentTo(updated);
            original.Should().BeEquivalentTo(_original);
        }

        [TestCase(0)]
        [TestCase(1000)]
        public void login_should_reject_old_password_after_a_concurrent_reset(int iterations)
        {
            if (iterations > 0)
            {
                StorePassword(Password, iterations);
            }

            var beforeLogin = Subject.Single();
            User reset = null;
            _beforeWrite = () => reset = StorePassword("replacement password");

            _service.FindUser("reader", Password).Should().BeNull();

            Subject.Single().Should().BeEquivalentTo(reset);
            beforeLogin.Should().NotBeEquivalentTo(reset);
            _credentialWrites.Should().Be(1);
            VerifyLoginReads(2);
        }

        [Test]
        public void parallel_legacy_login_should_reverify_the_saved_upgrade_without_overwriting_it()
        {
            User concurrentLogin = null;
            _beforeWrite = () => concurrentLogin = StorePassword(Password);

            var authenticated = _service.FindUser("reader", Password);

            authenticated.Should().BeEquivalentTo(concurrentLogin);
            Subject.Single().Should().BeEquivalentTo(concurrentLogin);
            _credentialWrites.Should().Be(1);
            VerifyLoginReads(2);
            _original.Password.Should().Be(Password.SHA256Hash());
            _original.Salt.Should().BeNull();
            _original.Iterations.Should().Be(0);
        }

        [Test]
        public void login_should_stop_after_two_conflicting_upgrade_attempts()
        {
            User concurrentLogin = null;
            Action conflictingWrite = null;
            conflictingWrite = () =>
            {
                concurrentLogin = StorePassword(Password, 1000);
                _beforeWrite = conflictingWrite;
            };
            _beforeWrite = conflictingWrite;

            _service.FindUser("reader", Password).Should().BeNull();

            _credentialWrites.Should().Be(2);
            VerifyLoginReads(2);
            Subject.Single().Should().BeEquivalentTo(concurrentLogin);
            Subject.Single().Iterations.Should().Be(1000);
        }

        [Test]
        public void login_should_not_restore_a_concurrently_renamed_account()
        {
            var renamed = Copy(_original);
            renamed.Username = "renamed";
            _beforeWrite = () => Subject.Update(renamed);

            _service.FindUser("reader", Password).Should().BeNull();

            Subject.Single().Should().BeEquivalentTo(renamed);
            _credentialWrites.Should().Be(1);
            VerifyLoginReads(2);
        }

        [Test]
        public void settings_reset_should_commit_username_and_password_metadata_together()
        {
            var updated = _service.Upsert("RENAMED", "replacement password");

            Subject.Single().Should().BeEquivalentTo(updated);
            updated.Id.Should().Be(_original.Id);
            updated.Identifier.Should().Be(_original.Identifier);
            updated.Username.Should().Be("renamed");
            updated.Iterations.Should().Be(220000);
            _service.FindUser("renamed", "replacement password").Should().NotBeNull();
            _service.FindUser("renamed", Password).Should().BeNull();
            _original.Username.Should().Be("reader");
            _original.Password.Should().Be(Password.SHA256Hash());
            _original.Salt.Should().BeNull();
            _original.Iterations.Should().Be(0);
            _credentialWrites.Should().Be(1);
            _serviceRepository.Verify(repository => repository.TryUpdateCredentials(
                It.IsAny<User>(), It.IsAny<User>(), true, true), Times.Once());
        }

        [Test]
        public void settings_password_reset_should_conflict_with_a_parallel_login_upgrade()
        {
            User concurrentLogin = null;
            _beforeWrite = () => concurrentLogin = StorePassword(Password);

            Assert.Throws<ModelConflictException>(() => _service.Upsert("reader", "replacement password"));

            Subject.Single().Should().BeEquivalentTo(concurrentLogin);
            _credentialWrites.Should().Be(1);
            _service.FindUser("reader", Password).Should().NotBeNull();
            _service.FindUser("reader", "replacement password").Should().BeNull();
        }

        [TestCase(false)]
        [TestCase(true)]
        public void settings_rename_should_preserve_a_parallel_login_upgrade(bool legacyDigestRoundTrip)
        {
            User concurrentLogin = null;
            _beforeWrite = () => concurrentLogin = StorePassword(Password);

            var renamed = _service.Upsert("RENAMED", legacyDigestRoundTrip ? _original.Password : null);

            var stored = Subject.Single();
            renamed.Username.Should().Be("renamed");
            stored.Username.Should().Be("renamed");
            stored.Id.Should().Be(_original.Id);
            stored.Identifier.Should().Be(_original.Identifier);
            AssertMetadata(stored, concurrentLogin);
            _credentialWrites.Should().Be(1);
            _serviceRepository.Verify(repository => repository.TryUpdateCredentials(
                It.IsAny<User>(), It.IsAny<User>(), true, false), Times.Once());
        }

        [Test]
        public void settings_rename_should_conflict_with_a_concurrently_renamed_account()
        {
            var renamed = Copy(_original);
            renamed.Username = "concurrent-name";
            _beforeWrite = () => Subject.Update(renamed);

            Assert.Throws<ModelConflictException>(() => _service.Upsert("requested-name", null));

            Subject.Single().Should().BeEquivalentTo(renamed);
            _credentialWrites.Should().Be(1);
        }

        private User StorePassword(string password, int iterations = 220000)
        {
            var updated = SaltedUser(Subject.Single(), password, iterations);
            Subject.Update(updated);
            return updated;
        }

        private void VerifyLoginReads(int count)
        {
            _serviceRepository.Verify(repository => repository.FindUser("reader"), Times.Exactly(count));
        }

        private static User SaltedUser(User original, string password, int iterations = 220000)
        {
            var updated = Copy(original);
            var salt = RandomNumberGenerator.GetBytes(16);
            updated.Salt = Convert.ToBase64String(salt);
            updated.Iterations = iterations;
            updated.Password = Convert.ToBase64String(Rfc2898DeriveBytes.Pbkdf2(
                password, salt, iterations, HashAlgorithmName.SHA512, 32));
            return updated;
        }

        private static void AssertMetadata(User stored, User expected)
        {
            stored.Password.Should().Be(expected.Password);
            stored.Salt.Should().Be(expected.Salt);
            stored.Iterations.Should().Be(expected.Iterations);
        }

        private static User Copy(User user)
        {
            return new User
            {
                Id = user.Id,
                Identifier = user.Identifier,
                Username = user.Username,
                Password = user.Password,
                Salt = user.Salt,
                Iterations = user.Iterations
            };
        }
    }
}
