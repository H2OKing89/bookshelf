using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using NLog;
using NzbDrone.Common.Extensions;

namespace NzbDrone.Common.Instrumentation
{
    public class CleanseLogMessage
    {
        private const string FailedMessage = "(removed, log message could not be safely cleansed)";
        private const string DatabaseConfigurationMessage = "(removed, database connection configuration)";
        private const int MaximumMessageLength = 64 * 1024;
        private static readonly TimeSpan MatchTimeout = TimeSpan.FromMilliseconds(100);
        private static readonly TimeSpan CleansingBudget = TimeSpan.FromSeconds(1);

        private static readonly Regex DatabaseConfigurationRegex = new (
            @"(?:Postgres(?:Main|Log|Cache)DbConnectionString|Readarr(?:__|:)Postgres(?:__|:)(?:Main|Log|Cache)DbConnectionString)[""']?\s*[:=]|<\s*Postgres(?:Main|Log|Cache)DbConnectionString\s*>",
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
            MatchTimeout);

        private static readonly Regex DatabaseConnectionRegex = new (
            @"(?<!\w)(?:Host|Server|Data\s+Source|Database|Initial\s+Catalog|User\s+ID)\s*=",
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
            MatchTimeout);

        private static readonly Regex[] CleansingRules =
        {
            // Connection-string quoting permits embedded separators and escaped quotes.
            // Consume an unterminated quoted value through the end rather than leaking a suffix.
            new (@"\b\w*(?:password|passwrd|pwd|username|user\s+id|uid)\s*=\s*(?<secret>""(?:[^""]|"""")*(?:""|$)|'(?:[^']|'')*(?:'|$)|[^;\r\n&]*)", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, MatchTimeout),

            // Url
            new (@"(?<=\?|&|: )((?:api|auth|pass)?key|(?:access[-_]?)?token|auth|user|uid|api|[a-z_]*apikey|account|passwd)=(?<secret>[^&=""]+?)(?=[ ""&=]|$)", RegexOptions.Compiled | RegexOptions.IgnoreCase, MatchTimeout),
            new (@"(?<=\?|&)[^=&?]*?(username|password)=(?<secret>[^&=]+?)(?= |&|$)", RegexOptions.Compiled | RegexOptions.IgnoreCase, MatchTimeout),
            new (@"rss(24h)?\.torrentleech\.org/(?!rss)(?<secret>[0-9a-z]+)", RegexOptions.Compiled | RegexOptions.IgnoreCase, MatchTimeout),
            new (@"torrentleech\.org/rss/download/[0-9]+/(?<secret>[0-9a-z]+)", RegexOptions.Compiled | RegexOptions.IgnoreCase, MatchTimeout),
            new (@"iptorrents\.com/[/a-z0-9?&;]*?(?:[?&;](u|tp)=(?<secret>[^&=;]+?))+(?= |;|&|$)", RegexOptions.Compiled | RegexOptions.IgnoreCase, MatchTimeout),
            new (@"/fetch/[a-z0-9]{32}/(?<secret>[a-z0-9]{32})", RegexOptions.Compiled, MatchTimeout),
            new (@"(getnzb|rss)(?:(?!getnzb|rss).)*?(?<=\?|&)(r)=(?<secret>[^&=]+?)(?= |&|$)", RegexOptions.Compiled | RegexOptions.IgnoreCase, MatchTimeout),
            new (@"\b(\w*)?(_?(?<!use|get_)token|username|passwo?rd)=(?<secret>[^&=]+?)(?= |&|$|;)", RegexOptions.Compiled | RegexOptions.IgnoreCase, MatchTimeout),
            new (@"-hd.me/torrent/[a-z0-9-]\.[0-9]+\.(?<secret>[0-9a-z]+)", RegexOptions.Compiled | RegexOptions.IgnoreCase, MatchTimeout),

            // Trackers Announce Keys; Designed for Qbit Json; should work for all in theory
            new (@"announce(\.php)?(/|%2f|%3fpasskey%3d)(?<secret>[a-z0-9]{16,})|(?:(?<![a-z0-9])|(?<=%2f))(?<secret>[a-z0-9]{16,})(/|%2f)announce", RegexOptions.Compiled | RegexOptions.IgnoreCase, MatchTimeout),

            // Path
            new (@"C:\\Users\\(?<secret>[^\""]+?)(\\|$)", RegexOptions.Compiled | RegexOptions.IgnoreCase, MatchTimeout),
            new (@"/(home|Users)/(?<secret>[^/""]+?)(/|$)", RegexOptions.Compiled | RegexOptions.IgnoreCase, MatchTimeout),

            // Email
            new (@"(?<![a-z0-9._%+-])(?<secret>[a-z0-9._%+-]+)@[a-z0-9.-]+\.[a-z]{2,}\b", RegexOptions.Compiled | RegexOptions.IgnoreCase, MatchTimeout),

            // NzbGet
            new (@"""Name""\s*:\s*""[^""]*(username|password)""\s*,\s*""Value""\s*:\s*""(?<secret>[^""]+?)""", RegexOptions.Compiled | RegexOptions.IgnoreCase, MatchTimeout),

            // Sabnzbd
            new (@"""[^""]*(username|password|api_?key|nzb_key)""\s*:\s*""(?<secret>[^""]+?)""", RegexOptions.Compiled | RegexOptions.IgnoreCase, MatchTimeout),
            new (@"""email_(account|to|from|pwd)""\s*:\s*""(?<secret>[^""]+?)""", RegexOptions.Compiled | RegexOptions.IgnoreCase, MatchTimeout),

            // uTorrent
            new (@"\[""[a-z._]*(username|password)"",\d,""(?<secret>[^""]+?)""", RegexOptions.Compiled | RegexOptions.IgnoreCase, MatchTimeout),
            new (@"\[""(boss_key|boss_key_salt|proxy\.proxy)"",\d,""(?<secret>[^""]+?)""", RegexOptions.Compiled | RegexOptions.IgnoreCase, MatchTimeout),

            // Deluge
            new (@"auth.login\(""(?<secret>[^""]+?)""", RegexOptions.Compiled | RegexOptions.IgnoreCase, MatchTimeout),

            // BroadcastheNet
            new (@"""?method""?\s*:\s*""(getTorrents)"",\s*""?params""?\s*:\s*\[\s*""(?<secret>[^""]+?)""", RegexOptions.Compiled | RegexOptions.IgnoreCase, MatchTimeout),
            new (@"getTorrents\(""(?<secret>[^""]+?)""", RegexOptions.Compiled | RegexOptions.IgnoreCase, MatchTimeout),
            new (@"(?<=\?|&)(authkey|torrent_pass)=(?<secret>[^&=]+?)(?=""|&|$)", RegexOptions.Compiled | RegexOptions.IgnoreCase, MatchTimeout),

            // Good Reads
            new (@"(?<=""(token|tokensecret)"":\s)""(?<secret>[^""]+?)""", RegexOptions.Compiled | RegexOptions.IgnoreCase, MatchTimeout),

            // Webhooks
            // Notifiarr
            new (@"api/v[0-9]/notification/readarr/(?<secret>[\w-]+)", RegexOptions.Compiled | RegexOptions.IgnoreCase, MatchTimeout),

            // Discord
            new (@"discord.com/api/webhooks/((?<secret>[\w-]+)/)?(?<secret>[\w-]+)", RegexOptions.Compiled | RegexOptions.IgnoreCase, MatchTimeout),

            // Telegram
            new (@"api.telegram.org/bot(?<id>[\d]+):(?<secret>[\w-]+)/", RegexOptions.Compiled | RegexOptions.IgnoreCase, MatchTimeout)
        };

        private static readonly Regex CleanseRemoteIPRegex = new (@"(?:Auth-\w+(?<!Failure|Unauthorized) ip|from) (\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3})", RegexOptions.Compiled, MatchTimeout);

        public static string Cleanse(string message)
        {
            return CleanseWithRules(message, CleansingRules);
        }

        public static string Cleanse(LogEventInfo logEvent, Func<LogEventInfo, string> render)
        {
            try
            {
                return Cleanse(render(logEvent));
            }
            catch (Exception)
            {
                // Formatting/exception failures must not enter NLog diagnostics with raw data.
                return FailedMessage;
            }
        }

        internal static string CleanseWithRules(string message, IReadOnlyList<Regex> rules)
        {
            if (message == null)
            {
                return null;
            }

            if (message.Length > MaximumMessageLength)
            {
                return FailedMessage;
            }

            if (string.IsNullOrWhiteSpace(message))
            {
                return message;
            }

            var elapsed = Stopwatch.StartNew();
            try
            {
                // A full database string may contain passwords, certificates and
                // provider options with their own escaping. Do not partially redact it.
                CheckBudget(elapsed);
                if (DatabaseConfigurationRegex.IsMatch(message) ||
                    (message.Contains(';') && DatabaseConnectionRegex.IsMatch(message)))
                {
                    return DatabaseConfigurationMessage;
                }

                foreach (var regex in rules)
                {
                    if (regex.MatchTimeout == Regex.InfiniteMatchTimeout || regex.MatchTimeout > MatchTimeout)
                    {
                        return FailedMessage;
                    }

                    CheckBudget(elapsed);
                    message = regex.Replace(message, match =>
                    {
                        CheckBudget(elapsed);
                        var value = match.Value;
                        foreach (var capture in match.Groups["secret"].Captures.OfType<Capture>().Reverse())
                        {
                            CheckBudget(elapsed);
                            value = value.Replace(capture.Index - match.Index, capture.Length, "(removed)");
                            if (value.Length > MaximumMessageLength)
                            {
                                throw new InvalidOperationException();
                            }
                        }

                        return value;
                    });
                    if (message.Length > MaximumMessageLength)
                    {
                        return FailedMessage;
                    }
                }

                CheckBudget(elapsed);
                message = CleanseRemoteIPRegex.Replace(message, match =>
                {
                    CheckBudget(elapsed);
                    return CleanseRemoteIP(match);
                });
                CheckBudget(elapsed);
                return message.Length <= MaximumMessageLength ? message : FailedMessage;
            }
            catch (Exception)
            {
                // Includes regex timeouts and evaluator failures. Never return a partial
                // result or inspect/log exceptions carrying the original input/pattern.
                return FailedMessage;
            }
        }

        private static void CheckBudget(Stopwatch elapsed)
        {
            if (elapsed.Elapsed >= CleansingBudget)
            {
                throw new TimeoutException();
            }
        }

        private static string CleanseRemoteIP(Match match)
        {
            var group = match.Groups[1];
            var valueIP = group.Value;

            if (IPAddress.TryParse(valueIP, out var address) && !address.IsLocalAddress())
            {
                var prefix = match.Value.Substring(0, group.Index - match.Index);
                var postfix = match.Value.Substring(group.Index + group.Length - match.Index);
                var items = valueIP.Split('.');

                return $"{prefix}{items[0]}.*.*.{items[3]}{postfix}";
            }

            return match.Value;
        }
    }
}
