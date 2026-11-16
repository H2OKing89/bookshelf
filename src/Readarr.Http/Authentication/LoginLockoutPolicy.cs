using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.AspNetCore.Authentication;
using NzbDrone.Core.Authentication;

namespace Readarr.Http.Authentication
{
    public interface ILoginLockoutPolicy
    {
        LoginAttemptResult Authenticate(string clientAddress, Func<User> authenticate);
        DateTime? GetLockoutEndTime(string clientAddress);
    }

    public sealed class LoginAttemptResult
    {
        public LoginAttemptResult(User user, bool isBlocked)
        {
            User = user;
            IsBlocked = isBlocked;
        }

        public User User { get; }
        public bool IsBlocked { get; }
    }

    public class LoginLockoutPolicy : ILoginLockoutPolicy
    {
        public const int MaximumTrackedAddresses = 4096;

        private const int MaximumFailedAttempts = 5;
        private static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(5);
        private readonly object _entriesGate = new object();
        private readonly Dictionary<string, AttemptState> _entries = new Dictionary<string, AttemptState>(StringComparer.Ordinal);
        private readonly ISystemClock _clock;
        private DateTime? _capacityLockedUntilUtc;

        public LoginLockoutPolicy(ISystemClock clock)
        {
            _clock = clock;
        }

        public LoginAttemptResult Authenticate(string clientAddress, Func<User> authenticate)
        {
            var state = Reserve(clientAddress, true);
            if (state == null)
            {
                return new LoginAttemptResult(null, true);
            }

            try
            {
                // Pin the entry until every waiter has finished. Removing an entry
                // while a queued caller still holds it could create a second gate
                // for the same address and let parallel attempts bypass the limit.
                lock (state.Gate)
                {
                    var now = _clock.UtcNow.UtcDateTime;
                    ResetExpired(state, now);
                    if (state.LockedUntilUtc > now)
                    {
                        return new LoginAttemptResult(null, true);
                    }

                    var user = authenticate();
                    if (user != null)
                    {
                        state.FailedAttempts = 0;
                        state.LockedUntilUtc = null;
                        Volatile.Write(ref state.ExpiresAtTicks, DateTime.MinValue.Ticks);
                        return new LoginAttemptResult(user, false);
                    }

                    now = _clock.UtcNow.UtcDateTime;
                    state.FailedAttempts++;
                    var expires = now + LockoutDuration;
                    if (state.FailedAttempts >= MaximumFailedAttempts)
                    {
                        state.LockedUntilUtc = expires;
                    }

                    Volatile.Write(ref state.ExpiresAtTicks, expires.Ticks);
                    return new LoginAttemptResult(null, false);
                }
            }
            finally
            {
                Release(clientAddress, state);
            }
        }

        public DateTime? GetLockoutEndTime(string clientAddress)
        {
            var state = Reserve(clientAddress, false);
            if (state == null)
            {
                lock (_entriesGate)
                {
                    return _entries.Count >= MaximumTrackedAddresses ? _capacityLockedUntilUtc : null;
                }
            }

            try
            {
                lock (state.Gate)
                {
                    var now = _clock.UtcNow.UtcDateTime;
                    ResetExpired(state, now);
                    return state.LockedUntilUtc > now ? state.LockedUntilUtc : null;
                }
            }
            finally
            {
                Release(clientAddress, state);
            }
        }

        private AttemptState Reserve(string clientAddress, bool create)
        {
            lock (_entriesGate)
            {
                var now = _clock.UtcNow.UtcDateTime;
                foreach (var key in _entries.Where(pair => pair.Value.Users == 0 && Volatile.Read(ref pair.Value.ExpiresAtTicks) <= now.Ticks)
                    .Select(pair => pair.Key).ToArray())
                {
                    _entries.Remove(key);
                }

                if (_entries.Count < MaximumTrackedAddresses)
                {
                    _capacityLockedUntilUtc = null;
                }

                if (_entries.TryGetValue(clientAddress, out var state))
                {
                    state.Users++;
                    return state;
                }

                if (_entries.Count >= MaximumTrackedAddresses)
                {
                    if (_capacityLockedUntilUtc == null || _capacityLockedUntilUtc <= now)
                    {
                        _capacityLockedUntilUtc = now + LockoutDuration;
                    }

                    return null;
                }

                if (!create)
                {
                    return null;
                }

                state = new AttemptState { Users = 1, ExpiresAtTicks = (now + LockoutDuration).Ticks };
                _entries.Add(clientAddress, state);
                return state;
            }
        }

        private void Release(string clientAddress, AttemptState state)
        {
            lock (_entriesGate)
            {
                state.Users--;
                if (state.Users == 0 && (state.FailedAttempts == 0 || Volatile.Read(ref state.ExpiresAtTicks) <= _clock.UtcNow.UtcDateTime.Ticks))
                {
                    _entries.Remove(clientAddress);
                    _capacityLockedUntilUtc = null;
                }
            }
        }

        private static void ResetExpired(AttemptState state, DateTime now)
        {
            if (Volatile.Read(ref state.ExpiresAtTicks) <= now.Ticks)
            {
                state.FailedAttempts = 0;
                state.LockedUntilUtc = null;
            }
        }

        private sealed class AttemptState
        {
            public object Gate { get; } = new object();
            public int Users { get; set; }
            public int FailedAttempts { get; set; }
            public DateTime? LockedUntilUtc { get; set; }
            public long ExpiresAtTicks;
        }
    }
}
