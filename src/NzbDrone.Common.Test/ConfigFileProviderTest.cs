using System;
using System.Collections.Generic;
using System.Xml.Linq;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Cache;
using NzbDrone.Common.Disk;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Common.Exceptions;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Options;
using NzbDrone.Core.Authentication;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Test.Common;

namespace NzbDrone.Common.Test
{
    [TestFixture]

    public class ConfigFileProviderTest : TestBase<ConfigFileProvider>
    {
        private string _configFileContents;
        private string _configFilePath;

        [SetUp]
        public void SetUp()
        {
            WithTempAsAppPath();

            _configFilePath = Mocker.Resolve<IAppFolderInfo>().GetConfigPath();

            _configFileContents = null;

            WithMockConfigFile(_configFilePath);
        }

        protected void WithMockConfigFile(string configFile)
        {
            Mocker.GetMock<IDiskProvider>()
                .Setup(v => v.FileExists(configFile))
                .Returns<string>(p => _configFileContents != null);

            Mocker.GetMock<IDiskProvider>()
                .Setup(v => v.ReadAllText(configFile))
                .Returns<string>(p => _configFileContents);

            Mocker.GetMock<IDiskProvider>()
                .Setup(v => v.WriteAllText(configFile, It.IsAny<string>()))
                .Callback<string, string>((p, t) => _configFileContents = t);

            Mocker.GetMock<IOptions<AuthOptions>>()
                .Setup(v => v.Value)
                .Returns(new AuthOptions());

            Mocker.GetMock<IOptions<AppOptions>>()
                .Setup(v => v.Value)
                .Returns(new AppOptions());

            Mocker.GetMock<IOptions<ServerOptions>>()
                .Setup(v => v.Value)
                .Returns(new ServerOptions());

            Mocker.GetMock<IOptions<LogOptions>>()
                .Setup(v => v.Value)
                .Returns(new LogOptions());

            Mocker.GetMock<IOptions<UpdateOptions>>()
                .Setup(v => v.Value)
                .Returns(new UpdateOptions());
        }

        [Test]
        public void GetValue_Success()
        {
            const string key = "Port";
            const string value = "8787";

            var result = Subject.GetValue(key, value);

            result.Should().Be(value);
        }

        [Test]
        public void GetInt_Success()
        {
            const string key = "Port";
            const int value = 8787;

            var result = Subject.GetValueInt(key, value);

            result.Should().Be(value);
        }

        [Test]
        public void GetBool_Success()
        {
            const string key = "LaunchBrowser";
            const bool value = true;

            var result = Subject.GetValueBoolean(key, value);

            result.Should().BeTrue();
        }

        [Test]
        public void GetLaunchBrowser_Success()
        {
            var result = Subject.LaunchBrowser;

            result.Should().Be(true);
        }

        [Test]
        public void GetPort_Success()
        {
            const int value = 8787;

            var result = Subject.Port;

            result.Should().Be(value);
        }

        [Test]
        public void SetValue_bool()
        {
            const string key = "LaunchBrowser";
            const bool value = false;

            Subject.SetValue(key, value);

            var result = Subject.LaunchBrowser;
            result.Should().Be(value);
        }

        [Test]
        public void SetValue_int()
        {
            const string key = "Port";
            const int value = 12345;

            Subject.SetValue(key, value);

            var result = Subject.Port;
            result.Should().Be(value);
        }

        [Test]
        public void GetValue_New_Key()
        {
            const string key = "Hello";
            const string value = "World";

            var result = Subject.GetValue(key, value);

            result.Should().Be(value);
        }

        [Test]
        public void GetAuthenticationType_No_Existing_Value()
        {
            var result = Subject.AuthenticationMethod;

            result.Should().Be(AuthenticationType.None);
        }

        [Test]
        public void SaveDictionary_should_save_proper_value()
        {
            var port = 20555;

            var dic = Subject.GetConfigDictionary();
            dic["Port"] = 20555;

            Subject.SaveConfigDictionary(dic);

            Subject.Port.Should().Be(port);
        }

        [Test]
        public void SaveDictionary_should_only_save_specified_values()
        {
            var port = 20555;
            var origSslPort = 20551;
            var sslPort = 20552;

            var dic = Subject.GetConfigDictionary();
            dic["Port"] = port;
            dic["SslPort"] = origSslPort;
            Subject.SaveConfigDictionary(dic);

            dic = new Dictionary<string, object>();
            dic["SslPort"] = sslPort;
            Subject.SaveConfigDictionary(dic);

            Subject.Port.Should().Be(port);
            Subject.SslPort.Should().Be(sslPort);
        }

        [Test]
        public void trusted_networks_should_default_to_empty_without_changing_server_defaults()
        {
            Subject.TrustedNetworks.Should().BeEmpty();
            XDocument.Parse(_configFileContents).Root.Element("TrustedNetworks").Value.Should().BeEmpty();
            Subject.Port.Should().Be(8787);
            Subject.EnableSsl.Should().BeFalse();
            Subject.AuthenticationMethod.Should().Be(AuthenticationType.None);
        }

        [Test]
        public void trusted_networks_should_read_the_saved_xml_value()
        {
            _configFileContents = "<Config><TrustedNetworks> 192.0.2.10, 2001:db8::10 </TrustedNetworks></Config>";

            Subject.TrustedNetworks.Should().Be("192.0.2.10, 2001:db8::10");
        }

        [Test]
        public void saving_trusted_networks_should_preserve_api_key_and_other_saved_values()
        {
            const string apiKey = "0123456789abcdef0123456789abcdef";
            _configFileContents = "<Config><ApiKey>" + apiKey + "</ApiKey><Port>20555</Port>" +
                "<EnableSsl>False</EnableSsl><LaunchBrowser>False</LaunchBrowser><AuthenticationMethod>Forms</AuthenticationMethod></Config>";

            Subject.SaveConfigDictionary(new Dictionary<string, object>
            {
                { "TrustedNetworks", "192.0.2.10,2001:db8::10" }
            });

            Subject.TrustedNetworks.Should().Be("192.0.2.10,2001:db8::10");
            Subject.ApiKey.Should().Be(apiKey);
            Subject.Port.Should().Be(20555);
            Subject.EnableSsl.Should().BeFalse();
            Subject.LaunchBrowser.Should().BeFalse();
            Subject.AuthenticationMethod.Should().Be(AuthenticationType.Forms);
            var saved = XDocument.Parse(_configFileContents).Root;
            saved.Element("ApiKey").Value.Should().Be(apiKey);
            saved.Element("TrustedNetworks").Value.Should().Be("192.0.2.10,2001:db8::10");
        }

        [Test]
        public void omitted_trusted_networks_should_be_preserved_and_explicit_empty_should_clear()
        {
            _configFileContents = "<Config><TrustedNetworks>192.0.2.10</TrustedNetworks></Config>";

            Subject.SaveConfigDictionary(new Dictionary<string, object> { { "Port", 20555 } });

            Subject.TrustedNetworks.Should().Be("192.0.2.10");

            Subject.SaveConfigDictionary(new Dictionary<string, object> { { "TrustedNetworks", string.Empty } });

            Subject.TrustedNetworks.Should().BeEmpty();
            XDocument.Parse(_configFileContents).Root.Element("TrustedNetworks").Value.Should().BeEmpty();
            Subject.Port.Should().Be(20555);
        }

        [Test]
        [NonParallelizable]
        public void trusted_networks_environment_option_should_override_xml_without_rewriting_it()
        {
            const string environmentKey = "Readarr__Server__TrustedNetworks";
            var previousValue = Environment.GetEnvironmentVariable(environmentKey);
            try
            {
                Environment.SetEnvironmentVariable(environmentKey, "198.51.100.20,2001:db8::10");
                var serverOptions = new ServerOptions();
                new ConfigurationBuilder().AddEnvironmentVariables().Build()
                    .GetSection("Readarr:Server").Bind(serverOptions);
                Mocker.GetMock<IOptions<ServerOptions>>().Setup(options => options.Value).Returns(serverOptions);
                _configFileContents = "<Config><TrustedNetworks>192.0.2.10</TrustedNetworks></Config>";

                Subject.TrustedNetworks.Should().Be("198.51.100.20,2001:db8::10");
                XDocument.Parse(_configFileContents).Root.Element("TrustedNetworks").Value.Should().Be("192.0.2.10");
            }
            finally
            {
                Environment.SetEnvironmentVariable(environmentKey, previousValue);
            }
        }

        [Test]
        public void explicit_empty_trusted_networks_option_should_override_saved_xml()
        {
            Mocker.GetMock<IOptions<ServerOptions>>().Setup(options => options.Value)
                .Returns(new ServerOptions { TrustedNetworks = string.Empty });
            _configFileContents = "<Config><TrustedNetworks>192.0.2.10</TrustedNetworks></Config>";

            Subject.TrustedNetworks.Should().BeEmpty();
            XDocument.Parse(_configFileContents).Root.Element("TrustedNetworks").Value.Should().Be("192.0.2.10");
        }

        [Test]
        public void allowed_hosts_should_default_to_optional_without_changing_other_defaults()
        {
            Subject.AllowedHosts.Should().BeEmpty();
            XDocument.Parse(_configFileContents).Root.Element("AllowedHosts").Value.Should().BeEmpty();
            Subject.Port.Should().Be(8787);
            Subject.AuthenticationMethod.Should().Be(AuthenticationType.None);
        }

        [Test]
        public void allowed_hosts_should_read_saved_xml_without_reinterpreting_it()
        {
            _configFileContents = "<Config><AllowedHosts> Books.TEST; [2001:db8::1] </AllowedHosts></Config>";

            Subject.AllowedHosts.Should().Be("Books.TEST; [2001:db8::1]");
        }

        [Test]
        public void allowed_hosts_save_should_preserve_credentials_and_explicit_false_values()
        {
            const string apiKey = "0123456789abcdef0123456789abcdef";
            _configFileContents = "<Config><ApiKey>" + apiKey + "</ApiKey><Port>20555</Port>" +
                "<EnableSsl>False</EnableSsl><LaunchBrowser>False</LaunchBrowser><AuthenticationMethod>Forms</AuthenticationMethod></Config>";

            Subject.SaveConfigDictionary(new Dictionary<string, object> { { "AllowedHosts", "books.test" } });

            Subject.AllowedHosts.Should().Be("books.test");
            Subject.ApiKey.Should().Be(apiKey);
            Subject.Port.Should().Be(20555);
            Subject.EnableSsl.Should().BeFalse();
            Subject.LaunchBrowser.Should().BeFalse();
            Subject.AuthenticationMethod.Should().Be(AuthenticationType.Forms);
        }

        [Test]
        public void omitted_allowed_hosts_should_be_preserved_and_explicit_empty_should_clear()
        {
            _configFileContents = "<Config><AllowedHosts>books.test</AllowedHosts></Config>";

            Subject.SaveConfigDictionary(new Dictionary<string, object> { { "Port", 20555 } });
            Subject.AllowedHosts.Should().Be("books.test");
            Subject.SaveConfigDictionary(new Dictionary<string, object> { { "AllowedHosts", string.Empty } });

            Subject.AllowedHosts.Should().BeEmpty();
            Subject.Port.Should().Be(20555);
        }

        [Test]
        [NonParallelizable]
        public void allowed_hosts_environment_should_override_xml_without_rewriting_it()
        {
            const string environmentKey = "Readarr__Server__AllowedHosts";
            var previousValue = Environment.GetEnvironmentVariable(environmentKey);
            try
            {
                Environment.SetEnvironmentVariable(environmentKey, "env.books.test");
                var options = new ServerOptions();
                new ConfigurationBuilder().AddEnvironmentVariables().Build().GetSection("Readarr:Server").Bind(options);
                Mocker.GetMock<IOptions<ServerOptions>>().Setup(value => value.Value).Returns(options);
                _configFileContents = "<Config><AllowedHosts>xml.books.test</AllowedHosts></Config>";

                Subject.AllowedHosts.Should().Be("env.books.test");
                XDocument.Parse(_configFileContents).Root.Element("AllowedHosts").Value.Should().Be("xml.books.test");
            }
            finally
            {
                Environment.SetEnvironmentVariable(environmentKey, previousValue);
            }
        }

        [Test]
        public void explicit_empty_allowed_hosts_option_should_override_saved_xml()
        {
            Mocker.GetMock<IOptions<ServerOptions>>().Setup(options => options.Value)
                .Returns(new ServerOptions { AllowedHosts = string.Empty });
            _configFileContents = "<Config><AllowedHosts>xml.books.test</AllowedHosts></Config>";

            Subject.AllowedHosts.Should().BeEmpty();
            XDocument.Parse(_configFileContents).Root.Element("AllowedHosts").Value.Should().Be("xml.books.test");
        }

        [Test]
        public void sql_log_storage_should_default_to_enabled_without_rewriting_the_config()
        {
            _configFileContents = "<Config><Port>19987</Port></Config>";

            Subject.LogDbEnabled.Should().BeTrue();

            _configFileContents.Should().Be("<Config><Port>19987</Port></Config>");
        }

        [TestCase(true)]
        [TestCase(false)]
        public void sql_log_storage_should_honor_an_explicit_saved_value(bool enabled)
        {
            _configFileContents = "<Config><LogDbEnabled>" + enabled + "</LogDbEnabled></Config>";

            Subject.LogDbEnabled.Should().Be(enabled);
        }

        [TestCase(true)]
        [TestCase(false)]
        [NonParallelizable]
        public void sql_log_storage_environment_option_should_override_xml_without_rewriting_it(bool enabled)
        {
            const string environmentKey = "Readarr__Log__DbEnabled";
            var previousValue = Environment.GetEnvironmentVariable(environmentKey);
            try
            {
                Environment.SetEnvironmentVariable(environmentKey, enabled.ToString());
                var logOptions = new LogOptions();
                new ConfigurationBuilder().AddEnvironmentVariables().Build()
                    .GetSection("Readarr:Log").Bind(logOptions);
                Mocker.GetMock<IOptions<LogOptions>>().Setup(options => options.Value).Returns(logOptions);
                _configFileContents = "<Config><LogDbEnabled>" + !enabled + "</LogDbEnabled></Config>";

                Subject.LogDbEnabled.Should().Be(enabled);
                XDocument.Parse(_configFileContents).Root.Element("LogDbEnabled").Value.Should().Be((!enabled).ToString());
            }
            finally
            {
                Environment.SetEnvironmentVariable(environmentKey, previousValue);
            }
        }

        [Test]
        public void saving_other_settings_should_preserve_disabled_sql_logs_and_saved_credentials()
        {
            const string apiKey = "0123456789abcdef0123456789abcdef";
            const string password = "Synthetic password; preserved";
            _configFileContents = "<Config><LogDbEnabled>False</LogDbEnabled><ApiKey>" + apiKey + "</ApiKey>" +
                "<PostgresPassword>" + password + "</PostgresPassword><PostgresMainDb>owned-main</PostgresMainDb>" +
                "<PostgresCacheDb>owned-cache</PostgresCacheDb></Config>";

            Subject.SaveConfigDictionary(new Dictionary<string, object> { { "Port", 19987 } });

            Subject.LogDbEnabled.Should().BeFalse();
            Subject.Port.Should().Be(19987);
            var saved = XDocument.Parse(_configFileContents).Root;
            saved.Element("LogDbEnabled").Value.Should().Be("False");
            saved.Element("ApiKey").Value.Should().Be(apiKey);
            saved.Element("PostgresPassword").Value.Should().Be(password);
            saved.Element("PostgresMainDb").Value.Should().Be("owned-main");
            saved.Element("PostgresCacheDb").Value.Should().Be("owned-cache");
        }

        [TestCase("Main")]
        [TestCase("Cache")]
        [TestCase("Log")]
        public void postgres_connection_strings_should_read_saved_values_without_persisting_defaults(string role)
        {
            var key = "Postgres" + role + "DbConnectionString";
            const string value = "Host=synthetic.invalid;Database=owned;Password=\" secret;with='quotes' \"";
            _configFileContents = new XElement("Config", new XElement(key, value)).ToString();

            PostgresConnectionString(role).Should().Be(value);
            Subject.HasPostgresComponentSettings().Should().BeFalse();
            XDocument.Parse(_configFileContents).Root.Elements().Should().ContainSingle();
        }

        [Test]
        public void absent_postgres_connection_strings_and_component_defaults_should_not_select_or_persist_postgres()
        {
            _configFileContents = "<Config><Port>19987</Port></Config>";

            Subject.PostgresMainDbConnectionString.Should().BeEmpty();
            Subject.PostgresCacheDbConnectionString.Should().BeEmpty();
            Subject.PostgresLogDbConnectionString.Should().BeEmpty();
            Subject.PostgresPort.Should().Be(5432);
            Subject.PostgresMainDb.Should().Be("readarr-main");
            Subject.PostgresCacheDb.Should().Be("readarr-cache");
            Subject.HasPostgresComponentSettings().Should().BeFalse();
            _configFileContents.Should().Be("<Config><Port>19987</Port></Config>");
        }

        [Test]
        public void binding_only_full_strings_should_not_invent_an_explicit_component_port()
        {
            var options = new PostgresOptions();
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string>
            {
                { "MainDbConnectionString", "Host=synthetic.invalid;Database=owned-main" },
                { "CacheDbConnectionString", "Host=synthetic.invalid;Database=owned-cache" },
                { "LogDbConnectionString", "Host=synthetic.invalid;Database=owned-log" }
            }).Build().Bind(options);
            Mocker.GetMock<IOptions<PostgresOptions>>().Setup(item => item.Value).Returns(options);

            options.Port.Should().BeNull();
            options.ConfiguredPort.Should().BeNull();
            Subject.PostgresPort.Should().Be(5432);
            Subject.HasPostgresComponentSettings().Should().BeFalse();
            Subject.PostgresMainDbConnectionString.Should().Contain("owned-main");
        }

        [Test]
        public void an_explicit_empty_bound_port_should_conflict_with_full_strings_without_changing_legacy_port_defaults()
        {
            var options = new PostgresOptions();
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string>
            {
                { "Port", string.Empty },
                { "MainDbConnectionString", "Host=synthetic.invalid;Database=owned-main" },
                { "CacheDbConnectionString", "Host=synthetic.invalid;Database=owned-cache" },
                { "LogDbConnectionString", "Host=synthetic.invalid;Database=owned-log" }
            }).Build().Bind(options);
            Mocker.GetMock<IOptions<PostgresOptions>>().Setup(item => item.Value).Returns(options);

            options.Port.Should().BeNull();
            options.ConfiguredPort.Should().BeEmpty();
            Subject.PostgresPort.Should().Be(5432);
            Subject.HasPostgresComponentSettings().Should().BeTrue();
            var error = Assert.Throws<ReadarrStartupException>(() => new ConnectionStringFactory(Mocker.Resolve<IAppFolderInfo>(), Subject));
            error.InnerException.Should().BeNull();
            error.ToString().Should().NotContain("synthetic.invalid");
        }

        [TestCase("PostgresHost", "")]
        [TestCase("PostgresUser", "")]
        [TestCase("PostgresPassword", "")]
        [TestCase("PostgresPort", "5432")]
        [TestCase("PostgresPort", "0")]
        [TestCase("PostgresMainDb", "readarr-main")]
        [TestCase("PostgresCacheDb", "readarr-cache")]
        [TestCase("PostgresLogDb", "readarr-log")]
        public void explicit_saved_component_presence_should_be_detected_even_when_equal_to_a_default(string key, string value)
        {
            _configFileContents = new XElement("Config", new XElement(key, value)).ToString();

            Subject.HasPostgresComponentSettings().Should().BeTrue();
        }

        [TestCase("Host", "")]
        [TestCase("User", "")]
        [TestCase("Password", "")]
        [TestCase("Port", "5432")]
        [TestCase("Port", "0")]
        [TestCase("MainDb", "readarr-main")]
        [TestCase("CacheDb", "readarr-cache")]
        [TestCase("LogDb", "readarr-log")]
        public void bound_component_presence_should_be_detected_without_confusing_it_with_defaults(string key, string value)
        {
            var options = new PostgresOptions();
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string> { { key, value } })
                .Build().Bind(options);
            Mocker.GetMock<IOptions<PostgresOptions>>().Setup(item => item.Value).Returns(options);

            Subject.HasPostgresComponentSettings().Should().BeTrue();
        }

        [TestCase(true)]
        [TestCase(false)]
        public void component_log_name_should_be_ignored_only_while_database_logging_is_disabled(bool enabled)
        {
            _configFileContents = new XElement(
                "Config",
                new XElement("LogDbEnabled", enabled),
                new XElement("PostgresLogDb", "ignored-log-name")).ToString();
            Mocker.GetMock<IOptions<PostgresOptions>>().Setup(item => item.Value)
                .Returns(new PostgresOptions { LogDb = "ignored-environment-log-name" });

            Subject.HasPostgresComponentSettings().Should().Be(enabled);
        }

        [TestCase("Main")]
        [TestCase("Cache")]
        [TestCase("Log")]
        [NonParallelizable]
        public void postgres_connection_string_environment_values_should_override_xml_without_being_persisted(string role)
        {
            var environmentKey = "Readarr__Postgres__" + role + "DbConnectionString";
            var previous = Environment.GetEnvironmentVariable(environmentKey);
            const string secret = "Host=synthetic.invalid;Database=owned;Password=\" secret;with='quotes' \"";
            try
            {
                Environment.SetEnvironmentVariable(environmentKey, secret);
                var options = new PostgresOptions();
                new ConfigurationBuilder().AddEnvironmentVariables().Build().GetSection("Readarr:Postgres").Bind(options);
                Mocker.GetMock<IOptions<PostgresOptions>>().Setup(item => item.Value).Returns(options);
                var key = "Postgres" + role + "DbConnectionString";
                _configFileContents = new XElement("Config", new XElement(key, "Host=saved.invalid;Database=saved")).ToString();

                PostgresConnectionString(role).Should().Be(secret);
                Subject.SaveConfigDictionary(new Dictionary<string, object> { { "Port", 19987 } });

                XDocument.Parse(_configFileContents).Root.Element(key).Value.Should().Be("Host=saved.invalid;Database=saved");
                _configFileContents.Should().NotContain(secret);
            }
            finally
            {
                Environment.SetEnvironmentVariable(environmentKey, previous);
            }
        }

        [Test]
        public void generating_default_configuration_should_not_copy_environment_connection_strings_to_disk()
        {
            const string secret = "Host=synthetic.invalid;Database=owned;Password=environment-only-sentinel";
            var options = new PostgresOptions
            {
                MainDbConnectionString = secret, CacheDbConnectionString = secret, LogDbConnectionString = secret
            };
            Mocker.GetMock<IOptions<PostgresOptions>>().Setup(item => item.Value).Returns(options);

            Subject.EnsureDefaultConfigFile();

            var saved = XDocument.Parse(_configFileContents).Root;
            saved.Element("PostgresMainDbConnectionString").Should().BeNull();
            saved.Element("PostgresCacheDbConnectionString").Should().BeNull();
            saved.Element("PostgresLogDbConnectionString").Should().BeNull();
            _configFileContents.Should().NotContain("environment-only-sentinel");
        }

        [Test]
        public void saving_other_settings_should_preserve_saved_postgres_connection_strings_and_password_quoting()
        {
            const string secret = "Host=synthetic.invalid;Database=owned;Password=\" secret;with='quotes' \"";
            _configFileContents = new XElement(
                "Config",
                new XElement("PostgresMainDbConnectionString", secret),
                new XElement("PostgresCacheDbConnectionString", secret),
                new XElement("PostgresLogDbConnectionString", secret)).ToString();

            Subject.SaveConfigDictionary(new Dictionary<string, object> { { "Port", 19987 } });

            var saved = XDocument.Parse(_configFileContents).Root;
            saved.Element("PostgresMainDbConnectionString").Value.Should().Be(secret);
            saved.Element("PostgresCacheDbConnectionString").Value.Should().Be(secret);
            saved.Element("PostgresLogDbConnectionString").Value.Should().Be(secret);
            Subject.HasPostgresComponentSettings().Should().BeFalse();
        }

        [Test]
        public void should_throw_if_config_file_is_empty()
        {
            Mocker.GetMock<IDiskProvider>()
                  .Setup(v => v.FileExists(_configFilePath))
                  .Returns(true);

            Assert.Throws<InvalidConfigFileException>(() => Subject.GetValue("key", "value"));
        }

        [Test]
        public void should_throw_if_config_file_contains_only_null_character()
        {
            _configFileContents = "\0";

            Assert.Throws<InvalidConfigFileException>(() => Subject.GetValue("key", "value"));
        }

        [Test]
        public void should_throw_if_config_file_contains_invalid_xml()
        {
            _configFileContents = "{ \"key\": \"value\" }";

            Assert.Throws<InvalidConfigFileException>(() => Subject.GetValue("key", "value"));
        }

        [Test]
        public void invalid_xml_containing_postgres_credentials_should_not_retain_parser_details()
        {
            const string sentinel = "private-connection-credential";
            _configFileContents = "<Config><PostgresMainDbConnectionString>Host=synthetic.invalid;Database=owned;Password=&" +
                sentinel + ";</PostgresMainDbConnectionString></Config>";

            var error = Assert.Throws<InvalidConfigFileException>(() => Subject.GetValue("PostgresMainDbConnectionString", string.Empty, false));

            error.InnerException.Should().BeNull();
            error.ToString().Should().NotContain(sentinel).And.NotContain("synthetic.invalid");
        }

        [Test]
        public void malformed_postgres_option_binding_should_not_retain_configuration_values_in_errors()
        {
            const string sentinel = "private-port-conversion-credential";
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string>
            {
                { "Readarr:Postgres:Port", sentinel },
                { "Readarr:Postgres:MainDbConnectionString", "Host=synthetic.invalid;Database=owned;Password=another-private-credential" }
            }).Build();
            var services = new ServiceCollection();
            services.Configure<PostgresOptions>(configuration.GetSection("Readarr:Postgres"));
            using var provider = services.BuildServiceProvider();
            var postgresOptions = provider.GetRequiredService<IOptions<PostgresOptions>>();

            var error = Assert.Throws<InvalidConfigFileException>(() => new ConfigFileProvider(
                Mocker.Resolve<IAppFolderInfo>(),
                Mocker.Resolve<ICacheManager>(),
                Mocker.GetMock<IEventAggregator>().Object,
                Mocker.GetMock<IDiskProvider>().Object,
                postgresOptions,
                Mocker.GetMock<IOptions<AuthOptions>>().Object,
                Mocker.GetMock<IOptions<AppOptions>>().Object,
                Mocker.GetMock<IOptions<ServerOptions>>().Object,
                Mocker.GetMock<IOptions<UpdateOptions>>().Object,
                Mocker.GetMock<IOptions<LogOptions>>().Object));

            error.InnerException.Should().BeNull();
            error.ToString().Should().NotContain(sentinel).And.NotContain("another-private-credential").And.NotContain("synthetic.invalid");
        }

        private string PostgresConnectionString(string role)
        {
            return role switch
            {
                "Main" => Subject.PostgresMainDbConnectionString,
                "Cache" => Subject.PostgresCacheDbConnectionString,
                "Log" => Subject.PostgresLogDbConnectionString,
                _ => throw new ArgumentException("Unknown owned connection role")
            };
        }
    }
}
