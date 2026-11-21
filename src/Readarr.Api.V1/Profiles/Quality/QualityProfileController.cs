using System.Collections.Generic;
using System.Linq;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.CustomFormats;
using NzbDrone.Core.DecisionEngine;
using NzbDrone.Core.Profiles.Qualities;
using NzbDrone.Http.REST.Attributes;
using Readarr.Http;
using Readarr.Http.REST;

namespace Readarr.Api.V1.Profiles.Quality
{
    [V1ApiController]
    public class QualityProfileController : RestController<QualityProfileResource>
    {
        private readonly IQualityProfileService _qualityProfileService;
        private readonly ICustomFormatService _formatService;

        public QualityProfileController(IQualityProfileService qualityProfileService, ICustomFormatService formatService)
        {
            _qualityProfileService = qualityProfileService;
            _formatService = formatService;
            SharedValidator.RuleFor(c => c.Name).NotEmpty();
            SharedValidator.RuleFor(c => c.Cutoff).ValidCutoff();
            SharedValidator.RuleFor(c => c.Items).ValidItems();
            SharedValidator.RuleFor(c => c.MinUpgradeFormatScore).GreaterThanOrEqualTo(0);

            SharedValidator.RuleFor(c => c.FormatItems).Must(items =>
            {
                var all = _formatService.All().Select(f => f.Id).ToList();
                var ids = items.Select(i => i.Format);

                return all.Except(ids).Empty();
            }).WithMessage("All Custom Formats and no extra ones need to be present inside your Profile! Try refreshing your browser.");

            SharedValidator.RuleFor(c => c).Custom((profile, context) =>
            {
                if (profile.FormatItems.Where(x => x.Score > 0).Sum(x => x.Score) < profile.MinFormatScore &&
                    profile.FormatItems.Max(x => x.Score) < profile.MinFormatScore)
                {
                    context.AddFailure("Minimum Custom Format Score can never be satisfied");
                }
            });
        }

        [RestPostById]
        public ActionResult<QualityProfileResource> Create(QualityProfileResource resource)
        {
            var model = resource.ToModel();
            ValidateSizeRules(model.Items);
            model = _qualityProfileService.Add(model);
            return Created(model.Id);
        }

        [RestDeleteById]
        public void DeleteProfile(int id)
        {
            _qualityProfileService.Delete(id);
        }

        [RestPutById]
        public ActionResult<QualityProfileResource> Update(QualityProfileResource resource)
        {
            var model = resource.ToModel();
            var saved = _qualityProfileService.Get(resource.Id);
            if (saved != null)
            {
                model.MinUpgradeFormatScore = resource.MinUpgradeFormatScore ?? saved.MinUpgradeFormatScore;
                PreserveSizeRules(model.Items, saved.Items);
            }

            ValidateSizeRules(model.Items);
            _qualityProfileService.Update(model);

            return Accepted(model.Id);
        }

        protected override QualityProfileResource GetResourceById(int id)
        {
            return _qualityProfileService.Get(id).ToResource();
        }

        [HttpGet]
        public List<QualityProfileResource> GetAll()
        {
            return _qualityProfileService.All().ToResource();
        }

        private static IEnumerable<QualityProfileQualityItem> ProfileItems(IEnumerable<QualityProfileQualityItem> items)
        {
            return items.SelectMany(item => new[] { item }.Concat(ProfileItems(item.Items)));
        }

        private static void PreserveSizeRules(IEnumerable<QualityProfileQualityItem> items, IEnumerable<QualityProfileQualityItem> saved)
        {
            var previous = ProfileItems(saved).Where(item => item.Quality != null).ToLookup(item => item.Quality.Id);
            foreach (var item in ProfileItems(items).Where(item => item.Quality != null))
            {
                var prior = previous[item.Quality.Id].FirstOrDefault();
                item.MinSize ??= prior?.MinSize;
                item.PreferredSize ??= prior?.PreferredSize;
                item.MaxSize ??= prior?.MaxSize;
            }
        }

        private static void ValidateSizeRules(IEnumerable<QualityProfileQualityItem> items)
        {
            const decimal largestSetting = 1000000;
            var failures = new List<ValidationFailure>();
            foreach (var item in ProfileItems(items))
            {
                var limits = new[] { item.MinSize, item.PreferredSize, item.MaxSize };
                var name = item.Quality?.Name ?? item.Name ?? "Quality group";
                if (limits.Any(limit => limit < 0 || limit > largestSetting))
                {
                    failures.Add(new ValidationFailure("Items", $"Size settings for {name} must be between 0 and {largestSetting}."));
                }
                else if (limits.Any(limit => limit > 0) && ReleaseSizePolicy.GetSizeUnit(item.Quality) == null)
                {
                    failures.Add(new ValidationFailure("Items", $"Size settings require an individual audiobook or ebook quality. Remove size settings from {name}."));
                }
                else
                {
                    var configured = limits.Where(limit => limit > 0).ToArray();
                    if (!configured.SequenceEqual(configured.OrderBy(limit => limit)))
                    {
                        failures.Add(new ValidationFailure("Items", $"Minimum, preferred and maximum size for {name} must be in ascending order."));
                    }
                }
            }

            if (failures.Count > 0)
            {
                throw new ValidationException(failures);
            }
        }
    }
}
