using NLog;
using NzbDrone.Core.Blocklisting;
using NzbDrone.Core.Books;
using NzbDrone.Core.CustomFormats;
using NzbDrone.Core.Download;
using NzbDrone.Core.History;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.MediaInfo;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Profiles;
using NzbDrone.Core.Profiles.Qualities;
using NzbDrone.Core.Qualities;

internal static class AudioFormatProbe
{
    internal static void Verify()
    {
        var atmos = new CustomFormat("Atmos", new AudioCodecSpecification { Value = (int)AudioCodecKind.Atmos }) { Id = 301 };
        var xheAac = new CustomFormat("xHE-AAC stereo", new AudioCodecSpecification { Value = (int)AudioCodecKind.XheAac },
            new AudioChannelsSpecification { Value = 2 }, new AudioBitrateModeSpecification { Value = (int)AudioBitrateKind.Variable },
            new AudioSampleRateSpecification { Value = 48000 }) { Id = 302 };
        var narrator = new CustomFormat("Preferred narrator", new NarratorSpecification { Value = "Owned narrator" }) { Id = 303 };
        var lossless = new CustomFormat("Lossless", new AudioCodecSpecification { Value = (int)AudioCodecKind.Lossless }) { Id = 304 };
        var unknown = new CustomFormat("Unknown codec", new AudioCodecSpecification { Value = (int)AudioCodecKind.Unknown }) { Id = 305 };
        var all = new List<CustomFormat> { atmos, xheAac, narrator, lossless, unknown };
        var profiles = new QualityProfile
        {
            FormatItems = new List<ProfileFormatItem>
            {
                new() { Format = atmos, Score = 25 }, new() { Format = xheAac, Score = 15 },
                new() { Format = narrator, Score = 5 }, new() { Format = lossless, Score = 10 }
            }
        };
        var author = new Author { Id = 11, Name = "Owned author", QualityProfile = profiles };
        var configuration = Program.Fake<ICustomFormatService>((method, _) => method.Name == "All" ? all : Program.Unexpected(method));
        var logger = LogManager.GetLogger("OwnedAudioFormatProbe");
        var calculator = new CustomFormatCalculationService(configuration, logger);
        const string indexerJson = """
            {"Audio1":{"Format":"E-AC-3","Format_AdditionalFeatures":"JOC","Channels":6,"SamplingRate":48000,"BitRate_Mode":"CBR"},
             "Audio2":{"Format":"USAC","Format_AdditionalFeatures":"xHE-AAC","Channels":2,"SamplingRate":48000,"BitRate_Mode":"VBR","Default":"Yes"}}
            """;
        var indexerMedia = MediaInfoJsonParser.ParseMam(indexerJson);
        var release = new RemoteBook
        {
            Author = author, Books = new List<Book> { new() { Id = 13, AuthorId = 11 } },
            ParsedBookInfo = new ParsedBookInfo { Quality = new QualityModel(Quality.MP3) },
            Release = new ReleaseInfo
            {
                Title = "Owned Audiobook with Atmos", Narrators = new List<string> { "OWNED NARRATOR" },
                MediaInfo = indexerMedia
            }
        };
        var matches = calculator.ParseCustomFormat(release, 100);
        Program.Check("published_media_rules_match_all_conditions_on_the_default_stream", matches.Select(format => format.Id).Order().SequenceEqual(new[] { 302, 303 }));
        Program.Check("published_advertised_audio_matches_contribute_to_profile_score", profiles.CalculateCustomFormatScore(matches) == 20);
        indexerMedia.AudioStreams[1].IsDefault = false;
        matches = calculator.ParseCustomFormat(release, 100);
        Program.Check("published_present_first_stream_used_when_there_is_no_default", matches.Select(format => format.Id).Order().SequenceEqual(new[] { 301, 303 }));

        EntityHistory saved = null;
        var historyRepository = Program.Fake<IHistoryRepository>((method, arguments) =>
        {
            if (method.Name == "Insert") return saved = (EntityHistory)arguments[0];
            return Program.Unexpected(method);
        });
        var events = Program.Fake<IEventAggregator>((method, arguments) =>
            method.Name == "PublishEvent" && arguments[0] is HistoryChangedEvent && saved != null
                ? null
                : Program.Unexpected(method));
        var historyService = new HistoryService(historyRepository, logger, events);
        historyService.Handle(new BookGrabbedEvent(release) { DownloadId = "owned-format-job", DownloadClientId = 42 });
        var savedAudio = AudioFormatEvidence.FromHistory(saved.Data);
        Program.Check("published_grab_history_retains_versioned_advertised_audio_evidence", savedAudio?.Origin == AudioEvidenceOrigin.Release &&
            savedAudio.Codec == AudioCodecKind.EAc3Atmos && savedAudio.Channels == 6);
        Program.Check("published_history_and_blocklist_calculate_same_score_from_archived_evidence",
            profiles.CalculateCustomFormatScore(calculator.ParseCustomFormat(saved, author)) == 30 &&
            profiles.CalculateCustomFormatScore(calculator.ParseCustomFormat(new Blocklist { SourceTitle = saved.SourceTitle, AudioFormatEvidence = savedAudio.ToStored() }, author)) == 30);

        release.RecordedAudioFormatEvidence = savedAudio;
        indexerMedia.AudioStreams[1].IsDefault = true;
        Program.Check("published_queue_snapshot_keeps_grab_evidence_when_parsed_result_changes",
            profiles.CalculateCustomFormatScore(calculator.ParseCustomFormat(release, 100)) == 30);

        var file = new BookFile
        {
            Path = "renamed-owned-audio.m4b", SceneName = release.Release.Title, EditionId = 17, Author = author,
            Edition = new Edition { Id = 17, Narrators = new List<string> { "Different narrator" } },
            MediaInfo = MediaInfoJsonParser.ParseCli("""{"media":{"track":[{"@type":"Audio","Format":"FLAC"}]}}""")
        };
        Program.Check("published_file_format_scoring_uses_local_measurement_and_exact_edition",
            calculator.ParseCustomFormat(file).Single().Id == lossless.Id);
        file.MediaInfo = indexerMedia;
        Program.Check("published_advertised_media_does_not_gain_points_as_a_measured_file",
            calculator.ParseCustomFormat(file).Single().Id == unknown.Id);
        release.RecordedAudioFormatEvidence = null;
        release.Release = new ReleaseInfo { Title = "Owned xHE-AAC Atmos audiobook by Owned narrator" };
        Program.Check("published_title_does_not_supply_audio_or_narrator_confirmation", calculator.ParseCustomFormat(release, 100).Single().Id == unknown.Id);
        var input = new CustomFormatInput { Audio = AudioFormatEvidence.ForFile(new MediaInfoModel { AudioFormat = "E-AC-3" }, null) };
        Program.Check("published_unreported_atmos_cannot_match_a_positive_or_negated_atmos_rule",
            !new AudioCodecSpecification { Value = (int)AudioCodecKind.Atmos }.IsSatisfiedBy(input) &&
            !new AudioCodecSpecification { Value = (int)AudioCodecKind.Atmos, Negate = true }.IsSatisfiedBy(input));
    }
}
