using System;
using System.Collections.Generic;
using System.IO;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Core.Books;
using NzbDrone.Core.Organizer;
using NzbDrone.Core.RootFolders;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.OrganizerTests
{
    [TestFixture]
    public class AuthorPathBuilderFixture : CoreTest<AuthorPathBuilder>
    {
        private Author _author;
        private string _root;

        [SetUp]
        public void Setup()
        {
            _root = Path.Combine(Path.GetTempPath(), "audiobooks");
            _author = new Author { Name = "J. R. Saileri", RootFolderPath = _root };

            Mocker.GetMock<IBuildFileNames>()
                .Setup(x => x.GetAuthorFolder(It.IsAny<Author>(), null))
                .Returns<Author, NamingConfig>((author, _) => author.Name);

            Mocker.GetMock<IDiskProvider>().Setup(x => x.FolderExists(_root)).Returns(true);
            Mocker.GetMock<IAuthorRepository>().Setup(x => x.AllAuthorPaths()).Returns(new Dictionary<int, string>());
            GivenFolders();
        }

        private void GivenFolders(params string[] names)
        {
            var paths = new List<string>();
            foreach (var name in names)
            {
                paths.Add(Path.Combine(_root, name));
            }

            Mocker.GetMock<IDiskProvider>().Setup(x => x.GetDirectories(_root)).Returns(paths);
        }

        [TestCase("J. R. Saileri", "J.R. Saileri")]
        [TestCase("D.S.Craig", "D.S. Craig")]
        [TestCase("NISIOISIN", "Nisio Isin")]
        [TestCase("pirateaba", "PirateAba")]
        public void should_reuse_unique_existing_name_variant(string name, string folder)
        {
            _author.Name = name;
            GivenFolders("Other Author", folder);

            Subject.BuildPath(_author, false).Should().Be(Path.Combine(_root, folder));
        }

        [Test]
        public void should_reuse_a_unique_stored_alias_folder()
        {
            _author.Name = "西尾維新";
            _author.Metadata.Value.Aliases.Add("Nisio Isin");
            GivenFolders("Nisio Isin");

            Subject.BuildPath(_author, false).Should().Be(Path.Combine(_root, "Nisio Isin"));
        }

        [Test]
        public void should_not_select_a_variant_folder_claimed_by_another_author()
        {
            var existingPath = Path.Combine(_root, "J.R. Saileri");
            GivenFolders("J.R. Saileri");
            Mocker.GetMock<IAuthorRepository>().Setup(x => x.AllAuthorPaths())
                .Returns(new Dictionary<int, string> { { 99, existingPath } });

            Subject.BuildPath(_author, false).Should().Be(Path.Combine(_root, _author.Name));

            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void should_allow_a_variant_folder_already_claimed_by_the_same_author()
        {
            _author.Id = 99;
            var existingPath = Path.Combine(_root, "J.R. Saileri");
            GivenFolders("J.R. Saileri");
            Mocker.GetMock<IAuthorRepository>().Setup(x => x.AllAuthorPaths())
                .Returns(new Dictionary<int, string> { { 99, existingPath } });

            Subject.BuildPath(_author, false).Should().Be(existingPath);
        }

        [Test]
        public void should_keep_the_exact_existing_generated_folder()
        {
            var expected = Path.Combine(_root, _author.Name);
            Mocker.GetMock<IDiskProvider>().Setup(x => x.FolderExists(expected)).Returns(true);
            GivenFolders(_author.Name, "J.R. Saileri");

            Subject.BuildPath(_author, false).Should().Be(expected);
            Mocker.GetMock<IDiskProvider>().Verify(x => x.GetDirectories(It.IsAny<string>()), Times.Never());
        }

        [Test]
        public void should_not_guess_between_multiple_existing_variants()
        {
            GivenFolders("J.R. Saileri", "J R Saileri");

            Subject.BuildPath(_author, false).Should().Be(Path.Combine(_root, _author.Name));

            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void should_consider_alias_and_canonical_matches_together_for_ambiguity()
        {
            _author.Metadata.Value.Aliases.Add("Another Name");
            GivenFolders("J.R. Saileri", "Another Name");

            Subject.BuildPath(_author, false).Should().Be(Path.Combine(_root, _author.Name));

            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void should_not_change_custom_folder_naming_to_a_bare_author_name()
        {
            Mocker.GetMock<IBuildFileNames>()
                .Setup(x => x.GetAuthorFolder(_author, null)).Returns("J. R. Saileri (audiobooks)");
            GivenFolders("J.R. Saileri");

            Subject.BuildPath(_author, false).Should().Be(Path.Combine(_root, "J. R. Saileri (audiobooks)"));
        }

        [Test]
        public void should_preserve_existing_relative_custom_path()
        {
            _author.Path = Path.Combine(_root, "Custom", "Chosen folder");
            Mocker.GetMock<IRootFolderService>().Setup(x => x.GetBestRootFolderPath(_author.Path)).Returns(_root);
            _author.RootFolderPath = Path.Combine(Path.GetTempPath(), "new-audiobooks");

            Subject.BuildPath(_author, true)
                .Should().Be(Path.Combine(_author.RootFolderPath, "Custom", "Chosen folder"));

            Mocker.GetMock<IDiskProvider>().Verify(x => x.FolderExists(It.IsAny<string>()), Times.Never());
        }

        [Test]
        public void should_preserve_parent_components_of_custom_folder_formats()
        {
            var parent = Path.Combine(_root, "J");
            Mocker.GetMock<IBuildFileNames>()
                .Setup(x => x.GetAuthorFolder(_author, null)).Returns(Path.Combine("J", _author.Name));
            Mocker.GetMock<IDiskProvider>().Setup(x => x.FolderExists(parent)).Returns(true);
            Mocker.GetMock<IDiskProvider>().Setup(x => x.GetDirectories(parent))
                .Returns(new[] { Path.Combine(parent, "J.R. Saileri") });

            Subject.BuildPath(_author, false).Should().Be(Path.Combine(parent, "J.R. Saileri"));
        }

        [Test]
        public void should_keep_generated_path_when_no_existing_folder_matches()
        {
            GivenFolders("J.R. Saileri Jr", "Other Author");

            Subject.BuildPath(_author, false).Should().Be(Path.Combine(_root, _author.Name));
        }

        [Test]
        public void should_not_equate_different_accented_names()
        {
            _author.Name = "José Author";
            GivenFolders("Jose Author");

            Subject.BuildPath(_author, false).Should().Be(Path.Combine(_root, _author.Name));
        }

        [Test]
        public void should_keep_generated_path_when_root_is_unavailable()
        {
            Mocker.GetMock<IDiskProvider>().Setup(x => x.FolderExists(_root)).Returns(false);

            Subject.BuildPath(_author, false).Should().Be(Path.Combine(_root, _author.Name));
            Mocker.GetMock<IDiskProvider>().Verify(x => x.GetDirectories(It.IsAny<string>()), Times.Never());
        }

        [Test]
        public void should_keep_generated_path_when_directory_listing_is_denied()
        {
            Mocker.GetMock<IDiskProvider>().Setup(x => x.GetDirectories(_root)).Throws<UnauthorizedAccessException>();

            Subject.BuildPath(_author, false).Should().Be(Path.Combine(_root, _author.Name));

            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void should_require_a_root_folder()
        {
            _author.RootFolderPath = null;

            Assert.Throws<ArgumentException>(() => Subject.BuildPath(_author, false));
        }
    }
}
