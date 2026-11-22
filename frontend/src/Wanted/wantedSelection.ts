import type { BookId, BookIdentity } from 'Book/BookIdentity';

interface WantedSelection {
  readonly scope: string | null;
  readonly bookIds: readonly BookId[];
  readonly selectedIds: ReadonlySet<BookId>;
  readonly anchor: BookId | null;
}

export const emptyWantedSelection: WantedSelection = {
  scope: null,
  bookIds: [],
  selectedIds: new Set(),
  anchor: null
};

export function reconcileWantedSelection(
  selection: WantedSelection,
  scope: string | null,
  eligibleBooks: readonly Pick<BookIdentity, 'id'>[]
): WantedSelection {
  // Previous results can be visible while a new page loads. Only confirmed
  // rows may decide which selected books still belong to the page.
  if (scope === null || (scope === selection.scope &&
      eligibleBooks.length === selection.bookIds.length &&
      eligibleBooks.every(({ id }, index) => id === selection.bookIds[index]))) {
    return selection;
  }

  const bookIds = eligibleBooks.map(({ id }) => id);

  return {
    scope,
    bookIds,
    selectedIds: new Set(bookIds.filter((id) => selection.selectedIds.has(id))),
    anchor: null
  };
}

export function selectAllWantedBooks(selection: WantedSelection, selected: boolean): WantedSelection {
  return {
    ...selection,
    selectedIds: new Set(selected ? selection.bookIds : []),
    anchor: null
  };
}

export function toggleWantedBook(
  selection: WantedSelection,
  id: BookId,
  selected: boolean | null | undefined,
  shiftKey: boolean
): WantedSelection {
  const index = selection.bookIds.indexOf(id);
  if (index === -1 || selected == null || selection.selectedIds.has(id) === selected) {
    return selection;
  }

  const anchor = shiftKey && selection.anchor !== null ? selection.bookIds.indexOf(selection.anchor) : -1;
  const first = anchor < 0 ? index : Math.min(anchor, index);
  const last = anchor < 0 ? index : Math.max(anchor, index);
  const selectedIds = new Set(selection.selectedIds);

  for (const bookId of selection.bookIds.slice(first, last + 1)) {
    if (selected) {
      selectedIds.add(bookId);
    } else {
      selectedIds.delete(bookId);
    }
  }

  return { ...selection, selectedIds, anchor: id };
}
