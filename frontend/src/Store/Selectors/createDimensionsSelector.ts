import type { StoreDimensions } from './selectorModels';
function createDimensionsSelector() {
  return (state: { app: { dimensions: StoreDimensions } }) => state.app.dimensions;
}
export default createDimensionsSelector;
