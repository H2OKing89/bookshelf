using System;
using System.Buffers.Binary;
using System.IO;
using FluentAssertions;
using ICSharpCode.SharpZipLib.Checksum;
using ImageMagick;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MediaCoverTests
{
    [TestFixture]
    [NonParallelizable]
    public class ImageResizerFixture : CoreTest<MediaCover.ImageResizer>
    {
        [SetUp]
        public void SetUp()
        {
            var disk = Mocker.GetMock<IDiskProvider>();
            disk.Setup(provider => provider.FileExists(It.IsAny<string>())).Returns<string>(File.Exists);
            disk.Setup(provider => provider.DeleteFile(It.IsAny<string>())).Callback<string>(File.Delete);
            disk.Setup(provider => provider.MoveFile(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()))
                .Callback<string, string, bool>((source, destination, overwrite) => File.Move(source, destination, overwrite));
            Mocker.SetConstant<IPlatformInfo>(Mocker.Resolve<PlatformInfo>());

            // Production policy must be configured before test encoders touch native state.
            _ = Subject;
        }

        [TestCase("jpg", MagickFormat.Jpeg)]
        [TestCase("JPEG", MagickFormat.Jpeg)]
        [TestCase("png", MagickFormat.Png)]
        [TestCase("gif", MagickFormat.Gif)]
        [TestCase("tif", MagickFormat.Tiff)]
        [TestCase("tiff", MagickFormat.Tiff)]
        [TestCase("bmp", MagickFormat.Bmp)]
        [TestCase("webp", MagickFormat.WebP)]
        public void should_resize_common_raster_formats_without_changing_source_or_format(string extension, MagickFormat format)
        {
            var source = Path.Combine(TempFolder, "source." + extension);
            var destination = Path.Combine(TempFolder, "thumbnail." + extension);
            using (var image = CreateImage(120, 60, 255, 0, 0))
            {
                image.Write(source, format);
            }

            var original = File.ReadAllBytes(source);

            Subject.Resize(source, destination, 30);

            using (var image = new MagickImage(destination))
            {
                image.Width.Should().Be(60);
                image.Height.Should().Be(30);
                image.Format.Should().Be(format);
                if (format == MagickFormat.Jpeg)
                {
                    image.Quality.Should().Be(92);
                }
            }

            File.ReadAllBytes(source).Should().Equal(original);
            AssertFilesReleased(source, destination);
            AssertNoTemporaryOutput();
        }

        [Test]
        public void should_resize_existing_png_fixture()
        {
            var destination = Path.Combine(TempFolder, "logo-170.png");

            Subject.Resize(GetTestPath("Files/1024.png"), destination, 170);

            using var image = new MagickImage(destination);
            image.Width.Should().Be(170);
            image.Height.Should().Be(170);
        }

        [Test]
        public void should_preserve_png_transparency()
        {
            var source = Path.Combine(TempFolder, "transparent.png");
            var destination = Path.Combine(TempFolder, "thumbnail.png");
            using (var image = CreateImage(100, 50, 255, 0, 0, 0))
            {
                image.Write(source, MagickFormat.Png);
            }

            Subject.Resize(source, destination, 25);

            using var resized = new MagickImage(destination);
            resized.Width.Should().Be(50);
            resized.Height.Should().Be(25);
            resized.HasAlpha.Should().BeTrue();
            using var pixels = resized.GetPixels();
            pixels.GetPixel(0, 0).ToColor().A.Should().Be(0);
        }

        [Test]
        public void should_preserve_gif_frames_delays_and_loop_count()
        {
            var source = Path.Combine(TempFolder, "animated.gif");
            var destination = Path.Combine(TempFolder, "thumbnail.gif");
            using (var images = new MagickImageCollection())
            {
                var first = CreateImage(80, 40, 255, 0, 0);
                first.AnimationDelay = 10;
                first.AnimationIterations = 3;
                images.Add(first);
                var second = CreateImage(80, 40, 0, 0, 255);
                second.AnimationDelay = 20;
                second.AnimationIterations = 3;
                images.Add(second);
                images.Write(source, MagickFormat.Gif);
            }

            Subject.Resize(source, destination, 20);

            using var resized = new MagickImageCollection(destination);
            resized.Count.Should().Be(2);
            resized.Should().OnlyContain(image => image.Width == 40 && image.Height == 20);
            resized[0].AnimationDelay.Should().Be(10);
            resized[1].AnimationDelay.Should().Be(20);
            resized[0].AnimationIterations.Should().Be(3);
        }

        [Test]
        public void should_resize_tiff_pages_independently()
        {
            var source = Path.Combine(TempFolder, "pages.tiff");
            var destination = Path.Combine(TempFolder, "thumbnail.tiff");
            using (var images = new MagickImageCollection())
            {
                images.Add(CreateImage(80, 40, 255, 0, 0));
                images.Add(CreateImage(40, 80, 0, 0, 255));
                images.Write(source, MagickFormat.Tiff);
            }

            Subject.Resize(source, destination, 20);

            using var resized = new MagickImageCollection(destination);
            resized.Count.Should().Be(2);
            resized[0].Width.Should().Be(40);
            resized[1].Width.Should().Be(10);
            resized.Should().OnlyContain(image => image.Height == 20);
        }

        [TestCase("Just some junk data")]
        [TestCase("<svg xmlns='http://www.w3.org/2000/svg'><image href='https://synthetic.invalid/probe'/></svg>")]
        [TestCase("%PDF-1.7\nunsupported document")]
        [TestCase("push graphic-context\nviewbox 0 0 1 1\npop graphic-context")]
        public void should_reject_non_raster_input_and_remove_failed_cached_output(string contents)
        {
            var source = Path.Combine(TempFolder, "disguised.png");
            var destination = Path.Combine(TempFolder, "thumbnail.png");
            File.WriteAllText(source, contents);
            File.WriteAllText(destination, "old cached thumbnail");

            Assert.Throws<InvalidDataException>(() => Subject.Resize(source, destination, 20));

            File.ReadAllText(source).Should().Be(contents);
            File.Exists(destination).Should().BeFalse();
            AssertNoTemporaryOutput();
            AssertFilesReleased(source);
        }

        [Test]
        public void native_policy_should_also_reject_svg_without_the_managed_signature_guard()
        {
            using var input = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='1' height='1'></svg>"));
            Assert.Throws(Is.InstanceOf<MagickException>(), () =>
            {
                using var image = new MagickImage(input, MagickFormat.Svg);
            });
        }

        [TestCase(false)]
        [TestCase(true)]
        public void should_never_delete_a_source_when_destination_is_the_same_canonical_path(bool valid)
        {
            var source = Path.Combine(TempFolder, "original.png");
            if (valid)
            {
                File.Copy(GetTestPath("Files/1024.png"), source);
            }
            else
            {
                File.WriteAllText(source, "original invalid data");
            }

            var bytes = File.ReadAllBytes(source);
            var destination = Path.Combine(TempFolder, ".", "original.png");

            Assert.Throws<ArgumentException>(() => Subject.Resize(source, destination, 20));

            File.ReadAllBytes(source).Should().Equal(bytes);
            AssertNoTemporaryOutput();
        }

        [TestCase(-1)]
        [TestCase(0)]
        [TestCase(16385)]
        public void should_reject_invalid_output_height(int height)
        {
            var destination = Path.Combine(TempFolder, "thumbnail.png");

            Assert.Throws<ArgumentOutOfRangeException>(() => Subject.Resize(GetTestPath("Files/1024.png"), destination, height));

            File.Exists(destination).Should().BeFalse();
            AssertNoTemporaryOutput();
        }

        [Test]
        [Platform("Linux")]
        public void should_preserve_source_when_a_parent_directory_alias_reaches_the_same_file()
        {
            var realDirectory = Path.Combine(TempFolder, "real");
            var aliasDirectory = Path.Combine(TempFolder, "alias");
            Directory.CreateDirectory(realDirectory);
            Directory.CreateSymbolicLink(aliasDirectory, realDirectory);
            var source = Path.Combine(realDirectory, "original.png");
            File.Copy(GetTestPath("Files/1024.png"), source);
            var bytes = File.ReadAllBytes(source);

            Assert.Throws<ArgumentException>(() => Subject.Resize(source, Path.Combine(aliasDirectory, "original.png"), 20));

            File.ReadAllBytes(source).Should().Equal(bytes);
            AssertNoTemporaryOutput();
        }

        [Test]
        [Platform("Linux")]
        public void should_preserve_source_target_when_reading_through_a_final_symlink()
        {
            var original = Path.Combine(TempFolder, "original.png");
            var source = Path.Combine(TempFolder, "source-link.png");
            File.Copy(GetTestPath("Files/1024.png"), original);
            File.CreateSymbolicLink(source, original);
            var bytes = File.ReadAllBytes(original);

            Assert.Throws<ArgumentException>(() => Subject.Resize(source, original, 20));

            File.ReadAllBytes(original).Should().Equal(bytes);
            File.ReadAllBytes(source).Should().Equal(bytes);
            AssertNoTemporaryOutput();
        }

        [Test]
        [Platform("Linux")]
        public void should_replace_a_destination_symlink_without_overwriting_the_original()
        {
            var source = Path.Combine(TempFolder, "original.png");
            var destination = Path.Combine(TempFolder, "destination-link.png");
            File.Copy(GetTestPath("Files/1024.png"), source);
            File.CreateSymbolicLink(destination, source);
            var bytes = File.ReadAllBytes(source);

            Subject.Resize(source, destination, 20);

            File.ReadAllBytes(source).Should().Equal(bytes);
            new FileInfo(destination).LinkTarget.Should().BeNull();
            using var image = new MagickImage(destination);
            image.Height.Should().Be(20);
        }

        [TestCase(17000u, 1u)]
        [TestCase(1u, 17000u)]
        [TestCase(5000u, 4000u)]
        public void should_reject_declared_dimensions_before_allocating_pixels(uint width, uint height)
        {
            var source = Path.Combine(TempFolder, "dimensions.png");
            var destination = Path.Combine(TempFolder, "thumbnail.png");
            var bytes = File.ReadAllBytes(GetTestPath("Files/1024.png"));
            BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(16, 4), width);
            BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(20, 4), height);
            var crc = new Crc32();
            crc.Update(new ArraySegment<byte>(bytes, 12, 17));
            BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(29, 4), (uint)crc.Value);
            File.WriteAllBytes(source, bytes);

            Assert.Throws(Is.InstanceOf<Exception>(), () => Subject.Resize(source, destination, 20));

            File.ReadAllBytes(source).Should().Equal(bytes);
            File.Exists(destination).Should().BeFalse();
            AssertNoTemporaryOutput();
        }

        [Test]
        public void should_reject_more_than_64_frames()
        {
            var source = Path.Combine(TempFolder, "many-frames.gif");
            var destination = Path.Combine(TempFolder, "thumbnail.gif");
            var single = Convert.FromBase64String("R0lGODlhAQABAIAAAP///wAAACH5BAEAAAAALAAAAAABAAEAAAICRAEAOw==");
            using (var output = File.Create(source))
            {
                output.Write(single, 0, 19);
                for (var i = 0; i < 65; i++)
                {
                    output.Write(single, 19, single.Length - 20);
                }

                output.WriteByte(0x3b);
            }

            Assert.Throws(Is.InstanceOf<Exception>(), () => Subject.Resize(source, destination, 20));

            File.Exists(destination).Should().BeFalse();
            AssertNoTemporaryOutput();
        }

        [Test]
        public void should_reject_encoded_input_over_32_mib_without_reading_it()
        {
            var source = Path.Combine(TempFolder, "large.png");
            var destination = Path.Combine(TempFolder, "thumbnail.png");
            using (var stream = File.Create(source))
            {
                stream.SetLength((32L * 1024 * 1024) + 1);
            }

            Assert.Throws<InvalidDataException>(() => Subject.Resize(source, destination, 20));

            File.Exists(destination).Should().BeFalse();
            AssertNoTemporaryOutput();
        }

        [Test]
        public void should_remove_partial_output_after_publication_failure_and_keep_source()
        {
            var source = Path.Combine(TempFolder, "source.png");
            var destination = Path.Combine(TempFolder, "thumbnail.png");
            File.Copy(GetTestPath("Files/1024.png"), source);
            var original = File.ReadAllBytes(source);
            var failure = new IOException("synthetic move failure");
            Mocker.GetMock<IDiskProvider>().Setup(provider => provider.MoveFile(It.IsAny<string>(), destination, true))
                .Callback<string, string, bool>((temporary, target, overwrite) =>
                {
                    File.Move(temporary, target, overwrite);
                    throw failure;
                });

            Assert.Throws<IOException>(() => Subject.Resize(source, destination, 20)).Should().BeSameAs(failure);

            File.ReadAllBytes(source).Should().Equal(original);
            File.Exists(destination).Should().BeFalse();
            AssertNoTemporaryOutput();
            AssertFilesReleased(source);
        }

        private void AssertNoTemporaryOutput()
        {
            Directory.GetFiles(TempFolder, ".cover-*").Should().BeEmpty();
        }

        private static MagickImage CreateImage(uint width, uint height, byte red, byte green, byte blue, byte alpha = 255)
        {
            var pixels = new byte[checked((int)(width * height * 4))];
            for (var offset = 0; offset < pixels.Length; offset += 4)
            {
                pixels[offset] = red;
                pixels[offset + 1] = green;
                pixels[offset + 2] = blue;
                pixels[offset + 3] = alpha;
            }

            var image = new MagickImage();
            try
            {
                image.ReadPixels(pixels, new PixelReadSettings(width, height, StorageType.Char, PixelMapping.RGBA));
                return image;
            }
            catch
            {
                image.Dispose();
                throw;
            }
        }

        private static void AssertFilesReleased(params string[] paths)
        {
            foreach (var path in paths)
            {
                using var stream = File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                stream.CanRead.Should().BeTrue();
            }
        }
    }
}
