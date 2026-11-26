import type { FormMessage } from 'Components/Form/formTypes';
import type { SettingsProjection } from 'Store/Selectors/selectSettings';
import { asStateRecord, isStateRecord, type StateRecord } from 'Store/StateTypes';
import { numberValue, textValue } from '../../../Components/Form/formValues';
import { getDraft, readDraft } from '../../settingDrafts';
import { adjustQualityDropMove, parseIndex, type QualityProfileDragMove } from './qualityProfileDragTypes';

interface QualityIdentity extends StateRecord {
  id: number;
  name: string;
}

export interface QualityLeaf extends StateRecord {
  id?: number | null;
  name?: string | null;
  quality: QualityIdentity;
  allowed: boolean;
  minSize?: number | null;
  preferredSize?: number | null;
  maxSize?: number | null;
  sizeUnit?: string | null;
}

export interface QualityGroup extends StateRecord {
  id: number;
  name: string;
  quality?: null;
  allowed: boolean;
  items: QualityLeaf[];
}

export type QualityProfileItem = QualityLeaf | QualityGroup;

export interface ProfileFormatItem extends StateRecord {
  id?: number;
  format: number;
  name: string;
  score: number | null;
}

export interface ProfileDraft<T> {
  value: T;
  errors: FormMessage[];
  warnings: FormMessage[];
  pending?: boolean;
}

export interface QualityProfileSummary {
  id: number;
  name: string;
  upgradeAllowed: boolean;
  cutoff: number | null;
  items: QualityProfileItem[];
}

export function isQualityLeaf(item: QualityProfileItem): item is QualityLeaf {
  return item.quality != null;
}

function optionalNumber(value: unknown): number | null | undefined {
  return value === undefined ? undefined : numberValue(value);
}

function optionalText(value: unknown): string | null | undefined {
  if (value === null) {
    return null;
  }
  return typeof value === 'string' ? value : undefined;
}

function readLeaf(value: unknown): QualityLeaf | null {
  const row = asStateRecord(value);
  const quality = asStateRecord(row.quality);
  const id = numberValue(quality.id);

  if (id === null || typeof quality.name !== 'string' || typeof row.allowed !== 'boolean') {
    return null;
  }

  const optionalNumbers = [row.id, row.minSize, row.preferredSize, row.maxSize];
  if (optionalNumbers.some((field) => field != null && numberValue(field) === null)) {
    return null;
  }

  return {
    ...row,
    id: optionalNumber(row.id),
    name: optionalText(row.name),
    quality: { ...quality, id, name: quality.name },
    allowed: row.allowed,
    minSize: optionalNumber(row.minSize),
    preferredSize: optionalNumber(row.preferredSize),
    maxSize: optionalNumber(row.maxSize),
    sizeUnit: optionalText(row.sizeUnit)
  };
}

// Reject an incomplete profile rather than filtering entries and accidentally
// transmitting only part of a profile when any item is edited.
export function parseQualityItems(value: unknown): QualityProfileItem[] | null {
  if (!Array.isArray(value)) {
    return null;
  }

  const result: QualityProfileItem[] = [];
  for (const entry of value) {
    if (!isStateRecord(entry)) {
      return null;
    }

    if (entry.quality != null) {
      const leaf = readLeaf(entry);
      if (!leaf) {
        return null;
      }
      result.push(leaf);
    } else {
      const id = numberValue(entry.id);
      if (id === null || typeof entry.name !== 'string' || typeof entry.allowed !== 'boolean' || !Array.isArray(entry.items)) {
        return null;
      }

      const items: QualityLeaf[] = [];
      for (const child of entry.items) {
        const leaf = readLeaf(child);
        if (!leaf) {
          return null;
        }
        items.push(leaf);
      }
      result.push({ ...entry, id, name: entry.name, quality: null, allowed: entry.allowed, items });
    }
  }

  return result;
}

export function parseFormatItems(value: unknown): ProfileFormatItem[] | null {
  if (!Array.isArray(value)) {
    return null;
  }

  const result: ProfileFormatItem[] = [];
  for (const entry of value) {
    const item = asStateRecord(entry);
    const format = numberValue(item.format);
    const score = numberValue(item.score);
    if (format === null || typeof item.name !== 'string' || (score === null && item.score !== null)) {
      return null;
    }
    result.push({ ...item, id: numberValue(item.id) ?? undefined, format, name: item.name, score });
  }
  return result;
}

export function qualityProfileDrafts(item: SettingsProjection) {
  const itemRows = parseQualityItems(getDraft(item, 'items')?.value);
  const formatRows = parseFormatItems(getDraft(item, 'formatItems')?.value);
  const items = readDraft(item, 'items', () => itemRows ?? []);
  const formatItems = readDraft(item, 'formatItems', () => formatRows ?? []);

  return {
    id: numberValue(getDraft(item, 'id')?.value),
    name: readDraft(item, 'name', textValue),
    upgradeAllowed: readDraft(item, 'upgradeAllowed', (value) => typeof value === 'boolean' ? value : null),
    cutoff: readDraft(item, 'cutoff', numberValue),
    minFormatScore: readDraft(item, 'minFormatScore', numberValue),
    cutoffFormatScore: readDraft(item, 'cutoffFormatScore', numberValue),
    minUpgradeFormatScore: readDraft(item, 'minUpgradeFormatScore', numberValue),
    items,
    formatItems,
    valid: itemRows !== null && formatRows !== null
  };
}

export function parseProfileSummary(value: unknown): QualityProfileSummary | null {
  const profile = asStateRecord(value);
  const id = numberValue(profile.id);
  const items = parseQualityItems(profile.items);
  if (id === null || typeof profile.name !== 'string' || typeof profile.upgradeAllowed !== 'boolean' || items === null) {
    return null;
  }

  return { id, name: profile.name, upgradeAllowed: profile.upgradeAllowed, cutoff: numberValue(profile.cutoff), items };
}

export function qualityItemId(item: QualityProfileItem) {
  return isQualityLeaf(item) ? item.quality.id : item.id;
}

export function qualityItemName(item: QualityProfileItem) {
  return isQualityLeaf(item) ? item.quality.name : item.name;
}

export function qualityOptions(items: readonly QualityProfileItem[]) {
  return items.filter((item) => item.allowed).map((item) => ({ key: qualityItemId(item), value: qualityItemName(item) })).reverse();
}

export function safeCutoff(items: readonly QualityProfileItem[], cutoff: number | null) {
  if (cutoff !== null && items.some((item) => item.allowed && qualityItemId(item) === cutoff)) {
    return cutoff;
  }

  const firstAllowed = items.find((item) => item.allowed);
  return firstAllowed ? qualityItemId(firstAllowed) : null;
}

export function nextGroupId(items: readonly QualityProfileItem[]) {
  return Math.max(1000, ...items.filter((item): item is QualityGroup => !isQualityLeaf(item)).map((item) => item.id)) + 1;
}

export type SizeRuleKey = 'minSize' | 'preferredSize' | 'maxSize';
export function updateSizeRule(items: readonly QualityProfileItem[], qualityId: number, field: SizeRuleKey, value: number | null): QualityProfileItem[] {
  return items.map((item) => {
    if (isQualityLeaf(item)) {
      return item.quality.id === qualityId ? { ...item, [field]: value ?? 0 } : item;
    }
    return { ...item, items: item.items.map((child) => child.quality.id === qualityId ? { ...child, [field]: value ?? 0 } : child) };
  });
}

export function moveQualityItem(items: QualityProfileItem[], move: QualityProfileDragMove): boolean {
  const adjusted = adjustQualityDropMove(move);
  if (!adjusted) {
    return false;
  }

  const [dragGroupIndex, dragItemIndex] = parseIndex(adjusted.dragQualityIndex);
  const [dropGroupIndex, dropItemIndex] = parseIndex(adjusted.dropQualityIndex);
  if (![dragItemIndex, dropItemIndex, dragGroupIndex ?? 0, dropGroupIndex ?? 0].every((index) => Number.isSafeInteger(index) && index >= 0)) {
    return false;
  }

  const dragGroup = dragGroupIndex === null ? undefined : items[dragGroupIndex];
  const dropGroup = dropGroupIndex === null ? undefined : items[dropGroupIndex];
  if ((dragGroupIndex !== null && (!dragGroup || isQualityLeaf(dragGroup))) ||
      (dropGroupIndex !== null && (!dropGroup || isQualityLeaf(dropGroup)))) {
    return false;
  }

  const sourceGroup = dragGroup && !isQualityLeaf(dragGroup) ? dragGroup : undefined;
  const destinationGroup = dropGroup && !isQualityLeaf(dropGroup) ? dropGroup : undefined;
  const source = sourceGroup ? sourceGroup.items : items;
  const item = source[dragItemIndex];
  const destination = destinationGroup ? destinationGroup.items : items;
  if (!item || dropItemIndex > destination.length || (destinationGroup && !isQualityLeaf(item)) ||
      (sourceGroup && sourceGroup === destinationGroup && sourceGroup.items.length === 1)) {
    return false;
  }

  source.splice(dragItemIndex, 1);
  if (sourceGroup && !sourceGroup.items.length && dragGroupIndex !== null) {
    items.splice(dragGroupIndex, 1);
  }

  if (destinationGroup && isQualityLeaf(item)) {
    destinationGroup.items.splice(dropItemIndex, 0, item);
  } else {
    items.splice(dropItemIndex, 0, item);
  }
  return true;
}
