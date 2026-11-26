using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.ImportLists.Exceptions;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.ImportLists.Hardcover
{
    public class HardcoverImportParser : IParseImportListResponse
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        private ImportListResponse _importListResponse;

        public IList<ImportListItemInfo> ParseResponse(ImportListResponse importListResponse)
        {
            _importListResponse = importListResponse;

            var items = new List<ImportListItemInfo>();

            if (!PreProcess(_importListResponse))
            {
                return items;
            }

            if (_importListResponse.Content.IsNullOrWhiteSpace())
            {
                throw IncompleteResponse();
            }

            var root = JsonConvert.DeserializeObject<JToken>(_importListResponse.Content);
            var request = JsonConvert.DeserializeObject<JObject>(Encoding.UTF8.GetString(_importListResponse.HttpRequest.ContentData ?? System.Array.Empty<byte>()));
            var variables = request?["variables"];
            if (root is not JObject || root["errors"]?.HasValues == true || root["data"]?["users"] is not JArray users || users.Count != 1)
            {
                throw IncompleteResponse();
            }

            var user = users.Single();
            var books = new List<JToken>();
            if (variables?["slugs"] is JArray slugs)
            {
                if (user["lists"] is not JArray lists || !new HashSet<string>(slugs.Values<string>()).SetEquals(lists.Select(list => list.Value<string>("slug"))))
                {
                    throw IncompleteResponse();
                }

                foreach (var list in lists)
                {
                    if (list["list_books"] is not JArray listBooks)
                    {
                        throw IncompleteResponse();
                    }

                    books.AddRange(listBooks.Select(entry => entry["book"]));
                }
            }
            else if (variables?["statusIds"] is JArray && user["user_books"] is JArray userBooks)
            {
                books.AddRange(userBooks.Select(entry => entry["book"]));
            }
            else
            {
                throw IncompleteResponse();
            }

            foreach (var entry in books)
            {
                if (entry is not JObject || ParseEntry(entry) is not { } item || item.Book.IsNullOrWhiteSpace())
                {
                    throw IncompleteResponse();
                }

                items.Add(item);
            }

            Logger.Debug("Hardcover: Parsed {0} items from response", items.Count);
            return items;
        }

        private ImportListException IncompleteResponse() => new(_importListResponse, "Hardcover did not return a complete list response.");

        private ImportListItemInfo ParseEntry(JToken entry)
        {
            var title = entry.Value<string>("title")
                        ?? entry.Value<string>("name")
                        ?? entry.Value<string>("book_title");

            // Try various author field structures
            var author = entry.Value<string>("author")
                         ?? entry.Value<string>("author_name");

            // Hardcover uses contributions[].author.name
            if (author.IsNullOrWhiteSpace())
            {
                var contributions = entry["contributions"];
                if (contributions != null && contributions.Type == JTokenType.Array)
                {
                    var firstContribution = contributions.Children().FirstOrDefault();
                    author = firstContribution?["author"]?.Value<string>("name");
                }
            }

            // Fallback to authors array
            if (author.IsNullOrWhiteSpace())
            {
                var authorToken = entry["authors"]?.Children().FirstOrDefault();
                author = authorToken?.Value<string>("name")
                          ?? authorToken?.Value<string>("full_name");
            }

            // Get Hardcover book ID
            var bookId = entry.Value<string>("id") ?? entry.Value<int?>("id")?.ToString();

            // Get author ID from contributions
            string authorId = null;
            var contribsToken = entry["contributions"];
            if (contribsToken != null && contribsToken.Type == JTokenType.Array)
            {
                var firstContrib = contribsToken.Children().FirstOrDefault();
                if (firstContrib != null)
                {
                    var authorToken = firstContrib["author"];
                    authorId = authorToken?.Value<string>("id") ?? authorToken?.Value<int?>("id")?.ToString();
                }
            }

            if (title.IsNullOrWhiteSpace() && author.IsNullOrWhiteSpace())
            {
                return null;
            }

            Logger.Trace("Hardcover: Parsed '{0}' by '{1}' (bookId={2}, authorId={3})", title, author, bookId, authorId);

            return new ImportListItemInfo
            {
                Book = title,
                Author = author,
                AuthorGoodreadsId = authorId,
                BookGoodreadsId = bookId
            };
        }

        protected virtual bool PreProcess(ImportListResponse importListResponse)
        {
            if (importListResponse.HttpResponse.StatusCode != HttpStatusCode.OK)
            {
                throw new ImportListException(importListResponse, "Import List API call resulted in an unexpected StatusCode [{0}]", importListResponse.HttpResponse.StatusCode);
            }

            if (importListResponse.HttpResponse.Headers.ContentType != null &&
                importListResponse.HttpResponse.Headers.ContentType.Contains("text/html"))
            {
                throw new ImportListException(importListResponse, "Import List responded with html content. Site is likely blocked or unavailable.");
            }

            return true;
        }
    }
}
