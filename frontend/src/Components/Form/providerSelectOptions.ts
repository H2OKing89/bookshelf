import { asStateRecord, isStateRecord } from 'Store/StateTypes';
import titleCase from 'Utilities/String/titleCase';
import type { EnhancedOption } from './enhancedSelectTypes';

export function numericChoice(value: unknown): number | null | undefined {
  if (value === null) {
    return null;
  }

  if (typeof value === 'number') {
    return Number.isSafeInteger(value) ? value : undefined;
  }

  if (typeof value === 'string' && value.trim()) {
    const parsed = Number(value);
    return Number.isSafeInteger(parsed) ? parsed : undefined;
  }

  return undefined;
}

export function providerSelectOptions(source: unknown, includeAny = false, protocol?: string): EnhancedOption<number>[] {
  const items = asStateRecord(source).items;
  const entries = Array.isArray(items) ? items.flatMap((entry: unknown) => {
    const item = asStateRecord(entry);

    if (typeof item.id !== 'number' || !Number.isSafeInteger(item.id) ||
        typeof item.name !== 'string' || (protocol !== undefined && item.protocol !== protocol)) {
      return [];
    }

    return [{ key: item.id, value: item.name, hint: protocol === undefined ? undefined : `(${item.id})` }];
  }).sort((a, b) => a.value.localeCompare(b.value)) : [];

  if (includeAny) {
    entries.unshift({ key: 0, value: '(Any)', hint: undefined });
  }

  return entries;
}

export function editionOptions(projection: unknown) {
  const raw = asStateRecord(projection).value;
  const editions: unknown[] = Array.isArray(raw) ? raw : [];
  const values = editions.flatMap((entry) => {
    const edition = asStateRecord(entry);
    if (typeof edition.foreignEditionId !== 'string' || typeof edition.title !== 'string') {
      return [];
    }

    let value = edition.title;
    if (typeof edition.disambiguation === 'string' && edition.disambiguation) {
      value += ` (${titleCase(edition.disambiguation)})`;
    }

    const extras = [edition.language, edition.publisher, edition.isbn13, edition.format]
      .filter((extra): extra is string => typeof extra === 'string' && !!extra);
    if (typeof edition.pageCount === 'number' && edition.pageCount > 0) {
      extras.push(`${edition.pageCount}p`);
    }
    if (extras.length) {
      value += ` [${extras.join(', ')}]`;
    }

    return [{ key: edition.foreignEditionId, value }];
  }).sort((a, b) => a.value.localeCompare(b.value));

  const monitored = asStateRecord(editions.find((entry) => asStateRecord(entry).monitored === true));
  return { values, value: typeof monitored.foreignEditionId === 'string' ? monitored.foreignEditionId : undefined };
}

export function selectEdition(projection: unknown, identity: string): unknown[] | undefined {
  const raw = asStateRecord(projection).value;
  if (!Array.isArray(raw) || !raw.some((entry: unknown) => asStateRecord(entry).foreignEditionId === identity)) {
    return undefined;
  }

  // Keep the original item fields so a selection never rewrites metadata.
  return raw.map((entry: unknown) => isStateRecord(entry) ?
    { ...entry, monitored: entry.foreignEditionId === identity } : entry);
}
