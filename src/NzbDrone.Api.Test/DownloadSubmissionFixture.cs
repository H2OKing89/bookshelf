using System;
using System.Collections.Generic;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Download;
using NzbDrone.Test.Common;
using Readarr.Api.V1.DownloadClient;

namespace NzbDrone.Api.Test
{
    [TestFixture]
    public class DownloadSubmissionFixture : TestBase<DownloadSubmissionController>
    {
        [Test]
        public void list_should_preserve_client_and_attempt_identity()
        {
            var hold = new DownloadSubmissionHold { AttemptId = Guid.NewGuid().ToString("N"), DownloadClientId = 12 };
            Mocker.GetMock<IDownloadSubmissionGuard>().Setup(guard => guard.GetPending()).Returns(new List<DownloadSubmissionHold> { hold });

            Subject.GetPending().Should().ContainSingle().Which.Should().BeSameAs(hold);
        }

        [TestCase(false, 12)]
        [TestCase(true, 0)]
        [TestCase(true, -1)]
        public void acknowledgement_requires_client_review_and_positive_identity(bool reviewed, int clientId)
        {
            Subject.Acknowledge(new DownloadSubmissionAcknowledgement
            {
                AttemptId = Guid.NewGuid().ToString("N"), DownloadClientId = clientId, ClientReviewed = reviewed
            }).Should().BeOfType<BadRequestObjectResult>();

            Mocker.GetMock<IDownloadSubmissionGuard>().Verify(guard => guard.Acknowledge(It.IsAny<string>(), It.IsAny<int>()), Times.Never());
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("invalid")]
        public void malformed_attempt_should_not_clear_any_hold(string attemptId)
        {
            Subject.Acknowledge(new DownloadSubmissionAcknowledgement
            {
                AttemptId = attemptId, DownloadClientId = 12, ClientReviewed = true
            }).Should().BeOfType<BadRequestObjectResult>();

            Mocker.GetMock<IDownloadSubmissionGuard>().Verify(guard => guard.Acknowledge(It.IsAny<string>(), It.IsAny<int>()), Times.Never());
        }

        [TestCase(true, typeof(NoContentResult))]
        [TestCase(false, typeof(ConflictObjectResult))]
        public void acknowledgement_should_use_exact_client_and_attempt(bool cleared, Type resultType)
        {
            var attemptId = Guid.NewGuid().ToString("N");
            Mocker.GetMock<IDownloadSubmissionGuard>().Setup(guard => guard.Acknowledge(attemptId, 12)).Returns(cleared);

            Subject.Acknowledge(new DownloadSubmissionAcknowledgement
            {
                AttemptId = attemptId, DownloadClientId = 12, ClientReviewed = true
            }).Should().BeOfType(resultType);

            Mocker.GetMock<IDownloadSubmissionGuard>().Verify(guard => guard.Acknowledge(attemptId, 12), Times.Once());
        }
    }
}
