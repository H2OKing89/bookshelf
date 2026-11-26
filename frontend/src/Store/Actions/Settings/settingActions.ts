import _ from 'lodash';
import { createAction } from 'redux-actions';
import type { SortDirection } from 'Helpers/Props/sortDirections';
import { getItemId, isItemId, isStateRecord, type ItemId, type StateRecord } from 'Store/StateTypes';
import getSectionState from 'Utilities/State/getSectionState';
import updateSectionState from 'Utilities/State/updateSectionState';

export interface SettingValue {
  name: string;
  value: unknown;
}

export interface ProviderSchemaSelection {
  implementation: string;
  presetName?: string | null;
}

export interface ProviderSortOptions {
  sortKey?: string;
  sortDirection?: SortDirection;
}

export interface CloneSettingItem {
  id: ItemId;
}

export function createSettingValueAction(type: string, section: string) {
  return createAction<SettingValue & { section: string }, SettingValue>(type, ({ name, value }) => ({ section, name, value }));
}

export function providerName(item: unknown) {
  return isStateRecord(item) && typeof item.name === 'string' ? item.name.toLowerCase() : '';
}

export function cloneSettingsItem(state: StateRecord, section: string, payload: unknown, provider = false) {
  if (!isStateRecord(payload) || !isItemId(payload.id)) {
    return state;
  }
  const sectionState = getSectionState(state, section);
  const items: unknown[] = Array.isArray(sectionState.items) ? sectionState.items : [];
  const item = items.find((value) => getItemId(value) === payload.id);
  if (!isStateRecord(item) || typeof item.name !== 'string') {
    return state;
  }
  const copied = _.cloneDeep(item);
  delete copied.id;
  const name = `${item.name} - Copy`;
  if (!provider) {
    return updateSectionState(state, section, { ...sectionState, pendingChanges: { ...copied, name } });
  }

  // Clone an implementation for a new provider, not its persisted identity or
  // credentials. A malformed field array cannot be used as a draft schema.
  if (!Array.isArray(copied.fields) || !copied.fields.every(isStateRecord)) {
    return state;
  }
  const fields = copied.fields.map((field) => field.privacy === 'apiKey' || field.privacy === 'password' ? { ...field, value: '' } : field);
  delete copied.name;
  return updateSectionState(state, section, {
    ...sectionState,
    selectedSchema: { ...copied, fields },
    pendingChanges: { name }
  });
}
