using System;
using System.Linq;
using System.Threading;
using FluentAssertions;
using NLog;
using NLog.Config;
using NLog.Targets;
using NUnit.Framework;
using NzbDrone.Common.Instrumentation;
using NzbDrone.Core.Datastore.Migration.Framework;
using NzbDrone.Core.Instrumentation;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.Instrumentation
{
    [TestFixture]
    [NonParallelizable]
    public class DatabaseTargetFixture : DbTest<DatabaseTarget, Log>
    {
        private static string _uniqueMessage;
        private Logger _logger;

        protected override MigrationType MigrationType => MigrationType.Log;

        [OneTimeSetUp]
        public void InitializeLogger()
        {
            NzbDroneLogger.GetLogger(typeof(DatabaseTargetFixture));
        }

        [SetUp]
        public void Setup()
        {
            Mocker.Resolve<ILogRepository, LogRepository>();
            Mocker.Resolve<DatabaseTarget>().Register();

            LogManager.ReconfigExistingLoggers();

            _logger = NzbDroneLogger.GetLogger(this);

            _uniqueMessage = "Unique message: " + Guid.NewGuid();
        }

        [Test]
        public void write_log()
        {
            _logger.Info(_uniqueMessage);

            Thread.Sleep(1000);

            StoredModel.Message.Should().Be(_uniqueMessage);
            VerifyLog(StoredModel, LogLevel.Info);
        }

        [Test]
        public void write_long_log()
        {
            var message = string.Empty;
            for (var i = 0; i < 100; i++)
            {
                message += Guid.NewGuid();
            }

            _logger.Info(message);

            Thread.Sleep(1000);

            StoredModel.Message.Should().HaveLength(message.Length);
            StoredModel.Message.Should().Be(message);
            VerifyLog(StoredModel, LogLevel.Info);
        }

        [Test]
        public void write_log_exception()
        {
            var ex = new InvalidOperationException("Fake Exception");

            _logger.Error(ex, _uniqueMessage);

            Thread.Sleep(1000);

            VerifyLog(StoredModel, LogLevel.Error);
            StoredModel.Message.Should().Be(_uniqueMessage + ": " + ex.Message);
            StoredModel.ExceptionType.Should().Be(ex.GetType().ToString());
            StoredModel.Exception.Should().Be(ex.ToString());

            ExceptionVerification.ExpectedErrors(1);
        }

        [Test]
        public void exception_log_with_no_message_should_use_exceptions_message()
        {
            var ex = new InvalidOperationException("Fake Exception");
            _uniqueMessage = string.Empty;

            _logger.Error(ex, _uniqueMessage);

            Thread.Sleep(1000);

            StoredModel.Message.Should().Be(ex.Message);

            VerifyLog(StoredModel, LogLevel.Error);

            ExceptionVerification.ExpectedErrors(1);
        }

        [Test]
        public void null_string_as_arg_should_not_fail()
        {
            var epFile = new BookFile();
            _logger.Debug("File {0} no longer exists on disk. removing from database.", epFile.Path);

            Thread.Sleep(1000);

            epFile.Path.Should().BeNull();
        }

        [Test]
        public void replacing_configuration_repeatedly_should_register_one_database_target_and_preserve_cleansing()
        {
            var original = LogManager.Configuration;
            var first = new LoggingConfiguration();
            var second = new LoggingConfiguration();
            try
            {
                var iteration = 0;
                foreach (var configuration in new[] { first, original, second, first, original })
                {
                    var previous = LogManager.Configuration;
                    LogManager.Configuration = configuration;
                    ShouldHaveNoDatabaseRule(previous);
                    previous.FindTargetByName("DbLogger").Should().BeNull();
                    ShouldHaveOneDatabaseRule(configuration);

                    var target = configuration.FindTargetByName("DbLogger");
                    var rule = Subject.Rule;
                    Subject.Register();
                    Subject.Register();
                    configuration.FindTargetByName("DbLogger").Should().BeSameAs(target);
                    Subject.Rule.Should().BeSameAs(rule);
                    ShouldHaveOneDatabaseRule(configuration);

                    var message = _uniqueMessage + " reload " + iteration++;
                    _logger.Info("{0} ?apikey=synthetic-reload-secret", message);
                    LogManager.Flush(TimeSpan.FromSeconds(5));

                    AllStoredModels.Where(log => log.Message.StartsWith(message, StringComparison.Ordinal))
                        .Should().ContainSingle().Which.Message.Should().Be(message + " ?apikey=(removed)");
                }
            }
            finally
            {
                LogManager.Configuration = original;
            }
        }

        [Test]
        public void unregister_should_remove_owned_rules_from_all_visited_configurations_and_stop_reregistration()
        {
            var original = LogManager.Configuration;
            var first = new LoggingConfiguration();
            var second = new LoggingConfiguration();
            try
            {
                LogManager.Configuration = first;
                LogManager.Configuration = original;
                LogManager.Configuration = second;
                Subject.UnRegister();
                Subject.UnRegister();

                foreach (var configuration in new[] { original, first, second })
                {
                    ShouldHaveNoDatabaseRule(configuration);
                    configuration.FindTargetByName("DbLogger").Should().BeNull();
                }

                LogManager.Configuration = first;
                ShouldHaveNoDatabaseRule(first);
                first.FindTargetByName("DbLogger").Should().BeNull();
            }
            finally
            {
                LogManager.Configuration = original;
            }
        }

        [Test]
        public void unregister_should_preserve_an_unrelated_target_with_the_database_alias()
        {
            var original = LogManager.Configuration;
            var configuration = new LoggingConfiguration();
            var unrelatedTarget = new MemoryTarget();
            var unrelatedRule = new LoggingRule("*", LogLevel.Info, unrelatedTarget);
            configuration.AddTarget("DbLogger", unrelatedTarget);
            configuration.LoggingRules.Add(unrelatedRule);

            try
            {
                LogManager.Configuration = configuration;
                Subject.Register();
                configuration.FindTargetByName("DbLogger").Should().BeSameAs(unrelatedTarget);
                ShouldHaveOneDatabaseRule(configuration);

                _logger.Info("{0} ?apikey=synthetic-reload-secret", _uniqueMessage);
                LogManager.Flush(TimeSpan.FromSeconds(5));
                StoredModel.Message.Should().Be(_uniqueMessage + " ?apikey=(removed)");

                Subject.UnRegister();
                ShouldHaveNoDatabaseRule(configuration);
                configuration.FindTargetByName("DbLogger").Should().BeSameAs(unrelatedTarget);
                configuration.LoggingRules.Should().ContainSingle().Which.Should().BeSameAs(unrelatedRule);

                _logger.Info("{0} after unregister", _uniqueMessage);
                LogManager.Flush(TimeSpan.FromSeconds(5));
                unrelatedTarget.Logs.Should().ContainSingle(message => message.Contains(_uniqueMessage + " after unregister"));
                AllStoredModels.Should().ContainSingle();
            }
            finally
            {
                LogManager.Configuration = original;
            }
        }

        [TearDown]
        public void Teardown()
        {
            Mocker.Resolve<DatabaseTarget>().UnRegister();
        }

        private void ShouldHaveOneDatabaseRule(LoggingConfiguration configuration)
        {
            var target = configuration.LoggingRules.SelectMany(rule => rule.Targets)
                .OfType<SlowRunningAsyncTargetWrapper>()
                .Where(wrapper => ReferenceEquals(wrapper.WrappedTarget, Subject))
                .Should().ContainSingle().Which;
            configuration.AllTargets.Count(candidate => ReferenceEquals(candidate, target)).Should().Be(1);
            configuration.LoggingRules.Count(rule => ReferenceEquals(rule, Subject.Rule)).Should().Be(1);
        }

        private void ShouldHaveNoDatabaseRule(LoggingConfiguration configuration)
        {
            configuration.LoggingRules.SelectMany(rule => rule.Targets)
                .OfType<SlowRunningAsyncTargetWrapper>()
                .Should().NotContain(wrapper => ReferenceEquals(wrapper.WrappedTarget, Subject));
        }

        private void VerifyLog(Log logItem, LogLevel level)
        {
            logItem.Time.Should().BeWithin(TimeSpan.FromSeconds(2));
            logItem.Logger.Should().Be(GetType().Name);
            logItem.Level.Should().Be(level.Name);
            _logger.Name.Should().EndWith(logItem.Logger);
        }
    }
}
