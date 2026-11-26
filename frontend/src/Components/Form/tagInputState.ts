import { asStateRecord } from 'Store/StateTypes';
import split from 'Utilities/String/split';

export interface TagValue {
  id: string | number;
  name: string;
}

export interface TagOption {
  id?: string | number;
  name: string;
}

export interface TagRemoval {
  id?: string | number;
  index: number;
}

export function numericTags(value: unknown): number[] {
  return Array.isArray(value) ? value.filter((id: unknown): id is number => typeof id === 'number' && Number.isSafeInteger(id)) : [];
}

export function knownTags(value: unknown, source: 'store' | 'option'): Array<{ id: number; name: string }> {
  if (!Array.isArray(value)) {
    return [];
  }
  return value.flatMap((entry: unknown) => {
    const item = asStateRecord(entry);
    const id = source === 'store' ? item.id : item.key;
    const name = source === 'store' ? item.label : item.value;
    return typeof id === 'number' && Number.isSafeInteger(id) && typeof name === 'string' ? [{ id, name }] : [];
  }).sort((a, b) => a.name.localeCompare(b.name));
}

export function numericTagProjection(values: unknown, allTags: unknown, source: 'store' | 'option') {
  const value = numericTags(values);
  const options = knownTags(allTags, source);
  return {
    value,
    tags: value.map((id) => ({ id, name: options.find((tag) => tag.id === id)?.name ?? `Unknown (${id})` })),
    tagList: options.filter((tag) => !value.includes(tag.id)),
    allTags: options
  };
}

export function textTags(value: unknown): string[] {
  if (Array.isArray(value)) {
    return value.filter((tag: unknown): tag is string => typeof tag === 'string' && tag.length > 0);
  }
  return typeof value === 'string' ? split(value) : [];
}

export function removeTag<Value extends string | number>(values: readonly Value[], removal: TagRemoval): Value[] {
  const index = removal.id === undefined ? removal.index : values.findIndex((value) => value === removal.id);
  return index >= 0 && index < values.length ? values.filter((_, current) => current !== index) : [...values];
}
