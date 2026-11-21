using System;
using System.Collections.Generic;
using FizzWare.NBuilder;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Books;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.DecisionEngine;
using NzbDrone.Core.Download;
using NzbDrone.Core.Download.Pending;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Parser;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Profiles.Qualities;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Download.Pending.PendingReleaseServiceTests
{
    [TestFixture]
    public class RemoveRejectedFixture : CoreTest<PendingReleaseService>
    {
        private DownloadDecision _temporarilyRejected;
        private Author _author;
        private Book _book;
        private QualityProfile _profile;
        private ReleaseInfo _release;
        private ParsedBookInfo _parsedBookInfo;
        private RemoteBook _remoteBook;

        [SetUp]
        public void Setup()
        {
            _author = Builder<Author>.CreateNew()
                                     .Build();

            _book = Builder<Book>.CreateNew()
                                       .Build();

            _profile = new QualityProfile
            {
                Name = "Test",
                Cutoff = Quality.MP3.Id,
                Items = new List<QualityProfileQualityItem>
                                   {
                                       new QualityProfileQualityItem { Allowed = true, Quality = Quality.MP3 },
                                       new QualityProfileQualityItem { Allowed = true, Quality = Quality.MP3 },
                                       new QualityProfileQualityItem { Allowed = true, Quality = Quality.MP3 }
                                   },
            };

            _author.QualityProfile = new LazyLoaded<QualityProfile>(_profile);

            _release = Builder<ReleaseInfo>.CreateNew()
                .With(release => release.PendingReleaseId = null)
                .With(release => release.PendingAuthorId = null)
                .Build();

            _parsedBookInfo = Builder<ParsedBookInfo>.CreateNew().Build();
            _parsedBookInfo.Quality = new QualityModel(Quality.MP3);

            _remoteBook = new RemoteBook();
            _remoteBook.Books = new List<Book> { _book };
            _remoteBook.Author = _author;
            _remoteBook.ParsedBookInfo = _parsedBookInfo;
            _remoteBook.Release = _release;

            _temporarilyRejected = new DownloadDecision(_remoteBook, new Rejection("Temp Rejected", RejectionType.Temporary));

            Mocker.GetMock<IPendingReleaseRepository>()
                  .Setup(s => s.All())
                  .Returns(new List<PendingRelease>());

            Mocker.GetMock<IAuthorService>()
                  .Setup(s => s.GetAuthor(It.IsAny<int>()))
                  .Returns(_author);

            Mocker.GetMock<IAuthorService>()
                  .Setup(s => s.FindAuthors(It.IsAny<IEnumerable<int>>()))
                  .Returns(new List<Author> { _author });

            Mocker.GetMock<IParsingService>()
                  .Setup(s => s.GetBooks(It.IsAny<ParsedBookInfo>(), _author, null))
                  .Returns(new List<Book> { _book });

            Mocker.GetMock<IPrioritizeDownloadDecision>()
                  .Setup(s => s.PrioritizeDecisions(It.IsAny<List<DownloadDecision>>()))
                  .Returns((List<DownloadDecision> d) => d);
        }

        private void GivenHeldRelease(string title, string indexer, DateTime publishDate)
        {
            var release = _release.JsonClone();
            release.Indexer = indexer;
            release.PublishDate = publishDate;

            var heldReleases = Builder<PendingRelease>.CreateListOfSize(1)
                                                   .All()
                                                   .With(h => h.AuthorId = _author.Id)
                                                   .With(h => h.Title = title)
                                                   .With(h => h.Release = release)
                                                   .Build();

            Mocker.GetMock<IPendingReleaseRepository>()
                  .Setup(s => s.All())
                  .Returns(heldReleases);
        }

        [Test]
        public void should_remove_if_it_is_the_same_release_from_the_same_indexer()
        {
            GivenHeldRelease(_release.Title, _release.Indexer, _release.PublishDate);

            Subject.Handle(new RssSyncCompleteEvent(new ProcessedDecisions(new List<DownloadDecision>(),
                                                                           new List<DownloadDecision>(),
                                                                           new List<DownloadDecision> { _temporarilyRejected })));

            VerifyDelete();
        }

        [Test]
        public void should_not_remove_if_title_is_different()
        {
            GivenHeldRelease(_release.Title + "-RP", _release.Indexer, _release.PublishDate);

            Subject.Handle(new RssSyncCompleteEvent(new ProcessedDecisions(new List<DownloadDecision>(),
                                                                           new List<DownloadDecision>(),
                                                                           new List<DownloadDecision> { _temporarilyRejected })));

            VerifyNoDelete();
        }

        [TestCase(RejectionCode.AboveMaximumSize)]
        [TestCase(RejectionCode.CustomFormatMinimumScore)]
        [TestCase(RejectionCode.NoMatchingIndexerTag)]
        [TestCase(RejectionCode.MinimumFreeSpace)]
        public void should_keep_the_existing_delay_when_the_rejection_can_change(string code)
        {
            GivenHeldRelease(_release.Title, _release.Indexer, _release.PublishDate);
            var rejected = new DownloadDecision(_temporarilyRejected.RemoteBook, new Rejection("Owned policy reason", code: code));

            Subject.Handle(new RssSyncCompleteEvent(new ProcessedDecisions(new List<DownloadDecision>(),
                                                                           new List<DownloadDecision>(),
                                                                           new List<DownloadDecision> { rejected })));

            VerifyNoDelete();
        }

        [Test]
        public void should_only_remove_the_rejected_pending_row_when_release_identity_is_shared()
        {
            var original = new PendingRelease { Id = 7, AuthorId = _author.Id, Title = _release.Title, Release = _release.JsonClone() };
            var current = new PendingRelease { Id = 8, AuthorId = _author.Id, Title = _release.Title, Release = _release.JsonClone() };
            var repository = Mocker.GetMock<IPendingReleaseRepository>();
            repository.Setup(service => service.All()).Returns(new List<PendingRelease> { original, current });
            _release.PendingReleaseId = original.Id;
            var rejected = new DownloadDecision(_remoteBook, new Rejection("Original book was deleted", code: RejectionCode.UnknownBook));

            Subject.Handle(new RssSyncCompleteEvent(new ProcessedDecisions(new List<DownloadDecision>(),
                                                                           new List<DownloadDecision>(),
                                                                           new List<DownloadDecision> { rejected })));

            repository.Verify(service => service.Delete(original), Times.Once());
            repository.Verify(service => service.Delete(current), Times.Never());
        }

        [Test]
        public void should_not_remove_if_indexer_is_different()
        {
            GivenHeldRelease(_release.Title, "AnotherIndexer", _release.PublishDate);

            Subject.Handle(new RssSyncCompleteEvent(new ProcessedDecisions(new List<DownloadDecision>(),
                                                                           new List<DownloadDecision>(),
                                                                           new List<DownloadDecision> { _temporarilyRejected })));

            VerifyNoDelete();
        }

        [Test]
        public void should_not_remove_if_publish_date_is_different()
        {
            GivenHeldRelease(_release.Title, _release.Indexer, _release.PublishDate.AddHours(1));

            Subject.Handle(new RssSyncCompleteEvent(new ProcessedDecisions(new List<DownloadDecision>(),
                                                                           new List<DownloadDecision>(),
                                                                           new List<DownloadDecision> { _temporarilyRejected })));

            VerifyNoDelete();
        }

        private void VerifyDelete()
        {
            Mocker.GetMock<IPendingReleaseRepository>()
                .Verify(v => v.Delete(It.IsAny<PendingRelease>()), Times.Once());
        }

        private void VerifyNoDelete()
        {
            Mocker.GetMock<IPendingReleaseRepository>()
                .Verify(v => v.Delete(It.IsAny<PendingRelease>()), Times.Never());
        }
    }
}
