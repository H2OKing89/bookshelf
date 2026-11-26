using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Threading;
using System.Xml.Linq;
using NLog;
using NUnit.Framework;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Processes;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Datastore;
using RestSharp;

namespace NzbDrone.Test.Common
{
    public class NzbDroneRunner
    {
        private readonly IProcessProvider _processProvider;
        private readonly RestClient _restClient;
        private Process _nzbDroneProcess;
        private List<string> _startupLog;

        public string AppData { get; private set; }
        public string ApiKey { get; private set; }
        public PostgresOptions PostgresOptions { get; private set; }
        public int Port { get; private set; }

        public NzbDroneRunner(Logger logger, PostgresOptions postgresOptions, int port = 0)
        {
            _processProvider = new ProcessProvider(logger);
            Port = port == 0 ? GetAvailablePort() : port;
            _restClient = TestRestClient.Create($"http://127.0.0.1:{Port}/api/v1");

            PostgresOptions = postgresOptions;
        }

        private static int GetAvailablePort()
        {
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }

        public void Start(bool enableAuth = false)
        {
            AppData = Path.Combine(TestContext.CurrentContext.TestDirectory, "_intg_" + TestBase.GetUID());
            Directory.CreateDirectory(AppData);

            GenerateConfigFile(enableAuth);

            string readarrConsoleExe;
            if (OsInfo.IsWindows)
            {
                readarrConsoleExe = "Readarr.Console.exe";
            }
            else
            {
                readarrConsoleExe = "Readarr";
            }

            _startupLog = new List<string>();
            if (BuildInfo.IsDebug)
            {
                Start(Path.Combine(TestContext.CurrentContext.TestDirectory, "..", "..", "_output", "net10.0", readarrConsoleExe));
            }
            else
            {
                Start(Path.Combine(TestContext.CurrentContext.TestDirectory, "..", "bin", readarrConsoleExe));
            }

            var startupTimeout = Stopwatch.StartNew();
            while (startupTimeout.Elapsed < TimeSpan.FromMinutes(2))
            {
                _nzbDroneProcess.Refresh();

                if (_nzbDroneProcess.HasExited)
                {
                    TestContext.Progress.WriteLine("Readarr has exited unexpectedly");
                    Thread.Sleep(2000);
                    var output = _startupLog.Join(Environment.NewLine);
                    Assert.Fail($"Process has exited: ExitCode={_nzbDroneProcess.ExitCode} Output={output}");
                }

                var request = new RestRequest("system/status");
                request.AddHeader("Authorization", ApiKey);
                request.AddHeader("X-Api-Key", ApiKey);

                var statusCall = _restClient.Execute(request);

                if (statusCall.ResponseStatus == ResponseStatus.Completed && statusCall.StatusCode == HttpStatusCode.OK)
                {
                    // Refuse to send any subsequent test mutations to an unrelated listener.
                    using var status = JsonDocument.Parse(statusCall.Content);
                    var actualHome = status.RootElement.GetProperty("appData").GetString();
                    var comparison = OsInfo.IsWindows ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
                    if (!string.Equals(Path.GetFullPath(actualHome), Path.GetFullPath(AppData), comparison))
                    {
                        throw new InvalidOperationException("The status response belongs to a different application home.");
                    }

                    _startupLog = null;
                    TestContext.Progress.WriteLine($"Readarr {Port} is started. Running Tests");
                    return;
                }

                TestContext.Progress.WriteLine("Waiting for Readarr to start. Response Status : {0}  [{1}] {2}", statusCall.ResponseStatus, statusCall.StatusDescription, statusCall.ErrorException?.Message ?? statusCall.ErrorMessage);

                Thread.Sleep(500);
            }

            Assert.Fail("Timed out waiting for the owned Readarr test process to start.");
        }

        public void Kill()
        {
            try
            {
                if (_nzbDroneProcess != null && !_nzbDroneProcess.HasExited)
                {
                    _nzbDroneProcess.Kill(entireProcessTree: true);
                    if (!_nzbDroneProcess.WaitForExit(10000))
                    {
                        throw new InvalidOperationException("The owned test process did not exit.");
                    }
                }

                if (AppData != null)
                {
                    TestBase.DeleteTempFolder(AppData);
                }
            }
            finally
            {
                _nzbDroneProcess?.Dispose();
                _restClient.Dispose();
            }
        }

        private void Start(string outputNzbdroneConsoleExe)
        {
            StringDictionary envVars = new();
            if (PostgresOptions?.Host != null)
            {
                envVars.Add("Readarr__Postgres__Host", PostgresOptions.Host);
                envVars.Add("Readarr__Postgres__Port", PostgresOptions.Port.ToString());
                envVars.Add("Readarr__Postgres__User", PostgresOptions.User);
                envVars.Add("Readarr__Postgres__Password", PostgresOptions.Password);
                envVars.Add("Readarr__Postgres__MainDb", PostgresOptions.MainDb);
                envVars.Add("Readarr__Postgres__LogDb", PostgresOptions.LogDb);
                envVars.Add("Readarr__Postgres__CacheDb", PostgresOptions.CacheDb);

                TestContext.Progress.WriteLine("Using configured test PostgreSQL options.");
            }

            TestContext.Progress.WriteLine("Starting instance from {0} on port {1}", outputNzbdroneConsoleExe, Port);

            var args = "-nobrowser -nosingleinstancecheck -data=\"" + AppData + "\"";
            _nzbDroneProcess = _processProvider.Start(outputNzbdroneConsoleExe, args, envVars, OnOutputDataReceived, OnOutputDataReceived);
        }

        private void OnOutputDataReceived(string data)
        {
            TestContext.Progress.WriteLine($" [{Port}] > " + data);

            if (_startupLog != null)
            {
                _startupLog.Add(data);
            }

            if (data.Contains("Press enter to exit"))
            {
                _nzbDroneProcess.StandardInput.WriteLine(" ");
            }
        }

        private void GenerateConfigFile(bool enableAuth)
        {
            var configFile = Path.Combine(AppData, "config.xml");

            // Generate and set the api key so we don't have to poll the config file
            var apiKey = Guid.NewGuid().ToString().Replace("-", "");

            var xDoc = new XDocument(
                new XDeclaration("1.0", "utf-8", "yes"),
                new XElement(ConfigFileProvider.CONFIG_ELEMENT_NAME,
                             new XElement(nameof(ConfigFileProvider.ApiKey), apiKey),
                             new XElement(nameof(ConfigFileProvider.LogLevel), "trace"),
                             new XElement(nameof(ConfigFileProvider.AuthenticationMethod), enableAuth ? "Forms" : "None"),
                             new XElement(nameof(ConfigFileProvider.AuthenticationRequired), "DisabledForLocalAddresses"),
                             new XElement(nameof(ConfigFileProvider.BindAddress), "127.0.0.1"),
                             new XElement(nameof(ConfigFileProvider.Port), Port)));

            var data = xDoc.ToString();

            File.WriteAllText(configFile, data);

            ApiKey = apiKey;
        }
    }
}
