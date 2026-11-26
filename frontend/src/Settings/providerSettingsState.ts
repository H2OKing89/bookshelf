import { isEqual } from "lodash";
import type { FormProps } from 'Components/Form/Form';
import type { ValueChange } from 'Components/Form/formTypes';
import { all as alertKinds, type Kind } from 'Helpers/Props/kinds';
import type { ProviderFieldDraft, SettingsProjection } from 'Store/Selectors/selectSettings';
import { asStateRecord } from 'Store/StateTypes';
import { checkValue, numberValue, textValue } from '../Components/Form/formValues';
import { readDraft } from './settingDrafts';

export interface ProviderEditBaseProps extends FormProps {
  item: SettingsProjection;
  isSaving: boolean;
  saveError?: unknown;
  onInputChange: (change: ValueChange<unknown>) => void;
  onFieldChange: (change: ValueChange<unknown>) => void;
  onModalClose: () => void;
  onSavePress: () => void;
}

export interface ProviderEditProps extends ProviderEditBaseProps {
  advancedSettings: boolean;
  isFetching: boolean;
  error?: unknown;
  isTesting: boolean;
  onTestPress: () => void;
  onAdvancedSettingsPress: () => void;
}

interface ProviderField extends ProviderFieldDraft {
  label: string;
  type: string;
  helpText?: string;
  helpTextWarning?: string;
  helpLink?: string;
  placeholder?: string;
  advanced?: boolean;
  hidden?: string;
  selectOptionsProviderAction?: string;
}

// The provider receives its original projection (including unknown schema
// metadata). These narrowed copies are for rendering only.
export function providerFields(item: SettingsProjection): ProviderField[] {
  return (item.fields ?? []).map((field) => ({
    ...field,
    label: typeof field.label === 'string' ? field.label : field.name,
    type: typeof field.type === 'string' ? field.type : 'textbox',
    helpText: typeof field.helpText === 'string' ? field.helpText : undefined,
    helpTextWarning: typeof field.helpTextWarning === 'string' ? field.helpTextWarning : undefined,
    helpLink: typeof field.helpLink === 'string' ? field.helpLink : undefined,
    placeholder: typeof field.placeholder === 'string' ? field.placeholder : undefined,
    advanced: field.advanced === true,
    hidden: typeof field.hidden === 'string' ? field.hidden : undefined,
    selectOptionsProviderAction: typeof field.selectOptionsProviderAction === 'string' ? field.selectOptionsProviderAction : undefined
  }));
}

export function checkDraft(item: SettingsProjection, name: string) {
  return readDraft(item, name, checkValue);
}

export function textDraft(item: SettingsProjection, name: string) {
  return readDraft(item, name, textValue);
}

export function numberDraft(item: SettingsProjection, name: string) {
  return readDraft(item, name, numberValue);
}

export function tagsDraft(item: SettingsProjection, name = 'tags') {
  return readDraft(item, name, (value) => Array.isArray(value) ?
    value.filter((tag: unknown): tag is number => typeof tag === 'number' && Number.isSafeInteger(tag)) : []);
}

interface ProviderMessage {
  type: Kind;
  message: string;
}

export function providerMessage(value: unknown): ProviderMessage | null {
  const data = asStateRecord(value);
  const type = alertKinds.find((kind) => kind === data.type);
  return typeof data.message === 'string' && type !== undefined ? { type, message: data.message } : null;
}

export function providerEditDrafts(item: SettingsProjection) {
  return {
    id: numberDraft(item, 'id').value,
    implementationName: item.implementationName ?? '',
    name: textDraft(item, 'name'),
    enable: checkDraft(item, 'enable'),
    protocol: textDraft(item, 'protocol'),
    priority: numberDraft(item, 'priority'),
    fields: providerFields(item),
    tags: tagsDraft(item),
    message: readDraft(item, 'message', providerMessage).value
  };
}

// Closing a provider editor must compare the submitted draft to the saved
// item/schema, including dynamic field values, not just a truthy dirty flag.
export function providerHasChanges(sectionValue: unknown, id?: number): boolean {
  const section = asStateRecord(sectionValue);
  const items: unknown[] = Array.isArray(section.items) ? section.items : [];
  const saved = id ? asStateRecord(items.find((item) => asStateRecord(item).id === id)) :
    { name: '', ...asStateRecord(section.selectedSchema) };
  const fields: unknown[] = Array.isArray(saved.fields) ? saved.fields : [];
  const changes = asStateRecord(section.pendingChanges);
  return Object.entries(changes).some(([key, value]) => {
    if (key !== 'fields') {
      return !isEqual(saved[key], value);
    }

    return Object.entries(asStateRecord(value)).some(([name, fieldValue]) => {
      const field = fields.find((item) => asStateRecord(item).name === name);
      return !isEqual(asStateRecord(field).value, fieldValue);
    });
  });
}
