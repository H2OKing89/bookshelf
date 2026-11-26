import { createSelector } from 'reselect';
import type { StoreProfiles } from './selectorModels';

export function createQualityProfileSelectorForHook(qualityProfileId: number | null | undefined) {
  return createSelector(
    (state: { settings: StoreProfiles }) => state.settings.qualityProfiles.items,
    (profiles) => profiles.find((profile) => profile.id === qualityProfileId)
  );
}
function createQualityProfileSelector() {
  return createSelector(
    (_state: unknown, { qualityProfileId }: { qualityProfileId?: number | null }) => qualityProfileId,
    (state: { settings: StoreProfiles }) => state.settings.qualityProfiles.items,
    (qualityProfileId, profiles) => profiles.find((profile) => profile.id === qualityProfileId)
  );
}
export default createQualityProfileSelector;
