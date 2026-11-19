using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace NzbDrone.Core.Authentication
{
    public static class UserCredentialStamp
    {
        public const string ClaimType = "credential_stamp";

        public static string Create(User user)
        {
            ArgumentNullException.ThrowIfNull(user);
            var value = string.Join("\n", user.Identifier.ToString("N"), user.Username, user.Password, user.Salt, user.Iterations.ToString(CultureInfo.InvariantCulture));
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
        }

        public static bool Matches(User user, string stamp)
        {
            if (user == null || stamp?.Length != 64)
            {
                return false;
            }

            var expected = Encoding.ASCII.GetBytes(Create(user));
            return CryptographicOperations.FixedTimeEquals(expected, Encoding.UTF8.GetBytes(stamp));
        }
    }
}
