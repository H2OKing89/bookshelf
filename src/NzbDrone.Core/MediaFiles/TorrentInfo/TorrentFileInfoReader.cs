using NLog;

namespace NzbDrone.Core.MediaFiles.TorrentInfo
{
    public interface ITorrentFileInfoReader
    {
        string GetHashFromTorrentFile(byte[] fileContents);
    }

    public class TorrentFileInfoReader : ITorrentFileInfoReader
    {
        private readonly Logger _logger;

        public TorrentFileInfoReader(Logger logger)
        {
            _logger = logger;
        }

        public string GetHashFromTorrentFile(byte[] fileContents)
        {
            try
            {
                return TorrentIdentity.FromTorrent(fileContents);
            }
            catch
            {
                _logger.Trace("Unable to read a supported torrent identity from the file.");
                throw;
            }
        }
    }
}
