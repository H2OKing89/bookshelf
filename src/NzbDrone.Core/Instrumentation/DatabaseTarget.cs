using System;
using System.Data;
using System.Data.SQLite;
using NLog;
using NLog.Common;
using NLog.Config;
using NLog.Targets;
using Npgsql;
using NzbDrone.Common.Instrumentation;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Lifecycle;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.Instrumentation
{
    public class DatabaseTarget : TargetWithLayout, IHandle<ApplicationShutdownRequested>
    {
        private const string TARGET_NAME = "DbLogger";
        private const string INSERT_COMMAND = "INSERT INTO \"Logs\" (\"Message\",\"Time\",\"Logger\",\"Exception\",\"ExceptionType\",\"Level\") " +
                                      "VALUES(@Message,@Time,@Logger,@Exception,@ExceptionType,@Level)";

        private readonly IConnectionStringFactory _connectionStringFactory;
        private LoggingConfiguration _registeredConfiguration;
        private SlowRunningAsyncTargetWrapper _registeredTarget;

        public DatabaseTarget(IConnectionStringFactory connectionStringFactory)
        {
            _connectionStringFactory = connectionStringFactory;
        }

        public void Register()
        {
            var configuration = LogManager.Configuration;
            if (configuration == null)
            {
                return;
            }

            if (!ReferenceEquals(_registeredConfiguration, configuration))
            {
                RemoveRegistration();
                _registeredConfiguration = configuration;
                _registeredTarget = new SlowRunningAsyncTargetWrapper(this) { TimeToSleepBetweenBatches = 500 };
                Rule = new LoggingRule("*", LogLevel.Info, _registeredTarget);
            }

            if (configuration.FindTargetByName(TARGET_NAME) == null)
            {
                configuration.AddTarget(TARGET_NAME, _registeredTarget);
            }

            if (!configuration.LoggingRules.Contains(Rule))
            {
                configuration.LoggingRules.Add(Rule);
            }

            LogManager.ConfigurationChanged -= OnLogManagerOnConfigurationChanged;
            LogManager.ConfigurationChanged += OnLogManagerOnConfigurationChanged;
            LogManager.ReconfigExistingLoggers();
        }

        public void UnRegister()
        {
            LogManager.ConfigurationChanged -= OnLogManagerOnConfigurationChanged;
            var target = _registeredTarget;
            RemoveRegistration();
            LogManager.ReconfigExistingLoggers();
            target?.Dispose();
            Dispose();
        }

        private void RemoveRegistration()
        {
            if (_registeredConfiguration == null)
            {
                return;
            }

            _registeredConfiguration.LoggingRules.Remove(Rule);
            if (ReferenceEquals(_registeredConfiguration.FindTargetByName(TARGET_NAME), _registeredTarget))
            {
                _registeredConfiguration.RemoveTarget(TARGET_NAME);
            }

            _registeredConfiguration = null;
            _registeredTarget = null;
            Rule = null;
        }

        private void OnLogManagerOnConfigurationChanged(object sender, LoggingConfigurationChangedEventArgs args)
        {
            if (args.ActivatedConfiguration != null)
            {
                Register();
            }
        }

        public LoggingRule Rule { get; set; }

        protected override void Write(LogEventInfo logEvent)
        {
            try
            {
                var log = new Log
                {
                    Time = logEvent.TimeStamp,
                    Logger = logEvent.LoggerName,
                    Level = logEvent.Level.Name
                };

                if (log.Logger?.StartsWith("NzbDrone.") == true)
                {
                    log.Logger = log.Logger.Remove(0, 9);
                }

                if (logEvent.Exception != null)
                {
                    log.Exception = CleanseLogMessage.Cleanse(logEvent, entry => entry.Exception.ToString());
                    log.ExceptionType = logEvent.Exception.GetType().ToString();
                }

                log.Message = CleanseLogMessage.Cleanse(logEvent, entry =>
                {
                    var message = entry.FormattedMessage;
                    if (entry.Exception == null)
                    {
                        return message;
                    }

                    return string.IsNullOrWhiteSpace(message) ? entry.Exception.Message : message + ": " + entry.Exception.Message;
                });

                var connectionInfo = _connectionStringFactory.LogDbConnection;

                if (connectionInfo.DatabaseType == DatabaseType.SQLite)
                {
                    WriteSqliteLog(log, connectionInfo.ConnectionString);
                }
                else
                {
                    WritePostgresLog(log, connectionInfo.ConnectionString);
                }
            }
            catch (SQLiteException ex)
            {
                InternalLogger.Error(ex, "Unable to save log event to database");
                throw;
            }
        }

        private void WritePostgresLog(Log log, string connectionString)
        {
            using (var connection =
                new NpgsqlConnection(connectionString))
            {
                connection.Open();
                using (var sqlCommand = connection.CreateCommand())
                {
                    sqlCommand.CommandText = INSERT_COMMAND;
                    sqlCommand.Parameters.Add(new NpgsqlParameter("Message", DbType.String) { Value = log.Message });
                    sqlCommand.Parameters.Add(new NpgsqlParameter("Time", DbType.DateTime) { Value = log.Time.ToUniversalTime() });
                    sqlCommand.Parameters.Add(new NpgsqlParameter("Logger", DbType.String) { Value = log.Logger });
                    sqlCommand.Parameters.Add(new NpgsqlParameter("Exception", DbType.String) { Value = log.Exception == null ? DBNull.Value : log.Exception });
                    sqlCommand.Parameters.Add(new NpgsqlParameter("ExceptionType", DbType.String) { Value = log.ExceptionType == null ? DBNull.Value : log.ExceptionType });
                    sqlCommand.Parameters.Add(new NpgsqlParameter("Level", DbType.String) { Value = log.Level });

                    sqlCommand.ExecuteNonQuery();
                }
            }
        }

        private void WriteSqliteLog(Log log, string connectionString)
        {
            using (var connection =
                SQLiteFactory.Instance.CreateConnection())
            {
                connection.ConnectionString = connectionString;
                connection.Open();
                using (var sqlCommand = connection.CreateCommand())
                {
                    sqlCommand.CommandText = INSERT_COMMAND;
                    sqlCommand.Parameters.Add(new SQLiteParameter("Message", DbType.String) { Value = log.Message });
                    sqlCommand.Parameters.Add(new SQLiteParameter("Time", DbType.DateTime) { Value = log.Time.ToUniversalTime() });
                    sqlCommand.Parameters.Add(new SQLiteParameter("Logger", DbType.String) { Value = log.Logger });
                    sqlCommand.Parameters.Add(new SQLiteParameter("Exception", DbType.String) { Value = log.Exception });
                    sqlCommand.Parameters.Add(new SQLiteParameter("ExceptionType", DbType.String) { Value = log.ExceptionType });
                    sqlCommand.Parameters.Add(new SQLiteParameter("Level", DbType.String) { Value = log.Level });
                    sqlCommand.ExecuteNonQuery();
                }
            }
        }

        public void Handle(ApplicationShutdownRequested message)
        {
            UnRegister();
        }
    }
}
