using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using NzbDrone.Core.MediaFiles.MediaInfo;
using NzbDrone.Core.Messaging.Commands;

namespace NzbDrone.Core.Test.MediaFiles.MediaInfo
{
    [TestFixture]
    public class MediaInfoProcessRunnerFixture
    {
        [Test]
        public async Task command_cancellation_terminates_owned_native_probe()
        {
            if (!OperatingSystem.IsLinux())
            {
                Assert.Ignore("Uses an owned synthetic Linux probe process");
            }

            var folder = Directory.CreateTempSubdirectory("bookshelf-probe-");
            var script = Path.Combine(folder.FullName, "probe");
            var input = Path.Combine(folder.FullName, "synthetic.m4b");
            File.WriteAllText(script, "#!/bin/sh\nprintf '%s' \"$$\" > \"$3.pid\"\nexec sleep 60\n");
            File.SetUnixFileMode(script, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            using var cancellation = new CancellationTokenSource();
            using var scope = CommandExecutionContext.Begin(cancellation.Token);
            var reading = Task.Run(() => new MediaInfoProcessRunner().Read(script, input, TimeSpan.FromSeconds(10)));
            try
            {
                var timer = Stopwatch.StartNew();
                while (!File.Exists(input + ".pid") && timer.Elapsed < TimeSpan.FromSeconds(5))
                {
                    await Task.Delay(20);
                }

                Assert.That(File.Exists(input + ".pid"), Is.True);
                var pid = int.Parse(File.ReadAllText(input + ".pid"));
                cancellation.Cancel();
                await Assert.CatchAsync<OperationCanceledException>(async () => await reading.WaitAsync(TimeSpan.FromSeconds(5)));
                Assert.Throws<ArgumentException>(() => Process.GetProcessById(pid));
            }
            finally
            {
                cancellation.Cancel();
                try
                {
                    await reading;
                }
                catch (OperationCanceledException)
                {
                }

                folder.Delete(true);
            }
        }
    }
}
