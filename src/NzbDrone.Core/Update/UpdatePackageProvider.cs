using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using NzbDrone.Common.Cloud;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Common.Http;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Update
{
    public interface IUpdatePackageProvider
    {
        UpdatePackage GetLatestUpdate(string branch, Version currentVersion);
        List<UpdatePackage> GetRecentUpdates(string branch, Version currentVersion, Version previousVersion = null);
    }

    public class UpdatePackageProvider : IUpdatePackageProvider
    {
        private readonly IHttpClient _httpClient;
        private readonly IHttpRequestBuilderFactory _requestBuilder;
        private readonly IPlatformInfo _platformInfo;
        private readonly IMainDatabase _mainDatabase;
        private readonly IBookshelfUpdatePolicy _updatePolicy;

        public UpdatePackageProvider(IHttpClient httpClient, IReadarrCloudRequestBuilder requestBuilder, IPlatformInfo platformInfo, IMainDatabase mainDatabase, IBookshelfUpdatePolicy updatePolicy)
        {
            _platformInfo = platformInfo;
            _requestBuilder = updatePolicy.IsEnabled ? requestBuilder.Services : null;
            _httpClient = httpClient;
            _mainDatabase = mainDatabase;
            _updatePolicy = updatePolicy;
        }

        public UpdatePackage GetLatestUpdate(string branch, Version currentVersion)
        {
            if (!_updatePolicy.IsEnabled)
            {
                return null;
            }

            _updatePolicy.EnsureEnabled();
            var request = _requestBuilder.Create()
                                         .Resource("/update/{branch}")
                                         .AddQueryParam("version", currentVersion)
                                         .AddQueryParam("os", OsInfo.Os.ToString().ToLowerInvariant())
                                         .AddQueryParam("arch", RuntimeInformation.OSArchitecture)
                                         .AddQueryParam("runtime", "netcore")
                                         .AddQueryParam("runtimeVer", _platformInfo.Version)
                                         .AddQueryParam("dbType", _mainDatabase.DatabaseType)
                                         .AddQueryParam("includeMajorVersion", true)
                                         .SetSegment("branch", branch);

            var update = _httpClient.Get<UpdatePackageAvailable>(request.Build()).Resource;

            if (!update.Available)
            {
                return null;
            }

            return update.UpdatePackage;
        }

        public List<UpdatePackage> GetRecentUpdates(string branch, Version currentVersion, Version previousVersion)
        {
            if (!_updatePolicy.IsEnabled)
            {
                return new List<UpdatePackage>();
            }

            _updatePolicy.EnsureEnabled();
            var request = _requestBuilder.Create()
                                         .Resource("/update/{branch}/changes")
                                         .AddQueryParam("version", currentVersion)
                                         .AddQueryParam("os", OsInfo.Os.ToString().ToLowerInvariant())
                                         .AddQueryParam("arch", RuntimeInformation.OSArchitecture)
                                         .AddQueryParam("runtime", "netcore")
                                         .AddQueryParam("runtimeVer", _platformInfo.Version)
                                         .SetSegment("branch", branch);

            if (previousVersion != null && previousVersion != currentVersion)
            {
                request.AddQueryParam("prevVersion", previousVersion);
            }

            var updates = _httpClient.Get<List<UpdatePackage>>(request.Build());

            return updates.Resource;
        }
    }
}
