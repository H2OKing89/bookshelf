import _ from 'lodash';
import { createSelector } from 'reselect';
import type { StateRecord } from 'Store/StateTypes';
import createAllAuthorsSelector from './createAllAuthorsSelector';

function createProfileInUseSelector(profileProp: string) {
  return createSelector(
    (_state: unknown, { id }: { id: number | null | undefined }) => id,
    createAllAuthorsSelector(),
    (state: { settings: { importLists: { items: readonly StateRecord[] } } }) => state.settings.importLists.items,
    (id, authors, lists) => !!id && (_.some(authors, { [profileProp]: id }) || _.some(lists, { [profileProp]: id }))
  );
}
export default createProfileInUseSelector;
