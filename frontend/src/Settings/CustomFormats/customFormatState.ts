import _ from 'lodash';
import type { SettingsProjection } from 'Store/Selectors/selectSettings';
import { asStateRecord, isStateRecord, type StateRecord } from 'Store/StateTypes';
import { getDraft } from '../settingDrafts';

export interface SpecificationLabel {
  name: string;
  required: boolean;
  negate: boolean;
}

export interface SpecificationCard extends SpecificationLabel {
  id: number;
  implementation: string;
  implementationName: string;
}

export interface FormatCard {
  id: number;
  name: string;
  specifications: SpecificationLabel[];
}

export interface SpecificationChoice {
  implementation: string;
  name?: string;
}

export interface SpecificationSchema {
  implementation: string;
  implementationName: string;
  infoLink?: string;
  presets: { name: string }[];
}

export interface ImportDraft {
  pendingChanges: StateRecord;
  specifications: StateRecord[];
}

function list(value: unknown): unknown[] {
  return Array.isArray(value) ? value : [];
}

function numericId(value: unknown): value is number {
  return typeof value === 'number' && Number.isSafeInteger(value) && value > 0;
}

function label(value: StateRecord): SpecificationLabel | null {
  return typeof value.name === 'string' ? { name: value.name, required: value.required === true, negate: value.negate === true } : null;
}

export function formatCards(values: unknown): FormatCard[] {
  return list(values).flatMap((value) => {
    if (!isStateRecord(value) || !numericId(value.id) || typeof value.name !== 'string') {
      return [];
    }
    const specifications = list(value.specifications).flatMap((item) => {
      const result = label(asStateRecord(item));
      return result ? [result] : [];
    });
    return [{ id: value.id, name: value.name, specifications }];
  });
}

export function specificationCards(values: unknown): SpecificationCard[] {
  return list(values).flatMap((value) => {
    const record = asStateRecord(value);
    const result = label(record);
    return result && numericId(record.id) && typeof record.implementation === 'string' ?
      [{ ...result, id: record.id, implementation: record.implementation, implementationName: typeof record.implementationName === 'string' ? record.implementationName : record.implementation }] : [];
  });
}

export function specificationSchemas(values: unknown): SpecificationSchema[] {
  return list(values).flatMap((value) => {
    const item = asStateRecord(value);
    if (typeof item.implementation !== 'string') {
      return [];
    }
    return [{
      implementation: item.implementation,
      implementationName: typeof item.implementationName === 'string' ? item.implementationName : item.implementation,
      infoLink: typeof item.infoLink === 'string' ? item.infoLink : undefined,
      presets: list(item.presets).flatMap((preset) => isStateRecord(preset) && typeof preset.name === 'string' ? [{ name: preset.name }] : [])
    }];
  });
}

function knownValues(record: StateRecord, keys: string[]) {
  return Object.fromEntries(keys.filter((key) => Object.hasOwn(record, key)).map((key) => [key, record[key]]));
}

// Validate the entire import against the current schema before touching the
// editor. Extra guide metadata is not a provider setting; only actual custom
// format and condition keys become draft values. Preserve explicit field values
// (including false, zero, null and arrays) and every schema-only field property.
export function parseCustomFormatImport(text: string, schemaValue: unknown): ImportDraft {
  const source: unknown = JSON.parse(text);
  if (!isStateRecord(source)) {
    throw new Error('Custom format JSON must be an object');
  }
  const pendingChanges = knownValues(source, ['name', 'includeCustomFormatWhenRenaming']);
  if (Object.hasOwn(pendingChanges, 'name') && typeof pendingChanges.name !== 'string' ||
      Object.hasOwn(pendingChanges, 'includeCustomFormatWhenRenaming') && pendingChanges.includeCustomFormatWhenRenaming !== null && typeof pendingChanges.includeCustomFormatWhenRenaming !== 'boolean') {
    throw new Error('Invalid custom format name or rename setting');
  }
  const raw = Object.hasOwn(source, 'specifications') ? source.specifications : [];
  if (!Array.isArray(raw)) {
    throw new Error('Custom format specifications must be an array');
  }
  const schema = list(schemaValue);
  const specifications = raw.map((value: unknown, index): StateRecord => {
    if (!isStateRecord(value) || typeof value.implementation !== 'string') {
      throw new Error('Invalid custom format condition');
    }
    const template = schema.find((candidate) => isStateRecord(candidate) && candidate.implementation === value.implementation);
    if (!isStateRecord(template)) {
      throw new Error(`Unknown Custom Format condition '${value.implementation}'`);
    }
    const fields = Object.hasOwn(value, 'fields') ? value.fields : {};
    if (!isStateRecord(fields) || !Array.isArray(template.fields) || !template.fields.every((field: unknown) => isStateRecord(field) && typeof field.name === 'string')) {
      throw new Error(`Invalid options for condition '${value.implementation}'`);
    }
    const knownFields = new Set(template.fields.map((field: StateRecord) => field.name));
    const unknown = Object.keys(fields).find((key) => !knownFields.has(key));
    if (unknown) {
      throw new Error(`Unknown option '${unknown}' for condition '${value.implementation}'`);
    }
    const edits = knownValues(value, ['name', 'required', 'negate']);
    if (Object.hasOwn(edits, 'name') && typeof edits.name !== 'string' ||
        ['required', 'negate'].some((key) => Object.hasOwn(edits, key) && typeof edits[key] !== 'boolean')) {
      throw new Error(`Invalid name or flags for condition '${value.implementation}'`);
    }
    const copied = _.cloneDeep(template);
    delete copied.presets;
    return {
      ...copied,
      ...edits,
      // These IDs have no server-side meaning; each active editor needs unique
      // temporary identities even if imported JSON supplies repeated IDs.
      id: index + 2,
      fields: template.fields.map((field: StateRecord) => ({ ..._.cloneDeep(field), ...(typeof field.name === 'string' && Object.hasOwn(fields, field.name) ? { value: _.cloneDeep(fields[field.name]) } : {}) }))
    };
  });
  return { pendingChanges, specifications };
}

export function exportCustomFormat(item: SettingsProjection) {
  const result: StateRecord = {};
  for (const key of ['name', 'includeCustomFormatWhenRenaming']) {
    const draft = getDraft(item, key);
    if (draft) {
      result[key] = draft.value;
    }
  }
  const specs = getDraft(item, 'specifications')?.value;
  result.specifications = list(specs).filter(isStateRecord).map((spec) => ({
    ...knownValues(spec, ['implementation', 'name', 'negate', 'required']),
    fields: Object.fromEntries(list(spec.fields).flatMap((field) => isStateRecord(field) && typeof field.name === 'string' ? [[field.name, field.value]] : []))
  }));
  return JSON.stringify(result, null, 2);
}
