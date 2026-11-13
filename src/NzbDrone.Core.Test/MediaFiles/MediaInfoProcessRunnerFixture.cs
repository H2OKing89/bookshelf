using System;
using System.Diagnostics;
using System.IO;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.MediaFiles.MediaInfo;

namespace NzbDrone.Core.Test.MediaFiles
{
    [TestFixture]
    [Platform("Linux")]
    public class MediaInfoProcessRunnerFixture
    {
        private string _directory;
        private string _executable;

        [SetUp]
        public void SetUp()
        {
            _directory = Path.Combine(Path.GetTempPath(), "bookshelf-mediainfo-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_directory);
            _executable = Path.Combine(_directory, "mediainfo");
        }

        [TearDown]
        public void TearDown()
        {
            Directory.Delete(_directory, true);
        }

        private void Script(string script)
        {
            File.WriteAllText(_executable, "#!/bin/sh\n" + script + "\n");
            var start = new ProcessStartInfo("/bin/chmod") { UseShellExecute = false };
            start.ArgumentList.Add("700");
            start.ArgumentList.Add(_executable);
            using var process = Process.Start(start);
            process.WaitForExit();
            process.ExitCode.Should().Be(0);
        }

        [Test]
        public void should_pass_file_paths_as_one_literal_argument()
        {
            Script("printf '%s\\n' \"$#\" \"$3\"");
            var path = Path.Combine(_directory, "a book ' $(touch should-not-exist).m4b");
            var output = new MediaInfoProcessRunner().Read(_executable, path);
            output.Should().Be("3\n" + path + "\n");
            File.Exists(Path.Combine(_directory, "should-not-exist")).Should().BeFalse();
        }

        [Test]
        public void should_stop_a_stalled_probe()
        {
            Script("exec sleep 10");
            var timer = Stopwatch.StartNew();
            Action read = () => new MediaInfoProcessRunner().Read(_executable, "book.m4b", TimeSpan.FromMilliseconds(150));
            read.Should().Throw<OperationCanceledException>();
            timer.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(5));
        }

        [Test]
        public void should_limit_output()
        {
            Script("head -c 1048577 /dev/zero");
            Action read = () => new MediaInfoProcessRunner().Read(_executable, "book.m4b", TimeSpan.FromSeconds(3));
            read.Should().Throw<InvalidDataException>();
        }

        [Test]
        public void should_discard_partial_output_from_failed_probes()
        {
            Script("printf '{\"media\":{}}'; exit 1");
            Action read = () => new MediaInfoProcessRunner().Read(_executable, "book.m4b");
            read.Should().Throw<InvalidDataException>();
        }
    }
}
