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

        private List<BookFileResource> Update(List<BookFile> files, QualityModel quality, string releaseGroup = null)
        {
            var ids = files.Select(file => file.Id).ToList();
            Mocker.GetMock<IMediaFileService>().Setup(service => service.Get(ids)).Returns(files);
            var response = Subject.SetQuality(new BookFileListResource { BookFileIds = ids, Quality = quality, ReleaseGroup = releaseGroup });
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
            Mocker.GetMock<IUpgradableSpecification>().Verify(
                specification => specification.QualityCutoffNotMet(
                    It.IsAny<QualityProfile>(), It.IsAny<QualityModel>(), It.IsAny<QualityModel>()),
                Times.Never());
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

        [Test]
        public void should_preserve_release_groups_for_quality_only_clients()
        {
            var first = File(1);
            first.ReleaseGroup = "First-Crew";
            var second = File(2);
            second.ReleaseGroup = "SecondCrew";

            var results = Update(new List<BookFile> { first, second }, new QualityModel(Quality.M4B));

            results.Select(result => result.ReleaseGroup).Should().Equal("First-Crew", "SecondCrew");
            first.ReleaseGroup.Should().Be("First-Crew");
            second.ReleaseGroup.Should().Be("SecondCrew");
        }

        [TestCase("  Audio-Crew  ", "Audio-Crew")]
        [TestCase("", "")]
        [TestCase("   ", "")]
        public void should_update_groups_without_changing_quality_or_file_identity(string group, string expected)
        {
            var first = File(1);
            first.ReleaseGroup = "OldCrew";
            first.SceneName = "Original.Release-OldCrew";
            first.OriginalFilePath = "/downloads/original.m4b";
            first.DateAdded = new System.DateTime(2025, 1, 1);
            var second = File(2);
            var firstQuality = first.Quality;
            var secondQuality = second.Quality;

            var results = Update(new List<BookFile> { first, second }, null, group);

            results.Select(result => result.ReleaseGroup).Should().Equal(expected, expected);
            first.Quality.Should().BeSameAs(firstQuality);
            second.Quality.Should().BeSameAs(secondQuality);
            first.Id.Should().Be(1);
            first.Path.Should().Be("/books/1.m4b");
            first.SceneName.Should().Be("Original.Release-OldCrew");
            first.OriginalFilePath.Should().Be("/downloads/original.m4b");
            first.DateAdded.Should().Be(new System.DateTime(2025, 1, 1));
        }

        [TestCase(null, "OriginalCrew")]
        [TestCase("", "")]
        [TestCase(" NewCrew ", "NewCrew")]
        public void should_support_group_only_single_file_updates(string group, string expected)
        {
            var file = File(5);
            file.ReleaseGroup = "OriginalCrew";
            var quality = file.Quality;
            Mocker.GetMock<IMediaFileService>().Setup(service => service.Get(file.Id)).Returns(file);

            Subject.SetQuality(new BookFileResource { Id = file.Id, ReleaseGroup = group });

            file.ReleaseGroup.Should().Be(expected);
            file.Quality.Should().BeSameAs(quality);
            Mocker.GetMock<IMediaFileService>().Verify(service => service.Update(file), Times.Once());
        }

        [Test]
        public void should_expose_saved_groups_for_mapped_and_unmapped_files()
        {
            var mapped = File(1, new Author { Id = 11, QualityProfile = new QualityProfile() });
            mapped.ReleaseGroup = "MappedCrew";
            var unmapped = File(2);
            unmapped.ReleaseGroup = "UnmappedCrew";

            var results = Update(new List<BookFile> { mapped, unmapped }, null);

            results.Select(result => result.ReleaseGroup).Should().Equal("MappedCrew", "UnmappedCrew");
        }
    }
}
