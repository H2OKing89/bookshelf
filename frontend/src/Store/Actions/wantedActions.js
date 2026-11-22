import { createAction } from 'redux-actions';
import { filterTypes, sortDirections } from 'Helpers/Props';
import { set } from './baseActions';
import createHandleActions from './Creators/createHandleActions';
import createSetClientSideCollectionSortReducer from './Creators/Reducers/createSetClientSideCollectionSortReducer';
import createSetTableOptionReducer from './Creators/Reducers/createSetTableOptionReducer';

//
// Variables

export const section = 'wanted';

//
// State

export const defaultState = {
  missing: {
    page: 1,
    pageSize: 20,
    sortKey: 'releaseDate',
    sortDirection: sortDirections.DESCENDING,

    columns: [
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
        name: 'releaseDate',
        label: 'Release Date',
        isSortable: true,
        isVisible: true
      },
      {
        name: 'books.lastSearchTime',
        label: 'Last Searched',
        isSortable: true,
        isVisible: false
      },
      {
        name: 'actions',
        columnLabel: 'Actions',
        isVisible: true,
        isModifiable: false
      }
    ],

    selectedFilterKey: 'monitored',

    filters: [
      {
        key: 'monitored',
        label: 'Monitored',
        filters: [
          {
            key: 'monitored',
            value: true,
            type: filterTypes.EQUAL
          }
        ]
      },
      {
        key: 'unmonitored',
        label: 'Unmonitored',
        filters: [
          {
            key: 'monitored',
            value: false,
            type: filterTypes.EQUAL
          }
        ]
      }
    ]
  },

  cutoffUnmet: {
    page: 1,
    pageSize: 20,
    sortKey: 'releaseDate',
    sortDirection: sortDirections.DESCENDING,

    columns: [
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
        name: 'releaseDate',
        label: 'Release Date',
        isSortable: true,
        isVisible: true
      },
      {
        name: 'books.lastSearchTime',
        label: 'Last Searched',
        isSortable: true,
        isVisible: false
      },
      {
        name: 'actions',
        columnLabel: 'Actions',
        isVisible: true,
        isModifiable: false
      }
    ],

    selectedFilterKey: 'monitored',

    filters: [
      {
        key: 'monitored',
        label: 'Monitored',
        filters: [
          {
            key: 'monitored',
            value: true,
            type: filterTypes.EQUAL
          }
        ]
      },
      {
        key: 'unmonitored',
        label: 'Unmonitored',
        filters: [
          {
            key: 'monitored',
            value: false,
            type: filterTypes.EQUAL
          }
        ]
      }
    ]
  }
};

export const persistState = [
  'wanted.missing.pageSize',
  'wanted.missing.sortKey',
  'wanted.missing.sortDirection',
  'wanted.missing.selectedFilterKey',
  'wanted.missing.columns',
  'wanted.cutoffUnmet.pageSize',
  'wanted.cutoffUnmet.sortKey',
  'wanted.cutoffUnmet.sortDirection',
  'wanted.cutoffUnmet.selectedFilterKey',
  'wanted.cutoffUnmet.columns'
];

const SET_WANTED_SORT = 'wanted/setSort';
const SET_WANTED_TABLE_OPTION = 'wanted/setTableOption';

export const setWantedPage = (kind, page) => set({ section: `wanted.${kind}`, page });
export const setWantedFilter = (kind, selectedFilterKey) => set({ section: `wanted.${kind}`, selectedFilterKey, page: 1 });
export const setWantedSort = createAction(SET_WANTED_SORT);
export const setWantedTableOption = createAction(SET_WANTED_TABLE_OPTION);

const sortReducers = {
  missing: createSetClientSideCollectionSortReducer('wanted.missing'),
  cutoffUnmet: createSetClientSideCollectionSortReducer('wanted.cutoffUnmet')
};

const tableOptionReducers = {
  missing: createSetTableOptionReducer('wanted.missing'),
  cutoffUnmet: createSetTableOptionReducer('wanted.cutoffUnmet')
};

export const reducers = createHandleActions({
  [SET_WANTED_SORT]: (state, action) => sortReducers[action.payload.kind](state, action),
  [SET_WANTED_TABLE_OPTION]: (state, action) => {
    const { kind } = action.payload;
    const next = tableOptionReducers[kind](state, action);

    return next[kind].pageSize === state[kind].pageSize ?
      next :
      { ...next, [kind]: { ...next[kind], page: 1 } };
  }
}, defaultState, section);
