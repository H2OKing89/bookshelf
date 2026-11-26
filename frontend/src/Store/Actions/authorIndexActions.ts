import { createAction } from 'redux-actions';
import { parseLibraryAuthor } from 'Book/LibraryModels';
import { filterBuilderTypes, filterBuilderValueTypes, sortDirections } from 'Helpers/Props';
import { asStateRecord, isStateRecord, type StateRecord } from 'Store/StateTypes';
import { createThunk, handleThunks } from 'Store/thunks';
import sortByName from 'Utilities/Array/sortByName';
import { filterPredicates, filters, sortPredicates } from './authorActions';
import createHandleActions from './Creators/createHandleActions';
import { createLibraryBulkEditor, matchesFilter, namedOptions, numberValue } from './Creators/libraryActionsState';
import createSetClientSideCollectionFilterReducer from './Creators/Reducers/createSetClientSideCollectionFilterReducer';
import createSetClientSideCollectionSortReducer from './Creators/Reducers/createSetClientSideCollectionSortReducer';
import createSetTableOptionReducer from './Creators/Reducers/createSetTableOptionReducer';

//
// Variables

export const section = 'authorIndex';

//
// State

export const defaultState = {
  isSaving: false,
  saveError: null,
  isDeleting: false,
  deleteError: null,
  sortKey: 'sortNameLastFirst',
  sortDirection: sortDirections.ASCENDING,
  secondarySortKey: 'sortNameLastFirst',
  secondarySortDirection: sortDirections.ASCENDING,
  view: 'posters',

  posterOptions: {
    detailedProgressBar: false,
    size: 'large',
    showTitle: 'lastFirst',
    showMonitored: true,
    showQualityProfile: true,
    showSearchAction: false
  },

  overviewOptions: {
    showTitle: 'lastFirst',
    detailedProgressBar: false,
    size: 'medium',
    showMonitored: true,
    showQualityProfile: true,
    showLastBook: false,
    showAdded: false,
    showBookCount: true,
    showPath: false,
    showSizeOnDisk: false,
    showSearchAction: false
  },

  tableOptions: {
    showTitle: 'lastFirst',
    showBanners: false,
    showSearchAction: false
  },

  columns: [
    {
      name: 'select',
      columnLabel: 'Select',
      isSortable: false,
      isVisible: true,
      isModifiable: false,
      isHidden: true
    },
    {
      name: 'status',
      columnLabel: 'Status',
      isSortable: true,
      isVisible: true,
      isModifiable: false
    },
    {
      name: 'sortName',
      label: 'Author Name',
      isSortable: true,
      isVisible: true,
      isModifiable: false
    },
    {
      name: 'qualityProfileId',
      label: 'Quality Profile',
      isSortable: true,
      isVisible: true
    },
    {
      name: 'metadataProfileId',
      label: 'Metadata Profile',
      isSortable: true,
      isVisible: false
    },
    {
      name: 'nextBook',
      label: 'Next Book',
      isSortable: true,
      isVisible: true
    },
    {
      name: 'lastBook',
      label: 'Last Book',
      isSortable: true,
      isVisible: false
    },
    {
      name: 'added',
      label: 'Added',
      isSortable: true,
      isVisible: false
    },
    {
      name: 'bookProgress',
      label: 'Books',
      isSortable: true,
      isVisible: true
    },
    {
      name: 'path',
      label: 'Path',
      isSortable: true,
      isVisible: false
    },
    {
      name: 'sizeOnDisk',
      label: 'Size on Disk',
      isSortable: true,
      isVisible: false
    },
    {
      name: 'genres',
      label: 'Genres',
      isSortable: false,
      isVisible: false
    },
    {
      name: 'ratings',
      label: 'Rating',
      isSortable: true,
      isVisible: false
    },
    {
      name: 'tags',
      label: 'Tags',
      isSortable: false,
      isVisible: false
    },
    {
      name: 'actions',
      columnLabel: 'Actions',
      isVisible: true,
      isModifiable: false
    }
  ],

  sortPredicates: {
    ...sortPredicates,
    bookProgress(item: StateRecord) {
      const stats = asStateRecord(item.statistics);
      const bookCount = numberValue(stats.bookCount);
      return (bookCount ? numberValue(stats.bookFileCount) / bookCount * 100 : 100) + bookCount / 1000000;
    },
    nextBook(item: StateRecord) {
      return asStateRecord(item.nextBook).releaseDate ?? '1/1/1000';
    },
    lastBook(item: StateRecord) {
      return asStateRecord(item.lastBook).releaseDate ?? '1/1/1000';
    },
    bookCount(item: StateRecord) {
      return numberValue(asStateRecord(item.statistics).bookCount);
    },
    ratings(item: StateRecord) {
      return numberValue(asStateRecord(item.ratings).value);
    }
  },
  selectedFilterKey: 'all',
  filters,
  filterPredicates: {
    ...filterPredicates,
    bookProgress(item: StateRecord, value: unknown, type: string) {
      const stats = asStateRecord(item.statistics);
      const count = numberValue(stats.bookCount);
      return matchesFilter(type, count ? numberValue(stats.bookFileCount) / count * 100 : 100, value);
    }
  },

  filterBuilderProps: [
    {
      name: 'monitored',
      label: 'Monitored',
      type: filterBuilderTypes.EXACT,
      valueType: filterBuilderValueTypes.BOOL
    },
    {
      name: 'status',
      label: 'Status',
      type: filterBuilderTypes.EXACT,
      valueType: filterBuilderValueTypes.AUTHOR_STATUS
    },
    {
      name: 'qualityProfileId',
      label: 'Quality Profile',
      type: filterBuilderTypes.EXACT,
      valueType: filterBuilderValueTypes.QUALITY_PROFILE
    },
    {
      name: 'metadataProfileId',
      label: 'Metadata Profile',
      type: filterBuilderTypes.EXACT,
      valueType: filterBuilderValueTypes.METADATA_PROFILE
    },
    {
      name: 'nextBook',
      label: 'Next Book',
      type: filterBuilderTypes.DATE,
      valueType: filterBuilderValueTypes.DATE
    },
    {
      name: 'lastBook',
      label: 'Last Book',
      type: filterBuilderTypes.DATE,
      valueType: filterBuilderValueTypes.DATE
    },
    {
      name: 'added',
      label: 'Added',
      type: filterBuilderTypes.DATE,
      valueType: filterBuilderValueTypes.DATE
    },
    {
      name: 'bookCount',
      label: 'Book Count',
      type: filterBuilderTypes.NUMBER
    },
    {
      name: 'bookProgress',
      label: 'Book Progress',
      type: filterBuilderTypes.NUMBER
    },
    {
      name: 'path',
      label: 'Path',
      type: filterBuilderTypes.STRING
    },
    {
      name: 'sizeOnDisk',
      label: 'Size on Disk',
      type: filterBuilderTypes.NUMBER,
      valueType: filterBuilderValueTypes.BYTES
    },
    {
      name: 'genres',
      label: 'Genres',
      type: filterBuilderTypes.ARRAY,
      optionsSelector: (items: unknown) => namedOptions(items, 'genres').sort(sortByName)
    },
    {
      name: 'ratings',
      label: 'Rating',
      type: filterBuilderTypes.NUMBER
    },
    {
      name: 'tags',
      label: 'Tags',
      type: filterBuilderTypes.ARRAY,
      valueType: filterBuilderValueTypes.TAG
    }
  ]
};

export const persistState = [
  'authorIndex.sortKey',
  'authorIndex.sortDirection',
  'authorIndex.selectedFilterKey',
  'authorIndex.customFilters',
  'authorIndex.view',
  'authorIndex.columns',
  'authorIndex.posterOptions',
  'authorIndex.bannerOptions',
  'authorIndex.overviewOptions',
  'authorIndex.tableOptions'
];

//
// Actions Types

export const SET_AUTHOR_SORT = 'authorIndex/setAuthorSort';
export const SET_AUTHOR_FILTER = 'authorIndex/setAuthorFilter';
export const SET_AUTHOR_VIEW = 'authorIndex/setAuthorView';
export const SET_AUTHOR_TABLE_OPTION = 'authorIndex/setAuthorTableOption';
export const SET_AUTHOR_POSTER_OPTION = 'authorIndex/setAuthorPosterOption';
export const SET_AUTHOR_BANNER_OPTION = 'authorIndex/setAuthorBannerOption';
export const SET_AUTHOR_OVERVIEW_OPTION = 'authorIndex/setAuthorOverviewOption';
export const SAVE_AUTHOR_EDITOR = 'authorIndex/saveAuthorEditor';
export const BULK_DELETE_AUTHOR = 'authorIndex/bulkDeleteAuthor';

//
// Action Creators

export const setAuthorSort = createAction<StateRecord>(SET_AUTHOR_SORT);
export const setAuthorFilter = createAction<StateRecord>(SET_AUTHOR_FILTER);
export const setAuthorView = createAction<StateRecord>(SET_AUTHOR_VIEW);
export const setAuthorTableOption = createAction<StateRecord>(SET_AUTHOR_TABLE_OPTION);
export const setAuthorPosterOption = createAction<StateRecord>(SET_AUTHOR_POSTER_OPTION);
export const setAuthorBannerOption = createAction<StateRecord>(SET_AUTHOR_BANNER_OPTION);
export const setAuthorOverviewOption = createAction<StateRecord>(SET_AUTHOR_OVERVIEW_OPTION);
export const saveAuthorEditor = createThunk(SAVE_AUTHOR_EDITOR);
export const bulkDeleteAuthor = createThunk(BULK_DELETE_AUTHOR);

//
// Action Handlers

const editor = createLibraryBulkEditor({
  section,
  target: 'authors',
  url: '/author/editor',
  idKey: 'authorIds',
  parse: parseLibraryAuthor
});

export const actionHandlers = handleThunks({
  [SAVE_AUTHOR_EDITOR]: editor.save,
  [BULK_DELETE_AUTHOR]: editor.remove
});

//
// Reducers

export const reducers = createHandleActions({

  [SET_AUTHOR_SORT]: createSetClientSideCollectionSortReducer(section),
  [SET_AUTHOR_FILTER]: createSetClientSideCollectionFilterReducer(section),

  [SET_AUTHOR_VIEW]: function(state, { payload }) {
    return isStateRecord(payload) && typeof payload.view === 'string' ? { ...state, view: payload.view } : state;
  },

  [SET_AUTHOR_TABLE_OPTION]: createSetTableOptionReducer(section),

  [SET_AUTHOR_POSTER_OPTION]: function(state, { payload }) {
    const posterOptions = asStateRecord(state.posterOptions);

    return {
      ...state,
      posterOptions: {
        ...posterOptions,
        ...asStateRecord(payload)
      }
    };
  },

  [SET_AUTHOR_BANNER_OPTION]: function(state, { payload }) {
    const bannerOptions = asStateRecord(state.bannerOptions);

    return {
      ...state,
      bannerOptions: {
        ...bannerOptions,
        ...asStateRecord(payload)
      }
    };
  },

  [SET_AUTHOR_OVERVIEW_OPTION]: function(state, { payload }) {
    const overviewOptions = asStateRecord(state.overviewOptions);

    return {
      ...state,
      overviewOptions: {
        ...overviewOptions,
        ...asStateRecord(payload)
      }
    };
  }

}, defaultState, section);
