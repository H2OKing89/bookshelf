export default function createWantedSelectionState(items, selectionScope) {
  return {
    selectionScope,
    allSelected: false,
    allUnselected: true,
    lastToggled: null,
    selectedState: Object.fromEntries(items.map(({ id }) => [id, false]))
  };
}
