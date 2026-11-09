import migrateAddAuthorDefaults from './migrateAddAuthorDefaults';
import migrateAudiobookColumns from './migrateAudiobookColumns';
import migrateAuthorSortKey from './migrateAuthorSortKey';
import migrateBlacklistToBlocklist from './migrateBlacklistToBlocklist';

export default function migrate(persistedState) {
  migrateAddAuthorDefaults(persistedState);
  migrateAudiobookColumns(persistedState);
  migrateAuthorSortKey(persistedState);
  migrateBlacklistToBlocklist(persistedState);
}
