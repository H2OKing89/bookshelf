import { asStateRecord, isStateRecord } from 'Store/StateTypes';
import sortByName from 'Utilities/Array/sortByName';
import translate from 'Utilities/String/translate';
import type { SelectOption, ValueChange } from './formTypes';
import type { SelectInputProps } from './SelectInput';

interface Profile {
  id: number;
  name: string;
}

export interface ProfileSelectProps extends Omit<SelectInputProps, 'values' | 'onChange'> {
  includeNoChange?: boolean;
  includeNoChangeDisabled?: boolean;
  includeMixed?: boolean;
  onChange: (change: ValueChange<number | 'noChange'>) => void;
}

export function profiles(section: unknown): Profile[] {
  const items = asStateRecord(section).items;

  return Array.isArray(items) ? items.filter((item): item is Profile =>
    isStateRecord(item) && typeof item.id === 'number' && Number.isFinite(item.id) && typeof item.name === 'string'
  ).sort(sortByName) : [];
}

export function profileOptions(items: readonly Profile[], includeNoChange = false, includeNoChangeDisabled = true, includeMixed = false): SelectOption[] {
  const values: SelectOption[] = items.map(({ id, name }) => ({ key: id, value: name }));

  if (includeNoChange) {
    values.unshift({ key: 'noChange', value: translate('NoChange'), isDisabled: includeNoChangeDisabled });
  }
  if (includeMixed) {
    values.unshift({ key: 'mixed', value: '(Mixed)', isDisabled: true });
  }

  return values;
}

export function getProfileDefault(value: string | number | null | undefined, values: readonly SelectOption[]): number | undefined {
  if (value != null && value !== '' && values.some((option) => String(option.key) === String(value))) {
    return undefined;
  }

  const first = values.find((option) => typeof option.key === 'number');
  return typeof first?.key === 'number' ? first.key : undefined;
}

export function parseProfileValue(value: string): number | 'noChange' | undefined {
  if (value === 'noChange') {
    return value;
  }

  const parsed = Number(value);
  return value.trim() && Number.isSafeInteger(parsed) ? parsed : undefined;
}
