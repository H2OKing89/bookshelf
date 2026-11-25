import React from 'react';
import type { SortDirection } from 'Helpers/Props/sortDirections';

type PropertyFunction<T> = () => T;

interface Column {
  name: string;
  label?: string | PropertyFunction<string> | React.ReactNode;
  className?: string;
  columnLabel?: string | PropertyFunction<string>;
  fixedSortDirection?: SortDirection;
  isSortable?: boolean;
  isVisible: boolean;
  isModifiable?: boolean;
}

export default Column;
