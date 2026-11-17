using System.Buffers.Binary;
using System.Data.SQLite;
using System.Diagnostics;
using System.Net;
using NzbDrone.Common.Cache;
using NzbDrone.Common.Http;
using NzbDrone.Core.Books.Calibre;
using System.Globalization;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using ImageMagick;
using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Core.MediaCover;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Qualities;

internal static class Program
{
    private static readonly Dictionary<string, bool> Checks = new();
    private static readonly Dictionary<string, string> Metadata = new();

    private static int Main(string[] args)
    {
        var fixture = Path.GetFullPath(args.Length > 0 ? args[0] : throw new ArgumentException("Own fixture directory required"));
        Directory.CreateDirectory(fixture);
        DiskProxy.PrivateMode(fixture);
        var temporary = Path.Combine(fixture, "tmp");
        Directory.CreateDirectory(temporary);
        DiskProxy.PrivateMode(temporary);
        foreach (var name in new[] { "TMPDIR", "TMP", "TEMP" }) Environment.SetEnvironmentVariable(name, temporary);
        foreach (var name in new[] { "No_Expand", "No_SQLiteXmlConfigFile", "No_PreLoadSQLite", "No_SQLiteFunctions" }) Environment.SetEnvironmentVariable(name, "true");
        try
        {
            NzbDrone.Common.Composition.AssemblyLoader.RegisterSQLiteResolver();
            var applicationDirectory = AppContext.BaseDirectory;
            Check("published_core_loaded_from_delivery_directory", Path.GetDirectoryName(typeof(ImageResizer).Assembly.Location) == applicationDirectory.TrimEnd(Path.DirectorySeparatorChar));
            var disk = DispatchProxy.Create<IDiskProvider, DiskProxy>();
            var resizer = new ImageResizer(disk, null);
            var source = Path.Combine(fixture, "synthetic.png");
            File.WriteAllBytes(source, Png(48, 24));
            var original = Hash(source);
            var output = Path.Combine(fixture, "synthetic-resized.png");
            resizer.Resize(source, output, 12);
            using (var image = new MagickImage(output))
            {
                using var pixels = image.GetPixels();
                Check("published_resizer_preserves_aspect_ratio", image.Width == 24 && image.Height == 12);
                Check("published_resizer_preserves_png_alpha", image.HasAlpha && pixels.GetPixel(0, 0).ToColor().A < byte.MaxValue);
            }
            Check("published_resizer_preserves_source_bytes", original.SequenceEqual(Hash(source)));
            var notices = File.ReadAllText(Path.Combine(applicationDirectory, "licenses", "managed", "Magick.NET", "NOTICE.txt"));
            var nativeVersion = Regex.Match(notices, @"\[ ImageMagick (?<version>[0-9][^\s]+)");
            Metadata["imageMagickVersion"] = MagickNET.ImageMagickVersion;
            Metadata["runtimeVersion"] = Environment.Version.ToString();
            Metadata["calibreVerification"] = "production client with a synthetic HTTP response";
            Check("native_magick_version_matches_distributed_notice", nativeVersion.Success && MagickNET.ImageMagickVersion.Contains(nativeVersion.Groups["version"].Value, StringComparison.Ordinal));
            Check("managed_magick_loaded_from_delivery_directory", Path.GetDirectoryName(typeof(MagickImage).Assembly.Location) == applicationDirectory.TrimEnd(Path.DirectorySeparatorChar));
            foreach (var (extension, format) in new[] { ("jpg", MagickFormat.Jpeg), ("png", MagickFormat.Png), ("gif", MagickFormat.Gif), ("tiff", MagickFormat.Tiff), ("bmp", MagickFormat.Bmp), ("webp", MagickFormat.WebP) })
            {
                var input = Path.Combine(fixture, "format." + extension);
                var resized = Path.Combine(fixture, "format-resized." + extension);
                using (var image = new MagickImage(source)) image.Write(input, format);
                resizer.Resize(input, resized, 6);
                using var decoded = new MagickImage(resized);
                Check("published_raster_" + extension + "_works", decoded.Width == 12 && decoded.Height == 6);
            }

            var animated = Path.Combine(fixture, "animated.gif");
            using (var images = new MagickImageCollection())
            {
                var first = new MagickImage(source) { AnimationDelay = 10, AnimationIterations = 3 };
                images.Add(first);
                var second = new MagickImage(Png(48, 24, 255, 0, 0, 255)) { AnimationDelay = 20, AnimationIterations = 3 };
                images.Add(second);
                images.Write(animated, MagickFormat.Gif);
            }
            var animatedHash = Hash(animated);
            var animatedOutput = Path.Combine(fixture, "animated-resized.gif");
            resizer.Resize(animated, animatedOutput, 6);
            using (var images = new MagickImageCollection(animatedOutput))
            {
                Check("published_gif_preserves_animation", images.Count == 2 && images.All(frame => frame.Width == 12 && frame.Height == 6) && images[0].AnimationDelay == 10 && images[1].AnimationDelay == 20 && images[0].AnimationIterations == 3);
            }
            Check("published_gif_preserves_source_bytes", animatedHash.SequenceEqual(Hash(animated)));

            var disguised = Path.Combine(fixture, "disguised.png");
            File.WriteAllText(disguised, "<svg xmlns='http://www.w3.org/2000/svg'><rect width='10' height='10'/></svg>");
            var rejected = Path.Combine(fixture, "rejected.png");
            var threw = false;
            try { resizer.Resize(disguised, rejected, 10); } catch { threw = true; }
            Check("disguised_vector_is_rejected_without_output", threw && !File.Exists(rejected));
            threw = false;
            try { resizer.Resize(source, source, 10); } catch { threw = true; }
            Check("same_path_request_preserves_original", threw && original.SequenceEqual(Hash(source)));

            var pdf = Path.Combine(fixture, "synthetic.pdf");
            File.WriteAllBytes(pdf, Pdf());
            var pdfHash = Hash(pdf);
            var tags = new EBookTagService(null, null, null, null, null, LogManager.GetLogger("LocalPackageProbe"));
            var readPdf = typeof(EBookTagService).GetMethod("ReadPdf", BindingFlags.Instance | BindingFlags.NonPublic);
            var metadata = (ParsedTrackInfo)readPdf.Invoke(tags, new object[] { pdf });
            var pdfAssembly = AppDomain.CurrentDomain.GetAssemblies().Single(assembly => assembly.GetName().Name == "UglyToad.PdfPig");
            Check("pdf_parser_loaded_from_delivery_directory", Path.GetDirectoryName(pdfAssembly.Location) == applicationDirectory.TrimEnd(Path.DirectorySeparatorChar));
            Check("published_pdf_adapter_reads_author_title", metadata.Authors.SequenceEqual(new[] { "Synthetic Author" }) && metadata.BookTitle == "Synthetic Book");
            Check("published_pdf_adapter_keeps_quality_provenance", metadata.Quality.Quality == Quality.PDF && metadata.Quality.QualityDetectionSource == QualityDetectionSource.TagLib);
            Check("published_pdf_adapter_preserves_source_bytes", pdfHash.SequenceEqual(Hash(pdf)));
            using (var exclusive = new FileStream(pdf, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) Check("published_pdf_adapter_releases_source", exclusive.CanRead);
            var malformed = Path.Combine(fixture, "malformed.pdf");
            File.WriteAllText(malformed, "not a PDF");
            var fallback = (ParsedTrackInfo)readPdf.Invoke(tags, new object[] { malformed });
            Check("published_pdf_adapter_keeps_invalid_file_fallback", fallback.Quality.Quality == Quality.PDF && fallback.Quality.QualityDetectionSource == QualityDetectionSource.Extension);

            var wave = Path.Combine(fixture, "synthetic-tags.wav");
            File.WriteAllBytes(wave, Wave());
            using (var tagged = TagLib.File.Create(wave))
            {
                tagged.Tag.Title = "Synthetic Tagged Recording";
                tagged.Tag.Album = "Synthetic Audio Book";
                tagged.Tag.AlbumArtists = new[] { "Synthetic Audio Author" };
                tagged.Tag.Performers = new[] { "Synthetic Audio Author" };
                tagged.Tag.Composers = new[] { "Synthetic Audio Narrator" };
                tagged.Save();
            }
            var waveHash = Hash(wave);
            var audio = new AudioTag(wave);
            Check("published_taglib_reads_recording_tags", audio.IsValid && audio.Title == "Synthetic Tagged Recording" && audio.Book == "Synthetic Audio Book");
            Check("published_taglib_keeps_author_and_narrator", audio.BookAuthors.Contains("Synthetic Audio Author") && audio.Narrators.Contains("Synthetic Audio Narrator"));
            Check("published_taglib_reads_audio_properties", audio.MediaInfo.AudioSampleRate == 8000 && audio.MediaInfo.AudioChannels == 1);
            Check("published_audio_tag_read_preserves_source_bytes", waveHash.SequenceEqual(Hash(wave)));
            using (var exclusive = new FileStream(wave, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) Check("published_taglib_releases_source", exclusive.CanRead);

            var epub = Path.Combine(fixture, "synthetic.epub");
            Epub(epub);
            var epubHash = Hash(epub);
            var readEpub = typeof(EBookTagService).GetMethod("ReadEpub", BindingFlags.Instance | BindingFlags.NonPublic);
            var epubMetadata = (ParsedTrackInfo)readEpub.Invoke(tags, new object[] { epub });
            Check("published_epub_adapter_reads_author_title", epubMetadata.BookTitle == "Synthetic EPUB Book" && epubMetadata.Authors.SequenceEqual(new[] { "Synthetic EPUB Author" }));
            Check("published_epub_adapter_keeps_provenance_and_identifiers", epubMetadata.Quality.Quality == Quality.EPUB && epubMetadata.Quality.QualityDetectionSource == QualityDetectionSource.TagLib && epubMetadata.Isbn == "9781234567897");
            Check("published_epub_adapter_preserves_source_bytes", epubHash.SequenceEqual(Hash(epub)));
            using (var exclusive = new FileStream(epub, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) Check("published_epub_adapter_releases_source", exclusive.CanRead);

            var calibreHttp = DispatchProxy.Create<IHttpClient, CalibreHttpProxy>();
            var calibre = new CalibreProxy(calibreHttp, null, null, null, null, null, new CacheManager(), LogManager.GetLogger("LocalCalibreProbe"));
            var calibreBook = calibre.GetBook(321, new CalibreSettings { Host = "127.0.0.1", Port = 12345, UrlBase = "/calibre", Library = "Synthetic" });
            Check("published_calibre_request_contract_is_preserved", CalibreHttpProxy.RequestUrl.EndsWith("/calibre/ajax/book/321/Synthetic", StringComparison.Ordinal));
            Check("published_calibre_response_deserializes", calibreBook.Id == 321 && calibreBook.Title == "Synthetic Calibre Book" && calibreBook.Authors.SequenceEqual(new[] { "Synthetic Calibre Author" }));

            {
                var tool = Path.Combine(applicationDirectory, "tools", "mediainfo", "mediainfo");
                var start = new ProcessStartInfo(tool) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
                start.ArgumentList.Add("--Output=JSON");
                start.ArgumentList.Add(wave);
                using var child = Process.Start(start);
                var outputRead = child.StandardOutput.ReadToEndAsync();
                var errorRead = child.StandardError.ReadToEndAsync();
                if (!child.WaitForExit(30000))
                {
                    child.Kill(entireProcessTree: true);
                    child.WaitForExit(5000);
                    throw new TimeoutException("Bundled MediaInfo exceeded its probe deadline");
                }
                if (!Task.WaitAll(new Task[] { outputRead, errorRead }, TimeSpan.FromSeconds(5)))
                {
                    throw new TimeoutException("Bundled MediaInfo output did not close");
                }
                Check("bundled_mediainfo_child_exits_successfully", child.ExitCode == 0);
                using var jsonInfo = JsonDocument.Parse(outputRead.Result);
                var expectedVersion = Regex.Match(File.ReadAllText(Path.Combine(applicationDirectory, "tools", "mediainfo", "VERSION")), @"^MediaInfo CLI (?<version>\S+)", RegexOptions.Multiline);
                var actualVersion = jsonInfo.RootElement.GetProperty("creatingLibrary").GetProperty("version").GetString();
                Metadata["mediaInfoVersion"] = actualVersion;
                Check("bundled_mediainfo_version_matches_bundle", expectedVersion.Success && actualVersion == expectedVersion.Groups["version"].Value);
                var tracks = jsonInfo.RootElement.GetProperty("media").GetProperty("track").EnumerateArray();
                Check("bundled_mediainfo_reads_synthetic_pcm", tracks.Any(track => track.GetProperty("@type").GetString() == "Audio" && track.GetProperty("Format").GetString() == "PCM"));
            }

            var databaseConnection = new SQLiteConnectionStringBuilder
            {
                DataSource = Path.Combine(fixture, "native.sqlite"),
                Version = 3,
                Pooling = false
            };
            using (var database = new SQLiteConnection(databaseConnection.ConnectionString))
            {
                database.Open();
                using var command = database.CreateCommand();
                command.CommandText = "CREATE TABLE OwnedFixture (Id INTEGER PRIMARY KEY); INSERT INTO OwnedFixture VALUES (1)";
                command.ExecuteNonQuery();
                command.CommandText = "SELECT COUNT(*) FROM OwnedFixture";
                Check("delivery_sqlite_native_can_create_write_read", Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture) == 1);
                command.CommandText = "PRAGMA integrity_check(1)";
                Check("delivery_sqlite_integrity_ok", (string)command.ExecuteScalar() == "ok");
            }
        }
        catch (Exception exception)
        {
            // Only type names are recorded, never parser messages or file contents.
            Checks["execution_completed_" + exception.GetType().Name] = false;
        }

        var result = new { checks = Checks, metadata = Metadata, passed = Checks.Values.Count(value => value), failed = Checks.Values.Count(value => !value) };
        var json = JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(Path.Combine(fixture, "native-probe-result.json"), json + "\n");
        Console.WriteLine(json);
        return result.failed == 0 ? 0 : 1;
    }

    private static void Check(string name, bool value) => Checks[name] = value;
    private static byte[] Hash(string path) => SHA256.HashData(File.ReadAllBytes(path));

    private static byte[] Wave()
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.ASCII, true);
        writer.Write(Encoding.ASCII.GetBytes("RIFF")); writer.Write(16036);
        writer.Write(Encoding.ASCII.GetBytes("WAVEfmt ")); writer.Write(16);
        writer.Write((ushort)1); writer.Write((ushort)1); writer.Write(8000); writer.Write(16000);
        writer.Write((ushort)2); writer.Write((ushort)16); writer.Write(Encoding.ASCII.GetBytes("data")); writer.Write(16000); writer.Write(new byte[16000]);
        return stream.ToArray();
    }

    private static void Epub(string file)
    {
        using var output = File.Create(file);
        using var archive = new ZipArchive(output, ZipArchiveMode.Create);
        void Add(string path, string content)
        {
            var entry = archive.CreateEntry(path);
            using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false)); writer.Write(content);
        }
        Add("mimetype", "application/epub+zip");
        Add("META-INF/container.xml", "<?xml version='1.0'?><container version='1.0' xmlns='urn:oasis:names:tc:opendocument:xmlns:container'><rootfiles><rootfile full-path='OEBPS/content.opf' media-type='application/oebps-package+xml'/></rootfiles></container>");
        Add("OEBPS/content.opf", "<?xml version='1.0'?><package version='2.0' unique-identifier='bookid' xmlns='http://www.idpf.org/2007/opf'><metadata xmlns:dc='http://purl.org/dc/elements/1.1/' xmlns:opf='http://www.idpf.org/2007/opf'><dc:identifier id='bookid' opf:scheme='ISBN'>9781234567897</dc:identifier><dc:title>Synthetic EPUB Book</dc:title><dc:creator>Synthetic EPUB Author</dc:creator><dc:language>en</dc:language></metadata><manifest><item id='chapter' href='chapter.xhtml' media-type='application/xhtml+xml'/><item id='ncx' href='toc.ncx' media-type='application/x-dtbncx+xml'/></manifest><spine toc='ncx'><itemref idref='chapter'/></spine></package>");
        Add("OEBPS/chapter.xhtml", "<html xmlns='http://www.w3.org/1999/xhtml'><head><title>Synthetic Chapter</title></head><body><p>Owned synthetic content.</p></body></html>");
        Add("OEBPS/toc.ncx", "<?xml version='1.0'?><ncx xmlns='http://www.daisy.org/z3986/2005/ncx/' version='2005-1'><head><meta name='dtb:uid' content='9781234567897'/></head><docTitle><text>Synthetic EPUB Book</text></docTitle><navMap><navPoint id='chapter' playOrder='1'><navLabel><text>Chapter</text></navLabel><content src='chapter.xhtml'/></navPoint></navMap></ncx>");
    }

    private static byte[] Png(int width, int height, byte red = 32, byte green = 100, byte blue = 180, byte alpha = 128)
    {
        using var output = new MemoryStream();
        output.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        var header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(0, 4), width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4, 4), height);
        header[8] = 8;
        header[9] = 6;
        Chunk(output, "IHDR", header);
        using var raw = new MemoryStream();
        using (var compressed = new ZLibStream(raw, CompressionLevel.SmallestSize, true))
        {
            for (var y = 0; y < height; y++)
            {
                compressed.WriteByte(0);
                for (var x = 0; x < width; x++) compressed.Write(new byte[] { red, green, blue, alpha });
            }
        }
        Chunk(output, "IDAT", raw.ToArray());
        Chunk(output, "IEND", Array.Empty<byte>());
        return output.ToArray();
    }

    private static void Chunk(Stream stream, string type, byte[] data)
    {
        var bytes = Encoding.ASCII.GetBytes(type).Concat(data).ToArray();
        var number = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(number, data.Length);
        stream.Write(number);
        stream.Write(bytes);
        uint crc = uint.MaxValue;
        foreach (var value in bytes)
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++) crc = (crc >> 1) ^ ((crc & 1) == 0 ? 0 : 0xedb88320u);
        }
        BinaryPrimitives.WriteUInt32BigEndian(number, ~crc);
        stream.Write(number);
    }

    private static byte[] Pdf()
    {
        var objects = new[] { "<< /Type /Catalog /Pages 2 0 R >>", "<< /Type /Pages /Kids [3 0 R] /Count 1 >>", "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 100 100] /Resources << >> >>", "<< /Author (Synthetic Author) /Title (Synthetic Book) >>" };
        var output = new StringBuilder("%PDF-1.4\n");
        var offsets = new List<int>();
        for (var index = 0; index < objects.Length; index++)
        {
            offsets.Add(output.Length);
            output.Append($"{index + 1} 0 obj\n{objects[index]}\nendobj\n");
        }
        var xref = output.Length;
        output.Append("xref\n0 5\n0000000000 65535 f \n");
        foreach (var offset in offsets) output.Append(offset.ToString("D10", CultureInfo.InvariantCulture)).Append(" 00000 n \n");
        output.Append($"trailer\n<< /Size 5 /Root 1 0 R /Info 4 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        return Encoding.ASCII.GetBytes(output.ToString());
    }
}

public class DiskProxy : DispatchProxy
{
    [DllImport("libc", EntryPoint = "chmod", SetLastError = true)]
    private static extern int Chmod(string path, uint mode);

    public static void PrivateMode(string path)
    {
        if (!OperatingSystem.IsWindows() && Chmod(path, 448) != 0) throw new IOException("Private directory mode could not be set");
    }

    protected override object Invoke(MethodInfo method, object[] args)
    {
        switch (method.Name)
        {
            case "FileExists": return File.Exists((string)args[0]);
            case "FolderExists": return Directory.Exists((string)args[0]);
            case "GetFileSize": return new FileInfo((string)args[0]).Length;
            case "CreateFolder":
            case "EnsureFolder": Directory.CreateDirectory((string)args[0]); return null;
            case "SetPermissions":
            case "SetFilePermissions": PrivateMode((string)args[0]); return null;
            case "OpenReadStream": return File.OpenRead((string)args[0]);
            case "OpenWriteStream": return new FileStream((string)args[0], FileMode.Create, FileAccess.Write, FileShare.None);
            case "MoveFile": File.Move((string)args[0], (string)args[1], args.Length > 2 && (bool)args[2]); return null;
            case "CopyFile": File.Copy((string)args[0], (string)args[1], args.Length > 2 && (bool)args[2]); return null;
            case "DeleteFile": File.Delete((string)args[0]); return null;
            case "DeleteFolder": if (Directory.Exists((string)args[0])) Directory.Delete((string)args[0], (bool)args[1]); return null;
            case "GetFiles": return Directory.GetFiles((string)args[0], "*", (bool)args[1] ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly);
            case "ReadAllText": return File.ReadAllText((string)args[0]);
            case "WriteAllText": File.WriteAllText((string)args[0], (string)args[1]); return null;
            default: throw new NotSupportedException("Unexpected probe disk method: " + method.Name);
        }
    }
}

public class CalibreHttpProxy : DispatchProxy
{
    public static string RequestUrl { get; private set; }
    protected override object Invoke(MethodInfo method, object[] args)
    {
        if (method.Name != "Get") throw new NotSupportedException("Unexpected synthetic Calibre request");
        var request = (HttpRequest)args[0];
        RequestUrl = request.Url.FullUri;
        var json = "{\"application_id\":321,\"title\":\"Synthetic Calibre Book\",\"authors\":[\"Synthetic Calibre Author\"],\"format_metadata\":{}}";
        var response = new HttpResponse(request, new HttpHeader(), json, HttpStatusCode.OK);
        return Activator.CreateInstance(method.ReturnType, response);
    }
}
