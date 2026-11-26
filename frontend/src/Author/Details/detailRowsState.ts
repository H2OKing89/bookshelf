import _ from 'lodash';
import { isValidElement } from 'react';
import { type AuthorId, type BookId,parseResourceId } from 'Book/BookIdentity';
import { detailStatistics, optionalNumber, optionalText, textList } from 'Book/Details/detailPresentation';
import type Column from 'Components/Table/Column';
import type { SortDirection } from 'Helpers/Props/sortDirections';
import { asStateRecord, type StateRecord } from 'Store/StateTypes';
import getToggledRange from 'Utilities/Table/getToggledRange';
import type { BookRowProps } from './BookRow';

export interface SeriesItem {
  id: number;
  title: string;
  links: { bookId: BookId; position: string | number | null }[];
}

type RowFields = Omit<BookRowProps, 'authorName' | 'authorMonitored' | 'bookFiles' | 'indexerFlags' | 'columns' | 'isEditorActive' | 'isSelected' | 'onSelectedChange' | 'onMonitorBookPress'>;
export type DetailBookRow = RowFields & StateRecord & { statistics: ReturnType<typeof detailStatistics> };
export type PositionMap = Record<number, string | number | null>;

export function detailBookRows(value: unknown, authorId: AuthorId): DetailBookRow[] {
  return Array.isArray(value) ? value.flatMap((entry: unknown) => {
    const book = asStateRecord(entry);
    if (typeof book.id !== 'number' || !Number.isSafeInteger(book.id) || book.id <= 0) {
      return [];
    }
    const id = parseResourceId(book.id, 'book');
    const rowAuthorId = typeof book.authorId === 'number' && Number.isSafeInteger(book.authorId) && book.authorId > 0 ?
      parseResourceId(book.authorId, 'author') : authorId;
    const ratings = asStateRecord(book.ratings);
    return [{
      ...book,
      id,
      authorId: rowAuthorId,
      monitored: book.monitored === true,
      title: optionalText(book.title) ?? '',
      titleSlug: optionalText(book.titleSlug) ?? '',
      displayTitle: optionalText(book.displayTitle),
      foreignBookId: optionalText(book.foreignBookId),
      foreignEditionId: optionalText(book.foreignEditionId),
      releaseDate: optionalText(book.releaseDate),
      seriesTitle: optionalText(book.seriesTitle),
      pageCount: optionalNumber(book.pageCount),
      runtimeLengthMinutes: optionalNumber(book.runtimeLengthMinutes),
      narrators: textList(book.narrators),
      ratings: { value: optionalNumber(ratings.value) ?? 0, votes: optionalNumber(ratings.votes) ?? 0 },
      isSaving: book.isSaving === true,
      statistics: detailStatistics(book.statistics)
    }];
  }) : [];
}

export function seriesRows(value: unknown): SeriesItem[] {
  return Array.isArray(value) ? value.flatMap((entry: unknown) => {
    const series = asStateRecord(entry);
    if (typeof series.id !== 'number' || !Number.isSafeInteger(series.id) || series.id <= 0 || typeof series.title !== 'string') {
      return [];
    }
    const links = Array.isArray(series.links) ? series.links.flatMap((value: unknown) => {
      const link = asStateRecord(value);
      if (typeof link.bookId !== 'number' || !Number.isSafeInteger(link.bookId) || link.bookId <= 0) {
        return [];
      }
      return [{
        bookId: parseResourceId(link.bookId, 'book'),
        position: typeof link.position === 'string' || typeof link.position === 'number' ? link.position : null
      }];
    }) : [];
    return [{ id: series.id, title: series.title, links }];
  }) : [];
}

function labelValue(value: unknown): Column['label'] {
  if (typeof value === 'string' || isValidElement(value)) {
    return value;
  }
  if (typeof value === 'function') {
    return () => {
      const label: unknown = value();
      return typeof label === 'string' ? label : '';
    };
  }
  return undefined;
}

function columnLabel(value: unknown): Column['columnLabel'] {
  if (typeof value === 'string') {
    return value;
  }
  if (typeof value === 'function') {
    return () => {
      const label: unknown = value();
      return typeof label === 'string' ? label : '';
    };
  }
  return undefined;
}

export function sortDirection(value: unknown): SortDirection | undefined {
  return value === 'ascending' || value === 'descending' ? value : undefined;
}

export function detailColumns(value: unknown): Column[] {
  return Array.isArray(value) ? value.flatMap((entry: unknown) => {
    const column = asStateRecord(entry);
    if (typeof column.name !== 'string') {
      return [];
    }
    return [{
      name: column.name,
      label: labelValue(column.label),
      columnLabel: columnLabel(column.columnLabel),
      className: optionalText(column.className) ?? undefined,
      isVisible: column.isVisible === true,
      isSortable: typeof column.isSortable === 'boolean' ? column.isSortable : undefined,
      isModifiable: typeof column.isModifiable === 'boolean' ? column.isModifiable : undefined,
      fixedSortDirection: sortDirection(column.fixedSortDirection)
    }];
  }) : [];
}

export function sortSeriesBooks(items: DetailBookRow[], positions: PositionMap, key: string | undefined, direction: SortDirection | undefined) {
  if (key === 'position') {
    return [...items].sort((a, b) => {
      const comparison = String(positions[a.id] ?? '').localeCompare(String(positions[b.id] ?? ''), undefined, { numeric: true, sensitivity: 'base' });
      return direction === 'descending' ? -comparison : comparison;
    });
  }
  return key ? _.orderBy(items, key, direction === 'descending' ? 'desc' : 'asc') : items;
}

export function monitorRange(items: readonly { id: BookId }[], id: BookId, anchor: BookId | null, shift: boolean | undefined) {
  const ids = [id];
  const range = shift && anchor ? getToggledRange(items, id, anchor) : null;
  if (range) {
    for (let index = range.lower; index < range.upper; index++) {
      ids.push(items[index].id);
    }
  }
  return [...new Set(ids)];
}
