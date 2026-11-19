using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.Authentication;
using NzbDrone.Host;
using NzbDrone.Test.Common;

namespace NzbDrone.App.Test
{
    [TestFixture]
    [NonParallelizable]
    public class PasswordResetUtilityFixture : TestBase
    {
        private int _previousExitCode;
        private Mock<IRecoveryConsole> _console;
        private Mock<ILocalPasswordRecovery> _recovery;
        private Mock<IAppHomeLock> _homeLock;
        private IAppFolderInfo _folders;

        [SetUp]
        public void Setup()
        {
            _previousExitCode = Environment.ExitCode;
            Directory.CreateDirectory(TempFolder);
            File.WriteAllText(Path.Combine(TempFolder, "config.xml"), "<Config />");
            _folders = Mock.Of<IAppFolderInfo>(folder => folder.AppDataFolder == TempFolder);
            _console = new Mock<IRecoveryConsole>();
            _recovery = new Mock<ILocalPasswordRecovery>(MockBehavior.Strict);
            _homeLock = new Mock<IAppHomeLock>();
        }

        [TearDown]
        public void RestoreExitCode()
        {
            Environment.ExitCode = _previousExitCode;
        }

        [Test]
        public void matching_passwords_should_reset_only_the_selected_existing_account()
        {
            var identifier = Guid.NewGuid();
            _recovery.Setup(service => service.FindAccount("existing")).Returns(identifier);
            _recovery.Setup(service => service.ResetPassword(identifier, "Owned private password"));
            Input("existing", "Owned private password", "Owned private password");

            Utility().Run();

            Environment.ExitCode.Should().Be(0);
            _recovery.Verify(service => service.ResetPassword(identifier, "Owned private password"), Times.Once());
            _console.Verify(console => console.WriteLine(It.Is<string>(message => message.Contains("Owned private password"))), Times.Never());
            _homeLock.Verify(home => home.Acquire(), Times.Once());
        }

        [Test]
        public void mismatched_confirmation_should_not_change_credentials_or_echo_inputs()
        {
            _recovery.Setup(service => service.FindAccount("existing")).Returns(Guid.NewGuid());
            Input("existing", "Owned private password", "Different private password");

            Utility().Run();

            Environment.ExitCode.Should().Be(1);
            _recovery.Verify(service => service.ResetPassword(It.IsAny<Guid>(), It.IsAny<string>()), Times.Never());
            _console.Verify(console => console.WriteLine(It.Is<string>(message => message.Contains("private password"))), Times.Never());
        }

        [Test]
        public void prompt_timeout_should_fail_without_any_credential_write()
        {
            _console.Setup(console => console.ReadLine(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<TimeSpan>())).Throws<TimeoutException>();

            Utility().Run();

            Environment.ExitCode.Should().Be(1);
            _recovery.VerifyNoOtherCalls();
        }

        [TestCase("-password=OwnedSecret")]
        [TestCase("-terminateexisting")]
        [TestCase("-nosingleinstancecheck")]
        public void unsafe_or_secret_argument_should_fail_before_reading_inputs_or_opening_databases(string extra)
        {
            Utility(extra).Run();

            Environment.ExitCode.Should().Be(1);
            _console.Verify(console => console.ReadLine(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<TimeSpan>()), Times.Never());
            _recovery.VerifyNoOtherCalls();
        }

        [Test]
        public void live_pid_should_fail_without_reading_inputs_or_stopping_the_process()
        {
            File.WriteAllText(Path.Combine(TempFolder, "readarr.pid"), Environment.ProcessId.ToString());

            Utility().Run();

            Environment.ExitCode.Should().Be(1);
            _console.Verify(console => console.ReadLine(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<TimeSpan>()), Times.Never());
            _recovery.VerifyNoOtherCalls();
        }

        [Test]
        public void pending_restore_should_not_be_applied_or_overwritten_by_password_recovery()
        {
            var pending = Path.Combine(TempFolder, "readarr.restore");
            File.WriteAllText(pending, "Owned pending restore bytes");

            Utility().Run();

            Environment.ExitCode.Should().Be(1);
            File.ReadAllText(pending).Should().Be("Owned pending restore bytes");
            _recovery.VerifyNoOtherCalls();
        }

        [Test]
        public void eof_should_fail_without_echoing_input_or_changing_credentials()
        {
            _console.Setup(console => console.ReadLine(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<TimeSpan>())).Throws<EndOfStreamException>();

            Utility().Run();

            Environment.ExitCode.Should().Be(1);
            _recovery.VerifyNoOtherCalls();
        }

        [Test]
        public void exclusive_home_lock_should_refuse_a_second_owner_and_release_on_disposal()
        {
            using (var first = new AppHomeLock(_folders))
            {
                first.Acquire();
                using var second = new AppHomeLock(_folders);
                Assert.Throws<NzbDrone.Common.Exceptions.ReadarrStartupException>(() => second.Acquire());
            }

            using var next = new AppHomeLock(_folders);
            Assert.DoesNotThrow(() => next.Acquire());
        }

        [Test]
        public void startup_lock_should_allow_the_existing_process_restart_handoff()
        {
            using var previous = new AppHomeLock(_folders);
            previous.Acquire();
            var release = Task.Run(() =>
            {
                Thread.Sleep(50);
                previous.Dispose();
            });
            using var replacement = new AppHomeLock(_folders);

            Assert.DoesNotThrow(() => replacement.AcquireForStartup());

            release.GetAwaiter().GetResult();
        }

        [Test]
        public void password_reset_should_be_a_utility_mode_even_when_help_or_service_flags_are_present()
        {
            Bootstrap.GetApplicationMode(new StartupContext("-passwordreset", "-?", "-i")).Should().Be(ApplicationModes.PasswordReset);
        }

        [Test]
        public void explicit_termination_policy_should_run_before_waiting_for_the_home_lock()
        {
            var sequence = new MockSequence();
            var policy = new Mock<ISingleInstancePolicy>(MockBehavior.Strict);
            var folders = new Mock<IAppFolderFactory>(MockBehavior.Strict);
            var homeLock = new Mock<IAppHomeLock>(MockBehavior.Strict);
            policy.InSequence(sequence).Setup(service => service.KillAllOtherInstance());
            folders.InSequence(sequence).Setup(service => service.Register());
            homeLock.InSequence(sequence).Setup(service => service.AcquireForStartup());

            Bootstrap.PrepareStartupHome(new StartupContext("-terminateexisting"), policy.Object, folders.Object, homeLock.Object);

            policy.VerifyAll();
            folders.VerifyAll();
            homeLock.VerifyAll();
        }

        [Test]
        public void disabling_process_detection_should_still_acquire_the_exclusive_home_lock()
        {
            var policy = new Mock<ISingleInstancePolicy>(MockBehavior.Strict);
            var folders = new Mock<IAppFolderFactory>();
            var homeLock = new Mock<IAppHomeLock>();

            Bootstrap.PrepareStartupHome(new StartupContext("-nosingleinstancecheck"), policy.Object, folders.Object, homeLock.Object);

            policy.VerifyNoOtherCalls();
            folders.Verify(service => service.Register(), Times.Once());
            homeLock.Verify(service => service.AcquireForStartup(), Times.Once());
        }

        private PasswordResetUtility Utility(string extra = null)
        {
            var args = extra == null
                ? new[] { "-passwordreset", "-data=" + TempFolder }
                : new[] { "-passwordreset", "-data=" + TempFolder, extra };
            return new PasswordResetUtility(_folders, new StartupContext(args), _homeLock.Object, _console.Object, new Lazy<ILocalPasswordRecovery>(() => _recovery.Object));
        }

        private void Input(params string[] values)
        {
            var sequence = _console.SetupSequence(console => console.ReadLine(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<TimeSpan>()));
            foreach (var value in values)
            {
                sequence.Returns(value);
            }
        }
    }
}
