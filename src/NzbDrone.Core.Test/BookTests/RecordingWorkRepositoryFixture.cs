using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Dapper;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MetadataSource.Audiobook;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.BookTests
{
    [TestFixture]
    public class RecordingWorkRepositoryFixture : DbTest<RecordingWorkRepository, RecordingWork>
    {
        private AuthorMetadata _author;
        private Edition _pack;
        private Edition _first;
        private Edition _second;
        private Edition _regional;
        private List<int> _fileIds;

        [SetUp]
        public void SetUp()
        {
            _author = new AuthorMetadata
            {
                ForeignAuthorId = AudiobookId.ForName("us", "Author"),
                Name = "Author",
                SortName = "author",
                NameLastFirst = "Author",
                SortNameLastFirst = "author",
                TitleSlug = "synthetic-author"
            };
            Db.Insert(_author);
            _pack = AddRecording("us", "B000000001", "Series Books 1–2");
            _first = AddRecording("us", "B000000002", "Volume One");
            _second = AddRecording("us", "B000000003", "Volume Two");
            _regional = AddRecording("uk", "B000000002", "Volume One");
            _fileIds = new List<int>
            {
                AddFile(_pack, "pack-1", 20, 1),
                AddFile(_pack, "pack-2", 30, 2),
                AddFile(_first, "first", 11, 1),
                AddFile(_second, "second", 12, 1)
            };
        }

        [Test]
        public void should_not_infer_work_coverage_from_pack_or_same_title()
        {
            Subject.Inspect(_pack.Id).Memberships.Should().BeEmpty();
            Subject.Inspect(_first.Id).Memberships.Should().BeEmpty();
            Subject.Inspect(_regional.Id).Memberships.Should().BeEmpty();
            Subject.ForBook(_pack.BookId).Single().FileCount.Should().Be(2);
        }

        [Test]
        public void should_preserve_legacy_unknown_ebook_flag_and_exclude_explicit_ebooks()
        {
            using (var connection = Mocker.Resolve<IMainDatabase>().OpenConnection())
            {
                connection.Execute("""UPDATE "Editions" SET "IsEbook" = NULL WHERE "Id" = @Id""", new { _first.Id });
                connection.Execute("""UPDATE "Editions" SET "IsEbook" = TRUE WHERE "Id" = @Id""", new { _regional.Id });
            }

            Subject.ForBook(_first.BookId).Single().EditionId.Should().Be(_first.Id);
            Save(_first, NewWork("One", "1")).Memberships.Should().ContainSingle();
            Subject.ForBook(_regional.BookId).Should().BeEmpty();
            Assert.Throws<ModelNotFoundException>(() => Subject.Inspect(_regional.Id));
        }

        [Test]
        public void should_distinguish_coverage_and_owned_recordings_without_counting_pack_bytes_twice()
        {
            var pack = Save(_pack, NewWork("One", "1"), NewWork("Two", "2"), NewWork("Maybe side story", "1.5–2.5", false));
            var firstWork = pack.Memberships.Single(item => item.Work.Position == "1").Work.Id;
            var secondWork = pack.Memberships.Single(item => item.Work.Position == "2").Work.Id;

            Save(_first, Existing(firstWork));
            Save(_second, Existing(secondWork));
            Save(_regional, Existing(firstWork));

            pack = Subject.Inspect(_pack.Id);
            pack.Memberships.Should().HaveCount(3);
            pack.Memberships.Single(item => item.Work.Position == "1").Recordings.Select(item => item.Recording.EditionId)
                .Should().BeEquivalentTo(new[] { _pack.Id, _first.Id, _regional.Id });
            pack.Memberships.Single(item => !item.Confirmed).HasConfirmedFiles.Should().BeFalse();
            pack.CoveredWorkCount.Should().Be(2);
            pack.RelatedRecordingCount.Should().Be(3);
            pack.RelatedFileCount.Should().Be(4);
            pack.RelatedSizeOnDisk.Should().Be(73);
            pack.Recording.FileCount.Should().Be(2);
            pack.Recording.SizeOnDisk.Should().Be(50);

            var regional = Subject.Inspect(_regional.Id);
            regional.Recording.FileCount.Should().Be(0);
            regional.CoveredWorkCount.Should().Be(1);
            regional.RelatedFileCount.Should().Be(3);
            regional.RelatedSizeOnDisk.Should().Be(61);
            Db.All<BookFile>().Select(item => item.Id).Should().BeEquivalentTo(_fileIds);
            Db.All<BookFile>().Single(item => item.Id == _fileIds[1]).Part.Should().Be(2);
            Db.All<Edition>().Should().HaveCount(4);
            Db.All<Book>().Should().HaveCount(4);
        }

        [Test]
        public void should_keep_an_uncertain_membership_out_of_confirmed_coverage()
        {
            var work = Save(_pack, NewWork("Side story", "1.5", false)).Memberships.Single().Work;
            Save(_first, Existing(work.Id));
            var current = Subject.Inspect(_pack.Id);
            current.Memberships.Single().HasConfirmedFiles.Should().BeTrue();
            current.CoveredWorkCount.Should().Be(0);
            current.RelatedFileCount.Should().Be(0);

            current = Save(_pack, Existing(work.Id));
            current.CoveredWorkCount.Should().Be(1);
            current.RelatedFileCount.Should().Be(3);
            Save(_pack);
            Subject.Inspect(_first.Id).Memberships.Single().Work.Id.Should().Be(work.Id);
        }

        [Test]
        public void should_reject_stale_duplicate_and_missing_work_edits_without_partial_writes()
        {
            var stale = Subject.Inspect(_pack.Id);
            var current = Save(_pack, NewWork("One", "1"));
            var workId = current.Memberships.Single().Work.Id;
            var conflict = Assert.Throws<NzbDroneClientException>(() => Subject.Save(_pack.Id, new RecordingWorkEdit
            {
                Revision = stale.Revision,
                Memberships = new List<RecordingWorkMemberEdit>()
            }));
            conflict.StatusCode.Should().Be(HttpStatusCode.Conflict);

            Assert.Throws<NzbDroneClientException>(() => Save(_pack, Existing(workId), Existing(workId))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
            Assert.Throws<NzbDroneClientException>(() => Save(_pack, NewWork("Two", "2"), Existing(int.MaxValue))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
            Subject.Inspect(_pack.Id).Revision.Should().Be(current.Revision);
            Db.All<RecordingWork>().Should().ContainSingle();
        }

        [Test]
        public void should_report_invalid_user_input_as_bad_request()
        {
            Assert.Throws<NzbDroneClientException>(() => Subject.FindWorks(new string('a', 513))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
            Assert.Throws<NzbDroneClientException>(() => Subject.Save(_pack.Id, null)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
            Assert.Throws<NzbDroneClientException>(() => Save(_pack, NewWork("", "1"))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
            Assert.Throws<NzbDroneClientException>(() => Save(_pack, new RecordingWorkMemberEdit { NewWork = new RecordingWork { Title = "One" } })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
            Subject.Inspect(_pack.Id).Memberships.Should().BeEmpty();
            Db.All<RecordingWork>().Should().BeEmpty();
        }

        [Test]
        public void should_preserve_membership_across_normal_metadata_corrections_but_not_identity_retargeting()
        {
            var snapshot = Save(_pack, NewWork("One", "1"));
            var work = snapshot.Memberships.Single().Work;
            Save(_first, Existing(work.Id));
            var editions = Mocker.Resolve<BasicRepository<Edition>>();
            _pack.Title = "Revised pack metadata";
            editions.Update(_pack);
            Subject.Inspect(_pack.Id).Memberships.Single().Work.Id.Should().Be(work.Id);

            _pack.ForeignEditionId = AudiobookId.ForAsin("uk", "B000000001");
            _pack.AudibleRegion = "uk";
            editions.Update(_pack);
            var retargeted = Subject.Inspect(_pack.Id);
            retargeted.UnmatchedMembershipCount.Should().Be(1);
            retargeted.Memberships.Should().BeEmpty();
            Subject.Inspect(_first.Id).RelatedRecordingCount.Should().Be(1);
            Assert.Throws<NzbDroneClientException>(() => Subject.Save(_pack.Id, new RecordingWorkEdit
            {
                Revision = snapshot.Revision,
                Memberships = new List<RecordingWorkMemberEdit> { Existing(work.Id) }
            }));
            Save(_pack, Existing(work.Id)).UnmatchedMembershipCount.Should().Be(0);
        }

        [Test]
        public void should_remove_only_deleted_edition_memberships_and_retain_the_independent_work()
        {
            var work = Save(_pack, NewWork("One", "1")).Memberships.Single().Work;
            Save(_first, Existing(work.Id));
            Mocker.Resolve<BasicRepository<Edition>>().Delete(_pack.Id);
            Subject.Inspect(_first.Id).RelatedFileCount.Should().Be(1);
            Subject.FindWorks("One").Single().Id.Should().Be(work.Id);
            Db.All<RecordingWorkMembership>().Should().ContainSingle();
            Save(_first);
            Db.All<RecordingWorkMembership>().Should().BeEmpty();
            Subject.FindWorks("One").Single().Id.Should().Be(work.Id);
        }

        [Test]
        public async Task should_serialize_two_edits_reviewing_an_empty_membership_set()
        {
            var revision = Subject.Inspect(_pack.Id).Revision;
            var repository = Subject;
            async Task<HttpStatusCode> Apply(string title)
            {
                return await Task.Run(() =>
                {
                    try
                    {
                        repository.Save(_pack.Id, new RecordingWorkEdit
                        {
                            Revision = revision,
                            Memberships = new List<RecordingWorkMemberEdit> { NewWork(title, "1") }
                        });
                        return HttpStatusCode.OK;
                    }
                    catch (NzbDroneClientException ex)
                    {
                        return ex.StatusCode;
                    }
                });
            }

            var statuses = await Task.WhenAll(Apply("One"), Apply("Different one"));
            statuses.Should().BeEquivalentTo(new[] { HttpStatusCode.OK, HttpStatusCode.Conflict });
            Db.All<RecordingWork>().Should().ContainSingle();
            Subject.Inspect(_pack.Id).Memberships.Should().ContainSingle();
        }

        [Test]
        public void should_keep_distinct_work_identity_even_for_matching_descriptions()
        {
            var snapshot = Save(_pack, NewWork("Side% story", "1.5"), NewWork("Side% story", "1.5"));
            snapshot.Memberships.Select(item => item.Work.Id).Distinct().Should().HaveCount(2);
            Subject.FindWorks("%").Should().HaveCount(2);
            Subject.FindWorks("_").Should().BeEmpty();
        }

        private RecordingWorkSnapshot Save(Edition edition, params RecordingWorkMemberEdit[] members)
        {
            return Subject.Save(edition.Id, new RecordingWorkEdit
            {
                Revision = Subject.Inspect(edition.Id).Revision,
                Memberships = members.ToList()
            });
        }

        private static RecordingWorkMemberEdit Existing(int workId, bool confirmed = true)
        {
            return new RecordingWorkMemberEdit { WorkId = workId, Confirmed = confirmed };
        }

        private static RecordingWorkMemberEdit NewWork(string title, string position, bool confirmed = true)
        {
            return new RecordingWorkMemberEdit
            {
                NewWork = new RecordingWork { Title = title, Author = "Author", Series = "Series", Position = position },
                Confirmed = confirmed
            };
        }

        private Edition AddRecording(string region, string asin, string title)
        {
            var foreignId = AudiobookId.ForAsin(region, asin);
            var book = new Book
            {
                AuthorMetadataId = _author.Id,
                ForeignBookId = foreignId,
                Title = title,
                CleanTitle = title,
                TitleSlug = $"{region}-{asin}",
                Added = DateTime.UtcNow,
                Monitored = true
            };
            Db.Insert(book);
            var edition = new Edition
            {
                BookId = book.Id,
                ForeignEditionId = foreignId,
                AudibleRegion = region,
                Asin = asin,
                Title = title,
                TitleSlug = book.TitleSlug,
                Monitored = true
            };
            Db.Insert(edition);
            return edition;
        }

        private int AddFile(Edition edition, string name, long size, int part)
        {
            var file = new BookFile
            {
                Path = System.IO.Path.Combine(TempFolder, name + ".m4b"),
                EditionId = edition.Id,
                Size = size,
                Part = part,
                Quality = new QualityModel(Quality.MP3),
                DateAdded = DateTime.UtcNow,
                Modified = DateTime.UtcNow
            };
            Db.Insert(file);
            return file.Id;
        }
    }
}
