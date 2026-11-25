using System.Collections.Generic;
using System.Linq;
using FizzWare.NBuilder;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.CustomFormats;
using NzbDrone.Core.ImportLists;
using NzbDrone.Core.Lifecycle;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Profiles.Qualities;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.RootFolders;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Profiles
{
    [TestFixture]

    public class QualityProfileServiceFixture : CoreTest<QualityProfileService>
    {
        [Test]
        public void init_should_add_default_audiobook_profile()
        {
            QualityProfile added = null;
            Mocker.GetMock<IProfileRepository>()
                .Setup(repository => repository.Insert(It.IsAny<QualityProfile>()))
                .Callback<QualityProfile>(profile => added = profile);

            Mocker.GetMock<ICustomFormatService>()
                .Setup(s => s.All())
                .Returns(new List<CustomFormat>());

            Subject.Handle(new ApplicationStartedEvent());

            Mocker.GetMock<IProfileRepository>()
                .Verify(v => v.Insert(It.IsAny<QualityProfile>()), Times.Once());

            added.Should().NotBeNull();
            added.Name.Should().Be("Audiobooks");
            added.Cutoff.Should().Be(Quality.MP3.Id);
            added.MinUpgradeFormatScore.Should().Be(0);
            added.Items.Where(item => item.Allowed).SelectMany(item => item.GetQualities())
                .Should().BeEquivalentTo(new[] { Quality.UnknownAudio, Quality.MP3, Quality.M4B, Quality.FLAC });
        }

        [TestCase(9, 10, 1)]
        [TestCase(10, 10, 0)]
        [TestCase(10, 0, 1)]
        public void refreshes_pending_decisions_only_when_the_saved_minimum_changes(int previous, int updated, int publishes)
        {
            var saved = false;
            var profile = new QualityProfile { Id = 2, MinUpgradeFormatScore = updated };
            Mocker.GetMock<IProfileRepository>().Setup(repository => repository.Get(profile.Id))
                .Returns(new QualityProfile { Id = profile.Id, MinUpgradeFormatScore = previous });
            Mocker.GetMock<IProfileRepository>().Setup(repository => repository.Update(profile)).Callback(() => saved = true);
            Mocker.GetMock<IEventAggregator>().Setup(events => events.PublishEvent(It.IsAny<MinimumUpgradeFormatScoreChangedEvent>()))
                .Callback<MinimumUpgradeFormatScoreChangedEvent>(_ => saved.Should().BeTrue());

            Subject.Update(profile);

            Mocker.GetMock<IEventAggregator>().Verify(events => events.PublishEvent(It.Is<MinimumUpgradeFormatScoreChangedEvent>(change =>
                change.QualityProfileId == profile.Id)), Times.Exactly(publishes));
        }

        [Test]

        //This confirms that new profiles are added only if no other profiles exists.
        //We don't want to keep adding them back if a user deleted them on purpose.
        public void Init_should_skip_if_any_profiles_already_exist()
        {
            Mocker.GetMock<IProfileRepository>()
                  .Setup(s => s.All())
                  .Returns(Builder<QualityProfile>.CreateListOfSize(2).Build().ToList());

            Subject.Handle(new ApplicationStartedEvent());

            Mocker.GetMock<IProfileRepository>()
                .Verify(v => v.Insert(It.IsAny<QualityProfile>()), Times.Never());
        }

        [Test]
        public void should_not_be_able_to_delete_profile_if_assigned_to_author()
        {
            var profile = Builder<QualityProfile>.CreateNew()
                                          .With(p => p.Id = 2)
                                          .Build();

            var authorList = Builder<Author>.CreateListOfSize(3)
                                            .Random(1)
                                            .With(c => c.QualityProfileId = profile.Id)
                                            .Build().ToList();

            var importLists = Builder<ImportListDefinition>.CreateListOfSize(2)
                .All()
                .With(c => c.ProfileId = 1)
                .Build().ToList();

            var rootFolders = Builder<RootFolder>.CreateListOfSize(2)
                .All()
                .With(f => f.DefaultQualityProfileId = 1)
                .BuildList();

            Mocker.GetMock<IAuthorService>().Setup(c => c.GetAllAuthors()).Returns(authorList);
            Mocker.GetMock<IImportListFactory>().Setup(c => c.All()).Returns(importLists);
            Mocker.GetMock<IRootFolderService>().Setup(c => c.All()).Returns(rootFolders);
            Mocker.GetMock<IProfileRepository>().Setup(c => c.Get(profile.Id)).Returns(profile);

            Assert.Throws<QualityProfileInUseException>(() => Subject.Delete(profile.Id));

            Mocker.GetMock<IProfileRepository>().Verify(c => c.Delete(It.IsAny<int>()), Times.Never());
        }

        [Test]
        public void should_not_be_able_to_delete_profile_if_assigned_to_import_list()
        {
            var profile = Builder<QualityProfile>.CreateNew()
                .With(p => p.Id = 2)
                .Build();

            var authorList = Builder<Author>.CreateListOfSize(3)
                .All()
                .With(c => c.QualityProfileId = 1)
                .Build().ToList();

            var importLists = Builder<ImportListDefinition>.CreateListOfSize(2)
                .Random(1)
                .With(c => c.ProfileId = profile.Id)
                .Build().ToList();

            var rootFolders = Builder<RootFolder>.CreateListOfSize(2)
                .All()
                .With(f => f.DefaultQualityProfileId = 1)
                .BuildList();

            Mocker.GetMock<IAuthorService>().Setup(c => c.GetAllAuthors()).Returns(authorList);
            Mocker.GetMock<IImportListFactory>().Setup(c => c.All()).Returns(importLists);
            Mocker.GetMock<IRootFolderService>().Setup(c => c.All()).Returns(rootFolders);
            Mocker.GetMock<IProfileRepository>().Setup(c => c.Get(profile.Id)).Returns(profile);

            Assert.Throws<QualityProfileInUseException>(() => Subject.Delete(profile.Id));

            Mocker.GetMock<IProfileRepository>().Verify(c => c.Delete(It.IsAny<int>()), Times.Never());
        }

        [Test]
        public void should_not_be_able_to_delete_profile_if_assigned_to_root_folder()
        {
            var profile = Builder<QualityProfile>.CreateNew()
                .With(p => p.Id = 2)
                .Build();

            var authorList = Builder<Author>.CreateListOfSize(3)
                .All()
                .With(c => c.QualityProfileId = 1)
                .Build().ToList();

            var importLists = Builder<ImportListDefinition>.CreateListOfSize(2)
                .All()
                .With(c => c.ProfileId = 1)
                .Build().ToList();

            var rootFolders = Builder<RootFolder>.CreateListOfSize(2)
                .Random(1)
                .With(f => f.DefaultQualityProfileId = profile.Id)
                .BuildList();

            Mocker.GetMock<IAuthorService>().Setup(c => c.GetAllAuthors()).Returns(authorList);
            Mocker.GetMock<IImportListFactory>().Setup(c => c.All()).Returns(importLists);
            Mocker.GetMock<IRootFolderService>().Setup(c => c.All()).Returns(rootFolders);
            Mocker.GetMock<IProfileRepository>().Setup(c => c.Get(profile.Id)).Returns(profile);

            Assert.Throws<QualityProfileInUseException>(() => Subject.Delete(profile.Id));

            Mocker.GetMock<IProfileRepository>().Verify(c => c.Delete(It.IsAny<int>()), Times.Never());
        }

        [Test]
        public void should_delete_profile_if_not_assigned_to_author_import_list_or_root_folder()
        {
            var authorList = Builder<Author>.CreateListOfSize(3)
                                            .All()
                                            .With(c => c.QualityProfileId = 2)
                                            .Build().ToList();

            var importLists = Builder<ImportListDefinition>.CreateListOfSize(2)
                .All()
                .With(c => c.ProfileId = 2)
                .Build().ToList();

            var rootFolders = Builder<RootFolder>.CreateListOfSize(2)
                .All()
                .With(f => f.DefaultQualityProfileId = 2)
                .BuildList();

            Mocker.GetMock<IAuthorService>().Setup(c => c.GetAllAuthors()).Returns(authorList);
            Mocker.GetMock<IImportListFactory>().Setup(c => c.All()).Returns(importLists);
            Mocker.GetMock<IRootFolderService>().Setup(c => c.All()).Returns(rootFolders);

            Subject.Delete(1);

            Mocker.GetMock<IProfileRepository>().Verify(c => c.Delete(1), Times.Once());
        }
    }
}
