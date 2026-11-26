using System.Collections.Generic;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Lifecycle;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Qualities
{
    [TestFixture]
    public class QualityDefinitionServiceFixture : CoreTest<QualityDefinitionService>
    {
        [Test]
        public void bulk_update_should_invalidate_cached_definitions_immediately()
        {
            IList<QualityDefinition> stored = new List<QualityDefinition>
            {
                new QualityDefinition(Quality.MP3) { MinSize = 0, MaxSize = 350, Id = 20 },
                new QualityDefinition(Quality.FLAC) { MinSize = 0, MaxSize = null, Id = 21 }
            };

            var repository = Mocker.GetMock<IQualityDefinitionRepository>();
            repository.Setup(r => r.All()).Returns(() => new List<QualityDefinition>(stored));
            repository.Setup(r => r.UpdateMany(It.IsAny<IList<QualityDefinition>>()))
                      .Callback<IList<QualityDefinition>>(definitions => stored = definitions);

            Assert.That(Subject.GetById(20).MaxSize, Is.EqualTo(350));

            Subject.UpdateMany(new List<QualityDefinition>
            {
                new QualityDefinition(Quality.MP3) { MinSize = 0, MaxSize = 349.8, Id = 20 },
                new QualityDefinition(Quality.FLAC) { MinSize = 0, MaxSize = null, Id = 21 }
            });

            Assert.That(Subject.GetById(20).MaxSize, Is.EqualTo(349.8));
            Assert.That(Subject.Get(Quality.MP3).MaxSize, Is.EqualTo(349.8));
            Assert.That(Subject.Get(Quality.FLAC).MaxSize, Is.Null);
        }

        [Test]
        public void init_should_add_all_definitions()
        {
            Subject.Handle(new ApplicationStartedEvent());

            Mocker.GetMock<IQualityDefinitionRepository>()
                .Verify(v => v.InsertMany(It.Is<List<QualityDefinition>>(d => d.Count == Quality.All.Count)), Times.Once());
        }

        [Test]
        public void init_should_insert_any_missing_definitions()
        {
            Mocker.GetMock<IQualityDefinitionRepository>()
                  .Setup(s => s.All())
                  .Returns(new List<QualityDefinition>
                      {
                              new QualityDefinition(Quality.MP3) { Weight = 1, MinSize = 0, MaxSize = 100, Id = 20 }
                      });

            Subject.Handle(new ApplicationStartedEvent());

            Mocker.GetMock<IQualityDefinitionRepository>()
                .Verify(v => v.InsertMany(It.Is<List<QualityDefinition>>(d => d.Count == Quality.All.Count - 1)), Times.Once());
        }

        [Test]
        public void init_should_update_existing_definitions()
        {
            Mocker.GetMock<IQualityDefinitionRepository>()
                  .Setup(s => s.All())
                  .Returns(new List<QualityDefinition>
                      {
                              new QualityDefinition(Quality.MP3) { Weight = 1, MinSize = 0, MaxSize = 100, Id = 20 }
                      });

            Subject.Handle(new ApplicationStartedEvent());

            Mocker.GetMock<IQualityDefinitionRepository>()
                .Verify(v => v.UpdateMany(It.Is<List<QualityDefinition>>(d => d.Count == 1)), Times.Once());
        }

        [Test]
        public void init_should_remove_old_definitions()
        {
            Mocker.GetMock<IQualityDefinitionRepository>()
                  .Setup(s => s.All())
                  .Returns(new List<QualityDefinition>
                      {
                              new QualityDefinition(new Quality { Id = 100, Name = "Test" }) { Weight = 1, MinSize = 0, MaxSize = 100, Id = 20 }
                      });

            Subject.Handle(new ApplicationStartedEvent());

            Mocker.GetMock<IQualityDefinitionRepository>()
                .Verify(v => v.DeleteMany(It.Is<List<QualityDefinition>>(d => d.Count == 1)), Times.Once());
        }
    }
}
