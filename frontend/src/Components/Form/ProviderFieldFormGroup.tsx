import React from 'react';
import FormGroup from 'Components/Form/FormGroup';
import FormInputGroup from 'Components/Form/FormInputGroup';
import FormLabel from 'Components/Form/FormLabel';
import type { FormMessage, ValueChange } from 'Components/Form/formTypes';
import { inputTypes } from 'Helpers/Props';
import { isStateRecord } from 'Store/StateTypes';
import { checkValue, numberValue } from './formValues';

interface ProviderFieldFormGroupProps {
  advancedSettings?: boolean;
  name: string;
  label: string;
  helpText?: string;
  helpTextWarning?: string;
  helpLink?: string;
  placeholder?: string;
  value?: unknown;
  type: string;
  advanced?: boolean;
  hidden?: string;
  pending?: boolean;
  errors?: readonly FormMessage[];
  warnings?: readonly FormMessage[];
  selectOptions?: unknown;
  selectOptionsProviderAction?: string;
  onChange: (change: ValueChange<unknown>) => void;
  [key: string]: unknown;
}

function getType({ type, selectOptionsProviderAction }: ProviderFieldFormGroupProps) {
  switch (type) {
    case 'captcha':
      return inputTypes.CAPTCHA;
    case 'checkbox':
      return inputTypes.CHECK;
    case 'device':
      return inputTypes.DEVICE;
    case 'bookshelf':
      return inputTypes.BOOKSHELF;
    case 'password':
      return inputTypes.PASSWORD;
    case 'number':
      return inputTypes.NUMBER;
    case 'path':
      return inputTypes.PATH;
    case 'filePath':
      return inputTypes.PATH;
    case 'select':
      if (selectOptionsProviderAction) {
        return inputTypes.DYNAMIC_SELECT;
      }
      return inputTypes.SELECT;
    case 'tag':
      return inputTypes.TEXT_TAG;
    case 'tagSelect':
      return inputTypes.TAG_SELECT;
    case 'textbox':
      return inputTypes.TEXT;
    case 'oAuth':
      return inputTypes.OAUTH;
    default:
      return inputTypes.TEXT;
  }
}

type SelectKey = string | number;

function isSelectKey(value: unknown): value is SelectKey {
  return typeof value === 'string' || (typeof value === 'number' && Number.isFinite(value));
}

export function getSelectValues(selectOptions: unknown) {
  if (!Array.isArray(selectOptions)) {
    return [];
  }

  return selectOptions.flatMap((option: unknown) => {
    if (!isStateRecord(option) || !isSelectKey(option.value) || typeof option.name !== 'string') {
      return [];
    }

    return [{
      key: option.value,
      value: option.name,
      hint: typeof option.hint === 'string' ? option.hint : undefined
    }];
  });
}

function providerTextValue(value: unknown) {
  if (isSelectKey(value)) {
    return value;
  }
  if (Array.isArray(value) && value.every((part: unknown) => typeof part === 'string')) {
    return value as string[];
  }
  return null;
}

function ProviderFieldFormGroup(props: ProviderFieldFormGroupProps) {
  const {
    advancedSettings = false,
    name,
    label,
    helpText,
    helpTextWarning,
    helpLink,
    placeholder,
    value,
    type,
    advanced,
    hidden,
    pending,
    errors,
    warnings,
    selectOptions,
    onChange,
    ...otherProps
  } = props;

  if (
    hidden === 'hidden' ||
    (hidden === 'hiddenIfNotSet' && (value == null || value === ''))
  ) {
    return null;
  }

  const common = {
    name,
    label,
    helpText,
    helpTextWarning,
    helpLink,
    placeholder,
    errors,
    warnings,
    pending,
    onChange,
    ...otherProps
  };
  const inputType = getType(props);
  const values = getSelectValues(selectOptions);
  let input: React.ReactElement | null = null;

  switch (inputType) {
    case inputTypes.CHECK:
      input = <FormInputGroup {...common} type={inputType} value={checkValue(value)} />;
      break;
    case inputTypes.NUMBER:
      input = <FormInputGroup {...common} type={inputType} value={numberValue(value)} />;
      break;
    case inputTypes.TEXT:
    case inputTypes.PASSWORD:
      input = <FormInputGroup {...common} type={inputType} value={providerTextValue(value)} />;
      break;
    case inputTypes.SELECT:
      input = Array.isArray(value) ?
        <FormInputGroup {...common} type={inputType} value={value.filter(isSelectKey)} values={values} /> :
        <FormInputGroup {...common} type={inputType} value={isSelectKey(value) ? value : null} values={values} />;
      break;
    case inputTypes.TAG_SELECT:
      input = <FormInputGroup
        {...common}
        type={inputType}
        value={Array.isArray(value) ? value.filter((tag: unknown): tag is number => typeof tag === 'number' && Number.isFinite(tag)) : []}
        values={values}
      />;
      break;
    default:
      input = <FormInputGroup
        {...common}
        type={inputType}
        value={value}
        values={values}
        includeFiles={type === 'filePath' ? true : undefined}
      />;
      break;
  }

  return (
    <FormGroup
      advancedSettings={advancedSettings}
      isAdvanced={advanced}
    >
      <FormLabel>{label}</FormLabel>
      {input}
    </FormGroup>
  );
}

export default ProviderFieldFormGroup;
