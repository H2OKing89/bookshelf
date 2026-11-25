using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace NzbDrone.Common.EnvironmentInfo
{
    public interface IStartupContext
    {
        HashSet<string> Flags { get; }
        Dictionary<string, string> Args { get; }
        bool Help { get; }
        bool InstallService { get; }
        bool UninstallService { get; }
        bool RegisterUrl { get; }
        bool PasswordReset { get; }

        string PreservedArguments { get; }
    }

    public class StartupContext : IStartupContext
    {
        public const string APPDATA = "data";
        public const string NO_BROWSER = "nobrowser";
        internal const string INSTALL_SERVICE = "i";
        internal const string UNINSTALL_SERVICE = "u";
        public const string HELP = "?";
        public const string TERMINATE = "terminateexisting";
        public const string RESTART = "restart";
        public const string REGISTER_URL = "registerurl";
        public const string NO_SINGLE_INSTANCE_CHECK = "nosingleinstancecheck";
        public const string EXIT_IMMEDIATELY = "exitimmediately";
        public const string PASSWORD_RESET = "passwordreset";

        public StartupContext(params string[] args)
        {
            Flags = new HashSet<string>();
            Args = new Dictionary<string, string>();

            foreach (var s in args)
            {
                var flag = s.TrimStart(' ', '/', '-');

                var argParts = flag.Split('=', 2);

                if (argParts.Length == 2)
                {
                    var value = argParts[1];
                    if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
                    {
                        value = value[1..^1];
                    }

                    Args.Add(argParts[0].Trim().ToLowerInvariant(), value);
                }
                else
                {
                    Flags.Add(flag.TrimEnd(' ', '/', '-').ToLowerInvariant());
                }
            }
        }

        public HashSet<string> Flags { get; private set; }
        public Dictionary<string, string> Args { get; private set; }

        public bool Help => Flags.Contains(HELP);
        public bool InstallService => Flags.Contains(INSTALL_SERVICE);
        public bool UninstallService => Flags.Contains(UNINSTALL_SERVICE);
        public bool RegisterUrl => Flags.Contains(REGISTER_URL);
        public bool ExitImmediately => Flags.Contains(EXIT_IMMEDIATELY);
        public bool PasswordReset => Flags.Any(flag => flag.StartsWith(PASSWORD_RESET)) || Args.Keys.Any(key => key.StartsWith(PASSWORD_RESET));

        public string PreservedArguments
        {
            get
            {
                var args = "";

                if (Args.ContainsKey(APPDATA))
                {
                    args = QuoteArgument("/data=" + Args[APPDATA]);
                }

                if (Flags.Contains(NO_BROWSER))
                {
                    args += " /" + NO_BROWSER;
                }

                return args.Trim();
            }
        }

        // ProcessStartInfo.Arguments uses .NET's command-line quoting rules on
        // every platform. Preserve one argument, including a path's spaces,
        // equal signs, quotes and backslashes, into either restart or updater.
        public static string QuoteArgument(string value)
        {
            if (value.Length > 0 && !value.Any(c => char.IsWhiteSpace(c) || c == '"'))
            {
                return value;
            }

            var builder = new StringBuilder().Append('"');
            var backslashes = 0;

            foreach (var character in value)
            {
                if (character == '\\')
                {
                    backslashes++;
                    continue;
                }

                builder.Append('\\', character == '"' ? (backslashes * 2) + 1 : backslashes);
                builder.Append(character);
                backslashes = 0;
            }

            return builder.Append('\\', backslashes * 2).Append('"').ToString();
        }
    }
}
