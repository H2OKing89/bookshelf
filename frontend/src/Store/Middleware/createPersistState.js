import _ from 'lodash';
import persistState from 'redux-localstorage';
import { initializeQueuePreferences } from 'Activity/Queue/queuePreferences';
import actions from 'Store/Actions';
import migrate from 'Store/Migrators/migrate';
import mergeColumnPreferences from './mergeColumnPreferences';
import { getPreferenceStorage, isPreferenceRecord, readPreferenceStorage } from './preferenceStorage';

const key = 'readarr';
const paths = actions.flatMap((action) => action.persistState || []);
const columnPaths = paths.filter((path) => path.endsWith('.columns'));

function merge(initialState, persistedState) {
  const computedState = _.merge({}, initialState, persistedState);

  columnPaths.forEach((path) => {
    const current = _.get(initialState, path);
    const saved = _.get(persistedState, path);
    _.set(computedState, path, mergeColumnPreferences(current, saved));
  });

  return computedState;
}

export default function createPersistState() {
  const storageState = readPreferenceStorage(getPreferenceStorage(), key);
  const persistedState = isPreferenceRecord(storageState) ? storageState : {};
  const legacyQueue = persistedState.queue;

  initializeQueuePreferences(legacyQueue);
  migrate(persistedState);

  return persistState(paths, {
    key,
    merge,
    deserialize: () => _.pick(persistedState, paths),
    slicer: (paths_) => (state) => {
      const preferences = _.pick(state, paths_);
      // Keep the pre-upgrade Queue preferences available to an older build.
      if (isPreferenceRecord(legacyQueue)) {
        preferences.queue = legacyQueue;
      }
      return preferences;
    },
    serialize: (state) => JSON.stringify(state, null, 2)
  });
}
