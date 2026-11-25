using System;

namespace NzbDrone.Common.Http
{
    public class HttpResponseSizeLimitException : Exception
    {
        public HttpResponseSizeLimitException(int limit)
            : base($"HTTP response exceeded the configured {limit} byte limit.")
        {
        }
    }
}
