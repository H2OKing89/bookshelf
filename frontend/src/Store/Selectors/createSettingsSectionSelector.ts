import { createSelector } from 'reselect';
import selectSettings from 'Store/Selectors/selectSettings';
import { isStateRecord, type LegacyRootState, type StateRecord } from 'Store/StateTypes';

const emptySection: StateRecord = {};

function createSettingsSectionSelector(section: string) {
  return createSelector(
    (state: LegacyRootState) => {
      const settings = state.settings?.[section];
      return isStateRecord(settings) ? settings : emptySection;
    },
    (sectionSettings) => {
      const {
        isFetching,
        isPopulated,
        error,
        item,
        pendingChanges,
        isSaving,
        saveError
      } = sectionSettings;

      return {
        isFetching: isFetching === true,
        isPopulated: isPopulated === true,
        error,
        isSaving: isSaving === true,
        saveError,
        ...selectSettings(item, pendingChanges, saveError)
      };
    }
  );
}

export default createSettingsSectionSelector;
