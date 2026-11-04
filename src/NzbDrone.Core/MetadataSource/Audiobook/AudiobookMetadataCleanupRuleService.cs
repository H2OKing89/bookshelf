using System;
using System.Collections.Generic;
using System.Linq;
using FluentValidation;
using FluentValidation.Results;
using NzbDrone.Common.Cache;

namespace NzbDrone.Core.MetadataSource.Audiobook
{
    public interface IAudiobookMetadataCleanupRuleService
    {
        List<AudiobookMetadataCleanupRule> All();
        AudiobookMetadataCleanupRule Find(int id);
        AudiobookMetadataCleanupRule Save(AudiobookMetadataCleanupRule item);
        void Delete(int id);
    }

    public static class AudiobookMetadataCleanupRuleValidation
    {
        public static List<AudiobookMetadataCleanupRule> NormalizeAndValidate(IEnumerable<AudiobookMetadataCleanupRule> rules)
        {
            var items = (rules ?? Enumerable.Empty<AudiobookMetadataCleanupRule>()).Select(item => new AudiobookMetadataCleanupRule
            {
                Id = item?.Id ?? 0, Phrase = AudiobookTextNormalizer.Text(item?.Phrase),
                Labels = AudiobookTextNormalizer.Names(item?.Labels), Enabled = item?.Enabled ?? false,
                Reason = AudiobookTextNormalizer.Text(item?.Reason), UpdatedAt = item?.UpdatedAt ?? default
            }).ToList();
            var errors = new List<ValidationFailure>();
            foreach (var item in items)
            {
                if (item.Phrase == null || item.Phrase.Length > 200)
                {
                    errors.Add(new ValidationFailure("phrase", "Enter a complete label of at most 200 characters."));
                }

                if (item.Reason == null)
                {
                    errors.Add(new ValidationFailure("reason", "Explain why this label is promotional or format text."));
                }
            }

            if (items.Where(item => item.Phrase != null).GroupBy(item => item.Phrase, StringComparer.OrdinalIgnoreCase).Any(group => group.Count() > 1))
            {
                errors.Add(new ValidationFailure("phrase", "A cleanup rule already exists for this phrase."));
            }

            if (errors.Any())
            {
                throw new ValidationException(errors);
            }

            return items;
        }
    }

    public class AudiobookMetadataCleanupRuleService : IAudiobookMetadataCleanupRuleService
    {
        private readonly IAudiobookMetadataCleanupRuleRepository _repository;
        private readonly ICached<List<AudiobookMetadataCleanupRule>> _cache;

        public AudiobookMetadataCleanupRuleService(IAudiobookMetadataCleanupRuleRepository repository, ICacheManager cacheManager)
        {
            _repository = repository;
            _cache = cacheManager.GetCache<List<AudiobookMetadataCleanupRule>>(GetType());
        }

        private static AudiobookMetadataCleanupRule Clone(AudiobookMetadataCleanupRule item) => new AudiobookMetadataCleanupRule
        {
            Id = item.Id, Phrase = item.Phrase, Labels = item.Labels?.ToList() ?? new List<string>(),
            Enabled = item.Enabled, Reason = item.Reason, UpdatedAt = item.UpdatedAt
        };

        public List<AudiobookMetadataCleanupRule> All() => _cache.Get("all", () =>
            (_repository.All() ?? new List<AudiobookMetadataCleanupRule>()).Select(Clone).ToList()).Select(Clone).ToList();

        public AudiobookMetadataCleanupRule Find(int id) => All().SingleOrDefault(item => item.Id == id);

        public AudiobookMetadataCleanupRule Save(AudiobookMetadataCleanupRule item)
        {
            var normalized = AudiobookMetadataCleanupRuleValidation.NormalizeAndValidate(All().Where(row => row.Id != item.Id).Append(item)).Last();
            normalized.UpdatedAt = DateTime.UtcNow;
            var saved = normalized.Id == 0 ? _repository.Insert(normalized) : _repository.Update(normalized);
            _cache.Clear();
            return Clone(saved);
        }

        public void Delete(int id)
        {
            _repository.Delete(id);
            _cache.Clear();
        }
    }
}
