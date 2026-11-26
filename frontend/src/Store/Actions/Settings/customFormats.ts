import { createAction } from 'redux-actions';
import createFetchHandler from 'Store/Actions/Creators/createFetchHandler';
import createRemoveItemHandler from 'Store/Actions/Creators/createRemoveItemHandler';
import createSaveProviderHandler from 'Store/Actions/Creators/createSaveProviderHandler';
import createSetSettingValueReducer from 'Store/Actions/Creators/Reducers/createSetSettingValueReducer';
import { asStateRecord, isStateRecord, type LegacyDispatch, type LegacyRootState, type StateRecord } from 'Store/StateTypes';
import { createThunk } from 'Store/thunks';
import { ApiError } from 'Utilities/Fetch/fetchJson';
import getSectionState from 'Utilities/State/getSectionState';
import { set } from '../baseActions';
import { type CloneSettingItem,cloneSettingsItem, createSettingValueAction } from './settingActions';

//
// Variables

const section = 'settings.customFormats';

//
// Actions Types

export const FETCH_CUSTOM_FORMATS = 'settings/customFormats/fetchCustomFormats';
export const SAVE_CUSTOM_FORMAT = 'settings/customFormats/saveCustomFormat';
export const DELETE_CUSTOM_FORMAT = 'settings/customFormats/deleteCustomFormat';
export const SET_CUSTOM_FORMAT_VALUE = 'settings/customFormats/setCustomFormatValue';
export const CLONE_CUSTOM_FORMAT = 'settings/customFormats/cloneCustomFormat';

//
// Action Creators

export const fetchCustomFormats = createThunk(FETCH_CUSTOM_FORMATS);
export const saveCustomFormat = createThunk(SAVE_CUSTOM_FORMAT);
export const deleteCustomFormat = createThunk(DELETE_CUSTOM_FORMAT);

export const setCustomFormatValue = createSettingValueAction(SET_CUSTOM_FORMAT_VALUE, section);

export const cloneCustomFormat = createAction<CloneSettingItem>(CLONE_CUSTOM_FORMAT);

//
// Details

export default {

  //
  // State

  defaultState: {
    isSchemaFetching: false,
    isSchemaPopulated: false,
    isFetching: false,
    isPopulated: false,
    schema: {
      includeCustomFormatWhenRenaming: false
    },
    error: null,
    isDeleting: false,
    deleteError: null,
    isSaving: false,
    saveError: null,
    items: [],
    pendingChanges: {}
  },

  //
  // Action Handlers

  actionHandlers: {
    [FETCH_CUSTOM_FORMATS]: createFetchHandler(section, '/customformat'),

    [DELETE_CUSTOM_FORMAT]: createRemoveItemHandler(section, '/customformat'),

    [SAVE_CUSTOM_FORMAT]: (getState: () => LegacyRootState, payload: StateRecord, dispatch: LegacyDispatch) => {
      const state = getState();
      const current = asStateRecord(getSectionState(state, section, true));
      const draft = asStateRecord(getSectionState(state, 'settings.customFormatSpecifications', true));
      if (draft.isPopulated !== true || !Array.isArray(draft.items) || !draft.items.every(isStateRecord)) {
        dispatch(set({ section, isSaving: false, saveError: new ApiError({ kind: 'invalid-request' }) }));
        return;
      }
      // Draft specifications belong to this save only. Never mutate the
      // previous Redux draft or its independently editable collection.
      const pendingChanges = { ...asStateRecord(current.pendingChanges), specifications: [...draft.items] };
      dispatch(set({ section, pendingChanges }));
      createSaveProviderHandler(section, '/customformat')(getState, payload, dispatch);
    }
  },

  //
  // Reducers

  reducers: {
    [SET_CUSTOM_FORMAT_VALUE]: createSetSettingValueReducer(section),

    [CLONE_CUSTOM_FORMAT]: (state: StateRecord, { payload }: { payload: unknown }) => cloneSettingsItem(state, section, payload)
  }

};
