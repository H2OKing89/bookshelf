import createFetchHandler from 'Store/Actions/Creators/createFetchHandler';
import createSaveProviderHandler from 'Store/Actions/Creators/createSaveProviderHandler';
import createSetProviderFieldValueReducer from 'Store/Actions/Creators/Reducers/createSetProviderFieldValueReducer';
import createSetSettingValueReducer from 'Store/Actions/Creators/Reducers/createSetSettingValueReducer';
import { createThunk } from 'Store/thunks';
import { createSettingValueAction } from './settingActions';

//
// Variables

const section = 'settings.metadata';

//
// Actions Types

export const FETCH_METADATA = 'settings/metadata/fetchMetadata';
export const SET_METADATA_VALUE = 'settings/metadata/setMetadataValue';
export const SET_METADATA_FIELD_VALUE = 'settings/metadata/setMetadataFieldValue';
export const SAVE_METADATA = 'settings/metadata/saveMetadata';

//
// Action Creators

export const fetchMetadata = createThunk(FETCH_METADATA);
export const saveMetadata = createThunk(SAVE_METADATA);

export const setMetadataValue = createSettingValueAction(SET_METADATA_VALUE, section);

export const setMetadataFieldValue = createSettingValueAction(SET_METADATA_FIELD_VALUE, section);

//
// Details

export default {

  //
  // State

  defaultState: {
    isFetching: false,
    isPopulated: false,
    error: null,
    isSaving: false,
    saveError: null,
    items: [],
    pendingChanges: {}
  },

  //
  // Action Handlers

  actionHandlers: {
    [FETCH_METADATA]: createFetchHandler(section, '/metadata'),
    [SAVE_METADATA]: createSaveProviderHandler(section, '/metadata')
  },

  //
  // Reducers

  reducers: {
    [SET_METADATA_VALUE]: createSetSettingValueReducer(section),
    [SET_METADATA_FIELD_VALUE]: createSetProviderFieldValueReducer(section)
  }

};
