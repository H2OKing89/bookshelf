using System;

namespace NzbDrone.Core.Authentication
{
    public interface ILocalPasswordRecovery
    {
        Guid FindAccount(string selector);
        void ResetPassword(Guid identifier, string password);
    }

    public class LocalPasswordRecovery : ILocalPasswordRecovery
    {
        private readonly IUserRepository _repository;
        private readonly IUserService _users;

        public LocalPasswordRecovery(IUserRepository repository, IUserService users)
        {
            _repository = repository;
            _users = users;
        }

        public Guid FindAccount(string selector)
        {
            if (string.IsNullOrWhiteSpace(selector))
            {
                throw new InvalidOperationException("Select an existing account.");
            }

            User user;
            if (selector.StartsWith("id:", StringComparison.OrdinalIgnoreCase))
            {
                user = Guid.TryParse(selector.Substring(3), out var identifier) ? _repository.FindUser(identifier) : null;
            }
            else
            {
                user = _repository.FindUser(selector.ToLowerInvariant());
                if (Guid.TryParse(selector, out var identifier))
                {
                    var byId = _repository.FindUser(identifier);
                    if (user != null && byId != null && user.Id != byId.Id)
                    {
                        throw new InvalidOperationException("Account selection is ambiguous; use an explicit id: identifier.");
                    }

                    user ??= byId;
                }
            }

            return user?.Identifier ?? throw new InvalidOperationException("The selected existing account could not be found.");
        }

        public void ResetPassword(Guid identifier, string password)
        {
            _users.ResetPassword(identifier, password);
        }
    }
}
