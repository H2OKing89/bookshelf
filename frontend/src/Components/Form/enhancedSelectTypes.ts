import type { ComponentType, ReactNode } from 'react';
import type { EnhancedSelectInputOptionProps } from './EnhancedSelectInputOption';
import type { FormControlProps } from './formControlProps';
import type { SelectOption, ValueChange } from './formTypes';
import type { HintedSelectInputSelectedValueProps } from './HintedSelectInputSelectedValue';

export type SelectKey = string | number;

export interface EnhancedOption<Key extends SelectKey = SelectKey> extends SelectOption<Key> {
  hint?: ReactNode;
  parentKey?: Key;
  depth?: number;
  isHidden?: boolean;
  name?: string;
  freeSpace?: number | null;
  isMissing?: boolean;
}

export interface OptionRendererProps extends EnhancedSelectInputOptionProps {
  value: ReactNode;
  hint?: ReactNode;
  name?: string;
  freeSpace?: number | null;
  isMissing?: boolean;
  authorFolder?: string;
  isWindows?: boolean;
}

export interface SelectedValueRendererProps extends HintedSelectInputSelectedValueProps {
  name?: string;
  freeSpace?: number | null;
  authorFolder?: string;
  isWindows?: boolean;
  includeFreeSpace?: boolean;
}

interface CommonProps<Key extends SelectKey> extends FormControlProps {
  className?: string;
  disabledClassName?: string;
  name: string;
  values: readonly EnhancedOption<Key>[];
  isDisabled?: boolean;
  isFetching?: boolean;
  isEditable?: boolean;
  hasError?: boolean;
  hasWarning?: boolean;
  valueOptions?: Record<string, unknown>;
  selectedValueOptions?: Record<string, unknown>;
  selectedValueComponent?: ComponentType<SelectedValueRendererProps>;
  optionComponent?: ComponentType<OptionRendererProps>;
  onOpen?: () => void;
  onEditableChange?: (change: ValueChange<string>) => void;
}

export interface EnhancedScalarProps<Key extends SelectKey> extends CommonProps<Key> {
  value?: Key | null;
  onChange: (change: ValueChange<Key>) => void;
}

export interface EnhancedMultiProps<Key extends SelectKey> extends CommonProps<Key> {
  value: Key[];
  onChange: (change: ValueChange<Key[]>) => void;
}

export type EnhancedSelectInputProps<Key extends SelectKey = SelectKey> =
  EnhancedScalarProps<Key> | EnhancedMultiProps<Key>;

export function isMultiSelect<Key extends SelectKey>(props: Readonly<EnhancedSelectInputProps<Key>>): props is Readonly<EnhancedMultiProps<Key>> {
  return Array.isArray(props.value);
}

export function optionLabel(value: EnhancedOption['value']): ReactNode {
  return typeof value === 'function' ? value() : value;
}

export function isOptionDisabled<Key extends SelectKey>(option: EnhancedOption<Key>, value: Key | Key[] | null | undefined): boolean {
  return !!option.isDisabled || !!option.isHidden ||
    (option.parentKey !== undefined && Array.isArray(value) && value.includes(option.parentKey));
}

export function toggleSelectedKey<Key extends SelectKey>(selected: readonly Key[], options: readonly EnhancedOption<Key>[], key: Key): Key[] {
  if (selected.includes(key)) {
    return selected.filter((value) => value !== key);
  }

  const known = options.map((option) => option.key);
  const next = known.filter((value) => selected.includes(value) || value === key);
  // A refreshed provider may temporarily omit existing selections. Do not
  // silently discard them just because another option was selected.
  return [...next, ...selected.filter((value) => !known.includes(value))];
}
