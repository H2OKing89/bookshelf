using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace NzbDrone.Core.MediaFiles.MediaInfo
{
    public interface IMediaInfoProcessRunner
    {
        string Read(string executable, string path);
    }

    public class MediaInfoProcessRunner : IMediaInfoProcessRunner
    {
        public string Read(string executable, string path)
        {
            return Read(executable, path, TimeSpan.FromSeconds(30));
        }

        public string Read(string executable, string path, TimeSpan timeout)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = executable,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            startInfo.ArgumentList.Add("--Output=JSON");
            startInfo.ArgumentList.Add("--Language=raw");
            startInfo.ArgumentList.Add(Path.GetFullPath(path));

            using var process = new Process { StartInfo = startInfo };
            process.Start();
            using var cancellation = new CancellationTokenSource(timeout);
            try
            {
                var output = ReadBounded(process.StandardOutput, MediaInfoJsonParser.MaximumJsonLength, cancellation);
                var errors = ReadBounded(process.StandardError, 16384, cancellation);
                Task.WhenAll(process.WaitForExitAsync(cancellation.Token), output, errors).GetAwaiter().GetResult();
                if (process.ExitCode != 0)
                {
                    throw new InvalidDataException($"MediaInfo exited with code {process.ExitCode}");
                }

                return output.GetAwaiter().GetResult();
            }
            finally
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    process.WaitForExit(5000);
                }
            }
        }

        private static async Task<string> ReadBounded(StreamReader reader, int maximumLength, CancellationTokenSource cancellation)
        {
            var result = new StringBuilder();
            var buffer = new char[4096];
            int count;
            while ((count = await reader.ReadAsync(buffer.AsMemory(), cancellation.Token)) > 0)
            {
                if (result.Length + count > maximumLength)
                {
                    cancellation.Cancel();
                    throw new InvalidDataException("MediaInfo output exceeded the limit");
                }

                result.Append(buffer, 0, count);
            }

            return result.ToString();
        }
    }
}
