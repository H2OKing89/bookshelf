using System;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Authentication;

namespace NzbDrone.Core.Test.Authentication
{
    [TestFixture]
    public class LocalPasswordRecoveryFixture
    {
        [TestCase("EXISTING")]
        [TestCase("existing")]
        public void selector_should_find_an_existing_username_without_creating_any_account(string selector)
        {
            var user = new User { Id = 17, Identifier = Guid.NewGuid(), Username = "existing" };
            var repository = new Mock<IUserRepository>(MockBehavior.Strict);
            repository.Setup(item => item.FindUser("existing")).Returns(user);
            var users = new Mock<IUserService>(MockBehavior.Strict);
            var service = new LocalPasswordRecovery(repository.Object, users.Object);

            service.FindAccount(selector).Should().Be(user.Identifier);

            users.VerifyNoOtherCalls();
        }

        [Test]
        public void an_explicit_identifier_should_not_be_interpreted_as_a_username()
        {
            var identifier = Guid.NewGuid();
            var repository = new Mock<IUserRepository>(MockBehavior.Strict);
            repository.Setup(item => item.FindUser(identifier)).Returns(new User { Id = 17, Identifier = identifier });
            var service = new LocalPasswordRecovery(repository.Object, Mock.Of<IUserService>());

            service.FindAccount("id:" + identifier).Should().Be(identifier);
        }

        [Test]
        public void ambiguous_guid_shaped_username_should_not_select_a_different_identity()
        {
            var identifier = Guid.NewGuid();
            var repository = new Mock<IUserRepository>();
            repository.Setup(item => item.FindUser(identifier.ToString())).Returns(new User { Id = 17, Identifier = Guid.NewGuid() });
            repository.Setup(item => item.FindUser(identifier)).Returns(new User { Id = 19, Identifier = identifier });
            var service = new LocalPasswordRecovery(repository.Object, Mock.Of<IUserService>());

            Assert.Throws<InvalidOperationException>(() => service.FindAccount(identifier.ToString()));
        }

        [Test]
        public void reset_should_use_the_existing_identity_and_shared_password_policy()
        {
            var users = new Mock<IUserService>(MockBehavior.Strict);
            var identifier = Guid.NewGuid();
            users.Setup(item => item.ResetPassword(identifier, "Owned new password")).Returns(new User { Identifier = identifier });
            var service = new LocalPasswordRecovery(Mock.Of<IUserRepository>(), users.Object);

            service.ResetPassword(identifier, "Owned new password");

            users.Verify(item => item.ResetPassword(identifier, "Owned new password"), Times.Once());
            users.VerifyNoOtherCalls();
        }
    }
}
