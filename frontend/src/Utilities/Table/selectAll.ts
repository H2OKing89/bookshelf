import type { SelectedState } from './selectionTypes';

export default function selectAll(selectedState: SelectedState, selected: boolean) {
  const newSelectedState = Object.fromEntries(Object.keys(selectedState).map((id) => [id, selected]));
  return { allSelected: selected, allUnselected: !selected, lastToggled: null, selectedState: newSelectedState };
}
