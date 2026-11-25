import React from 'react';
import Label from 'Components/Label';
import EnhancedSelectInputSelectedValue, { type EnhancedSelectInputSelectedValueProps } from './EnhancedSelectInputSelectedValue';
import type { SelectOption } from './formTypes';
import styles from './HintedSelectInputSelectedValue.css';

export interface HintedSelectInputSelectedValueProps extends Omit<EnhancedSelectInputSelectedValueProps, 'children'> {
  value: React.ReactNode | readonly (string | number)[];
  values: readonly SelectOption[];
  hint?: React.ReactNode;
  isMultiSelect?: boolean;
  includeHint?: boolean;
}

function HintedSelectInputSelectedValue(props: HintedSelectInputSelectedValueProps) {
  const {
    value,
    values,
    hint,
    isMultiSelect = false,
    includeHint = true,
    ...otherProps
  } = props;

  const selected: unknown[] = isMultiSelect && Array.isArray(value) ? value : [];
  const selectedKeys = selected.filter((key): key is string | number => typeof key === 'string' || typeof key === 'number');
  const valuesMap = new Map(values.map((option) => [option.key, option]));

  return (
    <EnhancedSelectInputSelectedValue
      className={styles.selectedValue}
      {...otherProps}
    >
      <div className={styles.valueText}>
        {
          isMultiSelect ?
            selectedKeys.map((key) => {
              const option = valuesMap.get(key);
              const label = option ? option.value : key;
              return (
                <Label key={`${typeof key}:${key}`}>
                  {typeof label === 'function' ? label() : label}
                </Label>
              );
            }) :
            null
        }

        {
          isMultiSelect ? null : value
        }
      </div>

      {
        hint != null && includeHint ?
          <div className={styles.hintText}>
            {hint}
          </div> :
          null
      }
    </EnhancedSelectInputSelectedValue>
  );
}

export default HintedSelectInputSelectedValue;
