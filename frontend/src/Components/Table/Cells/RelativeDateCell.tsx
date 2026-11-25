import React, { PureComponent } from 'react';
import type UiSettings from 'typings/Settings/UiSettings';
import type { DateInput } from 'Utilities/Date/dateTypes';
import formatDateTime from 'Utilities/Date/formatDateTime';
import getRelativeDate from 'Utilities/Date/getRelativeDate';
import TableRowCell from './TableRowCell';
import styles from './RelativeDateCell.css';

export type DatePresentation = Pick<UiSettings, 'showRelativeDates' | 'shortDateFormat' | 'longDateFormat' | 'timeFormat'>;

interface DateCellTargetProps {
  className?: string;
  title?: string;
  children?: React.ReactNode;
}

export interface RelativeDateCellProps extends DatePresentation, DateCellTargetProps {
  date?: DateInput;
  includeSeconds?: boolean;
  component?: React.ComponentType<DateCellTargetProps>;
}

class RelativeDateCell extends PureComponent<RelativeDateCellProps> {

  //
  // Render

  render() {
    const {
      className = styles.cell,
      date,
      includeSeconds = false,
      showRelativeDates,
      shortDateFormat,
      longDateFormat,
      timeFormat,
      component: Component = TableRowCell,
      ...otherProps
    } = this.props;

    if (date == null || date === '') {
      return (
        <Component
          className={className}
          {...otherProps}
        />
      );
    }

    return (
      <Component
        className={className}
        title={formatDateTime(date, longDateFormat, timeFormat, { includeSeconds, includeRelativeDay: !showRelativeDates })}
        {...otherProps}
      >
        {getRelativeDate(date, shortDateFormat, showRelativeDates, { timeFormat, includeSeconds, timeForToday: true })}
      </Component>
    );
  }
}

export default RelativeDateCell;
