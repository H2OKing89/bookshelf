import { createSelector } from 'reselect';
import createAuthorSelector from './createAuthorSelector';
import type { StoreProfiles } from './selectorModels';

function createAuthorQualityProfileSelector() {
  return createSelector(
    (state: { settings: StoreProfiles }) => state.settings.qualityProfiles.items,
    createAuthorSelector(),
    (profiles, author) => {
      if (!author) { return undefined; }
      return profiles.find((profile) => profile.id === author.qualityProfileId);
    }
  );
}
export default createAuthorQualityProfileSelector;
