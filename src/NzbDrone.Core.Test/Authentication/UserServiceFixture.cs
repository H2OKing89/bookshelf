using System;
using System.Security.Cryptography;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Authentication;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Authentication
{
    [TestFixture]
    public class UserServiceFixture : CoreTest<UserService>
    {
        private const string Password = "pāssword🔒:100%";
        private User _user;

        [SetUp]
        public void Setup()
        {
            _user = new User { Id = 42, Identifier = Guid.NewGuid(), Username = "reader", Password = Password.SHA256Hash() };
            Mocker.GetMock<IUserRepository>().Setup(x => x.FindUser("reader")).Returns(() => _user);
            Mocker.GetMock<IUserRepository>().Setup(x => x.SingleOrDefault()).Returns(() => _user);
            Mocker.GetMock<IUserRepository>()
                .Setup(x => x.TryUpdateCredentials(It.IsAny<User>(), It.IsAny<User>(), It.IsAny<bool>(), It.IsAny<bool>()))
                .Returns((User original, User updated, bool updateUsername, bool updatePassword) =>
                {
                    _user = updated;
                    return true;
                });
            Mocker.GetMock<IUserRepository>().Setup(x => x.Insert(It.IsAny<User>())).Returns<User>(user => user);
        }

        private void WithSaltedPassword(int iterations)
        {
            var salt = new byte[16];
            _user.Salt = Convert.ToBase64String(salt);
            _user.Iterations = iterations;
            _user.Password = Convert.ToBase64String(Rfc2898DeriveBytes.Pbkdf2(Password, salt, iterations, HashAlgorithmName.SHA512, 32));
        }

        [Test]
        public void should_salt_new_passwords_independently()
        {
            var first = Subject.Add("READER", Password);
            var second = Subject.Add("READER", Password);

            first.Username.Should().Be("reader");
            first.Identifier.Should().NotBeEmpty();
            second.Identifier.Should().NotBe(first.Identifier);
            first.Iterations.Should().Be(220000);
            Convert.FromBase64String(first.Salt).Should().HaveCount(16);
            Convert.FromBase64String(first.Password).Should().HaveCount(32);
            first.Password.Should().NotBe(Password).And.NotBe(Password.SHA256Hash());
            first.Salt.Should().NotBe(second.Salt);
            first.Password.Should().NotBe(second.Password);
            first.Password.Should().Be(Convert.ToBase64String(Rfc2898DeriveBytes.Pbkdf2(
                Password, Convert.FromBase64String(first.Salt), 220000, HashAlgorithmName.SHA512, 32)));
        }

        [Test]
        public void should_upgrade_legacy_password_once_without_changing_identity()
        {
            var identifier = _user.Identifier;

            Subject.FindUser("READER", Password).Should().BeSameAs(_user);

            _user.Id.Should().Be(42);
            _user.Identifier.Should().Be(identifier);
            _user.Iterations.Should().Be(220000);
            _user.Salt.Should().NotBeNullOrEmpty();
            var hash = _user.Password;
            var salt = _user.Salt;

            Subject.FindUser("reader", Password).Should().BeSameAs(_user);

            _user.Password.Should().Be(hash);
            _user.Salt.Should().Be(salt);
            Mocker.GetMock<IUserRepository>().Verify(x => x.TryUpdateCredentials(It.IsAny<User>(), _user, false, true), Times.Once());
        }

        [TestCase(0)]
        [TestCase(1000)]
        [TestCase(220000)]
        public void should_not_change_credentials_on_wrong_password(int iterations)
        {
            if (iterations > 0)
            {
                WithSaltedPassword(iterations);
            }

            var hash = _user.Password;
            var salt = _user.Salt;

            Subject.FindUser("reader", Password + "wrong").Should().BeNull();

            _user.Password.Should().Be(hash);
            _user.Salt.Should().Be(salt);
            _user.Iterations.Should().Be(iterations);
            Mocker.GetMock<IUserRepository>().Verify(x => x.TryUpdateCredentials(It.IsAny<User>(), It.IsAny<User>(), It.IsAny<bool>(), It.IsAny<bool>()), Times.Never());
        }

        [Test]
        public void should_verify_an_independent_utf8_pbkdf2_vector_and_upgrade_its_work_factor()
        {
            // Generated independently with Python hashlib.pbkdf2_hmac('sha512', ..., 1000, 32).
            _user.Salt = "AAECAwQFBgcICQoLDA0ODw==";
            _user.Iterations = 1000;
            _user.Password = "G1VNiuxAy9eTlJvtWflAatKuVXTqGh1JAA/MsYgfejc=";

            Subject.FindUser("reader", Password).Should().BeSameAs(_user);

            _user.Iterations.Should().Be(220000);
            _user.Salt.Should().NotBe("AAECAwQFBgcICQoLDA0ODw==");
            Mocker.GetMock<IUserRepository>().Verify(x => x.TryUpdateCredentials(It.IsAny<User>(), _user, false, true), Times.Once());
        }

        [TestCase(220000)]
        [TestCase(220001)]
        public void should_preserve_current_or_higher_work_factors(int iterations)
        {
            WithSaltedPassword(iterations);
            var hash = _user.Password;
            var salt = _user.Salt;

            Subject.FindUser("reader", Password).Should().BeSameAs(_user);

            _user.Password.Should().Be(hash);
            _user.Salt.Should().Be(salt);
            _user.Iterations.Should().Be(iterations);
            Mocker.GetMock<IUserRepository>().Verify(x => x.TryUpdateCredentials(It.IsAny<User>(), It.IsAny<User>(), It.IsAny<bool>(), It.IsAny<bool>()), Times.Never());
        }

        [TestCase(null, "password")]
        [TestCase("", "password")]
        [TestCase(" ", "password")]
        [TestCase("reader", null)]
        [TestCase("reader", "")]
        [TestCase("reader", " ")]
        [TestCase("missing", "password")]
        [TestCase("reader", "Pāssword🔒:100%")]
        public void should_reject_invalid_login(string username, string password)
        {
            Subject.FindUser(username, password).Should().BeNull();
            Mocker.GetMock<IUserRepository>().Verify(x => x.TryUpdateCredentials(It.IsAny<User>(), It.IsAny<User>(), It.IsAny<bool>(), It.IsAny<bool>()), Times.Never());
        }

        [TestCase(null, null, 0)]
        [TestCase("invalid", "invalid", 220000)]
        [TestCase("AAAAAAAAAAAAAAAAAAAAAA==", "invalid", 220000)]
        [TestCase("!!!!!!!!!!!!!!!!!!!!!!!!", "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=", 220000)]
        [TestCase("AAAAAAAAAAAAAAAAAAAAAA==", "!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!", 220000)]
        [TestCase("AAAAAAAAAAAAAAAAAAAAAA==", "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=", 0)]
        [TestCase("AAAAAAAAAAAAAAAAAAAAAA==", "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=", -1)]
        [TestCase("AAAAAAAAAAAAAAAAAAAAAA==", "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=", 1000001)]
        [TestCase(null, "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA", 220000)]
        [TestCase(" ", "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA", 0)]
        public void should_reject_malformed_metadata_without_writing(string salt, string hash, int iterations)
        {
            _user.Salt = salt;
            _user.Password = hash;
            _user.Iterations = iterations;

            Subject.FindUser("reader", Password).Should().BeNull();

            Mocker.GetMock<IUserRepository>().Verify(x => x.TryUpdateCredentials(It.IsAny<User>(), It.IsAny<User>(), It.IsAny<bool>(), It.IsAny<bool>()), Times.Never());
        }

        [TestCase(false)]
        [TestCase(true)]
        public void should_preserve_password_for_null_updates(bool salted)
        {
            if (salted)
            {
                WithSaltedPassword(220000);
            }

            var identifier = _user.Identifier;
            var hash = _user.Password;
            var salt = _user.Salt;
            var iterations = _user.Iterations;

            Subject.Upsert("NEWNAME", null).Should().BeSameAs(_user);

            _user.Username.Should().Be("newname");
            _user.Identifier.Should().Be(identifier);
            _user.Password.Should().Be(hash);
            _user.Salt.Should().Be(salt);
            _user.Iterations.Should().Be(iterations);
        }

        [Test]
        public void should_preserve_legacy_digest_round_trips()
        {
            var hash = _user.Password;

            Subject.Upsert("reader", hash);

            _user.Password.Should().Be(hash);
            _user.Salt.Should().BeNull();
            _user.Iterations.Should().Be(0);
        }

        [Test]
        public void should_change_password_without_changing_identity()
        {
            var identifier = _user.Identifier;

            Subject.Upsert("reader", "new password");

            _user.Identifier.Should().Be(identifier);
            Subject.FindUser("reader", Password).Should().BeNull();
            Subject.FindUser("reader", "new password").Should().BeSameAs(_user);
        }

        [Test]
        public void should_not_create_user_for_omitted_password()
        {
            _user = null;

            Subject.Upsert("reader", null).Should().BeNull();

            Mocker.GetMock<IUserRepository>().Verify(x => x.Insert(It.IsAny<User>()), Times.Never());
        }

        [Test]
        public void should_create_user_for_new_password()
        {
            _user = null;

            var user = Subject.Upsert("READER", Password);

            user.Username.Should().Be("reader");
            user.Iterations.Should().Be(220000);
            Mocker.GetMock<IUserRepository>().Verify(x => x.Insert(user), Times.Once());
        }

        [TestCase(0)]
        [TestCase(1000)]
        public void should_propagate_password_upgrade_write_failure(int iterations)
        {
            if (iterations > 0)
            {
                WithSaltedPassword(iterations);
            }

            Mocker.GetMock<IUserRepository>().Setup(x => x.TryUpdateCredentials(It.IsAny<User>(), It.IsAny<User>(), It.IsAny<bool>(), It.IsAny<bool>())).Throws(new InvalidOperationException("write failed"));

            Assert.Throws<InvalidOperationException>(() => Subject.FindUser("reader", Password));

            Mocker.GetMock<IUserRepository>().Verify(x => x.Insert(It.IsAny<User>()), Times.Never());
        }
    }
}
