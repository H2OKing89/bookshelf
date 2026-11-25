using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.LibraryStats;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.MediaInfo;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.LibraryStatsTests
{
    [TestFixture]
    public class LibraryStatisticsServiceFixture : DbTest<LibraryStatisticsService, BookFile>
    {
        private Author _author;
        private Author _otherAuthor;
        private Edition _pack;
        private Edition _local;
        private Edition _ebook;
        private Edition _unowned;
        private Edition _regional;
        private static readonly DateTime Modified = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc);

        private Author AddAuthor(string name)
        {
            var metadata = Db.Insert(new AuthorMetadata
            {
                ForeignAuthorId = "local:author:" + Guid.NewGuid().ToString("N"),
                Name = name,
                SortName = name,
                NameLastFirst = name,
                SortNameLastFirst = name,
                TitleSlug = Guid.NewGuid().ToString("N")
            });

            return Db.Insert(new Author { AuthorMetadataId = metadata.Id, Path = "/books/" + name, CleanName = name });
        }

        private Edition AddEdition(Author author, string id, bool ebook = false, string language = null, params string[] narrators)
        {
            var book = Db.Insert(new Book
            {
                AuthorMetadataId = author.AuthorMetadataId,
                ForeignBookId = id,
                Title = "Synthetic recording",
                CleanTitle = "syntheticrecording",
                TitleSlug = Guid.NewGuid().ToString("N"),
                Added = Modified
            });
            return Db.Insert(new Edition
            {
                BookId = book.Id,
                ForeignEditionId = id,
                Title = book.Title,
                TitleSlug = book.TitleSlug,
                Monitored = true,
                IsEbook = ebook,
                Language = language,
                Narrators = narrators.ToList()
            });
        }

        private BookFile AddFile(int editionId, long size, string extension, MediaInfoModel mediaInfo = null)
        {
            return Db.Insert(new BookFile
            {
                EditionId = editionId,
                Path = "/owned/" + Guid.NewGuid().ToString("N") + extension,
                Size = size,
                Modified = Modified,
                DateAdded = Modified,
                Quality = new QualityModel(Quality.MP3),
                Part = 1,
                MediaInfo = mediaInfo
            });
        }

        private static MediaInfoModel Measured(long size, double? seconds, bool complete = true)
        {
            var model = new MediaInfoModel
            {
                AudioFormat = "AAC",
                AudioProfile = "LC",
                AudioBitrate = 128,
                ContainerDurationSeconds = seconds,
                Source = "MediaInfo",
                Analysis = new MediaAnalysisStamp { Revision = MediaInfoAnalysis.CurrentRevision, BundleId = MediaInfoAnalysis.BundleId, Size = size, ModifiedUtc = Modified, Complete = complete }
            };
            return model;
        }

        [SetUp]
        public void SetUp()
        {
            _author = AddAuthor("Synthetic Author");
            _otherAuthor = AddAuthor("Other Author");
            _pack = AddEdition(_author, "audible:us:B011111111", false, "eng", "Reader One", "reader one", "Reader Two");
            _regional = AddEdition(_author, "audible:uk:B011111111", false, "English", "Reader One");
            _local = AddEdition(_otherAuthor, LocalRecordingId.NewId(), false, "Spanish", "Reader Three");
            _ebook = AddEdition(_otherAuthor, "ebook:owned", true);
            _unowned = AddEdition(_author, "legacy-recording", false);
            AddFile(_pack.Id, 100, ".m4b", Measured(100, 60));
            AddFile(_pack.Id, 200, ".mp3", Measured(200, 40, false));
            var stale = Measured(300, 999);
            stale.Analysis.Size = 10;
            AddFile(_regional.Id, 300, ".m4b", stale);
            AddFile(_local.Id, 400, ".m4b", Measured(400, 120));
            AddFile(_ebook.Id, 50, ".epub", Measured(50, 999));
            AddFile(0, 60, ".m4b");
            AddFile(123456, 70, ".m4b", new MediaInfoModel { Source = "MyAnonaMouse", ContainerDurationSeconds = 600 });
            AddFile(0, 10, ".dat");
            var one = Db.Insert(new RecordingWork { Title = "One", Author = "Synthetic Author", Series = "Pack", Position = "1" });
            var two = Db.Insert(new RecordingWork { Title = "Two", Author = "Synthetic Author", Series = "Pack", Position = "2" });
            var unknown = Db.Insert(new RecordingWork { Title = "Side Story", Author = "Synthetic Author", Series = "Pack", Position = "1.5" });
            Membership(_pack, one, true);
            Membership(_pack, two, true);
            Membership(_regional, one, true);
            Membership(_unowned, two, true);
            Membership(_pack, unknown, false);

            // A retargeted recording's old membership must not follow it.
            Membership(_local, unknown, true, "Former recording identity");
        }

        private void Membership(Edition edition, RecordingWork work, bool confirmed, string identityOverride = null)
        {
            Db.Insert(new RecordingWorkMembership
            {
                EditionId = edition.Id,
                WorkId = work.Id,
                Confirmed = confirmed,
                RecordingIdentity = identityOverride ?? RecordingWorkRepository.Identity(edition.ForeignEditionId, edition.Asin, edition.AudibleRegion)
            });
        }

        private static int Group(List<LibraryStatisticsGroup> groups, string name) => groups.Single(group => string.Equals(group.Label, name, StringComparison.OrdinalIgnoreCase)).Count;

        [Test]
        public void should_distinguish_catalog_recordings_and_explicit_works_from_owned_files()
        {
            var value = Subject.GetStatistics();
            value.AuthorCount.Should().Be(2);
            value.BookCount.Should().Be(5);
            value.AudioRecordingCount.Should().Be(4);
            value.OwnedAudioRecordingCount.Should().Be(3);
            value.EbookEditionCount.Should().Be(1);
            value.OwnedEbookEditionCount.Should().Be(1);
            value.ConfirmedWorkCount.Should().Be(2);
            value.OwnedConfirmedWorkCount.Should().Be(2);
            value.FileCount.Should().Be(8);
            value.AudioFileCount.Should().Be(6);
            value.EbookFileCount.Should().Be(1);
            value.OtherFileCount.Should().Be(1);
            value.MappedFileCount.Should().Be(5);
            value.UnresolvedFileCount.Should().Be(3);
            value.SizeOnDisk.Should().Be(1190);
            value.MappedSizeOnDisk.Should().Be(1050);
            value.UnresolvedSizeOnDisk.Should().Be(140);
            value.KnownAudioDurationSeconds.Should().Be(220);
            value.AudioFilesWithKnownDuration.Should().Be(3);
            Group(value.MediaInfoStatus, "current").Should().Be(2);
            Group(value.MediaInfoStatus, "partial").Should().Be(1);
            Group(value.MediaInfoStatus, "stale").Should().Be(1);
            Group(value.MediaInfoStatus, "advertised").Should().Be(1);
            Group(value.MediaInfoStatus, "missing").Should().Be(1);
            Group(value.Codecs, "AAC LC").Should().Be(3);
            Group(value.Codecs, "Unknown").Should().Be(3);
            Group(value.Bitrates, "128–191 kbps").Should().Be(3);
            Group(value.Languages, "English").Should().Be(2);
            Group(value.Narrators, "Reader One").Should().Be(2);
            Group(value.Sources, "audible").Should().Be(2);
            Group(value.Sources, "local").Should().Be(1);
            Group(value.Narrators, "Unknown").Should().Be(1);
        }

        [Test]
        public void should_filter_catalog_and_owned_files_without_assigning_unresolved_media()
        {
            var local = Subject.GetStatistics(source: "local");
            local.AuthorCount.Should().Be(1);
            local.AudioRecordingCount.Should().Be(1);
            local.FileCount.Should().Be(1);
            local.SizeOnDisk.Should().Be(400);
            local.KnownAudioDurationSeconds.Should().Be(120);
            local.ConfirmedWorkCount.Should().Be(0);
            local.UnresolvedFileCount.Should().Be(0);
            local.Authors.Should().HaveCount(2);
            var selected = Subject.GetStatistics(_author.Id, "audiobook", "audible");
            selected.BookCount.Should().Be(2);
            selected.FileCount.Should().Be(3);
            selected.KnownAudioDurationSeconds.Should().Be(100);
            selected.ConfirmedWorkCount.Should().Be(2);
            var other = Subject.GetStatistics(source: "other", media: "audiobook");
            other.AudioRecordingCount.Should().Be(1);
            other.OwnedAudioRecordingCount.Should().Be(0);
            other.ConfirmedWorkCount.Should().Be(1);
            other.OwnedConfirmedWorkCount.Should().Be(0);
            other.KnownAudioDurationSeconds.Should().BeNull();
            var ebook = Subject.GetStatistics(media: "ebook");
            ebook.EbookFileCount.Should().Be(1);
            ebook.AudioFileCount.Should().Be(0);
            ebook.ConfirmedWorkCount.Should().Be(0);
            ebook.MediaInfoStatus.Should().BeEmpty();
            ebook.KnownAudioDurationSeconds.Should().BeNull();
        }

        [Test]
        public void should_query_fresh_file_and_membership_counts_after_updates()
        {
            Subject.GetStatistics().OwnedAudioRecordingCount.Should().Be(3);
            foreach (var file in Db.All<BookFile>().Where(file => file.EditionId == _pack.Id))
            {
                Db.Delete(file);
            }

            var newFile = AddFile(_unowned.Id, 1000, ".m4b", Measured(1000, 300));
            var after = Subject.GetStatistics();
            after.OwnedAudioRecordingCount.Should().Be(3);
            after.OwnedConfirmedWorkCount.Should().Be(2);
            after.KnownAudioDurationSeconds.Should().Be(420);
            after.SizeOnDisk.Should().Be(1890);
            Db.Delete(newFile);
            Subject.GetStatistics().OwnedConfirmedWorkCount.Should().Be(1);
        }

        [Test]
        public void should_not_add_parallel_audio_stream_durations_or_infer_catalog_runtime()
        {
            var file = Db.All<BookFile>().Single(item => item.EditionId == _local.Id);
            file.MediaInfo.ContainerDurationSeconds = null;
            file.MediaInfo.AudioStreams = new List<AudioStreamInfo>
            {
                new AudioStreamInfo { DurationSeconds = 90 }, new AudioStreamInfo { DurationSeconds = 90 }
            };
            Db.Update(file);
            Subject.GetStatistics(source: "local").KnownAudioDurationSeconds.Should().Be(90);
            file.MediaInfo.AudioStreams[1].DurationSeconds = 91;
            Db.Update(file);
            Subject.GetStatistics(source: "local").KnownAudioDurationSeconds.Should().BeNull();
        }

        [TestCase(0, "all", "all")]
        [TestCase(-1, "all", "all")]
        [TestCase(null, "everything", "all")]
        [TestCase(null, "all", "amazon")]
        public void should_reject_invalid_filter_values(int? id, string media, string source)
        {
            Assert.Throws<NzbDroneClientException>(() => Subject.GetStatistics(id, media, source)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        }

        [Test]
        public void should_measure_large_catalog_query_without_joining_files_to_work_memberships()
        {
            const int count = 4160;
            var books = Enumerable.Range(0, count).Select(index => new Book
            {
                AuthorMetadataId = _author.AuthorMetadataId,
                ForeignBookId = "local:recording:" + index.ToString("D8"),
                Title = "Owned large catalog " + index,
                CleanTitle = "ownedlargecatalog" + index,
                TitleSlug = "owned-stats-large-" + index,
                Added = Modified
            }).ToList();
            Db.InsertMany(books);
            var editions = books.Select(book => new Edition { BookId = book.Id, ForeignEditionId = book.ForeignBookId, Title = book.Title, TitleSlug = book.TitleSlug }).ToList();
            Db.InsertMany(editions);
            Db.InsertMany(editions.Select(edition => new BookFile
            {
                EditionId = edition.Id,
                Size = 1024,
                Quality = new QualityModel(Quality.MP3),
                Path = "/owned/" + edition.Id + ".m4b",
                Modified = Modified,
                DateAdded = Modified,
                MediaInfo = Measured(1024, 60)
            }));
            var watch = Stopwatch.StartNew();
            var statistics = Subject.GetStatistics();
            watch.Stop();
            TestContext.Progress.WriteLine($"Library statistics read {statistics.BookCount} books and {statistics.FileCount} files in {watch.ElapsedMilliseconds} ms ({Db.DatabaseType}).");
            statistics.BookCount.Should().Be(5 + count);
            statistics.FileCount.Should().Be(8 + count);
            statistics.SizeOnDisk.Should().Be(1190 + (count * 1024L));
        }
    }
}
