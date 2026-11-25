import React from 'react';
import { useSelector } from 'react-redux';
import { createSelector } from 'reselect';
import { isStateRecord, type LegacyRootState } from 'Store/StateTypes';
import RelativeDateCell, { type DatePresentation, type RelativeDateCellProps } from './RelativeDateCell';

// These defaults match the server's ConfigService; only malformed/missing
// settings use them, rather than turning an otherwise valid date into an error.
export function getDatePresentation(value: unknown): DatePresentation {
  const item = isStateRecord(value) ? value : {};
  return {
    showRelativeDates: typeof item.showRelativeDates === 'boolean' ? item.showRelativeDates : true,
    shortDateFormat: typeof item.shortDateFormat === 'string' ? item.shortDateFormat : 'MMM D YYYY',
    longDateFormat: typeof item.longDateFormat === 'string' ? item.longDateFormat : 'dddd, MMMM D YYYY',
    timeFormat: typeof item.timeFormat === 'string' ? item.timeFormat : 'h(:mm)a'
  };
}

const selectDatePresentation = createSelector(
  (state: LegacyRootState) => {
    const ui = state.settings?.ui;
    return isStateRecord(ui) ? ui.item : undefined;
  },
  getDatePresentation
);

export default function RelativeDateCellConnector(props: Omit<RelativeDateCellProps, keyof DatePresentation>) {
  const settings = useSelector(selectDatePresentation);
  return <RelativeDateCell {...settings} {...props} />;
}
