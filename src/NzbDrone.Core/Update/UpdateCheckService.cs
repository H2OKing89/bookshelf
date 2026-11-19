using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.Configuration;

namespace NzbDrone.Core.Update
{
    public interface ICheckUpdateService
    {
        UpdatePackage AvailableUpdate();
    }

    public class CheckUpdateService : ICheckUpdateService
    {
        private readonly IUpdatePackageProvider _updatePackageProvider;
        private readonly IConfigFileProvider _configFileProvider;
        private readonly IBookshelfUpdatePolicy _updatePolicy;

        public CheckUpdateService(IUpdatePackageProvider updatePackageProvider,
                                  IConfigFileProvider configFileProvider,
                                  IBookshelfUpdatePolicy updatePolicy)
        {
            _updatePackageProvider = updatePackageProvider;
            _configFileProvider = configFileProvider;
            _updatePolicy = updatePolicy;
        }

        public UpdatePackage AvailableUpdate()
        {
            if (!_updatePolicy.IsEnabled)
            {
                return null;
            }

            _updatePolicy.EnsureEnabled();
            return _updatePackageProvider.GetLatestUpdate(_configFileProvider.Branch, BuildInfo.Version);
        }
    }
}
