using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Test.Common;

namespace NzbDrone.Common.Test.EnvironmentTests
{
    [TestFixture]
    public class StartupArgumentsFixture : TestBase
    {
        [Test]
        public void empty_array_should_return_empty_flags()
        {
            var args = new StartupContext(new string[0]);
            args.Flags.Should().BeEmpty();
        }

        [TestCase("/t")]
        [TestCase(" /t")]
        [TestCase(" /T")]
        [TestCase(" /t  ")]
        public void should_parse_single_flag(string arg)
        {
            var args = new StartupContext(new[] { arg });
            args.Flags.Should().HaveCount(1);
            args.Flags.Contains("t").Should().BeTrue();
        }

        [TestCase("/key=value")]
        [TestCase("/KEY=value")]
        [TestCase(" /key=\"value\"")]
        public void should_parse_args_with_alues(string arg)
        {
            var args = new StartupContext(new[] { arg });
            args.Args.Should().HaveCount(1);
            args.Args["key"].Should().Be("value");
        }

        [TestCase("/data=test", "/data=test")]
        [TestCase("/Data=/a/b/c", "/data=/a/b/c")]
        public void should_preserver_data(string arg, string preserved)
        {
            var args = new StartupContext(new[] { arg });
            args.PreservedArguments.Should().Be(preserved);
        }

        [TestCase("/nobrowser", "/nobrowser")]
        [TestCase("/Nobrowser", "/nobrowser")]
        [TestCase("-Nobrowser", "/nobrowser")]
        public void should_preserver_no_browser(string arg, string preserved)
        {
            var args = new StartupContext(new[] { arg });
            args.PreservedArguments.Should().Be(preserved);
        }

        [Test]
        public void should_preserver_both()
        {
            var args = new StartupContext(new[] { "/data=test", "/Nobrowser" });
            args.PreservedArguments.Should().Be("/data=test /nobrowser");
        }

        [TestCase("/data=/tmp/synthetic home=value/", "/tmp/synthetic home=value/", "\"/data=/tmp/synthetic home=value/\"")]
        [TestCase("/data=/tmp/home-with-dash-", "/tmp/home-with-dash-", "/data=/tmp/home-with-dash-")]
        [TestCase("/data=/tmp/home=edition", "/tmp/home=edition", "/data=/tmp/home=edition")]
        public void should_preserve_exact_home_argument(string argument, string path, string preserved)
        {
            var context = new StartupContext(argument);
            context.Args[StartupContext.APPDATA].Should().Be(path);
            context.PreservedArguments.Should().Be(preserved);
        }

        [Test]
        public void should_escape_trailing_backslashes_when_quoting_windows_paths()
        {
            StartupContext.QuoteArgument(@"/data=C:\synthetic home\").Should().Be("\"/data=C:\\synthetic home\\\\\"");
        }
    }
}
