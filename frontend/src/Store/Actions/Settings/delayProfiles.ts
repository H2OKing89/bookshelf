import { update } from 'Store/Actions/baseActions';
import createFetchHandler from 'Store/Actions/Creators/createFetchHandler';
import createFetchSchemaHandler from 'Store/Actions/Creators/createFetchSchemaHandler';
import createRemoveItemHandler from 'Store/Actions/Creators/createRemoveItemHandler';
import createSaveProviderHandler from 'Store/Actions/Creators/createSaveProviderHandler';
import { requestLegacy } from 'Store/Actions/Creators/legacyRequest';
import createSetSettingValueReducer from 'Store/Actions/Creators/Reducers/createSetSettingValueReducer';
import { asStateRecord, isStateRecord } from 'Store/StateTypes';
import { createThunk, type ThunkHandler } from 'Store/thunks';
import getSectionState from 'Utilities/State/getSectionState';
import { createSettingValueAction } from './settingActions';
import { parseSettingsList } from './settingsResponses';

//
// Variables

const section = 'settings.delayProfiles';

//
// Actions Types

export const FETCH_DELAY_PROFILES = 'settings/delayProfiles/fetchDelayProfiles';
export const FETCH_DELAY_PROFILE_SCHEMA = 'settings/delayProfiles/fetchDelayProfileSchema';
export const SAVE_DELAY_PROFILE = 'settings/delayProfiles/saveDelayProfile';
export const DELETE_DELAY_PROFILE = 'settings/delayProfiles/deleteDelayProfile';
export const REORDER_DELAY_PROFILE = 'settings/delayProfiles/reorderDelayProfile';
export const SET_DELAY_PROFILE_VALUE = 'settings/delayProfiles/setDelayProfileValue';

//
// Action Creators

export const fetchDelayProfiles = createThunk(FETCH_DELAY_PROFILES);
export const fetchDelayProfileSchema = createThunk(FETCH_DELAY_PROFILE_SCHEMA);
export const saveDelayProfile = createThunk(SAVE_DELAY_PROFILE);
export const deleteDelayProfile = createThunk(DELETE_DELAY_PROFILE);
export const reorderDelayProfile = createThunk(REORDER_DELAY_PROFILE);

export const setDelayProfileValue = createSettingValueAction(SET_DELAY_PROFILE_VALUE, section);

const reorder: ThunkHandler = (getState, payload, dispatch) => {
  const { id, moveIndex } = payload;
  if (typeof id !== 'number' || !Number.isSafeInteger(id) || id <= 0 ||
      typeof moveIndex !== 'number' || !Number.isSafeInteger(moveIndex) || moveIndex < 0) {
    return;
  }
  const delayProfiles = asStateRecord(getSectionState(getState(), section, true));
  const items: unknown[] = Array.isArray(delayProfiles.items) ? delayProfiles.items : [];
  const profile = items.find((item) => isStateRecord(item) && item.id === id);
  if (!isStateRecord(profile) || profile.order === moveIndex + 1) {
    return;
  }
  const after = moveIndex > 0 ? items.find((item) => isStateRecord(item) && item.order === moveIndex) : null;
  if (moveIndex > 0 && (!isStateRecord(after) || typeof after.id !== 'number' || !Number.isSafeInteger(after.id))) {
    return;
  }
  const afterQueryParam = isStateRecord(after) ? `afterId=${after.id}` : '';
  const { request } = requestLegacy({ method: 'PUT', url: `/delayprofile/reorder/${id}?${afterQueryParam}` });
  request.done((response) => {
    try {
      const data = parseSettingsList(response);
      if (data.every((item) => typeof item.order === 'number' && Number.isSafeInteger(item.order))) {
        dispatch(update({ section, data }));
      }
    } catch {
      // Do not replace the collection with a malformed reorder response.
    }
  });
};

//
// Details

export default {

  //
  // State

  defaultState: {
    isFetching: false,
    isPopulated: false,
    error: null,
    items: [],
    isSaving: false,
    saveError: null,
    pendingChanges: {}
  },

  //
  // Action Handlers

  actionHandlers: {
    [FETCH_DELAY_PROFILES]: createFetchHandler(section, '/delayprofile', (data) => parseSettingsList(data)),
    [FETCH_DELAY_PROFILE_SCHEMA]: createFetchSchemaHandler(section, '/delayprofile/schema'),

    [SAVE_DELAY_PROFILE]: createSaveProviderHandler(section, '/delayprofile'),
    [DELETE_DELAY_PROFILE]: createRemoveItemHandler(section, '/delayprofile'),

    [REORDER_DELAY_PROFILE]: reorder
  },

  //
  // Reducers

  reducers: {
    [SET_DELAY_PROFILE_VALUE]: createSetSettingValueReducer(section)
  }

};
