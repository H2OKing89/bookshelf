using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Common.Exceptions;
using NzbDrone.Common.Http;
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
            using var chain = new SyntheticCertificateChain();
            var path = Path.Combine(TempFolder, "synthetic.pfx");
            File.WriteAllBytes(path, chain.Pfx(password));

            using (var certificate = LoadCertificate(path, null, password))
            {
                certificate.Context.TargetCertificate.HasPrivateKey.Should().BeTrue();
                certificate.Context.TargetCertificate.Thumbprint.Should().Be(chain.Leaf.Thumbprint);
                certificate.Context.IntermediateCertificates.Select(c => c.Thumbprint).Should().Contain(chain.Intermediate.Thumbprint);
            }

            using var exclusive = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            exclusive.CanRead.Should().BeTrue();
        }

        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        [TestCase(true, true)]
        public async Task pem_certificate_should_select_matching_leaf_and_send_intermediate_chain(bool separateKey, bool encrypted)
        {
            using var chain = new SyntheticCertificateChain();
            var key = chain.KeyPem(encrypted);
            var certPath = Path.Combine(TempFolder, "certificates.pem");
            var keyPath = separateKey ? Path.Combine(TempFolder, "key.pem") : string.Empty;
            File.WriteAllText(certPath, chain.ReversedCertificatePem + (separateKey ? string.Empty : "\n" + key));
            if (separateKey)
            {
                File.WriteAllText(keyPath, key);
            }

            var certBytes = File.ReadAllBytes(certPath);
            var keyBytes = separateKey ? File.ReadAllBytes(keyPath) : null;
            using (var material = LoadCertificate(certPath, keyPath, encrypted ? SyntheticCertificateChain.Password : string.Empty))
            {
                material.Context.TargetCertificate.Thumbprint.Should().Be(chain.Leaf.Thumbprint);
                material.Context.IntermediateCertificates.Select(c => c.Thumbprint).Should().Contain(chain.Intermediate.Thumbprint);
                await Handshake(material, chain);
            }

            File.ReadAllBytes(certPath).Should().Equal(certBytes);
            if (separateKey)
            {
                File.ReadAllBytes(keyPath).Should().Equal(keyBytes);
            }
        }

        [Test]
        public async Task pkcs12_certificate_should_send_intermediate_chain()
        {
            using var chain = new SyntheticCertificateChain();
            var path = Path.Combine(TempFolder, "synthetic.p12");
            File.WriteAllBytes(path, chain.Pfx(SyntheticCertificateChain.Password));
            using var material = LoadCertificate(path, string.Empty, SyntheticCertificateChain.Password);
            await Handshake(material, chain);
        }

        [Test]
        public void should_fail_safely_for_wrong_password_missing_key_and_unsupported_type()
        {
            using var chain = new SyntheticCertificateChain();
            using var unrelatedKey = RSA.Create(2048);
            var certPath = Path.Combine(TempFolder, "certificate.pem");
            var wrongKeyPath = Path.Combine(TempFolder, "wrong-key.pem");
            var encryptedKeyPath = Path.Combine(TempFolder, "encrypted.pem");
            var invalidPath = Path.Combine(TempFolder, "invalid-cert.pem");
            var derPath = Path.Combine(TempFolder, "certificate.der");
            var pfxPath = Path.Combine(TempFolder, "certificate.pfx");
            File.WriteAllText(certPath, chain.ReversedCertificatePem);
            File.WriteAllText(wrongKeyPath, unrelatedKey.ExportRSAPrivateKeyPem());
            File.WriteAllText(encryptedKeyPath, chain.KeyPem(true));
            File.WriteAllText(invalidPath, "INVALID-CERTIFICATE-PRIVATE-MARKER");
            File.WriteAllBytes(derPath, chain.Leaf.Export(X509ContentType.Cert));
            File.WriteAllBytes(pfxPath, chain.Pfx(SyntheticCertificateChain.Password));

            var wrongPassword = "synthetic-incorrect-secret";
            foreach (var test in new[]
            {
                (certPath, encryptedKeyPath, wrongPassword), (certPath, wrongKeyPath, string.Empty),
                (certPath, Path.Combine(TempFolder, "missing.key"), string.Empty), (certPath, null, string.Empty),
                (invalidPath, null, wrongPassword), (derPath, wrongKeyPath, string.Empty),
                (pfxPath, null, wrongPassword), (pfxPath, wrongKeyPath, SyntheticCertificateChain.Password),
                ("relative.pem", string.Empty, wrongPassword), (Path.Combine(TempFolder, "missing.pem"), null, wrongPassword)
            })
            {
                var startupError = Assert.Throws<ReadarrStartupException>(() => LoadCertificate(test.Item1, test.Item2, test.Item3));
                startupError.Message.Should().StartWith("Readarr failed to start: ");
                startupError.Message.Should().NotContain(wrongPassword).And.NotContain(SyntheticCertificateChain.Password)
                    .And.NotContain("INVALID-CERTIFICATE-PRIVATE-MARKER");
                startupError.InnerException.Should().BeNull();
            }
        }

        private static LoadedSslCertificate LoadCertificate(string certPath, string keyPath, string password)
        {
            var method = typeof(Bootstrap).GetMethod("LoadSslCertificate", BindingFlags.NonPublic | BindingFlags.Static);
            try
            {
                return (LoadedSslCertificate)method.Invoke(null, new object[] { certPath, keyPath, password });
            }
            catch (TargetInvocationException error) when (error.InnerException != null)
            {
                ExceptionDispatchInfo.Capture(error.InnerException).Throw();
                throw;
            }
        }

        private static async Task Handshake(LoadedSslCertificate material, SyntheticCertificateChain chain)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            try
            {
                using var client = new TcpClient();
                await client.ConnectAsync((IPEndPoint)listener.LocalEndpoint, timeout.Token);
                using var server = await listener.AcceptTcpClientAsync(timeout.Token);
                using var clientStream = new SslStream(client.GetStream());
                using var serverStream = new SslStream(server.GetStream());
                var policy = new X509ChainPolicy
                {
                    TrustMode = X509ChainTrustMode.CustomRootTrust,
                    RevocationMode = X509RevocationMode.NoCheck,
                    DisableCertificateDownloads = true
                };
                policy.CustomTrustStore.Add(chain.Root);

                // The client trusts only the root; the intermediate must
                // actually travel across this owned local TLS connection.
                await Task.WhenAll(serverStream.AuthenticateAsServerAsync(new SslServerAuthenticationOptions
                    {
                        ServerCertificateContext = material.Context,
                        EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13
                    }, timeout.Token),
                    clientStream.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
                    {
                        TargetHost = "localhost",
                        CertificateChainPolicy = policy
                    }, timeout.Token));
                clientStream.IsAuthenticated.Should().BeTrue();
                clientStream.RemoteCertificate.GetCertHashString().Should().Be(chain.Leaf.Thumbprint);
            }
            finally
            {
                listener.Stop();
            }
        }

        internal sealed class SyntheticCertificateChain : IDisposable
        {
            public const string Password = " synthetic password ";
            private readonly RSA _rootKey = RSA.Create(2048);
            private readonly RSA _intermediateKey = RSA.Create(2048);
            private readonly RSA _leafKey = RSA.Create(2048);

            public SyntheticCertificateChain()
            {
                var start = DateTimeOffset.UtcNow.AddDays(-1);
                var end = DateTimeOffset.UtcNow.AddDays(2);
                var issuer = CaRequest("CN=Synthetic root", _rootKey);
                Root = issuer.CreateSelfSigned(start, end);
                var intermediate = CaRequest("CN=Synthetic intermediate", _intermediateKey);
                using (var publicIntermediate = intermediate.Create(Root, start, end, new byte[] { 1 }))
                {
                    Intermediate = publicIntermediate.CopyWithPrivateKey(_intermediateKey);
                }

                var request = new CertificateRequest("CN=localhost", _leafKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
                var names = new SubjectAlternativeNameBuilder();
                names.AddDnsName("localhost");
                names.AddIpAddress(IPAddress.Loopback);
                request.CertificateExtensions.Add(names.Build());
                request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
                request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, true));
                request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection { new Oid("1.3.6.1.5.5.7.3.1") }, false));
                using var publicLeaf = request.Create(Intermediate, start, end, new byte[] { 2 });
                Leaf = publicLeaf.CopyWithPrivateKey(_leafKey);
            }

            public X509Certificate2 Root { get; }
            public X509Certificate2 Intermediate { get; }
            public X509Certificate2 Leaf { get; }
            public string ReversedCertificatePem => Intermediate.ExportCertificatePem() + "\n" + Leaf.ExportCertificatePem();

            public byte[] Pfx(string password)
            {
                using var intermediate = X509CertificateLoader.LoadCertificate(Intermediate.RawData);
                return new X509Certificate2Collection { Leaf, intermediate }.Export(X509ContentType.Pfx, password);
            }

            public string KeyPem(bool encrypted) => encrypted
                ? _leafKey.ExportEncryptedPkcs8PrivateKeyPem(Password, new PbeParameters(PbeEncryptionAlgorithm.Aes256Cbc, HashAlgorithmName.SHA256, 1024))
                : _leafKey.ExportPkcs8PrivateKeyPem();

            public void Dispose()
            {
                Leaf.Dispose();
                Intermediate.Dispose();
                Root.Dispose();
                _leafKey.Dispose();
                _intermediateKey.Dispose();
                _rootKey.Dispose();
            }

            private static CertificateRequest CaRequest(string subject, RSA key)
            {
                var request = new CertificateRequest(subject, key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
                request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
                request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign, true));
                return request;
            }
        }
    }
}
