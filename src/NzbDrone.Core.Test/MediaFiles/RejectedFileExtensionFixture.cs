using System;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.DecisionEngine;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.BookImport;
using NzbDrone.Core.MediaFiles.BookImport.Specifications;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.Test.MediaFiles
{
    [TestFixture]
    public class RejectedFileExtensionFixture
    {
        private Mock<IConfigService> _config;
        private RejectedFileExtensionService _service;

        [SetUp]
        public void Setup()
        {
            _config = new Mock<IConfigService>();
            _service = new RejectedFileExtensionService(_config.Object);
        }

        [TestCase("book.M4B")]
        [TestCase("book.m4a")]
        [TestCase("book.OPUS")]
        [TestCase("book.EPUB")]
        [TestCase("book.kepub")]
        [TestCase("book.kepub.epub")]
        [TestCase("book.mobi")]
        [TestCase("book.azw3")]
        [TestCase("book.PDF")]
        public void audio_and_ebook_containers_should_be_accepted(string path)
        {
            _service.Classify(path).Should().Be(DownloadFileKind.Media);
            _service.GetRejection(path).Should().BeNull();
        }

        [TestCase("book.CUE")]
        [TestCase("book.m3u")]
        [TestCase("book.m3u8")]
        [TestCase("book.NFO")]
        [TestCase("cover.JPG")]
        [TestCase("cover.png")]
        [TestCase("checksums.sha256")]
        [TestCase("description.txt")]
        [TestCase("metadata.OPF")]
        [TestCase("metadata.json")]
        [TestCase("book.par2")]
        public void supporting_files_should_not_be_classified_as_dangerous(string path)
        {
            _service.Classify(path).Should().Be(DownloadFileKind.Sidecar);
            _service.GetRejection(path).Should().BeNull();
            _service.GetRejection(new[] { "book.m4b", path }).Should().BeNull();
        }

        [TestCase("book.EXE", RejectionCode.ExecutableFile)]
        [TestCase("folder\\install.PS1", RejectionCode.DangerousFile)]
        [TestCase("book.zip", RejectionCode.ArchiveNotExtracted)]
        [TestCase("book.tar.gz", RejectionCode.ArchiveNotExtracted)]
        [TestCase("book.r05", RejectionCode.ArchiveNotExtracted)]
        [TestCase("book.7z.001", RejectionCode.ArchiveNotExtracted)]
        [TestCase("book.doc", RejectionCode.UnsupportedFileExtension)]
        [TestCase("book", RejectionCode.UnsupportedFileExtension)]
        public void nonmedia_should_have_specific_codes(string path, string code)
        {
            _service.GetRejection(path).Code.Should().Be(code);
        }

        [Test]
        public void archive_should_be_temporary_while_executable_is_permanent()
        {
            _service.GetRejection("book.zip").Type.Should().Be(RejectionType.Temporary);
            _service.GetRejection("book.exe").Type.Should().Be(RejectionType.Permanent);
        }

        [Test]
        public void valid_audio_should_not_be_blocked_by_an_incidental_unknown_or_archive_file()
        {
            _service.GetRejection(new[] { "book.m4b", "extra.doc", "source.zip" }).Should().BeNull();
        }

        [Test]
        public void dangerous_file_should_take_priority_over_executables_archives_and_media()
        {
            _service.GetRejection(new[] { "book.m4b", "source.zip", "setup.exe", "shortcut.lnk" }).Code.Should().Be(RejectionCode.DangerousFile);
        }

        [Test]
        public void archive_only_batch_should_explain_extraction_instead_of_an_unknown_sidecar()
        {
            _service.GetRejection(new[] { "readme.txt", "source.zip", "extra.doc" }).Code.Should().Be(RejectionCode.ArchiveNotExtracted);
        }

        [TestCase("m4b", "book.M4B")]
        [TestCase(".M4B, mp3", "book.m4b")]
        [TestCase("tar.gz", "folder/book.tar.gz")]
        [TestCase(".CUE", "book.cue")]
        public void explicit_user_policy_should_match_case_insensitive_suffixes(string policy, string path)
        {
            _config.SetupGet(config => config.UserRejectedExtensions).Returns(policy);

            _service.Classify(path).Should().Be(DownloadFileKind.UserRejected);
            _service.GetRejection(path).Code.Should().Be(RejectionCode.UserRejectedExtension);
        }

        [Test]
        public void media_excluded_by_user_should_not_count_as_accepted_media_in_a_batch()
        {
            _config.SetupGet(config => config.UserRejectedExtensions).Returns("mp3");

            _service.GetRejection(new[] { "book.mp3", "cover.jpg" }).Code.Should().Be(RejectionCode.UserRejectedExtension);
        }

        [Test]
        public void clearing_policy_should_accept_previously_excluded_media()
        {
            _config.SetupGet(config => config.UserRejectedExtensions).Returns("mp3");
            _service.GetRejection("book.mp3").Should().NotBeNull();
            _config.SetupGet(config => config.UserRejectedExtensions).Returns(string.Empty);

            _service.GetRejection("book.mp3").Should().BeNull();
        }

        [Test]
        public void import_specification_should_preserve_existing_file_rescans()
        {
            _config.SetupGet(config => config.UserRejectedExtensions).Returns("mp3");
            var specification = new RejectedFileExtensionSpecification(_service);

            specification.IsSatisfiedBy(new LocalBook { Path = "book.mp3", ExistingFile = true }, null).Accepted.Should().BeTrue();
            specification.IsSatisfiedBy(new LocalBook { Path = "book.mp3" }, null).Code.Should().Be(RejectionCode.UserRejectedExtension);
            specification.IsSatisfiedBy(new LocalBook { Path = "book.cue" }, null).Code.Should().Be(RejectionCode.UnsupportedFileExtension);
        }

        [TestCase(" .MP3, m4b, .mp3, TAR.GZ ", "mp3, m4b, tar.gz")]
        [TestCase("", "")]
        [TestCase(null, "")]
        [TestCase(" , , ", "")]
        public void policy_should_be_normalized_without_duplicating_extensions(string input, string expected)
        {
            RejectedFileExtensions.Normalize(input).Should().Be(expected);
        }

        [TestCase("*.mp3")]
        [TestCase("../mp3")]
        [TestCase("folder/mp3")]
        [TestCase("mp3;m4b")]
        [TestCase("..mp3")]
        [TestCase("m p3")]
        public void policy_should_reject_paths_globs_and_malformed_tokens(string input)
        {
            RejectedFileExtensions.IsValid(input).Should().BeFalse();
            Assert.Throws<ArgumentException>(() => RejectedFileExtensions.Normalize(input));
        }
    }
}
