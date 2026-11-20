using System;
using System.Collections.Generic;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.Configuration
{
    [TestFixture]
    public class RejectedExtensionsConfigFixture : TestBase<ConfigService>
    {
        [Test]
        public void default_should_be_empty_and_not_persisted()
        {
            Subject.UserRejectedExtensions.Should().BeEmpty();
            Mocker.GetMock<IConfigRepository>().Verify(repository => repository.Upsert(It.IsAny<string>(), It.IsAny<string>()), Times.Never());
        }

        [Test]
        public void property_and_dictionary_saves_should_normalize_policy()
        {
            Subject.UserRejectedExtensions = " .MP3, m4b, mp3 ";
            Subject.SaveConfigDictionary(new Dictionary<string, object> { ["UserRejectedExtensions"] = ".EPUB, PDF" });

            Mocker.GetMock<IConfigRepository>().Verify(repository => repository.Upsert("userrejectedextensions", "mp3, m4b"), Times.Once());
            Mocker.GetMock<IConfigRepository>().Verify(repository => repository.Upsert("userrejectedextensions", "epub, pdf"), Times.Once());
        }

        [Test]
        public void omitted_or_null_value_should_preserve_policy()
        {
            Mocker.GetMock<IConfigRepository>().Setup(repository => repository.All()).Returns(new List<Config>
            {
                new Config { Key = "userrejectedextensions", Value = "mp3" }
            });
            Subject.SaveConfigDictionary(new Dictionary<string, object> { ["UserRejectedExtensions"] = null });
            Subject.UserRejectedExtensions = null;

            Subject.UserRejectedExtensions.Should().Be("mp3");
            Mocker.GetMock<IConfigRepository>().Verify(repository => repository.Upsert("userrejectedextensions", It.IsAny<string>()), Times.Never());
        }

        [Test]
        public void explicit_empty_should_clear_a_saved_policy()
        {
            Mocker.GetMock<IConfigRepository>().Setup(repository => repository.All()).Returns(new List<Config>
            {
                new Config { Key = "userrejectedextensions", Value = "mp3" }
            });

            Subject.SaveConfigDictionary(new Dictionary<string, object> { ["UserRejectedExtensions"] = "" });

            Mocker.GetMock<IConfigRepository>().Verify(repository => repository.Upsert("userrejectedextensions", ""), Times.Once());
        }

        [Test]
        public void invalid_policy_should_fail_before_any_other_setting_is_written()
        {
            Assert.Throws<ArgumentException>(() => Subject.SaveConfigDictionary(new Dictionary<string, object>
            {
                ["CreateEmptyAuthorFolders"] = true,
                ["UserRejectedExtensions"] = "../mp3"
            }));

            Mocker.GetMock<IConfigRepository>().Verify(repository => repository.Upsert(It.IsAny<string>(), It.IsAny<string>()), Times.Never());
        }
    }
}
