using System;
using System.IO;
using NLog;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.MediaFiles.MediaInfo
{
    public interface IMediaInfoProbe
    {
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

        public MediaInfoModel Read(string path)
        {
            var executable = Path.Combine(AppContext.BaseDirectory, "tools", "mediainfo", "mediainfo");
            if (!File.Exists(executable) || !File.Exists(path))
            {
                return null;
            }

            try
            {
                return MediaInfoJsonParser.ParseCli(_runner.Read(executable, path));
            }
            catch (Exception ex)
            {
                // Neither tag values nor the tool's stdout/stderr belong in the log.
                _logger.Debug("MediaInfo could not inspect {0} ({1}); retaining tag metadata", path, ex.GetType().Name);
                return null;
            }
        }
    }
}
