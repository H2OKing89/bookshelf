using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Download;
using NzbDrone.Core.History;
using NzbDrone.Core.MetadataSource.Audiobook;
using NzbDrone.Core.MetadataSource.Audnexus;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.MediaFiles.BookImport
{
    public class ReleaseGroupPackContext
    {
        public const string HistoryKey = "releaseGroupRecordingIds";

        private ReleaseGroupPackContext(string[] ids, string uploaderGroup)
        {
            RecordingIds = ids;
            UploaderGroup = uploaderGroup;
        }

        public string[] RecordingIds { get; }
        public string UploaderGroup { get; }

        public static string Capture(RemoteBook remoteBook)
        {
            // An omnibus can cover several works, but is still one recording.
            // Use only the independently mapped native recording identities
            // of an actual grab, never its range text or discography flag.
            var ids = remoteBook?.Books?.Select(book => book?.Id > 0 ? book.ForeignBookId : null).Distinct(StringComparer.Ordinal).ToArray();
            return IsIndependentSet(ids) ? string.Join('|', ids.OrderBy(id => id, StringComparer.Ordinal)) : null;
        }

        public static ReleaseGroupPackContext Read(IHistoryService history, DownloadClientItem download)
        {
            if (download?.DownloadId.IsNotNullOrWhiteSpace() != true || download.DownloadClientInfo?.Id is not > 0)
            {
                return null;
            }

            // The qualified download identity is mandatory. Do not borrow a
            // different client's grab or infer missing historical evidence.
            var grab = (history.FindByDownloadId(download.DownloadId, download.DownloadClientInfo.Id) ?? new List<EntityHistory>())
                .Where(row => row.EventType == EntityHistoryEventType.Grabbed)
                .OrderByDescending(row => row.Date).ThenByDescending(row => row.Id).FirstOrDefault();
            if (grab?.Data == null || !grab.Data.TryGetValue(HistoryKey, out var stored) || string.IsNullOrEmpty(stored))
            {
                return null;
            }

            var recordedClient = grab.Data.GetValueOrDefault("downloadClientId") ?? grab.Data.GetValueOrDefault("DownloadClientId");
            if (recordedClient != download.DownloadClientInfo.Id.ToString(CultureInfo.InvariantCulture))
            {
                return null;
            }

            var ids = stored.Split('|');
            return IsIndependentSet(ids) ? new ReleaseGroupPackContext(ids, grab.Data.GetValueOrDefault(EntityHistory.RELEASE_GROUP) ?? grab.Data.GetValueOrDefault("ReleaseGroup")) : null;
        }

        public bool Matches(LocalBook file)
        {
            var id = file.Edition?.ForeignEditionId;
            var asin = NativeAsin(id);
            var tagAsin = AudioRecordingIdentifier.Normalize(file.FileTrackInfo?.Asin);

            // A selected edition alone is not proof. Check the exact observed
            // ASIN and the assigned recording, and reject contradictory path
            // evidence. A bare ASIN shared across markets is excluded above.
            return !file.AdditionalFile && MediaFileExtensions.AudioExtensions.Contains(Path.GetExtension(file.Path)) &&
                asin != null && tagAsin == asin && file.FileTrackInfo?.AsinConflict == false &&
                file.Book?.ForeignBookId == id && RecordingIds.Contains(id, StringComparer.Ordinal) &&
                AudioRecordingIdentifier.MatchesAsin(asin, file.Edition) &&
                !AudioRecordingIdentifier.Inspect(file.Path, null, false, tagAsin).IdentifierConflict;
        }

        private static bool IsIndependentSet(string[] ids)
        {
            if (ids == null || ids.Length < 2 || ids.Length > 1000)
            {
                return false;
            }

            var asins = ids.Select(NativeAsin).ToArray();
            return asins.All(asin => asin != null) && asins.Distinct(StringComparer.Ordinal).Count() == ids.Length;
        }

        private static string NativeAsin(string id)
        {
            var parts = id?.Split(':');
            return parts?.Length == 3 && parts[0] == "audible" && AudnexusProxy.Regions.Contains(parts[1]) && AudiobookId.IsAsin(parts[2]) ? parts[2] : null;
        }
    }

    public static class ReleaseGroupResolution
    {
        public static void Apply(LocalBook file, ReleaseGroupPackContext pack = null)
        {
            var evidence = new ReleaseGroupEvidence
            {
                DownloadGroup = file.DownloadClientBookInfo?.ReleaseGroup,
                FolderGroup = file.FolderTrackInfo?.ReleaseGroup,
                FileGroup = file.FileTrackInfo?.ReleaseGroup
            };

            // Ordinary single recordings and multipart audiobooks keep the
            // previous download, folder, filename order exactly.
            if (evidence.DownloadGroup.IsNotNullOrWhiteSpace())
            {
                Resolve(evidence, evidence.DownloadGroup, "download", "ordinaryPrecedence");
            }
            else if (evidence.FolderGroup.IsNotNullOrWhiteSpace())
            {
                Resolve(evidence, evidence.FolderGroup, "folder", "ordinaryPrecedence");
            }
            else
            {
                Resolve(evidence, evidence.FileGroup, evidence.FileGroup.IsNullOrWhiteSpace() ? "unknown" : "file", "ordinaryPrecedence");
            }

            if (pack != null)
            {
                evidence.PackRecordingIds = pack.RecordingIds;
                evidence.PackUploaderGroup = FirstLabel(pack.UploaderGroup, evidence.DownloadGroup);
                evidence.Reason = "unverifiedPackRecording";
            }

            if (pack?.Matches(file) == true)
            {
                evidence.RecordingId = file.Edition.ForeignEditionId;
                evidence.RecordingFolderGroup = RecordingFolderGroup(file.Path, AudioRecordingIdentifier.Normalize(file.FileTrackInfo.Asin));
                evidence.FileGroup = FirstLabel(evidence.FileGroup, Parser.Parser.ParseReleaseGroup(Path.GetFileName(file.Path)));

                var conflict = evidence.RecordingFolderGroup.IsNotNullOrWhiteSpace() && evidence.FileGroup.IsNotNullOrWhiteSpace() &&
                    !string.Equals(evidence.RecordingFolderGroup, evidence.FileGroup, StringComparison.OrdinalIgnoreCase);
                if (conflict)
                {
                    // Do not invent a recording-level group from disagreeing
                    // labels. Retain the uploader (or unknown) and both labels.
                    Resolve(evidence, evidence.PackUploaderGroup, "packUploader", "conflictingRecordingLabels");
                }
                else if (evidence.FileGroup.IsNotNullOrWhiteSpace())
                {
                    Resolve(evidence, evidence.FileGroup, "recordingFile", "exactPackRecording");
                }
                else if (evidence.RecordingFolderGroup.IsNotNullOrWhiteSpace())
                {
                    Resolve(evidence, evidence.RecordingFolderGroup, "recordingFolder", "exactPackRecording");
                }
                else
                {
                    Resolve(evidence, evidence.PackUploaderGroup, "packUploader", "noRecordingLabel");
                }
            }

            if (file.ReleaseGroupOverride != null)
            {
                file.ReleaseGroupOverride = file.ReleaseGroupOverride.Trim();
                evidence = ReleaseGroupEvidence.ManualChoice(evidence, file.ReleaseGroupOverride);
            }

            file.ReleaseGroupEvidence = evidence;
            file.ReleaseGroup = evidence.ResolvedGroup;
        }

        private static string RecordingFolderGroup(string path, string asin)
        {
            var parent = Path.GetDirectoryName(path);
            for (var depth = 0; depth < 4 && parent.IsNotNullOrWhiteSpace(); depth++)
            {
                var name = Path.GetFileName(parent);
                var ids = AudioRecordingIdentifier.ReadBracketedAsins(name);
                if (ids.Count > 0)
                {
                    return ids.Count == 1 && ids[0] == asin ? Parser.Parser.ParseReleaseGroup(name) : null;
                }

                parent = Path.GetDirectoryName(parent);
            }

            return null;
        }

        private static string FirstLabel(string first, string second) => first.IsNotNullOrWhiteSpace() ? first : second;

        private static void Resolve(ReleaseGroupEvidence evidence, string group, string source, string reason)
        {
            evidence.ResolvedGroup = group;
            evidence.Source = source;
            evidence.Reason = reason;
        }
    }
}
