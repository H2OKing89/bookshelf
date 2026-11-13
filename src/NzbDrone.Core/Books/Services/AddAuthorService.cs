using System;
using System.Collections.Generic;
using System.Linq;
using FluentValidation;
using FluentValidation.Results;
using NLog;
using NzbDrone.Common.EnsureThat;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.MetadataSource.Audiobook;
using NzbDrone.Core.Parser;

namespace NzbDrone.Core.Books
{
    public interface IAddAuthorService
    {
        Author AddAuthor(Author newAuthor, bool doRefresh = true);
        Author AddAuthorFromBook(Author newAuthor, Book book);
        List<Author> AddAuthors(List<Author> newAuthors, bool doRefresh = true);
    }

    public class AddAuthorService : IAddAuthorService
    {
        private readonly IAuthorService _authorService;
        private readonly IAuthorMetadataService _authorMetadataService;
        private readonly IProvideAuthorInfo _authorInfo;
        private readonly IBuildAuthorPaths _authorPathBuilder;
        private readonly IAddAuthorValidator _addAuthorValidator;
        private readonly Logger _logger;

        public AddAuthorService(IAuthorService authorService,
                                IAuthorMetadataService authorMetadataService,
                                IProvideAuthorInfo authorInfo,
                                IBuildAuthorPaths authorPathBuilder,
                                IAddAuthorValidator addAuthorValidator,
                                Logger logger)
        {
            _authorService = authorService;
            _authorMetadataService = authorMetadataService;
            _authorInfo = authorInfo;
            _authorPathBuilder = authorPathBuilder;
            _addAuthorValidator = addAuthorValidator;
            _logger = logger;
        }

        public Author AddAuthor(Author newAuthor, bool doRefresh = true)
        {
            Ensure.That(newAuthor, () => newAuthor).IsNotNull();

            newAuthor = AddSkyhookData(newAuthor);
            newAuthor = SetPropertiesAndValidate(newAuthor);

            _logger.Info("Adding Author {0} Path: [{1}]", newAuthor, newAuthor.Path);

            // add metadata
            _authorMetadataService.Upsert(newAuthor.Metadata.Value);
            newAuthor.AuthorMetadataId = newAuthor.Metadata.Value.Id;

            // add the author itself
            return _authorService.AddAuthor(newAuthor, doRefresh);
        }

        public Author AddAuthorFromBook(Author newAuthor, Book book)
        {
            Ensure.That(newAuthor, () => newAuthor).IsNotNull();
            var metadata = book?.AuthorMetadata?.Value;
            var edition = book?.Editions?.Value?.SingleOrDefault(item => item.Monitored);
            if (!AudiobookId.IsNative(book?.ForeignBookId) || metadata == null || string.IsNullOrWhiteSpace(metadata.Name) ||
                !AudiobookId.IsNative(metadata.ForeignAuthorId) || edition?.ForeignEditionId != book.ForeignBookId)
            {
                throw new InvalidOperationException("Adding an author from a recording requires verified recording and author metadata.");
            }

            var recording = AudiobookId.Parse(book.ForeignBookId);
            AudiobookId.Parse(metadata.ForeignAuthorId);
            if (recording.IsName || edition.Asin != recording.Value || edition.AudibleRegion != recording.Region)
            {
                throw new InvalidOperationException("The recording metadata must retain its exact ASIN and marketplace.");
            }

            // The exact recording lookup already supplied this author credit. A
            // missing profile or an unavailable full bibliography must not block it.
            newAuthor.Id = 0;
            newAuthor.AuthorMetadataId = 0;
            newAuthor.Metadata = metadata;
            newAuthor = SetPropertiesAndValidate(newAuthor);
            _authorMetadataService.Upsert(metadata);
            newAuthor.AuthorMetadataId = metadata.Id;
            return _authorService.AddAuthor(newAuthor, false);
        }

        public List<Author> AddAuthors(List<Author> newAuthors, bool doRefresh = true)
        {
            var added = DateTime.UtcNow;
            var authorsToAdd = new List<Author>();

            foreach (var s in newAuthors)
            {
                try
                {
                    var author = AddSkyhookData(s);
                    author = SetPropertiesAndValidate(author);
                    author.Added = added;
                    authorsToAdd.Add(author);
                }
                catch (Exception ex)
                {
                    // Catch Import Errors for now until we get things fixed up
                    _logger.Error(ex, "Failed to import id: {0} - {1}", s.Metadata.Value.ForeignAuthorId, s.Metadata.Value.Name);
                }
            }

            // add metadata
            _authorMetadataService.UpsertMany(authorsToAdd.Select(x => x.Metadata.Value).ToList());
            authorsToAdd.ForEach(x => x.AuthorMetadataId = x.Metadata.Value.Id);

            return _authorService.AddAuthors(authorsToAdd, doRefresh);
        }

        private Author AddSkyhookData(Author newAuthor)
        {
            Author author;

            try
            {
                author = _authorInfo.GetAuthorInfo(newAuthor.Metadata.Value.ForeignAuthorId, false);
            }
            catch (AuthorNotFoundException)
            {
                _logger.Error("Author {0} was not found in the audiobook catalog.", newAuthor.Metadata.Value.ForeignAuthorId);

                throw new ValidationException(new List<ValidationFailure>
                {
                    new ("ForeignAuthorId", "An author with this ID was not found", newAuthor.Metadata.Value.ForeignAuthorId)
                });
            }

            author.ApplyChanges(newAuthor);

            return author;
        }

        private Author SetPropertiesAndValidate(Author newAuthor)
        {
            var path = newAuthor.Path;
            if (string.IsNullOrWhiteSpace(path))
            {
                path = _authorPathBuilder.BuildPath(newAuthor, false);
            }

            // Disambiguate author path if it exists already
            if (_authorService.AuthorPathExists(path))
            {
                if (newAuthor.Metadata.Value.Disambiguation.IsNotNullOrWhiteSpace())
                {
                    path += $" ({newAuthor.Metadata.Value.Disambiguation})";
                }

                if (_authorService.AuthorPathExists(path))
                {
                    var basepath = path;
                    var i = 0;
                    do
                    {
                        i++;
                        path = basepath + $" ({i})";
                    }
                    while (_authorService.AuthorPathExists(path));
                }
            }

            newAuthor.Path = path;
            newAuthor.CleanName = newAuthor.Metadata.Value.Name.CleanAuthorName();
            newAuthor.Added = DateTime.UtcNow;

            if (newAuthor.AddOptions != null && newAuthor.AddOptions.Monitor == MonitorTypes.None)
            {
                newAuthor.Monitored = false;
            }

            var validationResult = _addAuthorValidator.Validate(newAuthor);

            if (!validationResult.IsValid)
            {
                throw new ValidationException(validationResult.Errors);
            }

            return newAuthor;
        }
    }
}
