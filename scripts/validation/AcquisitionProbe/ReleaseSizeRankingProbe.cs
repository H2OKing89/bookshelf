using NLog;
using NzbDrone.Core.Books;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.DecisionEngine;
using NzbDrone.Core.DecisionEngine.Specifications;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Profiles.Delay;
using NzbDrone.Core.Profiles.Qualities;
using NzbDrone.Core.Qualities;

internal static class ReleaseSizeRankingProbe
{
    private const long MiB = 1024L * 1024;

    internal static void Verify()
    {
        var logger = LogManager.GetLogger("OwnedReleaseSizeProbe");
        var config = Program.Fake<IConfigService>((method, _) => method.Name switch
        {
            "get_DownloadPropersAndRepacks" => ProperDownloadTypes.PreferAndUpgrade,
            "get_MaximumSize" => 1024,
            _ => Program.Unexpected(method)
        });
        var delay = Program.Fake<IDelayProfileService>((method, _) => method.Name == "BestForTags"
            ? new DelayProfile { PreferredProtocol = DownloadProtocol.Usenet }
            : Program.Unexpected(method));
        var ranking = new DownloadDecisionPriorizationService(config, delay);
        var limits = new AcceptableSizeSpecification();
        var global = new MaximumSizeSpecification(config, logger);
        var longRecording = Release(Quality.MP3, 2400, 30 * 3600, min: 32, preferred: 60, max: 80);
        var shortRecording = Release(Quality.MP3, 2400, 3600, min: 32, preferred: 60, max: 80);
        var tooSmall = Release(Quality.MP3, 31, 3600, min: 32, preferred: 60, max: 80);

        Program.Check("published_size_policy_scales_with_recording_length_and_includes_the_boundary",
            limits.IsSatisfiedBy(longRecording, null).Accepted &&
            limits.IsSatisfiedBy(shortRecording, null).Code == RejectionCode.AboveMaximumSize &&
            limits.IsSatisfiedBy(tooSmall, null).Code == RejectionCode.BelowMinimumSize);
        Program.Check("published_global_size_cap_applies_independently_of_the_audio_profile",
            global.IsSatisfiedBy(longRecording, null).Code == RejectionCode.MaximumSizeExceeded);

        var unknownDuration = Release(Quality.MP3, 400, preferred: 100, max: 100);
        var unknownSize = Release(Quality.MP3, 0, 3600, min: 32, preferred: 100, max: 120);
        Program.Check("published_size_policy_leaves_unknown_recording_duration_or_size_undecided",
            limits.IsSatisfiedBy(unknownDuration, null).Accepted && limits.IsSatisfiedBy(unknownSize, null).Accepted &&
            ReleaseSizePolicy.GetPreferredDeviation(unknownDuration) == null && ReleaseSizePolicy.GetPreferredDeviation(unknownSize) == null);
        Program.Check("published_ebook_limits_use_whole_release_bytes_without_a_recording_duration",
            limits.IsSatisfiedBy(Release(Quality.EPUB, 8, min: 1, max: 8), null).Accepted &&
            limits.IsSatisfiedBy(Release(Quality.EPUB, 9, min: 1, max: 8), null).Code == RejectionCode.AboveMaximumSize);

        var near = Release(Quality.MP3, 110, 3600, preferred: 100);
        var far = Release(Quality.MP3, 145, 3600, preferred: 100);
        Program.Check("published_preferred_size_distinguishes_releases_inside_one_legacy_bucket",
            Rank(ranking, far, near).SequenceEqual(new[] { near, far }));

        var longNear = Release(Quality.MP3, 3270, 30 * 3600, preferred: 100);
        Program.Check("published_preferred_size_compares_relative_distance_across_lengths",
            Rank(ranking, near, longNear).SequenceEqual(new[] { longNear, near }));
        Program.Check("published_preferred_size_orders_usable_targets_before_unknown_duration",
            Rank(ranking, unknownDuration, far, near).SequenceEqual(new[] { near, far, unknownDuration }));

        var ebookBelow = Release(Quality.EPUB, 3, preferred: 4);
        var ebookAbove = Release(Quality.EPUB, 5, preferred: 4);
        Program.Check("published_preferred_size_ties_choose_the_smaller_candidate",
            Rank(ranking, ebookAbove, ebookBelow).SequenceEqual(new[] { ebookBelow, ebookAbove }));

        far.CustomFormatScore = 1;
        Program.Check("published_preferred_size_preserves_custom_format_score_precedence",
            Rank(ranking, near, far).SequenceEqual(new[] { far, near }));
        var legacySmall = Release(Quality.MP3, 100);
        var legacyLarge = Release(Quality.MP3, 900);
        Program.Check("published_preferred_size_preserves_ordering_when_no_target_is_configured",
            Rank(ranking, legacySmall, legacyLarge).SequenceEqual(new[] { legacyLarge, legacySmall }));
    }

    private static IEnumerable<RemoteBook> Rank(DownloadDecisionPriorizationService ranking, params RemoteBook[] books)
    {
        return ranking.PrioritizeDecisions(books.Select(book => new DownloadDecision(book)).ToList()).Select(decision => decision.RemoteBook);
    }

    private static RemoteBook Release(Quality quality, int sizeMiB, double? durationSeconds = null, decimal? min = null, decimal? preferred = null, decimal? max = null)
    {
        var item = new QualityProfileQualityItem { Quality = quality, Allowed = true, MinSize = min, PreferredSize = preferred, MaxSize = max };
        return new RemoteBook
        {
            Author = new Author { Id = 501, Name = "Owned Author", QualityProfile = new QualityProfile { Items = new() { item } } },
            Books = new List<Book> { new Book { Id = 502 } },
            ParsedBookInfo = new ParsedBookInfo { Quality = new QualityModel(quality) },
            Release = new ReleaseInfo
            {
                Title = "Owned size-policy release",
                Size = sizeMiB * MiB,
                AudioDurationSeconds = durationSeconds,
                DownloadProtocol = DownloadProtocol.Usenet,
                PublishDate = DateTime.UtcNow.AddHours(-3)
            },
            DownloadAllowed = true
        };
    }
}
