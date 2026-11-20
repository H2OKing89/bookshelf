using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using NLog;
using NzbDrone.Common;
using NzbDrone.Common.Crypto;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Instrumentation.Extensions;
using NzbDrone.Core.Books;
using NzbDrone.Core.CustomFormats;
using NzbDrone.Core.DecisionEngine;
using NzbDrone.Core.Download;
using NzbDrone.Core.Download.History;
using NzbDrone.Core.Download.TrackedDownloads;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.Parser;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.RootFolders;

namespace NzbDrone.Core.MediaFiles.BookImport.Manual
{
    public interface IManualImportService
    {
        List<ManualImportItem> GetMediaFiles(string path, string downloadId, Author author, FilterFilesType filter, bool replaceExistingFiles, int? downloadClientId = null);
        List<ManualImportItem> UpdateItems(List<ManualImportItem> item);
    }

    public class ManualImportService : IExecute<ManualImportCommand>, IManualImportService
    {
        private readonly IDiskProvider _diskProvider;
        private readonly IParsingService _parsingService;
        private readonly IRootFolderService _rootFolderService;
        private readonly IDiskScanService _diskScanService;
        private readonly IMakeImportDecision _importDecisionMaker;
        private readonly IAuthorService _authorService;
        private readonly IBookService _bookService;
        private readonly IEditionService _editionService;
        private readonly IProvideBookInfo _bookInfo;
        private readonly IMetadataTagService _metadataTagService;
        private readonly IImportApprovedBooks _importApprovedBooks;
        private readonly ICustomFormatCalculationService _formatCalculator;
        private readonly ITrackedDownloadService _trackedDownloadService;
        private readonly ICompletedDownloadService _completedDownloadService;
        private readonly IDownloadHistoryService _downloadHistoryService;
        private readonly IProvideImportItemService _provideImportItemService;
        private readonly Logger _logger;

        public ManualImportService(IDiskProvider diskProvider,
                                   IParsingService parsingService,
                                   IRootFolderService rootFolderService,
                                   IDiskScanService diskScanService,
                                   IMakeImportDecision importDecisionMaker,
                                   IAuthorService authorService,
                                   IBookService bookService,
                                   IEditionService editionService,
                                   IProvideBookInfo bookInfo,
                                   IMetadataTagService metadataTagService,
                                   IImportApprovedBooks importApprovedBooks,
                                   ICustomFormatCalculationService formatCalculator,
                                   ITrackedDownloadService trackedDownloadService,
                                   ICompletedDownloadService completedDownloadService,
                                   IDownloadHistoryService downloadHistoryService,
                                   IProvideImportItemService provideImportItemService,
                                   Logger logger)
        {
            _diskProvider = diskProvider;
            _parsingService = parsingService;
            _rootFolderService = rootFolderService;
            _diskScanService = diskScanService;
            _importDecisionMaker = importDecisionMaker;
            _authorService = authorService;
            _bookService = bookService;
            _editionService = editionService;
            _bookInfo = bookInfo;
            _metadataTagService = metadataTagService;
            _importApprovedBooks = importApprovedBooks;
            _formatCalculator = formatCalculator;
            _trackedDownloadService = trackedDownloadService;
            _completedDownloadService = completedDownloadService;
            _downloadHistoryService = downloadHistoryService;
            _provideImportItemService = provideImportItemService;
            _logger = logger;
        }

        public List<ManualImportItem> GetMediaFiles(string path, string downloadId, Author author, FilterFilesType filter, bool replaceExistingFiles, int? downloadClientId = null)
        {
            ValidateDownloadScope(downloadId, downloadClientId);
            if (downloadId.IsNotNullOrWhiteSpace())
            {
                var trackedDownload = FindTrackedDownload(downloadId, downloadClientId);

                if (trackedDownload == null)
                {
                    return new List<ManualImportItem>();
                }

                if (trackedDownload.ImportItem == null)
                {
                    trackedDownload.ImportItem = _provideImportItemService.ProvideImportItem(trackedDownload.DownloadItem, trackedDownload.ImportItem);
                }

                path = trackedDownload.ImportItem.OutputPath.FullPath;
                downloadClientId = trackedDownload.DownloadClient;
            }

            if (!_diskProvider.FolderExists(path))
            {
                if (!_diskProvider.FileExists(path))
                {
                    return new List<ManualImportItem>();
                }

                var files = new List<IFileInfo> { _diskProvider.GetFileInfo(path) };

                var config = new ImportDecisionMakerConfig
                {
                    Filter = FilterFilesType.None,
                    NewDownload = true,
                    SingleRelease = false,
                    IncludeExisting = !replaceExistingFiles,
                    AddNewAuthors = false,
                    KeepAllEditions = true,
                    PreserveExistingReleaseGroups = true
                };

                var decision = _importDecisionMaker.GetImportDecisions(files, null, null, config);
                var result = MapItem(decision.First(), downloadId, replaceExistingFiles, false, downloadClientId);

                return new List<ManualImportItem> { result };
            }

            return ProcessFolder(path, downloadId, author, filter, replaceExistingFiles, downloadClientId);
        }

        private List<ManualImportItem> ProcessFolder(string folder, string downloadId, Author author, FilterFilesType filter, bool replaceExistingFiles, int? downloadClientId = null)
        {
            DownloadClientItem downloadClientItem = null;
            var directoryInfo = new DirectoryInfo(folder);
            author = author ?? _parsingService.GetAuthor(directoryInfo.Name);

            if (downloadId.IsNotNullOrWhiteSpace())
            {
                var trackedDownload = FindTrackedDownload(downloadId, downloadClientId);
                downloadClientItem = trackedDownload?.DownloadItem;

                if (author == null)
                {
                    author = trackedDownload?.RemoteBook?.Author;
                }
            }

            var authorFiles = _diskScanService.GetBookFiles(folder).ToList();
            var idOverrides = new IdentificationOverrides
            {
                Author = author
            };
            var itemInfo = new ImportDecisionMakerInfo
            {
                DownloadClientItem = downloadClientItem,
                ParsedBookInfo = Parser.Parser.ParseBookTitle(directoryInfo.Name)
            };
            var config = new ImportDecisionMakerConfig
            {
                Filter = filter,
                NewDownload = true,
                SingleRelease = false,
                IncludeExisting = !replaceExistingFiles,
                AddNewAuthors = false,
                KeepAllEditions = true,
                PreserveExistingReleaseGroups = true
            };

            var decisions = _importDecisionMaker.GetImportDecisions(authorFiles, idOverrides, itemInfo, config);

            // paths will be different for new and old files which is why we need to map separately
            var newFiles = authorFiles.Join(decisions,
                                            f => f.FullName,
                                            d => d.Item.Path,
                                            (f, d) => new { File = f, Decision = d },
                                            PathEqualityComparer.Instance);

            var newItems = newFiles.Select(x => MapItem(x.Decision, downloadId, replaceExistingFiles, false, downloadClientId));
            var existingDecisions = decisions.Except(newFiles.Select(x => x.Decision));
            var existingItems = existingDecisions.Select(x => MapItem(x, null, replaceExistingFiles, false));

            return newItems.Concat(existingItems).ToList();
        }

        public List<ManualImportItem> UpdateItems(List<ManualImportItem> items)
        {
            foreach (var item in items)
            {
                ValidateDownloadScope(item.DownloadId, item.DownloadClientId);
            }

            var replaceExistingFiles = items.All(x => x.ReplaceExistingFiles);
            var groupedItems = items.Where(x => !x.AdditionalFile).GroupBy(x => new { BookId = x.Book?.Id, x.DownloadId, x.DownloadClientId });
            _logger.Debug($"UpdateItems, {groupedItems.Count()} groups, replaceExisting {replaceExistingFiles}");

            var result = new List<ManualImportItem>();

            foreach (var group in groupedItems)
            {
                _logger.Debug("UpdateItems, group key: {0}", group.Key);

                var disableReleaseSwitching = group.First().DisableReleaseSwitching;

                var files = group.Select(x => _diskProvider.GetFileInfo(x.Path)).ToList();
                var idOverride = new IdentificationOverrides
                {
                    Author = group.First().Author,
                    Book = group.First().Book,
                    Edition = group.First().Edition
                };
                var config = new ImportDecisionMakerConfig
                {
                    Filter = FilterFilesType.None,
                    NewDownload = true,
                    SingleRelease = true,
                    IncludeExisting = !replaceExistingFiles,
                    AddNewAuthors = false,
                    PreserveExistingReleaseGroups = true,
                    ReleaseGroupOverrides = group.Where(x => x.ReleaseGroupOverride != null)
                        .ToDictionary(x => x.Path, x => x.ReleaseGroupOverride, PathEqualityComparer.Instance)
                };
                var decisions = _importDecisionMaker.GetImportDecisions(files, idOverride, null, config);

                var existingItems = group.Join(decisions,
                                               i => i.Path,
                                               d => d.Item.Path,
                                               (i, d) => new { Item = i, Decision = d },
                                               PathEqualityComparer.Instance);

                foreach (var pair in existingItems)
                {
                    var item = pair.Item;
                    var decision = pair.Decision;

                    if (decision.Item.Author != null)
                    {
                        item.Author = decision.Item.Author;
                    }

                    if (decision.Item.Book != null)
                    {
                        item.Book = decision.Item.Book;
                        item.Edition = decision.Item.Edition;
                    }

                    if (item.Quality?.Quality == Quality.Unknown)
                    {
                        item.Quality = decision.Item.Quality;
                    }

                    item.ReleaseGroup = decision.Item.ReleaseGroup;
                    item.ReleaseGroupOverride = decision.Item.ReleaseGroupOverride;
                    item.CustomFormats = decision.Item.Author == null
                        ? new List<CustomFormat>()
                        : _formatCalculator.ParseCustomFormat(decision.Item);

                    item.Rejections = decision.Rejections;
                    item.Size = decision.Item.Size;

                    result.Add(item);
                }

                var newDecisions = decisions.Except(existingItems.Select(x => x.Decision));
                result.AddRange(newDecisions.Select(x => MapItem(x, null, replaceExistingFiles, disableReleaseSwitching)));
            }

            return result;
        }

        private ManualImportItem MapItem(ImportDecision<LocalBook> decision, string downloadId, bool replaceExistingFiles, bool disableReleaseSwitching, int? downloadClientId = null)
        {
            var item = new ManualImportItem();

            item.Id = HashConverter.GetHashInt31(downloadClientId.HasValue ? $"{downloadClientId}:{downloadId}:{decision.Item.Path}" : decision.Item.Path);
            item.Path = decision.Item.Path;
            item.Name = Path.GetFileNameWithoutExtension(decision.Item.Path);
            item.DownloadId = downloadId;
            item.DownloadClientId = downloadClientId;

            if (decision.Item.Author != null)
            {
                item.Author = decision.Item.Author;

                item.CustomFormats = _formatCalculator.ParseCustomFormat(decision.Item);
            }

            if (decision.Item.Book != null)
            {
                item.Book = decision.Item.Book;
                item.Edition = decision.Item.Edition;
            }

            item.Quality = decision.Item.Quality;
            item.ReleaseGroup = decision.Item.ReleaseGroup;
            item.ReleaseGroupOverride = decision.Item.ReleaseGroupOverride;
            item.IndexerFlags = (int)decision.Item.IndexerFlags;
            item.Size = _diskProvider.GetFileSize(decision.Item.Path);
            item.Rejections = decision.Rejections;
            item.Tags = decision.Item.FileTrackInfo;
            item.AdditionalFile = decision.Item.AdditionalFile;
            item.ReplaceExistingFiles = replaceExistingFiles;
            item.DisableReleaseSwitching = disableReleaseSwitching;

            return item;
        }

        public void Execute(ManualImportCommand message)
        {
            _logger.ProgressTrace("Manually importing {0} files using mode {1}", message.Files.Count, message.ImportMode);

            // Resolve every selected client before retention writes, transfers, or metadata changes.
            foreach (var file in message.Files)
            {
                ValidateDownloadScope(file.DownloadId, file.DownloadClientId);
            }

            foreach (var file in message.Files.Where(file => file.DownloadId.IsNotNullOrWhiteSpace()))
            {
                var tracked = FindTrackedDownload(file.DownloadId, file.DownloadClientId);
                if (tracked == null)
                {
                    throw new InvalidOperationException("The selected download is no longer tracked");
                }

                file.DownloadClientId = tracked.DownloadClient;
            }

            if (message.ImportMode == ImportMode.Copy)
            {
                foreach (var identity in message.Files.Where(file => file.DownloadId.IsNotNullOrWhiteSpace()).Select(file => new { file.DownloadId, file.DownloadClientId }).Distinct())
                {
                    var tracked = FindTrackedDownload(identity.DownloadId, identity.DownloadClientId);
                    if (tracked == null)
                    {
                        throw new InvalidOperationException("The selected download is no longer tracked");
                    }

                    tracked.ImportItem = _provideImportItemService.ProvideImportItem(tracked.DownloadItem, tracked.ImportItem);
                    if (tracked.ImportItem == null)
                    {
                        throw new InvalidOperationException("The local download import path could not be determined");
                    }

                    _downloadHistoryService.RecordSourceRetention(tracked);
                    tracked.KeepSourceFiles = true;
                }
            }

            var imported = new List<ImportResult>();
            var importedTrackedDownload = new List<ManuallyImportedFile>();
            var bookIds = message.Files.GroupBy(file => new
            {
                file.BookId,
                DownloadId = file.DownloadId.IsNullOrWhiteSpace() ? null : file.DownloadId,
                file.DownloadClientId
            }).ToList();
            var fileCount = 0;

            foreach (var importBookId in bookIds)
            {
                var bookImportDecisions = new List<ImportDecision<LocalBook>>();

                // turn off anyReleaseOk if specified
                if (importBookId.First().DisableReleaseSwitching)
                {
                    var book = _bookService.GetBook(importBookId.First().BookId);
                    book.AnyEditionOk = false;
                    _bookService.UpdateBook(book);
                }

                foreach (var file in importBookId)
                {
                    _logger.ProgressTrace("Processing file {0} of {1}", fileCount + 1, message.Files.Count);

                    var author = _authorService.GetAuthor(file.AuthorId);
                    var book = _bookService.GetBook(file.BookId);

                    var edition = _editionService.GetEditionByForeignEditionId(file.ForeignEditionId);
                    if (edition == null)
                    {
                        var tuple = _bookInfo.GetBookInfo(book.ForeignBookId);
                        edition = tuple.Item2.Editions.Value.SingleOrDefault(x => x.ForeignEditionId == file.ForeignEditionId);
                    }

                    var fileRootFolder = _rootFolderService.GetBestRootFolder(file.Path);
                    var fileInfo = _diskProvider.GetFileInfo(file.Path);
                    var fileTrackInfo = _metadataTagService.ReadTags(fileInfo) ?? new ParsedTrackInfo();

                    var localTrack = new LocalBook
                    {
                        ExistingFile = fileRootFolder != null,
                        FileTrackInfo = fileTrackInfo,
                        Path = file.Path,
                        Part = fileTrackInfo.TrackNumbers.Any() ? fileTrackInfo.TrackNumbers.First() : 1,
                        PartCount = importBookId.Count(),
                        Size = fileInfo.Length,
                        Modified = fileInfo.LastWriteTimeUtc,
                        Quality = file.Quality,
                        ReleaseGroup = file.ReleaseGroup?.Trim() ?? DetectReleaseGroup(file, fileTrackInfo),
                        ReleaseGroupOverride = file.ReleaseGroup?.Trim(),
                        IndexerFlags = (IndexerFlags)file.IndexerFlags,
                        Author = author,
                        Book = book,
                        Edition = edition
                    };

                    var importDecision = new ImportDecision<LocalBook>(localTrack);
                    if (_rootFolderService.GetBestRootFolder(author.Path) == null)
                    {
                        _logger.Warn($"Destination author folder {author.Path} not in a Root Folder, skipping import");
                        importDecision.Reject(new Rejection($"Destination author folder {author.Path} is not in a Root Folder", code: RejectionCode.RootFolderMissing));
                    }

                    bookImportDecisions.Add(importDecision);
                    fileCount += 1;
                }

                var downloadId = importBookId.Select(x => x.DownloadId).FirstOrDefault(x => x.IsNotNullOrWhiteSpace());
                if (downloadId.IsNullOrWhiteSpace())
                {
                    imported.AddRange(_importApprovedBooks.Import(bookImportDecisions, message.ReplaceExistingFiles, null, message.ImportMode));
                }
                else
                {
                    var trackedDownload = FindTrackedDownload(downloadId, importBookId.Key.DownloadClientId);
                    trackedDownload.KeepSourceFiles |= message.ImportMode == ImportMode.Copy;
                    var importResults = _importApprovedBooks.Import(bookImportDecisions, message.ReplaceExistingFiles, trackedDownload.DownloadItem, message.ImportMode);

                    imported.AddRange(importResults);

                    foreach (var importResult in importResults)
                    {
                        importedTrackedDownload.Add(new ManuallyImportedFile
                        {
                            TrackedDownload = trackedDownload,
                            ImportResult = importResult
                        });
                    }
                }
            }

            _logger.ProgressTrace("Manually imported {0} files", imported.Count);

            foreach (var groupedTrackedDownload in importedTrackedDownload.GroupBy(i => new { i.TrackedDownload.DownloadClient, i.TrackedDownload.DownloadItem.DownloadId }).ToList())
            {
                var trackedDownload = groupedTrackedDownload.First().TrackedDownload;
                trackedDownload.KeepSourceFiles |= message.ImportMode == ImportMode.Copy;
                _completedDownloadService.VerifyImport(
                    trackedDownload,
                    groupedTrackedDownload.Select(item => item.ImportResult).ToList());
            }
        }

        private static void ValidateDownloadScope(string downloadId, int? downloadClientId)
        {
            if (downloadClientId.HasValue && (downloadClientId.Value <= 0 || downloadId.IsNullOrWhiteSpace()))
            {
                throw new ArgumentException("A positive download client ID requires a download identifier.", nameof(downloadClientId));
            }
        }

        private TrackedDownload FindTrackedDownload(string downloadId, int? downloadClientId)
        {
            return downloadClientId.HasValue
                ? _trackedDownloadService.Find(downloadId, downloadClientId.Value)
                : _trackedDownloadService.Find(downloadId);
        }

        private string DetectReleaseGroup(ManualImportFile file, ParsedTrackInfo fileInfo)
        {
            var trackedDownload = file.DownloadId.IsNullOrWhiteSpace() ? null : FindTrackedDownload(file.DownloadId, file.DownloadClientId);
            var group = trackedDownload?.RemoteBook?.ParsedBookInfo?.ReleaseGroup;

            if (group.IsNullOrWhiteSpace() && trackedDownload?.DownloadItem?.Title.IsNotNullOrWhiteSpace() == true)
            {
                group = Parser.Parser.ParseReleaseGroup(trackedDownload.DownloadItem.Title);
            }

            if (group.IsNullOrWhiteSpace())
            {
                group = Parser.Parser.ParseReleaseGroup(Path.GetFileName(Path.GetDirectoryName(file.Path)) ?? string.Empty);
            }

            if (group.IsNullOrWhiteSpace())
            {
                group = fileInfo.ReleaseGroup;
            }

            return group.IsNullOrWhiteSpace() ? Parser.Parser.ParseReleaseGroup(Path.GetFileName(file.Path)) : group;
        }
    }
}
