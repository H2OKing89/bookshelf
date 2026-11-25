using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.Books;
using NzbDrone.Core.ImportLists;
using NzbDrone.Core.Messaging.Commands;
using Readarr.Http;

namespace Readarr.Api.V1.Books
{
    [V1ApiController("book/editor")]
    public class BookEditorController : Controller
    {
        private readonly IBookService _bookService;
        private readonly IManageCommandQueue _commandQueueManager;
        private readonly IImportListPolicyRepository _listPolicy;

        public BookEditorController(IBookService bookService, IManageCommandQueue commandQueueManager, IImportListPolicyRepository listPolicy)
        {
            _bookService = bookService;
            _commandQueueManager = commandQueueManager;
            _listPolicy = listPolicy;
        }

        [HttpPut]
        public IActionResult SaveAll([FromBody] BookEditorResource resource)
        {
            var booksToUpdate = _bookService.GetBooks(resource.BookIds);

            if (resource.Monitored.HasValue)
            {
                // Validation above confirms every requested ID exists. Write only
                // the monitoring field, then reload so metadata and author
                // associations changed concurrently are not replaced by the draft.
                _listPolicy.ReleaseBookOwnership(resource.BookIds);
                _bookService.SetMonitored(resource.BookIds, resource.Monitored.Value);
                booksToUpdate = _bookService.GetBooks(resource.BookIds);
            }

            return Accepted(booksToUpdate.ToResource());
        }

        [HttpDelete]
        public void DeleteBook([FromBody] BookEditorResource resource)
        {
            foreach (var bookId in resource.BookIds)
            {
                _bookService.DeleteBook(bookId, resource.DeleteFiles ?? false, resource.AddImportListExclusion ?? false);
            }
        }
    }
}
