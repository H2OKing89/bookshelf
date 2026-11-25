import { isStateRecord, type StateRecord } from 'Store/StateTypes';
import getSectionState from 'Utilities/State/getSectionState';

const emptySection: StateRecord = {};

export function sectionRecord(state: StateRecord, path?: string): StateRecord {
  const section = path === undefined ? undefined : getSectionState(state, path, true);
  return isStateRecord(section) ? section : emptySection;
}

export function recordItems(value: unknown): StateRecord[] {
  const items: unknown[] = Array.isArray(value) ? value : [];
  return items.filter(isStateRecord);
}
