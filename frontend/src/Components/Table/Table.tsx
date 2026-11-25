import classNames from 'classnames';
import React from 'react';
import IconButton from 'Components/Link/IconButton';
import Scroller from 'Components/Scroller/Scroller';
import TableOptionsModalWrapper from 'Components/Table/TableOptions/TableOptionsModalWrapper';
import { icons, scrollDirections } from 'Helpers/Props';
import type { SortDirection } from 'Helpers/Props/sortDirections';
import type Column from './Column';
import TableHeader from './TableHeader';
import TableHeaderCell from './TableHeaderCell';
import TableSelectAllHeaderCell from './TableSelectAllHeaderCell';
import type { CheckboxSelectionChange } from './tableSelection';
import styles from './Table.css';

interface TableOptionChange {
  columns?: Column[];
  pageSize?: number;
  [option: string]: unknown;
}

interface TableBaseProps {
  className?: string;
  horizontalScroll?: boolean;
  columns: readonly Column[];
  optionsComponent?: React.ElementType;
  pageSize?: number;
  canModifyColumns?: boolean;
  sortKey?: string;
  sortDirection?: SortDirection;
  children?: React.ReactNode;
  onSortPress?: (name: string, direction?: SortDirection) => void;
  onTableOptionChange?: (change: TableOptionChange) => void;
}

type TableProps = TableBaseProps & (
  { selectAll: true; allSelected?: boolean; allUnselected?: boolean; isSelectAllDisabled?: boolean; onSelectAllChange: (change: CheckboxSelectionChange) => void } |
  { selectAll?: false; allSelected?: boolean; allUnselected?: boolean; isSelectAllDisabled?: boolean; onSelectAllChange?: (change: CheckboxSelectionChange) => void }
);

function Table(props: TableProps) {
  const {
    className = styles.table,
    horizontalScroll = true,
    columns,
    optionsComponent,
    pageSize,
    canModifyColumns,
    children,
    onSortPress,
    onTableOptionChange,
    sortKey,
    sortDirection
  } = props;

  return (
    <Scroller
      className={classNames(
        styles.tableContainer,
        horizontalScroll && styles.horizontalScroll
      )}
      scrollDirection={
        horizontalScroll ?
          scrollDirections.HORIZONTAL :
          scrollDirections.NONE
      }
      autoFocus={false}
    >
      <table className={className}>
        <TableHeader>
          {
            props.selectAll ?
              <TableSelectAllHeaderCell allSelected={props.allSelected} allUnselected={props.allUnselected} isSelectAllDisabled={props.isSelectAllDisabled} onSelectAllChange={props.onSelectAllChange} /> :
              null
          }

          {
            columns.map((column) => {
              const {
                name,
                isVisible
              } = column;

              if (!isVisible) {
                return null;
              }

              if (
                (name === 'actions' || name === 'details') &&
                onTableOptionChange
              ) {
                return (
                  <TableHeaderCell
                    key={name}
                    name={name}
                    isSortable={false}
                  >
                    <TableOptionsModalWrapper
                      columns={columns}
                      optionsComponent={optionsComponent}
                      pageSize={pageSize}
                      canModifyColumns={canModifyColumns}
                      onTableOptionChange={onTableOptionChange}
                    >
                      <IconButton
                        name={icons.ADVANCED_SETTINGS}
                      />
                    </TableOptionsModalWrapper>
                  </TableHeaderCell>
                );
              }

              return (
                <TableHeaderCell
                  key={column.name}
                  onSortPress={onSortPress}
                  sortKey={sortKey}
                  sortDirection={sortDirection}
                  {...column}
                >
                  {typeof column.label === 'function' ? column.label() : column.label}
                </TableHeaderCell>
              );
            })
          }

        </TableHeader>
        {children}
      </table>
    </Scroller>
  );
}

export default Table;
