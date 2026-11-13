using System;
using System.IO;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.MediaFiles
{
    [TestFixture]
    public class AudioRecordingIdentifierFixture : CoreTest
    {
        [TestCase("B0CR45YNXY")]
        [TestCase("B0DFHMW24T")]
        [TestCase("1774249162")]
        public void should_read_filename_asin_even_when_tags_are_complete(string asin)
        {
            var tags = new ParsedTrackInfo { Title = "English title", BookTitle = "日本語のアルバム" };

            AudioRecordingIdentifier.Apply(tags, $"Complete book [{asin}].m4b", null);

            tags.Asin.Should().Be(asin);
            tags.AsinConflict.Should().BeFalse();
            tags.BookTitle.Should().Be("日本語のアルバム");
        }

        [TestCase("Book [Unabridged].m4b")]
        [TestCase("Book [B0CR45YNX].m4b")]
        [TestCase("Book B0CR45YNXY.m4b")]
        [TestCase("Book [ASIN000000].m4b")]
        public void should_not_interpret_arbitrary_text_as_recording_identity(string path)
        {
            var tags = new ParsedTrackInfo();

            AudioRecordingIdentifier.Apply(tags, path, null);

            tags.Asin.Should().BeNull();
            tags.AsinConflict.Should().BeFalse();
        }

        [Test]
        public void should_preserve_embedded_asin_and_flag_conflicting_filename()
        {
            var tags = new ParsedTrackInfo();

            AudioRecordingIdentifier.Apply(tags, "Book [B0DFHMW24T].m4b", new[] { " b0cr45ynxy " });

            tags.Asin.Should().Be("B0CR45YNXY");
            tags.AsinConflict.Should().BeTrue();
        }

        [Test]
        public void should_flag_multiple_recording_ids_in_one_filename()
        {
            var tags = new ParsedTrackInfo();

            AudioRecordingIdentifier.Apply(tags, "Book [B0CR45YNXY] [B0DFHMW24T].m4b", null);

            tags.Asin.Should().BeNull();
            tags.AsinConflict.Should().BeTrue();
        }

        [Test]
        public void should_allow_repeated_matching_identifier()
        {
            var tags = new ParsedTrackInfo();

            AudioRecordingIdentifier.Apply(tags, "Book [b0cr45ynxy] [B0CR45YNXY].m4b", new[] { "B0CR45YNXY", "B0CR45YNXY" });

            tags.Asin.Should().Be("B0CR45YNXY");
            tags.AsinConflict.Should().BeFalse();
        }

        [TestCase("Book [B0BXV7C8PX]/01.mp3")]
        [TestCase("Book [B0BXV7C8PX]/CD 1/01.mp3")]
        [TestCase("Book [B0BXV7C8PX]/Disc 1/Tracks/Audio/01.mp3")]
        public void should_read_recording_folder_asin_for_multipart_files(string path)
        {
            var tags = new ParsedTrackInfo();

            AudioRecordingIdentifier.Apply(tags, path, null);

            tags.Asin.Should().Be("B0BXV7C8PX");
            tags.AsinConflict.Should().BeFalse();
        }

        [Test]
        public void should_only_use_the_nearest_recording_folder()
        {
            var tags = new ParsedTrackInfo();

            AudioRecordingIdentifier.Apply(tags, "Library [B0CCFTQW7R]/Book [B0BXV7C8PX]/CD 1/01.mp3", null);

            tags.Asin.Should().Be("B0BXV7C8PX");
            tags.AsinConflict.Should().BeFalse();
        }

        [Test]
        public void should_not_search_unbounded_ancestors_for_recording_ids()
        {
            var tags = new ParsedTrackInfo();

            AudioRecordingIdentifier.Apply(tags, "Library [B0BXV7C8PX]/Author/Series/Book/CD 1/01.mp3", null);

            tags.Asin.Should().BeNull();
            tags.AsinConflict.Should().BeFalse();
        }

        [TestCase("Book [B0CCFTQW7R]/File [B0BXV7C8PX].m4b", null)]
        [TestCase("Book [B0CCFTQW7R]/01.mp3", "B0BXV7C8PX")]
        public void should_flag_disagreement_between_recording_folder_and_file(string path, string embeddedAsin)
        {
            var tags = new ParsedTrackInfo { Asin = embeddedAsin };

            AudioRecordingIdentifier.Apply(tags, path, null);

            tags.Asin.Should().Be("B0BXV7C8PX");
            tags.AsinConflict.Should().BeTrue();
        }

        [TestCase("B0BXV7C8PX", "B0HLLTHK38")]
        [TestCase("B0CCFTQW7R", "B0HLLTHK38")]
        [TestCase("B0F14RFHS6", "B0F14RPXHR")]
        public void should_reject_a_different_volume_or_regional_recording(string fileAsin, string editionAsin)
        {
            var edition = new Edition { Asin = editionAsin, ForeignEditionId = $"audible:us:{editionAsin}" };

            AudioRecordingIdentifier.MatchesEdition(new ParsedTrackInfo(), $"Book [{fileAsin}].m4b", edition).Should().BeFalse();
        }

        [Test]
        public void should_reject_inconsistent_catalog_identity_even_if_the_asin_field_matches()
        {
            var edition = new Edition { Asin = "B0BXV7C8PX", ForeignEditionId = "audible:us:B0HLLTHK38" };

            AudioRecordingIdentifier.MatchesEdition(new ParsedTrackInfo(), "Book [B0BXV7C8PX].m4b", edition).Should().BeFalse();
        }

        [Test]
        public void should_accept_the_exact_recording_in_its_own_marketplace()
        {
            var edition = new Edition { Asin = "B0F14RFHS6", ForeignEditionId = "audible:uk:B0F14RFHS6" };

            AudioRecordingIdentifier.MatchesEdition(new ParsedTrackInfo(), "Book [B0F14RFHS6].m4b", edition).Should().BeTrue();
        }

        [Test]
        public void should_flag_a_malformed_embedded_recording_identifier()
        {
            var tags = new ParsedTrackInfo();

            AudioRecordingIdentifier.Apply(tags, "Book [B0BXV7C8PX].m4b", new[] { "not-an-asin" });

            tags.Asin.Should().Be("B0BXV7C8PX");
            tags.AsinConflict.Should().BeTrue();
        }

        [TestCase("Book [B0D1CW8W56].m4b")]
        [TestCase("Book [B0D1CW8W56]/Book [B0D1CW8W56].m4b")]
        [TestCase("Book [B0D1CW8W56]/CD 1/01.mp3")]
        public void should_use_curated_path_when_explicitly_preferred(string path)
        {
            var tags = new ParsedTrackInfo { PreferPathAsin = true };
            AudioRecordingIdentifier.Apply(tags, path, new[] { "B079TJTK8S" });
            tags.Asin.Should().Be("B0D1CW8W56");
            tags.AsinConflict.Should().BeFalse();

            // Identification applies the constraint again before import.
            AudioRecordingIdentifier.MatchesEdition(tags, path, new Edition
            {
                Asin = "B0D1CW8W56", ForeignEditionId = "audible:us:B0D1CW8W56"
            }).Should().BeTrue();
            AudioRecordingIdentifier.MatchesEdition(tags, path, new Edition
            {
                Asin = "B0F14PB6WN", ForeignEditionId = "audible:us:B0F14PB6WN"
            }).Should().BeFalse();
        }

        [TestCase("Book [B0F14RFHS6]/Book [B0F14RPXHR].m4b")]
        [TestCase("Book [B0F14RFHS6] [B0F14RPXHR].m4b")]
        public void should_still_block_conflicting_path_ids_with_path_preference(string path)
        {
            var tags = new ParsedTrackInfo { PreferPathAsin = true };
            AudioRecordingIdentifier.Apply(tags, path, new[] { "B0F14RPXHR" });
            tags.AsinConflict.Should().BeTrue();
            AudioRecordingIdentifier.MatchesEdition(tags, path, new Edition { Asin = "B0F14RPXHR" }).Should().BeFalse();
        }

        [Test]
        public void should_keep_strict_embedded_checks_without_a_path_id()
        {
            var tags = new ParsedTrackInfo { PreferPathAsin = true };
            AudioRecordingIdentifier.Apply(tags, "Book.m4b", new[] { "B0F14RFHS6", "B0F14RPXHR" });
            tags.AsinConflict.Should().BeTrue();
        }

        [Test]
        public void should_keep_regional_recordings_separate_with_path_preference()
        {
            var tags = new ParsedTrackInfo { PreferPathAsin = true };
            var path = "Book [B0F14RFHS6].m4b";
            AudioRecordingIdentifier.Apply(tags, path, new[] { "B0F14RPXHR" });
            AudioRecordingIdentifier.MatchesEdition(tags, path, new Edition
            {
                Asin = "B0F14RFHS6", ForeignEditionId = "audible:uk:B0F14RFHS6"
            }).Should().BeTrue();
            AudioRecordingIdentifier.MatchesEdition(tags, path, new Edition
            {
                Asin = "B0F14RPXHR", ForeignEditionId = "audible:us:B0F14RPXHR"
            }).Should().BeFalse();
        }

        private static void SetAsinTag(TagLib.File file, string extension, string name, string value, string mean = "com.apple.iTunes")
        {
            switch (extension)
            {
                case "mp3":
                    var id3 = (TagLib.Id3v2.Tag)file.GetTag(TagLib.TagTypes.Id3v2);
                    TagLib.Id3v2.UserTextInformationFrame.Get(id3, name, true).Text = new[] { value };
                    break;
                case "flac":
                    ((TagLib.Ogg.XiphComment)file.GetTag(TagLib.TagTypes.Xiph)).SetField(name, value);
                    break;
                case "ape":
                    ((TagLib.Ape.Tag)file.GetTag(TagLib.TagTypes.Ape)).SetValue(name, value);
                    break;
                case "wma":
                    ((TagLib.Asf.Tag)file.GetTag(TagLib.TagTypes.Asf)).SetDescriptorString(value, name);
                    break;
                case "m4a":
                    ((TagLib.Mpeg4.AppleTag)file.GetTag(TagLib.TagTypes.Apple)).SetDashBox(mean, name, value);
                    break;
            }
        }

        [TestCase("mp3", "AUDIBLE_ASIN")]
        [TestCase("mp3", "aUdIbLe_AsIn")]
        [TestCase("flac", "AUDIBLE_ASIN")]
        [TestCase("flac", "audible_asin")]
        [TestCase("ape", "AUDIBLE_ASIN")]
        [TestCase("ape", "audible_asin")]
        [TestCase("wma", "AUDIBLE_ASIN")]
        [TestCase("wma", "audible_asin")]
        [TestCase("m4a", "AUDIBLE_ASIN")]
        [TestCase("m4a", "audible_asin")]
        public void should_read_embedded_audible_asin_aliases(string extension, string name)
        {
            var path = Path.Combine(Path.GetTempPath(), $"bookshelf-asin-{Guid.NewGuid():N}.{extension}");
            try
            {
                File.Copy(Path.Combine(TestContext.CurrentContext.TestDirectory, "Files", "Media", $"nin.{extension}"), path);
                using (var file = TagLib.File.Create(path))
                {
                    SetAsinTag(file, extension, name, "B0F14RFHS6");
                    file.Save();
                }

                ParsedTrackInfo parsed = new AudioTag(path);

                parsed.Asin.Should().Be("B0F14RFHS6");
                parsed.AsinConflict.Should().BeFalse();
            }
            finally
            {
                File.Delete(path);
            }
        }

        [TestCase("mp3")]
        [TestCase("flac")]
        [TestCase("ape")]
        [TestCase("wma")]
        [TestCase("m4a")]
        public void should_reject_conflicting_embedded_asin_aliases(string extension)
        {
            var path = Path.Combine(Path.GetTempPath(), $"bookshelf-asin-{Guid.NewGuid():N}.{extension}");
            try
            {
                File.Copy(Path.Combine(TestContext.CurrentContext.TestDirectory, "Files", "Media", $"nin.{extension}"), path);
                using (var file = TagLib.File.Create(path))
                {
                    SetAsinTag(file, extension, "ASIN", "B0F14RPXHR");
                    SetAsinTag(file, extension, "AUDIBLE_ASIN", "B0F14RFHS6");
                    file.Save();
                }

                ParsedTrackInfo parsed = new AudioTag(path);

                parsed.AsinConflict.Should().BeTrue();
            }
            finally
            {
                File.Delete(path);
            }
        }

        [TestCase("com.audible", "AUDIBLE_ASIN")]
        [TestCase("com.pilabor.tone", "AUDIBLE_ASIN")]
        [TestCase("custom.tagger", "aUdIbLe_AsIn")]
        public void should_read_mp4_recording_aliases_in_tagger_namespaces(string mean, string name)
        {
            var path = Path.Combine(Path.GetTempPath(), $"bookshelf-asin-{Guid.NewGuid():N}.m4b");
            try
            {
                File.Copy(Path.Combine(TestContext.CurrentContext.TestDirectory, "Files", "Media", "nin.m4a"), path);
                using (var file = TagLib.File.Create(path))
                {
                    SetAsinTag(file, "m4a", name, "B0F14RFHS6", mean);
                    file.Save();
                }

                ParsedTrackInfo parsed = new AudioTag(path);

                parsed.Asin.Should().Be("B0F14RFHS6");
                parsed.AsinConflict.Should().BeFalse();
            }
            finally
            {
                File.Delete(path);
            }
        }

        [TestCase("[B0CR45YNXY]", "B0CR45YNXY", false)]
        [TestCase("[B0CR45YNXY] [B0DFHMW24T]", null, true)]
        public void should_retain_filename_identifiers_when_audio_tag_reading_fails(string identifiers, string asin, bool conflict)
        {
            var path = Path.Combine(Path.GetTempPath(), $"bookshelf-missing-{Guid.NewGuid():N} {identifiers}.m4b");

            ParsedTrackInfo parsed = new AudioTag(path);

            parsed.Asin.Should().Be(asin);
            parsed.AsinConflict.Should().Be(conflict);
            ExceptionVerification.ExpectedErrors(1);
        }

        [TestCase(true)]
        [TestCase(false)]
        public void should_read_embedded_mp4_asin_even_if_other_tags_are_invalid(bool otherTagsValid)
        {
            var path = Path.Combine(Path.GetTempPath(), $"bookshelf-asin-{Guid.NewGuid():N}.m4b");
            try
            {
                File.Copy(Path.Combine(TestContext.CurrentContext.TestDirectory, "Files", "Media", "nin.m4a"), path);
                using (var file = TagLib.File.Create(path))
                {
                    var tag = (TagLib.Mpeg4.AppleTag)file.GetTag(TagLib.TagTypes.Apple);
                    tag.SetDashBox("com.apple.iTunes", "ASIN", "B0CR45YNXY");
                    file.Save();
                }

                var audioTag = new AudioTag(path);
                if (!otherTagsValid)
                {
                    // Reproduce a later tag/property read failing after the identifier has
                    // already been read, without depending on a specific TagLib corruption.
                    typeof(AudioTag).GetProperty(nameof(AudioTag.IsValid)).SetValue(audioTag, false);
                }

                ParsedTrackInfo parsed = audioTag;

                parsed.Asin.Should().Be("B0CR45YNXY");
                parsed.AsinConflict.Should().BeFalse();
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Test]
        public void should_detect_multiple_embedded_id3_recording_identifiers()
        {
            var path = Path.Combine(Path.GetTempPath(), $"bookshelf-asin-{Guid.NewGuid():N} [B0CR45YNXY].mp3");
            try
            {
                File.Copy(Path.Combine(TestContext.CurrentContext.TestDirectory, "Files", "Media", "nin.mp3"), path);
                using (var file = TagLib.File.Create(path))
                {
                    var tag = (TagLib.Id3v2.Tag)file.GetTag(TagLib.TagTypes.Id3v2);
                    TagLib.Id3v2.UserTextInformationFrame.Get(tag, "ASIN", true).Text = new[] { "B0CR45YNXY", "B0DFHMW24T" };
                    file.Save();
                }

                ParsedTrackInfo parsed = new AudioTag(path);

                parsed.AsinConflict.Should().BeTrue();
            }
            finally
            {
                File.Delete(path);
            }
        }
    }
}
