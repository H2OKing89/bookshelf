export interface CheckboxSelectionChange {
  name: string;
  value: boolean;
  shiftKey?: boolean;
}

export interface TableSelectionChange<Id extends string | number> {
  id: Id;
  value: boolean | null;
  shiftKey?: boolean;
}

export interface VirtualSelectionChange<Id extends string | number> {
  id: Id;
  value: boolean;
  shiftKey?: boolean;
}

export function getSelectAllValue(allSelected: boolean, allUnselected: boolean): boolean | null {
  if (allSelected) {
    return true;
  }
  return allUnselected ? false : null;
}
