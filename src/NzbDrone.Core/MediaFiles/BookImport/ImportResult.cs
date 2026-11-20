using System.Collections.Generic;
using System.Linq;
using NzbDrone.Common.EnsureThat;
using NzbDrone.Core.DecisionEngine;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.MediaFiles.BookImport
{
    public class ImportResult
    {
        private readonly List<Rejection> _failures = new List<Rejection>();

        public ImportDecision<LocalBook> ImportDecision { get; private set; }
        public List<string> Errors { get; private set; }

        public List<Rejection> Rejections
        {
            get
            {
                var rejections = ImportDecision.Rejections.Concat(_failures).ToList();
                foreach (var error in Errors.Where(error => !rejections.Any(rejection => rejection.Reason == error)))
                {
                    rejections.Add(new Rejection(error));
                }

                return rejections;
            }
        }

        // Only assigned after persistence. Completion must bind to this exact
        // live file rather than infer a committed destination from a name.
        [System.Text.Json.Serialization.JsonIgnore]
        [Newtonsoft.Json.JsonIgnore]
        public BookFile ImportedFile { get; set; }

        public ImportResultType Result
        {
            get
            {
                if (Errors.Any())
                {
                    if (ImportDecision.Approved)
                    {
                        return ImportResultType.Skipped;
                    }

                    return ImportResultType.Rejected;
                }

                return ImportResultType.Imported;
            }
        }

        public ImportResult(ImportDecision<LocalBook> importDecision, params string[] errors)
        {
            Ensure.That(importDecision, () => importDecision).IsNotNull();

            ImportDecision = importDecision;
            Errors = errors.ToList();
        }

        public static ImportResult WithRejections(ImportDecision<LocalBook> importDecision, params Rejection[] failures)
        {
            var result = new ImportResult(importDecision, failures.Select(rejection => rejection.Reason).ToArray());
            result._failures.AddRange(failures);
            return result;
        }

        public void AddRejection(Rejection failure)
        {
            Errors.Add(failure.Reason);
            _failures.Add(failure);
        }
    }
}
