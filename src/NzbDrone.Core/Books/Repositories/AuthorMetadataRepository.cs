using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using Dapper;
using NLog;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.MetadataSource.Audiobook;
using NzbDrone.Core.MetadataSource.Audnexus;

namespace NzbDrone.Core.Books
{
    public interface IAuthorMetadataRepository : IBasicRepository<AuthorMetadata>
    {
        List<AuthorMetadata> FindById(List<string> foreignIds);
        bool UpsertMany(List<AuthorMetadata> data);
        AuthorMetadata PromoteIdentity(int authorId, int metadataId, string previousForeignId, string canonicalForeignId, string recordingId);
    }

    public class AuthorMetadataRepository : BasicRepository<AuthorMetadata>, IAuthorMetadataRepository
    {
        private readonly Logger _logger;

        public AuthorMetadataRepository(IMainDatabase database, IEventAggregator eventAggregator, Logger logger)
            : base(database, eventAggregator)
        {
            _logger = logger;
        }

        public List<AuthorMetadata> FindById(List<string> foreignIds)
        {
            return Query(x => Enumerable.Contains(foreignIds, x.ForeignAuthorId));
        }

        // The caller established the contributor identity from the exact
        // existing recording. Recheck ownership and destination uniqueness at
        // commit time, then change the key in place: all numeric associations
        // (including books, files and history) keep their original IDs.
        public AuthorMetadata PromoteIdentity(int authorId, int metadataId, string previousForeignId, string canonicalForeignId, string recordingId)
        {
            var previous = AudiobookId.Parse(previousForeignId);
            var canonical = AudiobookId.Parse(canonicalForeignId);
            var recording = AudiobookId.Parse(recordingId);
            if (authorId <= 0 || metadataId <= 0 || !previous.IsName || canonical.IsName || recording.IsName ||
                canonical.Region != previous.Region || recording.Region != previous.Region)
            {
                throw new InvalidOperationException("Only a verified name-only Audible identity can be promoted in its existing marketplace.");
            }

            using var connection = _database.OpenConnection();
            using var transaction = connection.BeginTransaction(IsolationLevel.Serializable);
            if (_database.DatabaseType == DatabaseType.PostgreSQL)
            {
                connection.Execute("""
                    LOCK TABLE "AuthorMetadata", "Authors", "Books", "Editions", "ImportListExclusions"
                    IN SHARE ROW EXCLUSIVE MODE
                    """, transaction: transaction);
            }

            var canonicalIds = AudnexusProxy.Regions.Select(region => AudiobookId.ForAsin(region, canonical.Value)).ToArray();
            var canonicalPredicate = _database.DatabaseType == DatabaseType.PostgreSQL ? "= ANY(@CanonicalIds)" : "IN @CanonicalIds";
            var parameters = new
            {
                AuthorId = authorId,
                MetadataId = metadataId,
                PreviousId = previousForeignId,
                CanonicalId = canonicalForeignId,
                CanonicalIds = canonicalIds,
                RecordingId = recordingId,
                Asin = recording.Value,
                Region = recording.Region
            };
            var metadata = connection.QuerySingleOrDefault<AuthorMetadata>(
                """
                SELECT m.* FROM "AuthorMetadata" m INNER JOIN "Authors" a ON a."AuthorMetadataId" = m."Id"
                WHERE a."Id" = @AuthorId AND m."Id" = @MetadataId AND m."ForeignAuthorId" = @PreviousId
                """,
                parameters,
                transaction: transaction);
            var ownerCount = connection.ExecuteScalar<int>("""SELECT COUNT(*) FROM "Authors" WHERE "AuthorMetadataId" = @MetadataId""", parameters, transaction: transaction);
            var canonicalCount = connection.ExecuteScalar<int>($"""SELECT COUNT(*) FROM "AuthorMetadata" WHERE "ForeignAuthorId" {canonicalPredicate}""", parameters, transaction: transaction);
            var proofCount = connection.ExecuteScalar<int>(
                """
                SELECT COUNT(*) FROM "Books" b INNER JOIN "Editions" e ON e."BookId" = b."Id"
                WHERE b."AuthorMetadataId" = @MetadataId AND b."ForeignBookId" = @RecordingId
                    AND e."ForeignEditionId" = @RecordingId AND e."Asin" = @Asin AND e."AudibleRegion" = @Region
                """,
                parameters,
                transaction: transaction);
            if (metadata == null || ownerCount != 1 || canonicalCount != 0 || proofCount != 1)
            {
                throw new InvalidOperationException("The author identity or supporting recording changed, or the canonical author already exists. Manual review is required.");
            }

            metadata.ForeignAuthorId = canonicalForeignId;
            var canonicalLink = AudiobookMetadataMapper.Author(new AudnexusContributorResource { Asin = canonical.Value, Name = metadata.Name }, canonical.Region).Links.Single();
            metadata.Links ??= new List<Links>();
            if (!metadata.Links.Any(link => link.Url == canonicalLink.Url))
            {
                metadata.Links.Add(canonicalLink);
            }

            Update(connection, transaction, metadata);

            // Preserve the old exclusion when the target already has its own
            // exclusion; never delete or replace existing user choices.
            connection.Execute(
                """
                UPDATE "ImportListExclusions" SET "ForeignId" = @CanonicalId WHERE "ForeignId" = @PreviousId
                AND NOT EXISTS (SELECT 1 FROM "ImportListExclusions" WHERE "ForeignId" = @CanonicalId)
                """,
                parameters,
                transaction: transaction);
            transaction.Commit();
            return metadata;
        }

        public bool UpsertMany(List<AuthorMetadata> data)
        {
            var existingMetadata = FindById(data.Select(x => x.ForeignAuthorId).ToList());
            var updateMetadataList = new List<AuthorMetadata>();
            var addMetadataList = new List<AuthorMetadata>();
            var upToDateMetadataCount = 0;

            foreach (var meta in data)
            {
                var existing = existingMetadata.SingleOrDefault(x => x.ForeignAuthorId == meta.ForeignAuthorId);
                if (existing != null)
                {
                    // populate Id in remote data
                    meta.UseDbFieldsFrom(existing);

                    // responses vary, so try adding remote to what we have
                    if (!meta.Equals(existing))
                    {
                        updateMetadataList.Add(meta);
                    }
                    else
                    {
                        upToDateMetadataCount++;
                    }
                }
                else
                {
                    addMetadataList.Add(meta);
                }
            }

            UpdateMany(updateMetadataList);
            InsertMany(addMetadataList);

            _logger.Debug($"{upToDateMetadataCount} author metadata up to date; Updating {updateMetadataList.Count}, Adding {addMetadataList.Count} author metadata entries.");

            return updateMetadataList.Count > 0 || addMetadataList.Count > 0;
        }
    }
}
