import { batchActions } from 'redux-batched-actions';
import bookEntities from 'Book/bookEntities';
import filterTypePredicates from 'Helpers/Props/filterTypePredicates';
import { all as filterTypes } from 'Helpers/Props/filterTypes';
import { asStateRecord, isStateRecord, type LegacyDispatch, type LegacyRootState, type StateRecord } from 'Store/StateTypes';
import { ApiError } from 'Utilities/Fetch/fetchJson';
import getSectionState from 'Utilities/State/getSectionState';
import { set, updateItem } from '../baseActions';
import { requestLegacy } from './legacyRequest';

export type LibraryRow = StateRecord & { id: number };

export function isLibraryId(value: unknown): value is number {
  return typeof value === 'number' && Number.isSafeInteger(value) && value > 0 && value <= 2147483647;
}

export function libraryIds(value: unknown): number[] | null {
  return Array.isArray(value) && value.length > 0 && value.every(isLibraryId) ? [...new Set<number>(value)] : null;
}

export function getRows(value: unknown): LibraryRow[] {
  return Array.isArray(value) ? value.filter((item: unknown): item is LibraryRow =>
    isStateRecord(item) && isLibraryId(item.id)
  ) : [];
}

export function sectionRows(getState: () => LegacyRootState, section: string): LibraryRow[] {
  return getRows(asStateRecord(getSectionState(getState(), section, true)).items);
}

export function parseExactRows<T extends { id: number }>(value: unknown, ids: readonly number[], parse: (value: unknown) => T): T[] {
  if (!Array.isArray(value)) {
    throw new TypeError('Invalid library collection');
  }
  const items = value.map(parse);
  if (items.length !== ids.length || new Set(items.map((item) => item.id)).size !== items.length ||
      !items.every((item) => ids.includes(item.id))) {
    throw new TypeError('Invalid library collection identity');
  }
  return items;
}

export function numberValue(value: unknown, fallback = 0) {
  return typeof value === 'number' && Number.isFinite(value) ? value : fallback;
}

export function textValue(value: unknown, fallback = '') {
  return typeof value === 'string' ? value : fallback;
}

export function dateValue(value: unknown) {
  return typeof value === 'string' || typeof value === 'number' || value instanceof Date ? value : undefined;
}

export function matchesFilter(type: string, value: unknown, filter: unknown) {
  const key = filterTypes.find((candidate) => candidate === type);
  const predicate = key === undefined ? undefined : filterTypePredicates[key];
  return predicate?.(value, filter) ?? false;
}

export function bookSection(value: unknown): string | null {
  if (value == null) {
    return bookEntities.BOOKS;
  }
  return typeof value === 'string' && Object.values(bookEntities).some((section) => section === value) ? value : null;
}

export function namedOptions(items: unknown, key: string, deduplicate = false) {
  const names = (Array.isArray(items) ? items.filter(isStateRecord) : []).flatMap((item) => Array.isArray(item[key]) ? item[key].filter((entry: unknown): entry is string => typeof entry === 'string') : []);
  return (deduplicate ? [...new Set(names)] : names).map((name) => ({ id: name, name }));
}

interface BulkEditorOptions {
  section: string;
  target: string;
  url: string;
  idKey: string;
  parse: (value: unknown) => { id: number };
}

export function createLibraryBulkEditor(options: BulkEditorOptions) {
  const { section, target, url, idKey, parse } = options;

  function perform(payload: StateRecord, dispatch: LegacyDispatch, method: 'PUT' | 'DELETE') {
    const saving = method === 'PUT';
    const activity = saving ? 'isSaving' : 'isDeleting';
    const errorKey = saving ? 'saveError' : 'deleteError';
    const ids = libraryIds(payload[idKey]);
    const fail = (error: unknown) => dispatch(set({ section, [activity]: false, [errorKey]: error }));
    if (!ids) {
      fail(new ApiError({ kind: 'invalid-request' }));
      return;
    }
    dispatch(set({ section, [activity]: true }));
    const { request } = requestLegacy({ url, method, data: JSON.stringify({ ...payload, [idKey]: ids }), dataType: 'json' });
    request.done((data) => {
      if (!saving) {
        // SignalR owns the actual collection removal.
        fail(null);
        return;
      }
      try {
        const rows = parseExactRows(data, ids, parse);
        dispatch(batchActions([
          ...rows.map((row) => updateItem({ ...row, section: target, updateOnly: true })),
          set({ section, [activity]: false, [errorKey]: null })
        ]));
      } catch {
        fail(new ApiError({ kind: 'invalid-response' }));
      }
    });
    request.fail(fail);
  }

  return {
    save(_getState: () => LegacyRootState, payload: StateRecord, dispatch: LegacyDispatch) {
      return perform(payload, dispatch, 'PUT');
    },
    remove(_getState: () => LegacyRootState, payload: StateRecord, dispatch: LegacyDispatch) {
      return perform(payload, dispatch, 'DELETE');
    }
  };
}
