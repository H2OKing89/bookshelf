import React, { Component } from 'react';
import CheckInput from 'Components/Form/CheckInput';
import type { CheckboxSelectionChange, TableSelectionChange } from '../tableSelection';
import TableRowCell from './TableRowCell';
import styles from './TableSelectCell.css';

export interface TableSelectCellProps<Id extends string | number> extends React.AriaAttributes {
  className?: string;
  id: Id;
  isSelected?: boolean;
  isDisabled?: boolean;
  onSelectedChange: (change: TableSelectionChange<Id>) => void;
}

class TableSelectCell<Id extends string | number> extends Component<TableSelectCellProps<Id>> {

  //
  // Lifecycle

  componentDidMount() {
    const {
      id,
      isSelected = false,
      onSelectedChange
    } = this.props;

    onSelectedChange({ id, value: isSelected });
  }

  componentWillUnmount() {
    const {
      id,
      onSelectedChange
    } = this.props;

    onSelectedChange({ id, value: null });
  }

  //
  // Listeners

  onChange = ({ value, shiftKey }: CheckboxSelectionChange) => {
    const {
      id,
      onSelectedChange
    } = this.props;

    onSelectedChange({ id, value, shiftKey });
  };

  //
  // Render

  render() {
    const {
      className = styles.selectCell,
      id,
      isSelected = false,
      ...inputProps
    } = this.props;
    delete inputProps.onSelectedChange;

    return (
      <TableRowCell className={className}>
        <CheckInput
          className={styles.input}
          name={id.toString()}
          value={isSelected}
          {...inputProps}
          onChange={this.onChange}
        />
      </TableRowCell>
    );
  }
}

export default TableSelectCell;
