import { isValidElement } from 'react';
import type { BookFileId } from 'Book/BookIdentity';
import { type LibraryBookFile,parseLibraryBookFile } from 'Book/LibraryModels';
import type Column from 'Components/Table/Column';
import type { SortDirection } from 'Helpers/Props/sortDirections';
import { asStateRecord, type StateRecord } from 'Store/StateTypes';
import type { SelectedState } from 'Utilities/Table/selectionTypes';

export interface QualityChoice extends StateRecord {
  id: number;
  name: string;
}

export function qualityChoices(value: unknown): QualityChoice[] {
  if (!Array.isArray(value)) {
    return [];
  }
  return value.flatMap((source: unknown) => {
    const item = asStateRecord(source);
    const quality = asStateRecord(item.quality);

    if (typeof quality.id === 'number' && typeof quality.name === 'string') {
      return [{ ...quality, id: quality.id, name: quality.name }];
    }

    return qualityChoices(item.items);
  });
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

export function directionValue(value: unknown): SortDirection | undefined {
  return value === 'ascending' || value === 'descending' ? value : undefined;
}

export function editorColumns(value: unknown): Column[] {
  return Array.isArray(value) ? value.flatMap((item: unknown) => {
    const column = asStateRecord(item);
    if (typeof column.name !== 'string') {
      return [];
    }
    return [{
      name: column.name,
      label: labelValue(column.label),
      className: typeof column.className === 'string' ? column.className : undefined,
      isVisible: column.isVisible === true,
      isSortable: typeof column.isSortable === 'boolean' ? column.isSortable : undefined,
      isModifiable: typeof column.isModifiable === 'boolean' ? column.isModifiable : undefined,
      fixedSortDirection: directionValue(column.fixedSortDirection)
    }];
  }) : [];
}

export function editorFiles(value: readonly unknown[]): LibraryBookFile[] {
  return value.flatMap((entry) => {
    try {
      return [parseLibraryBookFile(entry)];
    } catch {
      // Do not offer destructive operations on malformed file associations.
      return [];
    }
  });
}

export function selectedFileIds(items: readonly { id: BookFileId }[], selected: SelectedState): BookFileId[] {
  return items.filter((item) => selected[item.id] === true).map((item) => item.id);
}
