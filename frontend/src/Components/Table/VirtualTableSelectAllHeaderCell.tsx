import React from 'react';
import CheckInput from 'Components/Form/CheckInput';
import { type CheckboxSelectionChange, getSelectAllValue } from './tableSelection';
import VirtualTableHeaderCell from './VirtualTableHeaderCell';
import styles from './VirtualTableSelectAllHeaderCell.css';

export interface SelectAllHeaderCellProps {
  allSelected?: boolean;
  allUnselected?: boolean;
  isSelectAllDisabled?: boolean;
  onSelectAllChange: (change: CheckboxSelectionChange) => void;
}

function VirtualTableSelectAllHeaderCell(props: SelectAllHeaderCellProps) {
  const {
    allSelected = false,
    allUnselected = false,
    isSelectAllDisabled,
    onSelectAllChange
  } = props;

  const value = getSelectAllValue(allSelected, allUnselected);

  return (
    <VirtualTableHeaderCell
      className={styles.selectAllHeaderCell}
      name="selectAll"
    >
      <CheckInput
        className={styles.input}
        name="selectAll"
        value={value}
        isDisabled={isSelectAllDisabled}
        onChange={onSelectAllChange}
      />
    </VirtualTableHeaderCell>
  );
}

export default VirtualTableSelectAllHeaderCell;
