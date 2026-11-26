import { createAction } from 'redux-actions';
import { filterBuilderTypes, filterBuilderValueTypes, filterTypes, sortDirections } from 'Helpers/Props';
import { asStateRecord, type StateRecord } from 'Store/StateTypes';
import translate from 'Utilities/String/translate';
import createHandleActions from './Creators/createHandleActions';
import { matchesFilter, numberValue } from './Creators/libraryActionsState';
import createSetClientSideCollectionFilterReducer from './Creators/Reducers/createSetClientSideCollectionFilterReducer';
import createSetClientSideCollectionSortReducer from './Creators/Reducers/createSetClientSideCollectionSortReducer';

//
// Variables

export const section = 'releases';
export const bookSection = 'releases.book';
export const authorSection = 'releases.author';

//
// State

export const defaultState = {
  sortKey: 'releaseWeight',
  sortDirection: sortDirections.ASCENDING,
  sortPredicates: {
    age: (item: StateRecord) => numberValue(item.ageMinutes),
    peers: (item: StateRecord) => numberValue(item.seeders) * 1000000 + numberValue(item.leechers),
    rejections: (item: StateRecord) => numberValue(item.releaseWeight) + (Array.isArray(item.rejections) && item.rejections.length ? 1000000 : 0)
  },

  filters: [
    {
      key: 'all',
      label: 'All',
      filters: []
    },
    {
      key: 'discography-pack',
      label: 'Discography',
      filters: [
        {
          key: 'discography',
          value: true,
          type: filterTypes.EQUAL
        }
      ]
    },
    {
      key: 'not-discography-pack',
      label: 'Not Discography',
      filters: [
        {
          key: 'discography',
          value: false,
          type: filterTypes.EQUAL
        }
      ]
    }
  ],

  filterPredicates: {
    quality(item: StateRecord, value: unknown, type: string) {
      const id = asStateRecord(asStateRecord(item.quality).quality).id;
      return typeof id === 'number' && (type === filterTypes.EQUAL || type === filterTypes.NOT_EQUAL) && matchesFilter(type, id, value);
    },
    rejectionCount(item: StateRecord, value: unknown, type: string) {
      return matchesFilter(type, Array.isArray(item.rejections) ? item.rejections.length : 0, value);
    },
    peers(item: StateRecord, value: unknown, type: string) {
      return matchesFilter(type, numberValue(item.seeders) + numberValue(item.leechers), value);
    }
  },

  filterBuilderProps: [
    {
      name: 'title',
      label: 'Title',
      type: filterBuilderTypes.STRING
    },
    {
      name: 'age',
      label: 'Age',
      type: filterBuilderTypes.NUMBER
    },
    {
      name: 'protocol',
      label: 'Protocol',
      type: filterBuilderTypes.EXACT,
      valueType: filterBuilderValueTypes.PROTOCOL
    },
    {
      name: 'indexerId',
      label: 'Indexer',
      type: filterBuilderTypes.EXACT,
      valueType: filterBuilderValueTypes.INDEXER
    },
    {
      name: 'size',
      label: 'Size',
      type: filterBuilderTypes.NUMBER,
      valueType: filterBuilderValueTypes.BYTES
    },
    {
      name: 'seeders',
      label: 'Seeders',
      type: filterBuilderTypes.NUMBER
    },
    {
      name: 'leechers',
      label: 'Peers',
      type: filterBuilderTypes.NUMBER
    },
    {
      name: 'quality',
      label: 'Quality',
      type: filterBuilderTypes.EXACT,
      valueType: filterBuilderValueTypes.QUALITY
    },
    {
      name: 'customFormatScore',
      label: () => translate('CustomFormatScore'),
      type: filterBuilderTypes.NUMBER
    },
    {
      name: 'rejectionCount',
      label: 'Rejection Count',
      type: filterBuilderTypes.NUMBER
    }
  ],

  book: {
    selectedFilterKey: 'all'
  },

  author: {
    selectedFilterKey: 'all'
  }
};

export const persistState = [
  'releases.selectedFilterKey',
  'releases.book.selectedFilterKey',
  'releases.author.selectedFilterKey',
  'releases.book.customFilters',
  'releases.author.customFilters'
];

//
// Actions Types

export const SET_RELEASES_SORT = 'releases/setReleasesSort';
export const SET_BOOK_RELEASES_FILTER = 'releases/setBookReleasesFilter';
export const SET_AUTHOR_RELEASES_FILTER = 'releases/setAuthorReleasesFilter';

//
// Action Creators

export const setReleasesSort = createAction<StateRecord>(SET_RELEASES_SORT);
export const setBookReleasesFilter = createAction<StateRecord>(SET_BOOK_RELEASES_FILTER);
export const setAuthorReleasesFilter = createAction<StateRecord>(SET_AUTHOR_RELEASES_FILTER);

//
// Reducers

export const reducers = createHandleActions({

  [SET_RELEASES_SORT]: createSetClientSideCollectionSortReducer(section),
  [SET_BOOK_RELEASES_FILTER]: createSetClientSideCollectionFilterReducer(bookSection),
  [SET_AUTHOR_RELEASES_FILTER]: createSetClientSideCollectionFilterReducer(authorSection)

}, defaultState, section);
