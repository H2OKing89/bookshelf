import { createAction } from 'redux-actions';
import { sortDirections } from 'Helpers/Props';
import createBulkEditItemHandler from 'Store/Actions/Creators/createBulkEditItemHandler';
import createBulkRemoveItemHandler from 'Store/Actions/Creators/createBulkRemoveItemHandler';
import createFetchHandler from 'Store/Actions/Creators/createFetchHandler';
import createFetchSchemaHandler from 'Store/Actions/Creators/createFetchSchemaHandler';
import createRemoveItemHandler from 'Store/Actions/Creators/createRemoveItemHandler';
import createSaveProviderHandler, { createCancelSaveProviderHandler } from 'Store/Actions/Creators/createSaveProviderHandler';
import createTestAllProvidersHandler from 'Store/Actions/Creators/createTestAllProvidersHandler';
import createTestProviderHandler, { createCancelTestProviderHandler } from 'Store/Actions/Creators/createTestProviderHandler';
import createSetClientSideCollectionSortReducer from 'Store/Actions/Creators/Reducers/createSetClientSideCollectionSortReducer';
import createSetProviderFieldValueReducer from 'Store/Actions/Creators/Reducers/createSetProviderFieldValueReducer';
import createSetSettingValueReducer from 'Store/Actions/Creators/Reducers/createSetSettingValueReducer';
import type { StateRecord } from 'Store/StateTypes';
import { createThunk } from 'Store/thunks';
import selectProviderSchema from 'Utilities/State/selectProviderSchema';
import { type CloneSettingItem, cloneSettingsItem,createSettingValueAction, providerName, type ProviderSchemaSelection, type ProviderSortOptions } from './settingActions';

//
// Variables

const section = 'settings.indexers';

//
// Actions Types

export const FETCH_INDEXERS = 'settings/indexers/fetchIndexers';
export const FETCH_INDEXER_SCHEMA = 'settings/indexers/fetchIndexerSchema';
export const SELECT_INDEXER_SCHEMA = 'settings/indexers/selectIndexerSchema';
export const CLONE_INDEXER = 'settings/indexers/cloneIndexer';
export const SET_INDEXER_VALUE = 'settings/indexers/setIndexerValue';
export const SET_INDEXER_FIELD_VALUE = 'settings/indexers/setIndexerFieldValue';
export const SAVE_INDEXER = 'settings/indexers/saveIndexer';
export const CANCEL_SAVE_INDEXER = 'settings/indexers/cancelSaveIndexer';
export const DELETE_INDEXER = 'settings/indexers/deleteIndexer';
export const TEST_INDEXER = 'settings/indexers/testIndexer';
export const CANCEL_TEST_INDEXER = 'settings/indexers/cancelTestIndexer';
export const TEST_ALL_INDEXERS = 'settings/indexers/testAllIndexers';
export const BULK_EDIT_INDEXERS = 'settings/indexers/bulkEditIndexers';
export const BULK_DELETE_INDEXERS = 'settings/indexers/bulkDeleteIndexers';
export const SET_MANAGE_INDEXERS_SORT = 'settings/indexers/setManageIndexersSort';

//
// Action Creators

export const fetchIndexers = createThunk(FETCH_INDEXERS);
export const fetchIndexerSchema = createThunk(FETCH_INDEXER_SCHEMA);
export const selectIndexerSchema = createAction<ProviderSchemaSelection>(SELECT_INDEXER_SCHEMA);
export const cloneIndexer = createAction<CloneSettingItem>(CLONE_INDEXER);

export const saveIndexer = createThunk(SAVE_INDEXER);
export const cancelSaveIndexer = createThunk(CANCEL_SAVE_INDEXER);
export const deleteIndexer = createThunk(DELETE_INDEXER);
export const testIndexer = createThunk(TEST_INDEXER);
export const cancelTestIndexer = createThunk(CANCEL_TEST_INDEXER);
export const testAllIndexers = createThunk(TEST_ALL_INDEXERS);
export const bulkEditIndexers = createThunk(BULK_EDIT_INDEXERS);
export const bulkDeleteIndexers = createThunk(BULK_DELETE_INDEXERS);
export const setManageIndexersSort = createAction<ProviderSortOptions>(SET_MANAGE_INDEXERS_SORT);

export const setIndexerValue = createSettingValueAction(SET_INDEXER_VALUE, section);

export const setIndexerFieldValue = createSettingValueAction(SET_INDEXER_FIELD_VALUE, section);

//
// Details

export default {

  //
  // State

  defaultState: {
    isFetching: false,
    isPopulated: false,
    error: null,
    isSchemaFetching: false,
    isSchemaPopulated: false,
    schemaError: null,
    schema: [],
    selectedSchema: {},
    isSaving: false,
    saveError: null,
    isDeleting: false,
    deleteError: null,
    isTesting: false,
    isTestingAll: false,
    items: [],
    pendingChanges: {},
    sortKey: 'name',
    sortDirection: sortDirections.ASCENDING,
    sortPredicates: {
      name: providerName
    }
  },

  //
  // Action Handlers

  actionHandlers: {
    [FETCH_INDEXERS]: createFetchHandler(section, '/indexer'),
    [FETCH_INDEXER_SCHEMA]: createFetchSchemaHandler(section, '/indexer/schema'),

    [SAVE_INDEXER]: createSaveProviderHandler(section, '/indexer'),
    [CANCEL_SAVE_INDEXER]: createCancelSaveProviderHandler(section),
    [DELETE_INDEXER]: createRemoveItemHandler(section, '/indexer'),
    [TEST_INDEXER]: createTestProviderHandler(section, '/indexer'),
    [CANCEL_TEST_INDEXER]: createCancelTestProviderHandler(section),
    [TEST_ALL_INDEXERS]: createTestAllProvidersHandler(section, '/indexer'),
    [BULK_EDIT_INDEXERS]: createBulkEditItemHandler(section, '/indexer/bulk'),
    [BULK_DELETE_INDEXERS]: createBulkRemoveItemHandler(section, '/indexer/bulk')
  },

  //
  // Reducers

  reducers: {
    [SET_INDEXER_VALUE]: createSetSettingValueReducer(section),
    [SET_INDEXER_FIELD_VALUE]: createSetProviderFieldValueReducer(section),

    [SELECT_INDEXER_SCHEMA]: (state: StateRecord, { payload }: { payload: unknown }) => {
      return selectProviderSchema(state, section, payload, (selectedSchema) => {
        selectedSchema.enableRss = selectedSchema.supportsRss;
        selectedSchema.enableAutomaticSearch = selectedSchema.supportsSearch;
        selectedSchema.enableInteractiveSearch = selectedSchema.supportsSearch;

        return selectedSchema;
      });
    },

    [CLONE_INDEXER]: (state: StateRecord, { payload }: { payload: unknown }) => cloneSettingsItem(state, section, payload, true),

    [SET_MANAGE_INDEXERS_SORT]: createSetClientSideCollectionSortReducer(section)

  }

};
