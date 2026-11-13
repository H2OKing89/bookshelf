using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.DecisionEngine.Specifications;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Profiles.Qualities;
using NzbDrone.Core.Qualities;
using NzbDrone.Test.Common;
using Readarr.Api.V1.BookFiles;

namespace NzbDrone.Integration.Test.ApiTests
{
    [TestFixture]
    public class BookFileQualityEditorFixture : TestBase<BookFileController>
    {
        private static BookFile File(int id, Author author = null)
        {
            return new BookFile
            {
                Id = id,
                Path = $"/books/{id}.m4b",
                Quality = new QualityModel(Quality.MP3),
                Author = author,
                EditionId = author == null ? 0 : id,
                Edition = author == null ? null : new Edition { Id = id, BookId = id + 100 }
            };
        }

        private List<BookFileResource> Update(List<BookFile> files, QualityModel quality)
        {
            var ids = files.Select(file => file.Id).ToList();
            Mocker.GetMock<IMediaFileService>().Setup(service => service.Get(ids)).Returns(files);
            var response = Subject.SetQuality(new BookFileListResource { BookFileIds = ids, Quality = quality });
            var accepted = response.Should().BeOfType<AcceptedResult>().Subject;
            Mocker.GetMock<IMediaFileService>().Verify(service => service.Update(files), Times.Once());
            return accepted.Value.Should().BeOfType<List<BookFileResource>>().Subject;
        }

        [Test]
        public void should_return_unmapped_files_after_saving_quality_without_an_author()
        {
            var quality = new QualityModel(Quality.M4B);
            var file = File(1);

            var result = Update(new List<BookFile> { file }, quality).Single();

            result.Id.Should().Be(file.Id);
            result.AuthorId.Should().Be(0);
            result.BookId.Should().Be(0);
            result.Quality.Should().Be(quality);
            file.Quality.Should().Be(quality);
            Mocker.GetMock<IUpgradableSpecification>().Verify(specification => specification.QualityCutoffNotMet(
                It.IsAny<QualityProfile>(), It.IsAny<QualityModel>(), It.IsAny<QualityModel>()), Times.Never());
        }

        [Test]
        public void should_use_each_files_author_and_quality_profile_in_mixed_batches()
        {
            var firstProfile = new QualityProfile { Id = 7 };
            var secondProfile = new QualityProfile { Id = 8 };
            var first = File(1, new Author { Id = 11, QualityProfile = firstProfile });
            var second = File(2, new Author { Id = 22, QualityProfile = secondProfile });
            var unmapped = File(3);
            var quality = new QualityModel(Quality.M4B);
            Mocker.GetMock<IUpgradableSpecification>().Setup(specification => specification.QualityCutoffNotMet(firstProfile, quality, null)).Returns(true);
            Mocker.GetMock<IUpgradableSpecification>().Setup(specification => specification.QualityCutoffNotMet(secondProfile, quality, null)).Returns(false);

            var results = Update(new List<BookFile> { first, second, unmapped }, quality);

            results.Select(result => result.AuthorId).Should().Equal(11, 22, 0);
            results.Select(result => result.BookId).Should().Equal(101, 102, 0);
            results.Select(result => result.QualityCutoffNotMet).Should().Equal(true, false, false);
            results.Should().OnlyContain(result => result.Quality == quality);
            Mocker.GetMock<IUpgradableSpecification>().Verify(specification => specification.QualityCutoffNotMet(firstProfile, quality, null), Times.Once());
            Mocker.GetMock<IUpgradableSpecification>().Verify(specification => specification.QualityCutoffNotMet(secondProfile, quality, null), Times.Once());
        }

        [Test]
        public void should_return_an_empty_result_when_no_requested_files_exist()
        {
            Update(new List<BookFile>(), new QualityModel(Quality.M4B)).Should().BeEmpty();
        }
    }
}
