using System;
using System.Collections.Generic;
using System.IO;
using FluentAssertions;
using FluentValidation;
using FluentValidation.Results;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MusicTests
{
    [TestFixture]
    public class AddAuthorFromBookFixture : CoreTest<AddAuthorService>
    {
        private Author _request;
        private Book _recording;

        [SetUp]
        public void Setup()
        {
            _request = new Author
            {
                Metadata = new AuthorMetadata { Name = "Client supplied name", ForeignAuthorId = "audible:us:name:Client" },
                Path = Path.Combine(Path.GetTempPath(), "Charles Dickens"),
                RootFolderPath = Path.GetTempPath(),
                QualityProfileId = 4,
                MetadataProfileId = 8,
                Monitored = false
            };
            _recording = new Book
            {
                ForeignBookId = "audible:us:B08QSKXDG8",
                AuthorMetadata = new AuthorMetadata
                {
                    Name = "Charles Dickens", ForeignAuthorId = "audible:us:B000APYNYE", TitleSlug = "author-us-B000APYNYE"
                },
                Editions = new List<Edition>
                {
                    new Edition
                    {
                        ForeignEditionId = "audible:us:B08QSKXDG8", Asin = "B08QSKXDG8", AudibleRegion = "us", Monitored = true
                    }
                }
            };
            Mocker.GetMock<IAddAuthorValidator>().Setup(service => service.Validate(It.IsAny<Author>())).Returns(new ValidationResult());
            Mocker.GetMock<IAuthorMetadataService>().Setup(service => service.Upsert(It.IsAny<AuthorMetadata>()))
                .Callback<AuthorMetadata>(metadata => metadata.Id = 200).Returns(true);
            Mocker.GetMock<IAuthorService>().Setup(service => service.AddAuthor(It.IsAny<Author>(), false))
                .Returns<Author, bool>((author, _) => author);
        }

        [Test]
        public void should_create_a_missing_author_from_the_verified_recording_without_fetching_a_profile_or_bibliography()
        {
            var author = Subject.AddAuthorFromBook(_request, _recording);

            author.Metadata.Value.Should().BeSameAs(_recording.AuthorMetadata.Value);
            author.Name.Should().Be("Charles Dickens");
            author.ForeignAuthorId.Should().Be("audible:us:B000APYNYE");
            author.AuthorMetadataId.Should().Be(200);
            author.Path.Should().Be(_request.Path);
            author.QualityProfileId.Should().Be(4);
            author.MetadataProfileId.Should().Be(8);
            author.Monitored.Should().BeFalse();
            Mocker.GetMock<IProvideAuthorInfo>().Verify(service => service.GetAuthorInfo(It.IsAny<string>(), It.IsAny<bool>()), Times.Never());
            Mocker.GetMock<IAuthorService>().Verify(service => service.AddAuthor(author, false), Times.Once());
        }

        [TestCase("Asin")]
        [TestCase("AudibleRegion")]
        [TestCase("ForeignEditionId")]
        [TestCase("AuthorName")]
        [TestCase("ForeignBookId")]
        public void should_reject_incomplete_or_conflicting_recording_metadata_before_persistence(string field)
        {
            var edition = _recording.Editions.Value[0];
            if (field == "Asin")
            {
                edition.Asin = "B002ZEEDAW";
            }
            else if (field == "AudibleRegion")
            {
                edition.AudibleRegion = "uk";
            }
            else if (field == "ForeignEditionId")
            {
                edition.ForeignEditionId = "audible:uk:B08QSKXDG8";
            }
            else if (field == "AuthorName")
            {
                _recording.AuthorMetadata.Value.Name = null;
            }
            else
            {
                _recording.ForeignBookId = "12345";
            }

            Action add = () => Subject.AddAuthorFromBook(_request, _recording);

            add.Should().Throw<InvalidOperationException>();
            Mocker.GetMock<IAuthorMetadataService>().Verify(service => service.Upsert(It.IsAny<AuthorMetadata>()), Times.Never());
            Mocker.GetMock<IAuthorService>().Verify(service => service.AddAuthor(It.IsAny<Author>(), It.IsAny<bool>()), Times.Never());
        }

        [Test]
        public void should_still_validate_author_paths_and_profiles_before_persisting_a_recording_credit()
        {
            Mocker.GetMock<IAddAuthorValidator>().Setup(service => service.Validate(It.IsAny<Author>()))
                .Returns(new ValidationResult(new[] { new ValidationFailure("Path", "Invalid library path") }));

            Action add = () => Subject.AddAuthorFromBook(_request, _recording);

            add.Should().Throw<ValidationException>();
            Mocker.GetMock<IAuthorMetadataService>().Verify(service => service.Upsert(It.IsAny<AuthorMetadata>()), Times.Never());
            Mocker.GetMock<IAuthorService>().Verify(service => service.AddAuthor(It.IsAny<Author>(), It.IsAny<bool>()), Times.Never());
        }
    }
}
