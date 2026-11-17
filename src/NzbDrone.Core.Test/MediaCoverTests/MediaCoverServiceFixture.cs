using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using FizzWare.NBuilder;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Common.Http;
using NzbDrone.Core.Books;
using NzbDrone.Core.Books.Events;
using NzbDrone.Core.Lifecycle;
using NzbDrone.Core.MediaCover;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MediaCoverTests
{
    [TestFixture]
    [NonParallelizable]
    public class MediaCoverServiceFixture : CoreTest<MediaCoverService>
    {
        private Author _author;
        private Book _book;
        private Edition _edition;
        private HttpResponse _httpResponse;

        [SetUp]
        public void Setup()
        {
            Mocker.SetConstant<IAppFolderInfo>(new AppFolderInfo(Mocker.Resolve<IStartupContext>()));

            _author = Builder<Author>.CreateNew()
                .With(v => v.Id = 2)
                .With(v => v.Metadata.Value.Images = new List<MediaCover.MediaCover> { new MediaCover.MediaCover(MediaCoverTypes.Poster, "http://test.org/AuthorImage.png") })
                .Build();

            _edition = Builder<Edition>.CreateNew()
                .With(v => v.Id = 8)
                .With(v => v.Images = new List<MediaCover.MediaCover> { new MediaCover.MediaCover(MediaCoverTypes.Cover, "http://test.org/BookImage.png") })
                .With(v => v.Monitored = true)
                .Build();

            _book = Builder<Book>.CreateNew()
                .With(v => v.Id = 4)
                .With(v => v.Editions = new List<Edition> { _edition })
                .Build();

            _httpResponse = new HttpResponse(null, new HttpHeader(), "");
            Mocker.GetMock<IHttpClient>().Setup(c => c.Get(It.IsAny<HttpRequest>(), It.IsAny<CancellationToken>())).Returns(_httpResponse);
        }

        [TestCase(".png")]
        [TestCase(".jpg")]
        public void should_convert_cover_urls_to_local(string extension)
        {
            var covers = new List<MediaCover.MediaCover>
                {
                    new MediaCover.MediaCover
                    {
                        Url = "http://dummy.com/test" + extension,
                        CoverType = MediaCoverTypes.Banner
                    }
                };

            Mocker.GetMock<IDiskProvider>().Setup(c => c.FileGetLastWrite(It.IsAny<string>()))
                  .Returns(new DateTime(1234));

            Mocker.GetMock<IDiskProvider>().Setup(c => c.FileExists(It.IsAny<string>()))
                  .Returns(true);

            Subject.ConvertToLocalUrls(12, MediaCoverEntity.Author, covers);

            covers.Single().Url.Should().Be("/MediaCover/12/banner" + extension + "?lastWrite=1234");
        }

        [TestCase(".png")]
        [TestCase(".jpg")]
        public void convert_to_local_url_should_not_change_extension(string extension)
        {
            var covers = new List<MediaCover.MediaCover>
                {
                    new MediaCover.MediaCover
                    {
                        Url = "http://dummy.com/test" + extension,
                        CoverType = MediaCoverTypes.Banner
                    }
                };

            Mocker.GetMock<IDiskProvider>().Setup(c => c.FileGetLastWrite(It.IsAny<string>()))
                  .Returns(new DateTime(1234));

            Mocker.GetMock<IDiskProvider>().Setup(c => c.FileExists(It.IsAny<string>()))
                  .Returns(true);

            Subject.ConvertToLocalUrls(12, MediaCoverEntity.Author, covers);

            covers.Single().Extension.Should().Be(extension);
        }

        [TestCase(".png")]
        [TestCase(".jpg")]
        public void should_convert_book_cover_urls_to_local(string extension)
        {
            var covers = new List<MediaCover.MediaCover>
                {
                    new MediaCover.MediaCover
                    {
                        Url = "http://dummy.com/test" + extension,
                        CoverType = MediaCoverTypes.Disc
                    }
                };

            Mocker.GetMock<IDiskProvider>().Setup(c => c.FileGetLastWrite(It.IsAny<string>()))
                  .Returns(new DateTime(1234));

            Mocker.GetMock<IDiskProvider>().Setup(c => c.FileExists(It.IsAny<string>()))
                  .Returns(true);

            Subject.ConvertToLocalUrls(6, MediaCoverEntity.Book, covers);

            covers.Single().Url.Should().Be("/MediaCover/Books/6/disc" + extension + "?lastWrite=1234");
        }

        [TestCase(".png")]
        [TestCase(".jpg")]
        public void should_convert_media_urls_to_local_without_time_if_file_doesnt_exist(string extension)
        {
            var covers = new List<MediaCover.MediaCover>
                {
                    new MediaCover.MediaCover
                    {
                        Url = "http://dummy.com/test" + extension,
                        CoverType = MediaCoverTypes.Banner
                    }
                };

            var path = Path.Combine(TestContext.CurrentContext.TestDirectory, "Files", "Media", "NonExistant.mp4");
            var fileInfo = new FileInfo(path);

            Mocker.GetMock<IDiskProvider>()
                .Setup(c => c.GetFileInfo(It.IsAny<string>()))
                .Returns((FileInfoBase)fileInfo);

            Subject.ConvertToLocalUrls(12, MediaCoverEntity.Author, covers);

            covers.Single().Url.Should().Be("/MediaCover/12/banner" + extension);
        }

        [Test]
        public void should_resize_covers_if_main_downloaded()
        {
            Mocker.GetMock<ICoverExistsSpecification>()
                  .Setup(v => v.AlreadyExists(It.IsAny<DateTime?>(), It.IsAny<long?>(), It.IsAny<string>()))
                  .Returns(false);

            Mocker.GetMock<IBookService>()
                  .Setup(v => v.GetBooksByAuthor(It.IsAny<int>()))
                  .Returns(new List<Book> { _book });

            Mocker.GetMock<IDiskProvider>()
                  .Setup(v => v.FileExists(It.IsAny<string>()))
                  .Returns(true);

            Subject.HandleAsync(new AuthorRefreshCompleteEvent(_author));

            Mocker.GetMock<IImageResizer>()
                  .Verify(v => v.Resize(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>()), Times.Exactly(2));
        }

        [Test]
        public void should_resize_covers_if_missing()
        {
            Mocker.GetMock<ICoverExistsSpecification>()
                  .Setup(v => v.AlreadyExists(It.IsAny<DateTime?>(), It.IsAny<long?>(), It.IsAny<string>()))
                  .Returns(true);

            Mocker.GetMock<IBookService>()
                  .Setup(v => v.GetBooksByAuthor(It.IsAny<int>()))
                  .Returns(new List<Book> { _book });

            Mocker.GetMock<IDiskProvider>()
                  .Setup(v => v.FileExists(It.IsAny<string>()))
                  .Returns(false);

            Subject.HandleAsync(new AuthorRefreshCompleteEvent(_author));

            Mocker.GetMock<IImageResizer>()
                  .Verify(v => v.Resize(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>()), Times.Exactly(2));
        }

        [Test]
        public void should_not_resize_covers_if_exists()
        {
            Mocker.GetMock<ICoverExistsSpecification>()
                  .Setup(v => v.AlreadyExists(It.IsAny<DateTime?>(), It.IsAny<long?>(), It.IsAny<string>()))
                  .Returns(true);

            Mocker.GetMock<IDiskProvider>()
                  .Setup(v => v.FileExists(It.IsAny<string>()))
                  .Returns(true);

            Mocker.GetMock<IBookService>()
                  .Setup(v => v.GetBooksByAuthor(It.IsAny<int>()))
                  .Returns(new List<Book> { _book });

            Mocker.GetMock<IDiskProvider>()
                  .Setup(v => v.GetFileSize(It.IsAny<string>()))
                  .Returns(1000);

            Subject.HandleAsync(new AuthorRefreshCompleteEvent(_author));

            Mocker.GetMock<IImageResizer>()
                  .Verify(v => v.Resize(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>()), Times.Never());
        }

        [Test]
        public void should_resize_covers_if_existing_is_empty()
        {
            Mocker.GetMock<ICoverExistsSpecification>()
                  .Setup(v => v.AlreadyExists(It.IsAny<DateTime?>(), It.IsAny<long?>(), It.IsAny<string>()))
                  .Returns(true);

            Mocker.GetMock<IDiskProvider>()
                  .Setup(v => v.FileExists(It.IsAny<string>()))
                  .Returns(true);

            Mocker.GetMock<IBookService>()
                  .Setup(v => v.GetBooksByAuthor(It.IsAny<int>()))
                  .Returns(new List<Book> { _book });

            Mocker.GetMock<IDiskProvider>()
                  .Setup(v => v.GetFileSize(It.IsAny<string>()))
                  .Returns(0);

            Subject.HandleAsync(new AuthorRefreshCompleteEvent(_author));

            Mocker.GetMock<IImageResizer>()
                  .Verify(v => v.Resize(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>()), Times.Exactly(2));
        }

        [Test]
        public void should_log_error_if_resize_failed()
        {
            Mocker.GetMock<ICoverExistsSpecification>()
                  .Setup(v => v.AlreadyExists(It.IsAny<DateTime?>(), It.IsAny<long?>(), It.IsAny<string>()))
                  .Returns(true);

            Mocker.GetMock<IDiskProvider>()
                  .Setup(v => v.FileExists(It.IsAny<string>()))
                  .Returns(false);

            Mocker.GetMock<IBookService>()
                  .Setup(v => v.GetBooksByAuthor(It.IsAny<int>()))
                  .Returns(new List<Book> { _book });

            Mocker.GetMock<IImageResizer>()
                  .Setup(v => v.Resize(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>()))
                  .Throws<ApplicationException>();

            Subject.HandleAsync(new AuthorRefreshCompleteEvent(_author));

            Mocker.GetMock<IImageResizer>()
                  .Verify(v => v.Resize(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>()), Times.Exactly(2));
        }

        [Test]
        public void should_skip_author_cover_when_url_is_null_or_invalid()
        {
            var authorWithInvalidCover = Builder<Author>.CreateNew()
                .With(v => v.Id = 2)
                .With(v => v.Metadata.Value.Images = new List<MediaCover.MediaCover>
                {
                    new MediaCover.MediaCover { CoverType = MediaCoverTypes.Poster, Url = null }
                })
                .Build();

            Mocker.GetMock<IBookService>()
                  .Setup(v => v.GetBooksByAuthor(It.IsAny<int>()))
                  .Returns(new List<Book>());

            Subject.HandleAsync(new AuthorRefreshCompleteEvent(authorWithInvalidCover));

            Mocker.GetMock<IHttpClient>()
                  .Verify(c => c.Get(It.IsAny<HttpRequest>(), It.IsAny<CancellationToken>()), Times.Never());
        }

        [Test]
        public void should_skip_author_cover_when_url_is_empty_or_malformed()
        {
            var authorWithInvalidCover = Builder<Author>.CreateNew()
                .With(v => v.Id = 2)
                .With(v => v.Metadata.Value.Images = new List<MediaCover.MediaCover>
                {
                    new MediaCover.MediaCover { CoverType = MediaCoverTypes.Poster, Url = "not-a-valid-uri" }
                })
                .Build();

            Mocker.GetMock<IBookService>()
                  .Setup(v => v.GetBooksByAuthor(It.IsAny<int>()))
                  .Returns(new List<Book>());

            Subject.HandleAsync(new AuthorRefreshCompleteEvent(authorWithInvalidCover));

            Mocker.GetMock<IHttpClient>()
                  .Verify(c => c.Get(It.IsAny<HttpRequest>(), It.IsAny<CancellationToken>()), Times.Never());
        }

        [Test]
        public void should_skip_book_cover_when_url_is_invalid()
        {
            var editionWithInvalidCover = Builder<Edition>.CreateNew()
                .With(v => v.Id = 8)
                .With(v => v.Images = new List<MediaCover.MediaCover>
                {
                    new MediaCover.MediaCover { CoverType = MediaCoverTypes.Cover, Url = string.Empty }
                })
                .With(v => v.Monitored = true)
                .Build();

            var bookWithInvalidCover = Builder<Book>.CreateNew()
                .With(v => v.Id = 4)
                .With(v => v.Editions = new List<Edition> { editionWithInvalidCover })
                .Build();

            Mocker.GetMock<ICoverExistsSpecification>()
                  .Setup(v => v.AlreadyExists(It.IsAny<DateTime?>(), It.IsAny<long?>(), It.IsAny<string>()))
                  .Returns(false);

            Mocker.GetMock<IBookService>()
                  .Setup(v => v.GetBooksByAuthor(It.IsAny<int>()))
                  .Returns(new List<Book> { bookWithInvalidCover });

            Mocker.GetMock<IDiskProvider>()
                  .Setup(v => v.FileExists(It.IsAny<string>()))
                  .Returns(true);

            Subject.HandleAsync(new AuthorRefreshCompleteEvent(_author));

            // Author has one valid cover, so Get is called once for author; book cover is skipped
            Mocker.GetMock<IHttpClient>()
                  .Verify(c => c.Get(It.IsAny<HttpRequest>(), It.IsAny<CancellationToken>()), Times.Once());
        }

        [Test]
        public void cancelled_refresh_should_not_start_cover_requests_or_publish_completion()
        {
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            using (CommandExecutionContext.Begin(cancellation.Token))
            {
                Subject.HandleAsync(new AuthorRefreshCompleteEvent(_author));
            }

            Mocker.GetMock<IHttpClient>().Verify(client => client.Get(It.IsAny<HttpRequest>(), It.IsAny<CancellationToken>()), Times.Never());
            Mocker.GetMock<IBookService>().Verify(service => service.GetBooksByAuthor(It.IsAny<int>()), Times.Never());
            Mocker.GetMock<IEventAggregator>().Verify(events => events.PublishEvent(It.IsAny<MediaCoversUpdatedEvent>()), Times.Never());
        }

        [Test]
        public void cancellation_during_headers_should_not_download_or_publish_completion()
        {
            using var cancellation = new CancellationTokenSource();
            Mocker.GetMock<IHttpClient>()
                .Setup(client => client.Get(It.IsAny<HttpRequest>(), It.IsAny<CancellationToken>()))
                .Callback<HttpRequest, CancellationToken>((request, token) =>
                {
                    cancellation.Cancel();
                    token.ThrowIfCancellationRequested();
                });

            using (CommandExecutionContext.Begin(cancellation.Token))
            {
                Subject.HandleAsync(new AuthorRefreshCompleteEvent(_author));
            }

            Mocker.GetMock<IHttpClient>().Verify(client => client.DownloadFile(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never());
            Mocker.GetMock<IEventAggregator>().Verify(events => events.PublishEvent(It.IsAny<MediaCoversUpdatedEvent>()), Times.Never());
        }

        [Test]
        public async Task shutdown_should_cancel_an_active_cover_download_without_publishing_completion()
        {
            using var started = new ManualResetEventSlim();
            Mocker.GetMock<IHttpClient>()
                .Setup(client => client.DownloadFile(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Callback<string, string, string, CancellationToken>((url, path, userAgent, token) =>
                {
                    started.Set();
                    token.WaitHandle.WaitOne(TimeSpan.FromSeconds(5)).Should().BeTrue();
                    token.ThrowIfCancellationRequested();
                });
            var work = Task.Run(() => Subject.HandleAsync(new AuthorRefreshCompleteEvent(_author)));
            try
            {
                started.Wait(TimeSpan.FromSeconds(5)).Should().BeTrue();
                Subject.Handle(new ApplicationShutdownRequested());
                await work.WaitAsync(TimeSpan.FromSeconds(5));
            }
            finally
            {
                Subject.Handle(new ApplicationShutdownRequested());
                await work.WaitAsync(TimeSpan.FromSeconds(5));
            }

            Mocker.GetMock<IImageResizer>().Verify(resizer => resizer.Resize(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>()), Times.Never());
            Mocker.GetMock<IEventAggregator>().Verify(events => events.PublishEvent(It.IsAny<MediaCoversUpdatedEvent>()), Times.Never());
        }

        [Test]
        public void cancellation_before_resize_slot_acquisition_should_not_release_an_unowned_slot()
        {
            var semaphore = (SemaphoreSlim)typeof(MediaCoverService).GetField("_semaphore", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
            var initialCount = semaphore.CurrentCount;
            Mocker.GetMock<IDiskProvider>()
                .Setup(disk => disk.FileSetLastWriteTime(It.IsAny<string>(), It.IsAny<DateTime>()))
                .Callback(() => Subject.Handle(new ApplicationShutdownRequested()));

            Subject.HandleAsync(new AuthorRefreshCompleteEvent(_author));

            semaphore.CurrentCount.Should().Be(initialCount);
            Mocker.GetMock<IImageResizer>().Verify(resizer => resizer.Resize(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>()), Times.Never());
            Mocker.GetMock<IEventAggregator>().Verify(events => events.PublishEvent(It.IsAny<MediaCoversUpdatedEvent>()), Times.Never());
        }

        [Test]
        public void cancellation_between_resizes_should_stop_remaining_work_and_restore_the_slot()
        {
            using var cancellation = new CancellationTokenSource();
            var semaphore = (SemaphoreSlim)typeof(MediaCoverService).GetField("_semaphore", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
            var initialCount = semaphore.CurrentCount;
            Mocker.GetMock<IImageResizer>()
                .Setup(resizer => resizer.Resize(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>()))
                .Callback(() => cancellation.Cancel());

            using (CommandExecutionContext.Begin(cancellation.Token))
            {
                Subject.HandleAsync(new AuthorRefreshCompleteEvent(_author));
            }

            semaphore.CurrentCount.Should().Be(initialCount);
            Mocker.GetMock<IImageResizer>().Verify(resizer => resizer.Resize(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>()), Times.Once());
            Mocker.GetMock<IEventAggregator>().Verify(events => events.PublishEvent(It.IsAny<MediaCoversUpdatedEvent>()), Times.Never());
        }

        [Test]
        public void cancelling_one_explicit_book_request_should_not_cancel_another()
        {
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            Assert.Throws<OperationCanceledException>(() => Subject.EnsureBookCovers(_book, cancellation.Token));
            Subject.EnsureBookCovers(_book, CancellationToken.None);

            Mocker.GetMock<IHttpClient>().Verify(client => client.Get(It.IsAny<HttpRequest>(), It.IsAny<CancellationToken>()), Times.Once());
            Mocker.GetMock<IHttpClient>().Verify(client => client.DownloadFile(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once());
        }
    }
}
