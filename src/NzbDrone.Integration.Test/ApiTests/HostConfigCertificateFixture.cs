using System;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Test.Common;
using Readarr.Api.V1.Config;

namespace NzbDrone.Integration.Test.ApiTests
{
    [TestFixture]
    public class HostConfigCertificateFixture : TestBase<HostConfigController>
    {
        [TestCase(null)]
        [TestCase("")]
        [TestCase("synthetic certificate password")]
        public void should_accept_pkcs12_settings_and_release_the_certificate_file(string password)
        {
            var path = WriteCertificate(password);

            IsValidCertificate(path, password).Should().BeTrue();

            using var exclusive = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            exclusive.CanRead.Should().BeTrue();
        }

        [Test]
        public void should_reject_wrong_certificate_password_without_throwing()
        {
            var path = WriteCertificate("synthetic certificate password");

            IsValidCertificate(path, "incorrect password").Should().BeFalse();

            using var exclusive = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            exclusive.CanRead.Should().BeTrue();
        }

        [Test]
        public void should_reject_missing_certificate_file_without_throwing()
        {
            IsValidCertificate(Path.Combine(TempFolder, "missing.pfx"), null).Should().BeFalse();
        }

        [Test]
        public void should_reject_malformed_certificate_without_throwing()
        {
            var path = Path.Combine(TempFolder, "invalid.pfx");
            File.WriteAllText(path, "synthetic invalid certificate");

            IsValidCertificate(path, null).Should().BeFalse();
        }

        private string WriteCertificate(string password)
        {
            using var key = RSA.Create(2048);
            var request = new CertificateRequest("CN=localhost", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
            var path = Path.Combine(TempFolder, "synthetic.pfx");
            File.WriteAllBytes(path, certificate.Export(X509ContentType.Pfx, password));
            return path;
        }

        private bool IsValidCertificate(string path, string password)
        {
            var resource = new HostConfigResource { SslCertPath = path, SslCertPassword = password };
            var method = typeof(HostConfigController).GetMethod("IsValidSslCertificate", BindingFlags.NonPublic | BindingFlags.Instance);
            return (bool)method.Invoke(Subject, new object[] { resource });
        }
    }
}
