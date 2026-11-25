import React, { Component } from 'react';
import CheckInput from 'Components/Form/CheckInput';
import type { CheckboxSelectionChange, VirtualSelectionChange } from '../tableSelection';
import VirtualTableRowCell from './VirtualTableRowCell';
import styles from './VirtualTableSelectCell.css';

export interface VirtualTableSelectCellProps<Id extends string | number> {
  className?: string;
  inputClassName?: string;
  id: Id;
  isSelected?: boolean;
  isDisabled?: boolean;
  onSelectedChange: (change: VirtualSelectionChange<Id>) => void;
}

class VirtualTableSelectCell<Id extends string | number> extends Component<VirtualTableSelectCellProps<Id>> {

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
      className = styles.cell,
      inputClassName = styles.input,
      id,
      isSelected = false,
      isDisabled
    } = this.props;

    return (
      <VirtualTableRowCell
        className={className}
      >
        <CheckInput
          className={inputClassName}
          name={id.toString()}
          value={isSelected}
          isDisabled={isDisabled}
          onChange={this.onChange}
        />
      </VirtualTableRowCell>
    );
  }
}

interface SelectCellRendererProps<Id extends string | number> {
  cellKey: React.Key;
  rowData: { name: Id; isSelected?: boolean };
  columnData: Omit<VirtualTableSelectCellProps<Id>, 'id' | 'isSelected'>;
}

export function virtualTableSelectCellRenderer<Id extends string | number>({ cellKey, rowData, columnData, ...otherProps }: SelectCellRendererProps<Id>) {
  return <VirtualTableSelectCell key={cellKey} id={rowData.name} isSelected={rowData.isSelected} {...columnData} {...otherProps} />;
}

export default VirtualTableSelectCell;
