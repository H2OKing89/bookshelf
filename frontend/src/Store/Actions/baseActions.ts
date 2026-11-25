import { createAction } from 'redux-actions';
import { ItemId, StateRecord } from 'Store/StateTypes';

interface SectionPayload extends StateRecord {
  section: string;
}

interface ItemPayload extends SectionPayload {
  id: ItemId;
}

//
// Action Types

export const SET = 'base/set';

export const UPDATE = 'base/update';
export const UPDATE_ITEM = 'base/updateItem';
export const UPDATE_SERVER_SIDE_COLLECTION = 'base/updateServerSideCollection';

export const SET_SETTING_VALUE = 'base/setSettingValue';
export const CLEAR_PENDING_CHANGES = 'base/clearPendingChanges';

export const REMOVE_ITEM = 'base/removeItem';

//
// Action Creators

export const set = createAction<SectionPayload>(SET);

export const update = createAction<SectionPayload & { data: unknown }>(UPDATE);
export const updateItem = createAction<ItemPayload & { updateOnly?: boolean }>(UPDATE_ITEM);
export const updateServerSideCollection = createAction<SectionPayload & { data: unknown }>(UPDATE_SERVER_SIDE_COLLECTION);

export const setSettingValue = createAction<SectionPayload & { name: string; value: unknown }>(SET_SETTING_VALUE);
export const clearPendingChanges = createAction<SectionPayload>(CLEAR_PENDING_CHANGES);

export const removeItem = createAction<ItemPayload>(REMOVE_ITEM);
