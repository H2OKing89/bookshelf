import type { FormMessage } from 'Components/Form/formTypes';
import { asStateRecord, isStateRecord, type StateRecord } from 'Store/StateTypes';

export interface ValidationFailure extends StateRecord {
  propertyName: string;
  errorMessage: string;
  isWarning: boolean;
  infoLink?: string;
  detailedDescription?: string;
}

export interface SettingDraft {
  value: unknown;
  previousValue?: unknown;
  pending?: boolean;
  errors: FormMessage[];
  warnings: FormMessage[];
}

export interface ProviderFieldDraft extends SettingDraft {
  [key: string]: unknown;
  name: string;
  pending: boolean;
}

export interface SettingsProjection {
  [key: string]: SettingDraft | ProviderFieldDraft[] | string | undefined;
  implementationName?: string;
  fields?: ProviderFieldDraft[];
}

function validationFailures(saveError: unknown): ValidationFailure[] {
  if (!isStateRecord(saveError) || saveError.status !== 400 || !Array.isArray(saveError.responseJSON)) {
    return [];
  }

  const failures: unknown[] = saveError.responseJSON;
  const result: ValidationFailure[] = [];

  failures.forEach((failure) => {
    if (!isStateRecord(failure) || typeof failure.errorMessage !== 'string') {
      return;
    }

    result.push({
      ...failure,
      propertyName: typeof failure.propertyName === 'string' ? failure.propertyName : '',
      errorMessage: failure.errorMessage,
      isWarning: failure.isWarning === true,
      infoLink: typeof failure.infoLink === 'string' ? failure.infoLink : undefined,
      detailedDescription: typeof failure.detailedDescription === 'string' ? failure.detailedDescription : undefined
    });
  });

  return result;
}

function mapFailure(failure: ValidationFailure): FormMessage {
  return {
    message: failure.errorMessage,
    link: failure.infoLink,
    detailedMessage: failure.detailedDescription
  };
}

function consumeFailures(failures: ValidationFailure[], key: string) {
  const field = key.toLowerCase();
  const matched: ValidationFailure[] = [];

  // Consume only matches, in original response order. The source error body
  // is never mutated; unmatched failures remain visible at form level.
  for (let index = failures.length - 1; index >= 0; index--) {
    if (failures[index].propertyName.toLowerCase() === field) {
      matched.unshift(...failures.splice(index, 1));
    }
  }

  return {
    errors: matched.filter((failure) => !failure.isWarning).map(mapFailure),
    warnings: matched.filter((failure) => failure.isWarning).map(mapFailure)
  };
}

function selectSettings(sourceItem: unknown, sourceChanges: unknown, saveError: unknown) {
  const item = asStateRecord(sourceItem);
  const pendingChanges = asStateRecord(sourceChanges);
  const failures = validationFailures(saveError);

  // A pending setting not yet returned by the server is still editable.
  const allSettings = { ...item, ...pendingChanges };
  const entries: [string, SettingsProjection[string]][] = [];

  Object.keys(allSettings).forEach((key) => {
    if (key === 'fields') {
      return;
    }

    if (key === 'implementationName') {
      const name = item[key];
      entries.push([key, typeof name === 'string' ? name : undefined]);
      return;
    }

    const setting: SettingDraft = {
      value: item[key],
      ...consumeFailures(failures, key)
    };

    if (Object.hasOwn(pendingChanges, key)) {
      setting.previousValue = setting.value;
      setting.value = pendingChanges[key];
      setting.pending = true;
    }

    entries.push([key, setting]);
  });

  const fields: ProviderFieldDraft[] = [];
  const pendingFields = asStateRecord(pendingChanges.fields);
  const sourceFields: unknown[] = Array.isArray(item.fields) ? item.fields : [];

  sourceFields.forEach((source) => {
    if (!isStateRecord(source) || typeof source.name !== 'string') {
      return;
    }

    const field: ProviderFieldDraft = {
      ...source,
      name: source.name,
      value: source.value,
      pending: false,
      ...consumeFailures(failures, source.name)
    };

    if (Object.hasOwn(pendingFields, field.name)) {
      field.previousValue = field.value;
      field.value = pendingFields[field.name];
      field.pending = true;
    }

    fields.push(field);
  });

  if (fields.length) {
    entries.push(['fields', fields]);
  }

  const settings: SettingsProjection = Object.fromEntries(entries);

  return {
    settings,
    validationErrors: failures.filter((failure) => !failure.isWarning),
    validationWarnings: failures.filter((failure) => failure.isWarning),
    hasPendingChanges: Object.keys(pendingChanges).length !== 0,
    hasSettings: entries.length !== 0,
    pendingChanges
  };
}

export default selectSettings;
