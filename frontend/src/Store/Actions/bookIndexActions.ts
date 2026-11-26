import { createAction } from 'redux-actions';
import { parseLibraryBook } from 'Book/LibraryModels';
import { filterBuilderTypes, filterBuilderValueTypes, sortDirections } from 'Helpers/Props';
import { asStateRecord, isStateRecord, type StateRecord } from 'Store/StateTypes';
import { createThunk, handleThunks } from 'Store/thunks';
import sortByName from 'Utilities/Array/sortByName';
import { filterPredicates, filters, sortPredicates } from './bookActions';
import createHandleActions from './Creators/createHandleActions';
import { createLibraryBulkEditor, matchesFilter, namedOptions, numberValue } from './Creators/libraryActionsState';
import createSetClientSideCollectionFilterReducer from './Creators/Reducers/createSetClientSideCollectionFilterReducer';
import createSetClientSideCollectionSortReducer from './Creators/Reducers/createSetClientSideCollectionSortReducer';
import createSetTableOptionReducer from './Creators/Reducers/createSetTableOptionReducer';

//
// Variables

export const section = 'bookIndex';

//
// State

export const defaultState = {
  isSaving: false,
  saveError: null,
  isDeleting: false,
  deleteError: null,
  sortKey: 'title',
  sortDirection: sortDirections.ASCENDING,
  secondarySortKey: 'title',
  secondarySortDirection: sortDirections.ASCENDING,
  view: 'posters',

  posterOptions: {
    detailedProgressBar: false,
    size: 'large',
    showTitle: true,
    showAuthor: true,
    showMonitored: true,
    showQualityProfile: true,
    showSearchAction: false
  },

  overviewOptions: {
    detailedProgressBar: false,
    size: 'medium',
    showReleaseDate: true,
    showMonitored: true,
    showQualityProfile: true,
    showAdded: false,
    showPath: false,
    showSizeOnDisk: false,
    showSearchAction: false
  },

  tableOptions: {
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
      name: 'title',
      label: 'Book',
      isSortable: true,
      isVisible: true,
      isModifiable: false
    },
    {
      name: 'authorName',
      label: 'Author',
      isSortable: true,
      isVisible: true,
      isModifiable: true
    },
    {
      name: 'releaseDate',
      label: 'Release Date',
      isSortable: true,
      isVisible: false
    },
    {
      name: 'runtimeLengthMinutes',
      label: 'Runtime',
      isSortable: true,
      isVisible: false
    },
    {
      name: 'narrators',
      label: 'Narrators',
      isSortable: false,
      isVisible: false
    },
    {
      name: 'qualityProfileId',
      label: 'Quality Profile',
      isSortable: true,
      isVisible: true
    },
    {
      name: 'fileQualityRank',
      label: 'Lowest File Quality Rank',
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
      name: 'bookFileCount',
      label: 'File Count',
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
      name: 'metadataLabels',
      label: 'Metadata Labels',
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
    authorName(item: StateRecord) {
      return asStateRecord(item.author).sortName;
    },
    bookFileCount(item: StateRecord) {
      return numberValue(asStateRecord(item.statistics).bookFileCount);
    },
    ratings(item: StateRecord) {
      return numberValue(asStateRecord(item.ratings).value);
    }
  },
  selectedFilterKey: 'all',
  filters,
  filterPredicates: {
    ...filterPredicates,
    author(item: StateRecord, value: unknown, type: string) {
      return isStateRecord(item.author) && matchesFilter(type, item.author.authorName, value);
    },
    anyEditionOk(item: StateRecord, value: unknown, type: string) {
      return matchesFilter(type, item.anyEditionOk, value);
    }
  },

  filterBuilderProps: [
    {
      name: 'author',
      label: 'Author',
      type: filterBuilderTypes.STRING
    },
    {
      name: 'title',
      label: 'Title',
      type: filterBuilderTypes.STRING
    },
    {
      name: 'monitored',
      label: 'Monitored',
      type: filterBuilderTypes.EXACT,
      valueType: filterBuilderValueTypes.BOOL
    },
    {
      name: 'anyEditionOk',
      label: 'Automatic Release Switching',
      type: filterBuilderTypes.EXACT,
      valueType: filterBuilderValueTypes.BOOL
    },
    {
      name: 'qualityProfileId',
      label: 'Quality Profile',
      type: filterBuilderTypes.EXACT,
      valueType: filterBuilderValueTypes.QUALITY_PROFILE
    },
    {
      name: 'releaseDate',
      label: 'Release Date',
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
      name: 'bookFileCount',
      label: 'File Count',
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
      name: 'metadataLabels',
      label: 'Metadata Labels',
      type: filterBuilderTypes.ARRAY,
      optionsSelector: (items: unknown) => namedOptions(items, 'metadataLabels', true).sort(sortByName)
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
  'bookIndex.sortKey',
  'bookIndex.sortDirection',
  'bookIndex.selectedFilterKey',
  'bookIndex.customFilters',
  'bookIndex.view',
  'bookIndex.columns',
  'bookIndex.posterOptions',
  'bookIndex.bannerOptions',
  'bookIndex.overviewOptions',
  'bookIndex.tableOptions'
];

//
// Actions Types

export const SET_BOOK_SORT = 'bookIndex/setBookSort';
export const SET_BOOK_FILTER = 'bookIndex/setBookFilter';
export const SET_BOOK_VIEW = 'bookIndex/setBookView';
export const SET_BOOK_TABLE_OPTION = 'bookIndex/setBookTableOption';
export const SET_BOOK_POSTER_OPTION = 'bookIndex/setBookPosterOption';
export const SET_BOOK_BANNER_OPTION = 'bookIndex/setBookBannerOption';
export const SET_BOOK_OVERVIEW_OPTION = 'bookIndex/setBookOverviewOption';
export const SAVE_BOOK_EDITOR = 'bookEditor/saveBookEditor';
export const BULK_DELETE_BOOK = 'bookEditor/bulkDeleteBook';

//
// Action Creators

export const setBookSort = createAction<StateRecord>(SET_BOOK_SORT);
export const setBookFilter = createAction<StateRecord>(SET_BOOK_FILTER);
export const setBookView = createAction<StateRecord>(SET_BOOK_VIEW);
export const setBookTableOption = createAction<StateRecord>(SET_BOOK_TABLE_OPTION);
export const setBookPosterOption = createAction<StateRecord>(SET_BOOK_POSTER_OPTION);
export const setBookBannerOption = createAction<StateRecord>(SET_BOOK_BANNER_OPTION);
export const setBookOverviewOption = createAction<StateRecord>(SET_BOOK_OVERVIEW_OPTION);
export const saveBookEditor = createThunk(SAVE_BOOK_EDITOR);
export const bulkDeleteBook = createThunk(BULK_DELETE_BOOK);

//
// Action Handlers

const editor = createLibraryBulkEditor({
  section,
  target: 'books',
  url: '/book/editor',
  idKey: 'bookIds',
  parse: parseLibraryBook
});

export const actionHandlers = handleThunks({
  [SAVE_BOOK_EDITOR]: editor.save,
  [BULK_DELETE_BOOK]: editor.remove
});

//
// Reducers

export const reducers = createHandleActions({

  [SET_BOOK_SORT]: createSetClientSideCollectionSortReducer(section),
  [SET_BOOK_FILTER]: createSetClientSideCollectionFilterReducer(section),

  [SET_BOOK_VIEW]: function(state, { payload }) {
    return isStateRecord(payload) && typeof payload.view === 'string' ? { ...state, view: payload.view } : state;
  },

  [SET_BOOK_TABLE_OPTION]: createSetTableOptionReducer(section),

  [SET_BOOK_POSTER_OPTION]: function(state, { payload }) {
    const posterOptions = asStateRecord(state.posterOptions);

    return {
      ...state,
      posterOptions: {
        ...posterOptions,
        ...asStateRecord(payload)
      }
    };
  },

  [SET_BOOK_BANNER_OPTION]: function(state, { payload }) {
    const bannerOptions = asStateRecord(state.bannerOptions);

    return {
      ...state,
      bannerOptions: {
        ...bannerOptions,
        ...asStateRecord(payload)
      }
    };
  },

  [SET_BOOK_OVERVIEW_OPTION]: function(state, { payload }) {
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
