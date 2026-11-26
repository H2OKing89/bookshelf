import { createSelector } from 'reselect';
import createAuthorSelector from './createAuthorSelector';
import type { StoreProfiles } from './selectorModels';

function createAuthorMetadataProfileSelector() {
  return createSelector(
    (state: { settings: StoreProfiles }) => state.settings.metadataProfiles.items,
    createAuthorSelector(),
    (profiles, author) => {
      if (!author) { return undefined; }
      return profiles.find((profile) => profile.id === author.metadataProfileId);
    }
  );
}
export default createAuthorMetadataProfileSelector;
