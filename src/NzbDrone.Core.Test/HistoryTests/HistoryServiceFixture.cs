using System.Collections.Generic;
using System.IO;
using FizzWare.NBuilder;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.Download;
using NzbDrone.Core.History;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.Events;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Profiles.Qualities;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Core.Test.Qualities;

namespace NzbDrone.Core.Test.HistoryTests
{
    public class HistoryServiceFixture : CoreTest<HistoryService>
    {
        private QualityProfile _profile;
        private QualityProfile _profileCustom;

        [SetUp]
        public void Setup()
        {
            _profile = new QualityProfile
            {
                Cutoff = Quality.MP3.Id,
                Items = QualityFixture.GetDefaultQualities(),
            };

            _profileCustom = new QualityProfile
            {
                Cutoff = Quality.MP3.Id,
                Items = QualityFixture.GetDefaultQualities(Quality.MP3),
            };
        }

        [Test]
        public void should_use_file_name_for_source_title_if_scene_name_is_null()
        {
            var author = Builder<Author>.CreateNew().Build();
            var trackFile = Builder<BookFile>.CreateNew()
                .With(f => f.SceneName = null)
                .With(f => f.Author = author)
                .Build();

            var localTrack = new LocalBook
            {
                Author = author,
                Book = new Book(),
                Path = @"C:\Test\Unsorted\Author.01.Hymn.mp3"
            };

            var downloadClientItem = new DownloadClientItem
            {
                DownloadClientInfo = new DownloadClientItemClientInfo
                {
                    Protocol = DownloadProtocol.Usenet,
                    Id = 1,
                    Name = "sab"
                },
                DownloadId = "abcd"
            };

            Subject.Handle(new TrackImportedEvent(localTrack, trackFile, new List<BookFile>(), true, downloadClientItem));

            Mocker.GetMock<IHistoryRepository>()
                .Verify(v => v.Insert(It.Is<EntityHistory>(h => h.SourceTitle == Path.GetFileNameWithoutExtension(localTrack.Path))));
        }

        [Test]
        public void committed_import_history_should_retain_recording_parts_client_and_source_timestamp()
        {
            var modified = new System.DateTime(2026, 11, 18, 1, 2, 3, System.DateTimeKind.Utc);
            var local = new LocalBook
            {
                Path = Path.Combine(TempFolder, "source.m4b"), Author = new Author { Id = 1 }, Book = new Book { Id = 2 },
                Part = 2, PartCount = 3, Size = 100, Modified = modified
            };
            var imported = new BookFile { Id = 4, EditionId = 5, Part = 2, Path = Path.Combine(TempFolder, "imported.m4b") };
            EntityHistory saved = null;
            Mocker.GetMock<IHistoryRepository>().Setup(repository => repository.Insert(It.IsAny<EntityHistory>()))
                .Callback<EntityHistory>(history => saved = history);
            var client = new DownloadClientItem { DownloadId = "owned-download", DownloadClientInfo = new DownloadClientItemClientInfo { Id = 6 } };

            Subject.Handle(new TrackImportedEvent(local, imported, new List<BookFile>(), true, client));

            saved.BookId.Should().Be(2);
            saved.Data["FileId"].Should().Be("4");
            saved.Data["EditionId"].Should().Be("5");
            saved.Data["Part"].Should().Be("2");
            saved.Data["PartCount"].Should().Be("3");
            saved.Data["DownloadClientId"].Should().Be("6");
            saved.Data["SourceModifiedUtc"].Should().Be(modified.ToString("O"));
        }
    }
}
