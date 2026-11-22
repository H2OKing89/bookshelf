import PropTypes from 'prop-types';
import React from 'react';
import CheckInput from 'Components/Form/CheckInput';
import VirtualTableHeaderCell from './TableHeaderCell';
import styles from './TableSelectAllHeaderCell.css';

function getValue(allSelected, allUnselected) {
  if (allSelected) {
    return true;
  } else if (allUnselected) {
    return false;
  }

  return null;
}

function TableSelectAllHeaderCell(props) {
  const {
    allSelected,
    allUnselected,
    isSelectAllDisabled,
    onSelectAllChange
  } = props;

  const value = getValue(allSelected, allUnselected);

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

TableSelectAllHeaderCell.propTypes = {
  allSelected: PropTypes.bool.isRequired,
  allUnselected: PropTypes.bool.isRequired,
  isSelectAllDisabled: PropTypes.bool,
  onSelectAllChange: PropTypes.func.isRequired
};

export default TableSelectAllHeaderCell;
