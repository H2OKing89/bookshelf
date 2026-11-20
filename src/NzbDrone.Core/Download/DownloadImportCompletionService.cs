using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using NLog;
using NzbDrone.Common;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Serializer;
using NzbDrone.Core.Download.History;
using NzbDrone.Core.Download.TrackedDownloads;
using NzbDrone.Core.History;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.BookImport;

namespace NzbDrone.Core.Download
{
    public class DownloadImportCompletionService : IDownloadImportCompletionService
    {
        private readonly IDownloadImportSourceService _sources;
        private readonly IHistoryService _history;
        private readonly IDownloadHistoryService _downloadHistory;
        private readonly IMediaFileService _files;
        private readonly IDiskProvider _disk;
        private readonly Logger _logger;

        public DownloadImportCompletionService(IDownloadImportSourceService sources,
                                              IHistoryService history,
                                              IDownloadHistoryService downloadHistory,
                                              IMediaFileService files,
                                              IDiskProvider disk,
                                              Logger logger)
        {
            _sources = sources;
            _history = history;
            _downloadHistory = downloadHistory;
            _files = files;
            _disk = disk;
            _logger = logger;
        }

        public DownloadImportReceipt VerifyAndRecord(TrackedDownload trackedDownload, List<ImportResult> importResults)
        {
            if (!ValidContext(trackedDownload))
            {
                return null;
            }

            var previousHistory = _downloadHistory.GetLatestDownloadHistoryItem(trackedDownload.DownloadItem.DownloadId, trackedDownload.DownloadClient);
            var previous = ReadReceipt(trackedDownload, previousHistory);
            var resumedCompletion = false;
            if (previous == null && previousHistory?.EventType == DownloadHistoryEventType.DownloadImportIncomplete)
            {
                previous = ReadReceipt(trackedDownload, _downloadHistory.GetLatestImportCompletion(
                    trackedDownload.DownloadItem.DownloadId, trackedDownload.DownloadClient));
                resumedCompletion = previous != null;
            }

            if (previous != null)
            {
                if (resumedCompletion || (trackedDownload.KeepSourceFiles && !previous.KeepSourceFiles))
                {
                    previous.KeepSourceFiles |= trackedDownload.KeepSourceFiles;
                    if (!_sources.Validate(trackedDownload, previous))
                    {
                        return null;
                    }

                    _downloadHistory.RecordImportCompletion(trackedDownload, previous);
                }

                return previous;
            }

            var sourceFiles = _sources.Scan(trackedDownload);
            if (sourceFiles == null || sourceFiles.Count == 0 || sourceFiles.Count > 10000 ||
                sourceFiles.Select(file => file.Path).Distinct(PathEqualityComparer.Instance).Count() != sourceFiles.Count)
            {
                return null;
            }

            var history = BoundHistory(trackedDownload);
            var expectedBooks = ExpectedBooks(trackedDownload, history);
            if (expectedBooks == null || expectedBooks.Count == 0 || expectedBooks.Count > 4096)
            {
                return null;
            }

            var receipt = new DownloadImportReceipt
            {
                DownloadId = trackedDownload.DownloadItem.DownloadId,
                DownloadClientId = trackedDownload.DownloadClient,
                OutputPath = OutputPath(trackedDownload),
                KeepSourceFiles = trackedDownload.KeepSourceFiles || _downloadHistory.KeepSources(trackedDownload) || StoredKeepSourceFiles(trackedDownload,
                    _downloadHistory.GetLatestImportCompletion(trackedDownload.DownloadItem.DownloadId, trackedDownload.DownloadClient)),
                BookIds = expectedBooks.OrderBy(id => id).ToList()
            };

            var authorIds = new HashSet<int>();
            var latestGrabDate = _downloadHistory.GetLatestGrab(trackedDownload.DownloadItem.DownloadId, trackedDownload.DownloadClient)?.Date;
            foreach (var source in sourceFiles.OrderBy(file => file.Path, StringComparer.Ordinal))
            {
                var decisions = (importResults ?? new List<ImportResult>())
                    .Where(result => result.ImportDecision?.Item != null && SamePath(result.ImportDecision.Item.Path, source.Path))
                    .ToList();
                var current = decisions.Where(result => result.Result == ImportResultType.Imported).ToList();
                DownloadImportFile evidence = null;
                if (current.Count == 1)
                {
                    var item = current[0].ImportDecision.Item;
                    var file = current[0].ImportedFile;
                    if (file != null && item.Book?.Id > 0 && item.Edition?.Id > 0 &&
                        item.Size == source.Size && item.Modified.ToUniversalTime() == source.ModifiedUtc.ToUniversalTime())
                    {
                        evidence = new DownloadImportFile
                        {
                            BookId = item.Book.Id,
                            EditionId = item.Edition.Id,
                            FileId = file.Id,
                            Part = item.Part,
                            PartCount = item.PartCount,
                            SourcePath = source.Path,
                            ImportedPath = file.Path,
                            SourceSize = source.Size,
                            SourceModifiedUtc = source.ModifiedUtc.ToUniversalTime()
                        };
                    }
                }

                if (evidence == null)
                {
                    evidence = HistoricalFile(source, history, latestGrabDate);
                }

                if (evidence == null || !MatchesCurrentDecisions(evidence, decisions) || !WithinOutput(trackedDownload, evidence.SourcePath) ||
                    !expectedBooks.Contains(evidence.BookId) || !ValidLiveFile(evidence, out var authorId))
                {
                    return null;
                }

                authorIds.Add(authorId);
                receipt.Files.Add(evidence);
            }

            if (authorIds.Count != 1 || !expectedBooks.SetEquals(receipt.Files.Select(file => file.BookId)) || !ValidParts(receipt.Files, true))
            {
                return null;
            }

            receipt.AuthorId = authorIds.Single();
            if (trackedDownload.RemoteBook?.Author?.Id > 0 && trackedDownload.RemoteBook.Author.Id != receipt.AuthorId)
            {
                return null;
            }

            if (!_sources.Validate(trackedDownload, receipt))
            {
                return null;
            }

            // Persist synchronously. An event subscriber cannot acknowledge this
            // write because event publication intentionally catches its errors.
            _downloadHistory.RecordImportCompletion(trackedDownload, receipt);
            return receipt;
        }

        public DownloadImportReceipt ReadReceipt(TrackedDownload trackedDownload, DownloadHistory history)
        {
            try
            {
                if (!ValidContext(trackedDownload) || history == null || history.EventType != DownloadHistoryEventType.DownloadImported ||
                    history.DownloadId != trackedDownload.DownloadItem.DownloadId || history.DownloadClientId != trackedDownload.DownloadClient)
                {
                    return null;
                }

                // Resolve remote path mappings before comparing the stored
                // local root, including immediately after a restart.
                var currentSources = _sources.Scan(trackedDownload);
                var json = DataValue(history.Data, DownloadHistoryService.ImportReceiptKey);
                if (json == null || json.Length > DownloadHistoryService.MaximumReceiptLength || !HasExplicitHeader(json) || !STJson.TryDeserialize<DownloadImportReceipt>(json, out var receipt) ||
                    receipt == null || receipt.Version != 1 || receipt.AuthorId <= 0 || receipt.AuthorId != history.AuthorId ||
                    (trackedDownload.RemoteBook?.Author?.Id > 0 && trackedDownload.RemoteBook.Author.Id != receipt.AuthorId) || receipt.DownloadId != history.DownloadId ||
                    receipt.DownloadClientId != history.DownloadClientId || !SamePath(receipt.OutputPath, OutputPath(trackedDownload)) ||
                    receipt.BookIds == null || receipt.BookIds.Count == 0 || receipt.BookIds.Count > 4096 || receipt.BookIds.Any(id => id <= 0) ||
                    receipt.BookIds.Distinct().Count() != receipt.BookIds.Count || receipt.Files == null || receipt.Files.Count == 0 || receipt.Files.Count > 10000 ||
                    receipt.Files.Any(file => file == null || !WithinOutput(trackedDownload, file.SourcePath)))
                {
                    return null;
                }

                var grab = _downloadHistory.GetLatestGrab(history.DownloadId, trackedDownload.DownloadClient);
                if (grab != null && grab.Date > history.Date)
                {
                    return null;
                }

                var expectedBooks = ExpectedBooks(trackedDownload, BoundHistory(trackedDownload));
                if (expectedBooks == null || (expectedBooks.Count > 0 && !expectedBooks.SetEquals(receipt.BookIds)) ||
                    !receipt.BookIds.ToHashSet().SetEquals(receipt.Files.Select(file => file.BookId)) || !ValidParts(receipt.Files))
                {
                    return null;
                }

                foreach (var file in receipt.Files)
                {
                    if (!ValidLiveFile(file, out var authorId) || authorId != receipt.AuthorId)
                    {
                        return null;
                    }
                }

                if (currentSources == null || currentSources.Any(source => !receipt.Files.Any(file =>
                    SamePath(file.SourcePath, source.Path) && file.SourceSize == source.Size &&
                    file.SourceModifiedUtc.ToUniversalTime() == source.ModifiedUtc.ToUniversalTime())))
                {
                    return null;
                }

                if (!_sources.Validate(trackedDownload, receipt))
                {
                    return null;
                }

                receipt.KeepSourceFiles |= _downloadHistory.KeepSources(trackedDownload);
                return receipt;
            }
            catch (Exception)
            {
                _logger.Debug("Persisted download completion could not be verified");
                return null;
            }
        }

        private List<EntityHistory> BoundHistory(TrackedDownload trackedDownload)
        {
            return (_history.FindByDownloadId(trackedDownload.DownloadItem.DownloadId, trackedDownload.DownloadClient) ?? new List<EntityHistory>())
                .OrderByDescending(history => history.Date).ThenByDescending(history => history.Id).ToList();
        }

        private HashSet<int> ExpectedBooks(TrackedDownload trackedDownload, List<EntityHistory> history)
        {
            var remoteBooks = trackedDownload.RemoteBook?.Books;
            if (remoteBooks?.Any(book => book == null || book.Id <= 0) == true)
            {
                return null;
            }

            var grab = _downloadHistory.GetLatestGrab(trackedDownload.DownloadItem.DownloadId, trackedDownload.DownloadClient);
            if (grab != null && grab.DownloadClientId != trackedDownload.DownloadClient)
            {
                return null;
            }

            IEnumerable<int> grabbedIds;
            var scope = DataValue(grab?.Data, "bookIds");
            if (scope != null)
            {
                if (scope.Length > 65536 || !STJson.TryDeserialize<List<int>>(scope, out var ids) ||
                    ids == null || ids.Count == 0 || ids.Count > 4096 || ids.Any(id => id <= 0))
                {
                    return null;
                }

                grabbedIds = ids;
            }
            else
            {
                // Older grabs lack a download-level scope. Use their latest
                // contiguous grab group, not every earlier attempt with this ID.
                // No time tolerance is needed between synchronous subscribers.
                grabbedIds = history.SkipWhile(item => item.EventType != EntityHistoryEventType.Grabbed)
                    .TakeWhile(item => item.EventType == EntityHistoryEventType.Grabbed)
                    .Where(item => item.BookId > 0).Select(item => item.BookId);
            }

            return (remoteBooks?.Select(book => book.Id) ?? Enumerable.Empty<int>()).Concat(grabbedIds).ToHashSet();
        }

        private DownloadImportFile HistoricalFile(DownloadImportSourceFile source, List<EntityHistory> history, DateTime? latestGrab)
        {
            foreach (var row in history.Where(row => row.EventType == EntityHistoryEventType.BookFileImported &&
                (!latestGrab.HasValue || row.Date >= latestGrab.Value) && SamePath(DataValue(row.Data, "droppedPath"), source.Path)))
            {
                if (!int.TryParse(DataValue(row.Data, "fileId"), NumberStyles.None, CultureInfo.InvariantCulture, out var fileId) || fileId <= 0 ||
                    !long.TryParse(DataValue(row.Data, "size"), NumberStyles.None, CultureInfo.InvariantCulture, out var size) || size != source.Size)
                {
                    continue;
                }

                var modified = DataValue(row.Data, "sourceModifiedUtc");
                if (modified != null && (!DateTime.TryParse(modified, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var date) ||
                    date.ToUniversalTime() != source.ModifiedUtc.ToUniversalTime()))
                {
                    continue;
                }

                // A legacy source with a later modification cannot borrow an
                // old file ID merely because its path and length still match.
                if (modified == null && source.ModifiedUtc.ToUniversalTime() > row.Date.ToUniversalTime())
                {
                    continue;
                }

                var live = _files.Get(fileId);
                if (live == null || (row.AuthorId > 0 && live.Author?.Value?.Id != row.AuthorId))
                {
                    continue;
                }

                var edition = IntegerData(row.Data, "editionId", live.EditionId);
                var part = IntegerData(row.Data, "part", live.Part);
                var count = IntegerData(row.Data, "partCount", 0);
                return new DownloadImportFile
                {
                    BookId = row.BookId,
                    EditionId = edition,
                    FileId = fileId,
                    Part = part,
                    PartCount = count,
                    SourcePath = source.Path,
                    ImportedPath = DataValue(row.Data, "importedPath"),
                    SourceSize = source.Size,
                    SourceModifiedUtc = source.ModifiedUtc.ToUniversalTime()
                };
            }

            return null;
        }

        private bool ValidLiveFile(DownloadImportFile evidence, out int authorId)
        {
            authorId = 0;
            if (evidence == null || evidence.BookId <= 0 || evidence.EditionId <= 0 || evidence.FileId <= 0 || evidence.Part < 0 ||
                evidence.SourceSize <= 0 || evidence.SourceModifiedUtc == DateTime.MinValue ||
                string.IsNullOrWhiteSpace(evidence.SourcePath) || string.IsNullOrWhiteSpace(evidence.ImportedPath) ||
                SamePath(evidence.SourcePath, evidence.ImportedPath))
            {
                return false;
            }

            var file = _files.Get(evidence.FileId);
            if (file == null || file.Id != evidence.FileId || file.EditionId != evidence.EditionId || file.Part != evidence.Part ||
                file.Edition?.Value?.BookId != evidence.BookId || !SamePath(file.Path, evidence.ImportedPath) ||
                file.Author?.Value?.Id is not > 0 || !_disk.FileExists(file.Path) || _disk.GetFileSize(file.Path) <= 0)
            {
                return false;
            }

            authorId = file.Author.Value.Id;
            return true;
        }

        private static bool MatchesCurrentDecisions(DownloadImportFile evidence, List<ImportResult> decisions)
        {
            foreach (var decision in decisions)
            {
                var item = decision.ImportDecision.Item;
                if ((item.Book?.Id > 0 && item.Book.Id != evidence.BookId) ||
                    (item.Edition?.Id > 0 && item.Edition.Id != evidence.EditionId) ||
                    ((item.Book?.Id > 0 || item.Edition?.Id > 0) && item.Part != evidence.Part) ||
                    (item.Size > 0 && item.Size != evidence.SourceSize) ||
                    (item.Modified != DateTime.MinValue && item.Modified.ToUniversalTime() != evidence.SourceModifiedUtc.ToUniversalTime()) ||
                    item.PartCount < 0 || item.PartCount > 10000)
                {
                    return false;
                }

                if (item.PartCount > 0)
                {
                    if (evidence.PartCount > 0 && evidence.PartCount != item.PartCount)
                    {
                        return false;
                    }

                    // Even a skipped/rejected decision can declare a missing
                    // part. Legacy history must not erase that current total.
                    evidence.PartCount = item.PartCount;
                }
            }

            return true;
        }

        private static bool ValidParts(List<DownloadImportFile> files, bool normalize = false)
        {
            if (files.Any(file => file.PartCount < 0 || (!normalize && file.PartCount == 0)) ||
                files.Select(file => file.FileId).Distinct().Count() != files.Count ||
                files.Select(file => file.SourcePath).Distinct(PathEqualityComparer.Instance).Count() != files.Count ||
                files.Select(file => file.ImportedPath).Distinct(PathEqualityComparer.Instance).Count() != files.Count)
            {
                return false;
            }

            foreach (var book in files.GroupBy(file => file.BookId))
            {
                if (book.Select(file => file.EditionId).Distinct().Count() != 1)
                {
                    return false;
                }

                var declaredCounts = book.Where(file => file.PartCount > 0).Select(file => file.PartCount).Distinct().ToList();
                if (declaredCounts.Count > 1)
                {
                    return false;
                }

                var parts = book.Select(file => file.Part).ToList();
                var nonAudioFormats = book.All(file => !MediaFileExtensions.AudioExtensions.Contains(Path.GetExtension(file.SourcePath)));
                var count = declaredCounts.SingleOrDefault();
                if (nonAudioFormats && parts.All(part => part == 0))
                {
                    if (count > 1)
                    {
                        return false;
                    }

                    foreach (var file in book.Where(_ => normalize))
                    {
                        file.PartCount = 1;
                    }

                    continue;
                }

                count = count == 0 ? parts.Count : count;
                if (count > 10000 || count != parts.Count || (count == 1 && parts[0] == 0 ? false :
                    !parts.OrderBy(part => part).SequenceEqual(Enumerable.Range(1, count))))
                {
                    return false;
                }

                foreach (var file in book.Where(_ => normalize))
                {
                    file.PartCount = count;
                }
            }

            return true;
        }

        public static string DataValue(Dictionary<string, string> data, string name)
        {
            return data?.Where(pair => string.Equals(pair.Key, name, StringComparison.OrdinalIgnoreCase)).Select(pair => pair.Value).SingleOrDefault();
        }

        private static int IntegerData(Dictionary<string, string> data, string name, int fallback)
        {
            var value = DataValue(data, name);
            return value == null ? fallback : int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var number) ? number : -1;
        }

        private static bool ValidContext(TrackedDownload trackedDownload)
        {
            return trackedDownload?.DownloadItem?.DownloadId.IsNotNullOrWhiteSpace() == true && trackedDownload.DownloadClient > 0 &&
                (trackedDownload.DownloadItem.DownloadClientInfo?.Id is not > 0 || trackedDownload.DownloadItem.DownloadClientInfo.Id == trackedDownload.DownloadClient) &&
                OutputPath(trackedDownload).IsNotNullOrWhiteSpace();
        }

        private static string OutputPath(TrackedDownload trackedDownload)
        {
            var mapped = trackedDownload.ImportItem?.OutputPath.FullPath;
            return mapped.IsNotNullOrWhiteSpace() ? mapped : trackedDownload.DownloadItem?.OutputPath.FullPath;
        }

        private static bool SamePath(string first, string second)
        {
            return first.IsNotNullOrWhiteSpace() && second.IsNotNullOrWhiteSpace() && first.PathEquals(second);
        }

        private static bool WithinOutput(TrackedDownload trackedDownload, string source)
        {
            return source.IsNotNullOrWhiteSpace() && (SamePath(OutputPath(trackedDownload), source) || OutputPath(trackedDownload).IsParentPath(source));
        }

        private static bool StoredKeepSourceFiles(TrackedDownload trackedDownload, DownloadHistory history)
        {
            if (history == null || history.DownloadId != trackedDownload.DownloadItem.DownloadId || history.DownloadClientId != trackedDownload.DownloadClient)
            {
                return false;
            }

            try
            {
                var json = DataValue(history.Data, DownloadHistoryService.ImportReceiptKey);
                if (json == null || json.Length > DownloadHistoryService.MaximumReceiptLength)
                {
                    return true;
                }

                using var document = JsonDocument.Parse(json);
                if (document.RootElement.ValueKind != JsonValueKind.Object)
                {
                    return true;
                }

                var paths = document.RootElement.EnumerateObject()
                    .Where(property => string.Equals(property.Name, "outputPath", StringComparison.OrdinalIgnoreCase)).ToList();
                if (paths.Count == 1 && paths[0].Value.ValueKind == JsonValueKind.String &&
                    paths[0].Value.GetString().IsNotNullOrWhiteSpace() && !SamePath(paths[0].Value.GetString(), OutputPath(trackedDownload)))
                {
                    // A known different output root has its own copy policy.
                    return false;
                }

                if (!HasExplicitHeader(json) || !STJson.TryDeserialize<DownloadImportReceipt>(json, out var receipt) ||
                    receipt == null || receipt.Version != 1 || receipt.DownloadId != history.DownloadId ||
                    receipt.DownloadClientId != history.DownloadClientId || !SamePath(receipt.OutputPath, OutputPath(trackedDownload)))
                {
                    return true;
                }

                return receipt.KeepSourceFiles;
            }
            catch (Exception)
            {
                // An own legacy/corrupt completion cannot silently turn an
                // unknown earlier Copy choice into permission to delete data.
                return true;
            }
        }

        private static bool HasExplicitHeader(string json)
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            var versions = document.RootElement.EnumerateObject()
                .Where(property => string.Equals(property.Name, "version", StringComparison.OrdinalIgnoreCase)).ToList();
            var keepSourceFiles = document.RootElement.EnumerateObject()
                .Where(property => string.Equals(property.Name, "keepSourceFiles", StringComparison.OrdinalIgnoreCase)).ToList();
            return versions.Count == 1 && versions[0].Value.ValueKind == JsonValueKind.Number &&
                versions[0].Value.TryGetInt32(out var version) && version == 1 && keepSourceFiles.Count == 1 &&
                keepSourceFiles[0].Value.ValueKind is JsonValueKind.True or JsonValueKind.False;
        }
    }
}
