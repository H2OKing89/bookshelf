# Host TLS and lifecycle (P18/P19)

## TLS configuration

In **Settings → General → Host**, enable SSL, set an unused SSL port, and use an absolute path readable by Bookshelf:

| Certificate file | Separate private key path | Certificate password |
| --- | --- | --- |
| Existing PKCS#12 (`.pfx` or `.p12`) | Empty | Existing password, or empty for a passwordless file |
| PEM certificate chain | PEM RSA, EC or PKCS#8 key | Empty |
| PEM certificate chain | Encrypted PKCS#8 PEM key (`ENCRYPTED PRIVATE KEY`) | Key password |
| PEM certificate chain plus one private key in the same file | Empty | Key password only if encrypted |

Include the intermediate certificates in the certificate chain file (or PKCS#12 package). The matching leaf is selected by private key, so PEM certificate order is not significant. Traditional encrypted OpenSSL PEM keys using `Proc-Type` are not supported; use encrypted PKCS#8 instead. The app does not create, rewrite, renew, or install certificates or keys, and SSL startup does not fetch missing issuers. Supply certificates appropriate to your own trust chain, hostname, and expiration requirements.

HTTP remains available on its existing bind address/port. These settings do not modify the existing forwarded-header, trusted proxy, allowed-host, or URL-base policy. SSL is off by default. For a newly enabled SSL listener, an invalid/missing certificate, wrong key/password or unsupported format is rejected rather than silently starting without HTTPS. Legacy homes with only the old SSL certificate hash (no certificate file) retain their previous HTTP migration behavior.

The configuration API accepts `sslKeyPath` and `sslCertPassword`. Omitting either preserves the saved value; an explicit empty string clears it. The password is now returned as the `********` placeholder when configured, and saving that placeholder preserves the password. Validation and startup errors identify the setting to fix without echoing key/password data. An actual replacement password consisting of exactly the reserved placeholder must be configured directly in the private configuration rather than round-tripped through the settings UI. Spaces in real saved passwords are preserved. Environment configuration overrides continue to use the existing `Readarr__Server__SslCertPath`, `Readarr__Server__SslCertPassword`, and new `Readarr__Server__SslKeyPath` names.

## Why retain an external process restart?

Compared with the pinned Sonarr V5 reference (`c018578e4`, no comparisons with V4), Sonarr's `Bootstrap.RunHostUntilShutdown` / `RunWithRestartCheck` rebuild the host in the same process after a restart and reset global logging targets. That is not a safe mechanical transplant into Bookshelf. A review of the fork's service lifetimes identifies important state that is not wholly owned by a single host:

| Area reviewed | Finding and action |
| --- | --- |
| Host and app-home lock (`Host/Bootstrap.cs`, `Host/AppLifetime.cs`, `Host/AppHomeLock.cs`) | The former `ApplicationStopped` callback launched the replacement **before** host disposal, leaving a race for the app-home lock. The replacement now starts after `IHost.Run()` has disposed the host, NLog has shut down, and SQLite/Npgsql pools have been cleared. The bounded existing lock retry is retained for supervised/legacy handoffs. |
| Startup arguments (`Common/EnvironmentInfo/StartupContext.cs`, `Update/InstallUpdateService.cs`) | Parsing split at every `=` and stripped trailing slash/hyphen; preserved arguments were not quoted. Restart now uses the resolved absolute home and a single correctly quoted data argument. Updater argument construction uses the same quoting routine. The app does not enable an updater, change its policy, kill unrelated instances or switch homes during restart. |
| Domain shutdown (`Core/Lifecycle/LifecycleService.cs`, `Host/AppLifetime.cs`) | SIGTERM/console/SCM stopping previously bypassed the application's `ApplicationShutdownRequested` handlers. The host-stopping callback now publishes the same once-only notification as an internal restart/shutdown. SCM-initiated stop does not call SCM again. |
| Commands and native analysis (`Core/Messaging/Commands/CommandExecutor.cs`, `Core/MediaFiles/MediaInfo`) | Cancel command execution before host disposal and wait up to five seconds for cooperative workers to persist their final statuses. An active MediaInfo subprocess now receives command cancellation and is reaped by the runner; cancelling a refresh does not masquerade as a failed metadata read. Existing non-cooperative jobs are not guaranteed to finish inside the grace period; a warning identifies that case. |
| Scheduler (`Core/Jobs/Scheduler.cs`) | Static timer, static cancellation source, and accumulating `Elapsed` subscriptions are unsafe across in-process host rebuilds. Existing shutdown handling now runs on external stop too and tolerates stopping before startup completed. Retain the process boundary rather than introduce an unproven scheduler rewrite. |
| Logging, database and subscriptions (`Core/Instrumentation/DatabaseTarget.cs`, `Startup.cs`, `Core/Messaging/Events/EventAggregator.cs`, `Core/MediaFiles/RootFolderWatchingService.cs`) | Event subscriber caches, database log target configuration subscriptions, static schema/logging state, a console cancellation callback and timer/debouncer work would need an explicit disposal and re-registration audit for an in-process restart. Close logging and pools before a fresh replacement; the OS process boundary remains protection against stale callbacks and native state. |
| Startup updater handoff (`Core/Update/InstallUpdateService.cs`, `Update/UpdateEngine/StartNzbDrone.cs`) | The existing startup update listener is reachable from both the early bootstrap and later startup event. It is inert under Bookshelf's disabled update policy. Keep update isolation; validate handoff arguments synthetically, not by re-enabling/preparing a live update. If updater policy changes later, audit that duplicate startup event and native service handoff before release. |

## Acceptance

The local `hosttlslifecycle` packaged check creates its own private application home, certificate root/intermediate/leaf and passwords, tests loopback TLS handshakes with only the generated root trusted, preserves an actual baseline password-protected PFX home, and tests separate and combined PEM keys, encrypted keys, invalid saves, invalid startup, and unchanged certificate files. It restarts twice from an app home with spaces and `=`, confirms a distinct replacement process in the fixture-owned process group with the same HTTP/HTTPS ports and app home, sends SIGTERM only to that owned group, checks the shutdown handlers and SQLite integrity/marker, and starts the same home again. No production service or external endpoint is involved.

Unit regressions exercise actual SSL streams, configuration round-tripping, exactly-once shutdown dispatch, argument quoting, worker draining, cancellation of a generated native probe and mocked updater/SCM handoffs. The external-process design is retained intentionally. Native Windows service/TLS acceptance, real updater acceptance (disabled by Bookshelf policy), long-running non-cooperative job teardown, and any future **in-process** host rebuild are separate, unverified acceptance scopes; they must not be inferred from Linux loopback evidence.

## Opt-in performance diagnostics (P23)

In **Settings → General**, enable advanced settings and turn **Performance
profiling** on. Save and restart Bookshelf. Open **System → Status → Performance
diagnostics**, perform the library operation you want to measure, and select
**Refresh timings**. The display has separate tables for:

- Authenticated API requests, grouped by controller operation and HTTP method.
- Background jobs, grouped by the application's command type, category and
  actual manual/scheduled/unspecified trigger. This covers author and book
  refresh commands, folder scans, manual/downloaded import commands, download
  monitor and completion checks, and explicit/stale MediaInfo refresh jobs.
  Other independent command types are not automatically timed.

Both tables report completed executions, failures/cancellations, mean and
maximum duration, SQLite statement count and duration, and PostgreSQL provider
operation count and duration (which includes physical connection opens). These
are aggregates, not a per-request, per-file, per-job or per-query trace. The
background timer starts on the command worker when supported work executes,
not when an API request queues the command. Direct nested service work belongs
to its enclosing request/job scope; a single import does not also create a
separate MediaInfo job just because the importer calls the analyzer. A queued
or interrupted operation is not counted as a successfully completed job.

The setting is off by default and applies at host startup. The diagnostic API
(`GET /api/v1/system/performance`) requires the actual application API key,
including when other UI routes permit a local or external authentication mode.
Responses are non-cacheable and the diagnostics endpoint itself is not sampled.
The last ten minutes of samples, up to 256 completed requests **and jobs
combined**, are retained using monotonic time; expired samples are pruned on
read, on new samples, and once per minute while idle. The API retains its
HTTP-only `samples` and `operations` fields, adding `jobSamples` and `jobs`.
No SQL text, parameters, request URLs, bodies, command arguments or IDs, file
paths, credentials or per-user identifiers are retained by this profiler.
Turning the setting off and restarting clears all samples; the setting can
also be overridden with `Readarr__Server__PerformanceProfilingEnabled`.

Local acceptance exercises actual scheduler, command worker, import and bundled
MediaInfo work on disposable media and a large synthetic catalogue, with
provider-specific database timing tests and both desktop and phone views. The
view is diagnostic evidence, not a comprehensive distributed profiler or a
benchmark for every workload. Use the separate job table instead of inferring
background execution time from the API request that queued it.
