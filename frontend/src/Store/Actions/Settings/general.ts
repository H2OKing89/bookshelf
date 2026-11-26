import createFetchHandler from 'Store/Actions/Creators/createFetchHandler';
import createSaveHandler from 'Store/Actions/Creators/createSaveHandler';
import createSetSettingValueReducer from 'Store/Actions/Creators/Reducers/createSetSettingValueReducer';
import { createThunk } from 'Store/thunks';
import { createSettingValueAction } from './settingActions';

//
// Variables

const section = 'settings.general';

//
// Actions Types

export const FETCH_GENERAL_SETTINGS = 'settings/general/fetchGeneralSettings';
export const SET_GENERAL_SETTINGS_VALUE = 'settings/general/setGeneralSettingsValue';
export const SAVE_GENERAL_SETTINGS = 'settings/general/saveGeneralSettings';

//
// Action Creators

export const fetchGeneralSettings = createThunk(FETCH_GENERAL_SETTINGS);
export const saveGeneralSettings = createThunk(SAVE_GENERAL_SETTINGS);
export const setGeneralSettingsValue = createSettingValueAction(SET_GENERAL_SETTINGS_VALUE, section);

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
    [FETCH_GENERAL_SETTINGS]: createFetchHandler(section, '/config/host'),
    [SAVE_GENERAL_SETTINGS]: createSaveHandler(section, '/config/host')
  },

  //
  // Reducers

  reducers: {
    [SET_GENERAL_SETTINGS_VALUE]: createSetSettingValueReducer(section)
  }

};
