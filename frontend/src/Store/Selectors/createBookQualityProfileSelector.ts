import { createSelector } from 'reselect';
import createBookAuthorSelector from './createBookAuthorSelector';
import type { StoreProfiles } from './selectorModels';

function createBookQualityProfileSelector() {
  return createSelector(
    (state: { settings: StoreProfiles }) => state.settings.qualityProfiles.items,
    createBookAuthorSelector(),
    (profiles, author) => {
      if (!author) { return {}; }
      return profiles.find((profile) => profile.id === author.qualityProfileId);
    }
  );
}
export default createBookQualityProfileSelector;
