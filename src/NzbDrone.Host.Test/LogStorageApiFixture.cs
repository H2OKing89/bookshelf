using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Instrumentation;
using NzbDrone.Test.Common;
using Readarr.Api.V1.Logs;
using Readarr.Http;

namespace NzbDrone.App.Test
{
    [TestFixture]
    public class LogStorageApiFixture : TestBase
    {
        [TestCase(null)]
        [TestCase("warn")]
        public void disabled_log_storage_should_return_an_empty_page_without_querying_the_repository(string level)
        {
            Mocker.GetMock<ILogDatabase>().SetupGet(database => database.IsEnabled).Returns(false);
            Mocker.GetMock<IConfigFileProvider>().SetupGet(config => config.LogDbEnabled).Returns(true);
            var repository = Mocker.GetMock<ILogRepository>();
            var controller = new LogController(Mocker.Resolve<LogService>());

            var response = controller.GetLogs(Request(), level);

            response.Page.Should().Be(3);
            response.PageSize.Should().Be(25);
            response.SortKey.Should().Be("time");
            response.SortDirection.Should().Be(SortDirection.Descending);
            response.TotalRecords.Should().Be(0);
            response.Records.Should().NotBeNull().And.BeEmpty();
            repository.VerifyNoOtherCalls();
        }

        [Test]
        public void enabled_log_storage_should_keep_the_existing_paging_filter_and_resource_mapping()
        {
            Mocker.GetMock<ILogDatabase>().SetupGet(database => database.IsEnabled).Returns(true);
            Mocker.GetMock<IConfigFileProvider>().SetupGet(config => config.LogDbEnabled).Returns(false);
            PagingSpec<Log> requestedPage = null;
            var instant = new DateTime(2026, 11, 17, 12, 34, 56, DateTimeKind.Utc);
            Mocker.GetMock<ILogRepository>()
                .Setup(repository => repository.GetPaged(It.IsAny<PagingSpec<Log>>()))
                .Returns((PagingSpec<Log> page) =>
                {
                    requestedPage = page;
                    page.TotalRecords = 51;
                    page.Records = new List<Log>
                    {
                        new Log { Id = 71, Time = instant, Level = "Warn", Logger = "Synthetic log source", Message = "Synthetic log entry" }
                    };
                    return page;
                });
            var controller = new LogController(Mocker.Resolve<LogService>());

            var response = controller.GetLogs(Request(), "warn");

            requestedPage.Page.Should().Be(3);
            requestedPage.PageSize.Should().Be(25);
            requestedPage.SortKey.Should().Be("id");
            requestedPage.SortDirection.Should().Be(SortDirection.Descending);
            var filter = requestedPage.FilterExpressions.Single().Compile();
            filter(new Log { Level = "Fatal" }).Should().BeTrue();
            filter(new Log { Level = "Error" }).Should().BeTrue();
            filter(new Log { Level = "Warn" }).Should().BeTrue();
            filter(new Log { Level = "Info" }).Should().BeFalse();
            response.TotalRecords.Should().Be(51);
            response.SortKey.Should().Be("time");
            response.Records.Should().ContainSingle();
            var resource = response.Records.Single();
            resource.Id.Should().Be(71);
            resource.Time.Should().Be(instant);
            resource.Level.Should().Be("warn");
            resource.Logger.Should().Be("Synthetic log source");
            resource.Message.Should().Be("Synthetic log entry");
        }

        private static PagingRequestResource Request()
        {
            return new PagingRequestResource
            {
                Page = 3,
                PageSize = 25,
                SortKey = "time",
                SortDirection = SortDirection.Descending
            };
        }
    }
}
