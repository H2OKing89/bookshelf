import type Author from 'Author/Author';
import type Command from 'Commands/Command';
import type { StateRecord } from 'Store/StateTypes';
import type UiSettings from 'typings/Settings/UiSettings';
import type SystemStatus from 'typings/SystemStatus';
import type { ResourceCollection } from './selectById';

export interface NamedStoreProfile extends StateRecord {
  id: number;
  name: string;
}
export interface AuthorCollection extends ResourceCollection<Author> {
  error?: unknown;
  isFetching?: boolean;
  isPopulated?: boolean;
}
export interface CommandCollection {
  items: readonly Command[];
}
export interface StoreProfiles {
  qualityProfiles: ResourceCollection<NamedStoreProfile>;
  metadataProfiles: ResourceCollection<NamedStoreProfile>;
}
export interface StoreDimensions {
  width: number;
  height: number;
  isExtraSmallScreen: boolean;
  isSmallScreen: boolean;
  isMediumScreen: boolean;
  isLargeScreen: boolean;
}
export interface StoreHealth extends StateRecord {
  source: string;
  type: string;
  message: string;
}
export interface StatusState { system: { status: { item: SystemStatus } } }
export interface UiState { settings: { ui: { item: UiSettings } } }
