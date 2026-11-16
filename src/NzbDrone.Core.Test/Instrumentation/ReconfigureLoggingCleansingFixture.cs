using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using NLog;
using NLog.Config;
using NLog.Targets.Syslog;
using NUnit.Framework;
using NzbDrone.Common.Instrumentation;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Instrumentation;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.Instrumentation
{
    [TestFixture]
    [NonParallelizable]
    public class ReconfigureLoggingCleansingFixture : TestBase<ReconfigureLogging>
    {
        [OneTimeSetUp]
        public void InitializeLogger()
        {
            // Run the application's static logger initialization before per-test logging setup.
            NzbDroneLogger.GetLogger(typeof(ReconfigureLoggingCleansingFixture));
        }

        [Test]
        public async Task configured_syslog_target_should_emit_cleansed_message_to_loopback()
        {
            const string secret = "synthetic-syslog-api-key";
            const string applicationName = "BookshelfSyslogTest";
            using var listener = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
            var port = ((IPEndPoint)listener.Client.LocalEndPoint).Port;
            var config = Mocker.GetMock<IConfigFileProvider>();
            config.SetupGet(provider => provider.LogLevel).Returns("info");
            config.SetupGet(provider => provider.SyslogServer).Returns(IPAddress.Loopback.ToString());
            config.SetupGet(provider => provider.SyslogPort).Returns(port);
            config.SetupGet(provider => provider.SyslogLevel).Returns("info");
            config.SetupGet(provider => provider.InstanceName).Returns(applicationName);

            var originalConfiguration = LogManager.Configuration;
            var originalSqlLogging = SqlBuilderExtensions.LogSql;
            try
            {
                LogManager.Configuration = new LoggingConfiguration();

                Subject.Reconfigure();

                var target = LogManager.Configuration.FindTargetByName<SyslogTarget>("syslogTarget");
                target.Should().NotBeNull();
                target.Layout.Should().BeOfType<CleansedLayout>();

                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                var receiving = listener.ReceiveAsync(timeout.Token);
                LogManager.GetLogger("SyslogCleansingTest").Info("request?apikey=" + secret + "&page=2");
                LogManager.Flush(TimeSpan.FromSeconds(2));
                var datagram = await receiving;
                var message = Encoding.UTF8.GetString(datagram.Buffer);

                IPAddress.IsLoopback(datagram.RemoteEndPoint.Address).Should().BeTrue();
                message.Should().Contain(applicationName);
                message.Should().Contain("request?apikey=(removed)&page=2");
                message.Should().NotContain(secret);
            }
            finally
            {
                SqlBuilderExtensions.LogSql = originalSqlLogging;
                LogManager.Configuration = originalConfiguration;
                LogManager.ReconfigExistingLoggers();
            }
        }
    }
}
