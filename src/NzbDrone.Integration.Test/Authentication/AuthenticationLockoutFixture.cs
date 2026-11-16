using System;
using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Authentication;
using NzbDrone.Core.Configuration;
using NzbDrone.Test.Common;
using Readarr.Http.Authentication;
using ReadarrAuthenticationService = Readarr.Http.Authentication.AuthenticationService;

namespace NzbDrone.Integration.Test.Authentication
{
    [TestFixture]
    public class AuthenticationLockoutFixture : TestBase
    {
        private Mock<IConfigFileProvider> _config;
        private Mock<IUserService> _users;
        private LoginLockoutPolicy _policy;
        private User _user;

        [SetUp]
        public void Setup()
        {
            _config = new Mock<IConfigFileProvider>();
            _config.SetupGet(provider => provider.AuthenticationMethod).Returns(AuthenticationType.Forms);
            _users = new Mock<IUserService>();
            _user = new User { Id = 7, Identifier = Guid.NewGuid(), Username = "reader" };
            _users.Setup(service => service.FindUser(It.IsAny<string>(), It.IsAny<string>()))
                .Returns<string, string>((username, password) => password == "correct password" ? _user : null);
            _policy = new LoginLockoutPolicy(new SystemClock());
        }

        [TestCase(AuthenticationType.Forms)]
        [TestCase(AuthenticationType.Basic)]
        public void password_login_modes_should_share_the_limit_across_authentication_service_instances(AuthenticationType method)
        {
            _config.SetupGet(provider => provider.AuthenticationMethod).Returns(method);
            var context = Context();
            for (var attempt = 0; attempt < 5; attempt++)
            {
                Service().Login(context.Request, "reader-" + attempt, "wrong password").Should().BeNull();
            }

            Service().Login(context.Request, "reader", "correct password").Should().BeNull();

            _users.Verify(service => service.FindUser(It.IsAny<string>(), It.IsAny<string>()), Times.Exactly(5));
            Service().GetLockoutEndTime(context.Request).Should().NotBeNull();
            ExceptionVerification.ExpectedWarns(6);
        }

        [TestCase(AuthenticationType.Forms)]
        [TestCase(AuthenticationType.Basic)]
        public void a_successful_password_login_should_clear_prior_failures(AuthenticationType method)
        {
            _config.SetupGet(provider => provider.AuthenticationMethod).Returns(method);
            var service = Service();
            var context = Context();
            Fail(service, context, 4);

            service.Login(context.Request, "reader", "correct password").Should().BeSameAs(_user);
            Fail(service, context, 4);

            service.GetLockoutEndTime(context.Request).Should().BeNull();
            ExceptionVerification.ExpectedWarns(8);
        }

        [Test]
        public void raw_forwarded_headers_should_not_change_the_failure_bucket()
        {
            var service = Service();
            var context = Context();
            for (var attempt = 0; attempt < 5; attempt++)
            {
                context.Request.Headers["X-Forwarded-For"] = "203.0.113." + attempt;
                context.Request.Headers["Forwarded"] = "for=198.51.100." + attempt;
                service.Login(context.Request, "reader", "wrong password");
            }

            context.Request.Headers["X-Forwarded-For"] = "203.0.113.99";
            service.Login(context.Request, "reader", "correct password").Should().BeNull();

            _users.Verify(users => users.FindUser(It.IsAny<string>(), It.IsAny<string>()), Times.Exactly(5));
            ExceptionVerification.ExpectedWarns(6);
        }

        [Test]
        public void mapped_ipv4_addresses_should_share_the_ipv4_failure_bucket()
        {
            var service = Service();
            var context = Context();
            Fail(service, context, 4);
            var mapped = Context("::ffff:192.0.2.1");

            service.Login(mapped.Request, "reader", "wrong password");

            service.GetLockoutEndTime(mapped.Request).Should().NotBeNull();
            service.GetLockoutEndTime(mapped.Request).Should().Be(service.GetLockoutEndTime(context.Request));
            ExceptionVerification.ExpectedWarns(5);
        }

        [Test]
        public void missing_remote_addresses_should_share_one_unknown_source_bucket()
        {
            var service = Service();
            var context = Context();
            context.Connection.RemoteIpAddress = null;
            Fail(service, context, 5);
            var another = Context();
            another.Connection.RemoteIpAddress = null;
            another.Request.Headers["X-Forwarded-For"] = "203.0.113.1";

            service.Login(another.Request, "reader", "correct password").Should().BeNull();

            ExceptionVerification.ExpectedWarns(6);
        }

        [Test]
        public void separately_resolved_client_addresses_should_have_independent_counters()
        {
            var service = Service();
            var first = Context();
            var second = Context("192.0.2.2");
            Fail(service, first, 4);
            Fail(service, second, 4);

            service.GetLockoutEndTime(first.Request).Should().BeNull();
            service.GetLockoutEndTime(second.Request).Should().BeNull();
            service.Login(second.Request, "reader", "correct password").Should().BeSameAs(_user);
            ExceptionVerification.ExpectedWarns(8);
        }

        [Test]
        public void none_mode_should_not_verify_passwords_or_expose_a_lockout()
        {
            _config.SetupGet(provider => provider.AuthenticationMethod).Returns(AuthenticationType.None);
            var service = Service();
            var context = Context();

            service.Login(context.Request, "reader", "correct password").Should().BeNull();
            service.GetLockoutEndTime(context.Request).Should().BeNull();

            _users.Verify(users => users.FindUser(It.IsAny<string>(), It.IsAny<string>()), Times.Never());
        }

        [Test]
        public void external_mode_should_keep_existing_password_lookup_behavior_without_local_lockout()
        {
            var context = Context();
            Fail(Service(), context, 5);
            _config.SetupGet(provider => provider.AuthenticationMethod).Returns(AuthenticationType.External);
            var service = Service();

            service.Login(context.Request, "reader", "correct password").Should().BeSameAs(_user);
            service.GetLockoutEndTime(context.Request).Should().BeNull();

            ExceptionVerification.ExpectedWarns(5);
        }

        [Test]
        public void an_existing_service_should_read_live_authentication_mode_without_static_cross_instance_state()
        {
            var forms = Service();
            var noneConfig = new Mock<IConfigFileProvider>();
            noneConfig.SetupGet(provider => provider.AuthenticationMethod).Returns(AuthenticationType.None);
            var none = new ReadarrAuthenticationService(noneConfig.Object, _users.Object, _policy);
            var context = Context();

            none.Login(context.Request, "reader", "correct password").Should().BeNull();
            forms.Login(context.Request, "reader", "correct password").Should().BeSameAs(_user);
            _config.SetupGet(provider => provider.AuthenticationMethod).Returns(AuthenticationType.None);
            forms.Login(context.Request, "reader", "correct password").Should().BeNull();

            _users.Verify(users => users.FindUser(It.IsAny<string>(), It.IsAny<string>()), Times.Once());
        }

        private ReadarrAuthenticationService Service() => new ReadarrAuthenticationService(_config.Object, _users.Object, _policy);

        private static DefaultHttpContext Context(string address = "192.0.2.1")
        {
            var context = new DefaultHttpContext();
            context.Connection.RemoteIpAddress = IPAddress.Parse(address);
            return context;
        }

        private static void Fail(ReadarrAuthenticationService service, HttpContext context, int count)
        {
            for (var attempt = 0; attempt < count; attempt++)
            {
                service.Login(context.Request, "reader", "wrong password").Should().BeNull();
            }
        }
    }
}
