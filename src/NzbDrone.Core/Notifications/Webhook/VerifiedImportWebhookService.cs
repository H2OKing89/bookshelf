using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Serializer;
using NzbDrone.Core.Books;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.CustomFormats;
using NzbDrone.Core.Download;
using NzbDrone.Core.Download.History;
using NzbDrone.Core.Download.TrackedDownloads;
using NzbDrone.Core.History;
using NzbDrone.Core.Languages;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.BookImport;
using NzbDrone.Core.MediaFiles.MediaInfo;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.Notifications.Webhook
{
    public class VerifiedImportWebhookService : IHandle<DownloadCompletedEvent>
    {
        public const string ContextKey = "importCompleteWebhookV1";

        private readonly INotificationFactory _factory;
        private readonly INotificationStatusService _status;
        private readonly IDownloadImportCompletionService _completion;
        private readonly IDownloadHistoryService _downloads;
        private readonly IDownloadHistoryRepository _downloadHistory;
        private readonly IHistoryService _history;
        private readonly IMediaFileService _files;
        private readonly IConfigFileProvider _config;
        private readonly Logger _logger;
        private readonly object _gate = new object();

        public VerifiedImportWebhookService(INotificationFactory factory,
                                            INotificationStatusService status,
                                            IDownloadImportCompletionService completion,
                                            IDownloadHistoryService downloads,
                                            IDownloadHistoryRepository downloadHistory,
                                            IHistoryService history,
                                            IMediaFileService files,
                                            IConfigFileProvider config,
                                            Logger logger)
        {
            _factory = factory;
            _status = status;
            _completion = completion;
            _downloads = downloads;
            _downloadHistory = downloadHistory;
            _history = history;
            _files = files;
            _config = config;
            _logger = logger;
        }

        public void Handle(DownloadCompletedEvent message)
        {
            var tracked = message.TrackedDownload;
            var item = tracked?.DownloadItem;
            if (tracked?.State != TrackedDownloadState.Imported || item?.Status != DownloadItemStatus.Completed ||
                tracked.ImportReceipt == null || tracked.DownloadClient <= 0 || item.DownloadId.IsNullOrWhiteSpace() ||
                (item.DownloadClientInfo?.Id > 0 && item.DownloadClientInfo.Id != tracked.DownloadClient))
            {
                return;
            }

            try
            {
                var hooks = _factory.OnImportCompleteEnabled();
                if (hooks.Empty())
                {
                    return;
                }

                var committed = _downloads.GetLatestDownloadHistoryItem(item.DownloadId, tracked.DownloadClient);
                var receipt = _completion.ReadReceipt(tracked, committed);
                if (receipt == null || receipt.AuthorId != message.AuthorId)
                {
                    return;
                }

                var grab = _downloads.GetLatestGrab(item.DownloadId, tracked.DownloadClient);
                var context = Context(receipt, grab);
                if (context != Context(tracked.ImportReceipt, grab))
                {
                    return;
                }

                var payload = BuildPayload(tracked, receipt, committed, grab, context, out var author);
                if (payload == null || STJson.ToJson(payload).Length > DownloadHistoryService.MaximumReceiptLength)
                {
                    return;
                }

                var eligible = hooks.Where(hook => hook.Definition.Tags.Empty() || hook.Definition.Tags.Intersect(author.Tags).Any()).ToList();
                if (eligible.Count == 0 || !Claim(receipt, committed, context))
                {
                    return;
                }

                foreach (var hook in eligible)
                {
                    Send(hook, payload);
                }
            }
            catch (Exception error)
            {
                // Payloads and provider failures can contain paths or request credentials.
                _logger.Warn("Unable to prepare verified import webhook ({0})", error.GetType().Name);
            }
        }

        private bool Claim(DownloadImportReceipt receipt, DownloadHistory committed, string context)
        {
            lock (_gate)
            {
                var previous = _downloadHistory.FindByDownloadId(receipt.DownloadId);
                if (previous.Any(row => row.DownloadClientId == receipt.DownloadClientId && row.DownloadId == receipt.DownloadId &&
                    row.EventType == DownloadHistoryEventType.DownloadImportCompleteNotification &&
                    RecordingLanguageEvidence.ReadHistory(row.Data, ContextKey) == context))
                {
                    return false;
                }

                // This stores an attempt before delivery. A restart must not replay an
                // event that could already have been accepted by a webhook consumer.
                return _downloadHistory.Insert(new DownloadHistory
                {
                    AuthorId = receipt.AuthorId,
                    DownloadId = receipt.DownloadId,
                    DownloadClientId = receipt.DownloadClientId,
                    Date = DateTime.UtcNow,
                    SourceTitle = committed.SourceTitle ?? string.Empty,
                    Protocol = committed.Protocol,
                    EventType = DownloadHistoryEventType.DownloadImportCompleteNotification,
                    Data = new Dictionary<string, string> { [ContextKey] = context }
                })?.Id > 0;
            }
        }

        private void Send(Webhook hook, WebhookImportCompletePayload payload)
        {
            try
            {
                hook.OnImportComplete(payload);
                _status.RecordSuccess(hook.Definition.Id);
            }
            catch (Exception error)
            {
                _logger.Warn("Unable to send verified import webhook to connection {0} ({1})", hook.Definition.Id, error.GetType().Name);
                try
                {
                    _status.RecordFailure(hook.Definition.Id);
                }
                catch (Exception statusError)
                {
                    _logger.Debug("Unable to record webhook failure status ({0})", statusError.GetType().Name);
                }
            }
        }

        private WebhookImportCompletePayload BuildPayload(TrackedDownload tracked,
                                                          DownloadImportReceipt receipt,
                                                          DownloadHistory committed,
                                                          DownloadHistory grab,
                                                          string context,
                                                          out Author author)
        {
            author = null;
            var formatRows = _history.Find(receipt.DownloadId, EntityHistoryEventType.BookFileImported, receipt.DownloadClientId)
                .Where(row => row.AuthorId == receipt.AuthorId && row.Date <= committed.Date && (grab == null || row.Date >= grab.Date))
                .ToLookup(row => RecordingLanguageEvidence.ReadHistory(row.Data, "fileId"));
            var recordings = new Dictionary<int, WebhookCommittedRecording>();
            foreach (var part in receipt.Files.OrderBy(file => file.BookId).ThenBy(file => file.EditionId).ThenBy(file => file.Part))
            {
                var file = _files.Get(part.FileId);
                var edition = file?.Edition?.Value;
                var fileAuthor = file?.Author?.Value;
                if (file?.Id != part.FileId || file.EditionId != part.EditionId || file.Part != part.Part ||
                    edition?.Id != part.EditionId || edition.BookId != part.BookId || fileAuthor?.Id != receipt.AuthorId ||
                    !file.Path.PathEquals(part.ImportedPath))
                {
                    return null;
                }

                author ??= fileAuthor;
                if (!recordings.TryGetValue(edition.Id, out var recording))
                {
                    recording = new WebhookCommittedRecording
                    {
                        BookId = part.BookId,
                        BookTitle = edition.Book?.Value?.Title,
                        EditionId = edition.Id,
                        EditionTitle = edition.Title,
                        RecordingId = edition.ForeignEditionId,
                        Asin = edition.Asin,
                        AudibleRegion = edition.AudibleRegion,
                        IsEbook = edition.IsEbook
                    };
                    recordings.Add(edition.Id, recording);
                }

                var row = formatRows[part.FileId.ToString(CultureInfo.InvariantCulture)].FirstOrDefault(history => history.BookId == part.BookId &&
                    RecordingLanguageEvidence.ReadHistory(history.Data, "editionId") == part.EditionId.ToString(CultureInfo.InvariantCulture) &&
                    MatchesRecordedPath(part.ImportedPath, RecordingLanguageEvidence.ReadHistory(history.Data, "importedPath")) &&
                    MatchesRecordedPath(part.SourcePath, RecordingLanguageEvidence.ReadHistory(history.Data, "droppedPath")));
                recording.Parts.Add(new WebhookCommittedPart
                {
                    FileId = file.Id,
                    Part = part.Part,
                    PartCount = part.PartCount,
                    SourceFileName = FileName(part.SourcePath),
                    ImportedFileName = FileName(part.ImportedPath),
                    Size = file.Size,
                    Quality = file.Quality?.Quality?.Name,
                    Media = Media(file, edition),
                    CustomFormatScores = CommittedImportScores.Read(RecordingLanguageEvidence.ReadHistory(row?.Data, CommittedImportScores.HistoryKey), file)
                });
            }

            if (author == null)
            {
                return null;
            }

            var client = tracked.DownloadItem.DownloadClientInfo;
            var source = RecordingLanguageEvidence.ReadHistory(grab?.Data, "releaseSource");
            var reportedScore = RecordingLanguageEvidence.ReadHistory(grab?.Data, "customFormatScore");
            return new WebhookImportCompletePayload
            {
                EventType = WebhookEventType.ImportComplete,
                InstanceName = _config.InstanceName,
                EventId = context,
                CommittedAtUtc = committed.Date,
                AuthorId = receipt.AuthorId,
                AuthorName = author.Name,
                DownloadClientId = receipt.DownloadClientId,
                DownloadClient = client?.Name,
                DownloadClientType = client?.Type,
                DownloadId = receipt.DownloadId,
                Protocol = committed.Protocol.ToString(),
                IndexerId = grab?.IndexerId > 0 ? grab.IndexerId : null,
                Indexer = RecordingLanguageEvidence.ReadHistory(grab?.Data, "indexer"),
                ReleaseSource = Enum.TryParse<ReleaseSourceType>(source, out var releaseSource) && Enum.IsDefined(releaseSource) ? releaseSource.ToString() : null,
                GrabCustomFormatScore = int.TryParse(reportedScore, NumberStyles.Integer, CultureInfo.InvariantCulture, out var score) ? score : null,
                Recordings = recordings.Values.ToList()
            };
        }

        private static WebhookCommittedMedia Media(BookFile file, Edition edition)
        {
            var info = file.MediaInfo;
            if (info == null || info.Source is not null and not "MediaInfo" and not "TagLib" ||
                MediaInfoAnalysis.IsInvalidatedByFileState(info, file.Size, file.Modified))
            {
                return null;
            }

            var audio = AudioFormatEvidence.ForFile(info, edition);
            return new WebhookCommittedMedia
            {
                Source = info.Source,
                SourceVersion = info.SourceVersion,
                AnalysisStatus = MediaInfoAnalysis.GetStatus(info, file.Size, file.Modified),
                ContainerFormat = info.ContainerFormat,
                AudioCodec = audio.Codec == AudioCodecKind.Unknown ? null : audio.Codec.ToString(),
                AudioChannels = audio.Channels > 0 ? audio.Channels : null,
                AudioBitrateMode = audio.BitrateMode == AudioBitrateKind.Unknown ? null : audio.BitrateMode.ToString(),
                AudioSampleRate = audio.SampleRate > 0 ? audio.SampleRate : null,
                DurationSeconds = info.GetFileDurationSeconds()
            };
        }

        private static string Context(DownloadImportReceipt receipt, DownloadHistory grab)
        {
            var evidence = new
            {
                receipt.DownloadClientId,
                receipt.DownloadId,
                receipt.AuthorId,
                GrabId = grab?.Id,
                GrabUtc = grab?.Date.ToUniversalTime(),
                Files = receipt.Files.OrderBy(file => file.FileId).Select(file => new { file.FileId, file.BookId, file.EditionId, file.Part, file.PartCount })
            };
            return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(STJson.ToJson(evidence))));
        }

        private static bool MatchesRecordedPath(string committedPath, string recordedPath)
        {
            try
            {
                return recordedPath.IsNotNullOrWhiteSpace() && committedPath.PathEquals(recordedPath);
            }
            catch (Exception error) when (error is ArgumentException or IOException or NotSupportedException)
            {
                return false;
            }
        }

        private static string FileName(string path)
        {
            return Path.GetFileName(path.Replace('\\', '/'));
        }
    }
}
