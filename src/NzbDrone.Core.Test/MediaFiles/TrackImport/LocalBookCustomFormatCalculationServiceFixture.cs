using System;
using System.Collections.Generic;
using System.IO;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.CustomFormats;
using NzbDrone.Core.Download;
using NzbDrone.Core.History;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.BookImport;
using NzbDrone.Core.Organizer;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Profiles;
using NzbDrone.Core.Profiles.Qualities;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MediaFiles.BookImport
{
    [TestFixture]
    public class LocalBookCustomFormatCalculationServiceFixture : CoreTest<LocalBookCustomFormatCalculationService>
    {
        private LocalBook _item;
        private List<CustomFormat> _formats;
        private string _intended;

        [SetUp]
        public void Setup()
        {
            _formats = new List<CustomFormat>
            {
                new CustomFormat("Preferred token", new ReleaseTitleSpecification { Value = "Preferred" }) { Id = 1 },
                new CustomFormat("Trusted group", new ReleaseGroupSpecification { Value = "^Trusted$" }) { Id = 2 },
                new CustomFormat("Freeleech", new IndexerFlagSpecification { Value = (int)IndexerFlags.Freeleech }) { Id = 3 }
            };
            var profile = new QualityProfile
            {
                FormatItems = new List<ProfileFormatItem>
                {
                    new ProfileFormatItem { Format = _formats[0], Score = 100 },
                    new ProfileFormatItem { Format = _formats[1], Score = 200 },
                    new ProfileFormatItem { Format = _formats[2], Score = 300 }
                }
            };
            var author = new Author { Id = 10, Name = "Owned Author", QualityProfile = profile };
            var book = new Book { Id = 20, Title = "Owned Book", Author = author };
            var edition = new Edition { Id = 30, BookId = 20, Book = book, ForeignEditionId = "audible:us:B000000001", Asin = "B000000001", AudibleRegion = "us" };
            _item = new LocalBook
            {
                Path = Path.Combine(TempFolder, "download", "Book-Preferred.mp3"), Author = author, Book = book, Edition = edition,
                Quality = new QualityModel(Quality.MP3), Size = 1024, Part = 1, PartCount = 2,
                FileTrackInfo = new ParsedTrackInfo { MediaInfo = new MediaInfoModel { AudioCodecId = "mp4a-40-2" } }
            };
            _intended = "Book";
            Mocker.GetMock<ICustomFormatService>().Setup(service => service.All()).Returns(() => _formats);
            Mocker.SetConstant<ICustomFormatCalculationService>(Mocker.Resolve<CustomFormatCalculationService>());
            Mocker.GetMock<IBuildFileNames>().Setup(builder => builder.BuildBookFileName(It.IsAny<Author>(), It.IsAny<Edition>(), It.IsAny<BookFile>(), null, It.IsAny<List<CustomFormat>>()))
                .Returns(() => _intended);
            Mocker.GetMock<IHistoryService>().Setup(service => service.FindByDownloadId(It.IsAny<string>(), It.IsAny<int>())).Returns(new List<EntityHistory>());
        }

        [Test]
        public void filename_tokens_are_scored_before_and_after_the_actual_intended_name()
        {
            var result = Subject.Calculate(_item, null);

            result.OriginalScore.Should().Be(100);
            result.IntendedScore.Should().Be(0);
            result.OriginalFormats.Should().ContainSingle().Which.Id.Should().Be(1);
        }

        [Test]
        public void keeping_the_original_name_preserves_its_preference()
        {
            _intended = "Book-Preferred";

            var result = Subject.Calculate(_item, null);

            result.OriginalScore.Should().Be(100);
            result.IntendedScore.Should().Be(100);
        }

        [Test]
        public void a_download_scene_title_survives_rename_like_the_actual_importer()
        {
            var client = new DownloadClientItem { Title = "Brandon Sanderson - Mistborn Preferred (2025)", DownloadId = "owned-download" };
            var metadata = ImportBookFileMetadata.Create(_item, client, Mocker.GetMock<IHistoryService>().Object);

            var result = Subject.Calculate(_item, client);

            metadata.SceneName.Should().Be(client.Title);
            result.IntendedScore.Should().Be(100);
        }

        [Test]
        public void unrelated_local_scene_text_cannot_claim_an_unpersisted_release_preference()
        {
            _item.Path = Path.Combine(TempFolder, "download", "Book.mp3");
            _item.SceneName = "Unpersisted Preferred";

            var result = Subject.Calculate(_item, null);

            result.OriginalScore.Should().Be(0);
            result.IntendedScore.Should().Be(0);
        }

        [Test]
        public void parent_folder_tokens_cannot_stand_in_for_the_recording_filename()
        {
            _item.Path = Path.Combine(TempFolder, "Preferred", "Book.mp3");

            Subject.Calculate(_item, null).OriginalScore.Should().Be(0);
        }

        [TestCase(null, 200)]
        [TestCase("", 0)]
        [TestCase("Other", 0)]
        public void explicit_group_choices_match_the_actual_import_metadata(string choice, int score)
        {
            _item.Path = Path.Combine(TempFolder, "download", "Book.mp3");
            _item.ReleaseGroup = "Trusted";
            _item.ReleaseGroupOverride = choice;

            var result = Subject.Calculate(_item, null);

            result.OriginalScore.Should().Be(score);
            result.IntendedScore.Should().Be(score);
        }

        [Test]
        public void only_the_latest_grab_flags_are_used_for_a_tracked_download()
        {
            _item.Path = Path.Combine(TempFolder, "download", "Book.mp3");
            Mocker.GetMock<IHistoryService>().Setup(service => service.FindByDownloadId("owned-download", 7))
                .Returns(new List<EntityHistory>
                {
                    new EntityHistory { EventType = EntityHistoryEventType.Grabbed, Date = DateTime.UtcNow.AddMinutes(-2), Data = new Dictionary<string, string> { ["indexerFlags"] = "0" } },
                    new EntityHistory { EventType = EntityHistoryEventType.Grabbed, Date = DateTime.UtcNow.AddMinutes(-1), Data = new Dictionary<string, string> { ["indexerFlags"] = "Freeleech" } },
                    new EntityHistory { EventType = EntityHistoryEventType.BookFileImported, Date = DateTime.UtcNow, Data = new Dictionary<string, string> { ["indexerFlags"] = "0" } }
                });

            var result = Subject.Calculate(_item, new DownloadClientItem { DownloadId = "owned-download", Title = "Unparsed", DownloadClientInfo = new DownloadClientItemClientInfo { Id = 7 } });

            result.OriginalScore.Should().Be(300);
            result.IntendedScore.Should().Be(300);
        }

        [TestCase(false, 300)]
        [TestCase(true, 0)]
        public void unproven_local_flags_cannot_override_a_tracked_downloads_acquisition_history(bool tracked, int score)
        {
            _item.Path = Path.Combine(TempFolder, "download", "Book.mp3");
            _item.IndexerFlags = IndexerFlags.Freeleech;

            var result = Subject.Calculate(_item, tracked ? new DownloadClientItem { DownloadId = "owned-download", Title = "Unparsed", DownloadClientInfo = new DownloadClientItemClientInfo { Id = 7 } } : null);

            result.IntendedScore.Should().Be(score);
        }

        [Test]
        public void preview_does_not_mutate_source_or_recording_metadata_and_uses_the_same_naming_inputs()
        {
            var originalPath = _item.Path;
            BookFile metadata = null;
            Mocker.GetMock<IBuildFileNames>().Setup(builder => builder.BuildBookFileName(_item.Author, _item.Edition, It.IsAny<BookFile>(), null, It.IsAny<List<CustomFormat>>()))
                .Callback<Author, Edition, BookFile, NamingConfig, List<CustomFormat>>((author, edition, file, config, formats) =>
                {
                    file.Path.Should().Be(originalPath);
                    file.EditionId.Should().Be(30);
                    file.MediaInfo.Should().BeSameAs(_item.FileTrackInfo.MediaInfo);
                    file.Part.Should().Be(1);
                    file.PartCount.Should().Be(2);
                    formats.Should().Contain(_formats[0]);
                    metadata = file;
                }).Returns("Book");

            Subject.Calculate(_item, null);

            metadata.Should().NotBeNull();
            _item.Path.Should().Be(originalPath);
            _item.Edition.ForeignEditionId.Should().Be("audible:us:B000000001");
            _item.Edition.Asin.Should().Be("B000000001");
            _item.Edition.AudibleRegion.Should().Be("us");
            _item.ReleaseGroupOverride.Should().BeNull();
        }
    }
}
