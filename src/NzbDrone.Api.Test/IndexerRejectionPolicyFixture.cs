using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Common.Serializer;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Indexers.Newznab;
using Readarr.Api.V1.Indexers;

namespace NzbDrone.Api.Test
{
    [TestFixture]
    public class IndexerRejectionPolicyFixture
    {
        [Test]
        public void mapper_should_round_trip_all_actions_and_explicit_zero()
        {
            var mapper = new IndexerResourceMapper();
            var definition = new IndexerDefinition
            {
                Name = "Owned indexer", Implementation = "Newznab", Settings = new NewznabSettings(),
                DangerousFileAction = RejectedImportAction.BlocklistAndRetry,
                ExecutableFileAction = RejectedImportAction.Review,
                UserRejectedExtensionAction = RejectedImportAction.Blocklist
            };
            var resource = mapper.ToResource(definition);
            var json = STJson.ToJson(resource);
            var model = mapper.ToModel(STJson.Deserialize<IndexerResource>(json));
            model.DangerousFileAction.Should().Be(RejectedImportAction.BlocklistAndRetry);
            model.ExecutableFileAction.Should().Be(RejectedImportAction.Review);
            model.UserRejectedExtensionAction.Should().Be(RejectedImportAction.Blocklist);
        }

        [Test]
        public void old_resource_omits_policy_but_explicit_review_is_a_real_choice()
        {
            STJson.Deserialize<IndexerResource>("{}").ExecutableFileAction.Should().BeNull();
            STJson.Deserialize<IndexerResource>("{\"executableFileAction\":\"review\"}").ExecutableFileAction.Should().Be(RejectedImportAction.Review);
        }
    }
}
