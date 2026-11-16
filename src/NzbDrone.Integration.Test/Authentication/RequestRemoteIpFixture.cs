using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Moq;
using NUnit.Framework;
using NzbDrone.Test.Common;
using Readarr.Http.Extensions;

namespace NzbDrone.Integration.Test.Authentication
{
    [TestFixture]
    public class RequestRemoteIpFixture : TestBase
    {
        [TestCase("192.168.1.50", "192.168.1.50")]
        [TestCase("203.0.113.66", "203.0.113.66")]
        [TestCase("::1", "::1")]
        [TestCase("2001:db8::1", "2001:db8::1")]
        [TestCase("::ffff:192.168.1.50", "192.168.1.50")]
        [TestCase("::ffff:100.64.0.1", "100.64.0.1")]
        public void should_report_the_connection_address_and_map_ipv4_mapped_addresses(string address, string expected)
        {
            var context = new DefaultHttpContext();
            context.Connection.RemoteIpAddress = IPAddress.Parse(address);
            context.Request.Headers["X-Forwarded-For"] = "127.0.0.1";

            context.GetRemoteIP().Should().Be(expected);
            context.Request.GetRemoteIP().Should().Be(expected);
        }

        [Test]
        public void should_report_unknown_when_the_remote_address_is_missing()
        {
            var context = new DefaultHttpContext();

            context.GetRemoteIP().Should().Be("Unknown");
            context.Request.GetRemoteIP().Should().Be("Unknown");
        }

        [Test]
        public void should_report_unknown_for_a_null_context_or_request()
        {
            HttpContext context = null;
            HttpRequest request = null;

            context.GetRemoteIP().Should().Be("Unknown");
            request.GetRemoteIP().Should().Be("Unknown");
        }

        [Test]
        public void should_report_unknown_when_the_request_has_no_context()
        {
            new Mock<HttpRequest>().Object.GetRemoteIP().Should().Be("Unknown");
        }

        [Test]
        public void should_report_unknown_when_the_context_has_no_connection()
        {
            var request = new Mock<HttpRequest>();
            request.SetupGet(value => value.HttpContext).Returns(new Mock<HttpContext>().Object);

            request.Object.GetRemoteIP().Should().Be("Unknown");
        }
    }
}
