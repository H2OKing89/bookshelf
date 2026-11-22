import _ from 'lodash';
import React from 'react';
import { createAction } from 'redux-actions';
import Icon from 'Components/Icon';
import { icons, sortDirections } from 'Helpers/Props';
import { createThunk, handleThunks } from 'Store/thunks';
import translate from 'Utilities/String/translate';
import { set } from './baseActions';
import createFetchHandler from './Creators/createFetchHandler';
import createHandleActions from './Creators/createHandleActions';
import createClearReducer from './Creators/Reducers/createClearReducer';
import createSetClientSideCollectionSortReducer from './Creators/Reducers/createSetClientSideCollectionSortReducer';
import createSetTableOptionReducer from './Creators/Reducers/createSetTableOptionReducer';

//
// Variables

export const section = 'queue';
const status = `${section}.status`;
const details = `${section}.details`;
const paged = `${section}.paged`;

//
// State

export const defaultState = {
  options: {
    includeUnknownAuthorItems: true
  },

  status: {
    isFetching: false,
    isPopulated: false,
    error: null,
    item: {}
  },

  details: {
    isFetching: false,
    isPopulated: false,
    error: null,
    items: [],
    params: {}
  },

  paged: {
    page: 1,
    pageSize: 20,
    sortKey: 'timeleft',
    sortDirection: sortDirections.ASCENDING,
    columns: [
      {
        name: 'status',
        columnLabel: () => translate('Status'),
        isSortable: true,
        isVisible: true,
        isModifiable: false
      },
      {
        name: 'authorMetadata.sortName',
        label: () => translate('Author'),
        isSortable: true,
        isVisible: true
      },
      {
        name: 'books.title',
        label: () => translate('BookTitle'),
        isSortable: true,
        isVisible: true
      },
      {
        name: 'books.releaseDate',
        label: () => translate('ReleaseDate'),
        isSortable: true,
        isVisible: false
      },
      {
        name: 'quality',
        label: () => translate('Quality'),
        isSortable: true,
        isVisible: true
      },
      {
        name: 'customFormats',
        label: 'Formats',
        isSortable: false,
        isVisible: true
      },
      {
        name: 'customFormatScore',
        columnLabel: () => translate('CustomFormatScore'),
        label: React.createElement(Icon, {
          name: icons.SCORE,
          title: () => translate('CustomFormatScore')
        }),
        isVisible: false
      },
      {
        name: 'protocol',
        label: () => translate('Protocol'),
        isSortable: true,
        isVisible: false
      },
      {
        name: 'indexer',
        label: () => translate('Indexer'),
        isSortable: true,
        isVisible: false
      },
      {
        name: 'downloadClient',
        label: () => translate('DownloadClient'),
        isSortable: true,
        isVisible: false
      },
      {
        name: 'title',
        label: () => translate('ReleaseTitle'),
        isSortable: true,
        isVisible: false
      },
      {
        name: 'size',
        label: () => translate('Size'),
        isSortable: true,
        isVisible: false
      },
      {
        name: 'outputPath',
        label: () => translate('OutputPath'),
        isSortable: false,
        isVisible: false
      },
      {
        name: 'estimatedCompletionTime',
        label: () => translate('TimeLeft'),
        isSortable: true,
        isVisible: true
      },
      {
        name: 'progress',
        label: () => translate('Progress'),
        isSortable: true,
        isVisible: true
      },
      {
        name: 'actions',
        columnLabel: () => translate('Actions'),
        isVisible: true,
        isModifiable: false
      }
    ]
  }
};

export const persistState = [
  'queue.options',
  'queue.paged.pageSize',
  'queue.paged.sortKey',
  'queue.paged.sortDirection',
  'queue.paged.columns'
];

const FETCH_QUEUE_STATUS = 'queue/fetchQueueStatus';
const FETCH_QUEUE_DETAILS = 'queue/fetchQueueDetails';
const CLEAR_QUEUE_DETAILS = 'queue/clearQueueDetails';
const SET_QUEUE_SORT = 'queue/setQueueSort';
const SET_QUEUE_TABLE_OPTION = 'queue/setQueueTableOption';
const SET_QUEUE_OPTION = 'queue/setQueueOption';

export const fetchQueueStatus = createThunk(FETCH_QUEUE_STATUS);
export const fetchQueueDetails = createThunk(FETCH_QUEUE_DETAILS);
export const clearQueueDetails = createAction(CLEAR_QUEUE_DETAILS);
export const setQueuePage = (page) => set({ section: paged, page });
export const setQueueSort = createAction(SET_QUEUE_SORT);
export const setQueueTableOption = createAction(SET_QUEUE_TABLE_OPTION);
export const setQueueOption = createAction(SET_QUEUE_OPTION);

//
// Helpers

const fetchQueueDetailsHelper = createFetchHandler(details, '/queue/details');

//
// Action Handlers

export const actionHandlers = handleThunks({

  [FETCH_QUEUE_STATUS]: createFetchHandler(status, '/queue/status'),

  [FETCH_QUEUE_DETAILS]: function(getState, payload, dispatch) {
    let params = payload;

    // If the payload params are empty try to get params from state.

    if (params && !_.isEmpty(params)) {
      dispatch(set({ section: details, params }));
    } else {
      params = getState().queue.details.params;
    }

    // Ensure there are params before trying to fetch the queue
    // so we don't make a bad request to the server.

    if (params && !_.isEmpty(params)) {
      fetchQueueDetailsHelper(getState, params, dispatch);
    }
  }
});

//
// Reducers

const tableOptionReducer = createSetTableOptionReducer(paged);

export const reducers = createHandleActions({
  [CLEAR_QUEUE_DETAILS]: createClearReducer(details, defaultState.details),

  [SET_QUEUE_SORT]: createSetClientSideCollectionSortReducer(paged),

  [SET_QUEUE_TABLE_OPTION]: (state, action) => {
    const next = tableOptionReducer(state, action);
    return next.paged.pageSize === state.paged.pageSize ? next : {
      ...next,
      paged: { ...next.paged, page: 1 }
    };
  },

  [SET_QUEUE_OPTION]: (state, { payload }) => {
    const options = { ...state.options, ...payload };

    return {
      ...state,
      options,
      paged: options.includeUnknownAuthorItems === state.options.includeUnknownAuthorItems ?
        state.paged : { ...state.paged, page: 1 }
    };
  }
}, defaultState, section);
