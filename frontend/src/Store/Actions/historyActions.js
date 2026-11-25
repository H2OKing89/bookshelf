import React from 'react';
import { createAction } from 'redux-actions';
import { historyEventFilters } from 'Activity/History/historyFilters';
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
    ...historyEventFilters.map(({ key, label, value }) => ({
      key,
      label,
      filters: [
        {
          key: 'eventType',
          value,
          type: filterTypes.EQUAL
        }
      ]
    }))
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
