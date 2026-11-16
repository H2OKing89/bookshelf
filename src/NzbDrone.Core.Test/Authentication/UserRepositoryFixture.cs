using System;
using System.Data.Common;
using System.Globalization;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Authentication;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Authentication
{
    [TestFixture]
    public class UserRepositoryFixture : DbTest<UserRepository, User>
    {
        private const string PasswordHash = "AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8=";
        private const string Salt = "AAECAwQFBgcICQoLDA0ODw==";
        private const int Iterations = 220000;

        [Test]
        public void should_round_trip_salted_password_metadata_and_identity()
        {
            var user = NewUser();

            Subject.Insert(user);

            Subject.Get(user.Id).Should().BeEquivalentTo(user);
            Subject.FindUser(user.Username).Should().BeEquivalentTo(user);
            Subject.FindUser(user.Identifier).Should().BeEquivalentTo(user);
        }

        [Test]
        public void should_persist_changed_password_metadata_without_changing_identity()
        {
            var user = NewUser();
            Subject.Insert(user);
            user.Password = "8RECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8=";
            user.Salt = "8RECAwQFBgcICQoLDA0ODw==";
            user.Iterations = 230000;

            Subject.Update(user);

            Subject.Get(user.Id).Should().BeEquivalentTo(user);
        }

        [Test]
        public void should_redact_password_metadata_from_sql_log_without_changing_user()
        {
            var user = NewUser();

            var sql = SqlBuilderExtensions.GetSqlLogString(
                "UPDATE \"Users\" SET \"Password\" = @Password, \"Salt\" = @Salt, \"Iterations\" = @Iterations WHERE \"Id\" = @Id",
                user);

            AssertRedactedSql(sql, user);
            user.Password.Should().Be(PasswordHash);
            user.Salt.Should().Be(Salt);
            user.Iterations.Should().Be(Iterations);
        }

        [Test]
        public void insert_constraint_failure_should_redact_password_metadata_in_sql_exception_data()
        {
            var user = NewUser();
            Subject.Insert(user);
            var duplicate = NewUser();

            var exception = Assert.Catch<DbException>(() => Subject.Insert(duplicate));

            exception.Data["SQL"].Should().BeOfType<string>();
            AssertRedactedSql((string)exception.Data["SQL"], duplicate);
            duplicate.Id.Should().Be(0);
            duplicate.Password.Should().Be(PasswordHash);
            duplicate.Salt.Should().Be(Salt);
            duplicate.Iterations.Should().Be(Iterations);
            Subject.Single().Should().BeEquivalentTo(user);
        }

        private static User NewUser()
        {
            return new User
            {
                Identifier = Guid.NewGuid(),
                Username = "reader",
                Password = PasswordHash,
                Salt = Salt,
                Iterations = Iterations
            };
        }

        private static void AssertRedactedSql(string sql, User user)
        {
            sql.Should().NotContain(user.Password)
                .And.NotContain(user.Salt)
                .And.NotContain(user.Iterations.ToString(CultureInfo.InvariantCulture));
            sql.Should().Contain(nameof(User.Password))
                .And.Contain(nameof(User.Salt))
                .And.Contain(nameof(User.Iterations))
                .And.Contain("(removed)");
            sql.Should().Contain(user.Username).And.Contain(user.Identifier.ToString());
        }
    }
}
