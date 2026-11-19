using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using NzbDrone.Common.Disk;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Authentication
{
    public interface IUserService
    {
        User Add(string username, string password);
        User Update(User user);
        User Upsert(string username, string password);
        User Upsert(Guid identifier, string username, string password);
        User ResetPassword(Guid identifier, string password);
        List<User> GetUsers();
        User FindUser();
        User FindUser(string username, string password);
        User FindUser(Guid identifier);
    }

    public class UserService : IUserService
    {
        private const int PasswordIterations = 220000;
        private const int MaximumPasswordIterations = 1000000;
        private const int SaltSize = 16;
        private const int PasswordSize = 32;

        private readonly IUserRepository _repo;
        private readonly IAppFolderInfo _appFolderInfo;
        private readonly IDiskProvider _diskProvider;

        public UserService(IUserRepository repo, IAppFolderInfo appFolderInfo, IDiskProvider diskProvider)
        {
            _repo = repo;
            _appFolderInfo = appFolderInfo;
            _diskProvider = diskProvider;
        }

        public User Add(string username, string password)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(username);
            ArgumentException.ThrowIfNullOrWhiteSpace(password);
            if (_repo.All().Any(existing => string.Equals(existing.Username, username, StringComparison.OrdinalIgnoreCase)))
            {
                throw new ModelConflictException(typeof(User), 0, "Username belongs to another account");
            }

            var user = new User
            {
                Identifier = Guid.NewGuid(),
                Username = username.ToLowerInvariant()
            };

            SetPassword(user, password);

            return _repo.Insert(user);
        }

        public User Update(User user)
        {
            return _repo.Update(user);
        }

        public User Upsert(string username, string password)
        {
            var user = FindUser();

            if (user == null)
            {
                return password == null ? null : Add(username, password);
            }

            return UpdateCredentials(user, username, password);
        }

        public User Upsert(Guid identifier, string username, string password)
        {
            var user = _repo.FindUser(identifier);
            if (user == null)
            {
                throw new ModelNotFoundException(typeof(User), 0);
            }

            return UpdateCredentials(user, username, password);
        }

        public User ResetPassword(Guid identifier, string password)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(password);
            var user = _repo.FindUser(identifier);
            if (user == null)
            {
                throw new ModelNotFoundException(typeof(User), 0);
            }

            var updated = CopyUser(user);
            SetPassword(updated, password);
            if (!_repo.TryUpdateCredentials(user, updated, false, true))
            {
                throw new ModelConflictException(typeof(User), user.Id, "Account changed during password recovery; try again");
            }

            return updated;
        }

        public List<User> GetUsers()
        {
            return _repo.All().OrderBy(user => user.Id).ToList();
        }

        private User UpdateCredentials(User user, string username, string password)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(username);
            var updated = CopyUser(user);

            // Older settings clients round-trip the stored SHA-256 digest.
            var updatePassword = password != null && !(IsLegacyPassword(user) && IsMatchingHash(user.Password, password));
            if (updatePassword)
            {
                SetPassword(updated, password);
            }

            updated.Username = username.ToLowerInvariant();

            if (!_repo.TryUpdateCredentials(user, updated, true, updatePassword))
            {
                throw new ModelConflictException(typeof(User), user.Id, "Account changed while saving; reload settings and try again");
            }

            return updated;
        }

        public User FindUser()
        {
            return _repo.SingleOrDefault();
        }

        public User FindUser(string username, string password)
        {
            if (username.IsNullOrWhiteSpace() || password.IsNullOrWhiteSpace())
            {
                return null;
            }

            for (var attempt = 0; attempt < 2; attempt++)
            {
                var user = _repo.FindUser(username.ToLowerInvariant());
                if (user == null)
                {
                    return null;
                }

                var legacy = IsLegacyPassword(user);
                if (legacy ? !IsMatchingHash(user.Password, password.SHA256Hash()) : !VerifyPassword(user, password))
                {
                    return null;
                }

                if (!legacy && user.Iterations >= PasswordIterations)
                {
                    return user;
                }

                var updated = CopyUser(user);
                SetPassword(updated, password);

                if (_repo.TryUpdateCredentials(user, updated, false, true))
                {
                    return updated;
                }
            }

            return null;
        }

        public User FindUser(Guid identifier)
        {
            return _repo.FindUser(identifier);
        }

        private static bool IsLegacyPassword(User user)
        {
            return string.IsNullOrEmpty(user.Salt) && user.Iterations == 0 && user.Password?.Length == 64;
        }

        private static User CopyUser(User user)
        {
            return new User
            {
                Id = user.Id,
                Identifier = user.Identifier,
                Username = user.Username,
                Password = user.Password,
                Salt = user.Salt,
                Iterations = user.Iterations
            };
        }

        private static void SetPassword(User user, string password)
        {
            var salt = RandomNumberGenerator.GetBytes(SaltSize);
            var hash = Convert.ToBase64String(DerivePassword(password, salt, PasswordIterations));

            user.Salt = Convert.ToBase64String(salt);
            user.Iterations = PasswordIterations;
            user.Password = hash;
        }

        private static byte[] DerivePassword(string password, byte[] salt, int iterations)
        {
            return Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA512, PasswordSize);
        }

        private static bool VerifyPassword(User user, string password)
        {
            // Bound corrupt database metadata before decoding or performing expensive derivation.
            if (user.Iterations <= 0 || user.Iterations > MaximumPasswordIterations ||
                user.Salt?.Length != 24 || user.Password?.Length != 44)
            {
                return false;
            }

            var salt = new byte[SaltSize];
            var storedPassword = new byte[PasswordSize];

            if (!Convert.TryFromBase64String(user.Salt, salt, out var saltLength) || saltLength != SaltSize ||
                !Convert.TryFromBase64String(user.Password, storedPassword, out var passwordLength) || passwordLength != PasswordSize)
            {
                return false;
            }

            return CryptographicOperations.FixedTimeEquals(storedPassword, DerivePassword(password, salt, user.Iterations));
        }

        private static bool IsMatchingHash(string storedHash, string hash)
        {
            return storedHash != null && hash != null && storedHash.Length == hash.Length &&
                CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(storedHash), Encoding.UTF8.GetBytes(hash));
        }
    }
}
