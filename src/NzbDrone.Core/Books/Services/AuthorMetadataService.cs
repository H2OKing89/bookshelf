using System.Collections.Generic;

namespace NzbDrone.Core.Books
{
    public interface IAuthorMetadataService
    {
        bool Upsert(AuthorMetadata author);
        bool UpsertMany(List<AuthorMetadata> authors);
        AuthorMetadata PromoteIdentity(int authorId, int metadataId, string previousForeignId, string canonicalForeignId, string recordingId);
    }

    public class AuthorMetadataService : IAuthorMetadataService
    {
        private readonly IAuthorMetadataRepository _authorMetadataRepository;

        public AuthorMetadataService(IAuthorMetadataRepository authorMetadataRepository)
        {
            _authorMetadataRepository = authorMetadataRepository;
        }

        public bool Upsert(AuthorMetadata author)
        {
            return _authorMetadataRepository.UpsertMany(new List<AuthorMetadata> { author });
        }

        public bool UpsertMany(List<AuthorMetadata> authors)
        {
            return _authorMetadataRepository.UpsertMany(authors);
        }

        public AuthorMetadata PromoteIdentity(int authorId, int metadataId, string previousForeignId, string canonicalForeignId, string recordingId)
        {
            return _authorMetadataRepository.PromoteIdentity(authorId, metadataId, previousForeignId, canonicalForeignId, recordingId);
        }
    }
}
