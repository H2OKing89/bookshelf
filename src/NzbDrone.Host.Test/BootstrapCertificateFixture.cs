using System;
using System.IO;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Common.Exceptions;
using NzbDrone.Host;
using NzbDrone.Test.Common;

namespace NzbDrone.App.Test
{
    [TestFixture]
    public class BootstrapCertificateFixture : TestBase
    {
        [TestCase(null)]
        [TestCase("")]
        [TestCase("synthetic certificate password")]
        public void should_load_pkcs12_certificate_with_its_private_key(string password)
        {
            var path = WriteCertificate(password);

            using (var certificate = ValidateCertificate(path, password))
            {
                certificate.HasPrivateKey.Should().BeTrue();
                certificate.Subject.Should().Contain("localhost");
            }

            using var exclusive = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            exclusive.CanRead.Should().BeTrue();
        }

        [Test]
        public void should_preserve_startup_error_wrapping_for_wrong_pkcs12_password()
        {
            var path = WriteCertificate("synthetic certificate password");
            var loaderError = Assert.Throws<CryptographicException>(() =>
                X509CertificateLoader.LoadPkcs12FromFile(path, "incorrect password", X509KeyStorageFlags.DefaultKeySet));

            var error = Assert.Throws<ReadarrStartupException>(() => ValidateCertificate(path, "incorrect password"));

            error.Message.Should().Be("Readarr failed to start: " + loaderError.Message);
            error.InnerException.Should().BeNull();
            using var exclusive = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            exclusive.CanRead.Should().BeTrue();
        }

        [Test]
        public void should_preserve_startup_error_wrapping_for_malformed_certificate()
        {
            var path = Path.Combine(TempFolder, "invalid.pfx");
            File.WriteAllText(path, "synthetic invalid certificate");
            var loaderError = Assert.Throws<CryptographicException>(() =>
                X509CertificateLoader.LoadPkcs12FromFile(path, null, X509KeyStorageFlags.DefaultKeySet));

            var error = Assert.Throws<ReadarrStartupException>(() => ValidateCertificate(path, null));

            error.Message.Should().Be("Readarr failed to start: " + loaderError.Message);
            error.InnerException.Should().BeNull();
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

        private static X509Certificate2 ValidateCertificate(string path, string password)
        {
            var method = typeof(Bootstrap).GetMethod("ValidateSslCertificate", BindingFlags.NonPublic | BindingFlags.Static);
            try
            {
                return (X509Certificate2)method.Invoke(null, new object[] { path, password });
            }
            catch (TargetInvocationException error) when (error.InnerException != null)
            {
                ExceptionDispatchInfo.Capture(error.InnerException).Throw();
                throw;
            }
        }
    }
}
