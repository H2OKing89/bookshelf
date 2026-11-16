using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Abstractions;
using System.Text;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;
using UglyToad.PdfPig;

namespace NzbDrone.Core.Test.MediaFiles
{
    [TestFixture]
    public class PdfMetadataFixture : CoreTest<EBookTagService>
    {
        [TestCase("Synthetic Author", "Synthetic Book", false)]
        [TestCase("Author (Editor)\\Team", "A (Book)\\Title\nSecond line", false)]
        [TestCase("著者 — García", "雪の本 📖", true)]
        public void should_read_pdf_author_and_title_without_changing_the_file(string author, string title, bool unicode)
        {
            var information = $"<< /Author {PdfText(author, unicode)} /Title {PdfText(title, unicode)} >>";
            var path = WritePdf("synthetic.PDF", MinimalPdf(information));
            var original = File.ReadAllBytes(path);

            var result = Subject.ReadTags(FileInfo(path));

            result.Authors.Should().Equal(author);
            result.BookTitle.Should().Be(title);
            AssertPdfQuality(result, QualityDetectionSource.TagLib);
            File.ReadAllBytes(path).Should().Equal(original);
            AssertFileReleased(path);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void valid_pdf_without_metadata_should_keep_the_previous_empty_tag_values(bool emptyInformation)
        {
            var path = WritePdf("no-metadata.pdf", MinimalPdf(emptyInformation ? "<< >>" : null));

            var result = Subject.ReadTags(FileInfo(path));

            result.Authors.Should().Equal(string.Empty);
            result.BookTitle.Should().BeEmpty();
            AssertPdfQuality(result, QualityDetectionSource.TagLib);
            AssertFileReleased(path);
        }

        [TestCase(true)]
        [TestCase(false)]
        public void valid_pdf_with_one_metadata_field_should_preserve_the_present_field(bool authorOnly)
        {
            var information = authorOnly ? "<< /Author (Synthetic Author) >>" : "<< /Title (Synthetic Book) >>";
            var path = WritePdf("partial-metadata.pdf", MinimalPdf(information));

            var result = Subject.ReadTags(FileInfo(path));

            result.Authors.Should().Equal(authorOnly ? "Synthetic Author" : string.Empty);
            result.BookTitle.Should().Be(authorOnly ? string.Empty : "Synthetic Book");
            AssertPdfQuality(result, QualityDetectionSource.TagLib);
            AssertFileReleased(path);
        }

        [TestCase("")]
        [TestCase("not a PDF")]
        [TestCase("%PDF-1.4\n1 0 obj\n<< /Type /Catalog >>")]
        public void malformed_or_empty_pdf_should_fall_back_to_extension_and_release_the_file(string contents)
        {
            var path = WritePdf("malformed.pdf", Encoding.ASCII.GetBytes(contents));

            var result = Subject.ReadTags(FileInfo(path));

            result.Authors.Should().BeEmpty();
            result.BookTitle.Should().BeNull();
            AssertPdfQuality(result, QualityDetectionSource.Extension);
            AssertFileReleased(path);
            ExceptionVerification.ExpectedErrors(1);
        }

        [Test]
        public void password_required_pdf_should_fall_back_to_extension_and_release_the_file()
        {
            // A synthetic standard-security PDF with a nonempty user password.
            // It contains one blank page and no external document or media data.
            var encryption = "<< /Filter /Standard /V 1 /R 2 /Length 40 " +
                "/O <f8d1b6c4c82168e373c68ac57dd8498c2d7deb87fe954bd480acefe7a5d4dba1> " +
                "/U <71ee183886b5402827d6d0f6aff5537110949a035e2fd687f9fb67918452df8b> /P -4 >>";
            var path = WritePdf("password-required.pdf", MinimalPdf(null, encryption));
            using (var stream = File.OpenRead(path))
            using (var document = PdfDocument.Open(stream, new ParsingOptions { Password = "synthetic-reader" }))
            {
                document.IsEncrypted.Should().BeTrue();
                document.NumberOfPages.Should().Be(1);
            }

            var result = Subject.ReadTags(FileInfo(path));

            result.Authors.Should().BeEmpty();
            result.BookTitle.Should().BeNull();
            AssertPdfQuality(result, QualityDetectionSource.Extension);
            AssertFileReleased(path);
            ExceptionVerification.ExpectedErrors(1);
        }

        private string WritePdf(string name, byte[] contents)
        {
            var path = Path.Combine(TempFolder, name);
            File.WriteAllBytes(path, contents);
            return path;
        }

        private static IFileInfo FileInfo(string path)
        {
            var file = new Mock<IFileInfo>();
            file.SetupGet(info => info.Extension).Returns(Path.GetExtension(path));
            file.SetupGet(info => info.FullName).Returns(path);
            return file.Object;
        }

        private static void AssertPdfQuality(ParsedTrackInfo result, QualityDetectionSource source)
        {
            result.Quality.Quality.Should().Be(Quality.PDF);
            result.Quality.QualityDetectionSource.Should().Be(source);
        }

        private static void AssertFileReleased(string path)
        {
            using (var exclusive = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                exclusive.CanRead.Should().BeTrue();
            }

            var moved = path + ".moved";
            File.Move(path, moved);
            File.Delete(moved);
            File.Exists(moved).Should().BeFalse();
        }

        private static string PdfText(string value, bool unicode)
        {
            if (unicode)
            {
                return "<FEFF" + Convert.ToHexString(Encoding.BigEndianUnicode.GetBytes(value)) + ">";
            }

            return "(" + value.Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)").Replace("\r", "\\r").Replace("\n", "\\n") + ")";
        }

        private static byte[] MinimalPdf(string information, string encryption = null)
        {
            var objects = new List<string>
            {
                "<< /Type /Catalog /Pages 2 0 R >>",
                "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
                "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 100 100] /Resources << >> >>"
            };
            var trailer = new StringBuilder("/Root 1 0 R");
            if (information != null)
            {
                objects.Add(information);
                trailer.Append($" /Info {objects.Count} 0 R");
            }

            if (encryption != null)
            {
                objects.Add(encryption);
                trailer.Append($" /Encrypt {objects.Count} 0 R /ID [<00112233445566778899aabbccddeeff> <00112233445566778899aabbccddeeff>]");
            }

            // Every object is ASCII, including UTF-16 metadata represented as hex,
            // so character offsets are the byte offsets required by a PDF xref.
            var document = new StringBuilder("%PDF-1.4\n");
            var offsets = new List<int>();
            for (var index = 0; index < objects.Count; index++)
            {
                offsets.Add(document.Length);
                document.Append($"{index + 1} 0 obj\n{objects[index]}\nendobj\n");
            }

            var crossReferenceOffset = document.Length;
            document.Append($"xref\n0 {objects.Count + 1}\n0000000000 65535 f \n");
            foreach (var offset in offsets)
            {
                document.Append(offset.ToString("D10", CultureInfo.InvariantCulture)).Append(" 00000 n \n");
            }

            document.Append($"trailer\n<< /Size {objects.Count + 1} {trailer} >>\nstartxref\n{crossReferenceOffset}\n%%EOF\n");
            return Encoding.ASCII.GetBytes(document.ToString());
        }
    }
}
