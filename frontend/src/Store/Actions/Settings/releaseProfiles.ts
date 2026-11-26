import createFetchHandler from 'Store/Actions/Creators/createFetchHandler';
import createRemoveItemHandler from 'Store/Actions/Creators/createRemoveItemHandler';
import createSaveProviderHandler from 'Store/Actions/Creators/createSaveProviderHandler';
import createSetSettingValueReducer from 'Store/Actions/Creators/Reducers/createSetSettingValueReducer';
import { createThunk } from 'Store/thunks';
import { createSettingValueAction } from './settingActions';
import { parseSettingsList } from './settingsResponses';

//
// Variables

const section = 'settings.releaseProfiles';

//
// Actions Types

export const FETCH_RELEASE_PROFILES = 'settings/releaseProfiles/fetchReleaseProfiles';
export const SAVE_RELEASE_PROFILE = 'settings/releaseProfiles/saveReleaseProfile';
export const DELETE_RELEASE_PROFILE = 'settings/releaseProfiles/deleteReleaseProfile';
export const SET_RELEASE_PROFILE_VALUE = 'settings/releaseProfiles/setReleaseProfileValue';

//
// Action Creators

export const fetchReleaseProfiles = createThunk(FETCH_RELEASE_PROFILES);
export const saveReleaseProfile = createThunk(SAVE_RELEASE_PROFILE);
export const deleteReleaseProfile = createThunk(DELETE_RELEASE_PROFILE);

export const setReleaseProfileValue = createSettingValueAction(SET_RELEASE_PROFILE_VALUE, section);

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
    [FETCH_RELEASE_PROFILES]: createFetchHandler(section, '/releaseprofile', (data) => parseSettingsList(data)),

    [SAVE_RELEASE_PROFILE]: createSaveProviderHandler(section, '/releaseprofile'),

    [DELETE_RELEASE_PROFILE]: createRemoveItemHandler(section, '/releaseprofile')
  },

  //
  // Reducers

  reducers: {
    [SET_RELEASE_PROFILE_VALUE]: createSetSettingValueReducer(section)
  }

};
