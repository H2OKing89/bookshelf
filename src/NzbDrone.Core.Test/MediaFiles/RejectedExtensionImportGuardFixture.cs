using System.Collections.Generic;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.DecisionEngine;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.BookImport;
using NzbDrone.Core.MediaFiles.Events;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MediaFiles
{
    [TestFixture]
    public class RejectedExtensionImportGuardFixture : CoreTest<ImportApprovedBooks>
    {
        [TestCase("book.mp3", "mp3", RejectionCode.UserRejectedExtension)]
        [TestCase("book.CUE", "", RejectionCode.UnsupportedFileExtension)]
        [TestCase("cover.jpg", "", RejectionCode.UnsupportedFileExtension)]
        [TestCase("install.EXE", "", RejectionCode.ExecutableFile)]
        [TestCase("book.zip", "", RejectionCode.ArchiveNotExtracted)]
        [TestCase("book.doc", "", RejectionCode.UnsupportedFileExtension)]
        public void an_explicit_manual_import_should_not_bypass_file_extension_policy(string path, string policy, string code)
        {
            Mocker.GetMock<IConfigService>().SetupGet(config => config.UserRejectedExtensions).Returns(policy);
            Mocker.SetConstant<IRejectedFileExtensionService>(new RejectedFileExtensionService(Mocker.GetMock<IConfigService>().Object));
            var decision = new ImportDecision<LocalBook>(new LocalBook { Path = path });

            var result = Subject.Import(new List<ImportDecision<LocalBook>> { decision }, true);

            result.Should().ContainSingle().Which.Result.Should().Be(ImportResultType.Rejected);
            decision.Rejections.Should().ContainSingle().Which.Code.Should().Be(code);
            Mocker.GetMock<IUpgradeMediaFiles>().Verify(upgrader => upgrader.UpgradeBookFile(It.IsAny<BookFile>(), It.IsAny<LocalBook>(), It.IsAny<bool>()), Times.Never());
            Mocker.GetMock<IEventAggregator>().Verify(events => events.PublishEvent(It.IsAny<TrackImportedEvent>()), Times.Never());
        }
    }
}
