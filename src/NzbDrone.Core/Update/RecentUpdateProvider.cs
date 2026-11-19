using System.Collections.Generic;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Update.History;

namespace NzbDrone.Core.Update
{
    public interface IRecentUpdateProvider
    {
        List<UpdatePackage> GetRecentUpdatePackages();
    }

    public class RecentUpdateProvider : IRecentUpdateProvider
    {
        private readonly IConfigFileProvider _configFileProvider;
        private readonly IUpdatePackageProvider _updatePackageProvider;
        private readonly IUpdateHistoryService _updateHistoryService;
        private readonly IBookshelfUpdatePolicy _updatePolicy;

        public RecentUpdateProvider(IConfigFileProvider configFileProvider,
                                    IUpdatePackageProvider updatePackageProvider,
                                    IUpdateHistoryService updateHistoryService,
                                    IBookshelfUpdatePolicy updatePolicy)
        {
            _configFileProvider = configFileProvider;
            _updatePackageProvider = updatePackageProvider;
            _updateHistoryService = updateHistoryService;
            _updatePolicy = updatePolicy;
        }

        public List<UpdatePackage> GetRecentUpdatePackages()
        {
            if (!_updatePolicy.IsEnabled)
            {
                return new List<UpdatePackage>();
            }

            _updatePolicy.EnsureEnabled();
            var branch = _configFileProvider.Branch;
            var version = BuildInfo.Version;
            var prevVersion = _updateHistoryService.PreviouslyInstalled();
            return _updatePackageProvider.GetRecentUpdates(branch, version, prevVersion);
        }
    }
}
