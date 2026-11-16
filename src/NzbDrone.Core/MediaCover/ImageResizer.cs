using System;
using System.IO;
using ImageMagick;
using ImageMagick.Configuration;
using NzbDrone.Common.Disk;
using NzbDrone.Common.EnvironmentInfo;

namespace NzbDrone.Core.MediaCover
{
    public interface IImageResizer
    {
        void Resize(string source, string destination, int height);
    }

    public class ImageResizer : IImageResizer
    {
        private const long MaximumInputBytes = 32L * 1024 * 1024;
        private const uint MaximumDimension = 16384;
        private const ulong MaximumFramePixels = 16UL * 1024 * 1024;
        private const ulong MaximumTotalPixels = 64UL * 1024 * 1024;
        private const int MaximumFrames = 64;
        private static readonly object ImageGate = new object();
        private static bool _initialized;
        private readonly IDiskProvider _diskProvider;

        public ImageResizer(IDiskProvider diskProvider, IPlatformInfo platformInfo)
        {
            _diskProvider = diskProvider;
            lock (ImageGate)
            {
                Initialize(diskProvider);
            }
        }

        public void Resize(string source, string destination, int height)
        {
            var comparison = OsInfo.IsWindows || OsInfo.IsOsx ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            var destinationEntry = ResolveExistingLinks(destination, false);
            if (string.Equals(ResolveExistingLinks(source, false), destinationEntry, comparison) ||
                string.Equals(ResolveExistingLinks(source, true), destinationEntry, comparison))
            {
                throw new ArgumentException("The resized cover must have a different destination", nameof(destination));
            }

            lock (ImageGate)
            {
                ResizeInternal(source, destination, height);
            }
        }

        private void ResizeInternal(string source, string destination, int height)
        {
            var temporary = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(destination)), ".cover-" + Guid.NewGuid().ToString("N"));
            try
            {
                if (height <= 0 || height > MaximumDimension)
                {
                    throw new ArgumentOutOfRangeException(nameof(height));
                }

                var outputFormat = GetOutputFormat(destination);
                using (var input = File.OpenRead(source))
                {
                    if (input.Length > MaximumInputBytes)
                    {
                        throw InvalidImage();
                    }

                    var inputFormat = GetInputFormat(input);
                    var settings = new MagickReadSettings { Format = inputFormat };
                    using (var information = new MagickImageCollection())
                    {
                        information.Ping(input, settings);
                        ValidateFrames(information, (uint)height, inputFormat);
                    }

                    input.Position = 0;
                    using var images = new MagickImageCollection(input, settings);
                    ValidateFrames(images, (uint)height, inputFormat);
                    if (images.Count > 1 && outputFormat != MagickFormat.Gif && outputFormat != MagickFormat.Tiff && outputFormat != MagickFormat.WebP)
                    {
                        throw InvalidImage();
                    }

                    if (images.Count > 1 && (inputFormat == MagickFormat.Gif || inputFormat == MagickFormat.WebP))
                    {
                        images.Coalesce();
                    }

                    foreach (var image in images)
                    {
                        image.Resize(0, (uint)height);
                        image.Page = new MagickGeometry(image.Width, image.Height);
                        image.Quality = 92;
                    }

                    ValidateFrames(images, (uint)height, outputFormat);
                    using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
                    {
                        images.Write(output, outputFormat);
                    }
                }

                // Publish a completed file, without following an existing destination
                // symlink or truncating a hard link to the original cover.
                _diskProvider.MoveFile(temporary, destination, true);
            }
            catch
            {
                if (_diskProvider.FileExists(destination))
                {
                    _diskProvider.DeleteFile(destination);
                }

                throw;
            }
            finally
            {
                if (File.Exists(temporary))
                {
                    File.Delete(temporary);
                }
            }
        }

        private static string ResolveExistingLinks(string path, bool includeFinalLink, int depth = 0)
        {
            if (depth > 40)
            {
                throw new IOException("Cover path contains too many symbolic links");
            }

            var fullPath = Path.GetFullPath(path);
            var root = Path.GetPathRoot(fullPath);
            var current = root;
            var parts = fullPath.Substring(root.Length).Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries);
            for (var index = 0; index < parts.Length; index++)
            {
                current = Path.Combine(current, parts[index]);
                FileSystemInfo entry = Directory.Exists(current) ? new DirectoryInfo(current) : new FileInfo(current);
                if (entry.Exists && (includeFinalLink || index < parts.Length - 1))
                {
                    var target = entry.ResolveLinkTarget(true);
                    if (target != null)
                    {
                        current = ResolveExistingLinks(target.FullName, true, depth + 1);
                    }
                }
            }

            return Path.GetFullPath(current);
        }

        private static void Initialize(IDiskProvider diskProvider)
        {
            if (_initialized)
            {
                return;
            }

            var directory = Path.Combine(Path.GetTempPath(), "bookshelf-cover-policy-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                if (OsInfo.IsNotWindows)
                {
                    diskProvider.SetPermissions(directory, "700", null);
                }

                var configuration = ConfigurationFiles.Default;
                configuration.Policy.Data = @"<policymap>
  <policy domain=""delegate"" rights=""none"" pattern=""*"" />
  <policy domain=""filter"" rights=""none"" pattern=""*"" />
  <policy domain=""path"" rights=""none"" pattern=""@*"" />
  <policy domain=""module"" rights=""none"" pattern=""*"" />
  <policy domain=""module"" rights=""read|write"" pattern=""{JPEG,PNG,GIF,TIFF,BMP,WEBP}"" />
  <policy domain=""coder"" rights=""none"" pattern=""*"" />
  <policy domain=""coder"" rights=""read|write"" pattern=""{JPEG,JPG,PNG,PNG8,PNG24,PNG32,GIF,GIF87,TIFF,TIF,BMP,BMP2,BMP3,WEBP}"" />
  <policy domain=""resource"" name=""width"" value=""16384"" />
  <policy domain=""resource"" name=""height"" value=""16384"" />
  <policy domain=""resource"" name=""area"" value=""16MP"" />
  <policy domain=""resource"" name=""memory"" value=""256MiB"" />
  <policy domain=""resource"" name=""map"" value=""0"" />
  <policy domain=""resource"" name=""disk"" value=""0"" />
  <policy domain=""resource"" name=""thread"" value=""1"" />
  <policy domain=""resource"" name=""list-length"" value=""64"" />
  <policy domain=""resource"" name=""time"" value=""30"" />
  <policy domain=""system"" name=""max-memory-request"" value=""128MiB"" />
  <policy domain=""system"" name=""max-profile-size"" value=""8MiB"" />
</policymap>";
                Environment.SetEnvironmentVariable("MAGICK_CONFIGURE_PATH", directory);
                MagickNET.Initialize(configuration, directory);
                AppDomain.CurrentDomain.ProcessExit += (sender, args) => CleanupConfiguration(directory);
                _initialized = true;
            }
            catch
            {
                CleanupConfiguration(directory);
                throw;
            }
        }

        private static void CleanupConfiguration(string directory)
        {
            try
            {
                Directory.Delete(directory, true);
            }
            catch
            {
                // Only this process owns the directory. An interrupted cleanup must
                // not stop shutdown or cause a later process to reuse its policy.
            }
        }

        private static MagickFormat GetInputFormat(Stream stream)
        {
            var header = new byte[12];
            var count = stream.Read(header, 0, header.Length);
            stream.Position = 0;
            if (count >= 8 && header[0] == 137 && header[1] == 80 && header[2] == 78 && header[3] == 71 &&
                header[4] == 13 && header[5] == 10 && header[6] == 26 && header[7] == 10)
            {
                return MagickFormat.Png;
            }

            if (count >= 3 && header[0] == 255 && header[1] == 216 && header[2] == 255)
            {
                return MagickFormat.Jpeg;
            }

            if (count >= 6 && header[0] == 'G' && header[1] == 'I' && header[2] == 'F' && header[3] == '8' &&
                (header[4] == '7' || header[4] == '9') && header[5] == 'a')
            {
                return MagickFormat.Gif;
            }

            if (count >= 4 && ((header[0] == 'I' && header[1] == 'I' && header[2] == 42 && header[3] == 0) ||
                (header[0] == 'M' && header[1] == 'M' && header[2] == 0 && header[3] == 42)))
            {
                return MagickFormat.Tiff;
            }

            if (count >= 2 && header[0] == 'B' && header[1] == 'M')
            {
                return MagickFormat.Bmp;
            }

            if (count >= 12 && header[0] == 'R' && header[1] == 'I' && header[2] == 'F' && header[3] == 'F' &&
                header[8] == 'W' && header[9] == 'E' && header[10] == 'B' && header[11] == 'P')
            {
                return MagickFormat.WebP;
            }

            throw InvalidImage();
        }

        private static MagickFormat GetOutputFormat(string destination)
        {
            return Path.GetExtension(destination).ToLowerInvariant() switch
            {
                ".jpg" or ".jpeg" => MagickFormat.Jpeg,
                ".png" => MagickFormat.Png,
                ".gif" => MagickFormat.Gif,
                ".tif" or ".tiff" => MagickFormat.Tiff,
                ".bmp" => MagickFormat.Bmp,
                ".webp" => MagickFormat.WebP,
                _ => throw InvalidImage()
            };
        }

        private static void ValidateFrames(MagickImageCollection images, uint height, MagickFormat format)
        {
            if (images.Count == 0 || images.Count > MaximumFrames)
            {
                throw InvalidImage();
            }

            ulong totalPixels = 0;
            ulong outputPixels = 0;
            ulong canvasWidth = 0;
            ulong canvasHeight = 0;
            foreach (var image in images)
            {
                if (image.Width == 0 || image.Height == 0 || image.Width > MaximumDimension || image.Height > MaximumDimension ||
                    (ulong)image.Width * image.Height > MaximumFramePixels || image.Page.X < 0 || image.Page.Y < 0)
                {
                    throw InvalidImage();
                }

                totalPixels += (ulong)image.Width * image.Height;
                var outputWidth = Math.Max(1UL, (((ulong)image.Width * height) + image.Height - 1) / image.Height);
                if (outputWidth > MaximumDimension || outputWidth * height > MaximumFramePixels)
                {
                    throw InvalidImage();
                }

                outputPixels += outputWidth * height;
                canvasWidth = Math.Max(canvasWidth, Math.Max(image.Page.Width, (ulong)image.Page.X + image.Width));
                canvasHeight = Math.Max(canvasHeight, Math.Max(image.Page.Height, (ulong)image.Page.Y + image.Height));
            }

            if (totalPixels > MaximumTotalPixels || outputPixels > MaximumTotalPixels || canvasWidth > MaximumDimension || canvasHeight > MaximumDimension)
            {
                throw InvalidImage();
            }

            if (images.Count > 1 && (format == MagickFormat.Gif || format == MagickFormat.WebP))
            {
                var outputWidth = Math.Max(1UL, ((canvasWidth * height) + canvasHeight - 1) / canvasHeight);
                if (canvasWidth * canvasHeight > MaximumFramePixels || canvasWidth * canvasHeight * (ulong)images.Count > MaximumTotalPixels ||
                    outputWidth > MaximumDimension || outputWidth * height > MaximumFramePixels || outputWidth * height * (ulong)images.Count > MaximumTotalPixels)
                {
                    throw InvalidImage();
                }
            }
        }

        private static InvalidDataException InvalidImage()
        {
            return new InvalidDataException("Cover image format or resource limits are not supported");
        }
    }
}
