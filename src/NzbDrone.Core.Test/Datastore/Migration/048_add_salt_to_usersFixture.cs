using System;
using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Datastore.Migration;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Datastore.Migration
{
    [TestFixture]
    public class add_salt_to_usersFixture : MigrationTest<add_salt_to_users>
    {
        [Test]
        public void should_preserve_legacy_user_identity_password_and_unrelated_configuration()
        {
            var identifier = Guid.NewGuid().ToString();
            var password = "migration-test-password".SHA256Hash();
            var db = WithMigrationTestDb(migration =>
            {
                migration.Insert.IntoTable("Users").Row(new
                {
                    Id = 17,
                    Identifier = identifier,
                    Username = "ExistingReader",
                    Password = password
                });
                migration.Insert.IntoTable("Config").Row(new
                {
                    Id = 57,
                    Key = "bookfolderformat",
                    Value = "{Author Name}/{Book Title}"
                });
            });

            var user = db.Query("SELECT * FROM \"Users\"").Single();
            Convert.ToInt32(user["Id"]).Should().Be(17);
            user["Identifier"].Should().Be(identifier);
            user["Username"].Should().Be("ExistingReader");
            user["Password"].Should().Be(password);
            user["Salt"].Should().BeNull();
            user["Iterations"].Should().BeNull();

            var config = db.Query("SELECT * FROM \"Config\" WHERE \"Id\" = 57").Single();
            Convert.ToInt32(config["Id"]).Should().Be(57);
            config["Key"].Should().Be("bookfolderformat");
            config["Value"].Should().Be("{Author Name}/{Book Title}");
        }

        [Test]
        public void should_add_password_metadata_columns_when_users_table_is_empty()
        {
            var db = WithMigrationTestDb();

            db.Query("SELECT \"Salt\", \"Iterations\" FROM \"Users\"").Should().BeEmpty();
        }
    }
}
