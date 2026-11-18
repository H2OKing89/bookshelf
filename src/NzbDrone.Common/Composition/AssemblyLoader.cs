using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using NzbDrone.Common.EnvironmentInfo;

namespace NzbDrone.Common.Composition
{
    public class AssemblyLoader
    {
        static AssemblyLoader()
        {
            AppDomain.CurrentDomain.AssemblyResolve += new ResolveEventHandler(ContainerResolveEventHandler);
            RegisterSQLiteResolver();
        }

        public static IEnumerable<Assembly> Load(IEnumerable<string> assemblies)
        {
            var toLoad = assemblies.ToList();
            toLoad.Add("Readarr.Common");
            toLoad.Add(OsInfo.IsWindows ? "Readarr.Windows" : "Readarr.Mono");

            var startupPath = AppDomain.CurrentDomain.BaseDirectory;

            return toLoad.Select(x =>
                AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(startupPath, $"{x}.dll")));
        }

        private static Assembly ContainerResolveEventHandler(object sender, ResolveEventArgs args)
        {
            var resolver = new AssemblyDependencyResolver(args.RequestingAssembly.Location);
            var assemblyPath = resolver.ResolveAssemblyToPath(new AssemblyName(args.Name));

            if (assemblyPath == null)
            {
                return null;
            }

            return AssemblyLoadContext.Default.LoadFromAssemblyPath(assemblyPath);
        }

        public static void RegisterSQLiteResolver()
        {
            // The managed provider and the engine are separate packages. Resolve
            // both import names to our pinned native asset on every platform.
            var sqliteAssembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "System.Data.SQLite.dll"));

            try
            {
                NativeLibrary.SetDllImportResolver(sqliteAssembly, LoadSqliteNativeLib);
            }
            catch (InvalidOperationException)
            {
                // This can only be set once per assembly
                // Catch required for NzbDrone.Host tests
            }
        }

        private static IntPtr LoadSqliteNativeLib(string libraryName, Assembly assembly, DllImportSearchPath? dllImportSearchPath)
        {
            if (libraryName is "sqlite3" or "e_sqlite3")
            {
                var fileName = OsInfo.IsWindows ? "e_sqlite3.dll" : OsInfo.IsOsx ? "libe_sqlite3.dylib" : "libe_sqlite3.so";
                var directories = new[] { AppContext.BaseDirectory }
                    .Concat(((string)AppContext.GetData("NATIVE_DLL_SEARCH_DIRECTORIES") ?? string.Empty)
                        .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries));

                foreach (var directory in directories.Distinct())
                {
                    var path = Path.Combine(directory, fileName);
                    if (Path.IsPathFullyQualified(path) && File.Exists(path))
                    {
                        return NativeLibrary.Load(path);
                    }
                }

                // Never substitute an arbitrary host SQLite when delivery is incomplete.
                throw new DllNotFoundException($"The bundled SQLite engine ({fileName}) is missing.");
            }

            return IntPtr.Zero;
        }
    }
}
