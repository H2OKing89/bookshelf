using NzbDrone.Core.MetadataSource.Goodreads;

namespace NzbDrone.Core.MetadataSource
{
    public interface IProvideListInfo
    {
        ListResource GetListInfo(int foreignListId, int page, bool useCache = true);
    }
}
