import { asStateRecord, isStateRecord } from 'Store/StateTypes';

export interface TagSummary { id: number; label: string }
interface NamedCard { id: number; name: string }
interface TaggedCard extends NamedCard { tags: number[]; priority: number }
export interface IndexerCard extends TaggedCard {
  supportsRss: boolean;
  supportsSearch: boolean;
  enableRss: boolean;
  enableAutomaticSearch: boolean;
  enableInteractiveSearch: boolean;
}
export interface DownloadClientCard extends TaggedCard { enable: boolean }
export interface ImportListCard extends NamedCard { enableAutomaticAdd: boolean; shouldSearch: boolean; minRefreshInterval: string }

function numericId(value: unknown): value is number {
  return typeof value === 'number' && Number.isSafeInteger(value) && value > 0;
}

function named(value: unknown): NamedCard | null {
  return isStateRecord(value) && numericId(value.id) && typeof value.name === 'string' ? { id: value.id, name: value.name } : null;
}

function tags(value: unknown) {
  return Array.isArray(value) ? value.filter(numericId) : [];
}

export function tagSummaries(value: unknown): TagSummary[] {
  return Array.isArray(value) ? value.flatMap((item: unknown) => isStateRecord(item) && numericId(item.id) && typeof item.label === 'string' ? [{ id: item.id, label: item.label }] : []) : [];
}

function sortedCards<T extends NamedCard>(items: unknown, project: (record: ReturnType<typeof asStateRecord>, card: NamedCard) => T): T[] {
  return (Array.isArray(items) ? items : []).flatMap((value: unknown) => {
    const card = named(value);
    return card ? [project(asStateRecord(value), card)] : [];
  }).sort((a, b) => a.name.localeCompare(b.name));
}

export function indexerCards(items: unknown): IndexerCard[] {
  return sortedCards(items, (item, card) => ({ ...card,
    priority: typeof item.priority === 'number' && Number.isFinite(item.priority) ? item.priority : 25,
    tags: tags(item.tags),
    supportsRss: item.supportsRss === true,
    supportsSearch: item.supportsSearch === true,
    enableRss: item.enableRss === true,
    enableAutomaticSearch: item.enableAutomaticSearch === true,
    enableInteractiveSearch: item.enableInteractiveSearch === true
  }));
}

export function downloadClientCards(items: unknown): DownloadClientCard[] {
  return sortedCards(items, (item, card) => ({ ...card, tags: tags(item.tags),
    priority: typeof item.priority === 'number' && Number.isFinite(item.priority) ? item.priority : 1,
    enable: item.enable === true
  }));
}

export function importListCards(items: unknown): ImportListCard[] {
  return sortedCards(items, (item, card) => ({ ...card,
    enableAutomaticAdd: item.enableAutomaticAdd === true,
    shouldSearch: item.shouldSearch === true,
    minRefreshInterval: typeof item.minRefreshInterval === 'string' ? item.minRefreshInterval : ''
  }));
}
