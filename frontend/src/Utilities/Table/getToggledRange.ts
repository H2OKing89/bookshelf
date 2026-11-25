import type { SelectionId } from './selectionTypes';

export interface SelectionRange {
  lower: number;
  upper: number;
}

export default function getToggledRange<Id extends SelectionId>(items: readonly { id: Id }[], id: Id, lastToggled: Id): SelectionRange | null {
  const lastToggledIndex = items.findIndex((item) => item.id === lastToggled);
  const changedIndex = items.findIndex((item) => item.id === id);
  // Filters, pagination and live updates can invalidate the previous anchor.
  // A stale range must not index a missing row or select unrelated records.
  if (lastToggledIndex < 0 || changedIndex < 0) {
    return null;
  }
  return lastToggledIndex > changedIndex ?
    { lower: changedIndex, upper: lastToggledIndex + 1 } :
    { lower: lastToggledIndex, upper: changedIndex };
}
