using System.Net;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Common.Network;

namespace NzbDrone.Common.Test.NetworkTests
{
    [TestFixture]
    public class IPNetworkParserFixture
    {
        [TestCase("192.0.2.25", "192.0.2.25", 32)]
        [TestCase("192.0.2.0/24", "192.0.2.0", 24)]
        [TestCase("192.0.2.25/32", "192.0.2.25", 32)]
        [TestCase("192.0.2.128/25", "192.0.2.128", 25)]
        [TestCase(" 192.0.2.0 / 24 ", "192.0.2.0", 24)]
        [TestCase("2001:db8::1", "2001:db8::1", 128)]
        [TestCase("2001:DB8::/32", "2001:db8::", 32)]
        [TestCase("2001:db8:8000::/33", "2001:db8:8000::", 33)]
        [TestCase("::ffff:192.0.2.0/120", "::ffff:192.0.2.0", 120)]
        [TestCase("::ffff:c000:200/120", "::ffff:192.0.2.0", 120)]
        [TestCase("::ffff:8000:0/97", "::ffff:128.0.0.0", 97)]
        [TestCase("::1", "::1", 128)]
        public void should_parse_unambiguous_ip_addresses_and_aligned_networks(string value, string expectedAddress, int expectedPrefix)
        {
            IPNetworkParser.TryParse(value, out var address, out var prefix).Should().BeTrue();

            address.Should().Be(IPAddress.Parse(expectedAddress));
            prefix.Should().Be(expectedPrefix);
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase(" ")]
        [TestCase("proxy.example.test")]
        [TestCase("*")]
        [TestCase("192.0.2.0/0")]
        [TestCase("0.0.0.0/0")]
        [TestCase("::/0")]
        [TestCase("192.0.2.0/-1")]
        [TestCase("192.0.2.0/+24")]
        [TestCase("192.0.2.0/33")]
        [TestCase("2001:db8::/129")]
        [TestCase("192.0.2.0/")]
        [TestCase("192.0.2.0/24/32")]
        [TestCase("192.0.2.5/24")]
        [TestCase("192.0.2.129/25")]
        [TestCase("2001:db8::1/64")]
        [TestCase("::ffff:192.0.2.1/120")]
        [TestCase("::ffff:0:0/80")]
        [TestCase("::ffff:0:0/96")]
        [TestCase("::ffff:0.0.0.0/96")]
        [TestCase("::/64")]
        [TestCase("::/80")]
        [TestCase("::fffe:0:0/95")]
        [TestCase("127.1")]
        [TestCase("2130706433")]
        [TestCase("0x7f000001")]
        [TestCase("0x7f.0.0.1")]
        [TestCase("0177.0.0.1")]
        [TestCase("192.000.2.1")]
        [TestCase("::ffff:192.000.2.1")]
        [TestCase("192.0.2.256")]
        [TestCase("192.0.2.1:80")]
        [TestCase("[::1]")]
        [TestCase("[::1]:80")]
        [TestCase("fe80::1%eth0")]
        [TestCase("fe80::1%1/128")]
        public void should_reject_invalid_or_ambiguous_trust_entries(string value)
        {
            IPNetworkParser.TryParse(value, out var address, out var prefix).Should().BeFalse();

            address.Should().BeNull();
            prefix.Should().Be(0);
        }

        [TestCase(null, "")]
        [TestCase("", "")]
        [TestCase(" , , ", "")]
        [TestCase(" 192.0.2.1 ,, 2001:db8::/32, ", "192.0.2.1, 2001:db8::/32")]
        public void should_allow_empty_lists_and_normalize_separators(string value, string expected)
        {
            IPNetworkParser.IsValidList(value).Should().BeTrue();
            IPNetworkParser.NormalizeList(value).Should().Be(expected);
        }

        [TestCase("192.0.2.1, invalid, 2001:db8::/32")]
        [TestCase("192.0.2.1,192.0.2.4/24")]
        [TestCase("192.0.2.1;198.51.100.1")]
        public void should_reject_the_whole_list_when_any_entry_is_invalid(string value)
        {
            IPNetworkParser.IsValidList(value).Should().BeFalse();
        }
    }
}
