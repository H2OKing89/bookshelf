using System.Data.SQLite;
using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NLog;
using NzbDrone.Common.Cache;
using NzbDrone.Common.Disk;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.Books;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.CustomFormats;
using NzbDrone.Core.DecisionEngine;
using NzbDrone.Core.Download;
using NzbDrone.Core.Download.Clients;
using NzbDrone.Core.Download.History;
using NzbDrone.Core.Download.TrackedDownloads;
using NzbDrone.Core.History;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Languages;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.BookImport;
using NzbDrone.Core.MediaFiles.Events;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Notifications;
using NzbDrone.Core.Notifications.Webhook;
using NzbDrone.Core.Organizer;
using NzbDrone.Core.Parser;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Profiles;
using NzbDrone.Core.Profiles.Qualities;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.RootFolders;

internal static class Program
{
    private static readonly Dictionary<string, bool> Checks = new();
    private static readonly Dictionary<string, object> Metadata = new();
    private static string Phase = "start";

    private static int Main(string[] args)
    {
        var fixture = Path.GetFullPath(args.Length == 1 ? args[0] : throw new ArgumentException("Owned fixture directory required"));
        if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException("Native Linux acquisition fixture required");
        if (!Directory.Exists(fixture) || Directory.EnumerateFileSystemEntries(fixture).Any(path =>
            Path.GetFileName(path) != "tmp" || !Directory.Exists(path) || Directory.EnumerateFileSystemEntries(path).Any()))
            throw new InvalidOperationException("A fresh owned fixture directory is required");
        File.SetUnixFileMode(fixture, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        foreach (var name in new[] { "No_Expand", "No_SQLiteXmlConfigFile", "No_PreLoadSQLite", "No_SQLiteFunctions" })
            Environment.SetEnvironmentVariable(name, "true");
        LogManager.GlobalThreshold = LogLevel.Off;
        try
        {
            NzbDrone.Common.Composition.AssemblyLoader.RegisterSQLiteResolver();
            var published = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
            Check("published_completion_code_loaded_from_delivery", Path.GetDirectoryName(typeof(DownloadImportCompletionService).Assembly.Location) == published);
            Check("published_common_code_loaded_from_delivery", Path.GetDirectoryName(typeof(BuildInfo).Assembly.Location) == published);
            Metadata["runtimeVersion"] = Environment.Version.ToString();
            Metadata["productId"] = BuildInfo.ProductId;
            Metadata["sourceCommit"] = BuildInfo.SourceCommit;
            Metadata["sourceTree"] = BuildInfo.SourceTree;
            Metadata["sourceBranch"] = BuildInfo.SourceBranch;
            Metadata["buildDirty"] = BuildInfo.BuildDirty;
            Metadata["version"] = BuildInfo.Version.ToString();
            Metadata["storageBoundary"] = "published services with owned SQLite-backed repository interfaces";
            Metadata["importerBoundary"] = "controlled committed-file results and published history writers";
            Metadata["sourceRetirementBoundary"] = "published source helper with owned physical filesystem operations";
            Metadata["downloadClientNetworkUsed"] = false;
            VerifyClientTags();
            VerifyCustomFormatUpgrades(fixture);
            VerifyRecordingLanguage();
            foreach (var name in new[] { "two_books_one_recording", "two_books_one_ebook", "declared_missing_part", "duplicate_parts", "different_editions", "unrepresented_source", "manual_subset", "rejected_part" })
                VerifyIncomplete(fixture, name);
            VerifyWriteFailure(fixture);
            VerifyCompleteAndRestart(fixture);
            VerifyEbookFormats(fixture);
            VerifyKeptSourceReceipt(fixture);
            VerifyNonmovableSource(fixture);
            VerifyPendingCopyRetention(fixture);
            VerifyRetirementRetry(fixture);
            VerifyResumedReceipt(fixture);
            VerifyLegacyHistory(fixture);
            VerifyStaleReceipt(fixture);
            VerifyForgedImportedState(fixture);
            VerifyFileDiagnostics(fixture);
            VerifyBlockedRecovery(fixture);
            VerifyManualIntervention(fixture);
            DownloadFailoverProbe.Verify(fixture);
            GrabSpaceProbe.Verify(fixture);
            DownloadTrackingProbe.Verify(fixture);
            Check("published_probe_uses_only_declared_boundaries", BoundaryProxy.Unexpected.Count == 0);
        }
        catch (Exception error)
        {
            Checks["published_acquisition_probe_completed"] = false;
            Metadata["failurePhase"] = Phase;
            Metadata["failureType"] = error.GetType().Name;
            if (error is ProbeFailure failure) Metadata["failureCheck"] = failure.Code;
            Metadata["unexpectedBoundaryMethods"] = BoundaryProxy.Unexpected.ToArray();
            var diagnostic = Path.Combine(fixture, "owned-probe-diagnostic.txt");
            File.WriteAllText(diagnostic, error.ToString());
            File.SetUnixFileMode(diagnostic, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        finally
        {
            var report = Path.Combine(fixture, "acquisition-probe-result.json");
            File.WriteAllText(report, JsonSerializer.Serialize(new { checks = Checks, metadata = Metadata }));
            File.SetUnixFileMode(report, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            SQLiteConnection.ClearAllPools();
        }
        return Checks.Values.All(value => value) ? 0 : 1;
    }

    internal static void Check(string name, bool condition)
    {
        Checks[name] = condition;
        if (!condition) throw new ProbeFailure(name);
    }

    internal static T Fake<T>(Func<MethodInfo, object[], object> handler) where T : class
    {
        var proxy = DispatchProxy.Create<T, BoundaryProxy>();
        ((BoundaryProxy)(object)proxy).Handler = handler;
        return proxy;
    }

    internal static T Construct<T>(Dictionary<Type, object> services)
    {
        var constructor = typeof(T).GetConstructors().Single();
        return (T)constructor.Invoke(constructor.GetParameters().Select(parameter => services.TryGetValue(parameter.ParameterType, out var service)
            ? service : throw new InvalidOperationException("Missing declared constructor boundary: " + parameter.ParameterType.FullName)).ToArray());
    }

    internal static object Unexpected(MethodInfo method)
    {
        var name = method.DeclaringType.FullName + "." + method.Name;
        BoundaryProxy.Unexpected.Add(name);
        throw new InvalidOperationException("Unexpected declared boundary call: " + name);
    }

    private static void VerifyClientTags()
    {
        Phase = "client_tag_isolation";
        foreach (var protocol in new[] { DownloadProtocol.Usenet, DownloadProtocol.Torrent })
        {
            var prefix = "published_" + protocol.ToString().ToLowerInvariant();
            var tagged = FakeClient(new DownloadClientDefinition { Id = 1, Name = "Owned tagged client", Protocol = protocol, Tags = new HashSet<int> { 1 }, Priority = 0 });
            var untagged = FakeClient(new DownloadClientDefinition { Id = 2, Name = "Owned default client", Protocol = protocol, Priority = 10 });
            var available = new List<IDownloadClient> { tagged, untagged };
            var factory = Fake<IDownloadClientFactory>((method, _) => method.Name == "GetAvailableProviders" ? available : Unexpected(method));
            var blocked = Fake<IDownloadClientStatusService>((method, _) => method.Name == "GetBlockedProviders" ? new List<DownloadClientStatus>() : Unexpected(method));
            var indexers = Fake<IIndexerFactory>((method, _) => method.Name == "Find" ? new IndexerDefinition { Id = 1, Name = "Owned indexer", DownloadClientId = 1 } : Unexpected(method));
            var provider = new DownloadClientProvider(blocked, factory, indexers, new CacheManager(), LogManager.GetLogger("OwnedAcquisitionProbe"));
            Check(prefix + "_null_author_tags_cannot_select_reserved_client", ReferenceEquals(provider.GetDownloadClient(protocol), untagged));
            Check(prefix + "_empty_author_tags_cannot_select_reserved_client", ReferenceEquals(provider.GetDownloadClient(protocol, tags: new HashSet<int>()), untagged));
            Check(prefix + "_matching_tags_keep_precedence", ReferenceEquals(provider.GetDownloadClient(protocol, tags: new HashSet<int> { 1 }), tagged));
            var rejected = false;
            try { provider.GetDownloadClient(protocol, 1); } catch (DownloadClientUnavailableException) { rejected = true; }
            Check(prefix + "_indexer_override_cannot_escape_tag_policy", rejected);
            available.Remove(untagged);
            rejected = false;
            try { provider.GetDownloadClient(protocol); } catch (DownloadClientUnavailableException) { rejected = true; }
            Check(prefix + "_no_eligible_client_is_explicit", rejected);
        }
    }

    private static void VerifyCustomFormatUpgrades(string root)
    {
        Phase = "custom_format_upgrade_guard";
        var folder = Directory.CreateDirectory(Path.Combine(root, "custom-format-guard")).FullName;
        var originalPath = Path.Combine(folder, "incoming.mp3");
        var existingPath = Path.Combine(folder, "existing-Preferred.mp3");
        File.WriteAllText(originalPath, "Owned incoming bytes");
        File.WriteAllText(existingPath, "Owned existing bytes");
        var before = new[] { originalPath, existingPath }.ToDictionary(path => path, path => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))));
        var preferred = new CustomFormat("Preferred filename", new ReleaseTitleSpecification { Value = "Preferred" }) { Id = 100 };
        var formats = Fake<ICustomFormatService>((method, _) => method.Name == "All" ? new List<CustomFormat> { preferred } : Unexpected(method));
        var calculator = new CustomFormatCalculationService(formats, LogManager.GetLogger("OwnedFormatProbe"));
        var intended = "Book";
        var names = Fake<IBuildFileNames>((method, _) => method.Name == "BuildBookFileName" ? intended : Unexpected(method));
        var history = Fake<IHistoryService>((method, _) => method.Name == "FindByDownloadId" ? new List<EntityHistory>() : Unexpected(method));
        var preview = new LocalBookCustomFormatCalculationService(names, calculator, history);
        var config = Fake<IConfigService>((method, _) => method.Name == "get_DownloadPropersAndRepacks" ? ProperDownloadTypes.PreferAndUpgrade : Unexpected(method));
        var profile = new QualityProfile
        {
            Items = new List<QualityProfileQualityItem>
            {
                new() { Quality = Quality.MP3, Allowed = true },
                new() { Quality = Quality.FLAC, Allowed = true }
            },
            FormatItems = new List<ProfileFormatItem> { new() { Format = preferred, Score = 100 } }
        };
        var author = new Author { Id = 81, Name = "Owned format author", QualityProfile = profile };
        var existing = new BookFile { Id = 82, EditionId = 83, Part = 1, Path = existingPath, Quality = new QualityModel(Quality.MP3), Author = author };
        var item = new LocalBook
        {
            Path = originalPath, Quality = new QualityModel(Quality.MP3), Author = author,
            Edition = new Edition { Id = 83, BookId = 84, ForeignEditionId = "audible:us:B000000083", Asin = "B000000083", AudibleRegion = "us" },
            Book = new Book { Id = 84, BookFiles = new List<BookFile> { existing } },
            FileTrackInfo = new ParsedTrackInfo(), Part = 1, PartCount = 1
        };
        var spec = new NzbDrone.Core.MediaFiles.BookImport.Specifications.UpgradeSpecification(config, calculator, preview, LogManager.GetLogger("OwnedFormatProbe"));
        var decision = spec.IsSatisfiedBy(item, null);
        Check("published_same_quality_custom_format_downgrade_has_stable_code", !decision.Accepted && decision.Code == RejectionCode.CustomFormatDowngrade);
        intended = "Book-Preferred";
        Check("published_same_quality_preserved_custom_format_is_accepted", spec.IsSatisfiedBy(item, null).Accepted);
        item.Path = existingPath;
        intended = "Book";
        decision = spec.IsSatisfiedBy(item, null);
        Check("published_filename_preference_lost_after_rename_has_specific_code", !decision.Accepted && decision.Code == RejectionCode.CustomFormatLostAfterRename);
        item.Path = originalPath;
        var client = new DownloadClientItem { Title = "Brandon Sanderson - Mistborn Preferred (2025)", DownloadId = "owned-cf" };
        Check("published_preserved_scene_metadata_keeps_its_format_after_rename", spec.IsSatisfiedBy(item, client).Accepted);
        item.Edition.Id = 85;
        Check("published_other_recording_is_not_a_format_replacement", spec.IsSatisfiedBy(item, null).Accepted);
        item.Edition.Id = 83;
        item.Quality = new QualityModel(Quality.FLAC);
        Check("published_quality_upgrade_retains_precedence_over_format_score", spec.IsSatisfiedBy(item, null).Accepted);
        Check("published_format_preview_preserves_recording_identity", item.Edition.Asin == "B000000083" && item.Edition.ForeignEditionId == "audible:us:B000000083" && item.Edition.AudibleRegion == "us");
        Check("published_format_decisions_leave_source_and_library_bytes_unchanged", before.All(pair => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(pair.Key))) == pair.Value));

        var improved = new CustomFormat("Improved filename", new ReleaseTitleSpecification { Value = "Improved" }) { Id = 101 };
        var comparison = new NzbDrone.Core.DecisionEngine.Specifications.UpgradableSpecification(config, LogManager.GetLogger("OwnedFormatProbe"));
        var quality = new QualityModel(Quality.MP3);
        profile.MinUpgradeFormatScore = 10;
        profile.FormatItems.Add(new ProfileFormatItem { Format = improved, Score = 109 });
        Check("published_profile_rejects_nine_point_upgrade", !comparison.IsUpgradable(profile, quality, new() { preferred }, quality, new() { improved }, out var below) && below);
        profile.FormatItems.Last().Score = 110;
        Check("published_profile_accepts_exact_ten_point_upgrade", comparison.IsUpgradable(profile, quality, new() { preferred }, quality, new() { improved }));
        profile.MinUpgradeFormatScore = 0;
        profile.FormatItems.Last().Score = 101;
        Check("published_zero_threshold_retains_small_positive_upgrade", comparison.IsUpgradable(profile, quality, new() { preferred }, quality, new() { improved }));
    }

    private static void VerifyRecordingLanguage()
    {
        Phase = "recording_language_formats";
        var languages = RecordingLanguageEvidence.Parse("English, fra", " en-US ", "unrecognized");
        Check("published_recording_language_parser_retains_known_and_unknown_values", languages.Select(language => language.Id).Order().SequenceEqual(new[] { 0, 1, 2 }));
        var input = new CustomFormatInput { Languages = languages, SelectedEditionLanguageId = Language.French.Id };
        Check("published_bilingual_recording_matches_both_languages", new LanguageSpecification { Value = Language.English.Id }.IsSatisfiedBy(input) &&
            new LanguageSpecification { Value = Language.French.Id }.IsSatisfiedBy(input));
        Check("published_language_can_match_selected_edition", new LanguageSpecification { Value = LanguageSpecification.SelectedEdition }.IsSatisfiedBy(input));
        Check("published_partial_unknown_cannot_satisfy_negated_language", !new LanguageSpecification { Value = Language.Spanish.Id, Negate = true }.IsSatisfiedBy(input));
        input.Languages = new[] { Language.Unknown };
        Check("published_unknown_requires_explicit_unknown_condition", new LanguageSpecification { Value = Language.Unknown.Id }.IsSatisfiedBy(input) &&
            !new LanguageSpecification { Value = Language.English.Id, ExceptLanguage = true }.IsSatisfiedBy(input));
    }

    private static IDownloadClient FakeClient(DownloadClientDefinition definition)
    {
        return Fake<IDownloadClient>((method, _) => method.Name switch
        {
            "get_Definition" => definition,
            "get_Protocol" => definition.Protocol,
            "get_Name" => definition.Name,
            _ => Unexpected(method)
        });
    }

    private static void VerifyIncomplete(string root, string name)
    {
        Phase = name;
        using var fixture = new Scenario(root, name, name is "two_books_one_recording" or "two_books_one_ebook" or "manual_subset" ? new[] { 13, 14 } : new[] { 13 });
        switch (name)
        {
            case "two_books_one_recording":
                fixture.Add(13, 17, 1, 2, "recording-part-1.m4b");
                fixture.Add(13, 17, 2, 2, "recording-part-2.m4b");
                break;
            case "two_books_one_ebook":
                fixture.Add(13, 17, 0, 0, "owned-book.epub");
                fixture.Add(13, 17, 0, 0, "owned-book.pdf");
                break;
            case "declared_missing_part":
                fixture.Add(13, 17, 1, 2, "recording-part-1.m4b");
                break;
            case "duplicate_parts":
                fixture.Add(13, 17, 1, 2, "duplicate-one.m4b");
                fixture.Add(13, 17, 1, 2, "duplicate-two.m4b");
                break;
            case "different_editions":
                fixture.Add(13, 17, 1, 2, "edition-one.m4b");
                fixture.Add(13, 18, 2, 2, "edition-two.m4b");
                break;
            case "unrepresented_source":
                fixture.Add(13, 17, 0, 1, "represented.m4b");
                fixture.AddUnrepresented("unrepresented-recording.m4b");
                break;
            case "manual_subset":
                fixture.Add(13, 17, 0, 1, "selected-recording.m4b");
                break;
            case "rejected_part":
                fixture.Add(13, 17, 1, 2, "successful-part.m4b");
                fixture.AddRejected("rejected-part.m4b", 13, 2, 2);
                break;
        }
        var hashes = fixture.SourceHashes();
        fixture.Import();
        fixture.AssertRetained(name, hashes);
    }

    private static void VerifyWriteFailure(string root)
    {
        Phase = "receipt_write_failure";
        using var fixture = new Scenario(root, Phase, new[] { 13 });
        fixture.Add(13, 17, 1, 2, "first.m4b");
        fixture.Add(13, 17, 2, 2, "second.m4b");
        var hashes = fixture.SourceHashes();
        fixture.Store.FailCompletionWrites = true;
        fixture.Import(allowStorageFailure: true);
        Check("receipt_write_failure_reaches_the_durable_store_boundary", fixture.Store.CompletionWriteAttempts > 0);
        fixture.AssertRetained(Phase, hashes);
        Check("receipt_write_failure_has_no_completed_receipt", fixture.Store.DownloadRows(fixture.Tracked.DownloadItem.DownloadId).All(row => row.EventType != DownloadHistoryEventType.DownloadImported));
    }

    private static void VerifyCompleteAndRestart(string root)
    {
        Phase = "complete_recording";
        using var fixture = new Scenario(root, Phase, new[] { 13 });
        fixture.Add(13, 17, 1, 2, "first.m4b");
        fixture.Add(13, 17, 2, 2, "second.m4b");
        fixture.AddUnrepresented("owned-sidecar.nfo");
        var sidecar = fixture.SourceHashes().Single(pair => pair.Key.EndsWith(".nfo", StringComparison.Ordinal));
        var library = fixture.LibraryHashes();
        fixture.Import();
        Check("complete_recording_is_imported", fixture.Tracked.State == TrackedDownloadState.Imported);
        Check("complete_recording_records_before_event_and_client_calls", fixture.OrderValid && fixture.CompletedEvents == 1 && fixture.Marked == 1 && fixture.Removed == 1);
        Check("complete_recording_has_live_source_bound_receipt", fixture.ReadReceipt() is { Files.Count: 2, BookIds.Count: 1 });
        Check("complete_recording_writes_one_durable_receipt", fixture.Store.DownloadRows(fixture.Tracked.DownloadItem.DownloadId).Count(row => row.EventType == DownloadHistoryEventType.DownloadImported) == 1);
        Check("complete_recording_preserves_library_bytes", fixture.LibraryHashes().OrderBy(pair => pair.Key).SequenceEqual(library.OrderBy(pair => pair.Key)));
        Check("complete_recording_retirement_follows_persistence", fixture.Retired >= 1 && fixture.OrderValid);
        Check("complete_recording_source_helper_preserves_sidecars", fixture.SourceHashes().TryGetValue(sidecar.Key, out var sidecarHash) && sidecarHash == sidecar.Value);
        fixture.ResetActions();
        var restarted = fixture.Restart();
        Check("verified_receipt_reconstructs_imported_state", restarted != null && restarted.State == TrackedDownloadState.Imported);
        fixture.Tracked = restarted;
        fixture.Process();
        fixture.Process();
        Check("verified_restart_does_not_publish_duplicate_completion", fixture.CompletedEvents == 0 && fixture.Marked == 0);
        Check("verified_restart_cleanup_is_idempotent", fixture.Removed == 1 && fixture.OrderValid);
    }

    private static void VerifyLegacyHistory(string root)
    {
        foreach (var mode in new[] { "grab_only", "unrelated_history", "legacy_completion_without_receipt" })
        {
            Phase = mode;
            using var fixture = new Scenario(root, mode, new[] { 13 });
            fixture.AddRejected("owned-recording.m4b", 13, 0, 1);
            if (mode == "unrelated_history")
                fixture.Store.Insert(new EntityHistory { BookId = 13, AuthorId = 5, DownloadId = "unrelated-download", EventType = EntityHistoryEventType.BookFileImported, Date = DateTime.UtcNow, SourceTitle = "Owned unrelated history" });
            if (mode == "legacy_completion_without_receipt")
                fixture.Store.Insert(new DownloadHistory { DownloadId = fixture.Tracked.DownloadItem.DownloadId, DownloadClientId = 11, AuthorId = 5,
                    EventType = DownloadHistoryEventType.DownloadImported, Date = DateTime.UtcNow.AddMinutes(1), SourceTitle = "Owned legacy marker" });
            var hashes = fixture.SourceHashes();
            var restored = fixture.Restart();
            Check(mode + "_cannot_reconstruct_imported_state", restored != null && restored.State != TrackedDownloadState.Imported);
            fixture.Tracked = restored;
            fixture.Process();
            fixture.AssertRetained(mode, hashes);
        }
    }

    private static void VerifyKeptSourceReceipt(string root)
    {
        Phase = "kept_source_receipt";
        using var fixture = new Scenario(root, Phase, new[] { 13 });
        fixture.Add(13, 17, 0, 1, "owned-copy-recording.m4b");
        fixture.Tracked.KeepSourceFiles = true;
        fixture.Tracked.DownloadItem.CanMoveFiles = false;
        var hashes = fixture.SourceHashes();
        fixture.Import();
        Check("kept_source_recording_completes_without_source_deletion", fixture.Tracked.State == TrackedDownloadState.Imported && fixture.SourceHashes().OrderBy(pair => pair.Key).SequenceEqual(hashes.OrderBy(pair => pair.Key)));
        Check("kept_source_receipt_prevents_client_data_deletion", fixture.Removed == 1 && fixture.DeleteDataFlags.SequenceEqual(new[] { false }) && fixture.ReadReceipt()?.KeepSourceFiles == true);
        fixture.Tracked.DownloadItem.CanMoveFiles = true;
        fixture.WriteIncompleteMarker();
        fixture.ResetActions();
        fixture.Tracked = fixture.Restart();
        Check("incomplete_marker_does_not_recreate_imported_state", fixture.Tracked != null && fixture.Tracked.State != TrackedDownloadState.Imported);
        fixture.Tracked.KeepSourceFiles = false;
        fixture.Tracked.State = TrackedDownloadState.ImportPending;
        fixture.Import();
        Check("persisted_copy_choice_survives_newer_incomplete_marker", fixture.Tracked.State == TrackedDownloadState.Imported && fixture.ReadReceipt()?.KeepSourceFiles == true && fixture.DeleteDataFlags.SequenceEqual(new[] { false }));
        Check("incomplete_retry_cannot_delete_kept_sources", fixture.SourceHashes().OrderBy(pair => pair.Key).SequenceEqual(hashes.OrderBy(pair => pair.Key)));
        fixture.ResetActions();
        fixture.Tracked = fixture.Restart();
        Check("kept_source_choice_survives_restart_and_client_mode_change", fixture.Tracked?.State == TrackedDownloadState.Imported && fixture.Tracked.KeepSourceFiles);
        fixture.Process();
        Check("kept_source_restart_never_requests_data_deletion", fixture.Removed == 1 && fixture.DeleteDataFlags.SequenceEqual(new[] { false }) && fixture.SourceHashes().OrderBy(pair => pair.Key).SequenceEqual(hashes.OrderBy(pair => pair.Key)));
    }

    private static void VerifyEbookFormats(string root)
    {
        Phase = "ebook_format_variants";
        using var fixture = new Scenario(root, Phase, new[] { 13 });
        fixture.Add(13, 17, 0, 0, "owned-book.epub");
        fixture.Add(13, 17, 0, 0, "owned-book.pdf");
        var library = fixture.LibraryHashes();
        fixture.Import();
        var receipt = fixture.ReadReceipt();
        Check("ebook_formats_complete_as_one_distinct_book", fixture.Tracked.State == TrackedDownloadState.Imported && receipt?.BookIds.SequenceEqual(new[] { 13 }) == true && receipt.Files.Count == 2);
        Check("ebook_format_receipt_precedes_event_and_cleanup", fixture.CompletedEvents == 1 && fixture.Marked == 1 && fixture.Removed == 1 && fixture.OrderValid);
        Check("ebook_format_library_bytes_remain_intact", fixture.LibraryHashes().OrderBy(pair => pair.Key).SequenceEqual(library.OrderBy(pair => pair.Key)));
    }

    private static void VerifyPendingCopyRetention(string root)
    {
        Phase = "pending_manual_copy";
        using var fixture = new Scenario(root, Phase, new[] { 13 });
        fixture.RecordSourceRetention();
        fixture.Add(13, 17, 1, 2, "selected-first.m4b");
        var initial = fixture.SourceHashes();
        fixture.Import();
        fixture.AssertRetained(Phase, initial);
        fixture.WriteIncompleteMarker();
        fixture.ResetActions();
        fixture.Tracked = fixture.Restart();
        Check("pending_copy_intent_survives_restart_without_completion_receipt", fixture.Tracked != null && fixture.KeepsSources() && fixture.ReadReceipt() == null);
        fixture.Tracked.KeepSourceFiles = false;
        fixture.Tracked.DownloadItem.CanMoveFiles = true;
        fixture.Tracked.State = TrackedDownloadState.ImportPending;
        fixture.Add(13, 17, 2, 2, "later-second.m4b");
        var complete = fixture.SourceHashes();
        fixture.Import();
        Check("pending_copy_later_completion_preserves_every_source", fixture.Tracked.State == TrackedDownloadState.Imported && fixture.SourceHashes().OrderBy(pair => pair.Key).SequenceEqual(complete.OrderBy(pair => pair.Key)));
        Check("pending_copy_later_receipt_and_client_keep_data", fixture.ReadReceipt()?.KeepSourceFiles == true && fixture.Removed == 1 && fixture.DeleteDataFlags.SequenceEqual(new[] { false }) && fixture.OrderValid);
        Check("pending_copy_retention_is_durable_and_not_a_completion", fixture.Store.DownloadRows(fixture.Tracked.DownloadItem.DownloadId).Count(row => row.EventType == DownloadHistoryEventType.DownloadSourceRetained) == 1);
    }

    private static void VerifyNonmovableSource(string root)
    {
        Phase = "nonmovable_source";
        using var fixture = new Scenario(root, Phase, new[] { 13 });
        fixture.Add(13, 17, 0, 1, "owned-recording.m4b");
        fixture.Tracked.DownloadItem.CanMoveFiles = false;
        var hashes = fixture.SourceHashes();
        fixture.Import();
        Check("nonmovable_source_completes_without_deleting_bytes", fixture.Tracked.State == TrackedDownloadState.Imported && fixture.SourceHashes().OrderBy(pair => pair.Key).SequenceEqual(hashes.OrderBy(pair => pair.Key)));
        Check("nonmovable_source_client_removal_preserves_data", fixture.Removed == 1 && fixture.DeleteDataFlags.SequenceEqual(new[] { false }) && fixture.OrderValid);
        fixture.ResetActions();
        fixture.Tracked = fixture.Restart();
        fixture.Process();
        Check("nonmovable_source_restart_removal_preserves_data", fixture.Removed == 1 && fixture.DeleteDataFlags.SequenceEqual(new[] { false }) && fixture.SourceHashes().OrderBy(pair => pair.Key).SequenceEqual(hashes.OrderBy(pair => pair.Key)));
    }

    private static void VerifyRetirementRetry(string root)
    {
        Phase = "source_delete_failure";
        using var fixture = new Scenario(root, Phase, new[] { 13 });
        fixture.Add(13, 17, 1, 2, "first.m4b");
        fixture.Add(13, 17, 2, 2, "second.m4b");
        var hashes = fixture.SourceHashes();
        fixture.DenySourceDeletes = true;
        fixture.Import();
        Check("source_delete_failure_keeps_receipt_but_blocks_completion", fixture.Tracked.State == TrackedDownloadState.ImportBlocked && fixture.ReadReceipt() != null && fixture.Retired > 0 && fixture.SourceDeleteAttempts > 0);
        Check("source_delete_failure_blocks_event_and_client_cleanup", fixture.CompletedEvents == 0 && fixture.Marked == 0 && fixture.Removed == 0 && fixture.OrderValid);
        Check("source_delete_failure_preserves_source_bytes", fixture.SourceHashes().OrderBy(pair => pair.Key).SequenceEqual(hashes.OrderBy(pair => pair.Key)));
        fixture.ResetActions();
        fixture.Tracked = fixture.Restart();
        Check("source_delete_failure_restart_reads_durable_proof", fixture.Tracked?.State == TrackedDownloadState.ImportBlocked && fixture.ReadReceipt() != null);
        var imports = fixture.ImportCalls;
        fixture.Process();
        Check("source_delete_failure_restart_waits_before_retry", fixture.Tracked.State == TrackedDownloadState.ImportBlocked && fixture.Retired == 0 && fixture.ImportCalls == imports);
        fixture.Advance(TimeSpan.FromSeconds(31));
        fixture.Process();
        Check("source_delete_failure_restart_retries_retirement_before_client_cleanup", fixture.Tracked.State == TrackedDownloadState.ImportBlocked && fixture.Retired > 0 && fixture.CompletedEvents == 0 && fixture.Marked == 0 && fixture.Removed == 0 && fixture.OrderValid);
        Check("source_delete_failure_restart_preserves_source_bytes", fixture.SourceHashes().OrderBy(pair => pair.Key).SequenceEqual(hashes.OrderBy(pair => pair.Key)));
        fixture.DenySourceDeletes = false;
        fixture.Advance(TimeSpan.FromSeconds(61));
        fixture.Process();
        Check("source_delete_retry_completes_after_fault_is_removed", fixture.Tracked.State == TrackedDownloadState.Imported && fixture.SourceHashes().Count == 0 && fixture.CompletedEvents == 1 && fixture.Marked == 1 && fixture.Removed == 1 && fixture.OrderValid);
        Check("source_delete_retry_does_not_duplicate_durable_completion", fixture.Store.DownloadRows(fixture.Tracked.DownloadItem.DownloadId)
            .Where(row => row.EventType == DownloadHistoryEventType.DownloadImported)
            .Select(row => row.Data[DownloadHistoryService.ImportReceiptKey]).Distinct().Count() == 1);
        Check("source_delete_retry_skips_committed_import_transfer_and_tagging_boundary", fixture.ImportCalls == imports);
        fixture.Process();
        Check("source_delete_retry_emits_one_live_completion", fixture.CompletedEvents == 1 && fixture.Marked == 1 && fixture.Removed == 1 && fixture.ImportCalls == imports);
    }

    private static void VerifyResumedReceipt(string root)
    {
        foreach (var status in new[] { DownloadItemStatus.Queued, DownloadItemStatus.Paused, DownloadItemStatus.Downloading, DownloadItemStatus.Failed, DownloadItemStatus.Warning })
        {
            var name = "resumed_" + status.ToString().ToLowerInvariant();
            Phase = name;
            using var fixture = new Scenario(root, name, new[] { 13 });
            fixture.Add(13, 17, 0, 1, "owned-recording.m4b");
            fixture.Tracked.KeepSourceFiles = true;
            Check(name + "_has_previous_durable_receipt", fixture.Completion.VerifyAndRecord(fixture.Tracked, fixture.Results) != null);
            var hashes = fixture.SourceHashes();
            fixture.Tracked.State = TrackedDownloadState.Imported;
            fixture.Tracked.DownloadItem.Status = status;
            fixture.Process();
            Check(name + "_old_receipt_cannot_authorize_cleanup", fixture.Tracked.State == TrackedDownloadState.Downloading && fixture.CompletedEvents == 0 && fixture.Marked == 0 && fixture.Removed == 0 && fixture.Retired == 0);
            Check(name + "_preserves_source_bytes", fixture.SourceHashes().OrderBy(pair => pair.Key).SequenceEqual(hashes.OrderBy(pair => pair.Key)));
        }
    }

    private static void VerifyStaleReceipt(string root)
    {
        Phase = "stale_live_file";
        using var fixture = new Scenario(root, Phase, new[] { 13 });
        var file = fixture.Add(13, 17, 0, 1, "owned-recording.m4b");
        var receipt = fixture.Completion.VerifyAndRecord(fixture.Tracked, fixture.Results);
        Check("stale_receipt_fixture_was_persisted", receipt != null);
        fixture.Store.DeleteFile(file.Id);
        var hashes = fixture.SourceHashes();
        fixture.Tracked = fixture.Restart();
        Check("stale_live_file_cannot_authorize_restart_cleanup", fixture.Tracked != null && fixture.Tracked.State != TrackedDownloadState.Imported);
        fixture.Process();
        fixture.AssertRetained(Phase, hashes);
    }

    private static void VerifyForgedImportedState(string root)
    {
        Phase = "forged_imported_state";
        using var fixture = new Scenario(root, Phase, new[] { 13 });
        fixture.AddRejected("owned-recording.m4b", 13, 0, 1);
        var hashes = fixture.SourceHashes();
        fixture.Tracked.State = TrackedDownloadState.Imported;
        fixture.Process();
        Check("forged_state_cannot_trigger_client_or_source_cleanup", fixture.Marked == 0 && fixture.Removed == 0 && fixture.Retired == 0);
        Check("forged_state_retains_source_bytes", fixture.SourceHashes().OrderBy(pair => pair.Key).SequenceEqual(hashes.OrderBy(pair => pair.Key)));
    }

    private static void VerifyFileDiagnostics(string root)
    {
        Phase = "executable_only_diagnostic";
        using (var fixture = new Scenario(root, Phase, new[] { 13 }))
        {
            fixture.AddUnrepresented("setup.EXE");
            var hashes = fixture.SourceHashes();
            fixture.Import();
            Check("published_executable_only_download_has_specific_diagnostic", fixture.Tracked.State == TrackedDownloadState.ImportBlocked && fixture.HasCode(RejectionCode.ExecutableFile) && fixture.HasCode(RejectionCode.NoEligibleFiles));
            fixture.AssertRetained(Phase, hashes);
            Check("published_ebook_container_and_sidecars_are_classified_by_book_policy", fixture.Extensions.Classify("Book.EPUB") == DownloadFileKind.Media && fixture.Extensions.Classify("Book.M4B") == DownloadFileKind.Media && fixture.Extensions.Classify("Book.cue") == DownloadFileKind.Sidecar && fixture.Extensions.Classify("Book.m3u") == DownloadFileKind.Sidecar);
            Check("published_archive_download_is_recoverable_not_executable", fixture.Extensions.GetRejection("Book.ZIP") is { Code: RejectionCode.ArchiveNotExtracted, Type: RejectionType.Temporary });
            fixture.UserRejectedExtensions = ".mp3";
            Check("published_user_rejected_extension_has_its_own_code", fixture.Extensions.GetRejection("Book.MP3")?.Code == RejectionCode.UserRejectedExtension);
        }

        Phase = "remote_inventory_missing_mount";
        using (var fixture = new Scenario(root, Phase, new[] { 13 }))
        {
            fixture.UseMissingOutput();
            fixture.ClientFiles.Add(new DownloadClientFile { RelativePath = "RemoteOnly/NeverOnDisk.MP3", Size = 123 });
            fixture.Import();
            Check("published_remote_inventory_explains_missing_local_media", fixture.Tracked.State == TrackedDownloadState.ImportBlocked && fixture.HasCode(RejectionCode.ImportPathUnavailable) && fixture.ClientInventoryCalls == 1);
            Check("published_remote_inventory_never_becomes_a_disk_path", fixture.DiskPaths.All(path => !path.Contains("RemoteOnly", StringComparison.Ordinal) && !path.Contains("NeverOnDisk", StringComparison.Ordinal)));
            Check("published_remote_inventory_grants_no_completion_or_cleanup", fixture.CompletedEvents == 0 && fixture.Marked == 0 && fixture.Removed == 0 && fixture.Retired == 0 && fixture.ReadReceipt() == null);
        }

        Phase = "remote_inventory_failure";
        using (var fixture = new Scenario(root, Phase, new[] { 13 }))
        {
            fixture.AddUnrepresented("setup.exe");
            fixture.InventoryThrows = true;
            var hashes = fixture.SourceHashes();
            fixture.Import();
            Check("published_inventory_error_retains_primary_and_file_diagnostics", fixture.HasCode(RejectionCode.NoEligibleFiles) && fixture.HasCode(RejectionCode.ExecutableFile) && fixture.HasCode(RejectionCode.InventoryUnavailable));
            fixture.AssertRetained(Phase, hashes);
        }
    }

    private static void VerifyBlockedRecovery(string root)
    {
        Phase = "blocked_missing_book_recovery";
        using var fixture = new Scenario(root, Phase, new[] { 13, 14 });
        var original = fixture.Add(13, 17, 0, 1, "first-book.m4b");
        var source = fixture.SourceHashes();
        var library = fixture.LibraryHashes();
        fixture.Import();
        Check("published_missing_book_import_is_blocked_with_bounded_retry", fixture.Tracked.State == TrackedDownloadState.ImportBlocked && fixture.Tracked.ImportRetryAfterUtc.HasValue && fixture.ImportCalls == 1);
        fixture.Process();
        fixture.Process();
        Check("published_unchanged_block_does_not_repeat_import_work_immediately", fixture.ImportCalls == 1 && fixture.CompletedEvents == 0 && fixture.Retired == 0);
        Check("published_blocked_retry_preserves_existing_file_identity_and_bytes", fixture.Store.GetFile(original.Id)?.EditionId == 17 && fixture.SourceHashes().OrderBy(pair => pair.Key).SequenceEqual(source.OrderBy(pair => pair.Key)) && fixture.LibraryHashes().OrderBy(pair => pair.Key).SequenceEqual(library.OrderBy(pair => pair.Key)));
        fixture.Add(14, 18, 0, 1, "second-book.m4b");
        fixture.Advance(TimeSpan.FromSeconds(31));
        fixture.Process();
        Check("published_block_recovers_after_waiting_and_fixing_coverage", fixture.Tracked.State == TrackedDownloadState.Imported && fixture.ImportCalls == 2 && fixture.CompletedEvents == 1 && fixture.Marked == 1 && fixture.Removed == 1);
        Check("published_recovery_preserves_old_library_file_id_and_bytes", fixture.Store.GetFile(original.Id)?.EditionId == 17 && library.All(pair => fixture.LibraryHashes().TryGetValue(pair.Key, out var hash) && hash == pair.Value));
        fixture.Process();
        Check("published_recovered_import_does_not_duplicate_completion", fixture.ImportCalls == 2 && fixture.CompletedEvents == 1 && fixture.Marked == 1 && fixture.Removed == 1 && fixture.OrderValid);
    }

    private static void VerifyManualIntervention(string root)
    {
        Phase = "manual_intervention_durable_episode";
        using (var fixture = new Scenario(root, Phase, new[] { 13, 14 }))
        {
            fixture.Add(13, 17, 0, 1, "first-recording.m4b");
            fixture.EnableAlertNotifications();
            var source = fixture.SourceHashes();
            fixture.Import();
            Check("published_blocked_import_dispatches_one_actionable_notification", fixture.InterventionEvents == 1 && fixture.AlertMessages.Count == 1 && fixture.AlertWebhooks.Count == 1);
            Check("published_intervention_attempt_is_durable_before_dispatch", fixture.InterventionOrderValid && fixture.Store.DownloadRows(fixture.Tracked.DownloadItem.DownloadId)
                .Count(row => row.EventType == DownloadHistoryEventType.ManualInteraction && row.Data.GetValueOrDefault(ManualInteractionRequiredService.PhaseKey) == "attempted") == 1);
            var alert = fixture.AlertMessages.Single();
            Check("published_intervention_message_identifies_client_source_and_typed_reason", alert.DownloadClientInfo.Id == 11 && alert.DownloadId == fixture.Tracked.DownloadItem.DownloadId &&
                alert.SourcePath == fixture.Tracked.DownloadItem.OutputPath.FullPath && alert.Message.Contains("manual intervention", StringComparison.Ordinal) &&
                alert.StatusMessages.SelectMany(status => status.Rejections).Any(reason => reason.Code == RejectionCode.CompletionUnverified));
            Check("published_intervention_payload_preserves_both_work_identities_and_known_recording", alert.Book == null && alert.BookIdentities.Select(book => book.Id).OrderBy(id => id).SequenceEqual(new[] { 13, 14 }) &&
                alert.BookIdentities.Single(book => book.Id == 13).Editions.Single().Id == 17 && alert.BookIdentities.Single(book => book.Id == 13).Editions.Single().AudibleRegion == "us");
            fixture.Tracked.WarnCode(RejectionCode.InvalidImportPath, "Owned changed diagnostic");
            fixture.RequireIntervention();
            fixture.Tracked.HasNotifiedManualInteractionRequired = false;
            fixture.RequireIntervention(restart: true);
            Check("published_changed_diagnostics_and_service_restart_do_not_repeat_block_alerts", fixture.InterventionEvents == 1 && fixture.AlertMessages.Count == 1);
            var restarted = fixture.Restart();
            Check("published_queue_restart_restores_blocked_download_with_typed_diagnostics", restarted != null && restarted.State == TrackedDownloadState.ImportBlocked &&
                restarted.StatusMessages.SelectMany(status => status.Rejections).Any(reason => reason.Code == RejectionCode.CompletionUnverified));
            fixture.RequireIntervention(restarted, restart: true);
            Check("published_reparsed_queue_restart_uses_the_durable_episode_marker", fixture.InterventionEvents == 1);
            fixture.Store.Insert(new DownloadHistory
            {
                DownloadId = fixture.Tracked.DownloadItem.DownloadId, DownloadClientId = 22, AuthorId = 5,
                EventType = DownloadHistoryEventType.DownloadGrabbed, Date = DateTime.UtcNow, SourceTitle = "Same hash on another client"
            });
            fixture.RequireIntervention();
            Check("published_other_client_grab_does_not_restart_blocked_episode", fixture.InterventionEvents == 1);
            var other = new TrackedDownload
            {
                DownloadClient = 22, Protocol = fixture.Tracked.Protocol, State = TrackedDownloadState.ImportBlocked,
                DownloadItem = fixture.Tracked.DownloadItem.Clone(), RemoteBook = fixture.Tracked.RemoteBook
            };
            other.DownloadItem.DownloadClientInfo = new DownloadClientItemClientInfo { Id = 22, Name = "Other owned client", Type = "OwnedFixture" };
            other.WarnCode(RejectionCode.NoEligibleFiles, "Other client needs review");
            fixture.RequireIntervention(other);
            fixture.RequireIntervention(other, restart: true);
            Check("published_same_download_on_different_clients_has_independent_durable_alerts", fixture.InterventionEvents == 2 && fixture.AlertMessages.Count == 2 &&
                fixture.AlertMessages.Select(message => message.DownloadClientInfo.Id).OrderBy(id => id).SequenceEqual(new[] { 11, 22 }));
            Check("published_blocked_alerts_preserve_all_owned_source_bytes", fixture.SourceHashes().OrderBy(pair => pair.Key).SequenceEqual(source.OrderBy(pair => pair.Key)));
            fixture.Add(14, 18, 0, 1, "second-recording.m4b");
            fixture.DenySourceDeletes = true;
            fixture.Import();
            fixture.RequireIntervention(restart: true);
            Check("published_receipt_without_source_retirement_does_not_reset_intervention", fixture.Tracked.State == TrackedDownloadState.ImportBlocked && fixture.InterventionEvents == 2 &&
                fixture.Store.DownloadRows(fixture.Tracked.DownloadItem.DownloadId).All(row => row.EventType != DownloadHistoryEventType.ManualInteraction || row.Data.GetValueOrDefault(ManualInteractionRequiredService.PhaseKey) != "recovered"));
            fixture.DenySourceDeletes = false;
            fixture.Import();
            Check("published_source_retired_recovery_is_durable_and_client_scoped", fixture.Tracked.State == TrackedDownloadState.Imported && fixture.SourceHashes().Count == 0 &&
                fixture.Store.DownloadRows(fixture.Tracked.DownloadItem.DownloadId).Count(row => row.EventType == DownloadHistoryEventType.ManualInteraction && row.DownloadClientId == 11 &&
                    row.Data.GetValueOrDefault(ManualInteractionRequiredService.PhaseKey) == "recovered") == 1);
            fixture.Tracked.State = TrackedDownloadState.ImportBlocked;
            fixture.Tracked.WarnCode(RejectionCode.CompletionUnverified, "Owned later blocked episode");
            fixture.RequireIntervention(restart: true);
            fixture.RequireIntervention();
            Check("published_verified_recovery_allows_exactly_one_later_block_alert", fixture.InterventionEvents == 3 && fixture.AlertMessages.Count == 3);
            fixture.RequireIntervention(other, restart: true);
            Check("published_recovery_on_one_client_does_not_reset_the_other_clients_episode", fixture.InterventionEvents == 3);
        }

        Phase = "manual_intervention_notification_opt_in";
        using (var fixture = new Scenario(root, Phase, new[] { 13 }))
        {
            fixture.Import();
            Check("published_existing_notification_configs_remain_opted_out", fixture.InterventionEvents == 1 && fixture.AlertMessages.Count == 0 && fixture.AlertWebhooks.Count == 0);
            fixture.EnableAlertNotifications();
            fixture.Tracked.DownloadItem.OutputPath = new OsPath(fixture.Tracked.DownloadItem.OutputPath.FullPath + "-new-source");
            fixture.RequireIntervention();
            var webhook = fixture.AlertWebhooks.Single();
            Check("published_new_webhook_event_requires_existing_import_failure_opt_in", fixture.AlertMessages.Count == 1 && webhook.EventType == WebhookEventType.ManualInteractionRequired && webhook.PayloadVersion == 1);
            Check("published_manual_intervention_webhook_has_actionable_identity_and_codes", webhook.DownloadClientId == 11 && webhook.Author.Id == 5 && webhook.Books.Single().Id == 13 &&
                webhook.DownloadStatusMessages.SelectMany(status => status.Rejections).Any(reason => reason.Code == RejectionCode.NoEligibleFiles) &&
                webhook.SourcePath == fixture.Tracked.DownloadItem.OutputPath.FullPath);
        }

        Phase = "manual_intervention_marker_write_failure";
        using (var fixture = new Scenario(root, Phase, new[] { 13 }))
        {
            fixture.EnableAlertNotifications();
            fixture.Store.FailInterventionWrites = true;
            fixture.Import();
            Check("published_failed_intervention_write_neither_sends_nor_acknowledges", fixture.InterventionEvents == 0 && !fixture.Tracked.HasNotifiedManualInteractionRequired && fixture.Tracked.State == TrackedDownloadState.ImportBlocked);
            fixture.Store.FailInterventionWrites = false;
            fixture.RequireIntervention(restart: true);
            Check("published_failed_intervention_write_can_retry_without_duplicate_import_work", fixture.InterventionEvents == 1 && fixture.AlertMessages.Count == 1 && fixture.ImportCalls == 1);
        }
    }

    internal sealed class ProbeTimeProvider : TimeProvider
    {
        private DateTimeOffset _now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan interval) => _now = _now.Add(interval);
    }

    internal sealed class ProbeFailure(string code) : Exception(code)
    {
        public string Code { get; } = code;
    }

    internal sealed class Scenario : IDisposable
    {
        private readonly string _source;
        private readonly string _library;
        private readonly Author _author = new() { Id = 5, Name = "Owned Author" };
        private readonly Logger _logger = LogManager.GetLogger("OwnedAcquisitionProbe");
        private readonly Dictionary<Type, object> _services = new();
        private readonly Dictionary<int, Book> _books;
        private readonly IDownloadHistoryService _downloadHistory;
        private readonly IHistoryService _history;
        private readonly DownloadClientDefinition _clientDefinition = new() { Id = 11, Name = "Owned download client", Protocol = DownloadProtocol.Usenet, RemoveCompletedDownloads = true };
        private readonly IEventAggregator _events;
        private readonly DownloadEventHub _hub;
        private readonly CompletedDownloadService _completed;
        private readonly ManualInteractionRequiredService _intervention;
        private readonly NotificationService _notifications;
        private readonly List<NotificationDefinition> _notificationDefinitions = new();
        private int _nextFileId = 100;
        private readonly ProbeTimeProvider _clock = new();

        public Store Store { get; }
        public IDownloadHistoryRepository DownloadRepository { get; }
        public TrackedDownload Tracked { get; set; }
        public List<ImportResult> Results { get; } = new();
        public IDownloadImportCompletionService Completion { get; }
        public int CompletedEvents { get; private set; }
        public int Marked { get; private set; }
        public int Removed { get; private set; }
        public int Retired { get; private set; }
        public List<bool> DeleteDataFlags { get; } = new();
        public bool OrderValid { get; private set; } = true;
        public bool DenySourceDeletes { get; set; }
        public int SourceDeleteAttempts { get; private set; }
        public int ImportCalls { get; private set; }
        public int ClientInventoryCalls { get; private set; }
        public bool InventoryThrows { get; set; }
        public List<DownloadClientFile> ClientFiles { get; } = new();
        public List<string> DiskPaths { get; } = new();
        public string UserRejectedExtensions { get; set; } = string.Empty;
        public IRejectedFileExtensionService Extensions { get; private set; }
        public int InterventionEvents { get; private set; }
        public List<BookDownloadMessage> AlertMessages { get; } = new();
        public List<WebhookManualInteractionPayload> AlertWebhooks { get; } = new();
        public bool InterventionOrderValid { get; private set; } = true;

        public Scenario(string root, string name, int[] expectedBooks)
        {
            var path = Path.Combine(root, name);
            Directory.CreateDirectory(path);
            _source = Directory.CreateDirectory(Path.Combine(path, "source")).FullName;
            _library = Directory.CreateDirectory(Path.Combine(path, "library")).FullName;
            Store = new Store(Path.Combine(path, "owned.sqlite"), _author);
            _books = expectedBooks.Append(13).Distinct().ToDictionary(id => id, id => new Book { Id = id, Author = _author, Title = "Owned Book " + id });
            Tracked = new TrackedDownload
            {
                DownloadClient = 11, Protocol = DownloadProtocol.Usenet, IsTrackable = true, IsClientAvailable = true, State = TrackedDownloadState.ImportPending,
                DownloadItem = new DownloadClientItem { DownloadId = "owned-" + name, Title = "Owned Book", Category = "owned", OutputPath = new OsPath(_source),
                    Status = DownloadItemStatus.Completed, CanMoveFiles = true, CanBeRemoved = true,
                    DownloadClientInfo = new DownloadClientItemClientInfo { Id = 11, Name = "Owned download client", Type = "OwnedFixture", Protocol = DownloadProtocol.Usenet } },
                RemoteBook = new RemoteBook { Author = _author, Books = expectedBooks.Select(id => _books[id]).ToList(),
                    ParsedBookInfo = new ParsedBookInfo { Quality = new QualityModel(Quality.M4B) },
                    Release = new ReleaseInfo { Title = "Owned Book", DownloadProtocol = DownloadProtocol.Usenet } }
            };
            Tracked.ImportItem = Tracked.DownloadItem.Clone();
            var entityRepository = Fake<IHistoryRepository>((method, arguments) => method.Name switch
            {
                "Insert" => Store.Insert((EntityHistory)arguments[0]),
                "FindByDownloadId" => Store.EntityRows((string)arguments[0]),
                "MostRecentForDownloadId" => Store.EntityRows((string)arguments[0]).FirstOrDefault(),
                _ => Unexpected(method)
            });
            DownloadRepository = Fake<IDownloadHistoryRepository>((method, arguments) => method.Name switch
            {
                "Insert" => Store.Insert((DownloadHistory)arguments[0]),
                "FindByDownloadId" => Store.DownloadRows((string)arguments[0]),
                "GetSubmissionAttempts" => Store.GetSubmissionAttempts(),
                "Update" => Store.Update((DownloadHistory)arguments[0]),
                "Delete" => Store.Delete((DownloadHistory)arguments[0]),
                _ => Unexpected(method)
            });
            _history = new HistoryService(entityRepository, _logger, DownloadRepository);
            _downloadHistory = new DownloadHistoryService(DownloadRepository, _history);
            DownloadImportSourceService publishedSources = null;
            var sources = Fake<IDownloadImportSourceService>((method, arguments) =>
            {
                if (method.Name == "Scan") return publishedSources.Scan((TrackedDownload)arguments[0]);
                if (method.Name == "Validate") return publishedSources.Validate((TrackedDownload)arguments[0], (DownloadImportReceipt)arguments[1]);
                if (method.Name == "Retire")
                {
                    var receipt = (DownloadImportReceipt)arguments[1];
                    Retired++;
                    OrderValid &= HasReceipt();
                    return publishedSources.Retire((TrackedDownload)arguments[0], receipt);
                }
                return Unexpected(method);
            });
            var files = Fake<IMediaFileService>((method, arguments) => method.Name == "Get" && arguments[0] is int id ? Store.GetFile(id) : Unexpected(method));
            var disk = Fake<IDiskProvider>((method, arguments) =>
            {
                if (arguments.Length > 0 && arguments[0] is string path) DiskPaths.Add(path);
                return method.Name switch
                {
                    "FileExists" => File.Exists((string)arguments[0]),
                    "FolderExists" => Directory.Exists((string)arguments[0]),
                    "GetFileSize" => new FileInfo((string)arguments[0]).Length,
                    "FileGetLastWrite" => File.GetLastWriteTimeUtc((string)arguments[0]),
                    "GetFileAttributes" => File.GetAttributes((string)arguments[0]),
                    "GetDirectoryInfo" => new System.IO.Abstractions.FileSystem().DirectoryInfo.FromDirectoryName((string)arguments[0]),
                    "GetFiles" => Directory.GetFiles((string)arguments[0], "*", (bool)arguments[1] ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly),
                    "GetDirectories" => Directory.GetDirectories((string)arguments[0]),
                    "DeleteFile" => DeleteOwnedSource((string)arguments[0]),
                    _ => Unexpected(method)
                };
            });
            var importItems = Fake<IProvideImportItemService>((method, arguments) => method.Name == "ProvideImportItem" ? ((DownloadClientItem)arguments[0]).Clone() : Unexpected(method));
            var roots = Fake<IRootFolderService>((method, _) => method.Name == "All" ? new List<RootFolder> { new() { Id = 1, Path = _library } } : Unexpected(method));
            var editions = Fake<IEditionService>((method, arguments) => method.Name == "GetEdition" ? Store.GetEdition((int)arguments[0]) : Unexpected(method));
            publishedSources = new DownloadImportSourceService(disk, roots, files, editions, importItems, _logger);
            Completion = new DownloadImportCompletionService(sources, _history, _downloadHistory, files, disk, _logger);
            _events = Fake<IEventAggregator>((method, arguments) =>
            {
                if (method.Name != "PublishEvent") return Unexpected(method);
                if (arguments[0] is DownloadCompletedEvent completed)
                {
                    CompletedEvents++;
                    OrderValid &= HasReceipt();
                    ((DownloadHistoryService)_downloadHistory).Handle(completed);
                    _hub.Handle(completed);
                }
                else if (arguments[0] is DownloadCanBeRemovedEvent removable) _hub.Handle(removable);
                else if (arguments[0] is BookImportIncompleteEvent incomplete)
                {
                    ((HistoryService)_history).Handle(incomplete);
                    ((DownloadHistoryService)_downloadHistory).Handle(incomplete);
                }
                else if (arguments[0] is ManualInteractionRequiredEvent intervention)
                {
                    InterventionEvents++;
                    InterventionOrderValid &= Store.DownloadRows(intervention.TrackedDownload.DownloadItem.DownloadId)
                        .Any(row => row.EventType == DownloadHistoryEventType.ManualInteraction && row.DownloadClientId == intervention.TrackedDownload.DownloadClient &&
                            row.Data.GetValueOrDefault(ManualInteractionRequiredService.PhaseKey) == "attempted");
                    _notifications.Handle(intervention);
                }
                return null;
            });
            var notificationDefinition = new NotificationDefinition { Id = 31, Name = "Owned import notification" };
            var webhookDefinition = new NotificationDefinition { Id = 32, Name = "Owned webhook", Settings = new WebhookSettings() };
            _notificationDefinitions.AddRange(new[] { notificationDefinition, webhookDefinition });
            var importNotification = Fake<INotification>((method, arguments) => method.Name switch
            {
                "get_Definition" => notificationDefinition,
                "OnImportFailure" => CaptureAlert((BookDownloadMessage)arguments[0]),
                _ => Unexpected(method)
            });
            var webhookProxy = Fake<IWebhookProxy>((method, arguments) =>
            {
                if (method.Name != "SendWebhook") return Unexpected(method);
                AlertWebhooks.Add((WebhookManualInteractionPayload)arguments[0]);
                return null;
            });
            var configFile = Fake<IConfigFileProvider>((method, _) => method.Name == "get_InstanceName" ? "Owned candidate" : Unexpected(method));
            var webhook = new Webhook(webhookProxy, configFile) { Definition = webhookDefinition };
            var notifications = new List<INotification> { importNotification, webhook };
            var notificationFactory = Fake<INotificationFactory>((method, _) => method.Name == "OnImportFailureEnabled"
                ? notifications.Where(notification => ((NotificationDefinition)notification.Definition).OnImportFailure).ToList() : Unexpected(method));
            var notificationStatus = Fake<INotificationStatusService>((method, _) => method.Name is "RecordSuccess" or "RecordFailure" ? null : Unexpected(method));
            _notifications = new NotificationService(notificationFactory, notificationStatus, _logger);
            _intervention = new ManualInteractionRequiredService(DownloadRepository, _events);
            var client = Fake<IDownloadClient>((method, arguments) =>
            {
                if (method.Name == "get_Definition") return _clientDefinition;
                if (method.Name == "get_Name") return _clientDefinition.Name;
                if (method.Name == "GetFiles")
                {
                    ClientInventoryCalls++;
                    if (InventoryThrows) throw new NotSupportedException("Owned client inventory unavailable");
                    return ClientFiles;
                }
                if (method.Name == "MarkItemAsImported") { Marked++; OrderValid &= HasReceipt(); return null; }
                if (method.Name == "RemoveItem") { Removed++; DeleteDataFlags.Add((bool)arguments[1]); OrderValid &= HasReceipt(); return null; }
                return Unexpected(method);
            });
            var clientProvider = Fake<IProvideDownloadClient>((method, _) => method.Name == "Get" ? client : Unexpected(method));
            var config = Fake<IConfigService>((method, _) => method.Name switch
            {
                "get_EnableCompletedDownloadHandling" => true,
                "get_UserRejectedExtensions" => UserRejectedExtensions,
                _ => Unexpected(method)
            });
            Extensions = new RejectedFileExtensionService(config);
            var importer = Fake<IDownloadedBooksImportService>((method, _) =>
            {
                if (method.Name != "ProcessPath") return Unexpected(method);
                ImportCalls++;
                return Results;
            });
            var tracked = Fake<ITrackedDownloadService>((method, _) => method.Name == "GetTrackedDownloads" ? new List<TrackedDownload> { Tracked } : Unexpected(method));
            _services[typeof(Logger)] = _logger;
            _services[typeof(IHistoryService)] = _history;
            _services[typeof(IDownloadHistoryService)] = _downloadHistory;
            _services[typeof(IDownloadHistoryRepository)] = DownloadRepository;
            _services[typeof(IDownloadImportCompletionService)] = Completion;
            _services[typeof(IDownloadImportSourceService)] = sources;
            _services[typeof(IDiskProvider)] = disk;
            _services[typeof(IEventAggregator)] = _events;
            _services[typeof(IProvideImportItemService)] = importItems;
            _services[typeof(IDownloadedBooksImportService)] = importer;
            _services[typeof(IProvideDownloadClient)] = clientProvider;
            _services[typeof(IConfigService)] = config;
            _services[typeof(TimeProvider)] = _clock;
            _services[typeof(IImportRetryService)] = new ImportRetryService(_clock);
            _services[typeof(IManualInteractionRequiredService)] = _intervention;
            _services[typeof(IDownloadImportDiagnostics)] = new DownloadImportDiagnostics(disk, clientProvider, Extensions, _logger);
            _services[typeof(ITrackedDownloadService)] = tracked;
            _services[typeof(IFailedDownloadService)] = Fake<IFailedDownloadService>((method, _) => Unexpected(method));
            _services[typeof(ITrackedDownloadAlreadyImported)] = Construct<TrackedDownloadAlreadyImported>(_services);
            _hub = Construct<DownloadEventHub>(_services);
            _completed = Construct<CompletedDownloadService>(_services);
            _services[typeof(ICompletedDownloadService)] = _completed;
            var grabbed = new BookGrabbedEvent(Tracked.RemoteBook) { DownloadId = Tracked.DownloadItem.DownloadId,
                DownloadClientId = 11, DownloadClient = "OwnedFixture", DownloadClientName = "Owned download client" };
            ((HistoryService)_history).Handle(grabbed);
            ((DownloadHistoryService)_downloadHistory).Handle(grabbed);
        }

        public BookFile Add(int bookId, int editionId, int part, int partCount, string name)
        {
            var local = Source(name, bookId, part, partCount);
            var edition = new Edition { Id = editionId, BookId = bookId, Title = "Owned Edition", Monitored = true,
                ForeignEditionId = "audible:us:B0000000" + editionId, Asin = "B0000000" + editionId, AudibleRegion = "us" };
            var bookEditions = _books[bookId].Editions?.Value ?? new List<Edition>();
            if (bookEditions.All(existing => existing.Id != edition.Id)) bookEditions.Add(edition);
            _books[bookId].Editions = bookEditions;
            local.Edition = edition;
            var path = Path.Combine(_library, name);
            File.Copy(local.Path, path);
            var file = new BookFile { Id = ++_nextFileId, EditionId = editionId, Edition = edition, Author = _author,
                Part = part, PartCount = partCount, Path = path, Size = local.Size, OriginalFilePath = local.Path };
            Store.InsertFile(file);
            var result = new ImportResult(new ImportDecision<LocalBook>(local)) { ImportedFile = file };
            Results.Add(result);
            var imported = new NzbDrone.Core.MediaFiles.Events.TrackImportedEvent(local, file, new List<BookFile>(), true, Tracked.DownloadItem);
            ((HistoryService)_history).Handle(imported);
            ((DownloadHistoryService)_downloadHistory).Handle(imported);
            return file;
        }

        public void AddUnrepresented(string name) => Source(name, 13, 0, 1);

        public void AddRejected(string name, int bookId, int part, int count)
        {
            var local = Source(name, bookId, part, count);
            Results.Add(new ImportResult(new ImportDecision<LocalBook>(local), "Owned import rejection"));
        }

        private LocalBook Source(string name, int bookId, int part, int count)
        {
            var path = Path.Combine(_source, name);
            File.WriteAllBytes(path, Encoding.UTF8.GetBytes("Owned synthetic audio: " + name));
            File.SetLastWriteTimeUtc(path, new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc));
            var book = _books.TryGetValue(bookId, out var existing) ? existing : new Book { Id = bookId, Author = _author, Title = "Owned Book" };
            return new LocalBook { Path = path, Author = _author, Book = book, Part = part, PartCount = count,
                Size = new FileInfo(path).Length, Modified = File.GetLastWriteTimeUtc(path),
                Quality = new QualityModel(MediaFileExtensions.GetQualityForExtension(Path.GetExtension(name))) };
        }

        private object DeleteOwnedSource(string path)
        {
            var relative = Path.GetRelativePath(_source, path);
            if (relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) || Path.IsPathRooted(relative))
                throw new InvalidOperationException("Physical probe deletion must remain inside its owned source directory");
            OrderValid &= HasReceipt();
            SourceDeleteAttempts++;
            if (DenySourceDeletes) throw new IOException("Owned source deletion is temporarily denied");
            File.Delete(path);
            return null;
        }

        public void Import(bool allowStorageFailure = false)
        {
            try { _completed.Import(Tracked); }
            catch (Exception) when (allowStorageFailure && Store.FailCompletionWrites) { }
        }

        public void Process() => Construct<DownloadProcessingService>(_services).Execute(new ProcessMonitoredDownloadsCommand());
        private object CaptureAlert(BookDownloadMessage message) { AlertMessages.Add(message); return null; }
        public void EnableAlertNotifications() { foreach (var definition in _notificationDefinitions) definition.OnImportFailure = true; }
        public void RequireIntervention(TrackedDownload tracked = null, bool restart = false) =>
            (restart ? new ManualInteractionRequiredService(DownloadRepository, _events) : _intervention).Required(tracked ?? Tracked);
        public void Advance(TimeSpan interval) => _clock.Advance(interval);
        public bool HasCode(string code) => Tracked.StatusMessages.SelectMany(message => message.Rejections).Any(rejection => rejection.Code == code);
        public void UseMissingOutput()
        {
            Tracked.DownloadItem.OutputPath = new OsPath(Path.Combine(Path.GetDirectoryName(_source), "missing-source"));
            Tracked.ImportItem = Tracked.DownloadItem.Clone();
        }

        public void WriteIncompleteMarker()
        {
            var message = new BookImportIncompleteEvent(Tracked);
            ((HistoryService)_history).Handle(message);
            ((DownloadHistoryService)_downloadHistory).Handle(message);
        }

        public void RecordSourceRetention() => _downloadHistory.RecordSourceRetention(Tracked);
        public bool KeepsSources() => _downloadHistory.KeepSources(Tracked);

        public TrackedDownload Restart()
        {
            var services = new Dictionary<Type, object>(_services)
            {
                [typeof(ICacheManager)] = new CacheManager(),
                [typeof(IParsingService)] = Fake<IParsingService>((method, _) => method.Name == "Map" ? Tracked.RemoteBook : Unexpected(method)),
                [typeof(ICustomFormatCalculationService)] = Fake<ICustomFormatCalculationService>((method, _) => method.Name == "ParseCustomFormat" ? new List<CustomFormat>() : Unexpected(method))
            };
            var tracker = Construct<TrackedDownloadService>(services);
            var item = Tracked.DownloadItem.Clone();
            item.Removed = false;
            var refreshed = tracker.TrackDownload(_clientDefinition, item);
            // This fixture models a successful single-item client poll. The
            // separate tracking probe exercises full/failed/partial responses.
            if (refreshed != null) refreshed.IsClientAvailable = true;
            return refreshed;
        }

        public DownloadImportReceipt ReadReceipt() => Completion.ReadReceipt(Tracked, _downloadHistory.GetLatestImportCompletion(Tracked.DownloadItem.DownloadId, Tracked.DownloadClient));

        private bool HasReceipt() => ReadReceipt() != null;

        public void ResetActions() { CompletedEvents = Marked = Removed = Retired = 0; DeleteDataFlags.Clear(); OrderValid = true; }

        public Dictionary<string, string> SourceHashes() => Hashes(_source);
        public Dictionary<string, string> LibraryHashes() => Hashes(_library);
        private static Dictionary<string, string> Hashes(string folder) => Directory.GetFiles(folder, "*", SearchOption.AllDirectories)
            .ToDictionary(path => path, path => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))));

        public void AssertRetained(string prefix, Dictionary<string, string> before)
        {
            Check(prefix + "_remains_pending", Tracked.State is TrackedDownloadState.ImportBlocked or TrackedDownloadState.ImportPending or TrackedDownloadState.ImportFailed or TrackedDownloadState.Downloading);
            Check(prefix + "_has_no_completion_or_client_cleanup", CompletedEvents == 0 && Marked == 0 && Removed == 0 && Retired == 0);
            Check(prefix + "_preserves_every_source_byte", SourceHashes().OrderBy(pair => pair.Key).SequenceEqual(before.OrderBy(pair => pair.Key)));
            Check(prefix + "_does_not_write_a_complete_receipt", ReadReceipt() == null);
        }

        public void Dispose() { }
    }

    internal sealed class Store
    {
        private readonly string _connectionString;
        private readonly Author _author;
        public bool FailCompletionWrites { get; set; }
        public bool FailInterventionWrites { get; set; }
        public int CompletionWriteAttempts { get; private set; }

        public Store(string path, Author author)
        {
            _author = author;
            _connectionString = new SQLiteConnectionStringBuilder { DataSource = path, Pooling = false, DefaultTimeout = 5 }.ConnectionString;
            Execute("CREATE TABLE EntityHistory (Id INTEGER PRIMARY KEY, BookId INTEGER, AuthorId INTEGER, DownloadId TEXT, EventType INTEGER, Date TEXT, SourceTitle TEXT, Data TEXT)");
            Execute("CREATE TABLE DownloadHistory (Id INTEGER PRIMARY KEY, AuthorId INTEGER, DownloadClientId INTEGER, DownloadId TEXT, EventType INTEGER, Date TEXT, SourceTitle TEXT, Data TEXT)");
            Execute("CREATE TABLE BookFiles (Id INTEGER PRIMARY KEY, BookId INTEGER, EditionId INTEGER, Part INTEGER, PartCount INTEGER, Path TEXT, Size INTEGER)");
        }

        private SQLiteConnection Open() { var connection = new SQLiteConnection(_connectionString); connection.Open(); return connection; }
        private static SQLiteCommand Command(SQLiteConnection connection, string sql, params object[] parameters)
        {
            var command = connection.CreateCommand();
            command.CommandText = sql;
            for (var index = 0; index < parameters.Length; index++) command.Parameters.AddWithValue("@p" + index, parameters[index] ?? DBNull.Value);
            return command;
        }
        private void Execute(string sql, params object[] parameters) { using var connection = Open(); using var command = Command(connection, sql, parameters); command.ExecuteNonQuery(); }
        private static string Data(Dictionary<string, string> value) => JsonSerializer.Serialize(value, new JsonSerializerOptions { DictionaryKeyPolicy = JsonNamingPolicy.CamelCase });

        public EntityHistory Insert(EntityHistory value)
        {
            using var connection = Open();
            using var command = Command(connection, "INSERT INTO EntityHistory(BookId,AuthorId,DownloadId,EventType,Date,SourceTitle,Data) VALUES(@p0,@p1,@p2,@p3,@p4,@p5,@p6)",
                value.BookId, value.AuthorId, value.DownloadId, (int)value.EventType, value.Date.ToString("O"), value.SourceTitle, Data(value.Data));
            command.ExecuteNonQuery();
            using var identity = Command(connection, "SELECT last_insert_rowid()"); value.Id = Convert.ToInt32(identity.ExecuteScalar()); return value;
        }

        public DownloadHistory Insert(DownloadHistory value)
        {
            if (value.EventType == DownloadHistoryEventType.DownloadImported) CompletionWriteAttempts++;
            if (FailCompletionWrites && value.EventType == DownloadHistoryEventType.DownloadImported) throw new IOException("Owned receipt write failure");
            if (FailInterventionWrites && value.EventType == DownloadHistoryEventType.ManualInteraction) throw new IOException("Owned intervention write failure");
            using var connection = Open();
            using var command = Command(connection, "INSERT INTO DownloadHistory(AuthorId,DownloadClientId,DownloadId,EventType,Date,SourceTitle,Data) VALUES(@p0,@p1,@p2,@p3,@p4,@p5,@p6)",
                value.AuthorId, value.DownloadClientId, value.DownloadId, (int)value.EventType, value.Date.ToString("O"), value.SourceTitle, Data(value.Data));
            command.ExecuteNonQuery();
            using var identity = Command(connection, "SELECT last_insert_rowid()"); value.Id = Convert.ToInt32(identity.ExecuteScalar()); return value;
        }

        public DownloadHistory Update(DownloadHistory value)
        {
            Execute("UPDATE DownloadHistory SET AuthorId=@p0,DownloadClientId=@p1,DownloadId=@p2,EventType=@p3,Date=@p4,SourceTitle=@p5,Data=@p6 WHERE Id=@p7",
                value.AuthorId, value.DownloadClientId, value.DownloadId, (int)value.EventType, value.Date.ToString("O"), value.SourceTitle, Data(value.Data), value.Id);
            return value;
        }

        public object Delete(DownloadHistory value)
        {
            Execute("DELETE FROM DownloadHistory WHERE Id=@p0", value.Id);
            return null;
        }

        public List<DownloadHistory> GetSubmissionAttempts()
        {
            var ids = new List<string>();
            using (var connection = Open())
            using (var command = Command(connection, "SELECT DISTINCT DownloadId FROM DownloadHistory WHERE EventType=@p0", (int)DownloadHistoryEventType.DownloadSubmissionPending))
            using (var reader = command.ExecuteReader())
                while (reader.Read()) ids.Add(reader.GetString(0));
            return ids.SelectMany(DownloadRows).Where(row => row.EventType == DownloadHistoryEventType.DownloadSubmissionPending).ToList();
        }

        public void InsertFile(BookFile value) => Execute("INSERT INTO BookFiles(Id,BookId,EditionId,Part,PartCount,Path,Size) VALUES(@p0,@p1,@p2,@p3,@p4,@p5,@p6)",
            value.Id, value.Edition.Value.BookId, value.EditionId, value.Part, value.PartCount, value.Path, value.Size);

        public void DeleteFile(int id) => Execute("DELETE FROM BookFiles WHERE Id=@p0", id);

        public Edition GetEdition(int id)
        {
            using var connection = Open(); using var command = Command(connection, "SELECT BookId FROM BookFiles WHERE EditionId=@p0 LIMIT 1", id); using var reader = command.ExecuteReader();
            return reader.Read() ? new Edition { Id = id, BookId = reader.GetInt32(0) } : null;
        }

        public BookFile GetFile(int id)
        {
            using var connection = Open(); using var command = Command(connection, "SELECT * FROM BookFiles WHERE Id=@p0", id); using var reader = command.ExecuteReader();
            if (!reader.Read()) return null;
            return new BookFile { Id = id, EditionId = reader.GetInt32(2), Edition = new Edition { Id = reader.GetInt32(2), BookId = reader.GetInt32(1) }, Author = _author,
                Part = reader.GetInt32(3), PartCount = reader.GetInt32(4), Path = reader.GetString(5), Size = reader.GetInt64(6) };
        }

        public List<EntityHistory> EntityRows(string downloadId)
        {
            var rows = new List<EntityHistory>();
            using var connection = Open(); using var command = Command(connection, "SELECT * FROM EntityHistory WHERE DownloadId=@p0 ORDER BY Date DESC,Id DESC", downloadId); using var reader = command.ExecuteReader();
            while (reader.Read()) rows.Add(new EntityHistory { Id = reader.GetInt32(0), BookId = reader.GetInt32(1), AuthorId = reader.GetInt32(2), DownloadId = reader.GetString(3),
                EventType = (EntityHistoryEventType)reader.GetInt32(4), Date = DateTime.Parse(reader.GetString(5), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                SourceTitle = reader.GetString(6), Data = JsonSerializer.Deserialize<Dictionary<string, string>>(reader.GetString(7)), Author = _author,
                Book = new Book { Id = reader.GetInt32(1), Author = _author, Title = "Owned Book" } });
            return rows;
        }

        public List<DownloadHistory> DownloadRows(string downloadId)
        {
            var rows = new List<DownloadHistory>();
            using var connection = Open(); using var command = Command(connection, "SELECT * FROM DownloadHistory WHERE DownloadId=@p0 ORDER BY Date DESC,Id DESC", downloadId); using var reader = command.ExecuteReader();
            while (reader.Read()) rows.Add(new DownloadHistory { Id = reader.GetInt32(0), AuthorId = reader.GetInt32(1), DownloadClientId = reader.GetInt32(2), DownloadId = reader.GetString(3),
                EventType = (DownloadHistoryEventType)reader.GetInt32(4), Date = DateTime.Parse(reader.GetString(5), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                SourceTitle = reader.GetString(6), Data = JsonSerializer.Deserialize<Dictionary<string, string>>(reader.GetString(7)) });
            return rows;
        }
    }
}

public class BoundaryProxy : DispatchProxy
{
    public static List<string> Unexpected { get; } = new();
    public Func<MethodInfo, object[], object> Handler { get; set; }
    protected override object Invoke(MethodInfo targetMethod, object[] args) => Handler(targetMethod, args);
}
