using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FizzWare.NBuilder;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Core.Books;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Extras.Metadata;
using NzbDrone.Core.Extras.Metadata.Files;
using NzbDrone.Core.Housekeeping.Housekeepers;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.HealthCheck.Checks
{
    [TestFixture]
    public class DeleteBadMediaCoversFixture : CoreTest<DeleteBadMediaCovers>
    {
        private List<MetadataFile> _metadata;
        private List<Author> _author;

        [SetUp]
        public void Setup()
        {
            _author = Builder<Author>.CreateListOfSize(1)
                .All()
                .With(c => c.Path = "C:\\Music\\".AsOsAgnostic())
                .Build().ToList();

            _metadata = Builder<MetadataFile>.CreateListOfSize(1)
               .Build().ToList();

            Mocker.GetMock<IAuthorService>()
                .Setup(c => c.AllAuthorPaths())
                .Returns(_author.ToDictionary(x => x.Id, x => x.Path));

            Mocker.GetMock<IMetadataFileService>()
                .Setup(c => c.GetFilesByAuthor(_author.First().Id))
                .Returns(_metadata);

            Mocker.GetMock<IConfigService>().SetupGet(c => c.CleanupMetadataImages).Returns(true);
        }

        [Test]
        public void should_not_process_non_image_files()
        {
            _metadata.First().RelativePath = "book\\file.xml".AsOsAgnostic();
            _metadata.First().Type = MetadataType.BookMetadata;

            Subject.Clean();

            Mocker.GetMock<IDiskProvider>().Verify(c => c.OpenReadStream(It.IsAny<string>()), Times.Never());
        }

        [Test]
        public void should_not_process_images_before_tvdb_switch()
        {
            _metadata.First().LastUpdated = new DateTime(2014, 12, 25);

            Subject.Clean();

            Mocker.GetMock<IDiskProvider>().Verify(c => c.OpenReadStream(It.IsAny<string>()), Times.Never());
        }

        [Test]
        public void should_not_run_if_flag_is_false()
        {
            Mocker.GetMock<IConfigService>().SetupGet(c => c.CleanupMetadataImages).Returns(false);

            Subject.Clean();

            Mocker.GetMock<IConfigService>().VerifySet(c => c.CleanupMetadataImages = true, Times.Never());
            Mocker.GetMock<IAuthorService>().Verify(c => c.GetAllAuthors(), Times.Never());

            AssertImageWasNotRemoved();
        }

        [Test]
        public void should_set_clean_flag_to_false()
        {
            _metadata.First().LastUpdated = new DateTime(2014, 12, 25);

            Subject.Clean();

            Mocker.GetMock<IConfigService>().VerifySet(c => c.CleanupMetadataImages = false, Times.Once());
        }

        [Test]
        public void should_delete_html_images()
        {
            var imagePath = "C:\\Music\\Book\\image.jpg".AsOsAgnostic();
            _metadata.First().LastUpdated = new DateTime(2014, 12, 29);
            _metadata.First().RelativePath = "Book\\image.jpg".AsOsAgnostic();
            _metadata.First().Type = MetadataType.AuthorImage;

            Mocker.GetMock<IDiskProvider>()
                .Setup(c => c.OpenReadStream(imagePath))
                .Returns(new FileStream(GetTestPath("Files/html_image.jpg"), FileMode.Open, FileAccess.Read));

            Subject.Clean();

            Mocker.GetMock<IDiskProvider>().Verify(c => c.DeleteFile(imagePath), Times.Once());
            Mocker.GetMock<IMetadataFileService>().Verify(c => c.Delete(_metadata.First().Id), Times.Once());
        }

        [Test]
        public void should_delete_empty_images()
        {
            var imagePath = "C:\\Music\\Book\\image.jpg".AsOsAgnostic();
            _metadata.First().LastUpdated = new DateTime(2014, 12, 29);
            _metadata.First().Type = MetadataType.BookImage;
            _metadata.First().RelativePath = "Book\\image.jpg".AsOsAgnostic();

            Mocker.GetMock<IDiskProvider>()
                .Setup(c => c.OpenReadStream(imagePath))
                              .Returns(new FileStream(GetTestPath("Files/emptyfile.txt"), FileMode.Open, FileAccess.Read));

            Subject.Clean();

            Mocker.GetMock<IDiskProvider>().Verify(c => c.DeleteFile(imagePath), Times.Once());
            Mocker.GetMock<IMetadataFileService>().Verify(c => c.Delete(_metadata.First().Id), Times.Once());
        }

        [Test]
        public void should_not_delete_non_html_files()
        {
            var imagePath = "C:\\Music\\Book\\image.jpg".AsOsAgnostic();
            _metadata.First().LastUpdated = new DateTime(2014, 12, 29);
            _metadata.First().RelativePath = "Book\\image.jpg".AsOsAgnostic();

            Mocker.GetMock<IDiskProvider>()
                .Setup(c => c.OpenReadStream(imagePath))
                              .Returns(new FileStream(GetTestPath("Files/Queue.txt"), FileMode.Open, FileAccess.Read));

            Subject.Clean();
            AssertImageWasNotRemoved();
        }

        [TestCase(false)]
        [TestCase(true)]
        public void should_delete_invalid_headers_when_reads_are_partial_or_the_file_is_truncated(bool truncated)
        {
            var imagePath = "C:\\Music\\Book\\image.jpg".AsOsAgnostic();
            var source = Path.Combine(TempFolder, "header.jpg");
            File.WriteAllText(source, truncated ? "image" : "<html>not an image");
            _metadata.First().LastUpdated = new DateTime(2014, 12, 29);
            _metadata.First().RelativePath = "Book\\image.jpg".AsOsAgnostic();
            _metadata.First().Type = MetadataType.BookImage;
            Mocker.GetMock<IDiskProvider>().Setup(provider => provider.OpenReadStream(imagePath))
                .Returns(new PartialReadFileStream(source, truncated));

            Subject.Clean();

            Mocker.GetMock<IDiskProvider>().Verify(provider => provider.DeleteFile(imagePath), Times.Once());
            Mocker.GetMock<IMetadataFileService>().Verify(service => service.Delete(_metadata.First().Id), Times.Once());
        }

        private void AssertImageWasNotRemoved()
        {
            Mocker.GetMock<IDiskProvider>().Verify(c => c.DeleteFile(It.IsAny<string>()), Times.Never());
            Mocker.GetMock<IMetadataFileService>().Verify(c => c.Delete(It.IsAny<int>()), Times.Never());
        }

        private sealed class PartialReadFileStream : FileStream
        {
            private readonly bool _reportLongerLength;

            public PartialReadFileStream(string path, bool reportLongerLength)
                : base(path, FileMode.Open, FileAccess.Read)
            {
                _reportLongerLength = reportLongerLength;
            }

            public override long Length => _reportLongerLength ? base.Length + 10 : base.Length;

            public override int Read(byte[] buffer, int offset, int count) => base.Read(buffer, offset, Math.Min(2, count));

            public override int Read(Span<byte> buffer) => base.Read(buffer.Slice(0, Math.Min(2, buffer.Length)));
        }
    }
}
