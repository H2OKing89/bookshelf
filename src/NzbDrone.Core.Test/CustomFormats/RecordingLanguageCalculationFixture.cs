using System.Collections.Generic;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.CustomFormats;
using NzbDrone.Core.History;
using NzbDrone.Core.Languages;
using NzbDrone.Core.MediaFiles.BookImport;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.CustomFormats
{
    [TestFixture]
    public class RecordingLanguageCalculationFixture : CoreTest<CustomFormatCalculationService>
    {
        private readonly Author _author = new Author { Name = "Synthetic Author" };

        [SetUp]
        public void Setup()
        {
            Mocker.GetMock<ICustomFormatService>().Setup(service => service.All()).Returns(new List<CustomFormat>
            {
                new("English", new LanguageSpecification { Value = Language.English.Id }),
                new("French", new LanguageSpecification { Value = Language.French.Id }),
                new("Selected", new LanguageSpecification { Value = LanguageSpecification.SelectedEdition }),
                new("Unknown", new LanguageSpecification { Value = Language.Unknown.Id })
            });
        }

        [Test]
        public void release_scoring_should_prefer_stream_evidence_and_compare_with_the_selected_edition()
        {
            var remote = new RemoteBook
            {
                Author = _author,
                Release = new ReleaseInfo
                {
                    Languages = new List<Language> { Language.English },
                    MediaInfo = new MediaInfoModel { AudioStreams = new List<AudioStreamInfo> { new AudioStreamInfo { Language = "fre" } } }
                },
                Books = new List<Book>
                {
                    new Book { Editions = new List<Edition> { new Edition { Monitored = true, Language = "fr-FR" } } }
                }
            };

            Subject.ParseCustomFormat(remote, 1).Should().OnlyContain(format => format.Name == "French" || format.Name == "Selected")
                .And.HaveCount(2);

            remote.Books[0].AnyEditionOk = true;

            Subject.ParseCustomFormat(remote, 1).Should().ContainSingle().Which.Name.Should().Be("French");

            remote.Release = new ReleaseInfo { Title = "Book with English and French in the title" };

            Subject.ParseCustomFormat(remote, 1).Should().ContainSingle().Which.Name.Should().Be("Unknown");
        }

        [Test]
        public void imported_file_scoring_should_keep_tags_and_the_exact_linked_edition_across_rename()
        {
            var edition = new Edition { Id = 1, Language = "fra" };
            var local = new LocalBook
            {
                Path = System.IO.Path.Combine(TempFolder, "before.m4b"),
                Author = _author,
                Edition = edition,
                FileTrackInfo = new ParsedTrackInfo { Language = "en-US, fre" }
            };
            var file = ImportBookFileMetadata.Create(local, null, Mocker.GetMock<IHistoryService>().Object);

            file.TaggedLanguage.Should().Be("en-US, fre");
            Subject.ParseCustomFormat(file, _author).Should().HaveCount(3);

            file.Path = System.IO.Path.Combine(TempFolder, "renamed.m4b");

            Subject.ParseCustomFormat(file, _author).Should().HaveCount(3);

            file.TaggedLanguage = null;

            Subject.ParseCustomFormat(file, _author).Should().OnlyContain(format => format.Name == "French" || format.Name == "Selected")
                .And.HaveCount(2);
        }

        [Test]
        public void history_scoring_should_use_the_saved_release_evidence_without_inventing_language_for_legacy_rows()
        {
            var history = new EntityHistory
            {
                SourceTitle = "Owned Recording",
                Data = new Dictionary<string, string>
                {
                    ["recordingLanguages"] = "2,1",
                    ["selectedEditionLanguageId"] = "2"
                }
            };

            Subject.ParseCustomFormat(history, _author).Should().HaveCount(3);

            history.Data.Clear();

            Subject.ParseCustomFormat(history, _author).Should().ContainSingle().Which.Name.Should().Be("Unknown");
        }
    }
}
