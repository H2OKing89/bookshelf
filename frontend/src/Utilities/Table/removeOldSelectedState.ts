import areAllSelected from './areAllSelected';
import type { SelectionId, SelectionState } from './selectionTypes';

export default function removeOldSelectedState<Id extends SelectionId>(state: SelectionState<Id>, prevItems: readonly { id: Id }[]) {
  const selectedState = { ...state.selectedState };
  for (const item of prevItems) {
    delete selectedState[item.id];
  }
  return { ...areAllSelected(selectedState), selectedState };
}
