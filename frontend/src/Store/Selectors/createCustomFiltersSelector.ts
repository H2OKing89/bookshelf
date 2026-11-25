import { createSelector } from 'reselect';
import type { StateRecord } from 'Store/StateTypes';
import { recordItems, sectionRecord } from './collectionProjection';

function createCustomFiltersSelector(type: string, alternateType?: string) {
  return createSelector(
    (state: StateRecord) => sectionRecord(state, 'customFilters').items,
    (items) => recordItems(items).filter((customFilter) =>
      customFilter.type === type || (alternateType !== undefined && customFilter.type === alternateType)
    )
  );
}

export default createCustomFiltersSelector;
