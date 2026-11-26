import { isStateRecord, type StateRecord } from 'Store/StateTypes';

export type SettingsResource = StateRecord & { id: number };

export function parseSettingsObject(value: unknown): StateRecord {
  if (!isStateRecord(value)) {
    throw new TypeError('Invalid settings response');
  }
  return value;
}

export function parseSettingsList(value: unknown, minimumId = 1): SettingsResource[] {
  if (!Array.isArray(value) || !value.every((item): item is SettingsResource =>
    isStateRecord(item) && typeof item.id === 'number' && Number.isSafeInteger(item.id) && item.id >= minimumId
  ) || new Set(value.map((item) => item.id)).size !== value.length) {
    throw new TypeError('Invalid settings collection');
  }
  return value;
}

export function parseNamedOptions(value: unknown): SettingsResource[] {
  const options = parseSettingsList(value, Number.MIN_SAFE_INTEGER);
  if (!options.every((item) => typeof item.name === 'string')) {
    throw new TypeError('Invalid named options');
  }
  return options;
}
