using System;
using System.Linq;
using Dapper;
using Npgsql;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.Authentication
{
    public interface IUserRepository : IBasicRepository<User>
    {
        User FindUser(string username);
        User FindUser(Guid identifier);
        bool TryUpdateCredentials(User original, User updated, bool updateUsername, bool updatePassword);
    }

    public class UserRepository : BasicRepository<User>, IUserRepository
    {
        public UserRepository(IMainDatabase database, IEventAggregator eventAggregator)
            : base(database, eventAggregator)
        {
        }

        public User FindUser(string username)
        {
            return Query(x => x.Username == username).SingleOrDefault();
        }

        public User FindUser(Guid identifier)
        {
            return Query(x => x.Identifier == identifier).SingleOrDefault();
        }

        public bool TryUpdateCredentials(User original, User updated, bool updateUsername, bool updatePassword)
        {
            if (!updateUsername && !updatePassword)
            {
                throw new ArgumentException("A credential field must be selected for update");
            }

            var fields = updateUsername ? "\"Username\" = @Username" : string.Empty;

            if (updatePassword)
            {
                fields += (updateUsername ? ", " : string.Empty) +
                    "\"Password\" = @Password, \"Salt\" = @Salt, \"Iterations\" = @Iterations";
            }

            var sql = $"UPDATE \"Users\" SET {fields} WHERE \"Id\" = @Id AND \"Identifier\" = @Identifier AND \"Username\" = @OriginalUsername";

            if (updatePassword)
            {
                sql += " AND \"Password\" = @OriginalPassword AND COALESCE(\"Salt\", '') = @OriginalSalt AND COALESCE(\"Iterations\", 0) = @OriginalIterations";
            }

            if (updateUsername)
            {
                sql += " AND NOT EXISTS (SELECT 1 FROM \"Users\" AS other WHERE LOWER(other.\"Username\") = LOWER(@Username) AND other.\"Id\" <> @Id)";
            }

            int affected;
            using (var connection = _database.OpenConnection())
            {
                using var transaction = updateUsername ? connection.BeginTransaction() : null;
                if (updateUsername && connection is NpgsqlConnection)
                {
                    // Serialize account renames across processes without imposing a
                    // new unique index on identities retained from older homes.
                    connection.Execute("SELECT pg_catalog.pg_advisory_xact_lock(1800133326)", transaction: transaction);
                }

                // Credential parameters must never enter SQL traces or exception diagnostic data.
                affected = connection.Execute(
                    sql,
                    new
                    {
                        original.Id,
                        original.Identifier,
                        OriginalUsername = original.Username,
                        OriginalPassword = original.Password,
                        OriginalSalt = original.Salt ?? string.Empty,
                        OriginalIterations = original.Iterations,
                        updated.Username,
                        updated.Password,
                        updated.Salt,
                        updated.Iterations
                    },
                    transaction: transaction);
                transaction?.Commit();
            }

            if (affected == 0)
            {
                return false;
            }

            if (affected != 1)
            {
                throw new InvalidOperationException("Expected to update one user record");
            }

            ModelUpdated(updated);
            return true;
        }
    }
}
