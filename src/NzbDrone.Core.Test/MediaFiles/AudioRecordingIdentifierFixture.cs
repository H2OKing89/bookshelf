using System;
using System.IO;
using FluentAssertions;
using NUnit.Framework;
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
