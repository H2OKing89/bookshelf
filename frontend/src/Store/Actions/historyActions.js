import React from 'react';
import { createAction } from 'redux-actions';
import Icon from 'Components/Icon';
import { filterTypes, icons, sortDirections } from 'Helpers/Props';
import { set } from './baseActions';
import createHandleActions from './Creators/createHandleActions';
import createSetClientSideCollectionSortReducer from './Creators/Reducers/createSetClientSideCollectionSortReducer';
import createSetTableOptionReducer from './Creators/Reducers/createSetTableOptionReducer';

//
// Variables

export const section = 'history';

//
// State

export const defaultState = {
  page: 1,
  pageSize: 20,
  sortKey: 'date',
  sortDirection: sortDirections.DESCENDING,
  columns: [
    {
      name: 'eventType',
      columnLabel: 'Event Type',
      isVisible: true,
      isModifiable: false
    },
    {
      name: 'authorMetadata.sortName',
      label: 'Author',
      isSortable: true,
      isVisible: true
    },
    {
      name: 'books.title',
      label: 'Book',
      isSortable: true,
      isVisible: true
    },
    {
      name: 'quality',
      label: 'Quality',
      isVisible: true
    },
    {
      name: 'customFormats',
      label: 'Formats',
      isSortable: false,
      isVisible: true
    },
    {
      name: 'date',
      label: 'Date',
      isSortable: true,
      isVisible: true
    },
    {
      name: 'downloadClient',
      label: 'Download Client',
      isVisible: false
    },
    {
      name: 'indexer',
      label: 'Indexer',
      isVisible: false
    },
    {
      name: 'releaseGroup',
      label: 'Release Group',
      isVisible: false
    },
    {
      name: 'sourceTitle',
      label: 'Source Title',
      isVisible: false
    },
    {
      name: 'customFormatScore',
      columnLabel: 'Custom Format Score',
      label: React.createElement(Icon, {
        name: icons.SCORE,
        title: 'Custom format score'
      }),
      isVisible: false
    },
    {
      name: 'details',
      columnLabel: 'Details',
      isVisible: true,
      isModifiable: false
    }
  ],

  selectedFilterKey: 'all',

  filters: [
    {
      key: 'all',
      label: 'All',
      filters: []
    },
    {
      key: 'grabbed',
      label: 'Grabbed',
      filters: [
        {
          key: 'eventType',
          value: '1',
          type: filterTypes.EQUAL
        }
      ]
    },
    {
      key: 'bookFileImported',
      label: 'Book Imported',
      filters: [
        {
          key: 'eventType',
          value: '3',
          type: filterTypes.EQUAL
        }
      ]
    },
    {
      key: 'failed',
      label: 'Download Failed',
      filters: [
        {
          key: 'eventType',
          value: '4',
          type: filterTypes.EQUAL
        }
      ]
    },
    {
      key: 'importFailed',
      label: 'Import Failed',
      filters: [
        {
          key: 'eventType',
          value: '7',
          type: filterTypes.EQUAL
        }
      ]
    },
    {
      key: 'deleted',
      label: 'Deleted',
      filters: [
        {
          key: 'eventType',
          value: '5',
          type: filterTypes.EQUAL
        }
      ]
    },
    {
      key: 'renamed',
      label: 'Renamed',
      filters: [
        {
          key: 'eventType',
          value: '6',
          type: filterTypes.EQUAL
        }
      ]
    },
    {
      key: 'retagged',
      label: 'Retagged',
      filters: [
        {
          key: 'eventType',
          value: '9',
          type: filterTypes.EQUAL
        }
      ]
    },
    {
      key: 'ignored',
      label: 'Ignored',
      filters: [
        {
          key: 'eventType',
          value: '10',
          type: filterTypes.EQUAL
        }
      ]
    }
  ]

};

export const persistState = [
  'history.pageSize',
  'history.sortKey',
  'history.sortDirection',
  'history.selectedFilterKey',
  'history.columns'
];

//
// Actions Types

const SET_HISTORY_SORT = 'history/setHistorySort';
const SET_HISTORY_TABLE_OPTION = 'history/setHistoryTableOption';

//
// Action Creators

export const setHistoryPage = (page) => set({ section, page });
export const setHistorySort = createAction(SET_HISTORY_SORT);
export const setHistoryFilter = (selectedFilterKey) => set({ section, selectedFilterKey, page: 1 });
export const setHistoryTableOption = createAction(SET_HISTORY_TABLE_OPTION);

//
// Reducers

const tableOptionReducer = createSetTableOptionReducer(section);
export const reducers = createHandleActions({
  [SET_HISTORY_SORT]: createSetClientSideCollectionSortReducer(section),
  [SET_HISTORY_TABLE_OPTION]: (state, action) => {
    const next = tableOptionReducer(state, action);
    return next.pageSize === state.pageSize ? next : { ...next, page: 1 };
  }
}, defaultState, section);
