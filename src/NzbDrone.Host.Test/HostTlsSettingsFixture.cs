using System.Collections.Generic;
using System.IO;
using FluentAssertions;
using FluentValidation.Results;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Serializer;
using NzbDrone.Core.Authentication;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Validation.Paths;
using NzbDrone.Test.Common;
using Readarr.Api.V1.Config;

namespace NzbDrone.App.Test
{
    [TestFixture]
    public class HostTlsSettingsFixture : TestBase
    {
        private Mock<IConfigFileProvider> _file;
        private Mock<IConfigService> _settings;
        private TestController _controller;
        private Dictionary<string, object> _saved;

        [SetUp]
        public void BeforeTest()
        {
            _file = new Mock<IConfigFileProvider>();
            _settings = new Mock<IConfigService>();
            var users = new Mock<IUserService>();
            users.Setup(u => u.GetUsers()).Returns(new List<User>());
            _controller = new TestController(_file.Object, _settings.Object, users.Object, new FileExistsValidator(Mock.Of<IDiskProvider>()));
            _file.Setup(file => file.SaveConfigDictionary(It.IsAny<Dictionary<string, object>>()))
                .Callback<Dictionary<string, object>>(settings => _saved = settings);
        }

        [Test]
        public void profile_is_opt_in_and_tls_secret_is_masked_on_reads()
        {
            const string secret = "synthetic-tls-secret";
            _file.SetupGet(c => c.SslCertPassword).Returns(secret);
            var result = _controller.GetHostConfig();
            result.SslCertPassword.Should().Be("********");
            result.PerformanceProfilingEnabled.Should().BeFalse();
            result.ToJson().Should().NotContain(secret);
        }

        [TestCase(null)]
        [TestCase("********")]
        public void old_clients_and_masked_passwords_preserve_tls_and_profiling_values(string password)
        {
            using var chain = new BootstrapCertificateFixture.SyntheticCertificateChain();
            var certPath = Path.Combine(TempFolder, "chain.pem");
            var keyPath = Path.Combine(TempFolder, "key.pem");
            File.WriteAllText(certPath, chain.ReversedCertificatePem);
            File.WriteAllText(keyPath, chain.KeyPem(true));
            _file.SetupGet(c => c.SslKeyPath).Returns(keyPath);
            _file.SetupGet(c => c.SslCertPassword).Returns(BootstrapCertificateFixture.SyntheticCertificateChain.Password);
            _file.SetupGet(c => c.PerformanceProfilingEnabled).Returns(true);
            var resource = Resource(certPath);
            resource.SslCertPassword = password;

            _controller.Validate(resource).IsValid.Should().BeTrue();
            _controller.SaveHostConfig(resource);
            _saved.Should().NotContainKeys(nameof(resource.SslKeyPath), nameof(resource.SslCertPassword), nameof(resource.PerformanceProfilingEnabled));
        }

        [Test]
        public void explicit_empty_key_and_password_and_false_profiling_are_saved()
        {
            using var chain = new BootstrapCertificateFixture.SyntheticCertificateChain();
            var certPath = Path.Combine(TempFolder, "chain.pem");
            File.WriteAllText(certPath, chain.ReversedCertificatePem + "\n" + chain.KeyPem(false));
            var resource = Resource(certPath);
            resource.SslKeyPath = string.Empty;
            resource.SslCertPassword = string.Empty;
            resource.PerformanceProfilingEnabled = false;
            _controller.Validate(resource).IsValid.Should().BeTrue();
            _controller.SaveHostConfig(resource);
            _saved[nameof(resource.SslCertPassword)].Should().Be(string.Empty);
            _saved[nameof(resource.SslKeyPath)].Should().Be(string.Empty);
            _saved[nameof(resource.PerformanceProfilingEnabled)].Should().Be(false);
        }

        [Test]
        public void invalid_tls_settings_should_report_sanitized_field_errors()
        {
            using var chain = new BootstrapCertificateFixture.SyntheticCertificateChain();
            var certPath = Path.Combine(TempFolder, "chain.pem");
            var keyPath = Path.Combine(TempFolder, "key.pem");
            File.WriteAllText(certPath, chain.ReversedCertificatePem);
            File.WriteAllText(keyPath, chain.KeyPem(true));
            var resource = Resource(certPath);
            resource.SslKeyPath = keyPath;
            resource.SslCertPassword = "incorrect-private-marker";
            var result = _controller.Validate(resource);
            result.IsValid.Should().BeFalse();
            result.Errors.Should().ContainSingle(failure => failure.PropertyName == nameof(resource.SslKeyPath));
            result.ToJson().Should().NotContain(resource.SslCertPassword);
            _file.Verify(c => c.SaveConfigDictionary(It.IsAny<Dictionary<string, object>>()), Times.Never());
        }

        private static HostConfigResource Resource(string path) => new HostConfigResource
        {
            BindAddress = "*",
            Port = 8787,
            EnableSsl = true,
            SslPort = 6868,
            SslCertPath = path,
            Branch = "develop",
            BackupInterval = 7,
            BackupRetention = 28
        };

        private class TestController : HostConfigController
        {
            public TestController(IConfigFileProvider file, IConfigService config, IUserService users, FileExistsValidator exists)
                : base(file, config, users, exists)
            {
            }

            public ValidationResult Validate(HostConfigResource resource) => SharedValidator.Validate(resource);
        }
    }
}
