using System;
using System.Data;
using System.Data.Common;
using FluentAssertions;
using Moq;
using Moq.Protected;
using NUnit.Framework;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Test.Datastore
{
    [TestFixture]
    public class DatabaseVersionParserFixture
    {
        [TestCase("3.44.2", "3.44.2")]
        [TestCase("14.8 (Debian 14.8-1.pgdg110+1)", "14.8")]
        [TestCase("16.3 - Percona Distribution", "16.3")]
        [TestCase("17.0", "17.0")]
        [TestCase("  16.12 (Alpine)", "16.12")]
        [TestCase("3.44.2.1", "3.44.2.1")]
        public void should_read_numeric_version_without_vendor_suffix(string source, string expected)
        {
            DatabaseVersionParser.ParseServerVersion(source).Should().Be(new Version(expected));
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase(" ")]
        [TestCase("unexpected secret sentinel")]
        [TestCase("16")]
        [TestCase("16..3")]
        [TestCase("2147483648.0")]
        public void invalid_versions_should_fail_without_echoing_provider_text(string source)
        {
            var error = Assert.Throws<FormatException>(() => DatabaseVersionParser.ParseServerVersion(source));

            error.Message.Should().Be("The database provider returned an invalid server version.");
        }

        [Test]
        public void database_version_should_use_provider_metadata_and_dispose_its_connection()
        {
            var connection = new Mock<DbConnection>(MockBehavior.Strict);
            connection.SetupGet(item => item.ServerVersion).Returns("16.3 (owned fixture)");
            connection.Protected().Setup("Dispose", new object[] { true });
            var database = new Database("owned", () => connection.Object);

            database.Version.Should().Be(new Version(16, 3));

            connection.VerifyGet(item => item.ServerVersion, Times.Once());
            connection.Protected().Verify("Dispose", Times.Once(), new object[] { true });
        }

        [Test]
        public void provider_version_failure_should_not_execute_a_fallback_sql_command()
        {
            var connection = new Mock<DbConnection>(MockBehavior.Strict);
            connection.SetupGet(item => item.ServerVersion).Throws(new InvalidOperationException("Owned provider failure"));
            connection.Protected().Setup("Dispose", new object[] { true });
            var database = new Database("owned", () => connection.Object);

            Assert.Throws<InvalidOperationException>(() => _ = database.Version);

            connection.Protected().Verify("Dispose", Times.Once(), new object[] { true });
        }

        [Test]
        public void unsupported_provider_should_fail_without_probing_with_sql()
        {
            var connection = new Mock<IDbConnection>(MockBehavior.Strict);
            connection.Setup(item => item.Dispose());
            var database = new Database("owned", () => connection.Object);

            Assert.Throws<NotSupportedException>(() => _ = database.Version);

            connection.Verify(item => item.Dispose(), Times.Once());
        }
    }
}
