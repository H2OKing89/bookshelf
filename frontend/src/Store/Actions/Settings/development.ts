import createFetchHandler from 'Store/Actions/Creators/createFetchHandler';
import createSaveHandler from 'Store/Actions/Creators/createSaveHandler';
import createSetSettingValueReducer from 'Store/Actions/Creators/Reducers/createSetSettingValueReducer';
import { createThunk } from 'Store/thunks';
import { createSettingValueAction } from './settingActions';
import { parseSettingsObject } from './settingsResponses';

//
// Variables

const section = 'settings.development';

//
// Actions Types

export const FETCH_DEVELOPMENT_SETTINGS = 'settings/development/fetchDevelopmentSettings';
export const SET_DEVELOPMENT_SETTINGS_VALUE = 'settings/development/setDevelopmentSettingsValue';
export const SAVE_DEVELOPMENT_SETTINGS = 'settings/development/saveDevelopmentSettings';

//
// Action Creators

export const fetchDevelopmentSettings = createThunk(FETCH_DEVELOPMENT_SETTINGS);
export const saveDevelopmentSettings = createThunk(SAVE_DEVELOPMENT_SETTINGS);
export const setDevelopmentSettingsValue = createSettingValueAction(SET_DEVELOPMENT_SETTINGS_VALUE, section);

//
// Details

export default {

  //
  // State

  defaultState: {
    isFetching: false,
    isPopulated: false,
    error: null,
    pendingChanges: {},
    isSaving: false,
    saveError: null,
    item: {}
  },

  //
  // Action Handlers

  actionHandlers: {
    [FETCH_DEVELOPMENT_SETTINGS]: createFetchHandler(section, '/config/development', parseSettingsObject),
    [SAVE_DEVELOPMENT_SETTINGS]: createSaveHandler(section, '/config/development')
  },

  //
  // Reducers

  reducers: {
    [SET_DEVELOPMENT_SETTINGS_VALUE]: createSetSettingValueReducer(section)
  }

};
