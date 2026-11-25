using System;
using System.Collections.Generic;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.ImportLists.Exclusions;
using NzbDrone.Core.Books;
using NzbDrone.Core.MetadataSource.Audiobook;
using NzbDrone.Core.MetadataSource.BookInfo;
using NzbDrone.Http.REST.Attributes;
using Readarr.Http;
using Readarr.Http.REST;

namespace Readarr.Api.V1.ImportLists
{
    [V1ApiController]
    public class ImportListExclusionController : RestController<ImportListExclusionResource>
    {
        private readonly IImportListExclusionService _importListExclusionService;

        public ImportListExclusionController(IImportListExclusionService importListExclusionService,
                                         ImportListExclusionExistsValidator importListExclusionExistsValidator)
        {
            _importListExclusionService = importListExclusionService;

            SharedValidator.RuleFor(c => c.ForeignId).NotEmpty().Must(IsQualifiedIdentity).WithMessage("Choose a qualified recording or author identity.").SetValidator(importListExclusionExistsValidator);
            SharedValidator.RuleFor(c => c.AuthorName).NotEmpty();
        }

        private static bool IsQualifiedIdentity(string id)
        {
            if (Guid.TryParse(id, out _))
            {
                return true;
            }

            if (LocalAuthorId.IsLocal(id) || LocalRecordingId.IsLocal(id))
            {
                var parts = id.Split(':');
                return parts.Length == 3 && Guid.TryParse(parts[2], out _);
            }

            try
            {
                AudiobookId.Parse(id);
                return true;
            }
            catch (BookInfoException)
            {
                return false;
            }
        }

        protected override ImportListExclusionResource GetResourceById(int id)
        {
            return _importListExclusionService.Get(id).ToResource();
        }

        [HttpGet]
        public List<ImportListExclusionResource> GetImportListExclusions()
        {
            return _importListExclusionService.All().ToResource();
        }

        [RestPostById]
        public ActionResult<ImportListExclusionResource> AddImportListExclusion(ImportListExclusionResource resource)
        {
            var customFilter = _importListExclusionService.Add(resource.ToModel());

            return Created(customFilter.Id);
        }

        [RestPutById]
        public ActionResult<ImportListExclusionResource> UpdateImportListExclusion(ImportListExclusionResource resource)
        {
            _importListExclusionService.Update(resource.ToModel());
            return Accepted(resource.Id);
        }

        [RestDeleteById]
        public void DeleteImportListExclusionResource(int id)
        {
            _importListExclusionService.Delete(id);
        }
    }
}
