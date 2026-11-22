export function getPreferenceStorage(): Storage | undefined {
  try {
    return window.localStorage;
  } catch {
    return undefined;
  }
}

export function readPreferenceStorage(storage: Storage | undefined, key: string): unknown {
  try {
    const value = storage?.getItem(key);
    return value == null ? undefined : JSON.parse(value);
  } catch {
    return undefined;
  }
}

export function writePreferenceStorage(storage: Storage | undefined, key: string, value: unknown) {
  try {
    storage?.setItem(key, JSON.stringify(value));
  } catch {
    // Preferences can still be changed for the current session.
  }
}

export function isPreferenceRecord(value: unknown): value is Record<string, unknown> {
  return value !== null && typeof value === 'object' && !Array.isArray(value);
}
