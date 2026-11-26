import { createSelector } from 'reselect';
import type { StoreProfiles } from './selectorModels';

export function createMetadataProfileSelectorForHook(metadataProfileId: number | null | undefined) {
  return createSelector(
    (state: { settings: StoreProfiles }) => state.settings.metadataProfiles.items,
    (profiles) => profiles.find((profile) => profile.id === metadataProfileId)
  );
}
function createMetadataProfileSelector() {
  return createSelector(
    (_state: unknown, { metadataProfileId }: { metadataProfileId?: number | null }) => metadataProfileId,
    (state: { settings: StoreProfiles }) => state.settings.metadataProfiles.items,
    (metadataProfileId, profiles) => profiles.find((profile) => profile.id === metadataProfileId)
  );
}
export default createMetadataProfileSelector;
