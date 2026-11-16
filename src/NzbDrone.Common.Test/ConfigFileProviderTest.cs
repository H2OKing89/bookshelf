using System;
using System.Collections.Generic;
using System.Xml.Linq;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Options;
using NzbDrone.Core.Authentication;
using NzbDrone.Core.Configuration;
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
    }
}
