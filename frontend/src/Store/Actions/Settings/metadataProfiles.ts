import { createAction } from 'redux-actions';
import createFetchHandler from 'Store/Actions/Creators/createFetchHandler';
import createFetchSchemaHandler from 'Store/Actions/Creators/createFetchSchemaHandler';
import createRemoveItemHandler from 'Store/Actions/Creators/createRemoveItemHandler';
import createSaveProviderHandler from 'Store/Actions/Creators/createSaveProviderHandler';
import createSetSettingValueReducer from 'Store/Actions/Creators/Reducers/createSetSettingValueReducer';
import type { StateRecord } from 'Store/StateTypes';
import { createThunk } from 'Store/thunks';
import { type CloneSettingItem, cloneSettingsItem,createSettingValueAction } from './settingActions';

//
// Variables

const section = 'settings.metadataProfiles';

//
// Actions Types

export const FETCH_METADATA_PROFILES = 'settings/metadataProfiles/fetchMetadataProfiles';
export const FETCH_METADATA_PROFILE_SCHEMA = 'settings/metadataProfiles/fetchMetadataProfileSchema';
export const SAVE_METADATA_PROFILE = 'settings/metadataProfiles/saveMetadataProfile';
export const DELETE_METADATA_PROFILE = 'settings/metadataProfiles/deleteMetadataProfile';
export const SET_METADATA_PROFILE_VALUE = 'settings/metadataProfiles/setMetadataProfileValue';
export const CLONE_METADATA_PROFILE = 'settings/metadataProfiles/cloneMetadataProfile';

//
// Action Creators

export const fetchMetadataProfiles = createThunk(FETCH_METADATA_PROFILES);
export const fetchMetadataProfileSchema = createThunk(FETCH_METADATA_PROFILE_SCHEMA);
export const saveMetadataProfile = createThunk(SAVE_METADATA_PROFILE);
export const deleteMetadataProfile = createThunk(DELETE_METADATA_PROFILE);

export const setMetadataProfileValue = createSettingValueAction(SET_METADATA_PROFILE_VALUE, section);

export const cloneMetadataProfile = createAction<CloneSettingItem>(CLONE_METADATA_PROFILE);

//
// Details

export default {

  //
  // State

  defaultState: {
    isFetching: false,
    isPopulated: false,
    error: null,
    isDeleting: false,
    deleteError: null,
    isSchemaFetching: false,
    isSchemaPopulated: false,
    schemaError: null,
    schema: {},
    isSaving: false,
    saveError: null,
    items: [],
    pendingChanges: {}
  },

  //
  // Action Handlers

  actionHandlers: {
    [FETCH_METADATA_PROFILES]: createFetchHandler(section, '/metadataprofile'),
    [FETCH_METADATA_PROFILE_SCHEMA]: createFetchSchemaHandler(section, '/metadataprofile/schema'),
    [SAVE_METADATA_PROFILE]: createSaveProviderHandler(section, '/metadataprofile'),
    [DELETE_METADATA_PROFILE]: createRemoveItemHandler(section, '/metadataprofile')
  },

  //
  // Reducers

  reducers: {
    [SET_METADATA_PROFILE_VALUE]: createSetSettingValueReducer(section),

    [CLONE_METADATA_PROFILE]: (state: StateRecord, { payload }: { payload: unknown }) => cloneSettingsItem(state, section, payload)
  }

};
