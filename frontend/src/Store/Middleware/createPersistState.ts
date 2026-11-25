import _ from 'lodash';
import { initializeQueuePreferences } from 'Activity/Queue/queuePreferences';
import actions from 'Store/Actions';
import migrate from 'Store/Migrators/migrate';
import { isStateRecord, LegacyRootState, StateRecord } from 'Store/StateTypes';
import mergeColumnPreferences from './mergeColumnPreferences';
import { getPreferenceStorage, isPreferenceRecord, readPreferenceStorage, writePreferenceStorage } from './preferenceStorage';

const key = 'readarr';
const paths = actions.flatMap((action) => 'persistState' in action ? action.persistState ?? [] : []);

interface Column {
  name: string;
  isVisible: boolean;
  isModifiable?: boolean;
}

function isColumn(value: unknown): value is Column {
  return isStateRecord(value) && typeof value.name === 'string' && typeof value.isVisible === 'boolean';
}

// Preferences, not server resources, are persisted. An obsolete or malformed
// saved value cannot replace the current shape or insert unregistered keys
// into an options object. Filter keys are intentionally strings or IDs.
function mergePreference(current: unknown, saved: unknown, path: string): unknown {
  if (saved === undefined) {
    return current;
  }
  if (path.endsWith('.columns') && Array.isArray(current) && current.every(isColumn)) {
    return mergeColumnPreferences(current, saved);
  }
  if (path.endsWith('.selectedFilterKey')) {
    return saved === null || typeof saved === 'string' || (typeof saved === 'number' && Number.isFinite(saved)) ?
      saved : current;
  }
  if (Array.isArray(current)) {
    return Array.isArray(saved) ? saved : current;
  }
  if (isPreferenceRecord(current)) {
    if (!isPreferenceRecord(saved)) {
      return current;
    }
    return Object.fromEntries(Object.entries(current).map(([name, value]) => [
      name,
      mergePreference(value, saved[name], `${path}.${name}`)
    ]));
  }
  if (current === null || current === undefined) {
    return saved === null || ['boolean', 'number', 'string'].includes(typeof saved) ? saved : current;
  }
  return typeof current === typeof saved ? saved : current;
}

export default function createPersistState(initialState: LegacyRootState) {
  const storage = getPreferenceStorage();
  const storageState = readPreferenceStorage(storage, key);
  const persistedState = isPreferenceRecord(storageState) ? storageState : {};
  const legacyQueue = persistedState.queue;

  initializeQueuePreferences(legacyQueue);
  migrate(persistedState);

  const computedState: LegacyRootState = { ...initialState };

  paths.forEach((path) => {
    const current: unknown = _.get(initialState, path);
    const saved: unknown = _.get(persistedState, path);
    if (current !== undefined && saved !== undefined) {
      // The paths are supplied by application-owned slice registrations.
      // Clone the parent before setting, to leave defaults untouched.
      const parts = path.split('.');
      for (let depth = 1; depth < parts.length; depth++) {
        const parentPath = parts.slice(0, depth).join('.');
        const parent: unknown = _.get(computedState, parentPath);
        if (isStateRecord(parent)) {
          _.set(computedState, parentPath, { ...parent });
        }
      }
      _.set(computedState, path, mergePreference(current, saved, path));
    }
  });

  return {
    initialState: computedState,
    attach(store: { getState(): LegacyRootState; subscribe(listener: () => void): () => void }) {
      return store.subscribe(() => {
        const preferences: StateRecord = _.pick(store.getState(), paths);
        // Keep the pre-upgrade Queue preferences available to an older build.
        if (isPreferenceRecord(legacyQueue)) {
          preferences.queue = legacyQueue;
        }
        writePreferenceStorage(storage, key, preferences, 2);
      });
    }
  };
}
