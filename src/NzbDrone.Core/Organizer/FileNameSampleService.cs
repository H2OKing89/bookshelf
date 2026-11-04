using System.Collections.Generic;
using NzbDrone.Core.Books;
using NzbDrone.Core.CustomFormats;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Qualities;

namespace NzbDrone.Core.Organizer
{
    public interface IFilenameSampleService
    {
        SampleResult GetStandardTrackSample(NamingConfig nameSpec);
        SampleResult GetMultiDiscTrackSample(NamingConfig nameSpec);
        string GetAuthorFolderSample(NamingConfig nameSpec);
    }

    public class FileNameSampleService : IFilenameSampleService
    {
        private readonly IBuildFileNames _buildFileNames;

        private static Author _standardAuthor;
        private static Book _standardBook;
        private static Edition _standardEdition;
        private static BookFile _singleTrackFile;
        private static BookFile _multiTrackFile;
        private static List<CustomFormat> _customFormats;

        public FileNameSampleService(IBuildFileNames buildFileNames)
        {
            _buildFileNames = buildFileNames;

            _standardAuthor = new Author
            {
                Metadata = new AuthorMetadata
                {
                    Name = "pirate aba",
                    Disambiguation = "Author",
                    NameLastFirst = "pirate aba"
                }
            };

            var series = new Series
            {
                Title = "The Wandering Inn"
            };

            var seriesLink = new SeriesBookLink
            {
                Position = "20",
                IsPrimary = true,
                Series = series
            };

            _standardBook = new Book
            {
                Title = "Memory's Flame",
                ReleaseDate = new System.DateTime(2026, 11, 3),
                Author = _standardAuthor,
                AuthorMetadata = _standardAuthor.Metadata.Value,
                SeriesLinks = new List<SeriesBookLink> { seriesLink }
            };

            _standardEdition = new Edition
            {
                Title = "Memory's Flame",
                Subtitle = "The Wandering Inn, Book 20",
                Asin = "B0HGBVHDM2",
                AudibleRegion = "us",
                Narrators = new List<string> { "Erin Bennett" },
                Language = "eng",
                Monitored = true,
                ReleaseDate = new System.DateTime(2026, 11, 3),
                Book = _standardBook
            };
            _standardBook.Editions = new List<Edition> { _standardEdition };

            _customFormats = new List<CustomFormat>
            {
                new CustomFormat
                {
                    Name = "Surround Sound",
                    IncludeCustomFormatWhenRenaming = true
                },
                new CustomFormat
                {
                    Name = "Unabridged",
                    IncludeCustomFormatWhenRenaming = true
                }
            };

            var mediaInfo = new MediaInfoModel()
            {
                AudioFormat = "AAC",
                AudioChannels = 2,
                AudioBitrate = 128,
                AudioBits = 16,
                AudioSampleRate = 44100
            };

            _singleTrackFile = new BookFile
            {
                Quality = new QualityModel(Quality.M4B),
                Path = "/audiobooks/Memory's Flame.m4b",
                SceneName = "Memory's Flame",
                ReleaseGroup = "RlsGrp",
                MediaInfo = mediaInfo,
                Edition = _standardEdition,
                Part = 1,
                PartCount = 1
            };

            _multiTrackFile = new BookFile
            {
                Quality = new QualityModel(Quality.MP3, new Revision(2)),
                Path = "/audiobooks/Memory's Flame - 01.mp3",
                SceneName = "Memory's Flame",
                ReleaseGroup = "RlsGrp",
                MediaInfo = new MediaInfoModel
                {
                    AudioFormat = "MP3", AudioChannels = 2, AudioBitrate = 128, AudioBits = 16, AudioSampleRate = 44100
                },
                Edition = _standardEdition,
                Part = 1,
                PartCount = 10
            };
        }

        public SampleResult GetStandardTrackSample(NamingConfig nameSpec)
        {
            var result = new SampleResult
            {
                FileName = BuildTrackSample(_standardAuthor, _singleTrackFile, nameSpec),
                Author = _standardAuthor,
                Book = _standardBook,
                BookFile = _singleTrackFile
            };

            return result;
        }

        public SampleResult GetMultiDiscTrackSample(NamingConfig nameSpec)
        {
            var result = new SampleResult
            {
                FileName = BuildTrackSample(_standardAuthor, _multiTrackFile, nameSpec),
                Author = _standardAuthor,
                Book = _standardBook,
                BookFile = _multiTrackFile
            };

            return result;
        }

        public string GetAuthorFolderSample(NamingConfig nameSpec)
        {
            return _buildFileNames.GetAuthorFolder(_standardAuthor, nameSpec);
        }

        private string BuildTrackSample(Author author, BookFile bookFile, NamingConfig nameSpec)
        {
            try
            {
                return _buildFileNames.BuildBookFileName(author, bookFile.Edition.Value, bookFile, nameSpec, _customFormats);
            }
            catch (NamingFormatException)
            {
                return string.Empty;
            }
        }
    }
}
