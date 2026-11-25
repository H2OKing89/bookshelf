using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using NzbDrone.Common.Cache;
using NzbDrone.Common.Serializer;
using NzbDrone.Core.ThingiProvider;

namespace NzbDrone.Core.Notifications
{
    // A media library refresh is idempotent. Keep pending targets in memory until
    // a complete successful send, and retry after the next import or periodic
    // download-monitor command. Never replay an obsolete connection's work against
    // a different configuration, even when two connections share a hostname.
    public class MediaServerUpdateQueue<TProvider>
    {
        private readonly ICached<UpdateState> _queues;
        private readonly IEqualityComparer<string> _comparer;

        private class UpdateState
        {
            public UpdateState(IEqualityComparer<string> comparer)
            {
                Pending = new HashSet<string>(comparer);
            }

            public HashSet<string> Pending { get; }
            public bool Refreshing { get; set; }
        }

        public MediaServerUpdateQueue(ICacheManager cacheManager, IEqualityComparer<string> comparer = null)
        {
            // Cache entries are shared across provider instances. Pending work for
            // an active connection is not subject to an arbitrary expiration.
            _queues = cacheManager.GetCache<UpdateState>(typeof(TProvider), "mediaServerUpdates");
            _comparer = comparer ?? StringComparer.Ordinal;
        }

        private static string Key(ProviderDefinition definition)
        {
            var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(STJson.ToJson(definition.Settings))));
            return $"{definition.Id}:{fingerprint}";
        }

        public void Add(ProviderDefinition definition, IEnumerable<string> targets)
        {
            var items = targets.Where(target => !string.IsNullOrWhiteSpace(target)).ToList();
            if (items.Count == 0)
            {
                return;
            }

            var state = _queues.Get(Key(definition), () => new UpdateState(_comparer));
            lock (state)
            {
                state.Pending.UnionWith(items);
            }
        }

        public bool Process(ProviderDefinition definition, Action<List<string>> refresh)
        {
            var state = _queues.Find(Key(definition));
            if (state == null)
            {
                return false;
            }

            lock (state)
            {
                if (state.Refreshing || state.Pending.Count == 0)
                {
                    return false;
                }

                state.Refreshing = true;
            }

            var refreshed = false;
            while (true)
            {
                List<string> targets;
                lock (state)
                {
                    if (state.Pending.Count == 0)
                    {
                        state.Refreshing = false;
                        return refreshed;
                    }

                    targets = state.Pending.ToList();
                    state.Pending.Clear();
                }

                try
                {
                    refresh(targets);
                    refreshed = true;
                }
                catch
                {
                    // A remote operation might have partially succeeded. Replaying
                    // that idempotent refresh is preferable to losing a target.
                    // Arrivals during in-flight work remain pending as well.
                    lock (state)
                    {
                        state.Pending.UnionWith(targets);
                        state.Refreshing = false;
                    }

                    throw;
                }
            }
        }
    }
}
