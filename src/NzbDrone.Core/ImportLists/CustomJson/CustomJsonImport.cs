using System;
using System.Collections.Generic;
using FluentValidation.Results;
using NLog;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Parser;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.ThingiProvider;

namespace NzbDrone.Core.ImportLists.CustomJson
{
    public class CustomJsonImport : ImportListBase<CustomJsonImportSettings>
    {
        private readonly ICustomJsonImportProxy _proxy;

        public override string Name => "Custom JSON";
        public override ImportListType ListType => ImportListType.Other;
        public override TimeSpan MinRefreshInterval => TimeSpan.FromHours(6);
        public override ProviderMessage Message => new ("Imports a complete, versioned Bookshelf JSON list. Each recording must have an exact qualified identity; bare ASINs, title-only matches and inferred volume ranges are not imported.", ProviderMessageType.Info);

        public CustomJsonImport(ICustomJsonImportProxy proxy,
                                IImportListStatusService importListStatusService,
                                IConfigService configService,
                                IParsingService parsingService,
                                Logger logger)
            : base(importListStatusService, configService, parsingService, logger)
        {
            _proxy = proxy;
        }

        public override IList<ImportListItemInfo> Fetch() => FetchWithStatus().Items;

        public override ImportListFetchResult FetchWithStatus()
        {
            try
            {
                var items = CleanupListItems(_proxy.GetItems(Settings));
                _importListStatusService.RecordSuccess(Definition.Id);
                return new ImportListFetchResult(items, true);
            }
            catch (CustomJsonImportException exception)
            {
                // Only explicitly constructed, static diagnostics may contain
                // response information (status/page numbers), never its content.
                _logger.Warn("Custom JSON list {0}: {1}", Definition.Id, exception.Message);
            }
            catch (Exception)
            {
                _logger.Warn("Custom JSON list {0} could not be fetched.", Definition.Id);
            }

            _importListStatusService.RecordFailure(Definition.Id);
            return new ImportListFetchResult(Array.Empty<ImportListItemInfo>(), false);
        }

        protected override void Test(List<ValidationFailure> failures)
        {
            try
            {
                _proxy.GetItems(Settings);
            }
            catch (CustomJsonImportException exception)
            {
                failures.Add(new ValidationFailure("BaseUrl", exception.Message));
            }
            catch (Exception)
            {
                failures.Add(new ValidationFailure("BaseUrl", "Could not fetch the custom JSON list."));
            }
        }
    }
}
