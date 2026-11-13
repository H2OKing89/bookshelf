using System.Collections.Generic;
using System.IO;
using System.Linq;
using FizzWare.NBuilder;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.Events;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.MediaFiles.TrackFileMovingServiceTests
{
    [TestFixture]
    public class MediaFileServiceFixture : CoreTest<MediaFileService>
    {
        private Book _book;
        private List<BookFile> _trackFiles;

        [SetUp]
        public void Setup()
        {
            _book = Builder<Book>.CreateNew()
                         .Build();

            _trackFiles = Builder<BookFile>.CreateListOfSize(3)
                                               .TheFirst(2)
                                               .With(f => f.EditionId = _book.Id)
                                               .TheNext(1)
                                               .With(f => f.EditionId = 0)
                                               .Build().ToList();
        }

        [Test]
        public void should_throw_trackFileDeletedEvent_for_each_mapped_track_on_deletemany()
        {
            Subject.DeleteMany(_trackFiles, DeleteMediaFileReason.Manual);

            VerifyEventPublished<BookFileDeletedEvent>(Times.Exactly(2));
        }

        [Test]
        public void should_throw_trackFileDeletedEvent_for_mapped_track_on_delete()
        {
            Subject.Delete(_trackFiles[0], DeleteMediaFileReason.Manual);

            VerifyEventPublished<BookFileDeletedEvent>(Times.Once());
        }

        [Test]
        public void should_throw_trackFileAddedEvent_for_each_track_added_on_addmany()
        {
            Subject.AddMany(_trackFiles);

            VerifyEventPublished<BookFileAddedEvent>(Times.Exactly(3));
        }

        [Test]
        public void should_throw_trackFileAddedEvent_for_track_added()
        {
            Subject.Add(_trackFiles[0]);

            VerifyEventPublished<BookFileAddedEvent>(Times.Once());
        }

        [Test]
        public void should_not_report_database_failure_when_post_commit_notification_fails()
        {
            Mocker.GetMock<IEventAggregator>().Setup(x => x.PublishEvent(It.IsAny<BookFileAddedEvent>()))
                .Throws(new IOException("notification failed"));

            Assert.DoesNotThrow(() => Subject.ImportFiles(new List<BookFile> { _trackFiles[0] }, new List<BookFile>(), new List<BookFile>()));

            Mocker.GetMock<IMediaFileRepository>().Verify(x => x.ImportFiles(It.IsAny<List<BookFile>>(), It.IsAny<List<BookFile>>(), It.IsAny<List<BookFile>>()), Times.Once());
            ExceptionVerification.ExpectedWarns(1);
        }
    }
}
