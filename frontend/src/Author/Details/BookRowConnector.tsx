/* eslint max-params: 0 */
import { connect } from 'react-redux';
import { createSelector } from 'reselect';
import type { BookId } from 'Book/BookIdentity';
import type { LibraryBookFile } from 'Book/LibraryModels';
import createAuthorSelector from 'Store/Selectors/createAuthorSelector';
import type { ResourceCollection } from 'Store/Selectors/selectById';
import BookRow from './BookRow';

type RowFile = LibraryBookFile & { indexerFlags?: number | null };

export function groupBookFiles(items: readonly RowFile[]): Map<BookId, RowFile[]> {
  const result = new Map<BookId, RowFile[]>();
  for (const file of items) {
    const bookId = file.bookId;
    if (bookId) {
      const grouped = result.get(bookId);
      if (grouped) {
        grouped.push(file);
      } else {
        result.set(bookId, [file]);
      }
    }
  }
  return result;
}

const selectBookFiles = createSelector(
  (state: { bookFiles: ResourceCollection<RowFile> }) => state.bookFiles.items,
  groupBookFiles
);

function createMapStateToProps() {
  return createSelector(
    createAuthorSelector(),
    selectBookFiles,
    (_state: unknown, { id }: { id: BookId }) => id,
    (author, bookFiles, bookId) => {
      const files = bookFiles.get(bookId) ?? [];
      return {
        authorMonitored: author?.monitored === true,
        authorName: author?.authorName ?? '',
        bookFiles: files,
        indexerFlags: files[0]?.indexerFlags ?? 0
      };
    }
  );
}

export default connect(createMapStateToProps)(BookRow);
