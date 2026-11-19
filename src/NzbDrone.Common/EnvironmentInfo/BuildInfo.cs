using System;
using System.IO;
using System.Linq;
using System.Reflection;

namespace NzbDrone.Common.EnvironmentInfo
{
    public static class BuildInfo
    {
        static BuildInfo()
        {
            var assembly = Assembly.GetExecutingAssembly();

            Version = assembly.GetName().Version;

            var attributes = assembly.GetCustomAttributes(true);

            Branch = "unknown";

            var config = attributes.OfType<AssemblyConfigurationAttribute>().FirstOrDefault();
            if (config != null)
            {
                Branch = config.Configuration;
            }

            Release = $"{Version}-{Branch}";
        }

        public static string AppName { get; } = "Readarr";
        public static string ProductId { get; } = "bookshelf-audiobooks";
        public static string SourceCommit => Metadata("BookshelfSourceCommit");
        public static string SourceTree => Metadata("BookshelfSourceTree");
        public static string SourceBranch => Metadata("BookshelfSourceBranch");
        public static bool BuildDirty => !string.Equals(Metadata("BookshelfBuildDirty"), "false", StringComparison.Ordinal);

        public static Version Version { get; }
        public static string Branch { get; }
        public static string Release { get; }

        public static DateTime BuildDateTime
        {
            get
            {
                var fileLocation = Assembly.GetCallingAssembly().Location;
                return new FileInfo(fileLocation).LastWriteTimeUtc;
            }
        }

        public static bool IsDebug
        {
            get
            {
#if DEBUG
                return true;
#else
                return false;
#endif
            }
        }

        private static string Metadata(string key)
        {
            return typeof(BuildInfo).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
                .FirstOrDefault(attribute => attribute.Key == key)?.Value ?? string.Empty;
        }
    }
}
