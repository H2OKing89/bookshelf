using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Cache;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Common.Messaging;
using NzbDrone.Core.HealthCheck;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.HealthCheck
{
    public class HealthCheckServiceFixture : CoreTest<HealthCheckService>
    {
        private FakeHealthCheck _healthCheck;

        [SetUp]
        public void SetUp()
        {
            _healthCheck = new FakeHealthCheck();
            Mocker.GetMock<IRuntimeInfo>().SetupGet(info => info.StartTime).Returns(DateTime.UtcNow.AddHours(-1));

            Mocker.SetConstant<IEnumerable<IProvideHealthCheck>>(new[] { _healthCheck });
            Mocker.SetConstant<ICacheManager>(Mocker.Resolve<CacheManager>());

            Mocker.GetMock<IServerSideNotificationService>()
                .Setup(v => v.GetServerChecks())
                .Returns(new List<Core.HealthCheck.HealthCheck>());
        }

        [Test]
        public void one_recovery_after_a_real_issue_uses_the_latest_severity_and_removes_the_issue()
        {
            Subject.HandleAsync(new FakeEvent2());
            _healthCheck.Result = new Core.HealthCheck.HealthCheck(typeof(FakeHealthCheck), HealthCheckResult.Warning, "Original warning");
            Subject.HandleAsync(new FakeEvent2());
            Subject.HandleAsync(new FakeEvent2());
            var previous = new Core.HealthCheck.HealthCheck(typeof(FakeHealthCheck), HealthCheckResult.Error, "Latest error");
            _healthCheck.Result = previous;
            Subject.HandleAsync(new FakeEvent2());

            Subject.Results().Should().ContainSingle().Which.Should().BeSameAs(previous);
            Mocker.GetMock<IEventAggregator>().Verify(aggregator => aggregator.PublishEvent(It.IsAny<HealthCheckRestoredEvent>()), Times.Never());

            _healthCheck.Result = new Core.HealthCheck.HealthCheck(typeof(FakeHealthCheck));
            Parallel.Invoke(() => Subject.HandleAsync(new FakeEvent2()), () => Subject.HandleAsync(new FakeEvent2()));
            Subject.HandleAsync(new FakeEvent2());

            Subject.Results().Should().BeEmpty();
            Mocker.GetMock<IEventAggregator>().Verify(aggregator => aggregator.PublishEvent(It.Is<HealthCheckRestoredEvent>(
                restored => restored.PreviousCheck == previous && !restored.IsInStartupGracePeriod)), Times.Once());
            Mocker.GetMock<IEventAggregator>().Verify(aggregator => aggregator.PublishEvent(It.IsAny<HealthCheckRestoredEvent>()), Times.Once());
            Mocker.GetMock<IEventAggregator>().Verify(aggregator => aggregator.PublishEvent(It.IsAny<HealthCheckFailedEvent>()), Times.Once());
        }

        [Test]
        public void transient_startup_issue_recovers_during_grace()
        {
            Mocker.GetMock<IRuntimeInfo>().SetupGet(info => info.StartTime).Returns(DateTime.UtcNow);
            _healthCheck.Result = new Core.HealthCheck.HealthCheck(typeof(FakeHealthCheck), HealthCheckResult.Warning, "Startup warning");
            Subject.HandleAsync(new FakeEvent2());
            _healthCheck.Result = new Core.HealthCheck.HealthCheck(typeof(FakeHealthCheck));
            Subject.HandleAsync(new FakeEvent2());

            Subject.Results().Should().BeEmpty();
            Mocker.GetMock<IEventAggregator>().Verify(aggregator => aggregator.PublishEvent(It.Is<HealthCheckRestoredEvent>(
                restored => restored.IsInStartupGracePeriod)), Times.Once());
        }

        [Test]
        public void should_not_execute_conditional()
        {
            Subject.HandleAsync(new FakeEvent());

            _healthCheck.Executed.Should().BeFalse();
        }

        [Test]
        public void should_execute_conditional()
        {
            Subject.HandleAsync(new FakeEvent() { ShouldExecute = true });

            _healthCheck.Executed.Should().BeTrue();
        }

        [Test]
        public void should_execute_unconditional()
        {
            Subject.HandleAsync(new FakeEvent2());

            _healthCheck.Executed.Should().BeTrue();
        }
    }

    public class FakeEvent : IEvent
    {
        public bool ShouldExecute { get; set; }
    }

    public class FakeEvent2 : IEvent
    {
        public bool ShouldExecute { get; set; }
    }

    [CheckOn(typeof(FakeEvent))]
    [CheckOn(typeof(FakeEvent2))]
    public class FakeHealthCheck : IProvideHealthCheck, ICheckOnCondition<FakeEvent>
    {
        public bool CheckOnStartup => false;
        public bool CheckOnSchedule => false;

        public bool Executed { get; set; }
        public bool Checked { get; set; }
        public Core.HealthCheck.HealthCheck Result { get; set; }

        public Core.HealthCheck.HealthCheck Check()
        {
            Executed = true;

            return Result ?? new Core.HealthCheck.HealthCheck(GetType());
        }

        public bool ShouldCheckOnEvent(FakeEvent message)
        {
            return message.ShouldExecute;
        }
    }
}
