using NLog;
using NzbDrone.Common.Cache;
using NzbDrone.Common.Disk;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.CustomFormats;
using NzbDrone.Core.Download;
using NzbDrone.Core.Download.History;
using NzbDrone.Core.Download.TrackedDownloads;
using NzbDrone.Core.History;
using NzbDrone.Core.MediaFiles.BookImport;
using NzbDrone.Core.MediaFiles.BookImport.Manual;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Parser;
using NzbDrone.Core.ThingiProvider.Events;

internal static class DownloadTrackingProbe
{
    internal static void Verify(string root)
    {
        var owned = Directory.CreateDirectory(Path.Combine(root, "download-tracking")).FullName;
        var sentinel = Path.Combine(owned, "retained-source.m4b");
        File.WriteAllText(sentinel, "Owned tracking preservation bytes");
        var services = new Dictionary<Type, object>();
        var success = 0;
        var failure = 0;
        var imports = 0;
        var removals = 0;
        var retained = 0;
        var checks = 0;
        var partial = false;
        List<TrackedDownload> visible = new();
        services[typeof(Logger)] = LogManager.GetLogger("OwnedTrackingProbe");
        services[typeof(ICacheManager)] = new CacheManager();
        services[typeof(IParsingService)] = Program.Fake<IParsingService>((method, _) => method.Name == "Map" ? null : Program.Unexpected(method));
        services[typeof(ICustomFormatCalculationService)] = Program.Fake<ICustomFormatCalculationService>((method, _) => Program.Unexpected(method));
        services[typeof(IHistoryService)] = Program.Fake<IHistoryService>((method, arguments) =>
        {
            if (method.Name == "FindByDownloadId")
            {
                Program.Check("published_tracking_history_is_client_scoped", arguments.Length == 2 && (int)arguments[1] > 0);
                return new List<EntityHistory>();
            }
            return Program.Unexpected(method);
        });
        services[typeof(IDownloadHistoryService)] = Program.Fake<IDownloadHistoryService>((method, arguments) =>
        {
            if (method.Name == "GetLatestDownloadHistoryItem")
            {
                Program.Check("published_tracking_state_history_is_client_scoped", arguments.Length == 2 && (int)arguments[1] > 0);
                return null;
            }
            if (method.Name == "RecordSourceRetention") { retained++; return null; }
            return Program.Unexpected(method);
        });
        services[typeof(IDownloadImportCompletionService)] = Program.Fake<IDownloadImportCompletionService>((method, _) =>
            method.Name == "ReadReceipt" ? new DownloadImportReceipt() : Program.Unexpected(method));
        services[typeof(IImportRetryService)] = new ImportRetryService(TimeProvider.System);
        services[typeof(IEventAggregator)] = Program.Fake<IEventAggregator>((method, arguments) =>
        {
            if (method.Name != "PublishEvent") return Program.Unexpected(method);
            if (arguments[0] is TrackedDownloadRefreshedEvent refreshed) visible = refreshed.TrackedDownloads;
            if (arguments[0] is DownloadCanBeRemovedEvent) removals++;
            return null;
        });
        services[typeof(IConfigService)] = Program.Fake<IConfigService>((method, _) =>
            method.Name == "get_EnableCompletedDownloadHandling" ? true : Program.Unexpected(method));
        services[typeof(IManageCommandQueue)] = Program.Fake<IManageCommandQueue>((method, _) => method.Name == "Push" ? null : Program.Unexpected(method));
        services[typeof(IDownloadClientStatusService)] = Program.Fake<IDownloadClientStatusService>((method, _) =>
        {
            if (method.Name == "RecordSuccess") { success++; return null; }
            if (method.Name == "RecordFailure") { failure++; return null; }
            return Program.Unexpected(method);
        });
        services[typeof(IFailedDownloadService)] = Program.Fake<IFailedDownloadService>((method, _) =>
            method.Name == "Check" ? null : Program.Unexpected(method));
        services[typeof(ICompletedDownloadService)] = Program.Fake<ICompletedDownloadService>((method, arguments) =>
        {
            if (method.Name == "Check")
            {
                checks++;
                if (partial && ((TrackedDownload)arguments[0]).DownloadItem.DownloadId == "broken")
                    throw new IOException("Owned per-item processing fixture");
                return null;
            }
            if (method.Name == "Import") { imports++; return null; }
            return Program.Unexpected(method);
        });
        var tracker = Program.Construct<TrackedDownloadService>(services);
        services[typeof(ITrackedDownloadService)] = tracker;
        var definition = new DownloadClientDefinition { Id = 1, Name = "Owned tracked client" };
        Func<IEnumerable<DownloadClientItem>> response = () => Array.Empty<DownloadClientItem>();
        var client = Program.Fake<IDownloadClient>((method, _) => method.Name switch
        {
            "get_Definition" => definition,
            "GetItems" => response(),
            _ => Program.Unexpected(method)
        });
        services[typeof(IDownloadClientFactory)] = Program.Fake<IDownloadClientFactory>((method, _) =>
            method.Name == "DownloadHandlingEnabled" ? new List<IDownloadClient> { client } : Program.Unexpected(method));
        var monitor = Program.Construct<DownloadMonitoringService>(services);
        var processor = Program.Construct<DownloadProcessingService>(services);

        TrackedDownload Track(int clientId, string id = "same-hash") => tracker.TrackDownload(
            new DownloadClientDefinition { Id = clientId }, Item(owned, clientId, id));
        var first = Track(1);
        first.KeepSourceFiles = true;
        first.HasNotifiedManualInteractionRequired = true;
        var other = Track(2);
        Program.Check("published_same_download_id_has_separate_client_cache_entries", tracker.GetTrackedDownloads().Count == 2 &&
            ReferenceEquals(tracker.Find("same-hash", 1), first) && ReferenceEquals(tracker.Find("same-hash", 2), other));
        Program.Check("published_client_cache_does_not_leak_copy_or_intervention_flags", !other.KeepSourceFiles && !other.HasNotifiedManualInteractionRequired);
        var rejected = false;
        try { tracker.Find("same-hash"); } catch (AmbiguousTrackedDownloadException) { rejected = true; }
        Program.Check("published_legacy_ambiguous_tracking_lookup_requires_client", rejected);

        // The real manual importer must resolve every identity before any source-retention or import work.
        var manualServices = typeof(ManualImportService).GetConstructors().Single().GetParameters()
            .ToDictionary(parameter => parameter.ParameterType, parameter => services.GetValueOrDefault(parameter.ParameterType));
        var manual = Program.Construct<ManualImportService>(manualServices);
        rejected = false;
        try
        {
            manual.Execute(new ManualImportCommand
            {
                ImportMode = ImportMode.Copy,
                Files = new List<ManualImportFile>
                {
                    new() { Path = sentinel, DownloadId = "same-hash", DownloadClientId = 1 },
                    new() { Path = sentinel, DownloadId = "same-hash" }
                }
            });
        }
        catch (AmbiguousTrackedDownloadException) { rejected = true; }
        Program.Check("published_manual_import_ambiguous_selection_aborts_before_retention", rejected && retained == 0 && imports == 0);

        first.State = TrackedDownloadState.Imported;
        first.DownloadItem.Status = DownloadItemStatus.Completed;
        first.DownloadItem.CanBeRemoved = true;
        first.IsClientAvailable = true;
        response = () => throw new IOException("Owned offline client fixture");
        monitor.Execute(new RefreshMonitoredDownloadsCommand());
        processor.Execute(new ProcessMonitoredDownloadsCommand());
        Program.Check("published_offline_poll_preserves_all_client_cache_entries", tracker.GetTrackedDownloads().Count == 2 && ReferenceEquals(tracker.Find("same-hash", 1), first));
        Program.Check("published_offline_imported_cache_does_not_authorize_cleanup", !first.IsClientAvailable && removals == 0 && imports == 0);
        Program.Check("published_offline_active_other_client_remains_visible_but_not_processable", visible.Count == 1 && ReferenceEquals(visible.Single(), other) && !other.IsTrackable && !other.IsClientAvailable);
        Program.Check("published_offline_poll_is_not_recorded_as_success", success == 0 && failure == 1);

        var beforeChecks = checks;
        response = LazyFailure;
        monitor.Execute(new RefreshMonitoredDownloadsCommand());
        Program.Check("published_lazy_response_failure_does_not_evict_or_track_partial_jobs", tracker.GetTrackedDownloads().Count == 2 && tracker.Find("new-item", 1) == null && checks == beforeChecks && success == 0 && failure == 2);

        partial = true;
        response = () => new[] { Item(owned, 1, "first-new"), Item(owned, 1, "broken") };
        monitor.Execute(new RefreshMonitoredDownloadsCommand());
        processor.Execute(new ProcessMonitoredDownloadsCommand());
        Program.Check("published_per_item_failure_preserves_missing_client_jobs", tracker.GetTrackedDownloads().Count == 4 && ReferenceEquals(tracker.Find("same-hash", 1), first));
        Program.Check("published_per_item_failure_leaves_every_job_unsafe_for_import_or_cleanup", tracker.GetTrackedDownloads().All(job => !job.IsTrackable && !job.IsClientAvailable) && imports == 0 && removals == 0);
        Program.Check("published_per_item_failure_is_not_a_successful_client_poll", success == 0 && failure == 3);

        partial = false;
        response = () => Array.Empty<DownloadClientItem>();
        monitor.Execute(new RefreshMonitoredDownloadsCommand());
        Program.Check("published_successful_empty_poll_removes_only_that_clients_vanished_jobs", tracker.GetTrackedDownloads().Count == 1 && ReferenceEquals(tracker.Find("same-hash", 2), other) && success == 1);

        var imported = Track(1, "fresh-imported");
        imported.State = TrackedDownloadState.Imported;
        imported.DownloadItem.Status = DownloadItemStatus.Completed;
        imported.DownloadItem.CanBeRemoved = true;
        response = () => new[] { imported.DownloadItem.Clone() };
        monitor.Execute(new RefreshMonitoredDownloadsCommand());
        Program.Check("published_successful_imported_poll_is_fresh_without_queue_visibility", imported.IsClientAvailable && !imported.IsTrackable && !visible.Contains(imported));
        processor.Execute(new ProcessMonitoredDownloadsCommand());
        Program.Check("published_only_fresh_imported_jobs_reach_receipt_guarded_cleanup", removals == 1 && imports == 0);

        tracker.Handle(new BookGrabbedEvent(null) { DownloadId = "fresh-imported", DownloadClientId = 1 });
        Program.Check("published_regrab_invalidates_only_the_scoped_cached_completion", tracker.Find("fresh-imported", 1) == null && ReferenceEquals(tracker.Find("same-hash", 2), other));
        tracker.Handle(new ProviderDeletedEvent<IDownloadClient>(2));
        Program.Check("published_deleted_client_retires_only_its_cached_jobs", tracker.GetTrackedDownloads().Count == 0);
        Program.Check("published_tracking_and_cleanup_proof_preserves_owned_source_bytes", File.ReadAllText(sentinel) == "Owned tracking preservation bytes");

        IEnumerable<DownloadClientItem> LazyFailure()
        {
            yield return Item(owned, 1, "new-item");
            throw new IOException("Owned lazy enumeration fixture");
        }
    }

    private static DownloadClientItem Item(string root, int clientId, string downloadId) => new()
    {
        DownloadId = downloadId,
        Title = "Owned Author - Owned Book",
        Category = "books",
        OutputPath = new OsPath(Path.Combine(root, $"client-{clientId}", downloadId)),
        Status = DownloadItemStatus.Downloading,
        DownloadClientInfo = new DownloadClientItemClientInfo { Id = clientId, Name = $"Owned client {clientId}" }
    };
}
