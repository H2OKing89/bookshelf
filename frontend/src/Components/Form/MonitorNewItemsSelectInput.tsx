import React from 'react';
import monitorNewItemsOptions from 'Utilities/Author/monitorNewItemsOptions';
import translate from 'Utilities/String/translate';
import SelectInput, { type SelectInputProps, type SelectOption } from './SelectInput';

export interface MonitorNewItemsSelectInputProps extends Omit<SelectInputProps, 'values'> {
  includeNoChange?: boolean;
  includeMixed?: boolean;
}

function MonitorNewItemsSelectInput(props: MonitorNewItemsSelectInputProps) {
  const {
    includeNoChange = false,
    includeMixed = false,
    ...otherProps
  } = props;

  const values: SelectOption[] = [...monitorNewItemsOptions];

  if (includeNoChange) {
    values.unshift({
      key: 'noChange',
      value: translate('NoChange'),
      isDisabled: true
    });
  }

  if (includeMixed) {
    values.unshift({
      key: 'mixed',
      value: '(Mixed)',
      isDisabled: true
    });
  }

  return (
    <SelectInput
      values={values}
      {...otherProps}
    />
  );
}

export default MonitorNewItemsSelectInput;
