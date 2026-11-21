using System;
using System.IO;
using System.Linq;
using NzbDrone.Common.Disk;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.MediaFiles.MediaInfo
{
    public static class MediaInfoAnalysis
    {
        public const int CurrentRevision = 1;
        internal const int MaximumAudioStreams = 64;
        internal const double StoredTimestampToleranceMilliseconds = 1;
        private static readonly TimeSpan RetryInterval = TimeSpan.FromHours(24);
        private static readonly Lazy<string> Bundle = new Lazy<string>(ReadBundleId);

        public static string BundleId => Bundle.Value;

        public static string GetStatus(MediaInfoModel model, long? fileSize = null, DateTime? fileModified = null)
        {
            if (model == null)
            {
                return null;
            }

            if (model.Source == "MyAnonaMouse")
            {
                return "advertised";
            }

            var stamp = model.Analysis;
            if (stamp?.Revision != CurrentRevision || !string.Equals(stamp.BundleId, BundleId, StringComparison.Ordinal) ||
                IsInvalidatedByFileState(model, fileSize, fileModified))
            {
                return "stale";
            }

            return stamp.Complete ? "current" : "partial";
        }

        public static bool IsInvalidatedByFileState(MediaInfoModel model, long? size, DateTime? modified)
        {
            var stamp = model?.Analysis;
            if (stamp == null)
            {
                return model?.LastAnalysisAttempt != null;
            }

            return (size.HasValue && modified.HasValue && !MatchesStoredFile(stamp, size.Value, modified.Value)) ||
                (model.LastAnalysisAttempt?.Target is { } latest && !SameFile(stamp, latest));
        }

        internal static bool SameFile(MediaAnalysisStamp left, MediaAnalysisStamp right)
        {
            return left != null && right != null && left.Size == right.Size && left.ModifiedUtc == right.ModifiedUtc;
        }

        internal static MediaAnalysisStamp Capture(IDiskProvider disk, string path, string bundleId)
        {
            try
            {
                var file = disk.GetFileInfo(path);
                if (file?.Exists != true || file.Length <= 0 || file.LastWriteTimeUtc == default)
                {
                    return null;
                }

                return CreateStamp(file.Length, file.LastWriteTimeUtc, bundleId);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                return null;
            }
        }

        internal static MediaAnalysisStamp Capture(string path, string bundleId)
        {
            try
            {
                var file = new FileInfo(path);
                return file.Exists ? CreateStamp(file.Length, file.LastWriteTimeUtc, bundleId) : null;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                return null;
            }
        }

        internal static MediaAnalysisStamp CreateStamp(long size, DateTime modifiedUtc, string bundleId)
        {
            return size > 0 && modifiedUtc != default
                ? new MediaAnalysisStamp { Revision = CurrentRevision, BundleId = bundleId, Size = size, ModifiedUtc = AsUtc(modifiedUtc) }
                : null;
        }

        internal static void Stamp(MediaInfoModel model, MediaAnalysisStamp target, DateTime now)
        {
            target.Complete = model.Source == "MediaInfo" && target.BundleId != null &&
                model.AudioStreams?.Count is > 0 and < MaximumAudioStreams &&
                model.AudioStreams.All(stream => !string.IsNullOrWhiteSpace(stream?.AudioFormat) &&
                    stream.AudioChannels > 0 && stream.AudioSampleRate > 0);
            model.Analysis = target;
            model.LastAnalysisAttempt = new MediaAnalysisAttempt { Target = target, AttemptedAtUtc = now };
        }

        internal static bool ShouldProbe(MediaInfoModel model, MediaAnalysisStamp target, DateTime now)
        {
            if (model?.Analysis?.Complete == true && MatchesAnalysis(model.Analysis, target) &&
                !IsInvalidatedByFileState(model, null, null))
            {
                return false;
            }

            var attempt = model?.LastAnalysisAttempt;
            return attempt == null || !MatchesAnalysis(attempt.Target, target) || now - attempt.AttemptedAtUtc >= RetryInterval;
        }

        internal static MediaInfoModel PreserveUseful(BookFile stored, MediaInfoModel read, MediaAnalysisStamp attempted, DateTime now)
        {
            var prior = stored.MediaInfo;
            var knownIdentity = prior?.Analysis;
            if (prior != null && knownIdentity == null && stored.Size > 0 && stored.Modified != default)
            {
                knownIdentity = new MediaAnalysisStamp { Size = stored.Size, ModifiedUtc = AsUtc(stored.Modified) };
            }

            // Preserve the last measured streams as a unit. Filling their fields from a
            // different run risks joining observations from different audio streams.
            var sameFile = SameFile(knownIdentity, attempted) ||
                (prior?.Analysis == null && MatchesStoredFile(attempted, stored.Size, stored.Modified));
            var usefulPrior = prior != null && (!string.IsNullOrWhiteSpace(prior.AudioFormat) || prior.AudioBitrate > 0 || prior.AudioStreams?.Count > 0);
            if (read?.Analysis != null && (read.Analysis.Complete || !usefulPrior) &&
                !(sameFile && usefulPrior && LostTechnicalDetail(prior, read)))
            {
                return read;
            }

            var attempt = new MediaAnalysisAttempt { Target = attempted, AttemptedAtUtc = now };
            return (prior ?? new MediaInfoModel()).WithAnalysis(knownIdentity, attempt);
        }

        internal static MediaInfoModel PreserveOnScan(BookFile stored, MediaInfoModel read)
        {
            return read?.Analysis is { } target && read.LastAnalysisAttempt != null
                ? PreserveUseful(stored, read, target, read.LastAnalysisAttempt.AttemptedAtUtc)
                : stored.MediaInfo ?? read;
        }

        private static bool MatchesAnalysis(MediaAnalysisStamp existing, MediaAnalysisStamp target)
        {
            return SameFile(existing, target) && existing.Revision == target.Revision &&
                string.Equals(existing.BundleId, target.BundleId, StringComparison.Ordinal);
        }

        private static bool MatchesStoredFile(MediaAnalysisStamp stamp, long size, DateTime modified)
        {
            // Some database providers round persisted file timestamps. Live before/after
            // checks use exact ticks and keep file hashes out of the background scan.
            return stamp != null && stamp.Size == size && Math.Abs((stamp.ModifiedUtc - AsUtc(modified)).TotalMilliseconds) <= StoredTimestampToleranceMilliseconds;
        }

        private static DateTime AsUtc(DateTime time)
        {
            return time.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(time, DateTimeKind.Utc) : time.ToUniversalTime();
        }

        private static bool LostTechnicalDetail(MediaInfoModel previous, MediaInfoModel current)
        {
            var oldStreams = previous.AudioStreams ?? Enumerable.Empty<AudioStreamInfo>();
            var newStreams = current.AudioStreams ?? Enumerable.Empty<AudioStreamInfo>();
            return LostStreamDetail(previous, current) || oldStreams.Any(stream => stream != null &&
                !newStreams.Any(replacement => replacement != null && replacement.Index == stream.Index && !LostStreamDetail(stream, replacement))) ||
                (previous.GetFileDurationSeconds() > 0 && !(current.GetFileDurationSeconds() > 0));
        }

        private static bool LostStreamDetail(AudioStreamInfo previous, AudioStreamInfo current)
        {
            return Missing(previous.AudioFormat, current.AudioFormat) ||
                Missing(previous.AudioProfile, current.AudioProfile) ||
                Missing(previous.AudioCodecId, current.AudioCodecId) ||
                Missing(previous.AudioCommercialName, current.AudioCommercialName) ||
                Missing(previous.AudioAdditionalFeatures, current.AudioAdditionalFeatures) ||
                Missing(previous.AudioBitrateMode, current.AudioBitrateMode) ||
                Missing(previous.AudioChannelLayout, current.AudioChannelLayout) ||
                Missing(previous.Language, current.Language) ||
                (previous.AudioChannels > 0 && current.AudioChannels <= 0) ||
                (previous.AudioBitrate > 0 && current.AudioBitrate <= 0) ||
                (previous.AudioSampleRate > 0 && current.AudioSampleRate <= 0) ||
                (previous.AudioBits > 0 && current.AudioBits <= 0) ||
                (previous.DurationSeconds > 0 && !(current.DurationSeconds > 0));
        }

        private static bool Missing(string previous, string current)
        {
            return !string.IsNullOrWhiteSpace(previous) && string.IsNullOrWhiteSpace(current);
        }

        private static string ReadBundleId()
        {
            var folder = Path.Combine(AppContext.BaseDirectory, "tools", "mediainfo");
            try
            {
                var identifier = new FileInfo(Path.Combine(folder, "build-id"));
                if (File.Exists(Path.Combine(folder, "mediainfo")) && identifier.Exists && identifier.Length is >= 64 and <= 66)
                {
                    var value = File.ReadAllText(identifier.FullName).Trim();
                    return value.Length == 64 && value.All(Uri.IsHexDigit) ? value.ToLowerInvariant() : null;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }

            return null;
        }
    }
}
