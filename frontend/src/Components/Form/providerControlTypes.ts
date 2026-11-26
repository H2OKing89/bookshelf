import { asStateRecord, isStateRecord, type StateRecord } from 'Store/StateTypes';
import type { FormControlProps } from './formControlProps';
import type { ValueChange } from './formTypes';

export type ProviderKey = string | number;

export interface ProviderControlProps extends FormControlProps {
  provider: string;
  providerData: unknown;
  name: string;
  className?: string;
  hasError?: boolean;
  hasWarning?: boolean;
  value?: unknown;
  onChange: (change: ValueChange<unknown>) => void;
}

export function isProviderKey(value: unknown): value is ProviderKey {
  return typeof value === 'string' || (typeof value === 'number' && Number.isFinite(value));
}

export function providerKeys(value: unknown): ProviderKey[] {
  return Array.isArray(value) ? value.filter(isProviderKey) : [];
}

export function providerFields(data: unknown): StateRecord[] {
  const fields = asStateRecord(data).fields;
  return Array.isArray(fields) ? fields.filter(isStateRecord) : [];
}

export interface ProviderItem {
  id: ProviderKey;
  name: string;
}

export function providerItems(value: unknown): ProviderItem[] {
  return Array.isArray(value) ? value.flatMap((item: unknown) => {
    if (!isStateRecord(item) || !isProviderKey(item.id) || typeof item.name !== 'string') {
      return [];
    }

    return [{ id: item.id, name: item.name }];
  }) : [];
}
