using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Serializer;
using NzbDrone.Core.Books;
using NzbDrone.Core.DecisionEngine;
using NzbDrone.Core.Download;
using NzbDrone.Core.Download.History;
using NzbDrone.Core.Download.TrackedDownloads;
using NzbDrone.Core.History;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.BookImport;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Download
{
    [TestFixture]
    public class DownloadImportCompletionServiceFixture : CoreTest<DownloadImportCompletionService>
    {
        private TrackedDownload _tracked;
        private List<DownloadImportSourceFile> _sources;
        private List<ImportResult> _results;
        private List<EntityHistory> _history;
        private Dictionary<int, BookFile> _files;
        private DownloadHistory _marker;
        private DownloadHistory _completionMarker;
        private DownloadHistory _grab;
        private DateTime _modified;

        [SetUp]
        public void Setup()
        {
            _marker = null;
            _completionMarker = null;
            _grab = null;
            _modified = new DateTime(2026, 11, 18, 1, 2, 3, DateTimeKind.Utc);
            _tracked = new TrackedDownload
            {
                DownloadClient = 1,
                DownloadItem = new DownloadClientItem
                {
                    DownloadId = "owned-download",
                    OutputPath = new OsPath(Path.Combine(TempFolder, "download")),
                    DownloadClientInfo = new DownloadClientItemClientInfo { Id = 1 }
                },
                RemoteBook = new RemoteBook { Author = new Author { Id = 1 }, Books = new List<Book> { new Book { Id = 10 } } }
            };
            _sources = new List<DownloadImportSourceFile>();
            _results = new List<ImportResult>();
            _history = new List<EntityHistory>();
            _files = new Dictionary<int, BookFile>();
            Mocker.GetMock<IDownloadImportSourceService>().Setup(service => service.Scan(It.IsAny<TrackedDownload>())).Returns(() => _sources);
            Mocker.GetMock<IDownloadImportSourceService>().Setup(service => service.Validate(It.IsAny<TrackedDownload>(), It.IsAny<DownloadImportReceipt>())).Returns(true);
            Mocker.GetMock<IHistoryService>().Setup(service => service.FindByDownloadId("owned-download")).Returns(() => _history);
            Mocker.GetMock<IDownloadHistoryService>().Setup(service => service.GetLatestDownloadHistoryItem("owned-download")).Returns(() => _marker);
            Mocker.GetMock<IDownloadHistoryService>().Setup(service => service.GetLatestImportCompletion("owned-download", 1)).Returns(() => _completionMarker);
            Mocker.GetMock<IDownloadHistoryService>().Setup(service => service.GetLatestGrab("owned-download")).Returns(() => _grab);
            Mocker.GetMock<IMediaFileService>().Setup(service => service.Get(It.IsAny<int>())).Returns<int>(id => _files.GetValueOrDefault(id));
            Mocker.GetMock<IDiskProvider>().Setup(disk => disk.FileExists(It.IsAny<string>())).Returns<string>(path => _files.Values.Any(file => file.Path == path));
            Mocker.GetMock<IDiskProvider>().Setup(disk => disk.GetFileSize(It.IsAny<string>())).Returns(100);
            Mocker.GetMock<IDownloadHistoryService>().Setup(service => service.RecordImportCompletion(It.IsAny<TrackedDownload>(), It.IsAny<DownloadImportReceipt>()))
                .Returns<TrackedDownload, DownloadImportReceipt>((tracked, receipt) => _marker = _completionMarker = Marker(receipt));
            AddPart(1, 1);
        }

        private void AddPart(int part, int count, int bookId = 10, int editionId = 20)
        {
            var id = _files.Count + 30;
            var source = new DownloadImportSourceFile
            {
                Path = Path.Combine(TempFolder, "download", "part-" + id + ".m4b"), Size = 100, ModifiedUtc = _modified
            };
            var file = new BookFile
            {
                Id = id, EditionId = editionId, Part = part, Path = Path.Combine(TempFolder, "library", "part-" + id + ".m4b"),
                Author = new Author { Id = 1 }, Edition = new Edition { Id = editionId, BookId = bookId }
            };
            _files.Add(id, file);
            _sources.Add(source);
            _results.Add(new ImportResult(new ImportDecision<LocalBook>(new LocalBook
            {
                Path = source.Path, Size = source.Size, Modified = source.ModifiedUtc,
                Author = new Author { Id = 1 }, Book = new Book { Id = bookId }, Edition = new Edition { Id = editionId },
                Part = part, PartCount = count
            })) { ImportedFile = file });
        }

        private DownloadHistory Marker(DownloadImportReceipt receipt)
        {
            return new DownloadHistory
            {
                Id = 100, AuthorId = receipt.AuthorId, DownloadId = "owned-download", DownloadClientId = 1,
                EventType = DownloadHistoryEventType.DownloadImported, Date = _modified.AddHours(1),
                Data = new Dictionary<string, string> { [DownloadHistoryService.ImportReceiptKey] = STJson.ToJson(receipt) }
            };
        }

        private EntityHistory ImportedHistory(int index, bool legacy = false, bool lowerCase = true)
        {
            var result = _results[index];
            var item = result.ImportDecision.Item;
            var values = new Dictionary<string, string>
            {
                ["FileId"] = result.ImportedFile.Id.ToString(CultureInfo.InvariantCulture), ["DroppedPath"] = item.Path,
                ["ImportedPath"] = result.ImportedFile.Path, ["Size"] = item.Size.ToString(CultureInfo.InvariantCulture)
            };
            if (!legacy)
            {
                values.Add("EditionId", item.Edition.Id.ToString(CultureInfo.InvariantCulture));
                values.Add("Part", item.Part.ToString(CultureInfo.InvariantCulture));
                values.Add("PartCount", item.PartCount.ToString(CultureInfo.InvariantCulture));
                values.Add("DownloadClientId", "1");
                values.Add("SourceModifiedUtc", item.Modified.ToString("O", CultureInfo.InvariantCulture));
            }

            return new EntityHistory
            {
                DownloadId = "owned-download", BookId = item.Book.Id, AuthorId = 1, Date = _modified.AddMinutes(1),
                EventType = EntityHistoryEventType.BookFileImported,
                Data = lowerCase ? values.ToDictionary(pair => char.ToLowerInvariant(pair.Key[0]) + pair.Key.Substring(1), pair => pair.Value) : values
            };
        }

        [TestCase(false)]
        [TestCase(true)]
        public void an_incomplete_marker_can_resume_only_a_live_valid_previous_receipt(bool missingCommittedFile)
        {
            var original = Subject.VerifyAndRecord(_tracked, _results);
            _marker = new DownloadHistory
            {
                DownloadId = "owned-download", DownloadClientId = 1,
                EventType = DownloadHistoryEventType.DownloadImportIncomplete
            };
            if (missingCommittedFile)
            {
                _files.Clear();
            }

            var resumed = Subject.VerifyAndRecord(_tracked, new List<ImportResult>());

            if (missingCommittedFile)
            {
                resumed.Should().BeNull();
                _marker.EventType.Should().Be(DownloadHistoryEventType.DownloadImportIncomplete);
            }
            else
            {
                resumed.Should().BeEquivalentTo(original);
                _marker.EventType.Should().Be(DownloadHistoryEventType.DownloadImported);
            }
        }

        [Test]
        public void committed_current_file_should_persist_complete_proof_even_if_event_history_was_not_written()
        {
            var receipt = Subject.VerifyAndRecord(_tracked, _results);

            receipt.Should().NotBeNull();
            receipt.AuthorId.Should().Be(1);
            receipt.BookIds.Should().Equal(10);
            receipt.Files.Single().FileId.Should().Be(30);
            _marker.Should().NotBeNull();
            Mocker.GetMock<IDownloadHistoryService>().Verify(service => service.RecordImportCompletion(_tracked, receipt), Times.Once());
        }

        [Test]
        public void two_parts_of_one_book_should_not_satisfy_a_two_book_download()
        {
            _results[0].ImportDecision.Item.PartCount = 2;
            AddPart(2, 2);
            _tracked.RemoteBook.Books.Add(new Book { Id = 11 });

            Subject.VerifyAndRecord(_tracked, _results).Should().BeNull();
            _marker.Should().BeNull();
        }

        [TestCase("missing")]
        [TestCase("duplicate")]
        [TestCase("different-edition")]
        [TestCase("declared-total")]
        public void incomplete_or_incoherent_recording_parts_should_not_complete(string problem)
        {
            _results[0].ImportDecision.Item.PartCount = problem == "declared-total" ? 3 : 2;
            AddPart(problem == "duplicate" ? 1 : 2, problem == "declared-total" ? 3 : 2, editionId: problem == "different-edition" ? 21 : 20);
            if (problem == "missing")
            {
                _results[1].Errors.Add("Import failed");
            }

            Subject.VerifyAndRecord(_tracked, _results).Should().BeNull();
            _marker.Should().BeNull();
        }

        [TestCase(false)]
        [TestCase(true)]
        public void bound_history_should_cover_an_already_imported_part_without_reimporting_it(bool lowerCase)
        {
            _results[0].ImportDecision.Item.PartCount = 2;
            AddPart(2, 2);
            _history.Add(ImportedHistory(1, lowerCase: lowerCase));
            _results[1].Errors.Add("Already imported");

            Subject.VerifyAndRecord(_tracked, _results).Files.Should().HaveCount(2);
        }

        [Test]
        public void fresh_inventory_can_rebuild_a_legacy_receipt_using_committed_file_provenance()
        {
            _history.Add(ImportedHistory(0, legacy: true));
            _grab = new DownloadHistory { DownloadId = "owned-download", DownloadClientId = 1 };
            _results.Clear();

            Subject.VerifyAndRecord(_tracked, _results).Should().NotBeNull();
        }

        [TestCase("unknown-scope")]
        [TestCase("uncommitted")]
        [TestCase("source-size")]
        [TestCase("source-time")]
        [TestCase("missing-file")]
        [TestCase("file-association")]
        [TestCase("physical-file")]
        public void missing_or_changed_evidence_should_keep_the_download_pending(string problem)
        {
            switch (problem)
            {
                case "unknown-scope": _tracked.RemoteBook = null; break;
                case "uncommitted": _results[0].ImportedFile = null; break;
                case "source-size": _sources[0].Size++; break;
                case "source-time": _sources[0].ModifiedUtc = _modified.AddSeconds(1); break;
                case "missing-file": _files.Clear(); break;
                case "file-association": _files[30].Edition = new Edition { Id = 20, BookId = 99 }; break;
                case "physical-file": Mocker.GetMock<IDiskProvider>().Setup(disk => disk.FileExists(It.IsAny<string>())).Returns(false); break;
            }

            Subject.VerifyAndRecord(_tracked, _results).Should().BeNull();
            _marker.Should().BeNull();
        }

        [Test]
        public void grabbed_book_scope_should_not_be_silently_dropped_by_a_smaller_remote_mapping()
        {
            _history.Add(new EntityHistory
            {
                BookId = 11, DownloadId = "owned-download", EventType = EntityHistoryEventType.Grabbed,
                Data = new Dictionary<string, string> { ["downloadClientId"] = "1" }
            });

            Subject.VerifyAndRecord(_tracked, _results).Should().BeNull();
        }

        [Test]
        public void unknown_remote_mapping_can_use_a_bound_grab_scope()
        {
            _tracked.RemoteBook = null;
            _history.Add(new EntityHistory
            {
                BookId = 10, DownloadId = "owned-download", EventType = EntityHistoryEventType.Grabbed,
                Data = new Dictionary<string, string> { ["downloadClientId"] = "1" }
            });

            Subject.VerifyAndRecord(_tracked, _results).Should().NotBeNull();
        }

        [Test]
        public void receipt_write_failure_should_propagate_without_returning_a_completion_proof()
        {
            Mocker.GetMock<IDownloadHistoryService>().Setup(service => service.RecordImportCompletion(_tracked, It.IsAny<DownloadImportReceipt>()))
                .Throws(new IOException("Owned write failure"));

            Assert.Throws<IOException>(() => Subject.VerifyAndRecord(_tracked, _results));
        }

        [Test]
        public void a_valid_receipt_should_survive_restart_after_its_source_files_are_retired()
        {
            var receipt = Subject.VerifyAndRecord(_tracked, _results);
            _sources.Clear();

            Subject.ReadReceipt(_tracked, _marker).Should().BeEquivalentTo(receipt);
        }

        [TestCase("version")]
        [TestCase("download")]
        [TestCase("client")]
        [TestCase("output")]
        [TestCase("book")]
        [TestCase("edition")]
        [TestCase("part")]
        [TestCase("author")]
        [TestCase("file-id")]
        [TestCase("path")]
        [TestCase("extra-source")]
        [TestCase("changed-source")]
        [TestCase("new-grab")]
        public void stale_or_tampered_receipt_should_not_authorize_cleanup(string problem)
        {
            var receipt = Subject.VerifyAndRecord(_tracked, _results);
            switch (problem)
            {
                case "version": receipt.Version++; break;
                case "download": receipt.DownloadId = "another-download"; break;
                case "client": receipt.DownloadClientId++; break;
                case "output": receipt.OutputPath = Path.Combine(TempFolder, "unrelated"); break;
                case "book": receipt.BookIds.Add(11); break;
                case "edition": receipt.Files[0].EditionId++; break;
                case "part": receipt.Files[0].Part++; break;
                case "author": receipt.AuthorId++; break;
                case "file-id": receipt.Files[0].FileId++; break;
                case "path": receipt.Files[0].ImportedPath = Path.Combine(TempFolder, "other.m4b"); break;
                case "extra-source": _sources.Add(new DownloadImportSourceFile { Path = Path.Combine(TempFolder, "download", "extra.m4b"), Size = 100, ModifiedUtc = _modified }); break;
                case "changed-source": _sources[0].Size++; break;
                case "new-grab": _grab = new DownloadHistory { DownloadClientId = 1, DownloadId = "owned-download", Date = _marker.Date.AddMinutes(1) }; break;
            }

            _marker = Marker(receipt);
            Subject.ReadReceipt(_tracked, _marker).Should().BeNull();
        }

        [Test]
        public void copy_intent_should_survive_revalidation_and_a_later_inventory_expansion()
        {
            _tracked.KeepSourceFiles = true;
            Subject.VerifyAndRecord(_tracked, _results);
            _tracked.KeepSourceFiles = false;
            _results[0].ImportDecision.Item.PartCount = 2;
            AddPart(2, 2);

            Subject.VerifyAndRecord(_tracked, _results).KeepSourceFiles.Should().BeTrue();
        }

        [Test]
        public void a_new_grab_should_not_borrow_import_provenance_from_an_earlier_attempt()
        {
            Subject.VerifyAndRecord(_tracked, _results);
            _history.Add(ImportedHistory(0));
            _grab = new DownloadHistory
            {
                DownloadId = "owned-download", DownloadClientId = 1, Date = _marker.Date.AddMinutes(1),
                Data = new Dictionary<string, string> { ["bookIds"] = "[10]" }
            };
            _results.Clear();

            Subject.VerifyAndRecord(_tracked, _results).Should().BeNull();
            Mocker.GetMock<IDownloadHistoryService>().Verify(service => service.RecordImportCompletion(_tracked, It.IsAny<DownloadImportReceipt>()), Times.Once());
        }

        [Test]
        public void latest_grab_scope_should_not_include_unrelated_books_from_an_earlier_cycle()
        {
            _history.Add(new EntityHistory
            {
                BookId = 99, DownloadId = "owned-download", EventType = EntityHistoryEventType.Grabbed,
                Data = new Dictionary<string, string> { ["downloadClientId"] = "1" }
            });
            _grab = new DownloadHistory
            {
                DownloadId = "owned-download", DownloadClientId = 1, Date = _modified.AddMinutes(-1),
                Data = new Dictionary<string, string> { ["bookIds"] = "[10]" }
            };

            Subject.VerifyAndRecord(_tracked, _results).BookIds.Should().Equal(10);
        }

        [Test]
        public void restart_receipt_validation_should_resolve_remote_path_mapping_before_comparing_output_roots()
        {
            var receipt = Subject.VerifyAndRecord(_tracked, _results);
            var local = _tracked.DownloadItem.OutputPath;
            _tracked.ImportItem = null;
            _tracked.DownloadItem.OutputPath = new OsPath(@"Z:\remote\download");
            Mocker.GetMock<IDownloadImportSourceService>().Setup(service => service.Scan(_tracked))
                .Callback(() => _tracked.ImportItem = new DownloadClientItem { OutputPath = local }).Returns(() => _sources);

            Subject.ReadReceipt(_tracked, _marker).Should().BeEquivalentTo(receipt);
        }

        [Test]
        public void an_unsafe_source_or_destination_should_not_persist_a_completion_receipt()
        {
            Mocker.GetMock<IDownloadImportSourceService>().Setup(service => service.Validate(_tracked, It.IsAny<DownloadImportReceipt>())).Returns(false);

            Subject.VerifyAndRecord(_tracked, _results).Should().BeNull();

            Mocker.GetMock<IDownloadHistoryService>().Verify(service => service.RecordImportCompletion(_tracked, It.IsAny<DownloadImportReceipt>()), Times.Never());
        }

        [Test]
        public void an_existing_receipt_should_not_bypass_a_rejected_cleanup_preflight_after_restart()
        {
            Subject.VerifyAndRecord(_tracked, _results);
            Mocker.GetMock<IDownloadImportSourceService>().Setup(service => service.Validate(_tracked, It.IsAny<DownloadImportReceipt>())).Returns(false);

            Subject.ReadReceipt(_tracked, _marker).Should().BeNull();
        }

        [TestCase("missing")]
        [TestCase("string")]
        [TestCase("boolean")]
        [TestCase("fraction")]
        [TestCase("duplicate")]
        public void receipt_version_must_be_explicit_unambiguous_and_numeric(string problem)
        {
            Subject.VerifyAndRecord(_tracked, _results);
            var original = _marker.Data[DownloadHistoryService.ImportReceiptKey];
            var document = JsonNode.Parse(original).AsObject();
            switch (problem)
            {
                case "missing": document.Remove("version"); break;
                case "string": document["version"] = "1"; break;
                case "boolean": document["version"] = true; break;
                case "fraction": document["version"] = JsonNode.Parse("1.0"); break;
            }

            _marker.Data[DownloadHistoryService.ImportReceiptKey] = problem == "duplicate" ? "{\"version\":1," + original.Substring(1) : document.ToJsonString();

            Subject.ReadReceipt(_tracked, _marker).Should().BeNull();
        }

        [TestCase("missing")]
        [TestCase("duplicate")]
        [TestCase("string")]
        [TestCase("number")]
        [TestCase("null")]
        public void missing_or_malformed_copy_intent_cannot_authorize_source_deletion(string problem)
        {
            Subject.VerifyAndRecord(_tracked, _results);
            var original = _marker.Data[DownloadHistoryService.ImportReceiptKey];
            var document = JsonNode.Parse(original).AsObject();
            switch (problem)
            {
                case "missing": document.Remove("keepSourceFiles"); break;
                case "string": document["keepSourceFiles"] = "false"; break;
                case "number": document["keepSourceFiles"] = 0; break;
                case "null": document["keepSourceFiles"] = null; break;
            }

            _marker.Data[DownloadHistoryService.ImportReceiptKey] = problem == "duplicate" ? "{\"keepSourceFiles\":false," + original.Substring(1) : document.ToJsonString();
            Subject.ReadReceipt(_tracked, _marker).Should().BeNull();

            Subject.VerifyAndRecord(_tracked, _results).KeepSourceFiles.Should().BeTrue();
        }

        [TestCase(DownloadHistoryEventType.DownloadImportIncomplete)]
        [TestCase(DownloadHistoryEventType.DownloadGrabbed)]
        public void a_later_marker_and_fresh_tracking_object_should_not_forget_an_earlier_copy_choice(DownloadHistoryEventType laterEvent)
        {
            _tracked.KeepSourceFiles = true;
            Subject.VerifyAndRecord(_tracked, _results);
            _marker = new DownloadHistory { DownloadId = "owned-download", DownloadClientId = 1, EventType = laterEvent };
            if (laterEvent == DownloadHistoryEventType.DownloadGrabbed)
            {
                _grab = _marker;
            }

            _tracked = new TrackedDownload
            {
                DownloadClient = 1, DownloadItem = _tracked.DownloadItem, RemoteBook = _tracked.RemoteBook
            };

            Subject.VerifyAndRecord(_tracked, _results).KeepSourceFiles.Should().BeTrue();
        }

        [TestCase(false)]
        [TestCase(true)]
        public void a_known_foreign_client_or_output_root_should_not_supply_its_copy_choice(bool foreignClient)
        {
            _tracked.KeepSourceFiles = true;
            var receipt = Subject.VerifyAndRecord(_tracked, _results);
            _tracked.KeepSourceFiles = false;
            if (foreignClient)
            {
                receipt.DownloadClientId = 2;
            }
            else
            {
                receipt.OutputPath = Path.Combine(TempFolder, "another-download");
            }

            _completionMarker = Marker(receipt);
            if (foreignClient)
            {
                _completionMarker.DownloadClientId = 2;
            }

            _marker = new DownloadHistory { DownloadId = "owned-download", DownloadClientId = 1, EventType = DownloadHistoryEventType.DownloadImportIncomplete };

            Subject.VerifyAndRecord(_tracked, _results).KeepSourceFiles.Should().BeFalse();
        }

        [TestCase("declared-total", false)]
        [TestCase("declared-total", true)]
        [TestCase("book", false)]
        [TestCase("book", true)]
        [TestCase("edition", false)]
        [TestCase("edition", true)]
        [TestCase("part", false)]
        [TestCase("part", true)]
        [TestCase("size", false)]
        [TestCase("size", true)]
        [TestCase("modified", false)]
        [TestCase("modified", true)]
        public void legacy_history_cannot_override_current_skipped_or_rejected_recording_constraints(string mismatch, bool rejected)
        {
            _history.Add(ImportedHistory(0, legacy: true));
            _grab = new DownloadHistory { DownloadId = "owned-download", DownloadClientId = 1 };
            var item = _results[0].ImportDecision.Item;
            switch (mismatch)
            {
                case "declared-total": item.PartCount = 2; break;
                case "book": item.Book.Id++; break;
                case "edition": item.Edition.Id++; break;
                case "part": item.Part++; break;
                case "size": item.Size++; break;
                case "modified": item.Modified = item.Modified.AddSeconds(1); break;
            }

            _results[0].Errors.Add("Already imported or rejected");
            if (rejected)
            {
                _results[0].ImportDecision.Reject(new Rejection("Current recording decision differs"));
            }

            Subject.VerifyAndRecord(_tracked, _results).Should().BeNull();

            Mocker.GetMock<IDownloadHistoryService>().Verify(service => service.RecordImportCompletion(It.IsAny<TrackedDownload>(), It.IsAny<DownloadImportReceipt>()), Times.Never());
        }

        [Test]
        public void a_pending_subset_copy_should_retain_sources_when_a_later_automatic_import_completes()
        {
            Mocker.GetMock<IDownloadHistoryService>().Setup(service => service.KeepSources(_tracked)).Returns(true);

            var receipt = Subject.VerifyAndRecord(_tracked, _results);

            receipt.KeepSourceFiles.Should().BeTrue();
            _tracked.KeepSourceFiles.Should().BeFalse();
        }

        [Test]
        public void persisted_pending_copy_intent_should_override_an_older_move_receipt_after_restart()
        {
            Subject.VerifyAndRecord(_tracked, _results).KeepSourceFiles.Should().BeFalse();
            Mocker.GetMock<IDownloadHistoryService>().Setup(service => service.KeepSources(_tracked)).Returns(true);

            Subject.ReadReceipt(_tracked, _marker).KeepSourceFiles.Should().BeTrue();
            Subject.VerifyAndRecord(_tracked, _results).KeepSourceFiles.Should().BeTrue();
        }
    }
}
