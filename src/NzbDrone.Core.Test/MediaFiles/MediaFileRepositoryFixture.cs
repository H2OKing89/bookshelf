using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Dapper;
using FizzWare.NBuilder;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.LibraryStats;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.MediaInfo;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.MediaFiles
{
    [TestFixture]
    public class MediaFileRepositoryFixture : DbTest<MediaFileRepository, BookFile>
    {
        private Author _author;
        private Book _book;
        private Edition _edition;

        [SetUp]
        public void Setup()
        {
            var meta = Builder<AuthorMetadata>.CreateNew()
                .With(a => a.Id = 0)
                .Build();
            Db.Insert(meta);

            _author = Builder<Author>.CreateNew()
                .With(a => a.AuthorMetadataId = meta.Id)
                .With(a => a.Id = 0)
                .Build();
            Db.Insert(_author);

            _book = Builder<Book>.CreateNew()
                .With(a => a.Id = 0)
                .With(a => a.AuthorMetadataId = _author.AuthorMetadataId)
                .Build();
            Db.Insert(_book);

            _edition = Builder<Edition>.CreateNew()
                .With(a => a.Id = 0)
                .With(a => a.BookId = _book.Id)
                .Build();
            Db.Insert(_edition);

            var files = Builder<BookFile>.CreateListOfSize(10)
                .All()
                .With(c => c.Id = 0)
                .With(c => c.Quality = new QualityModel(Quality.MP3))
                .TheFirst(5)
                .With(c => c.EditionId = _edition.Id)
                .TheRest()
                .With(c => c.EditionId = 0)
                .TheFirst(1)
                .With(c => c.Path = @"C:\Test\Path\Author\somefile1.flac".AsOsAgnostic())
                .TheNext(1)
                .With(c => c.Path = @"C:\Test\Path\Author\somefile2.flac".AsOsAgnostic())
                .BuildListOfNew();
            Db.InsertMany(files);
        }

        [Test]
        public void existing_file_should_store_language_tag_evidence_when_rescanned()
        {
            var file = Subject.GetFilesByAuthor(_author.Id).First();
            file.TaggedLanguage = "en-US, fre";

            Subject.Update(file);

            Subject.Get(file.Id).TaggedLanguage.Should().Be("en-US, fre");
        }

        [Test]
        public void should_persist_release_group_evidence_without_changing_the_file_identity()
        {
            var file = Subject.GetFilesByAuthor(_author.Id).First();
            file.ReleaseGroupEvidence.Should().BeNull("legacy files have unknown acquisition provenance");
            file.ReleaseGroup = "RecordingCrew";
            file.ReleaseGroupEvidence = new ReleaseGroupEvidence
            {
                PackUploaderGroup = "UploaderCrew",
                FileGroup = "RecordingCrew",
                RecordingId = "audible:us:B111111111",
                PackRecordingIds = new[] { "audible:us:B111111111", "audible:us:B222222222" },
                Source = "recordingFile",
                Reason = "exactPackRecording",
                ResolvedGroup = "RecordingCrew"
            };
            Subject.Update(file);
            var stored = Subject.Get(file.Id);
            stored.EditionId.Should().Be(file.EditionId);
            stored.ReleaseGroup.Should().Be("RecordingCrew");
            stored.ReleaseGroupEvidence.Should().BeEquivalentTo(file.ReleaseGroupEvidence);
        }

        [TestCase(true, false)]
        [TestCase(false, true)]
        [TestCase(true, true)]
        public void property_editor_should_preserve_exact_media_analysis_and_publish_once_after_commit(bool quality, bool group)
        {
            var files = Subject.GetFilesByAuthor(_author.Id).Take(2).ToList();
            var expected = new Dictionary<int, (string Path, long Size, int Edition, DateTime Modified, string Media, string Group)>();
            foreach (var file in files)
            {
                file.Size = 12345;
                file.Modified = new DateTime(2026, 11, 24, 8, 0, 0, DateTimeKind.Utc).AddTicks(7574270);
                Subject.SetFields(file, item => item.Size, item => item.Modified);

                // A real saved probe may have a fractional timestamp in its
                // document. Seed that exact storage value without a serializer
                // round trip, which must not happen during an unrelated edit.
                var metadata = JsonSerializer.Serialize(new
                {
                    source = "MediaInfo",
                    analysis = new { revision = MediaInfoAnalysis.CurrentRevision, size = file.Size, modifiedUtc = file.Modified, complete = true }
                });
                using (var connection = Db.OpenConnection())
                {
                    connection.Execute("UPDATE \"BookFiles\" SET \"MediaInfo\" = @metadata WHERE \"Id\" = @Id", new { metadata, file.Id });
                }

                expected.Add(file.Id, (file.Path, file.Size, file.EditionId, file.Modified, metadata, file.ReleaseGroup));
            }

            files = files.Select(file => Subject.Get(file.Id)).ToList();
            foreach (var file in files)
            {
                MediaInfoAnalysis.IsInvalidatedByFileState(file.MediaInfo, file.Size, file.Modified).Should().BeFalse();
                file.Quality = new QualityModel(Quality.M4B);
                file.ReleaseGroup = "ReviewedCrew";
                file.ReleaseGroupEvidence = ReleaseGroupEvidence.ManualChoice(file.ReleaseGroupEvidence, file.ReleaseGroup);

                // Also prove unrelated fields from a stale resource cannot
                // replace a newer stored file state or recording association.
                file.Path = "stale path";
                file.Size++;
                file.Modified = file.Modified.AddSeconds(1);
                file.EditionId = 0;
            }

            var service = new MediaFileService(Subject, Mocker.GetMock<IEventAggregator>().Object, TestLogger);
            Mocker.GetMock<IEventAggregator>().Setup(events => events.PublishEvent(It.IsAny<LibraryStatisticsChangedEvent>()))
                .Callback(() =>
                {
                    foreach (var file in files)
                    {
                        var stored = Subject.Get(file.Id);
                        var before = expected[file.Id];
                        stored.Path.Should().Be(before.Path);
                        stored.Size.Should().Be(before.Size);
                        stored.EditionId.Should().Be(before.Edition);
                        stored.Modified.Should().Be(before.Modified);
                        stored.Quality.Quality.Should().Be(quality ? Quality.M4B : Quality.MP3);
                        stored.ReleaseGroup.Should().Be(group ? "ReviewedCrew" : before.Group);
                        if (group)
                        {
                            stored.ReleaseGroupEvidence.Source.Should().Be("manual");
                            stored.ReleaseGroupEvidence.ResolvedGroup.Should().Be("ReviewedCrew");
                        }
                        else
                        {
                            stored.ReleaseGroupEvidence.Should().BeNull();
                        }

                        MediaInfoAnalysis.IsInvalidatedByFileState(stored.MediaInfo, stored.Size, stored.Modified).Should().BeFalse();
                        using var connection = Db.OpenConnection();
                        connection.QuerySingle<string>("SELECT \"MediaInfo\" FROM \"BookFiles\" WHERE \"Id\" = @Id", new { file.Id }).Should().Be(before.Media);
                    }
                });

            service.UpdateQualityAndReleaseGroup(files, quality, group);
            Mocker.GetMock<IEventAggregator>().Verify(events => events.PublishEvent(It.IsAny<LibraryStatisticsChangedEvent>()), Times.Once());
        }

        [Test]
        public void property_editor_should_rollback_all_fields_when_any_selected_file_is_missing()
        {
            var file = Subject.GetFilesByAuthor(_author.Id).First();
            var previousGroup = file.ReleaseGroup;
            file.ReleaseGroup = "ReviewedCrew";
            file.ReleaseGroupEvidence = ReleaseGroupEvidence.ManualChoice(file.ReleaseGroupEvidence, file.ReleaseGroup);
            var missing = new BookFile { Id = 99999, ReleaseGroup = "OtherCrew" };
            var service = new MediaFileService(Subject, Mocker.GetMock<IEventAggregator>().Object, TestLogger);

            Assert.Throws<ModelNotFoundException>(() => service.UpdateQualityAndReleaseGroup(new List<BookFile> { file, missing }, false, true));

            Subject.Get(file.Id).ReleaseGroup.Should().Be(previousGroup);
            Subject.Get(file.Id).ReleaseGroupEvidence.Should().BeNull();
            Mocker.GetMock<IEventAggregator>().Verify(events => events.PublishEvent(It.IsAny<LibraryStatisticsChangedEvent>()), Times.Never());
        }

        [Test]
        public void get_files_by_author()
        {
            VerifyData();
            var authorFiles = Subject.GetFilesByAuthor(_author.Id);
            VerifyEagerLoaded(authorFiles);

            authorFiles.Should().OnlyContain(c => c.Author.Value.Id == _author.Id);
        }

        [Test]
        public void get_unmapped_files()
        {
            VerifyData();
            var unmappedfiles = Subject.GetUnmappedFiles();
            VerifyUnmapped(unmappedfiles);

            unmappedfiles.Should().HaveCount(5);
        }

        [TestCase("C:\\Test\\Path")]
        [TestCase("C:\\Test\\Path\\")]
        public void get_files_by_base_path_should_cope_with_trailing_slash(string dir)
        {
            VerifyData();
            var firstReleaseFiles = Subject.GetFilesWithBasePath(dir.AsOsAgnostic());

            firstReleaseFiles.Should().HaveCount(2);
        }

        [TestCase("C:\\Test\\Path")]
        [TestCase("C:\\Test\\Path\\")]
        public void get_files_by_base_path_should_not_get_files_for_partial_path(string dir)
        {
            VerifyData();

            var files = Builder<BookFile>.CreateListOfSize(2)
                .All()
                .With(c => c.Id = 0)
                .With(c => c.Quality = new QualityModel(Quality.MP3))
                .TheFirst(1)
                .With(c => c.Path = @"C:\Test\Path2\Author\somefile1.flac".AsOsAgnostic())
                .TheNext(1)
                .With(c => c.Path = @"C:\Test\Path2\Author\somefile2.flac".AsOsAgnostic())
                .BuildListOfNew();
            Db.InsertMany(files);

            var firstReleaseFiles = Subject.GetFilesWithBasePath(dir.AsOsAgnostic());
            firstReleaseFiles.Should().HaveCount(2);
        }

        [Test]
        public void get_file_by_path()
        {
            VerifyData();
            var file = Subject.GetFileWithPath(@"C:\Test\Path\Author\somefile2.flac".AsOsAgnostic());

            file.Should().NotBeNull();
            file.Edition.IsLoaded.Should().BeTrue();
            file.Edition.Value.Should().NotBeNull();
            file.Author.IsLoaded.Should().BeTrue();
            file.Author.Value.Should().NotBeNull();
        }

        [Test]
        public void get_files_by_book()
        {
            VerifyData();
            var files = Subject.GetFilesByBook(_book.Id);
            VerifyEagerLoaded(files);

            files.Should().OnlyContain(c => c.EditionId == _book.Id);
        }

        [Test]
        public void should_replace_same_path_in_a_single_transaction()
        {
            var old = Db.All<BookFile>().First();
            var replacement = new BookFile { Path = old.Path, EditionId = old.EditionId, Quality = new QualityModel(Quality.MP3) };

            Subject.ImportFiles(new List<BookFile> { replacement }, new List<BookFile>(), new List<BookFile> { old });

            Db.All<BookFile>().Should().HaveCount(10);
            Subject.GetFileWithPath(old.Path).Id.Should().Be(replacement.Id).And.NotBe(old.Id);
        }

        [Test]
        public void should_roll_back_old_deletion_and_all_new_inserts_if_any_insert_fails()
        {
            var existing = Db.All<BookFile>().ToList();
            var old = existing[0];
            var replacement = new BookFile { Path = old.Path, EditionId = old.EditionId, Quality = new QualityModel(Quality.MP3) };
            var collision = new BookFile { Path = existing[1].Path, Quality = new QualityModel(Quality.MP3) };

            Assert.Catch(() => Subject.ImportFiles(new List<BookFile> { replacement, collision }, new List<BookFile>(), new List<BookFile> { old }));

            Db.All<BookFile>().Select(file => new { file.Id, file.Path }).Should()
                .BeEquivalentTo(existing.Select(file => new { file.Id, file.Path }));
            replacement.Id.Should().Be(0);
            collision.Id.Should().Be(0);
        }

        [Test]
        public void should_roll_back_retired_rows_when_an_update_fails()
        {
            var existing = Db.All<BookFile>().ToList();
            var invalidUpdate = new BookFile
            {
                Id = existing[2].Id,
                Path = existing[1].Path,
                Quality = new QualityModel(Quality.MP3)
            };

            Assert.Catch(() => Subject.ImportFiles(new List<BookFile>(), new List<BookFile> { invalidUpdate }, new List<BookFile> { existing[0] }));

            Db.All<BookFile>().Select(file => new { file.Id, file.Path }).Should()
                .BeEquivalentTo(existing.Select(file => new { file.Id, file.Path }));
        }

        [Test]
        public void should_store_analysis_for_the_exact_row_and_skip_a_changed_row()
        {
            var snapshot = Subject.GetFilesByAuthor(_author.Id).First();
            snapshot.Modified = new DateTime(2026, 11, 20, 12, 0, 0, DateTimeKind.Utc);
            snapshot.Size = 1234;
            Subject.SetFields(snapshot, file => file.Modified, file => file.Size);
            snapshot = Subject.Get(snapshot.Id);
            snapshot.MediaInfo = new MediaInfoModel
            {
                AudioFormat = "AAC",
                Analysis = new MediaAnalysisStamp { Revision = 1, Size = snapshot.Size, ModifiedUtc = snapshot.Modified }
            };

            Subject.UpdateMediaInfo(new[] { snapshot });

            var changed = Subject.Get(snapshot.Id);
            changed.MediaInfo.Analysis.Revision.Should().Be(1);
            changed.EditionId.Should().Be(snapshot.EditionId);
            changed.Modified = changed.Modified.AddHours(1);
            Subject.SetFields(changed, file => file.Modified);

            snapshot.MediaInfo.AudioFormat = "stale result";
            Subject.UpdateMediaInfo(new[] { snapshot });

            var stored = Subject.Get(snapshot.Id);
            stored.MediaInfo.AudioFormat.Should().Be("AAC");
            stored.Modified.Should().Be(changed.Modified);
        }

        private void VerifyData()
        {
            Db.All<Author>().Should().HaveCount(1);
            Db.All<Book>().Should().HaveCount(1);
            Db.All<BookFile>().Should().HaveCount(10);
        }

        private void VerifyEagerLoaded(List<BookFile> files)
        {
            foreach (var file in files)
            {
                file.Edition.IsLoaded.Should().BeTrue();
                file.Edition.Value.Should().NotBeNull();
                file.Author.IsLoaded.Should().BeTrue();
                file.Author.Value.Should().NotBeNull();
                file.Author.Value.Metadata.IsLoaded.Should().BeTrue();
                file.Author.Value.Metadata.Value.Should().NotBeNull();
            }
        }

        private void VerifyUnmapped(List<BookFile> files)
        {
            foreach (var file in files)
            {
                file.Edition.IsLoaded.Should().BeFalse();
                file.Edition.Value.Should().BeNull();
                file.Author.IsLoaded.Should().BeFalse();
                file.Author.Value.Should().BeNull();
            }
        }

        [Ignore("Doesn't make sense now we link to edition")]
        [Test]
        public void delete_files_by_book_should_work_if_join_fails()
        {
            Db.Delete(_book);
            Subject.DeleteFilesByBook(_book.Id);

            Db.All<BookFile>().Where(x => x.EditionId == _book.Id).Should().HaveCount(0);
        }
    }
}
