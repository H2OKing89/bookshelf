using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Datastore.Events;
using NzbDrone.Core.Jobs;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Datastore
{
    [TestFixture]
    public class BasicRepositoryWriteFixture : DbTest
    {
        private EventPublishingRepository _repository;
        private List<ScheduledTask> _tasks;
        private List<ModelEvent<ScheduledTask>> _events;

        [SetUp]
        public void Setup()
        {
            _tasks = new List<ScheduledTask>
            {
                NewTask("FirstTask", 10),
                NewTask("SecondTask", 20)
            };
            Db.InsertMany(_tasks);

            _events = new List<ModelEvent<ScheduledTask>>();
            Mocker.GetMock<IEventAggregator>()
                .Setup(aggregator => aggregator.PublishEvent(It.IsAny<ModelEvent<ScheduledTask>>()))
                .Callback<ModelEvent<ScheduledTask>>(_events.Add);

            _repository = new EventPublishingRepository(Mocker.Resolve<IDatabase>(), Mocker.GetMock<IEventAggregator>().Object);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void single_update_should_propagate_constraint_failure_without_publishing_success(bool setFields)
        {
            _tasks[0].TypeName = _tasks[1].TypeName;
            _tasks[0].Interval = 100;

            var exception = Assert.Catch<DbException>(() => UpdateSingle(_tasks[0], setFields));

            exception.Data.Contains("SQL").Should().BeFalse();
            AssertOriginalRows();
            _events.Should().BeEmpty();
        }

        [TestCase(false)]
        [TestCase(true)]
        public void bulk_update_should_roll_back_when_second_row_violates_constraint(bool setFields)
        {
            // The first update succeeds; the second conflicts with its new unique TypeName.
            _tasks[0].TypeName = "UpdatedTask";
            _tasks[0].Interval = 100;
            _tasks[1].TypeName = "UpdatedTask";
            _tasks[1].Interval = 200;

            var exception = Assert.Catch<DbException>(() => UpdateBulk(_tasks, setFields));

            exception.Data.Contains("SQL").Should().BeFalse();
            AssertOriginalRows();
            _events.Should().BeEmpty();
        }

        [TestCase(false)]
        [TestCase(true)]
        public void single_update_should_reject_missing_row_without_publishing_success(bool setFields)
        {
            var missing = NewTask("MissingTask", 100);
            missing.Id = int.MaxValue;

            Assert.Throws<ModelNotFoundException>(() => UpdateSingle(missing, setFields));

            AssertOriginalRows();
            _events.Should().BeEmpty();
        }

        [TestCase(false)]
        [TestCase(true)]
        public void bulk_update_should_roll_back_when_second_row_is_missing(bool setFields)
        {
            _tasks[0].TypeName = "UpdatedTask";
            _tasks[0].Interval = 100;
            var missing = NewTask("MissingTask", 200);
            missing.Id = int.MaxValue;

            Assert.Throws<ModelNotFoundException>(() => UpdateBulk(new List<ScheduledTask> { _tasks[0], missing }, setFields));

            AssertOriginalRows();
            _events.Should().BeEmpty();
        }

        [TestCase(false)]
        [TestCase(true)]
        public void single_update_should_publish_success_after_write_is_committed(bool setFields)
        {
            _tasks[0].TypeName = "UpdatedTask";
            _tasks[0].Interval = 100;

            Mocker.GetMock<IEventAggregator>()
                .Setup(aggregator => aggregator.PublishEvent(It.IsAny<ModelEvent<ScheduledTask>>()))
                .Callback<ModelEvent<ScheduledTask>>(message =>
                {
                    // Db opens a separate connection, so it can only observe a committed write.
                    var stored = Db.All<ScheduledTask>().Single(task => task.Id == message.ModelId);
                    stored.TypeName.Should().Be("UpdatedTask");
                    stored.Interval.Should().Be(100);
                    _events.Add(message);
                });

            UpdateSingle(_tasks[0], setFields);

            _events.Should().ContainSingle();
            _events[0].Action.Should().Be(ModelAction.Updated);
            _events[0].Model.Should().BeSameAs(_tasks[0]);
        }

        [Test]
        public void bulk_set_fields_should_publish_success_only_after_all_writes_are_committed()
        {
            _tasks[0].TypeName = "FirstUpdatedTask";
            _tasks[0].Interval = 100;
            _tasks[1].TypeName = "SecondUpdatedTask";
            _tasks[1].Interval = 200;

            Mocker.GetMock<IEventAggregator>()
                .Setup(aggregator => aggregator.PublishEvent(It.IsAny<ModelEvent<ScheduledTask>>()))
                .Callback<ModelEvent<ScheduledTask>>(message =>
                {
                    var stored = Db.All<ScheduledTask>().OrderBy(task => task.Id).ToList();
                    stored.Select(task => task.TypeName).Should().Equal("FirstUpdatedTask", "SecondUpdatedTask");
                    stored.Select(task => task.Interval).Should().Equal(100, 200);
                    _events.Add(message);
                });

            _repository.SetFields(_tasks, task => task.TypeName, task => task.Interval);

            _events.Select(message => message.ModelId).Should().Equal(_tasks.Select(task => task.Id));
            _events.Should().OnlyContain(message => message.Action == ModelAction.Updated);
        }

        [Test]
        public void update_many_should_preserve_its_absence_of_model_events()
        {
            _tasks[0].Interval = 100;
            _tasks[1].Interval = 200;

            _repository.UpdateMany(_tasks);

            Db.All<ScheduledTask>().OrderBy(task => task.Id).Select(task => task.Interval).Should().Equal(100, 200);
            _events.Should().BeEmpty();
        }

        [TestCase(false)]
        [TestCase(true)]
        public void empty_bulk_update_should_be_a_no_op(bool setFields)
        {
            Assert.DoesNotThrow(() => UpdateBulk(new List<ScheduledTask>(), setFields));

            AssertOriginalRows();
            _events.Should().BeEmpty();
        }

        private void UpdateSingle(ScheduledTask task, bool setFields)
        {
            if (setFields)
            {
                _repository.SetFields(task, model => model.TypeName, model => model.Interval);
            }
            else
            {
                _repository.Update(task);
            }
        }

        private void UpdateBulk(IList<ScheduledTask> tasks, bool setFields)
        {
            if (setFields)
            {
                _repository.SetFields(tasks, model => model.TypeName, model => model.Interval);
            }
            else
            {
                _repository.UpdateMany(tasks);
            }
        }

        private void AssertOriginalRows()
        {
            var stored = Db.All<ScheduledTask>().OrderBy(task => task.Id).ToList();
            stored.Select(task => task.Id).Should().Equal(_tasks.Select(task => task.Id));
            stored.Select(task => task.TypeName).Should().Equal("FirstTask", "SecondTask");
            stored.Select(task => task.Interval).Should().Equal(10, 20);
        }

        private static ScheduledTask NewTask(string typeName, int interval)
        {
            return new ScheduledTask
            {
                TypeName = typeName,
                Interval = interval,
                LastExecution = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                LastStartTime = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            };
        }

        private class EventPublishingRepository : BasicRepository<ScheduledTask>
        {
            public EventPublishingRepository(IDatabase database, IEventAggregator eventAggregator)
                : base(database, eventAggregator)
            {
            }

            protected override bool PublishModelEvents => true;
        }
    }
}
