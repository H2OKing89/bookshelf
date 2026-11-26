using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using MimeKit;
using Moq;
using NLog;
using NUnit.Framework;
using NzbDrone.Common.Http.Dispatchers;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Notifications;
using NzbDrone.Core.Notifications.Email;

namespace NzbDrone.Core.Test.NotificationTests.EmailTests
{
    public class EmailTransportFixture
    {
        [Test]
        public async Task unencrypted_notification_delivers_envelope_and_body_without_bcc_header()
        {
            using var server = new OwnedSmtpServer();
            var settings = Settings(server);
            settings.Cc = new[] { "cc@example.test" };
            settings.Bcc = new[] { "hidden@example.test" };

            Notification().Test(settings).Should().BeNull();
            await server.Completion;

            server.Recipients.Should().BeEquivalentTo("RCPT TO:<to@example.test>", "RCPT TO:<cc@example.test>", "RCPT TO:<hidden@example.test>");
            var message = server.Message();
            message.Subject.Should().Be("Readarr - Test Notification");
            message.TextBody.Should().Contain("Success! You have properly configured your email notification settings");
            message.Bcc.Mailboxes.Should().BeEmpty();
            server.Encrypted.Should().BeFalse();
            server.Authenticated.Should().BeFalse();
        }

        [Test]
        public async Task required_encryption_negotiates_starttls_before_authentication_and_delivery()
        {
            using var server = new OwnedSmtpServer(startTls: true);
            var settings = Settings(server);
            settings.RequireEncryption = true;
            settings.Username = "owned-user";
            settings.Password = "owned-password";
            var policy = new Mock<ICertificateValidationService>();
            policy.Setup(x => x.ShouldByPassValidationError(It.IsAny<object>(), It.IsAny<X509Certificate>(), It.IsAny<X509Chain>(), It.IsAny<SslPolicyErrors>()))
                .Returns((object sender, X509Certificate certificate, X509Chain chain, SslPolicyErrors errors) => sender is string host && host == settings.Server && certificate.GetCertHashString() == server.CertificateHash);

            Notification(policy.Object).Test(settings).Should().BeNull();
            await server.Completion;

            policy.Verify(x => x.ShouldByPassValidationError(It.IsAny<object>(), It.IsAny<X509Certificate>(), It.IsAny<X509Chain>(), It.IsAny<SslPolicyErrors>()), Times.AtLeastOnce());
            server.Encrypted.Should().BeTrue();
            server.Authenticated.Should().BeTrue();
            server.AuthenticatedBeforeTls.Should().BeFalse();
            server.Message().To.Mailboxes.Single().Address.Should().Be("to@example.test");
        }

        [Test]
        public async Task required_encryption_does_not_send_credentials_or_message_without_starttls()
        {
            using var server = new OwnedSmtpServer();
            var settings = Settings(server);
            settings.RequireEncryption = true;
            settings.Username = "owned-user";
            settings.Password = "owned-password";

            var failure = Notification().Test(settings);
            failure.PropertyName.Should().Be("Server");
            failure.ErrorMessage.Should().Be("Unable to send test email");
            await server.Completion;

            server.Authenticated.Should().BeFalse();
            server.Recipients.Should().BeEmpty();
            server.HasMessage.Should().BeFalse();
        }

        [Test]
        public async Task certificate_policy_rejection_prevents_credentials_and_message_delivery()
        {
            using var server = new OwnedSmtpServer(startTls: true);
            var settings = Settings(server);
            settings.RequireEncryption = true;
            settings.Username = "owned-user";
            settings.Password = "owned-password";
            var policy = new Mock<ICertificateValidationService>();
            policy.Setup(x => x.ShouldByPassValidationError(It.IsAny<object>(), It.IsAny<X509Certificate>(), It.IsAny<X509Chain>(), It.IsAny<SslPolicyErrors>())).Returns(false);

            Notification(policy.Object).Test(settings).Should().NotBeNull();
            await server.Completion;

            policy.Verify(x => x.ShouldByPassValidationError(It.IsAny<object>(), It.IsAny<X509Certificate>(), It.IsAny<X509Chain>(), It.IsAny<SslPolicyErrors>()), Times.AtLeastOnce());
            server.Authenticated.Should().BeFalse();
            server.Recipients.Should().BeEmpty();
            server.HasMessage.Should().BeFalse();
        }

        [Test]
        public async Task rejected_recipient_retains_failure_instead_of_reporting_partial_delivery_success()
        {
            using var server = new OwnedSmtpServer();
            var settings = Settings(server);
            settings.Cc = new[] { "rejected@example.test" };

            Notification().Test(settings).Should().NotBeNull();
            await server.Completion;

            server.Recipients.Should().Contain("RCPT TO:<to@example.test>");
            server.Recipients.Should().Contain("RCPT TO:<rejected@example.test>");
            server.HasMessage.Should().BeFalse();
        }

        [Test]
        public async Task import_notification_attaches_ebook_but_not_audiobook()
        {
            using var server = new OwnedSmtpServer();
            var settings = Settings(server);
            settings.AttachFiles = true;
            var email = Notification();
            email.Definition = new NotificationDefinition { Settings = settings };
            var directory = Directory.CreateTempSubdirectory("bookshelf-email-");
            try
            {
                var ebook = Path.Combine(directory.FullName, "owned.epub");
                var audiobook = Path.Combine(directory.FullName, "owned.m4b");
                var bytes = Encoding.UTF8.GetBytes("owned synthetic ebook bytes");
                await File.WriteAllBytesAsync(ebook, bytes);
                await File.WriteAllTextAsync(audiobook, "owned synthetic audiobook bytes");
                email.OnReleaseImport(new BookDownloadMessage
                {
                    Message = "Owned book",
                    BookFiles = new List<BookFile> { new BookFile { Path = ebook }, new BookFile { Path = audiobook } }
                });
                await server.Completion;

                var message = server.Message();
                message.HtmlBody.Should().Contain("Owned book Downloaded and sorted.");
                var attachment = message.Attachments.Should().ContainSingle().Subject.Should().BeOfType<MimePart>().Subject;
                attachment.FileName.Should().EndWith("owned.epub");
                using var output = new MemoryStream();
                attachment.Content.DecodeTo(output);
                output.ToArray().Should().Equal(bytes);
            }
            finally
            {
                directory.Delete(true);
            }
        }

        private static Email Notification(ICertificateValidationService policy = null)
        {
            return new Email(policy ?? Mock.Of<ICertificateValidationService>(), LogManager.CreateNullLogger());
        }

        private static EmailSettings Settings(OwnedSmtpServer server)
        {
            return new EmailSettings
            {
                Server = "127.0.0.1",
                Port = server.Port,
                RequireEncryption = false,
                From = "from@example.test",
                To = new[] { "to@example.test" }
            };
        }

        // The fixture owns both socket endpoints and all mail data. No relay, DNS,
        // configured credentials or trust-store modifications are involved.
        private sealed class OwnedSmtpServer : IDisposable
        {
            private readonly TcpListener _listener = new TcpListener(IPAddress.Loopback, 0);
            private readonly CancellationTokenSource _timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            private readonly X509Certificate2 _certificate;
            private readonly bool _startTls;
            private string _data;

            public OwnedSmtpServer(bool startTls = false)
            {
                _startTls = startTls;
                if (startTls)
                {
                    using var key = RSA.Create(2048);
                    var request = new CertificateRequest("CN=127.0.0.1", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
                    _certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddDays(1));
                }

                _listener.Start();
                Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
                Completion = Run();
            }

            public int Port { get; }
            public Task Completion { get; }
            public bool Encrypted { get; private set; }
            public bool Authenticated { get; private set; }
            public bool AuthenticatedBeforeTls { get; private set; }
            public string CertificateHash => _certificate?.GetCertHashString();
            public List<string> Recipients { get; } = new List<string>();
            public bool HasMessage => _data != null;

            public MimeMessage Message()
            {
                using var stream = new MemoryStream(Encoding.ASCII.GetBytes(_data));
                return MimeMessage.Load(stream);
            }

            public void Dispose()
            {
                _timeout.Cancel();
                _listener.Stop();
                _timeout.Dispose();
                _certificate?.Dispose();
            }

            private async Task Run()
            {
                using var client = await _listener.AcceptTcpClientAsync(_timeout.Token);
                using var network = client.GetStream();
                try
                {
                    await Converse(network, false);
                }
                catch (IOException) when (_startTls && !HasMessage)
                {
                    // A rejecting TLS policy closes the connection before SMTP resumes.
                }
                catch (AuthenticationException) when (_startTls && !HasMessage)
                {
                    // Likewise, some platforms signal a failed handshake directly.
                }
            }

            private async Task Converse(Stream stream, bool encrypted)
            {
                using var reader = new StreamReader(stream, Encoding.ASCII, false, leaveOpen: true);
                using var writer = new StreamWriter(stream, Encoding.ASCII, leaveOpen: true) { NewLine = "\r\n", AutoFlush = true };
                if (!encrypted)
                {
                    await writer.WriteLineAsync("220 owned SMTP");
                }

                while (await reader.ReadLineAsync(_timeout.Token) is string line)
                {
                    if (line.StartsWith("EHLO ", StringComparison.Ordinal))
                    {
                        await writer.WriteLineAsync("250-owned SMTP");
                        if (_startTls && !encrypted)
                        {
                            await writer.WriteLineAsync("250-STARTTLS");
                        }

                        await writer.WriteLineAsync("250 AUTH PLAIN");
                    }
                    else if (line == "STARTTLS" && _startTls && !encrypted)
                    {
                        await writer.WriteLineAsync("220 Ready for TLS");
                        using var tls = new SslStream(stream, leaveInnerStreamOpen: true);
                        await tls.AuthenticateAsServerAsync(new SslServerAuthenticationOptions { ServerCertificate = _certificate }, _timeout.Token);
                        Encrypted = true;
                        await Converse(tls, true);
                        return;
                    }
                    else if (line.StartsWith("AUTH PLAIN ", StringComparison.Ordinal))
                    {
                        AuthenticatedBeforeTls = !encrypted;
                        var value = Encoding.UTF8.GetString(Convert.FromBase64String(line.Substring("AUTH PLAIN ".Length)));
                        Authenticated = value == "\0owned-user\0owned-password";
                        await writer.WriteLineAsync(Authenticated ? "235 Authenticated" : "535 Invalid owned credentials");
                    }
                    else if (line.StartsWith("MAIL FROM:", StringComparison.Ordinal))
                    {
                        await writer.WriteLineAsync("250 OK");
                    }
                    else if (line.StartsWith("RCPT TO:", StringComparison.Ordinal))
                    {
                        Recipients.Add(line);
                        await writer.WriteLineAsync(line.Contains("rejected@example.test", StringComparison.Ordinal) ? "550 Rejected" : "250 OK");
                    }
                    else if (line == "DATA")
                    {
                        await writer.WriteLineAsync("354 End with dot");
                        var data = new StringBuilder();
                        while (await reader.ReadLineAsync(_timeout.Token) is string part && part != ".")
                        {
                            data.Append(part.StartsWith("..", StringComparison.Ordinal) ? part.Substring(1) : part).Append("\r\n");
                        }

                        _data = data.ToString();
                        await writer.WriteLineAsync("250 Delivered");
                    }
                    else if (line == "QUIT")
                    {
                        await writer.WriteLineAsync("221 Bye");
                        return;
                    }
                    else if (line == "RSET")
                    {
                        await writer.WriteLineAsync("250 Reset");
                    }
                    else
                    {
                        await writer.WriteLineAsync("500 Unexpected command");
                    }
                }
            }
        }
    }
}
