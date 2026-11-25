import type { SelectedState } from './selectionTypes';

export default function getSelectedIds(selectedState: SelectedState, options?: { parseIds?: true }): number[];
export default function getSelectedIds(selectedState: SelectedState, options: { parseIds: false }): string[];
export default function getSelectedIds(selectedState: SelectedState, options: { parseIds: boolean }): Array<string | number>;
export default function getSelectedIds(selectedState: SelectedState, { parseIds = true }: { parseIds?: boolean } = {}): Array<string | number> {
  const ids: Array<string | number> = [];
  for (const [id, selected] of Object.entries(selectedState)) {
    if (selected) {
      if (!parseIds) {
        ids.push(id);
      } else if (/^-?\d+$/.test(id)) {
        const numericId = Number(id);
        if (Number.isSafeInteger(numericId)) {
          ids.push(numericId);
        }
      }
    }
  }
  return ids;
}
