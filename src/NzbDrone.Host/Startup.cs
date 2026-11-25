using System;
using System.Collections.Generic;
using System.IO;
using DryIoc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HostFiltering;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NLog.Extensions.Logging;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Common.Instrumentation;
using NzbDrone.Common.Processes;
using NzbDrone.Common.Serializer;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Instrumentation;
using NzbDrone.Core.Lifecycle;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Host.AccessControl;
using NzbDrone.Host.Diagnostics;
using NzbDrone.Http.Authentication;
using NzbDrone.SignalR;
using Readarr.Api.V1.System;
using Readarr.Http;
using Readarr.Http.Authentication;
using Readarr.Http.ClientSchema;
using Readarr.Http.ErrorManagement;
using Readarr.Http.Frontend;
using Readarr.Http.Middleware;
using LogLevel = Microsoft.Extensions.Logging.LogLevel;

namespace NzbDrone.Host
{
    public class Startup
    {
        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        public IConfiguration Configuration { get; }

        public void ConfigureServices(IServiceCollection services)
        {
            services.AddLogging(b =>
            {
                b.ClearProviders();
                b.SetMinimumLevel(Microsoft.Extensions.Logging.LogLevel.Trace);
                b.AddFilter("Microsoft.AspNetCore", Microsoft.Extensions.Logging.LogLevel.Warning);
                b.AddFilter("Readarr.Http.Authentication", LogLevel.Information);
                b.AddFilter("Microsoft.AspNetCore.DataProtection.KeyManagement.XmlKeyManager", LogLevel.Error);
                b.AddNLog();
            });

            services.AddOptions<ForwardedHeadersOptions>()
                .Configure<IConfigFileProvider>(ForwardedHeadersConfigurator.Configure);

            services.AddOptions<HostFilteringOptions>()
                .Configure<IConfigFileProvider>(ConfigureHostFilteringOptions.Configure)
                .ValidateOnStart();

            services.AddRouting(options => options.LowercaseUrls = true);

            services.AddResponseCompression(options => options.EnableForHttps = true);

            services.AddCors(options =>
            {
                options.AddPolicy(VersionedApiControllerAttribute.API_CORS_POLICY,
                    builder =>
                    builder.AllowAnyOrigin()
                    .AllowAnyMethod()
                    .AllowAnyHeader());

                options.AddPolicy("AllowGet",
                    builder =>
                    builder.AllowAnyOrigin()
                    .WithMethods("GET", "OPTIONS")
                    .AllowAnyHeader());
            });

            services
            .AddControllers(options =>
            {
                options.ReturnHttpNotAcceptable = true;
            })
            .ConfigureApiBehaviorOptions(options =>
            {
                // DryIoc registers resource types too; API resources must still bind from JSON bodies.
                options.DisableImplicitFromServicesParameters = true;
            })
            .AddApplicationPart(typeof(SystemController).Assembly)
            .AddApplicationPart(typeof(StaticResourceController).Assembly)
            .AddJsonOptions(options =>
            {
                STJson.ApplySerializerSettings(options.JsonSerializerOptions);
            })
            .AddControllersAsServices();

            services.ConfigureHttpJsonOptions(options => STJson.ApplySerializerSettings(options.SerializerOptions));
            services.AddBookshelfApiDocuments();

            services
            .AddSignalR()
            .AddJsonProtocol(options =>
            {
                options.PayloadSerializerOptions = STJson.GetSerializerSettings();
            });

            services.AddDataProtection()
                .PersistKeysToFileSystem(new DirectoryInfo(Configuration["dataProtectionFolder"]));

            services.AddSingleton<IAuthorizationPolicyProvider, UiAuthorizationPolicyProvider>();
            services.AddSingleton<IAuthorizationHandler, UiAuthorizationHandler>();

            services.AddAuthorization(options =>
            {
                options.AddPolicy("SignalR", policy =>
                {
                    policy.AuthenticationSchemes.Add("SignalR");
                    policy.RequireAuthenticatedUser();
                });

                // Require auth on everything except those marked [AllowAnonymous]
                options.FallbackPolicy = new AuthorizationPolicyBuilder("API")
                .RequireAuthenticatedUser()
                .Build();
            });

            services.AddSingleton(TimeProvider.System);
            services.AddAuthorization(options => options.AddPolicy(PerformanceProfiler.AuthorizationPolicy, policy => policy
                .AddAuthenticationSchemes("API")
                .RequireAuthenticatedUser()
                .RequireClaim("ApiKey", "true")));
            services.AddAppAuthentication();
        }

        public void Configure(IApplicationBuilder app,
                              IContainer container,
                              Lazy<IMainDatabase> mainDatabaseFactory,
                              Lazy<ILogDatabase> logDatabaseFactory,
                              Lazy<ICacheDatabase> cacheDatabaseFactory,
                              DatabaseTarget dbTarget,
                              InitializeLogger initializeLogger,
                              ReconfigureLogging reconfigureLogging,
                              IAppFolderFactory appFolderFactory,
                              IProvidePidFile pidFileProvider,
                              IAppHomeLock appHomeLock,
                              IConfigFileProvider configFileProvider,
                              IRuntimeInfo runtimeInfo,
                              IFirewallAdapter firewallAdapter,
                              IEventAggregator eventAggregator,
                              ReadarrErrorPipeline errorHandler)
        {
            initializeLogger.Initialize();
            appFolderFactory.Register();

            configFileProvider.EnsureDefaultConfigFile();

            reconfigureLogging.Reconfigure();

            appHomeLock.Acquire();
            pidFileProvider.Write();

            // instantiate the databases to initialize/migrate them
            _ = mainDatabaseFactory.Value;
            var logDatabase = logDatabaseFactory.Value;
            _ = cacheDatabaseFactory.Value;

            if (logDatabase.IsEnabled)
            {
                dbTarget.Register();
            }

            SchemaBuilder.Initialize(container);

            if (OsInfo.IsNotWindows)
            {
                Console.CancelKeyPress += (sender, eventArgs) => NLog.LogManager.Configuration = null;
            }

            eventAggregator.PublishEvent(new ApplicationStartingEvent());

            if (OsInfo.IsWindows && runtimeInfo.IsAdmin)
            {
                firewallAdapter.MakeAccessible();
            }

            app.UseTrustedForwardedHeaders();
            app.UseHostFiltering();
            app.UseMiddleware<LoggingMiddleware>();
            app.UsePathBase(new PathString(configFileProvider.UrlBase));
            app.UseExceptionHandler(new ExceptionHandlerOptions
            {
                AllowStatusCode404Response = true,
                ExceptionHandler = errorHandler.HandleException
            });

            app.UseRouting();
            app.UseCors();
            app.UseAuthentication();
            app.UseAuthorization();
            app.UseMiddleware<PerformanceProfilingMiddleware>();
            app.UseResponseCompression();
            app.Properties["host.AppName"] = BuildInfo.AppName;

            app.UseMiddleware<VersionMiddleware>();
            app.UseMiddleware<UrlBaseMiddleware>(configFileProvider.UrlBase);
            app.UseMiddleware<StartingUpMiddleware>();
            app.UseMiddleware<CacheHeaderMiddleware>();
            app.UseMiddleware<IfModifiedMiddleware>();
            app.UseMiddleware<BufferingMiddleware>(new List<string> { "/api/v1/command" });

            app.UseWebSockets();

            app.UseEndpoints(x =>
            {
                x.MapHub<MessageHub>("/signalr/messages").RequireAuthorization("SignalR");
                x.MapControllers();
                x.MapBookshelfApiDocuments();
            });
        }
    }
}
