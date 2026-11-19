using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Authentication;

namespace NzbDrone.Host
{
    public interface IRecoveryConsole
    {
        string ReadLine(string prompt, int maximumLength, TimeSpan remaining);
        void WriteLine(string message);
    }

    public class RecoveryConsole : IRecoveryConsole
    {
        public string ReadLine(string prompt, int maximumLength, TimeSpan remaining)
        {
            Console.WriteLine(prompt);
            if (remaining <= TimeSpan.Zero)
            {
                throw new TimeoutException();
            }

            return Task.Run(() => ReadBounded(maximumLength, remaining)).WaitAsync(remaining).GetAwaiter().GetResult();
        }

        private static string ReadBounded(int maximumLength, TimeSpan remaining)
        {
            var timer = Stopwatch.StartNew();
            var buffer = new char[maximumLength];
            var count = 0;
            while (timer.Elapsed < remaining)
            {
                char character;
                if (Console.IsInputRedirected)
                {
                    var next = Console.In.Read();
                    if (next < 0)
                    {
                        throw new EndOfStreamException();
                    }

                    character = (char)next;
                }
                else
                {
                    if (!Console.KeyAvailable)
                    {
                        Thread.Sleep(25);
                        continue;
                    }

                    var key = Console.ReadKey(true);
                    if (key.Key == ConsoleKey.Backspace)
                    {
                        if (count > 0)
                        {
                            buffer[--count] = '\0';
                        }

                        continue;
                    }

                    character = key.KeyChar;
                }

                if (character == '\r')
                {
                    if (!Console.IsInputRedirected)
                    {
                        return new string(buffer, 0, count);
                    }

                    continue;
                }

                if (character == '\n')
                {
                    return new string(buffer, 0, count);
                }

                if (char.IsControl(character) || count == maximumLength)
                {
                    throw new InvalidOperationException();
                }

                buffer[count++] = character;
            }

            throw new TimeoutException();
        }

        public void WriteLine(string message)
        {
            Console.WriteLine(message);
        }
    }

    public interface IPasswordResetUtility
    {
        void Run();
    }

    public class PasswordResetUtility : IPasswordResetUtility
    {
        private static readonly TimeSpan PromptTimeout = TimeSpan.FromMinutes(2);
        private readonly IAppFolderInfo _folders;
        private readonly IStartupContext _startup;
        private readonly IAppHomeLock _homeLock;
        private readonly IRecoveryConsole _console;
        private readonly Lazy<ILocalPasswordRecovery> _recovery;

        public PasswordResetUtility(IAppFolderInfo folders,
                                    IStartupContext startup,
                                    IAppHomeLock homeLock,
                                    IRecoveryConsole console,
                                    Lazy<ILocalPasswordRecovery> recovery)
        {
            _folders = folders;
            _startup = startup;
            _homeLock = homeLock;
            _console = console;
            _recovery = recovery;
        }

        public void Run()
        {
            try
            {
                ValidateHomeAndArguments();
                _homeLock.Acquire();
                RejectLivePid();
                var timer = Stopwatch.StartNew();
                var selector = _console.ReadLine("Existing account username or identifier:", 256, PromptTimeout - timer.Elapsed);
                var identifier = _recovery.Value.FindAccount(selector);
                var password = _console.ReadLine("New password (input hidden):", 16384, PromptTimeout - timer.Elapsed);
                var confirmation = _console.ReadLine("Confirm new password (input hidden):", 16384, PromptTimeout - timer.Elapsed);
                if (string.IsNullOrWhiteSpace(password) || password != confirmation || timer.Elapsed >= PromptTimeout)
                {
                    throw new InvalidOperationException();
                }

                RejectLivePid();
                _recovery.Value.ResetPassword(identifier, password);
                _console.WriteLine("Password reset completed. The account identity, authentication mode and API key are unchanged.");
                Environment.ExitCode = 0;
            }
            catch (Exception)
            {
                // This boundary includes input, account and provider exceptions;
                // supplied text and credentials must never be rendered or logged.
                _console.WriteLine("Password reset failed. Stop the server, select an existing compatible home and account, and provide matching nonblank passwords within two minutes.");
                Environment.ExitCode = 1;
            }
        }

        public static void ValidateArguments(IStartupContext startup)
        {
            if (!startup.Args.TryGetValue(StartupContext.APPDATA, out var selected) || string.IsNullOrWhiteSpace(selected) || !Path.IsPathFullyQualified(selected) ||
                startup.Args.Count != 1 ||
                !startup.Flags.Contains(StartupContext.PASSWORD_RESET) ||
                startup.Flags.Count > (startup.Flags.Contains(StartupContext.EXIT_IMMEDIATELY) ? 2 : 1))
            {
                throw new InvalidOperationException();
            }
        }

        private void ValidateHomeAndArguments()
        {
            ValidateArguments(_startup);
            if (!Directory.Exists(_folders.AppDataFolder) || !File.Exists(Path.Combine(_folders.AppDataFolder, "config.xml")))
            {
                throw new InvalidOperationException();
            }

            if (File.Exists(_folders.GetDatabaseRestore()))
            {
                throw new InvalidOperationException();
            }
        }

        private void RejectLivePid()
        {
            var path = Path.Combine(_folders.AppDataFolder, "readarr.pid");
            if (!File.Exists(path))
            {
                return;
            }

            if (!int.TryParse(File.ReadAllText(path), out var id) || id <= 0)
            {
                throw new InvalidOperationException();
            }

            try
            {
                using var process = Process.GetProcessById(id);
                if (!process.HasExited)
                {
                    throw new InvalidOperationException();
                }
            }
            catch (ArgumentException)
            {
                // A stale PID naming no live process is safe; do not delete it.
            }
        }
    }
}
