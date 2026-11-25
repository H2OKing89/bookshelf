import classNames from 'classnames';
import React from 'react';
import EnhancedSelectInputOption, { type EnhancedSelectInputOptionProps } from './EnhancedSelectInputOption';
import styles from './HintedSelectInputOption.css';

export interface HintedSelectInputOptionProps extends Omit<EnhancedSelectInputOptionProps, 'children'> {
  value: React.ReactNode;
  hint?: React.ReactNode;
}

function HintedSelectInputOption(props: HintedSelectInputOptionProps) {
  const {
    id,
    value,
    hint,
    depth,
    isSelected,
    isDisabled,
    isMultiSelect,
    isMobile,
    ...otherProps
  } = props;

  return (
    <EnhancedSelectInputOption
      id={id}
      depth={depth}
      isSelected={isSelected}
      isDisabled={isDisabled}
      isHidden={isDisabled}
      isMultiSelect={isMultiSelect}
      isMobile={isMobile}
      {...otherProps}
    >
      <div className={classNames(
        styles.optionText,
        isMobile && styles.isMobile
      )}
      >
        <div>{value}</div>

        {
          hint != null &&
            <div className={styles.hintText}>
              {hint}
            </div>
        }
      </div>
    </EnhancedSelectInputOption>
  );
}

export default HintedSelectInputOption;
