import React from 'react';
import monitorOptions from 'Utilities/Author/monitorOptions';
import translate from 'Utilities/String/translate';
import SelectInput, { type SelectInputProps, type SelectOption } from './SelectInput';

export interface MonitorBooksSelectInputProps extends Omit<SelectInputProps, 'values'> {
  includeNoChange?: boolean;
  includeMixed?: boolean;
  includeSpecificBook?: boolean;
}

function MonitorBooksSelectInput(props: MonitorBooksSelectInputProps) {
  const {
    includeNoChange = false,
    includeMixed = false,
    includeSpecificBook = false,
    ...otherProps
  } = props;

  const values: SelectOption[] = [...monitorOptions];

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

  if (includeSpecificBook) {
    values.push({
      key: 'specificBook',
      value: 'Only This Book'
    });
  }

  return (
    <SelectInput
      values={values}
      {...otherProps}
    />
  );
}

export default MonitorBooksSelectInput;
