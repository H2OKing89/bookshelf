using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using NzbDrone.Common.Http;
using NzbDrone.Core.Books;
using NzbDrone.Core.MetadataSource.Audiobook;
using NzbDrone.Core.MetadataSource.BookInfo;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.ImportLists.CustomJson
{
    public interface ICustomJsonImportProxy
    {
        List<ImportListItemInfo> GetItems(CustomJsonImportSettings settings);
    }

    // Messages are deliberately owned constants (plus numeric page/status),
    // never a provider-supplied body, URI, credential or parser exception.
    public class CustomJsonImportException : Exception
    {
        public CustomJsonImportException(string message)
            : base(message)
        {
        }
    }

    public class CustomJsonImportProxy : ICustomJsonImportProxy
    {
        public const int SchemaVersion = 1;
        public const int PageSize = 100;
        public const int MaxItems = 1000;
        public const int MaxPageBytes = 1024 * 1024;
        private readonly IHttpClient _httpClient;

        public CustomJsonImportProxy(IHttpClient httpClient)
        {
            _httpClient = httpClient;
        }

        public List<ImportListItemInfo> GetItems(CustomJsonImportSettings settings)
        {
            if (settings == null || !settings.Validate().IsValid)
            {
                throw new CustomJsonImportException("Invalid custom JSON endpoint URL or bearer token.");
            }

            string snapshotId = null;
            var totalItems = -1;
            var pages = 1;
            var processed = 0;
            var items = new List<ImportListItemInfo>();
            var workIds = new HashSet<string>(StringComparer.Ordinal);
            var recordingAuthors = new Dictionary<string, string>(StringComparer.Ordinal);

            for (var page = 1; page <= pages; page++)
            {
                using var document = GetPage(settings, page);
                var root = document.RootElement;
                RequireFields(root, "schemaVersion", "snapshotId", "page", "pageSize", "totalItems", "complete", "items");
                Require(Integer(root, "schemaVersion") == SchemaVersion, "Unsupported Bookshelf custom JSON schema version.");
                Require(root.GetProperty("complete").ValueKind == JsonValueKind.True, "The provider reported an incomplete page.");
                Require(Integer(root, "page") == page && Integer(root, "pageSize") == PageSize, "Unexpected page number or page size.");

                var pageSnapshot = Text(root, "snapshotId", 128);
                var pageTotal = Integer(root, "totalItems");
                Require(pageTotal >= 0 && pageTotal <= MaxItems, "The provider total exceeds the supported 1000 items or is invalid.");
                if (page == 1)
                {
                    snapshotId = pageSnapshot;
                    totalItems = pageTotal;
                    pages = Math.Max(1, (totalItems + PageSize - 1) / PageSize);
                }

                Require(pageSnapshot == snapshotId && pageTotal == totalItems, "The provider snapshot changed during paging.");
                var entries = root.GetProperty("items");
                Require(entries.ValueKind == JsonValueKind.Array && entries.GetArrayLength() == Math.Min(PageSize, totalItems - processed), "The page does not contain its declared complete set of items.");
                foreach (var entry in entries.EnumerateArray())
                {
                    ParseItem(entry, items, workIds, recordingAuthors);
                }

                processed += entries.GetArrayLength();
            }

            Require(processed == totalItems, "The provider returned an incomplete result.");
            return items;
        }

        private JsonDocument GetPage(CustomJsonImportSettings settings, int page)
        {
            // Only the configured origin/path is used. The protocol never
            // follows provider-supplied next URLs, redirects or cookies.
            var url = new UriBuilder(settings.BaseUrl) { Query = $"page={page}&pageSize={PageSize}" }.Uri.AbsoluteUri;
            var request = new HttpRequest(url, HttpAccept.Json)
            {
                AllowAutoRedirect = false,
                SuppressHttpError = true,
                LogHttpError = false,
                RedactSensitiveDetails = true,
                UseCookies = false,
                MaxBufferedResponseBytes = MaxPageBytes,
                RequestTimeout = TimeSpan.FromSeconds(30)
            };

            if (!string.IsNullOrEmpty(settings.ApiKey))
            {
                request.Headers.Set("Authorization", "Bearer " + settings.ApiKey);
            }

            HttpResponse response;
            try
            {
                response = _httpClient.Get(request);
            }
            catch (HttpResponseSizeLimitException)
            {
                throw new CustomJsonImportException($"Custom JSON page {page} exceeds the 1 MiB response limit.");
            }
            catch (Exception)
            {
                throw new CustomJsonImportException($"Could not fetch custom JSON page {page}.");
            }

            Require(response.StatusCode == HttpStatusCode.OK, $"Custom JSON page {page} returned HTTP {(int)response.StatusCode}; redirects and partial content are not supported.");
            Require(response.ResponseData != null && response.ResponseData.Length <= MaxPageBytes, "Custom JSON response is empty or exceeds the 1 MiB response limit.");
            Require(MediaTypeHeaderValue.TryParse(response.Headers.ContentType, out var contentType) &&
                    (contentType.MediaType.Equals("application/json", StringComparison.OrdinalIgnoreCase) || contentType.MediaType.EndsWith("+json", StringComparison.OrdinalIgnoreCase)),
                "Custom JSON endpoint must return a JSON content type.");

            try
            {
                return JsonDocument.Parse(response.ResponseData, new JsonDocumentOptions { MaxDepth = 16 });
            }
            catch (JsonException)
            {
                throw new CustomJsonImportException($"Custom JSON page {page} is not a valid JSON document.");
            }
        }

        private static void ParseItem(JsonElement entry, List<ImportListItemInfo> items, HashSet<string> workIds, Dictionary<string, string> recordingAuthors)
        {
            var kind = Text(entry, "kind", 16);
            var authorId = Text(entry, "authorId", 1024);
            var author = Text(entry, "author");
            Require(IsQualifiedId(authorId, true), "A custom JSON item has an invalid qualified author ID.");

            switch (kind)
            {
                case "author":
                    RequireFields(entry, "kind", "authorId", "author");
                    AddItem(items, new ImportListItemInfo { Author = author, AuthorGoodreadsId = authorId });
                    break;
                case "recording":
                    RequireFields(entry, "kind", "authorId", "author", "title", "recordingId", "editionId");
                    AddRecording(entry, authorId, author, items, recordingAuthors);
                    break;
                case "work":
                    RequireFields(entry, "kind", "authorId", "author", "workId", "workTitle", "recordings");
                    Require(workIds.Add(Text(entry, "workId")), "The custom JSON snapshot contains duplicate work IDs.");
                    Text(entry, "workTitle");
                    var recordings = entry.GetProperty("recordings");
                    Require(recordings.ValueKind == JsonValueKind.Array && recordings.GetArrayLength() > 0 && recordings.GetArrayLength() <= MaxItems,
                        "A work must contain explicitly identified recordings within the list limit.");
                    foreach (var recording in recordings.EnumerateArray())
                    {
                        RequireFields(recording, "title", "recordingId", "editionId");
                        AddRecording(recording, authorId, author, items, recordingAuthors);
                    }

                    break;
                default:
                    throw new CustomJsonImportException("Unsupported custom JSON item kind.");
            }
        }

        private static void AddRecording(JsonElement entry, string authorId, string author, List<ImportListItemInfo> items, Dictionary<string, string> recordingAuthors)
        {
            var recordingId = Text(entry, "recordingId", 1024);
            var editionId = Text(entry, "editionId", 1024);
            Require(IsQualifiedId(recordingId, false) && recordingId == editionId,
                "Recording and edition IDs must be identical qualified recording identities.");
            Require(!AudiobookId.IsNative(recordingId) || !LocalAuthorId.IsLocal(authorId),
                "An Audible recording cannot be assigned to a local-only author.");
            Require(!recordingAuthors.TryGetValue(recordingId, out var existingAuthor) || existingAuthor == authorId,
                "A recording has conflicting qualified author IDs in the custom JSON snapshot.");
            recordingAuthors[recordingId] = authorId;

            AddItem(items, new ImportListItemInfo
            {
                Author = author,
                AuthorGoodreadsId = authorId,
                Book = Text(entry, "title"),
                BookGoodreadsId = recordingId,
                EditionGoodreadsId = editionId
            });
        }

        private static void AddItem(List<ImportListItemInfo> items, ImportListItemInfo item)
        {
            Require(items.Count < MaxItems, "Expanded work membership exceeds the supported 1000 items.");
            items.Add(item);
        }

        private static bool IsQualifiedId(string value, bool author)
        {
            if (author ? LocalAuthorId.IsLocal(value) : LocalRecordingId.IsLocal(value))
            {
                var parts = value.Split(':');
                return parts.Length == 3 && Guid.TryParseExact(parts[2], "N", out var id) && id != Guid.Empty && parts[2] == id.ToString("N");
            }

            if (!AudiobookId.IsNative(value))
            {
                return false;
            }

            try
            {
                var parsed = AudiobookId.Parse(value);
                return parsed.IsName
                    ? author && value == AudiobookId.ForName(parsed.Region, parsed.Value) && !string.IsNullOrWhiteSpace(parsed.Value)
                    : value == AudiobookId.ForAsin(parsed.Region, parsed.Value);
            }
            catch (BookInfoException)
            {
                return false;
            }
        }

        private static string Text(JsonElement element, string property, int maxLength = 512)
        {
            if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String)
            {
                var text = value.GetString();
                if (!string.IsNullOrWhiteSpace(text) && text.Length <= maxLength && !text.Any(char.IsControl))
                {
                    return text;
                }
            }

            throw new CustomJsonImportException($"Missing or invalid custom JSON field: {property}.");
        }

        private static int Integer(JsonElement element, string property)
        {
            if (element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number))
            {
                return number;
            }

            throw new CustomJsonImportException($"Missing or invalid custom JSON field: {property}.");
        }

        private static void RequireFields(JsonElement element, params string[] allowed)
        {
            Require(element.ValueKind == JsonValueKind.Object, "Each custom JSON list entry and page must be a JSON object.");
            var fields = new HashSet<string>(StringComparer.Ordinal);
            foreach (var field in element.EnumerateObject())
            {
                Require(allowed.Contains(field.Name, StringComparer.Ordinal) && fields.Add(field.Name), "The custom JSON document contains an unexpected or duplicate field.");
            }

            Require(fields.Count == allowed.Length, "The custom JSON document is missing a required field.");
        }

        private static void Require(bool condition, string message)
        {
            if (!condition)
            {
                throw new CustomJsonImportException(message);
            }
        }
    }
}
