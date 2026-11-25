import { createSelector } from 'reselect';
import type AppState from 'App/State/AppState';
import type { DownloadClientAppState, ImportListAppState, IndexerAppState } from 'App/State/SettingsAppState';
import { asStateRecord, type StateRecord } from 'Store/StateTypes';
import filterCollection from 'Utilities/Array/filterCollection';
import sortCollection from 'Utilities/Array/sortCollection';
import { recordItems, sectionRecord } from './collectionProjection';
import createCustomFiltersSelector from './createCustomFiltersSelector';

interface CollectionProjection<T> {
  customFilters: StateRecord[];
  items: T[];
  totalItems: number;
}

type ProjectedProvider<Section extends { items: object[] }> = Section & CollectionProjection<Section['items'][number]>;

function createProviderCollectionSelector<Section extends { items: object[] }>(
  section: string,
  selectSection: (state: AppState) => Section
) {
  const customFilterSelector = createCustomFiltersSelector(section);

  return createSelector(
    selectSection,
    (state: AppState) => customFilterSelector(asStateRecord(state)),
    (sectionState, customFilters) => {
      const state: StateRecord = { ...sectionState, customFilters };
      const filtered = filterCollection<Section['items'][number]>(sectionState.items, state);

      return {
        ...sectionState,
        customFilters,
        items: sortCollection(filtered, state),
        totalItems: sectionState.items.length
      };
    }
  );
}

type DynamicProjection = StateRecord & CollectionProjection<StateRecord>;

function createClientSideCollectionSelector(section: 'settings.downloadClients'): (state: AppState) => ProjectedProvider<DownloadClientAppState>;
function createClientSideCollectionSelector(section: 'settings.importLists'): (state: AppState) => ProjectedProvider<ImportListAppState>;
function createClientSideCollectionSelector(section: 'settings.indexers'): (state: AppState) => ProjectedProvider<IndexerAppState>;
function createClientSideCollectionSelector(section: string, uiSection?: string): (state: StateRecord) => DynamicProjection;
function createClientSideCollectionSelector(section: string, uiSection?: string):
  | ((state: AppState) => ProjectedProvider<DownloadClientAppState>)
  | ((state: AppState) => ProjectedProvider<ImportListAppState>)
  | ((state: AppState) => ProjectedProvider<IndexerAppState>)
  | ((state: StateRecord) => DynamicProjection) {
  // Already-typed provider views keep their actual resource shapes. The
  // remaining dynamic legacy sections validate only their shared row contract.
  if (uiSection === undefined) {
    if (section === 'settings.downloadClients') {
      return createProviderCollectionSelector(section, (state) => state.settings.downloadClients);
    }

    if (section === 'settings.importLists') {
      return createProviderCollectionSelector(section, (state) => state.settings.importLists);
    }

    if (section === 'settings.indexers') {
      return createProviderCollectionSelector(section, (state) => state.settings.indexers);
    }
  }

  return createSelector(
    (state: StateRecord) => sectionRecord(state, section),
    (state: StateRecord) => sectionRecord(state, uiSection),
    createCustomFiltersSelector(section, uiSection),
    (sectionState, uiSectionState, customFilters) => {
      const state: StateRecord = { ...sectionState, ...uiSectionState, customFilters };
      const items = recordItems(state.items);
      const filtered = filterCollection(items, state);

      return {
        ...sectionState,
        ...uiSectionState,
        customFilters,
        items: sortCollection(filtered, state),
        totalItems: items.length
      };
    }
  );
}

export default createClientSideCollectionSelector;
