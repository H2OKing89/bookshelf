import areAllSelected from './areAllSelected';
import getToggledRange from './getToggledRange';
import type { SelectionId, SelectionState } from './selectionTypes';

export default function toggleSelected<Id extends SelectionId>(state: SelectionState<Id>, items: readonly { id: Id }[], id: Id, selected: boolean | null | undefined, shiftKey: boolean) {
  const { lastToggled } = state;
  const selectedState = { ...state.selectedState, [id]: selected };
  if (selected == null) {
    delete selectedState[id];
  } else if (shiftKey && lastToggled != null) {
    const range = getToggledRange(items, id, lastToggled);
    if (range) {
      for (let index = range.lower; index < range.upper; index++) {
        selectedState[items[index].id] = selected;
      }
    }
  }
  return { ...areAllSelected(selectedState), lastToggled: id, selectedState };
}
