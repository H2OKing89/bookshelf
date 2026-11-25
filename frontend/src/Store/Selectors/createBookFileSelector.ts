import { createSelector } from 'reselect';
import { BookFileId } from 'Book/BookIdentity';
import { LibraryBookFile } from 'Book/LibraryModels';
import selectById, { ResourceCollection } from './selectById';

function createBookFileSelector() {
  return createSelector(
    (state: { bookFiles: ResourceCollection<LibraryBookFile> }) => state.bookFiles,
    (_state: unknown, { bookFileId }: { bookFileId: BookFileId | 0 | null | undefined }) => bookFileId,
    selectById
  );
}

export default createBookFileSelector;
