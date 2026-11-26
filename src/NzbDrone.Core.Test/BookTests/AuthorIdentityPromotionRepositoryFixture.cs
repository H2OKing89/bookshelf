using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.History;
using NzbDrone.Core.ImportLists.Exclusions;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MetadataSource.Audiobook;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.BookTests
{
    [TestFixture]
    public class AuthorIdentityPromotionRepositoryFixture : DbTest<AuthorMetadataRepository, AuthorMetadata>
    {
        private const string CanonicalAsin = "B099999902";
        private const string RecordingAsin = "B099999912";
        private readonly string _previousId = AudiobookId.ForName("us", "Synthetic Author");
        private readonly string _canonicalId = AudiobookId.ForAsin("us", CanonicalAsin);
        private readonly string _recordingId = AudiobookId.ForAsin("us", RecordingAsin);
        private AuthorMetadata _metadata;
        private Author _author;
        private Book _book;
        private Edition _edition;
        private BookFile _file;
        private EntityHistory _history;

        [SetUp]
        public void SetUp()
        {
            _metadata = Db.Insert(new AuthorMetadata
            {
                ForeignAuthorId = _previousId,
                TitleSlug = "stable-author-slug",
                Name = "Synthetic Author",
                SortName = "synthetic author",
                NameLastFirst = "Synthetic Author",
                SortNameLastFirst = "synthetic author",
                Overview = "Already stored biography",
                Aliases = new List<string> { "Synthetic Variant" },
                Links = new List<Links> { new Links { Name = "Other source", Url = "https://fixture.invalid/author" } }
            });
            _author = Db.Insert(new Author
            {
                AuthorMetadataId = _metadata.Id,
                CleanName = "syntheticauthor",
                Path = Path.Combine(TempFolder, "Author"),
                Monitored = false,
                QualityProfileId = 1,
                MetadataProfileId = 1,
                Tags = new HashSet<int> { 3 }
            });
            _book = Db.Insert(new Book
            {
                AuthorMetadataId = _metadata.Id,
                ForeignBookId = _recordingId,
                TitleSlug = "stable-recording",
                Title = "Synthetic Recording",
                CleanTitle = "syntheticrecording",
                Monitored = false
            });
            _edition = Db.Insert(new Edition
            {
                BookId = _book.Id,
                ForeignEditionId = _recordingId,
                Asin = RecordingAsin,
                AudibleRegion = "us",
                TitleSlug = _book.TitleSlug,
                Title = _book.Title,
                Monitored = true
            });
            _file = Db.Insert(new BookFile
            {
                EditionId = _edition.Id,
                Path = Path.Combine(_author.Path, "Recording.m4b"),
                Part = 1,
                DateAdded = DateTime.UtcNow,
                Modified = DateTime.UtcNow,
                Quality = new QualityModel(Quality.M4B)
            });
            _history = Db.Insert(new EntityHistory
            {
                AuthorId = _author.Id,
                BookId = _book.Id,
                Date = DateTime.UtcNow,
                SourceTitle = _book.Title,
                DownloadId = "synthetic-download",
                Quality = _file.Quality,
                EventType = EntityHistoryEventType.BookFileImported
            });
        }

        [Test]
        public void should_update_only_the_existing_metadata_key_and_keep_all_existing_model_ids()
        {
            var exclusion = Db.Insert(new ImportListExclusion { ForeignId = _previousId, Name = "Preserved choice" });

            var promoted = Subject.PromoteIdentity(_author.Id, _metadata.Id, _previousId, _canonicalId, _recordingId);

            promoted.Id.Should().Be(_metadata.Id);
            promoted.ForeignAuthorId.Should().Be(_canonicalId);
            StoredModel.Should().BeEquivalentTo(promoted);
            promoted.TitleSlug.Should().Be(_metadata.TitleSlug);
            promoted.Name.Should().Be(_metadata.Name);
            promoted.Overview.Should().Be(_metadata.Overview);
            promoted.Aliases.Should().Equal(_metadata.Aliases);
            promoted.Links.Should().Contain(link => link.Url == _metadata.Links.Single().Url);
            promoted.Links.Should().Contain(link => link.Url == "https://www.audible.com/author/" + CanonicalAsin);

            var author = Db.Single<Author>();
            author.Id.Should().Be(_author.Id);
            author.AuthorMetadataId.Should().Be(_metadata.Id);
            author.Path.Should().Be(_author.Path);
            author.Monitored.Should().BeFalse();
            author.Tags.Should().BeEquivalentTo(_author.Tags);
            author.Metadata.Value.ForeignAuthorId.Should().Be(_canonicalId);
            Db.Single<Book>().Id.Should().Be(_book.Id);
            Db.Single<Book>().AuthorMetadataId.Should().Be(_metadata.Id);
            Db.Single<Book>().ForeignBookId.Should().Be(_recordingId);
            Db.Single<Edition>().Id.Should().Be(_edition.Id);
            Db.Single<Edition>().ForeignEditionId.Should().Be(_recordingId);
            Db.Single<BookFile>().Id.Should().Be(_file.Id);
            Db.Single<BookFile>().Path.Should().Be(_file.Path);
            Db.Single<BookFile>().EditionId.Should().Be(_edition.Id);
            Db.Single<EntityHistory>().Id.Should().Be(_history.Id);
            Db.Single<EntityHistory>().AuthorId.Should().Be(_author.Id);
            Db.Single<EntityHistory>().BookId.Should().Be(_book.Id);
            Db.Single<ImportListExclusion>().Id.Should().Be(exclusion.Id);
            Db.Single<ImportListExclusion>().ForeignId.Should().Be(_canonicalId);
        }

        [Test]
        public void should_keep_both_exclusions_when_the_canonical_identity_already_has_one()
        {
            Db.Insert(new ImportListExclusion { ForeignId = _previousId, Name = "Original" });
            Db.Insert(new ImportListExclusion { ForeignId = _canonicalId, Name = "Existing canonical" });

            Subject.PromoteIdentity(_author.Id, _metadata.Id, _previousId, _canonicalId, _recordingId);

            Db.All<ImportListExclusion>().Select(exclusion => exclusion.ForeignId).Should().BeEquivalentTo(new[] { _previousId, _canonicalId });
        }

        [TestCase("us")]
        [TestCase("uk")]
        public void should_not_replace_a_canonical_identity_that_already_exists_in_any_marketplace(string region)
        {
            Db.Insert(new AuthorMetadata
            {
                ForeignAuthorId = AudiobookId.ForAsin(region, CanonicalAsin),
                TitleSlug = "other",
                Name = "Other author",
                SortName = "other author",
                NameLastFirst = "Other author",
                SortNameLastFirst = "other author"
            });

            Assert.Throws<InvalidOperationException>(() => Subject.PromoteIdentity(_author.Id, _metadata.Id, _previousId, _canonicalId, _recordingId));

            AllStoredModels.Should().HaveCount(2);
            Subject.Get(_metadata.Id).ForeignAuthorId.Should().Be(_previousId);
            Db.Single<Author>().Id.Should().Be(_author.Id);
            Db.Single<Book>().AuthorMetadataId.Should().Be(_metadata.Id);
        }

        [TestCase("wrong-owner")]
        [TestCase("wrong-old-key")]
        [TestCase("missing-recording")]
        [TestCase("wrong-marketplace")]
        [TestCase("wrong-edition")]
        public void should_reject_stale_or_non_exact_evidence_without_changing_the_author(string reason)
        {
            if (reason == "wrong-edition")
            {
                _edition.AudibleRegion = "uk";
                Db.Update(_edition);
            }

            var desiredKey = reason == "wrong-marketplace" ? AudiobookId.ForAsin("uk", CanonicalAsin) : _canonicalId;
            var oldKey = reason == "wrong-old-key" ? AudiobookId.ForName("us", "Different name") : _previousId;
            var recording = reason == "missing-recording" ? AudiobookId.ForAsin("us", "B099999913") : _recordingId;
            var owner = reason == "wrong-owner" ? _author.Id + 1 : _author.Id;

            Assert.Throws<InvalidOperationException>(() => Subject.PromoteIdentity(owner, _metadata.Id, oldKey, desiredKey, recording));

            StoredModel.ForeignAuthorId.Should().Be(_previousId);
            StoredModel.TitleSlug.Should().Be(_metadata.TitleSlug);
            Db.Single<Book>().Id.Should().Be(_book.Id);
            Db.Single<BookFile>().EditionId.Should().Be(_edition.Id);
        }
    }
}
