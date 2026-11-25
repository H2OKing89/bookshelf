import React, { Component } from 'react';
import Icon from 'Components/Icon';
import Link from 'Components/Link/Link';
import { icons, sortDirections } from 'Helpers/Props';
import type { SortDirection } from 'Helpers/Props/sortDirections';
import styles from './TableHeaderCell.css';

export interface TableHeaderCellProps {
  className?: string;
  name: string;
  label?: React.ReactNode | (() => string);
  columnLabel?: string | (() => string);
  isSortable?: boolean;
  isVisible?: boolean;
  isModifiable?: boolean;
  sortKey?: string;
  sortDirection?: SortDirection;
  fixedSortDirection?: SortDirection;
  children?: React.ReactNode;
  onSortPress?: (name: string, direction?: SortDirection) => void;
}

class TableHeaderCell extends Component<TableHeaderCellProps> {

  //
  // Listeners

  onPress = () => {
    const {
      name,
      fixedSortDirection
    } = this.props;

    if (fixedSortDirection) {
      this.props.onSortPress?.(name, fixedSortDirection);
    } else {
      this.props.onSortPress?.(name);
    }
  };

  //
  // Render

  render() {
    const {
      className = styles.headerCell,
      name,
      label,
      columnLabel,
      isSortable = false,
      sortKey,
      sortDirection,
      children,
      ...otherProps
    } = this.props;

    delete otherProps.isVisible;
    delete otherProps.isModifiable;
    delete otherProps.fixedSortDirection;
    delete otherProps.onSortPress;
    const renderedLabel = typeof label === 'function' ? label() : label;
    const isSorting = isSortable && sortKey === name;
    const sortIcon = sortDirection === sortDirections.ASCENDING ?
      icons.SORT_ASCENDING :
      icons.SORT_DESCENDING;

    return (
      isSortable ?
        <Link
          {...otherProps}
          component="th"
          className={className}
          label={typeof renderedLabel === 'string' ? renderedLabel : undefined}
          title={typeof columnLabel === 'function' ? columnLabel() : columnLabel}
          onPress={this.onPress}
        >
          {children}

          {
            isSorting &&
              <Icon
                name={sortIcon}
                className={styles.sortIcon}
              />
          }
        </Link> :

        <th className={className}>
          {children}
        </th>
    );
  }
}

export default TableHeaderCell;
