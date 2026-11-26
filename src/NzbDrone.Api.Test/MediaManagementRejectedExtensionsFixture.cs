using System.Collections.Generic;
using FluentAssertions;
using FluentValidation.Results;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Serializer;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Validation;
using NzbDrone.Core.Validation.Paths;
using NzbDrone.Test.Common;
using Readarr.Api.V1.Config;

namespace NzbDrone.Api.Test
{
    [TestFixture]
    public class MediaManagementRejectedExtensionsFixture : TestBase<MediaManagementRejectedExtensionsFixture.TestMediaManagementConfigController>
    {
        [TestCase("mp3, .M4B, tar.gz", true)]
        [TestCase("", true)]
        [TestCase(null, true)]
        [TestCase("../mp3", false)]
        [TestCase("*.mp3", false)]
        [TestCase("mp3;exe", false)]
        public void api_should_validate_extension_tokens(string policy, bool valid)
        {
            var resource = new MediaManagementConfigResource { MinimumFreeSpaceWhenImporting = 100, UserRejectedExtensions = policy };

            Subject.ValidateSettings(resource).IsValid.Should().Be(valid);
        }

        [Test]
        public void resource_should_roundtrip_policy_with_explicit_empty_and_legacy_omission()
        {
            Mocker.GetMock<IConfigService>().SetupGet(config => config.UserRejectedExtensions).Returns("mp3");
            var resource = MediaManagementConfigResourceMapper.ToResource(Mocker.GetMock<IConfigService>().Object);

            STJson.Deserialize<MediaManagementConfigResource>(STJson.ToJson(resource)).UserRejectedExtensions.Should().Be("mp3");
            STJson.Deserialize<MediaManagementConfigResource>("{\"userRejectedExtensions\":\"\"}").UserRejectedExtensions.Should().BeEmpty();
            STJson.Deserialize<MediaManagementConfigResource>("{}").UserRejectedExtensions.Should().BeNull();
        }

        [Test]
        public void omitted_policy_should_be_passed_as_null_to_preserve_the_existing_value()
        {
            var resource = STJson.Deserialize<MediaManagementConfigResource>("{\"id\":1,\"minimumFreeSpaceWhenImporting\":100}");

            Subject.SaveConfig(resource);

            Mocker.GetMock<IConfigService>().Verify(
                config => config.SaveConfigDictionary(It.Is<Dictionary<string, object>>(
                    values => values.ContainsKey("UserRejectedExtensions") && values["UserRejectedExtensions"] == null)),
                Times.Once());
        }

        [TestCase("{}", null)]
        [TestCase("{\"checkFreeSpaceWhenGrabbing\":false}", false)]
        [TestCase("{\"checkFreeSpaceWhenGrabbing\":true}", true)]
        public void preserve_omission_and_explicit_grab_space_policy_on_save(string json, bool? enabled)
        {
            var resource = STJson.Deserialize<MediaManagementConfigResource>(json);
            resource.MinimumFreeSpaceWhenImporting = 100;

            Subject.SaveConfig(resource);

            Mocker.GetMock<IConfigService>().Verify(
                config => config.SaveConfigDictionary(It.Is<Dictionary<string, object>>(
                    values => Equals(values["CheckFreeSpaceWhenGrabbing"], enabled))),
                Times.Once());
        }

        public class TestMediaManagementConfigController : MediaManagementConfigController
        {
            public TestMediaManagementConfigController(IConfigService configService,
                PathExistsValidator pathExistsValidator,
                FolderChmodValidator folderChmodValidator,
                FolderWritableValidator folderWritableValidator,
                AuthorPathValidator authorPathValidator,
                StartupFolderValidator startupFolderValidator,
                SystemFolderValidator systemFolderValidator,
                RootFolderAncestorValidator rootFolderAncestorValidator,
                RootFolderValidator rootFolderValidator)
                : base(configService,
                    pathExistsValidator,
                    folderChmodValidator,
                    folderWritableValidator,
                    authorPathValidator,
                    startupFolderValidator,
                    systemFolderValidator,
                    rootFolderAncestorValidator,
                    rootFolderValidator)
            {
            }

            public ValidationResult ValidateSettings(MediaManagementConfigResource resource)
            {
                return SharedValidator.Validate(resource);
            }
        }
    }
}
