using System.Collections.Generic;
using System.IO;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.DecisionEngine;
using NzbDrone.Core.DecisionEngine.Specifications;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Profiles.Qualities;
using NzbDrone.Core.Qualities;

namespace NzbDrone.Core.Test.DecisionEngineTests
{
    [TestFixture]
    public class AcceptableSizeSpecificationFixture
    {
        private const long Mib = 1024 * 1024;
        private static readonly AcceptableSizeSpecification Specification = new AcceptableSizeSpecification();

        [TestCase((3000L * Mib) - 1, RejectionCode.BelowMinimumSize)]
        [TestCase(3000L * Mib, null)]
        [TestCase(3600L * Mib, null)]
        [TestCase((3600L * Mib) + 1, RejectionCode.AboveMaximumSize)]
        public void should_scale_both_bounds_for_a_thirty_hour_release(long bytes, string code)
        {
            var subject = Release(Quality.M4B, bytes, 30 * 3600);

            Specification.IsSatisfiedBy(subject, null).Code.Should().Be(code);
        }

        [TestCase(0)]
        [TestCase(-1)]
        [TestCase(double.NaN)]
        [TestCase(double.PositiveInfinity)]
        [TestCase(0.01)]
        public void should_skip_bound_when_duration_evidence_is_unusable(double duration)
        {
            var subject = Release(Quality.MP3, 1, duration);
            subject.Release.MediaInfo = new MediaInfoModel
            {
                Source = "MyAnonaMouse",
                DurationSeconds = 600,
                MediaFileSizeBytes = subject.Release.Size
            };

            Specification.IsSatisfiedBy(subject, null).Accepted.Should().BeTrue();
            ReleaseSizePolicy.GetPreferredDeviation(subject).Should().BeNull("a sample duration is not whole-release evidence");
        }

        [Test]
        public void should_use_absolute_ebook_size_and_leaf_policy_in_group()
        {
            var subject = Release(Quality.EPUB, 4 * Mib, 30 * 3600);
            subject.Author.QualityProfile.Value.Items = new List<QualityProfileQualityItem>
            {
                new ()
                {
                    Id = 1000,
                    MaxSize = 1,
                    Items = new List<QualityProfileQualityItem>
                    {
                        new () { Quality = Quality.EPUB, MinSize = 3, MaxSize = 5, PreferredSize = 4 }
                    }
                }
            };

            Specification.IsSatisfiedBy(subject, null).Accepted.Should().BeTrue();
            ReleaseSizePolicy.GetPreferredDeviation(subject).Should().Be(0);
            subject.Release.Size = (5 * Mib) + 1;
            Specification.IsSatisfiedBy(subject, null).Code.Should().Be(RejectionCode.AboveMaximumSize);
        }

        [Test]
        public void should_skip_legacy_unset_explicit_zero_and_unknown_quality_settings()
        {
            var subject = Release(Quality.M4B, long.MaxValue, 3600);
            var item = subject.Author.QualityProfile.Value.Items[0];
            item.MinSize = null;
            item.MaxSize = 0;
            item.PreferredSize = 0;

            Specification.IsSatisfiedBy(subject, null).Accepted.Should().BeTrue();
            ReleaseSizePolicy.GetPreferredDeviation(subject).Should().BeNull();
            subject.ParsedBookInfo.Quality.Quality = Quality.UnknownAudio;
            item.Quality = Quality.UnknownAudio;
            item.MaxSize = 100;

            Specification.IsSatisfiedBy(subject, null).Accepted.Should().BeTrue();
        }

        [Test]
        public void should_require_exact_unique_edition_with_matching_identity_fields_for_duration_fallback()
        {
            var subject = Release(Quality.M4B, 1100 * Mib);
            var edition = Edition(1, "B000000001", 60);
            subject.Books.Add(new Book { Id = 1, Editions = new List<Edition> { edition } });
            subject.Release.Title = "Owned release";

            Specification.IsSatisfiedBy(subject, null).Accepted.Should().BeTrue("a work match alone is not an edition match");
            subject.Release.Title = "Owned release [B000000001]";
            Specification.IsSatisfiedBy(subject, null).Code.Should().Be(RejectionCode.AboveMaximumSize);
            edition.ForeignEditionId = "audible:us:B000000002";
            Specification.IsSatisfiedBy(subject, null).Accepted.Should().BeTrue("conflicting IDs cannot supply runtime");
            edition.ForeignEditionId = "audible:us:B000000001";
            subject.Books[0].Editions.Value.Add(Edition(1, "B000000001", 60));
            Specification.IsSatisfiedBy(subject, null).Accepted.Should().BeTrue("multiple recordings matching a bare ASIN are ambiguous");
        }

        [Test]
        public void should_sum_exact_bundle_editions_once_and_skip_incomplete_or_partial_identity()
        {
            var subject = Release(Quality.M4B, 310 * Mib);
            var first = new Book { Id = 1, Editions = new List<Edition> { Edition(1, "B000000001", 60) } };
            var second = new Book { Id = 2, Editions = new List<Edition> { Edition(2, "B000000002", 120) } };
            subject.Books.AddRange(new[] { first, second, second });
            subject.Release.Title = "Owned release [B000000001] [B000000002]";

            ReleaseSizePolicy.GetPreferredDeviation(subject).Should().Be(0, "the two runtimes sum to three hours without repeating the second work");
            subject.Release.Title = "Owned release [B000000001]";
            ReleaseSizePolicy.GetPreferredDeviation(subject).Should().BeNull("one identity cannot explain a bundle");
            subject.Books.RemoveAll(book => book.Id == 2);
            subject.Release.Title = "Owned release Part 1 of 2 [B000000001]";
            ReleaseSizePolicy.GetPreferredDeviation(subject).Should().BeNull("a partial release cannot use the whole-book duration");
        }

        [Test]
        public void should_sum_each_completed_local_part_once_and_reject_partial_or_unverified_local_evidence()
        {
            var subject = Release(Quality.M4B, 310 * Mib);
            var edition = Edition(1, "B000000001", null);
            subject.Books.Add(new Book { Id = 1, Editions = new List<Edition> { edition } });
            subject.Release.Title = "Owned release [B000000001]";
            var first = LocalPart(1, 1, 3600);
            var second = LocalPart(1, 2, 7200);
            edition.BookFiles = new List<BookFile> { first, second };

            ReleaseSizePolicy.GetPreferredDeviation(subject).Should().Be(0);
            var firstPath = first.Path;
            first.Path = "relative-part.m4b";
            ReleaseSizePolicy.GetPreferredDeviation(subject).Should().BeNull("a relative file path cannot establish local duration");
            edition.RuntimeLengthMinutes = 180;
            ReleaseSizePolicy.GetPreferredDeviation(subject).Should().Be(0, "invalid local paths leave the catalog runtime usable");
            edition.RuntimeLengthMinutes = null;
            first.Path = firstPath;
            second.Part = 1;
            ReleaseSizePolicy.GetPreferredDeviation(subject).Should().BeNull("duplicated part numbers do not prove completeness");
            second.Part = 2;
            second.MediaInfo.Source = "MyAnonaMouse";
            ReleaseSizePolicy.GetPreferredDeviation(subject).Should().BeNull("indexer media evidence must not masquerade as local measurements");
            edition.BookFiles.Value.Remove(second);
            ReleaseSizePolicy.GetPreferredDeviation(subject).Should().BeNull("part one alone does not prove the whole recording");
        }

        [Test]
        public void should_prefer_proven_release_duration_to_catalog_metadata()
        {
            var subject = Release(Quality.M4B, 310 * Mib, 3 * 3600);
            subject.Books.Add(new Book { Id = 1, Editions = new List<Edition> { Edition(1, "B000000001", 300) } });
            subject.Release.Title = "Owned release [B000000001]";

            ReleaseSizePolicy.GetPreferredDeviation(subject).Should().Be(0);
            Specification.IsSatisfiedBy(subject, null).Accepted.Should().BeTrue();
        }

        private static RemoteBook Release(Quality quality, long size, double? duration = null)
        {
            return new RemoteBook
            {
                Author = new Author
                {
                    QualityProfile = new QualityProfile
                    {
                        Items = new List<QualityProfileQualityItem>
                        {
                            new () { Quality = quality, MinSize = 100, PreferredSize = 310m / 3, MaxSize = 120 }
                        }
                    }
                },
                ParsedBookInfo = new ParsedBookInfo { Quality = new QualityModel(quality) },
                Release = new ReleaseInfo { Title = "Owned release", Size = size, AudioDurationSeconds = duration }
            };
        }

        private static Edition Edition(int bookId, string asin, int? minutes)
        {
            return new Edition { Id = bookId, BookId = bookId, Asin = asin, ForeignEditionId = "audible:us:" + asin, RuntimeLengthMinutes = minutes };
        }

        private static BookFile LocalPart(int editionId, int part, double duration)
        {
            return new BookFile
            {
                EditionId = editionId,
                Path = Path.Combine(Path.GetTempPath(), "owned-size-policy", $"part{part}.m4b"),
                Part = part,
                PartCount = 2,
                MediaInfo = new MediaInfoModel { Source = "MediaInfo", ContainerDurationSeconds = duration }
            };
        }
    }
}
