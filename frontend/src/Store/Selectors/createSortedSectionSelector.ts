import { createSelector } from 'reselect';
import { asStateRecord, type LegacyRootState } from 'Store/StateTypes';
import getSectionState from 'Utilities/State/getSectionState';

function createSortedSectionSelector(section: string, comparer: (a: unknown, b: unknown) => number) {
  return createSelector(
    (state: LegacyRootState) => getSectionState(state, section, true),
    (sectionState) => {
      const value = asStateRecord(sectionState);
      const items: unknown[] = Array.isArray(value.items) ? value.items : [];
      return { ...value, items: [...items].sort(comparer) };
    }
  );
}
export default createSortedSectionSelector;
