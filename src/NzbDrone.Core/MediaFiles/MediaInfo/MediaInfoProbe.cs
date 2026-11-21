using System;
using System.IO;
using NLog;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.MediaFiles.MediaInfo
{
    public interface IMediaInfoProbe
    {
        string BundleId { get; }
        MediaInfoModel Read(string path);
    }

    public class MediaInfoProbe : IMediaInfoProbe
    {
        private readonly IMediaInfoProcessRunner _runner;
        private readonly Logger _logger;

        public MediaInfoProbe(IMediaInfoProcessRunner runner, Logger logger)
        {
            _runner = runner;
            _logger = logger;
        }

        public string BundleId => MediaInfoAnalysis.BundleId;

        public MediaInfoModel Read(string path)
        {
            var executable = Path.Combine(AppContext.BaseDirectory, "tools", "mediainfo", "mediainfo");
            if (!File.Exists(executable) || !File.Exists(path))
            {
                return null;
            }

            try
            {
                var before = MediaInfoAnalysis.Capture(path, BundleId);
                var result = MediaInfoJsonParser.ParseCli(_runner.Read(executable, path));
                if (result == null || !MediaInfoAnalysis.SameFile(before, MediaInfoAnalysis.Capture(path, BundleId)))
                {
                    return null;
                }

                MediaInfoAnalysis.Stamp(result, before, DateTime.UtcNow);
                return result;
            }
            catch (Exception ex)
            {
                // Neither tag values nor the tool's stdout/stderr belong in the log.
                _logger.Debug("MediaInfo could not inspect an audio file ({0}); retaining tag metadata", ex.GetType().Name);
                return null;
            }
        }
    }
}
