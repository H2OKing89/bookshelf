using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using FizzWare.NBuilder;
using FluentAssertions;
using FluentValidation;
using FluentValidation.Results;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.MusicTests
{
    [TestFixture]
    public class AddAuthorFixture : CoreTest<AddAuthorService>
    {
        private Author _fakeAuthor;

        [SetUp]
        public void Setup()
        {
            _fakeAuthor = Builder<Author>
                .CreateNew()
                .With(s => s.Path = null)
                .Build();
            _fakeAuthor.Books = new List<Book>();

            Mocker.GetMock<IAuthorService>()
                .Setup(s => s.AddAuthor(It.IsAny<Author>(), It.IsAny<bool>()))
                .Returns<Author, bool>((author, _) => author);
        }

        private void GivenValidAuthor(string readarrId)
        {
            Mocker.GetMock<IProvideAuthorInfo>()
                .Setup(s => s.GetAuthorInfo(readarrId, false))
                .Returns(_fakeAuthor);
        }

        private void GivenValidPath()
        {
            Mocker.GetMock<IBuildAuthorPaths>()
                  .Setup(s => s.BuildPath(It.IsAny<Author>(), false))
                  .Returns<Author, bool>((author, _) => Path.Combine(author.RootFolderPath, author.Name));

            Mocker.GetMock<IAddAuthorValidator>()
                  .Setup(s => s.Validate(It.IsAny<Author>()))
                  .Returns(new ValidationResult());
        }

        [Test]
        public void prepared_author_should_fetch_once_before_saving_with_the_final_options()
        {
            var newAuthor = new Author
            {
                ForeignAuthorId = "ce09ea31-3d4a-4487-a797-e315175457a0",
                RootFolderPath = Path.GetTempPath(),
                MetadataProfileId = 42,
                Tags = new HashSet<int> { 7 }
            };
            GivenValidAuthor(newAuthor.ForeignAuthorId);
            GivenValidPath();
            var prepared = Subject.PrepareAuthor(new Author { ForeignAuthorId = newAuthor.ForeignAuthorId });
            Mocker.GetMock<IAuthorMetadataService>().Verify(service => service.Upsert(It.IsAny<AuthorMetadata>()), Times.Never());
            Mocker.GetMock<IAuthorService>().Verify(service => service.AddAuthor(It.IsAny<Author>(), It.IsAny<bool>()), Times.Never());

            var added = Subject.AddAuthor(newAuthor, prepared);

            added.MetadataProfileId.Should().Be(42);
            added.Tags.Should().Equal(7);
            added.Path.Should().Be(Path.Combine(newAuthor.RootFolderPath, _fakeAuthor.Name));
            Mocker.GetMock<IProvideAuthorInfo>().Verify(provider => provider.GetAuthorInfo(newAuthor.ForeignAuthorId, false), Times.Once());
            Mocker.GetMock<IAuthorMetadataService>().Verify(service => service.Upsert(prepared.Metadata.Value), Times.Once());
        }

        [Test]
        public void cancelled_author_preparation_should_not_save_metadata_or_author()
        {
            using var source = new CancellationTokenSource();
            var newAuthor = new Author { ForeignAuthorId = "ce09ea31-3d4a-4487-a797-e315175457a0" };
            Mocker.GetMock<IProvideAuthorInfo>().Setup(provider => provider.GetAuthorInfo(newAuthor.ForeignAuthorId, false))
                .Returns(() =>
                {
                    source.Cancel();
                    throw new OperationCanceledException(source.Token);
                });
            using var scope = CommandExecutionContext.Begin(source.Token);

            Assert.Throws<OperationCanceledException>(() => Subject.PrepareAuthor(newAuthor));

            Mocker.GetMock<IAuthorMetadataService>().Verify(service => service.Upsert(It.IsAny<AuthorMetadata>()), Times.Never());
            Mocker.GetMock<IAuthorService>().Verify(service => service.AddAuthor(It.IsAny<Author>(), It.IsAny<bool>()), Times.Never());
        }

        [Test]
        public void should_be_able_to_add_a_author_without_passing_in_name()
        {
            var newAuthor = new Author
            {
                ForeignAuthorId = "ce09ea31-3d4a-4487-a797-e315175457a0",
                RootFolderPath = @"C:\Test\Music"
            };

            GivenValidAuthor(newAuthor.ForeignAuthorId);
            GivenValidPath();

            var author = Subject.AddAuthor(newAuthor);

            author.Name.Should().Be(_fakeAuthor.Name);
        }

        [Test]
        public void should_have_proper_path()
        {
            var newAuthor = new Author
            {
                ForeignAuthorId = "ce09ea31-3d4a-4487-a797-e315175457a0",
                RootFolderPath = @"C:\Test\Music"
            };

            GivenValidAuthor(newAuthor.ForeignAuthorId);
            GivenValidPath();

            var author = Subject.AddAuthor(newAuthor);

            author.Path.Should().Be(Path.Combine(newAuthor.RootFolderPath, _fakeAuthor.Name));
        }

        [Test]
        public void should_use_the_existing_folder_selected_by_the_path_builder()
        {
            var newAuthor = new Author
            {
                ForeignAuthorId = "ce09ea31-3d4a-4487-a797-e315175457a0",
                RootFolderPath = Path.GetTempPath()
            };
            var existingPath = Path.Combine(newAuthor.RootFolderPath, "Existing alias");
            GivenValidAuthor(newAuthor.ForeignAuthorId);
            GivenValidPath();
            Mocker.GetMock<IBuildAuthorPaths>()
                .Setup(x => x.BuildPath(It.IsAny<Author>(), false)).Returns(existingPath);

            Subject.AddAuthor(newAuthor).Path.Should().Be(existingPath);
        }

        [Test]
        public void should_preserve_an_explicit_author_path()
        {
            var newAuthor = new Author
            {
                ForeignAuthorId = "ce09ea31-3d4a-4487-a797-e315175457a0",
                Path = Path.Combine(Path.GetTempPath(), "My custom folder")
            };
            GivenValidAuthor(newAuthor.ForeignAuthorId);
            GivenValidPath();

            Subject.AddAuthor(newAuthor).Path.Should().Be(newAuthor.Path);

            Mocker.GetMock<IBuildAuthorPaths>()
                .Verify(x => x.BuildPath(It.IsAny<Author>(), It.IsAny<bool>()), Times.Never());
        }

        [Test]
        public void should_throw_if_author_validation_fails()
        {
            var newAuthor = new Author
            {
                ForeignAuthorId = "ce09ea31-3d4a-4487-a797-e315175457a0",
                Path = @"C:\Test\Music\Name1"
            };

            GivenValidAuthor(newAuthor.ForeignAuthorId);

            Mocker.GetMock<IAddAuthorValidator>()
                  .Setup(s => s.Validate(It.IsAny<Author>()))
                  .Returns(new ValidationResult(new List<ValidationFailure>
                                                {
                                                    new ValidationFailure("Path", "Test validation failure")
                                                }));

            Assert.Throws<ValidationException>(() => Subject.AddAuthor(newAuthor));
        }

        [Test]
        public void should_throw_if_author_cannot_be_found()
        {
            var newAuthor = new Author
            {
                ForeignAuthorId = "ce09ea31-3d4a-4487-a797-e315175457a0",
                Path = @"C:\Test\Music\Name1"
            };

            Mocker.GetMock<IProvideAuthorInfo>()
                  .Setup(s => s.GetAuthorInfo(newAuthor.ForeignAuthorId, false))
                  .Throws(new AuthorNotFoundException(newAuthor.ForeignAuthorId));

            Mocker.GetMock<IAddAuthorValidator>()
                  .Setup(s => s.Validate(It.IsAny<Author>()))
                  .Returns(new ValidationResult(new List<ValidationFailure>
                                                {
                                                    new ValidationFailure("Path", "Test validation failure")
                                                }));

            Assert.Throws<ValidationException>(() => Subject.AddAuthor(newAuthor));

            ExceptionVerification.ExpectedErrors(1);
        }

        [Test]
        public void should_disambiguate_if_author_folder_exists()
        {
            var newAuthor = new Author
            {
                ForeignAuthorId = "ce09ea31-3d4a-4487-a797-e315175457a0",
                Path = @"C:\Test\Music\Name1",
            };

            _fakeAuthor.Metadata = Builder<AuthorMetadata>.CreateNew().With(x => x.Disambiguation = "Disambiguation").Build();

            GivenValidAuthor(newAuthor.ForeignAuthorId);
            GivenValidPath();

            Mocker.GetMock<IAuthorService>()
                .Setup(x => x.AuthorPathExists(newAuthor.Path))
                .Returns(true);

            var author = Subject.AddAuthor(newAuthor);
            author.Path.Should().Be(newAuthor.Path + " (Disambiguation)");
        }

        [Test]
        public void should_disambiguate_with_numbers_if_author_folder_still_exists()
        {
            var newAuthor = new Author
            {
                ForeignAuthorId = "ce09ea31-3d4a-4487-a797-e315175457a0",
                Path = @"C:\Test\Music\Name1",
            };

            _fakeAuthor.Metadata = Builder<AuthorMetadata>.CreateNew().With(x => x.Disambiguation = "Disambiguation").Build();

            GivenValidAuthor(newAuthor.ForeignAuthorId);
            GivenValidPath();

            Mocker.GetMock<IAuthorService>()
                .Setup(x => x.AuthorPathExists(newAuthor.Path))
                .Returns(true);

            Mocker.GetMock<IAuthorService>()
                .Setup(x => x.AuthorPathExists(newAuthor.Path + " (Disambiguation)"))
                .Returns(true);

            Mocker.GetMock<IAuthorService>()
                .Setup(x => x.AuthorPathExists(newAuthor.Path + " (Disambiguation) (1)"))
                .Returns(true);

            Mocker.GetMock<IAuthorService>()
                .Setup(x => x.AuthorPathExists(newAuthor.Path + " (Disambiguation) (2)"))
                .Returns(true);

            var author = Subject.AddAuthor(newAuthor);
            author.Path.Should().Be(newAuthor.Path + " (Disambiguation) (3)");
        }

        [Test]
        public void should_disambiguate_with_numbers_if_author_folder_exists_and_no_disambiguation()
        {
            var newAuthor = new Author
            {
                ForeignAuthorId = "ce09ea31-3d4a-4487-a797-e315175457a0",
                Path = @"C:\Test\Music\Name1",
            };

            _fakeAuthor.Metadata = Builder<AuthorMetadata>.CreateNew().With(x => x.Disambiguation = string.Empty).Build();

            GivenValidAuthor(newAuthor.ForeignAuthorId);
            GivenValidPath();

            Mocker.GetMock<IAuthorService>()
                .Setup(x => x.AuthorPathExists(newAuthor.Path))
                .Returns(true);

            Mocker.GetMock<IAuthorService>()
                .Setup(x => x.AuthorPathExists(newAuthor.Path + " (1)"))
                .Returns(true);

            Mocker.GetMock<IAuthorService>()
                .Setup(x => x.AuthorPathExists(newAuthor.Path + " (2)"))
                .Returns(true);

            var author = Subject.AddAuthor(newAuthor);
            author.Path.Should().Be(newAuthor.Path + " (3)");
        }
    }
}
