import { asStateRecord, isStateRecord } from 'Store/StateTypes';
import { createThunk, handleThunks } from 'Store/thunks';
import { ApiError } from 'Utilities/Fetch/fetchJson';
import { set, update } from './baseActions';
import createFetchHandler from './Creators/createFetchHandler';
import createHandleActions from './Creators/createHandleActions';
import createRemoveItemHandler from './Creators/createRemoveItemHandler';
import { requestLegacy } from './Creators/legacyRequest';

//
// Variables

export const section = 'tags';

//
// State

export const defaultState = {
  isFetching: false,
  isPopulated: false,
  error: null,
  items: [],

  details: {
    isFetching: false,
    isPopulated: false,
    error: null,
    items: []
  }
};

//
// Actions Types

export const FETCH_TAGS = 'tags/fetchTags';
export const ADD_TAG = 'tags/addTag';
export const DELETE_TAG = 'tags/deleteTag';
export const FETCH_TAG_DETAILS = 'tags/fetchTagDetails';

//
// Action Creators

export const fetchTags = createThunk(FETCH_TAGS);
export const addTag = createThunk(ADD_TAG);
export const deleteTag = createThunk(DELETE_TAG);
export const fetchTagDetails = createThunk(FETCH_TAG_DETAILS);

//
// Action Handlers

export const actionHandlers = handleThunks({
  [FETCH_TAGS]: createFetchHandler(section, '/tag'),

  [ADD_TAG]: function(getState, payload, dispatch) {
    const tag = asStateRecord(payload.tag);
    if (typeof tag.label !== 'string' || !tag.label.trim()) {
      dispatch(set({ section, error: new ApiError({ kind: 'invalid-request' }) }));
      return;
    }

    const promise = requestLegacy({
      url: '/tag',
      method: 'POST',
      data: JSON.stringify(tag),
      dataType: 'json'
    }).request;

    promise.done((data) => {
      if (!isStateRecord(data) || typeof data.id !== 'number' || !Number.isSafeInteger(data.id) || data.id <= 0 || typeof data.label !== 'string') {
        dispatch(set({ section, error: new ApiError({ kind: 'invalid-response' }) }));
        return;
      }

      const items = getState().tags.items;
      const tags = Array.isArray(items) ? items.filter((item: unknown) => asStateRecord(item).id !== data.id) : [];
      dispatch(update({ section, data: [...tags, data] }));
      if (typeof payload.onTagCreated === 'function') {
        payload.onTagCreated(data);
      }
    });
    promise.fail((error) => dispatch(set({ section, error })));

  },

  [DELETE_TAG]: createRemoveItemHandler(section, '/tag'),
  [FETCH_TAG_DETAILS]: createFetchHandler('tags.details', '/tag/detail')

});

//
// Reducers
export const reducers = createHandleActions({}, defaultState, section);
