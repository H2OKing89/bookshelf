export type SelectionId = string | number;
export type SelectedState = Readonly<Record<string, boolean | null | undefined>>;
export interface SelectionState<Id extends SelectionId = SelectionId> {
  selectedState: SelectedState;
  lastToggled?: Id | null;
}
export interface SelectionFlags {
  allSelected: boolean;
  allUnselected: boolean;
}
