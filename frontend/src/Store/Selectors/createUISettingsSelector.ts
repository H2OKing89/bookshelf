import { createSelector } from 'reselect';
import type { UiState } from './selectorModels';

function createUISettingsSelector() {
  return createSelector((state: UiState) => state.settings.ui, (ui) => ui.item);
}
export default createUISettingsSelector;
