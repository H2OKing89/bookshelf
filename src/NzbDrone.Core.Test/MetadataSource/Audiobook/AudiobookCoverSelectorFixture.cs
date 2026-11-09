using System.Collections.Generic;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.MediaCover;
using NzbDrone.Core.MetadataSource.Audiobook;
using MediaCoverModel = NzbDrone.Core.MediaCover.MediaCover;

namespace NzbDrone.Core.Test.MetadataSource.Audiobook
{
    [TestFixture]
    public class AudiobookCoverSelectorFixture
    {
        private const string Resized = "https://m.media-amazon.com/images/I/51POf8gOyLL._SL500_.jpg";
        private const string Larger = "https://m.media-amazon.com/images/I/51POf8gOyLL._SL2400_.jpg";
        private const string Original = "https://m.media-amazon.com/images/I/51POf8gOyLL.jpg";

        [Test]
        public void should_choose_the_largest_valid_returned_variant_without_changing_its_url()
        {
            var images = new Dictionary<int, string>
            {
                [500] = Resized,
                [2400] = Larger,
                [4000] = "not an image URL",
                [5000] = "file:///images/cover.jpg"
            };

            AudiobookCoverSelector.Largest(images).Should().Be(Larger);
        }

        [TestCase(Resized, true)]
        [TestCase("https://m.media-amazon.com/images/I/51POf8gOyLL._SL300_.jpg", true)]
        [TestCase(Larger, false)]
        [TestCase(Original, false)]
        [TestCase("https://example.com/cover.jpg", false)]
        public void should_only_enrich_covers_known_to_be_small(string url, bool expected)
        {
            AudiobookCoverSelector.NeedsEnrichment(new List<MediaCoverModel> { new MediaCoverModel(MediaCoverTypes.Cover, url) })
                .Should().Be(expected);
        }

        [Test]
        public void should_enrich_missing_covers_but_not_repeat_a_lookup_when_a_large_cover_exists()
        {
            AudiobookCoverSelector.NeedsEnrichment(null).Should().BeTrue();
            AudiobookCoverSelector.NeedsEnrichment(new List<MediaCoverModel>()).Should().BeTrue();
            AudiobookCoverSelector.NeedsEnrichment(new List<MediaCoverModel> { new MediaCoverModel(MediaCoverTypes.Banner, Original) }).Should().BeTrue();
            AudiobookCoverSelector.NeedsEnrichment(new List<MediaCoverModel>
            {
                new MediaCoverModel(MediaCoverTypes.Cover, Resized),
                new MediaCoverModel(MediaCoverTypes.Cover, Larger)
            }).Should().BeFalse();
        }

        [TestCase(Resized, Larger, Larger)]
        [TestCase(Larger, Resized, Larger)]
        [TestCase(Resized, Original, Original)]
        [TestCase(Original, Larger, Original)]
        public void should_prefer_the_better_supplied_variant_of_the_same_asset(string preferred, string alternate, string expected)
        {
            Select(preferred, alternate).Should().Be(expected);
        }

        [TestCase("https://m.media-amazon.com/images/I/81DifferentAsset.jpg")]
        [TestCase("https://example.com/images/I/51POf8gOyLL.jpg")]
        [TestCase("https://m.media-amazon.com.example.org/images/I/51POf8gOyLL.jpg")]
        [TestCase("https://m.media-amazon.com/images/I/51POf8gOyLL._SX2400_.jpg")]
        [TestCase("https://m.media-amazon.com/images/I/51POf8gOyLL.jpg?resize=100")]
        [TestCase("file:///images/I/51POf8gOyLL.jpg")]
        public void should_not_guess_the_quality_of_an_unrelated_or_unrecognized_image(string alternate)
        {
            Select(Resized, alternate).Should().Be(Resized);
        }

        [Test]
        public void should_recognize_the_same_asset_on_known_legacy_amazon_hosts()
        {
            Select(Resized, "https://images-na.ssl-images-amazon.com/images/I/51POf8gOyLL.jpg")
                .Should().Be("https://images-na.ssl-images-amazon.com/images/I/51POf8gOyLL.jpg");
        }

        [Test]
        public void should_preserve_unrelated_cover_types_and_inputs()
        {
            var preferred = new List<MediaCoverModel>
            {
                new MediaCoverModel(MediaCoverTypes.Cover, Resized),
                new MediaCoverModel(MediaCoverTypes.Banner, "https://example.com/banner.jpg")
            };
            var alternate = new List<MediaCoverModel> { new MediaCoverModel(MediaCoverTypes.Cover, Original) };

            var result = AudiobookCoverSelector.PreferLarger(preferred, alternate);

            result[0].Url.Should().Be(Original);
            result[1].Url.Should().Be("https://example.com/banner.jpg");
            preferred[0].Url.Should().Be(Resized);
            alternate[0].Url.Should().Be(Original);
        }

        [Test]
        public void should_fill_missing_images_and_preserve_images_when_the_alternative_is_empty()
        {
            var images = new List<MediaCoverModel> { new MediaCoverModel(MediaCoverTypes.Cover, Original) };

            AudiobookCoverSelector.PreferLarger(new List<MediaCoverModel>(), images).Should().Equal(images);
            AudiobookCoverSelector.PreferLarger(images, null).Should().Equal(images);
            AudiobookCoverSelector.PreferLarger(null, null).Should().BeEmpty();
        }

        [Test]
        public void should_fill_a_missing_cover_when_other_image_types_are_already_present()
        {
            var banner = new MediaCoverModel(MediaCoverTypes.Banner, "https://example.com/banner.jpg");
            var cover = new MediaCoverModel(MediaCoverTypes.Cover, Larger);
            var preferred = new List<MediaCoverModel> { banner };

            var result = AudiobookCoverSelector.PreferLarger(preferred, new List<MediaCoverModel> { cover });

            result.Should().Equal(banner, cover);
            preferred.Should().Equal(banner);
            AudiobookCoverSelector.NeedsEnrichment(result).Should().BeFalse();
        }

        private static string Select(string preferred, string alternate) => AudiobookCoverSelector.PreferLarger(
            new List<MediaCoverModel> { new MediaCoverModel(MediaCoverTypes.Cover, preferred) },
            new List<MediaCoverModel> { new MediaCoverModel(MediaCoverTypes.Cover, alternate) })[0].Url;
    }
}
