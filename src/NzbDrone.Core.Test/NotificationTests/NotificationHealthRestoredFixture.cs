using System;
using System.Collections.Generic;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Serializer;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.HealthCheck;
using NzbDrone.Core.Notifications;
using NzbDrone.Core.Notifications.Webhook;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;
using WebhookNotification = NzbDrone.Core.Notifications.Webhook.Webhook;

namespace NzbDrone.Core.Test.NotificationTests
{
    public class NotificationHealthRestoredFixture : CoreTest<NotificationService>
    {
        private readonly List<INotification> _providers = new List<INotification>();

        [SetUp]
        public void Setup()
        {
            _providers.Clear();
            Mocker.GetMock<INotificationFactory>().Setup(factory => factory.OnHealthRestoredEnabled(true)).Returns(_providers);
        }

        private Mock<INotification> AddProvider(int id, bool includeWarnings)
        {
            var provider = new Mock<INotification>();
            var definition = new NotificationDefinition
            {
                Id = id, Name = "Owned health connection", OnHealthRestored = true, OnHealthIssue = false,
                IncludeHealthWarnings = includeWarnings
            };
            provider.SetupGet(notification => notification.Definition).Returns(definition);
            _providers.Add(provider.Object);
            return provider;
        }

        private static Core.HealthCheck.HealthCheck Prior(HealthCheckResult level)
        {
            return new Core.HealthCheck.HealthCheck(typeof(NotificationHealthRestoredFixture), level, "Owned issue");
        }

        [TestCase(HealthCheckResult.Warning, false, false)]
        [TestCase(HealthCheckResult.Warning, true, true)]
        [TestCase(HealthCheckResult.Error, false, true)]
        [TestCase(HealthCheckResult.Notice, true, false)]
        [TestCase(HealthCheckResult.Ok, true, false)]
        public void recovery_uses_the_prior_level_and_independent_opt_in(HealthCheckResult level, bool includeWarnings, bool delivered)
        {
            var provider = AddProvider(1, includeWarnings);
            var prior = Prior(level);

            Subject.Handle(new HealthCheckRestoredEvent(prior, false));

            provider.Verify(notification => notification.OnHealthRestored(prior), delivered ? Times.Once() : Times.Never());
            provider.Verify(notification => notification.OnHealthIssue(It.IsAny<Core.HealthCheck.HealthCheck>()), Times.Never());
            ((NotificationDefinition)provider.Object.Definition).Enable.Should().BeTrue();
        }

        [Test]
        public void grace_recoveries_never_notify()
        {
            AddProvider(1, true);
            Subject.Handle(new HealthCheckRestoredEvent(Prior(HealthCheckResult.Error), true));
            Mocker.GetMock<INotificationFactory>().Verify(factory => factory.OnHealthRestoredEnabled(true), Times.Never());
        }

        [Test]
        public void failed_connection_and_status_update_do_not_prevent_other_deliveries()
        {
            var provider = AddProvider(1, true);
            var other = AddProvider(2, true);
            var prior = Prior(HealthCheckResult.Error);
            provider.Setup(notification => notification.OnHealthRestored(prior)).Throws(new InvalidOperationException("Owned private credential"));
            Mocker.GetMock<INotificationStatusService>().Setup(status => status.RecordFailure(1, It.IsAny<TimeSpan>()))
                .Throws(new InvalidOperationException("Owned status failure"));

            Subject.Handle(new HealthCheckRestoredEvent(prior, false));

            other.Verify(notification => notification.OnHealthRestored(prior), Times.Once());
            Mocker.GetMock<INotificationStatusService>().Verify(status => status.RecordSuccess(2), Times.Once());
            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void webhook_uses_a_distinct_event_with_previous_health_details()
        {
            var prior = Prior(HealthCheckResult.Warning);
            prior.WikiUrl = null;
            var settings = new WebhookSettings();
            var hook = new WebhookNotification(Mocker.GetMock<IWebhookProxy>().Object,
                Mocker.GetMock<IConfigFileProvider>().Object);
            hook.Definition = new NotificationDefinition { Settings = settings };
            WebhookHealthPayload payload = null;
            Mocker.GetMock<IWebhookProxy>().Setup(proxy => proxy.SendWebhook(It.IsAny<WebhookHealthPayload>(), settings))
                .Callback<WebhookPayload, WebhookSettings>((sent, _) => payload = (WebhookHealthPayload)sent);

            hook.OnHealthRestored(prior);

            hook.SupportsOnHealthRestored.Should().BeTrue();
            payload.EventType.Should().Be(WebhookEventType.HealthRestored);
            payload.Level.Should().Be(HealthCheckResult.Warning);
            payload.Type.Should().Be(nameof(NotificationHealthRestoredFixture));
            payload.Message.Should().Be("Owned issue");
            payload.WikiUrl.Should().BeNull();
            payload.ToJson().Should().Contain("\"HealthRestored\"");
        }

        [Test]
        public void health_recovery_is_disabled_by_default()
        {
            var definition = new NotificationDefinition();
            definition.OnHealthRestored.Should().BeFalse();
            definition.Enable.Should().BeFalse();
        }
    }
}
