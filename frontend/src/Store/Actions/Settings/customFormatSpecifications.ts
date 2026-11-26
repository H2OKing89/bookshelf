import _ from 'lodash';
import { createAction } from 'redux-actions';
import { batchActions } from 'redux-batched-actions';
import createFetchSchemaHandler from 'Store/Actions/Creators/createFetchSchemaHandler';
import createClearReducer from 'Store/Actions/Creators/Reducers/createClearReducer';
import createSetProviderFieldValueReducer from 'Store/Actions/Creators/Reducers/createSetProviderFieldValueReducer';
import createSetSettingValueReducer from 'Store/Actions/Creators/Reducers/createSetSettingValueReducer';
import { asStateRecord, createItemMap, findItemIndex, isItemId, isStateRecord, type LegacyDispatch, type LegacyRootState, type StateRecord } from 'Store/StateTypes';
import { createThunk } from 'Store/thunks';
import { ApiError } from 'Utilities/Fetch/fetchJson';
import getNextId from 'Utilities/State/getNextId';
import getProviderState from 'Utilities/State/getProviderState';
import getSectionState from 'Utilities/State/getSectionState';
import selectProviderSchema from 'Utilities/State/selectProviderSchema';
import updateSectionState from 'Utilities/State/updateSectionState';
import { removeItem, set, update, updateItem } from '../baseActions';
import { type CloneSettingItem, createSettingValueAction, type ProviderSchemaSelection } from './settingActions';

//
// Variables

const section = 'settings.customFormatSpecifications';

//
// Actions Types

export const FETCH_CUSTOM_FORMAT_SPECIFICATIONS = 'settings/customFormatSpecifications/fetchCustomFormatSpecifications';
export const FETCH_CUSTOM_FORMAT_SPECIFICATION_SCHEMA = 'settings/customFormatSpecifications/fetchCustomFormatSpecificationSchema';
export const SELECT_CUSTOM_FORMAT_SPECIFICATION_SCHEMA = 'settings/customFormatSpecifications/selectCustomFormatSpecificationSchema';
export const SET_CUSTOM_FORMAT_SPECIFICATION_VALUE = 'settings/customFormatSpecifications/setCustomFormatSpecificationValue';
export const SET_CUSTOM_FORMAT_SPECIFICATION_FIELD_VALUE = 'settings/customFormatSpecifications/setCustomFormatSpecificationFieldValue';
export const SAVE_CUSTOM_FORMAT_SPECIFICATION = 'settings/customFormatSpecifications/saveCustomFormatSpecification';
export const DELETE_CUSTOM_FORMAT_SPECIFICATION = 'settings/customFormatSpecifications/deleteCustomFormatSpecification';
export const DELETE_ALL_CUSTOM_FORMAT_SPECIFICATION = 'settings/customFormatSpecifications/deleteAllCustomFormatSpecification';
export const CLONE_CUSTOM_FORMAT_SPECIFICATION = 'settings/customFormatSpecifications/cloneCustomFormatSpecification';
export const CLEAR_CUSTOM_FORMAT_SPECIFICATIONS = 'settings/customFormatSpecifications/clearCustomFormatSpecifications';
export const CLEAR_CUSTOM_FORMAT_SPECIFICATION_PENDING = 'settings/customFormatSpecifications/clearCustomFormatSpecificationPending';
//
// Action Creators

export const fetchCustomFormatSpecifications = createThunk(FETCH_CUSTOM_FORMAT_SPECIFICATIONS);
export const fetchCustomFormatSpecificationSchema = createThunk(FETCH_CUSTOM_FORMAT_SPECIFICATION_SCHEMA);
export const selectCustomFormatSpecificationSchema = createAction<ProviderSchemaSelection>(SELECT_CUSTOM_FORMAT_SPECIFICATION_SCHEMA);

export const saveCustomFormatSpecification = createThunk(SAVE_CUSTOM_FORMAT_SPECIFICATION);
export const deleteCustomFormatSpecification = createThunk(DELETE_CUSTOM_FORMAT_SPECIFICATION);
export const deleteAllCustomFormatSpecification = createThunk(DELETE_ALL_CUSTOM_FORMAT_SPECIFICATION);

export const setCustomFormatSpecificationValue = createSettingValueAction(SET_CUSTOM_FORMAT_SPECIFICATION_VALUE, section);

export const setCustomFormatSpecificationFieldValue = createSettingValueAction(SET_CUSTOM_FORMAT_SPECIFICATION_FIELD_VALUE, section);

export const cloneCustomFormatSpecification = createAction<CloneSettingItem>(CLONE_CUSTOM_FORMAT_SPECIFICATION);

export const clearCustomFormatSpecification = createAction(CLEAR_CUSTOM_FORMAT_SPECIFICATIONS);

export const clearCustomFormatSpecificationPending = createThunk(CLEAR_CUSTOM_FORMAT_SPECIFICATION_PENDING);

//
// Details

export default {

  //
  // State

  defaultState: {
    isPopulated: false,
    error: null,
    isSchemaFetching: false,
    isSchemaPopulated: false,
    schemaError: null,
    schema: [],
    selectedSchema: {},
    isSaving: false,
    saveError: null,
    items: [],
    pendingChanges: {}
  },

  //
  // Action Handlers

  actionHandlers: {
    [FETCH_CUSTOM_FORMAT_SPECIFICATION_SCHEMA]: createFetchSchemaHandler(section, '/customformat/schema'),

    [FETCH_CUSTOM_FORMAT_SPECIFICATIONS]: (getState: () => LegacyRootState, payload: StateRecord, dispatch: LegacyDispatch) => {
      const { id } = payload;
      let specifications: StateRecord[] = [];
      if (id != null && id !== 0) {
        const formats = asStateRecord(getSectionState(getState(), 'settings.customFormats', true));
        const items: unknown[] = Array.isArray(formats.items) ? formats.items : [];
        const format = isItemId(id) ? items[findItemIndex(items, formats.itemMap, id)] : undefined;
        if (!isStateRecord(format) || !Array.isArray(format.specifications) || !format.specifications.every(isStateRecord)) {
          dispatch(batchActions([
            update({ section, data: [] }),
            set({ section, isPopulated: false, error: new ApiError({ kind: 'invalid-response' }) })
          ]));
          return;
        }
        // The editor owns temporary identities. Persisted specifications often
        // contain zero or no ID; never allow them to collide in the draft.
        specifications = format.specifications.map((spec, index) => ({ ..._.cloneDeep(spec), id: index + 1 }));
      }
      dispatch(batchActions([update({ section, data: specifications }), set({ section, isPopulated: true, error: null })]));
    },

    [SAVE_CUSTOM_FORMAT_SPECIFICATION]: (getState: () => LegacyRootState, payload: StateRecord, dispatch: LegacyDispatch) => {
      const { id, ...otherPayload } = payload;
      if (id != null && id !== 0 && !isItemId(id)) {
        return false;
      }
      const saveData = getProviderState({ id, ...otherPayload }, getState, section, false);
      const items = asStateRecord(getSectionState(getState(), section, true)).items;
      const newId = id == null || id === 0 ? getNextId(Array.isArray(items) ? items : []) : id;
      if (!isItemId(newId) || typeof saveData.implementation !== 'string' || (id && saveData.id !== id)) {
        return false;
      }
      dispatch(batchActions([
        updateItem({ ...saveData, section, id: newId }),
        set({ section, pendingChanges: {}, saveError: null })
      ]));
      return true;
    },

    [DELETE_CUSTOM_FORMAT_SPECIFICATION]: (_getState: () => LegacyRootState, payload: StateRecord, dispatch: LegacyDispatch) => {
      if (isItemId(payload.id)) {
        dispatch(removeItem({ section, id: payload.id }));
      }
    },

    [DELETE_ALL_CUSTOM_FORMAT_SPECIFICATION]: (_getState: () => LegacyRootState, _payload: StateRecord, dispatch: LegacyDispatch) => {
      dispatch(update({ section, data: [] }));
    },

    [CLEAR_CUSTOM_FORMAT_SPECIFICATION_PENDING]: (_getState: () => LegacyRootState, _payload: StateRecord, dispatch: LegacyDispatch) => {
      dispatch(set({ section, pendingChanges: {}, saveError: null }));
    }
  },

  //
  // Reducers

  reducers: {
    [SET_CUSTOM_FORMAT_SPECIFICATION_VALUE]: createSetSettingValueReducer(section),
    [SET_CUSTOM_FORMAT_SPECIFICATION_FIELD_VALUE]: createSetProviderFieldValueReducer(section),

    [SELECT_CUSTOM_FORMAT_SPECIFICATION_SCHEMA]: (state: StateRecord, { payload }: { payload: unknown }) => selectProviderSchema(state, section, payload),

    [CLONE_CUSTOM_FORMAT_SPECIFICATION]: (state: StateRecord, { payload }: { payload: unknown }) => {
      const sectionState = getSectionState(state, section);
      const items: unknown[] = Array.isArray(sectionState.items) ? sectionState.items : [];
      const id = asStateRecord(payload).id;
      const item = isItemId(id) ? items[findItemIndex(items, sectionState.itemMap, id)] : undefined;
      const newId = getNextId(items);
      if (!isStateRecord(item) || typeof item.name !== 'string' || newId === undefined) {
        return state;
      }
      const updated = [...items, { ..._.cloneDeep(item), id: newId, name: `${item.name} - Copy` }];
      return updateSectionState(state, section, { ...sectionState, items: updated, itemMap: createItemMap(updated) });
    },

    [CLEAR_CUSTOM_FORMAT_SPECIFICATIONS]: createClearReducer(section, {
      isPopulated: false,
      error: null,
      items: [],
      itemMap: {},
      pendingChanges: {}
    })
  }
};
