using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using FluentValidation;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.MetadataSource.Audiobook;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MetadataSource.Audiobook
{
    [TestFixture]
    public class AudiobookMetadataCleanupRuleFixture : CoreTest<AudiobookMetadataCleanupRuleService>
    {
        private List<AudiobookMetadataCleanupRule> _rows;

        [SetUp]
        public void Setup()
        {
            _rows = new List<AudiobookMetadataCleanupRule>();
            Mocker.GetMock<IAudiobookMetadataCleanupRuleRepository>().Setup(repository => repository.All()).Returns(() => _rows);
            Mocker.GetMock<IAudiobookMetadataCleanupRuleRepository>().Setup(repository => repository.Insert(It.IsAny<AudiobookMetadataCleanupRule>()))
                .Returns((AudiobookMetadataCleanupRule item) =>
                {
                    item.Id = _rows.Count + 1;
                    _rows.Add(item);
                    return item;
                });
            Mocker.GetMock<IAudiobookMetadataCleanupRuleRepository>().Setup(repository => repository.Update(It.IsAny<AudiobookMetadataCleanupRule>()))
                .Returns((AudiobookMetadataCleanupRule item) =>
                {
                    _rows[_rows.FindIndex(row => row.Id == item.Id)] = item;
                    return item;
                });
            Mocker.GetMock<IAudiobookMetadataCleanupRuleRepository>().Setup(repository => repository.Delete(It.IsAny<int>()))
                .Callback((int id) => _rows.RemoveAll(row => row.Id == id));
        }

        [Test]
        public void should_normalize_and_save_editable_rules_without_reseeding_deleted_defaults()
        {
            var saved = Subject.Save(new AudiobookMetadataCleanupRule
            {
                Phrase = "  A   LitRPG Adventure  ", Labels = new List<string> { " LitRPG ", "litrpg", "" }, Enabled = true, Reason = "Observed genre line"
            });

            saved.Phrase.Should().Be("A LitRPG Adventure");
            saved.Labels.Should().Equal("LitRPG");
            saved.Enabled = false;
            Subject.Save(saved);
            Subject.Find(saved.Id).Enabled.Should().BeFalse();
            Subject.Delete(saved.Id);
            Subject.All().Should().BeEmpty();
        }

        [Test]
        public void should_not_allow_callers_to_mutate_cached_rules()
        {
            var saved = Subject.Save(new AudiobookMetadataCleanupRule { Phrase = "Light Novel", Labels = new List<string> { "Light Novel" }, Reason = "Format" });
            var item = Subject.Find(saved.Id);
            item.Phrase = "Changed";
            item.Labels.Clear();

            Subject.Find(saved.Id).Phrase.Should().Be("Light Novel");
            Subject.Find(saved.Id).Labels.Should().Equal("Light Novel");
        }

        [Test]
        public void should_validate_drafts_with_the_same_normalized_duplicate_policy_as_saved_rules()
        {
            var duplicate = new[]
            {
                new AudiobookMetadataCleanupRule { Phrase = "A LitRPG Adventure", Reason = "Genre" },
                new AudiobookMetadataCleanupRule { Phrase = " a   litrpg adventure ", Reason = "Genre" }
            };

            Assert.Throws<ValidationException>(() => AudiobookMetadataCleanupRuleValidation.NormalizeAndValidate(duplicate));
            Subject.Save(duplicate[0]);
            Assert.Throws<ValidationException>(() => Subject.Save(duplicate[1]));
            Subject.All().Should().ContainSingle();
        }

        [TestCase(null, "Reason")]
        [TestCase(" ", "Reason")]
        [TestCase("Light Novel", " ")]
        public void should_reject_incomplete_rules(string phrase, string reason)
        {
            Assert.Throws<ValidationException>(() => Subject.Save(new AudiobookMetadataCleanupRule { Phrase = phrase, Reason = reason }));
            _rows.Should().BeEmpty();
        }

        [Test]
        public void should_ship_disabled_defaults_with_only_evidenced_classification_labels()
        {
            var seeds = AudiobookMetadataCleanupRuleSeed.Read();

            seeds.Should().OnlyContain(item => !item.Enabled);
            seeds.Single(item => item.Phrase == "A Slice of Life Harem").Labels.Should().BeEmpty();
            seeds.Single(item => item.Phrase == "A Slice of Life Harem LitRPG").Labels.Should().Equal("LitRPG");
        }
    }
}
