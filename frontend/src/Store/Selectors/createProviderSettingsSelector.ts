import { createSelector } from 'reselect';
import selectSettings from 'Store/Selectors/selectSettings';
import { asStateRecord, isStateRecord, type ItemId, type LegacyRootState, type StateRecord } from 'Store/StateTypes';

const emptySection: StateRecord = {};

function createProviderSettingsSelector(sectionName: string) {
  return createSelector(
    (_state: LegacyRootState, { id }: { id?: ItemId | null }) => id,
    (state: LegacyRootState) => {
      const section = state.settings?.[sectionName];
      return isStateRecord(section) ? section : emptySection;
    },
    (id, section) => {
      const isExisting = !!id;
      const items: unknown[] = Array.isArray(section.items) ? section.items : [];
      const item = isExisting ?
        items.find((candidate) => isStateRecord(candidate) && candidate.id === id) :
        { name: '', ...asStateRecord(Array.isArray(section.schema) ? section.selectedSchema : section.schema) };
      const settings = selectSettings(item, section.pendingChanges, section.saveError);

      return {
        isFetching: (isExisting ? section.isFetching : section.isSchemaFetching) === true,
        isPopulated: (isExisting ? section.isPopulated : section.isSchemaPopulated) === true,
        error: isExisting ? section.error : section.schemaError,
        isSaving: section.isSaving === true,
        saveError: section.saveError,
        isTesting: section.isTesting === true,
        ...settings,
        item: settings.settings
      };
    }
  );
}

export default createProviderSettingsSelector;
