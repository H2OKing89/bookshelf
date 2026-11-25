using System.Collections.Generic;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.ImportLists.Exclusions;
using Readarr.Api.V1.ImportLists;

namespace NzbDrone.Api.Test
{
    public class ImportListExclusionIdentityFixture
    {
        [TestCase("audible:us:B000000002", true)]
        [TestCase("audible:uk:B000000002", true)]
        [TestCase("audible:us:name:Owned%20Author", true)]
        [TestCase("local:recording:11111111111111111111111111111111", true)]
        [TestCase("8a1c6d9b-2c7e-4f10-aaff-1edd2c30e4e2", true)]
        [TestCase("B000000002", false)]
        [TestCase("audible:unknown:B000000002", false)]
        [TestCase("local:recording:invalid", false)]
        public void should_accept_only_qualified_or_legacy_guid_identities(string id, bool valid)
        {
            var service = new Mock<IImportListExclusionService>();
            service.Setup(x => x.All()).Returns(new List<ImportListExclusion>());
            var controller = new ValidationController(service.Object);
            controller.Accepts(id).Should().Be(valid);
        }

        private class ValidationController : ImportListExclusionController
        {
            public ValidationController(IImportListExclusionService service)
                : base(service, new ImportListExclusionExistsValidator(service))
            {
            }

            public bool Accepts(string id) => SharedValidator.Validate(new ImportListExclusionResource { ForeignId = id, AuthorName = "Owned title" }).IsValid;
        }
    }
}
