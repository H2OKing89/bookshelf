using System.IO.Abstractions;
using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.Commands;
using NzbDrone.Core.MediaFiles.MediaInfo;
using NzbDrone.Core.Parser.Model;

internal static class MediaAnalysisFreshnessProbe
{
    internal static void Verify(string root)
    {
        var folder = Path.Combine(root, "media-analysis");
        var originalModified = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        const long fileSize = 16384;
        var files = Enumerable.Range(1, 31)
            .Select(id => new BookFile { Id = id, Path = Path.Combine(folder, $"track-{id}.m4b"), Size = fileSize, Modified = originalModified })
            .ToList();
        var first = files[0];
        first.MediaInfo = NativeAnalysis();
        var firstModified = originalModified;
        var diskReads = 0;
        var reads = 0;
        var writtenFiles = 0;
        var fallback = false;
        var mutateDuringRead = false;

        var disk = Program.Fake<IDiskProvider>((method, args) =>
        {
            if (method.Name != "GetFileInfo") return Program.Unexpected(method);
            diskReads++;
            var modified = (string)args[0] == first.Path ? firstModified : originalModified;
            return Program.Fake<IFileInfo>((property, _) => property.Name switch
            {
                "get_Exists" => true,
                "get_Length" => fileSize,
                "get_LastWriteTimeUtc" => modified,
                _ => Program.Unexpected(property)
            });
        });
        var tag = Program.Fake<IAudioTagService>((method, args) =>
        {
            if (method.Name != "ReadAudioTag") return Program.Unexpected(method);
            reads++;
            if (mutateDuringRead && (string)args[0] == first.Path) firstModified = firstModified.AddMinutes(1);
            return new AudioTag { MediaInfo = fallback ? new MediaInfoModel { Source = "TagLib", AudioBitrate = 64000 } : NativeAnalysis() };
        });
        var probe = Program.Fake<IMediaInfoProbe>((method, _) => method.Name == "get_BundleId"
            ? MediaInfoAnalysis.BundleId
            : Program.Unexpected(method));
        var mediaFiles = Program.Fake<IMediaFileService>((method, args) =>
        {
            switch (method.Name)
            {
                case "GetFiles":
                    return files;
                case "GetFilesByBook" when (int)args[0] == 7:
                    return new List<BookFile> { first };
                case "UpdateMediaInfo":
                    writtenFiles += ((List<BookFile>)args[0]).Count;
                    return null;
                default:
                    return Program.Unexpected(method);
            }
        });
        var service = new RefreshMediaInfoService(mediaFiles, tag, probe, disk, LogManager.GetLogger("OwnedMediaAnalysisProbe"));

        Program.Check("published_media_analysis_identifies_the_packaged_tool", MediaInfoAnalysis.BundleId != null);
        Program.Check("published_legacy_analysis_is_stale", Status(first) == "stale");
        service.Execute(new RefreshStaleMediaInfoCommand());
        Program.Check("published_scheduled_media_analysis_is_bounded_and_stamps_stable_files",
            reads == 30 && writtenFiles == 30 && Status(first) == "current" && files[^1].MediaInfo == null);
        service.Execute(new RefreshStaleMediaInfoCommand());
        Program.Check("published_scheduled_media_analysis_advances_without_reprobing_healthy_files",
            reads == 31 && writtenFiles == 31 && diskReads == 62 && Status(files[^1]) == "current");

        first.MediaInfo.Analysis.BundleId = "previous-bundle";
        Program.Check("published_previous_bundle_analysis_is_stale", Status(first) == "stale");
        service.Execute(new RefreshStaleMediaInfoCommand());
        Program.Check("published_scheduled_media_analysis_renews_previous_bundle_evidence",
            reads == 32 && writtenFiles == 32 && Status(first) == "current");

        fallback = true;
        firstModified = firstModified.AddMinutes(1);
        first.Modified = firstModified;
        service.Execute(new RefreshStaleMediaInfoCommand());
        service.Execute(new RefreshStaleMediaInfoCommand());
        Program.Check("published_incomplete_reanalysis_retains_streams_and_waits_before_retrying_changed_file",
            reads == 33 && writtenFiles == 33 && Status(first) == "stale" && first.MediaInfo.AudioStreams.Single().AudioProfile == "LC" &&
            MediaInfoAnalysis.IsInvalidatedByFileState(first.MediaInfo, first.Size, first.Modified));

        fallback = false;
        service.Execute(new RefreshMediaInfoCommand { BookId = 7 });
        Program.Check("published_manual_media_analysis_can_retry_a_failed_target_immediately",
            reads == 34 && writtenFiles == 34 && Status(first) == "current");
        var prior = first.MediaInfo;
        mutateDuringRead = true;
        service.Execute(new RefreshMediaInfoCommand { BookId = 7 });
        Program.Check("published_media_analysis_does_not_save_when_the_file_changes_during_probe",
            reads == 35 && writtenFiles == 34 && ReferenceEquals(prior, first.MediaInfo));
    }

    private static string Status(BookFile file) => MediaInfoAnalysis.GetStatus(file.MediaInfo, file.Size, file.Modified);

    private static MediaInfoModel NativeAnalysis() => new()
    {
        Source = "MediaInfo",
        AudioFormat = "AAC",
        AudioProfile = "LC",
        AudioChannels = 2,
        AudioSampleRate = 48000,
        AudioStreams = new List<AudioStreamInfo>
        {
            new() { Index = 0, AudioFormat = "AAC", AudioProfile = "LC", AudioChannels = 2, AudioSampleRate = 48000 }
        }
    };
}
