using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Serializer;
using NzbDrone.Core.Books;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MetadataSource.Audiobook
{
    [TestFixture]
    public class AudiobookCalendarDateFixture : CoreTest<BookService>
    {
        private sealed class ReleaseResource
        {
            [JsonConverter(typeof(STJCalendarDateConverter))]
            public DateTime? ReleaseDate { get; set; }
        }

        [TestCase("2026-07-10")]
        [TestCase("2026-07-10T00:00:00Z")]
        [TestCase("2026-07-10T00:00:00+14:00")]
        [TestCase("2026-07-10T23:00:00-12:00")]
        public void should_preserve_calendar_day_when_reading_old_and_new_api_formats(string value)
        {
            var resource = JsonSerializer.Deserialize<ReleaseResource>("{\"ReleaseDate\":\"" + value + "\"}");
            resource.ReleaseDate.Should().Be(new DateTime(2026, 7, 10, 0, 0, 0, DateTimeKind.Utc));
            resource.ReleaseDate.Value.Kind.Should().Be(DateTimeKind.Utc);
        }

        [TestCase(DateTimeKind.Local)]
        [TestCase(DateTimeKind.Unspecified)]
        [TestCase(DateTimeKind.Utc)]
        public void should_serialize_a_date_without_timezone_conversion(DateTimeKind kind)
        {
            var options = new JsonSerializerOptions();
            options.Converters.Add(new STJUtcConverter());
            var resource = new ReleaseResource { ReleaseDate = new DateTime(2026, 7, 10, 5, 0, 0, kind) };
            JsonSerializer.Serialize(resource, options).Should().Be("{\"ReleaseDate\":\"2026-07-10\"}");
            CalendarDate.Normalize(resource.ReleaseDate).Value.Kind.Should().Be(DateTimeKind.Utc);
        }

        [Test]
        public void should_keep_missing_dates_null()
        {
            JsonSerializer.Serialize(new ReleaseResource()).Should().Be("{\"ReleaseDate\":null}");
            JsonSerializer.Deserialize<ReleaseResource>("{\"ReleaseDate\":null}").ReleaseDate.Should().BeNull();
        }

        [TestCase("2026-02-30")]
        [TestCase("10/07/2026")]
        [TestCase("2026-07-10-invalid")]
        public void should_reject_invalid_calendar_dates(string value)
        {
            Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<ReleaseResource>("{\"ReleaseDate\":\"" + value + "\"}"));
        }

        [TestCase("123")]
        [TestCase("false")]
        [TestCase("{}")]
        public void should_reject_non_date_json_values(string value)
        {
            Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<ReleaseResource>("{\"ReleaseDate\":" + value + "}"));
        }

        [TestCase(DateTimeKind.Local)]
        [TestCase(DateTimeKind.Unspecified)]
        [TestCase(DateTimeKind.Utc)]
        public void should_query_whole_calendar_days_without_converting_local_time(DateTimeKind kind)
        {
            var start = new DateTime(2026, 7, 10, 0, 0, 0, kind);
            var end = new DateTime(2026, 7, 11, 0, 0, 0, kind);
            var expectedStart = new DateTime(2026, 7, 10, 0, 0, 0, DateTimeKind.Utc);
            var expectedEnd = new DateTime(2026, 7, 12, 0, 0, 0, DateTimeKind.Utc).AddTicks(-1);

            Subject.BooksBetweenDates(start, end, true);
            Subject.AuthorBooksBetweenDates(new Author(), start, end, true);

            Mocker.GetMock<IBookRepository>().Verify(r => r.BooksBetweenDates(expectedStart, expectedEnd, true), Times.Once());
            Mocker.GetMock<IBookRepository>().Verify(r => r.AuthorBooksBetweenDates(It.IsAny<Author>(), expectedStart, expectedEnd, true), Times.Once());
        }
    }
}
