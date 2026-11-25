import React, { Component } from 'react';
import Icon from 'Components/Icon';
import Link from 'Components/Link/Link';
import { icons, sortDirections } from 'Helpers/Props';
import type { SortDirection } from 'Helpers/Props/sortDirections';
import styles from './VirtualTableHeaderCell.css';

export interface VirtualTableHeaderCellProps {
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

class VirtualTableHeaderCell extends Component<VirtualTableHeaderCellProps> {

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
      isSortable = false,
      sortKey,
      sortDirection,
      children,
      ...otherProps
    } = this.props;

    delete otherProps.fixedSortDirection;
    delete otherProps.onSortPress;
    delete otherProps.isVisible;
    delete otherProps.isModifiable;
    delete otherProps.label;
    delete otherProps.columnLabel;
    const isSorting = isSortable && sortKey === name;
    const sortIcon = sortDirection === sortDirections.ASCENDING ?
      icons.SORT_ASCENDING :
      icons.SORT_DESCENDING;

    return (
      isSortable ?
        <Link
          component="div"
          className={className}
          onPress={this.onPress}
          {...otherProps}
          label={undefined}
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

        <div className={className}>
          {children}
        </div>
    );
  }
}

interface HeaderRendererProps {
  columnData?: Omit<VirtualTableHeaderCellProps, 'name'>;
  dataKey: string;
  label?: React.ReactNode;
}

export function headerRenderer({ columnData = {}, dataKey, label }: HeaderRendererProps) {
  return <VirtualTableHeaderCell name={dataKey} {...columnData}>{label}</VirtualTableHeaderCell>;
}

export default VirtualTableHeaderCell;
