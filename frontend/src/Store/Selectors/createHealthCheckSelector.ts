import { createSelector } from 'reselect';
import type { StoreHealth } from './selectorModels';

function createHealthCheckSelector() {
  return createSelector(
    (state: { system: { health: { items: readonly StoreHealth[] } } }) => state.system.health,
    (state: { app: { isConnected: boolean } }) => state.app,
    (health, app) => {
      const items = [...health.items];
      if (!app.isConnected) {
        items.push({ source: 'UI', type: 'warning', message: "Could not connect to SignalR, UI won't update" });
      }
      return items;
    }
  );
}
export default createHealthCheckSelector;
