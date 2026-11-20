using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.DecisionEngine;
using NzbDrone.Core.Download.TrackedDownloads;
using NzbDrone.Core.MediaFiles.BookImport;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.Test.Download
{
    [TestFixture]
    public class ImportRejectionBoundaryFixture
    {
        [Test]
        public void import_result_should_preserve_decision_code_and_classification_in_queue_details()
        {
            var rejection = new Rejection("Localized reason", RejectionType.Temporary, RejectionCode.CustomFormatDowngrade);
            var result = new ImportResult(new ImportDecision<LocalBook>(new LocalBook(), rejection), rejection.Reason);

            var status = new TrackedDownloadStatusMessage("Part 1", result.Errors, result.Rejections);

            status.Messages.Should().Equal(rejection.Reason);
            status.Rejections.Should().ContainSingle().Which.Should().BeSameAs(rejection);
            result.Result.Should().Be(ImportResultType.Rejected);
        }

        [Test]
        public void typed_publication_failure_should_remain_skipped_and_include_later_rollback_failure()
        {
            var result = ImportResult.WithRejections(new ImportDecision<LocalBook>(new LocalBook()),
                new Rejection("Destination error", code: "destination_exists"));
            result.AddRejection(new Rejection("Batch rolled back", RejectionType.Temporary, "import_failed"));

            result.Result.Should().Be(ImportResultType.Skipped);
            result.Errors.Should().Equal("Destination error", "Batch rolled back");
            result.Rejections.Select(rejection => rejection.Code).Should().Equal("destination_exists", "import_failed");
        }

        [Test]
        public void unclassified_runtime_errors_should_remain_unknown_regardless_of_english_words()
        {
            var result = new ImportResult(new ImportDecision<LocalBook>(new LocalBook()), "Not an upgrade for existing book file(s)");

            result.Rejections.Single().Code.Should().Be(RejectionCode.Unknown);
        }

        [Test]
        public void a_code_should_survive_a_changed_explanation_in_import_status()
        {
            var result = ImportResult.WithRejections(new ImportDecision<LocalBook>(new LocalBook()),
                new Rejection("First explanation", RejectionType.Temporary, "future_reason"));
            result.Rejections.Single().Reason = "Different localized explanation";

            result.Rejections.Should().Contain(rejection => rejection.Code == "future_reason");
        }
    }
}
