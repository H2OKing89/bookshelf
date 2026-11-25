
import type React from 'react';

export interface ValueChange<Value> {
  name: string;
  value: Value;
}

export interface SelectOption<Key extends string | number = string | number> extends Omit<React.OptionHTMLAttributes<HTMLOptionElement>, 'value' | 'children' | 'disabled'> {
  key: Key;
  value: React.ReactNode | (() => React.ReactNode);
  isDisabled?: boolean;
}

export interface FormMessage {
  message: string;
  link?: string;
  detailedMessage?: string;
}

export interface ValidationMessage {
  errorMessage: string;
}

export interface FieldAssociation {
  name: string;
  inputId: string;
  labelId: string;
  isGroup: boolean;
}

export type SelectionChangeHandler = (start: number | null, end: number | null) => void;
