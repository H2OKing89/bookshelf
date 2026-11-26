using NLog;

namespace NzbDrone.Core.MediaFiles.TorrentInfo
{
    public interface ITorrentFileInfoReader
    {
        TorrentIdentity GetTorrentIdentity(byte[] fileContents);
    }

    public class TorrentFileInfoReader : ITorrentFileInfoReader
    {
        private readonly Logger _logger;

        public TorrentFileInfoReader(Logger logger)
        {
            _logger = logger;
        }

        public TorrentIdentity GetTorrentIdentity(byte[] fileContents)
        {
            try
            {
                return TorrentIdentity.ReadTorrent(fileContents);
            }
            catch
            {
                _logger.Trace("Unable to read a supported torrent identity from the file.");
                throw;
            }
        }
    }
}
