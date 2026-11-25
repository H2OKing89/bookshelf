using System;
using System.IO;
using System.Linq;
using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Extensions;

namespace NzbDrone.Common.Http
{
    public enum SslCertificateField
    {
        CertificatePath,
        KeyPath
    }

    // Deliberately contains only actionable, non-secret text. Crypto/file API
    // exceptions may echo malformed input and must not escape to API or logs.
    public sealed class SslCertificateLoadException : Exception
    {
        public SslCertificateField Field { get; }

        internal SslCertificateLoadException(SslCertificateField field, string message)
            : base(message)
        {
            Field = field;
        }
    }

    public sealed class LoadedSslCertificate : IDisposable
    {
        private readonly X509Certificate2Collection _certificates;

        internal LoadedSslCertificate(SslStreamCertificateContext context, X509Certificate2Collection certificates)
        {
            Context = context;
            _certificates = certificates;
        }

        public SslStreamCertificateContext Context { get; }

        public void Dispose()
        {
            foreach (var certificate in Context.IntermediateCertificates)
            {
                certificate.Dispose();
            }

            foreach (var certificate in _certificates)
            {
                certificate.Dispose();
            }
        }
    }

    public static class SslCertificateLoader
    {
        public static LoadedSslCertificate Load(string certificatePath, string keyPath, string password)
        {
            var bytes = ReadFile(certificatePath, SslCertificateField.CertificatePath);
            var certificates = new X509Certificate2Collection();

            try
            {
                var type = X509Certificate2.GetCertContentType(bytes);
                X509Certificate2 leaf;

                switch (type)
                {
                    case X509ContentType.Pkcs12:
                        if (!string.IsNullOrWhiteSpace(keyPath))
                        {
                            throw new SslCertificateLoadException(SslCertificateField.KeyPath, "A PKCS#12 certificate already contains its private key. Clear the SSL private key path.");
                        }

                        certificates = X509CertificateLoader.LoadPkcs12Collection(bytes, password, KeyStorageFlags);
                        leaf = certificates.Cast<X509Certificate2>().FirstOrDefault(c => c.HasPrivateKey);
                        break;

                    case X509ContentType.Cert when bytes.AsSpan().IndexOf("-----BEGIN CERTIFICATE-----"u8) >= 0:
                        leaf = LoadPem(bytes, keyPath, password, certificates);
                        break;

                    default:
                        throw new SslCertificateLoadException(SslCertificateField.CertificatePath, "The SSL certificate must be PKCS#12 (PFX/P12) or PEM with a matching private key.");
                }

                if (leaf == null || !leaf.HasPrivateKey)
                {
                    throw new SslCertificateLoadException(SslCertificateField.CertificatePath, "The SSL certificate does not contain a matching private key.");
                }

                var chain = new X509Certificate2Collection();
                foreach (var certificate in certificates)
                {
                    // Exclude the leaf (including keyless copies) and duplicate
                    // certificates, but retain the supplied intermediate chain.
                    if (!certificate.Equals(leaf) && !chain.Contains(certificate))
                    {
                        chain.Add(certificate);
                    }
                }

                // Never download missing issuers on startup or on a settings save.
                var context = SslStreamCertificateContext.Create(leaf, chain, offline: true);
                return new LoadedSslCertificate(context, certificates);
            }
            catch (SslCertificateLoadException)
            {
                DisposeCertificates(certificates);
                throw;
            }
            catch (Exception error) when (error is CryptographicException or ArgumentException or NotSupportedException)
            {
                DisposeCertificates(certificates);
                throw new SslCertificateLoadException(SslCertificateField.CertificatePath, "Unable to load the SSL certificate. Check its format, private key, and password.");
            }
            finally
            {
                CryptographicOperations.ZeroMemory(bytes);
            }
        }

        private static X509KeyStorageFlags KeyStorageFlags => OperatingSystem.IsWindows()
            ? X509KeyStorageFlags.DefaultKeySet
            : X509KeyStorageFlags.EphemeralKeySet;

        private static X509Certificate2 LoadPem(byte[] bytes, string keyPath, string password, X509Certificate2Collection certificates)
        {
            var certificatePem = Encoding.UTF8.GetString(bytes);
            certificates.ImportFromPem(certificatePem);
            var separateKey = !string.IsNullOrWhiteSpace(keyPath);
            var field = separateKey ? SslCertificateField.KeyPath : SslCertificateField.CertificatePath;
            var keyPem = certificatePem;
            if (separateKey)
            {
                var keyBytes = ReadFile(keyPath, field);
                try
                {
                    keyPem = Encoding.UTF8.GetString(keyBytes);
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(keyBytes);
                }
            }

            var remaining = keyPem.AsSpan();
            var keyCount = 0;
            var encrypted = false;
            while (PemEncoding.TryFind(remaining, out var fields))
            {
                var label = remaining[fields.Label];
                if (label.SequenceEqual("ENCRYPTED PRIVATE KEY") || label.SequenceEqual("PRIVATE KEY") ||
                    label.SequenceEqual("RSA PRIVATE KEY") || label.SequenceEqual("EC PRIVATE KEY"))
                {
                    keyCount++;
                    encrypted = label.SequenceEqual("ENCRYPTED PRIVATE KEY");
                }

                remaining = remaining[fields.Location.End..];
            }

            if (keyCount != 1)
            {
                throw new SslCertificateLoadException(field, "Provide exactly one supported PEM private key (RSA, EC, PKCS#8 or encrypted PKCS#8).");
            }

            if (!encrypted && !string.IsNullOrEmpty(password))
            {
                throw new SslCertificateLoadException(field, "The PEM private key is not encrypted. Clear the SSL certificate password or supply an encrypted private key.");
            }

            // A full chain may have the leaf in any position. Match the key,
            // not an issuer's subject/serial number, and retain the whole chain.
            foreach (var candidate in certificates)
            {
                X509Certificate2 leaf;
                try
                {
                    leaf = encrypted
                        ? X509Certificate2.CreateFromEncryptedPem(candidate.ExportCertificatePem(), keyPem, password ?? string.Empty)
                        : X509Certificate2.CreateFromPem(candidate.ExportCertificatePem(), keyPem);
                }
                catch (CryptographicException)
                {
                    continue;
                }

                if (OperatingSystem.IsWindows())
                {
                    // Windows TLS requires a usable key container. Reimport
                    // in memory with the historical PFX storage behavior.
                    using (var ephemeralLeaf = leaf)
                    {
                        var pkcs12 = ephemeralLeaf.Export(X509ContentType.Pkcs12);
                        try
                        {
                            leaf = X509CertificateLoader.LoadPkcs12(pkcs12, null, KeyStorageFlags);
                        }
                        finally
                        {
                            CryptographicOperations.ZeroMemory(pkcs12);
                        }
                    }
                }

                certificates.Add(leaf);
                return leaf;
            }

            throw new SslCertificateLoadException(field, "Unable to load the PEM private key. Check that the key matches the certificate and that the password is correct.");
        }

        private static byte[] ReadFile(string path, SslCertificateField field)
        {
            var name = field == SslCertificateField.KeyPath ? "SSL private key" : "SSL certificate";
            if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path) || !path.IsPathValid(PathValidationType.CurrentOs))
            {
                throw new SslCertificateLoadException(field, $"Enter an absolute path to the {name} file.");
            }

            try
            {
                return File.ReadAllBytes(path);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                throw new SslCertificateLoadException(field, $"Cannot read the {name} file. Check that it exists and is readable.");
            }
        }

        private static void DisposeCertificates(X509Certificate2Collection certificates)
        {
            foreach (var certificate in certificates)
            {
                certificate.Dispose();
            }
        }
    }
}
