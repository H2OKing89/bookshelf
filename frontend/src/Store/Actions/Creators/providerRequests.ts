import _ from 'lodash';
import { asStateRecord, isItemId, isStateRecord, type ItemId, type LegacyRootState, type StateRecord } from 'Store/StateTypes';
import getProviderState from 'Utilities/State/getProviderState';
import getSectionState from 'Utilities/State/getSectionState';

export function isAborted(value: unknown) {
  return isStateRecord(value) && value.aborted === true;
}

export function getDraft(getState: () => LegacyRootState, section: string) {
  return asStateRecord(asStateRecord(getSectionState(getState(), section, true)).pendingChanges);
}

// Clear only draft values that were actually submitted and are still unchanged.
// Fields are independently editable; preserve later changes without retaining
// the whole submitted draft (including credentials) after a successful save.
export function remainingDraft(submitted: StateRecord, current: StateRecord): StateRecord {
  const result: StateRecord = {};
  for (const [name, value] of Object.entries(current)) {
    if (name === 'fields' && isStateRecord(value)) {
      const remainingFields = Object.fromEntries(Object.entries(value).filter(([field, fieldValue]) =>
        !Object.hasOwn(asStateRecord(submitted.fields), field) ||
        !_.isEqual(asStateRecord(submitted.fields)[field], fieldValue)
      ));
      if (Object.keys(remainingFields).length) {
        result.fields = remainingFields;
      }
    } else if (!Object.hasOwn(submitted, name) || !_.isEqual(submitted[name], value)) {
      result[name] = value;
    }
  }
  return result;
}

export function createProviderSaveData(
  id: unknown,
  payload: StateRecord,
  getState: () => LegacyRootState,
  section: string
): { data: StateRecord | StateRecord[]; ids: ItemId[]; method: 'PUT' | 'POST' } | null {
  if (Array.isArray(id)) {
    if (id.length === 0 || !id.every(isItemId) || id.some((value) => value === 0) || new Set(id.map(String)).size !== id.length) {
      return null;
    }
    const data = id.map((value) => getProviderState({ ...payload, id: value }, getState, section));
    if (data.some((item, index) => item.id !== id[index])) {
      return null;
    }
    return {
      data,
      ids: id,
      method: 'PUT'
    };
  }
  // The existing editors represent a new provider by a missing ID or zero.
  if (id == null || id === 0) {
    return { data: getProviderState({ ...payload, id }, getState, section), ids: [], method: 'POST' };
  }
  if (!isItemId(id)) {
    return null;
  }
  const data = getProviderState({ ...payload, id }, getState, section);
  return data.id === id ? { data, ids: [id], method: 'PUT' } : null;
}

export type ProviderItem = StateRecord & { id: ItemId };

export function parseProviderSaveResponse(
  response: unknown,
  ids: ItemId[],
  removeStale: boolean,
  bulk: boolean
): ProviderItem[] | null {
  const items: unknown[] = Array.isArray(response) ? response : [response];
  if (!items.every((item): item is ProviderItem => isStateRecord(item) && isItemId(item.id) && item.id !== 0)) {
    return null;
  }
  if (new Set(items.map((item) => String(item.id))).size !== items.length) {
    return null;
  }
  if (!bulk && items.length !== 1) {
    return null;
  }
  if (ids.length && (items.some((item) => !ids.some((id) => String(id) === String(item.id))) ||
      ((!bulk || !removeStale) && items.length !== ids.length))) {
    return null;
  }
  return items;
}
