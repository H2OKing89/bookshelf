using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Core.Books;
using NzbDrone.Core.Books.Events;
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.Events;
using NzbDrone.Core.MediaFiles.RecordingReview;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.MediaFiles
{
    [TestFixture]
    public class RecordingReviewServiceFixture : CoreTest<RecordingReviewService>
    {
        private const string OwnedAsin = "B011111111";
        private BookFile _file;
        private Book _book;
        private Author _author;
        private Edition _edition;
        private AudioTag _tag;
        private Mock<IFileInfo> _stat;

        [SetUp]
        public void Setup()
        {
            _file = new BookFile
            {
                Id = 11,
                Path = @"C:\Synthetic\Owned Author\Owned Work [B011111111]\Owned Work [B011111111].m4b".AsOsAgnostic(),
                Size = 512,
                Modified = new DateTime(2026, 11, 21, 12, 0, 0, DateTimeKind.Utc),
                OriginalFilePath = "original-release.m4b",
                ReleaseGroup = "Owned"
            };
            _book = new Book { Id = 20, AuthorMetadataId = 30, ForeignBookId = "audible:us:" + OwnedAsin };
            _author = new Author { Id = 31, AuthorMetadataId = 30, Name = "Owned Author", Path = @"C:\Synthetic\Owned Author".AsOsAgnostic() };
            _edition = new Edition
            {
                Id = 40,
                BookId = _book.Id,
                Title = "Owned Work",
                ForeignEditionId = _book.ForeignBookId,
                Asin = OwnedAsin,
                AudibleRegion = "us",
                Narrators = new List<string> { "Owned Narrator" }
            };
            _tag = new AudioTag { Title = "Owned Work", BookAuthors = new[] { "Owned Author" }, Narrators = new[] { "Owned Narrator" } };
            _stat = new Mock<IFileInfo>();
            _stat.SetupGet(info => info.Exists).Returns(true);
            _stat.SetupGet(info => info.Attributes).Returns(FileAttributes.Normal);
            _stat.SetupGet(info => info.Length).Returns(_file.Size);
            _stat.SetupGet(info => info.LastWriteTimeUtc).Returns(_file.Modified);

            Mocker.GetMock<IMediaFileService>().Setup(files => files.Get(_file.Id)).Returns(_file);
            Mocker.GetMock<IMediaFileService>().Setup(files => files.GetFilesByBook(_book.Id)).Returns(new List<BookFile>());
            Mocker.GetMock<IDiskProvider>().Setup(provider => provider.GetFileInfo(It.IsAny<string>())).Returns(_stat.Object);
            Mocker.GetMock<IAudioTagService>().Setup(tags => tags.ReadAudioTag(It.IsAny<string>())).Returns(_tag);
            Mocker.GetMock<IBookRepository>().Setup(repository => repository.Find(_book.Id)).Returns(_book);
            Mocker.GetMock<IAuthorRepository>().Setup(repository => repository.GetAuthorsByMetadataId(It.IsAny<int[]>())).Returns(new List<Author> { _author });
            Catalog(_edition);
        }

        [Test]
        public void should_show_scoped_evidence_and_apply_one_explicit_association()
        {
            var result = Subject.Inspect(_file.Id);
            result.Reason.Should().Be("exactCatalogMatch");
            result.FilenameAsins.Should().Equal(OwnedAsin);
            result.FolderAsins.Should().Equal(OwnedAsin);
            result.EmbeddedAsins.Should().BeEmpty();
            result.TagNarrators.Should().Equal("Owned Narrator");
            result.CurrentRecording.Should().BeNull();
            var candidate = result.Candidates.Should().ContainSingle().Subject;
            candidate.RecordingId.Should().Be(_edition.ForeignEditionId);
            candidate.Revision.Should().HaveLength(64);
            VerifyNoAssociation();

            Mocker.GetMock<IRecordingReviewRepository>().Setup(repository => repository.Associate(_file, _edition, _book, _author)).Returns(true);

            var applied = Subject.Associate(_file.Id, _edition.Id, candidate.Revision);

            applied.Reason.Should().Be("alreadyAssociated");
            applied.CurrentRecording.RecordingId.Should().Be(candidate.RecordingId);
            applied.CurrentRecording.Revision.Should().BeNull();
            _file.OriginalFilePath.Should().Be("original-release.m4b");
            _file.ReleaseGroup.Should().Be("Owned");
            Mocker.GetMock<IEventAggregator>().Verify(
                events => events.PublishEvent(It.Is<BookUpdatedEvent>(message => message.Book.Id == _book.Id && message.Book.AuthorId == _author.Id)),
                Times.Once);
            Mocker.GetMock<IEventAggregator>().Verify(events => events.PublishEvent(It.Is<BookFileAddedEvent>(message => message.BookFile.Id == _file.Id)), Times.Once);
            Mocker.GetMock<IAudioTagService>().Verify(tags => tags.WriteTags(It.IsAny<BookFile>(), It.IsAny<bool>(), It.IsAny<bool>()), Times.Never);
        }

        [Test]
        public void should_keep_file_and_nearest_folder_disagreement_blocked()
        {
            _file.Path = @"C:\Synthetic\Owned Author\Another Work [B022222222]\CD 1\Owned Work [B011111111].m4b".AsOsAgnostic();

            var result = Subject.Inspect(_file.Id);

            result.Reason.Should().Be("identifierConflict");
            result.FilenameAsins.Should().Equal(OwnedAsin);
            result.FolderAsins.Should().Equal("B022222222");
            result.Candidates.Should().OnlyContain(candidate => candidate.Revision == null);
            VerifyNoAssociation();
        }

        [Test]
        public void should_show_two_markets_without_choosing_a_recording()
        {
            Catalog(_edition, new Edition
            {
                Id = 41,
                BookId = _book.Id,
                ForeignEditionId = "audible:uk:" + OwnedAsin,
                AudibleRegion = "uk",
                Asin = OwnedAsin
            });

            var result = Subject.Inspect(_file.Id);

            result.Reason.Should().Be("ambiguousCatalogMatch");
            result.Candidates.Should().HaveCount(2).And.OnlyContain(candidate => candidate.Revision == null);
        }

        [Test]
        public void should_refuse_stale_file_state_and_replaced_catalog_identity()
        {
            var revision = Subject.Inspect(_file.Id).Candidates[0].Revision;
            _stat.SetupGet(info => info.Length).Returns(_file.Size + 1);

            Assert.Throws<NzbDroneClientException>(() => Subject.Associate(_file.Id, _edition.Id, revision));

            _stat.SetupGet(info => info.Length).Returns(_file.Size);
            _edition.Title = "Revised title";
            Assert.Throws<NzbDroneClientException>(() => Subject.Associate(_file.Id, _edition.Id, revision));
            VerifyNoAssociation();
        }

        [Test]
        public void should_explain_a_different_performance()
        {
            _tag.Narrators = new[] { "Another Narrator" };

            var review = Subject.Inspect(_file.Id);

            review.Reason.Should().Be("differentPerformance");
            review.Candidates[0].Revision.Should().BeNull();
        }

        [Test]
        public void should_distinguish_no_local_catalog_result_from_no_identifier()
        {
            Catalog();
            Subject.Inspect(_file.Id).Reason.Should().Be("catalogMissing");

            _file.Path = @"C:\Synthetic\Owned Author\Owned Work.m4b".AsOsAgnostic();
            Subject.Inspect(_file.Id).Reason.Should().Be("insufficientEvidence");
            _tag.BookAuthors = Array.Empty<string>();
            Subject.Inspect(_file.Id).Reason.Should().Be("authorMissing");
        }

        [TestCase(true)]
        [TestCase(false)]
        public void should_route_occupied_and_multipart_targets_to_manual_review(bool occupied)
        {
            if (occupied)
            {
                Mocker.GetMock<IMediaFileService>().Setup(files => files.GetFilesByBook(_book.Id))
                    .Returns(new List<BookFile> { new BookFile { Id = 12, EditionId = _edition.Id } });
            }
            else
            {
                _tag.TrackCount = 6;
            }

            var result = Subject.Inspect(_file.Id);

            result.Reason.Should().Be("existingOrMultipartRecording");
            result.Candidates[0].Revision.Should().BeNull();
        }

        private void Catalog(params Edition[] editions)
        {
            Mocker.GetMock<IRecordingReviewRepository>()
                .Setup(repository => repository.FindCatalogEditions(It.IsAny<string[]>(), It.IsAny<string[]>(), It.IsAny<int>()))
                .Returns(new List<Edition>(editions));
        }

        private void VerifyNoAssociation()
        {
            Mocker.GetMock<IRecordingReviewRepository>()
                .Verify(repository => repository.Associate(It.IsAny<BookFile>(), It.IsAny<Edition>(), It.IsAny<Book>(), It.IsAny<Author>()), Times.Never);
        }
    }
}
