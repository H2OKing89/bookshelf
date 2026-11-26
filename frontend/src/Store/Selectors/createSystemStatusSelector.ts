import { createSelector } from 'reselect';
import type { StatusState } from './selectorModels';

function createSystemStatusSelector() {
  return createSelector((state: StatusState) => state.system.status, (status) => status.item);
}
export default createSystemStatusSelector;
