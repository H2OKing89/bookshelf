import type { SelectedState, SelectionFlags } from './selectionTypes';

export default function areAllSelected(selectedState: SelectedState): SelectionFlags {
  let allSelected = true;
  let allUnselected = true;

  Object.keys(selectedState).forEach((key) => {
    if (selectedState[key]) {
      allUnselected = false;
    } else {
      allSelected = false;
    }
  });

  return {
    allSelected,
    allUnselected
  };
}
