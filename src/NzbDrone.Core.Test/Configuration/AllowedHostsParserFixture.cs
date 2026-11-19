using System;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Configuration;

namespace NzbDrone.Core.Test.Configuration
{
    [TestFixture]
    public class AllowedHostsParserFixture
    {
        [TestCase(null)]
        [TestCase("")]
        [TestCase(" \t\r\n ")]
        public void empty_configuration_should_remain_optional(string value)
        {
            AllowedHostsParser.Parse(value).Should().BeEmpty();
            AllowedHostsParser.IsValidList(value).Should().BeTrue();
            AllowedHostsParser.NormalizeList(value).Should().BeEmpty();
        }

        [TestCase(" Books.Example.Test ", "books.example.test")]
        [TestCase("books.test; BOOKS.test, other.test", "books.test, other.test")]
        [TestCase("2001:0DB8:0:0::1, [2001:db8::1]", "[2001:db8::1]")]
        [TestCase("::1", "[::1]")]
        [TestCase("192.0.2.10", "192.0.2.10")]
        [TestCase("bücher.test", "xn--bcher-kva.test")]
        [TestCase("*.Bücher.test", "*.xn--bcher-kva.test")]
        [TestCase("books.test.", "books.test.")]
        [TestCase("bookshelf-server", "bookshelf-server")]
        public void supported_hosts_should_have_unambiguous_normalized_patterns(string value, string expected)
        {
            AllowedHostsParser.IsValidList(value).Should().BeTrue();
            AllowedHostsParser.NormalizeList(value).Should().Be(expected);
        }

        [TestCase("*")]
        [TestCase(", ; ,")]
        [TestCase("https://books.test")]
        [TestCase("books.test:8787")]
        [TestCase("[::1]:8787")]
        [TestCase("books.test/path")]
        [TestCase("books.test\\path")]
        [TestCase("books.test?apiKey=secret")]
        [TestCase("reader@books.test")]
        [TestCase("books.test#fragment")]
        [TestCase("books.test, *")]
        [TestCase("books.test, invalid host")]
        [TestCase("0.0.0.0")]
        [TestCase("::")]
        [TestCase("[::]")]
        [TestCase("127.1")]
        [TestCase("0177.0.0.1")]
        [TestCase("0x7f000001")]
        [TestCase("192.0.2.0/24")]
        [TestCase("[fe80::1%eth0]")]
        [TestCase("[not-an-ip]")]
        [TestCase("[127.0.0.1]")]
        [TestCase("*.192.0.2.10")]
        [TestCase("*.[::1]")]
        [TestCase("*.*.books.test")]
        [TestCase("books.*")]
        [TestCase("*.")]
        [TestCase(".books.test")]
        [TestCase("-books.test")]
        [TestCase("books-.test")]
        [TestCase("books..test")]
        [TestCase("books_test")]
        public void invalid_configuration_should_never_become_an_allow_all_list(string value)
        {
            AllowedHostsParser.IsValidList(value).Should().BeFalse();
            Assert.Throws<FormatException>(() => AllowedHostsParser.Parse(value));
            Assert.Throws<FormatException>(() => AllowedHostsParser.NormalizeList(value));
        }
    }
}
