using NLog;
using NzbDrone.Common.Serializer;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Download;
using NzbDrone.Core.Download.History;
using NzbDrone.Core.Download.TrackedDownloads;
using NzbDrone.Core.History;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Notifications;
using NzbDrone.Core.Notifications.Webhook;

internal static class ImportCompleteWebhookProbe
{
    internal static void Verify(string root)
    {
        using var fixture = new Program.Scenario(root, "verified_import_webhook", new[] { 13 });
        var sent = new List<WebhookImportCompletePayload>();
        var deliveredAfterClaim = false;
        var downloadId = fixture.Tracked.DownloadItem.DownloadId;
        var logger = LogManager.GetLogger("OwnedImportCompleteWebhookProbe");
        var entityRepository = Program.Fake<IHistoryRepository>((method, args) => method.Name == "FindByDownloadId"
            ? fixture.Store.EntityRows((string)args[0])
            : Program.Unexpected(method));
        var history = new HistoryService(entityRepository, logger, fixture.DownloadRepository);
        var downloadHistory = new DownloadHistoryService(fixture.DownloadRepository, history);
        var config = Program.Fake<IConfigFileProvider>((method, _) => method.Name == "get_InstanceName"
            ? "Owned candidate"
            : Program.Unexpected(method));
        var status = Program.Fake<INotificationStatusService>((method, _) => method.Name == "RecordSuccess" ? null : Program.Unexpected(method));
        var settings = new WebhookSettings { Url = "https://owned.invalid/credential-path", Password = "owned-password" };
        var webhookProxy = Program.Fake<IWebhookProxy>((method, args) =>
        {
            if (method.Name != "SendWebhook") return Program.Unexpected(method);
            var payload = (WebhookImportCompletePayload)args[0];
            deliveredAfterClaim = fixture.Store.DownloadRows(downloadId).Any(row =>
                row.EventType == DownloadHistoryEventType.DownloadImportCompleteNotification && row.DownloadClientId == fixture.Tracked.DownloadClient &&
                row.Data.GetValueOrDefault(VerifiedImportWebhookService.ContextKey) == payload.EventId);
            sent.Add(payload);
            return null;
        });
        var webhook = new Webhook(webhookProxy, config)
        {
            Definition = new NotificationDefinition { Id = 42, Settings = settings }
        };
        var factory = Program.Fake<INotificationFactory>((method, _) => method.Name == "OnImportCompleteEnabled"
            ? settings.OnImportComplete ? new List<Webhook> { webhook } : new List<Webhook>()
            : Program.Unexpected(method));
        var files = new Dictionary<int, BookFile>();
        var fileService = Program.Fake<IMediaFileService>((method, args) => method.Name == "Get" && args[0] is int id
            ? files.GetValueOrDefault(id)
            : Program.Unexpected(method));

        VerifiedImportWebhookService Service() => new(factory, status, fixture.Completion, downloadHistory, fixture.DownloadRepository, history, fileService, config, logger);
        DownloadCompletedEvent CompletionEvent() => new(fixture.Tracked, 5);
        int Attempts() => fixture.Store.DownloadRows(downloadId).Count(row => row.EventType == DownloadHistoryEventType.DownloadImportCompleteNotification);

        var handler = Service();
        settings.OnImportComplete = true;
        var first = fixture.Add(13, 17, 1, 2, "first.m4b");
        files.Add(first.Id, first);
        fixture.Import();
        handler.Handle(CompletionEvent());
        Program.Check("published_import_complete_webhook_skips_incomplete_parts",
            fixture.ReadReceipt() == null && fixture.CompletedEvents == 0 && sent.Count == 0 && Attempts() == 0);

        fixture.Tracked.State = TrackedDownloadState.ImportPending;
        var second = fixture.Add(13, 17, 2, 2, "second.m4b");
        files.Add(second.Id, second);
        fixture.Store.FailCompletionWrites = true;
        fixture.Import(allowStorageFailure: true);
        handler.Handle(CompletionEvent());
        Program.Check("published_import_complete_webhook_skips_failed_receipt_write",
            fixture.Store.CompletionWriteAttempts > 0 && fixture.ReadReceipt() == null && fixture.CompletedEvents == 0 && sent.Count == 0 && Attempts() == 0);

        fixture.Tracked.State = TrackedDownloadState.ImportPending;
        fixture.Store.FailCompletionWrites = false;
        fixture.Import();
        Program.Check("published_import_complete_webhook_requires_live_verified_receipt",
            fixture.Tracked.State == TrackedDownloadState.Imported && fixture.ReadReceipt()?.Files.Count == 2 && fixture.CompletedEvents == 1);
        settings.OnImportComplete = false;
        handler.Handle(CompletionEvent());
        Program.Check("published_import_complete_webhook_respects_opt_in", sent.Count == 0 && Attempts() == 0);

        settings.OnImportComplete = true;
        handler.Handle(CompletionEvent());
        Program.Check("published_import_complete_webhook_claim_is_durable_before_delivery",
            sent.Count == 1 && deliveredAfterClaim && Attempts() == 1);
        var payload = sent.Single();
        var recording = payload.Recordings.Single();
        Program.Check("published_import_complete_webhook_contract_has_recording_and_exact_parts",
            payload.SchemaVersion == 1 && payload.EventType == WebhookEventType.ImportComplete && payload.EventId.Length == 64 &&
            payload.DownloadId == downloadId && payload.DownloadClientId == fixture.Tracked.DownloadClient &&
            recording.BookId == 13 && recording.EditionId == 17 && recording.RecordingId == "audible:us:B000000017" &&
            recording.AudibleRegion == "us" && recording.Parts.Select(part => part.Part).SequenceEqual(new[] { 1, 2 }) &&
            recording.Parts.All(part => part.PartCount == 2 && part.CustomFormatScores == null));
        var body = payload.ToJson();
        Program.Check("published_import_complete_webhook_serializes_only_base_names",
            recording.Parts.Select(part => part.SourceFileName).SequenceEqual(new[] { "first.m4b", "second.m4b" }) &&
            recording.Parts.Select(part => part.ImportedFileName).SequenceEqual(new[] { "first.m4b", "second.m4b" }) &&
            !body.Contains(root, StringComparison.Ordinal) && !body.Contains("credential-path", StringComparison.Ordinal) &&
            !body.Contains("owned-password", StringComparison.Ordinal));

        fixture.Tracked = fixture.Restart();
        Service().Handle(CompletionEvent());
        Program.Check("published_import_complete_webhook_restart_preserves_imported_state_and_skips_repeat",
            fixture.Tracked?.State == TrackedDownloadState.Imported && sent.Count == 1 && Attempts() == 1);
    }
}
