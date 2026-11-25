import _ from 'lodash';
import { handleActions } from 'redux-actions';
import {
  CLEAR_PENDING_CHANGES,
  REMOVE_ITEM,
  SET,
  UPDATE,
  UPDATE_ITEM,
  UPDATE_SERVER_SIDE_COLLECTION
} from 'Store/Actions/baseActions';
import {
  asStateRecord,
  createItemMap,
  findItemIndex,
  getItemId,
  isItemId,
  isStateRecord,
  StateRecord
} from 'Store/StateTypes';
import getSectionState from 'Utilities/State/getSectionState';
import updateSectionState from 'Utilities/State/updateSectionState';

type BaseHandler = (state: StateRecord, payload: StateRecord) => StateRecord;
type SectionHandler = (state: StateRecord | undefined, action: { type: string; payload: unknown }) => StateRecord;

const baseHandlers: Record<string, BaseHandler> = {
  [SET]: (state, payload) => ({ ...state, ..._.omit(payload, ['section', 'id']) }),

  [UPDATE]: (state, payload) => {
    const data: unknown = payload.data;

    if (Array.isArray(data)) {
      return { ...state, items: data, itemMap: createItemMap(data) };
    }

    return { ...state, item: data };
  },

  [UPDATE_ITEM]: (state, payload) => {
    const { updateOnly = false } = payload;
    const otherProps = _.omit(payload, ['section', 'updateOnly']);
    const { id } = otherProps;
    const items: readonly unknown[] = Array.isArray(state.items) ? state.items : [];

    if (!isItemId(id)) {
      return state;
    }

    const index = findItemIndex(items, state.itemMap, id);

    if (index < 0 && updateOnly) {
      return state;
    }

    if (index >= 0) {
      const item = items[index];
      const newItem = { ...asStateRecord(item), ...otherProps };
      // Avoid unnecessary subscription/selector work for identical updates.
      if (_.isEqual(item, newItem)) {
        return state;
      }

      const updated = [...items];
      updated[index] = newItem;
      // Recalculate only if the previous index was absent or stale.
      const itemMap = asStateRecord(state.itemMap);
      return {
        ...state,
        items: updated,
        itemMap: itemMap[id] === index ? itemMap : createItemMap(updated)
      };
    }

    const updated = [...items, otherProps];
    return { ...state, items: updated, itemMap: createItemMap(updated) };
  },

  [CLEAR_PENDING_CHANGES]: (state) => ({
    ...state,
    pendingChanges: {},
    ...(Object.hasOwn(state, 'saveError') ? { saveError: null } : {})
  }),

  [REMOVE_ITEM]: (state, payload) => {
    const items: readonly unknown[] = Array.isArray(state.items) ? state.items : [];
    if (!isItemId(payload.id)) {
      return state;
    }

    const updated = items.filter((item) => getItemId(item) !== payload.id);
    return { ...state, items: updated, itemMap: createItemMap(updated) };
  },

  [UPDATE_SERVER_SIDE_COLLECTION]: (state, payload) => {
    const data = payload.data;
    if (!isStateRecord(data)) {
      return state;
    }

    const { records, totalRecords, pageSize, ...rest } = data;
    if (!Array.isArray(records) ||
        typeof totalRecords !== 'number' || !Number.isSafeInteger(totalRecords) || totalRecords < 0 ||
        typeof pageSize !== 'number' || !Number.isSafeInteger(pageSize) || pageSize <= 0) {
      return state;
    }

    return {
      ...state,
      ...rest,
      pageSize,
      totalRecords,
      totalPages: Math.max(Math.ceil(totalRecords / pageSize), 1),
      items: records,
      itemMap: createItemMap(records)
    };
  }
};

export default function createHandleActions(
  handlers: Record<string, SectionHandler>,
  defaultState: StateRecord,
  section: string
) {
  const scopedHandlers = Object.fromEntries(Object.entries(baseHandlers).map(([type, handler]) => [
    type,
    (state: StateRecord = defaultState, { payload }: { payload: unknown }) => {
      if (!isStateRecord(payload) || typeof payload.section !== 'string' ||
          payload.section.split('.')[0] !== section) {
        return state;
      }

      const current = getSectionState(state, payload.section);
      const next = handler(current, payload);
      return current === next ? state : updateSectionState(state, payload.section, next);
    }
  ]));

  return handleActions<StateRecord, unknown>({ ...scopedHandlers, ...handlers }, defaultState);
}
