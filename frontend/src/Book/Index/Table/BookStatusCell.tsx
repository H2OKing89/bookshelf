import React from 'react';
import Icon from 'Components/Icon';
import VirtualTableRowCell from 'Components/Table/Cells/TableRowCell';
import { icons } from 'Helpers/Props';
import translate from 'Utilities/String/translate';
import styles from './BookStatusCell.css';

interface BookStatusCellProps extends Omit<React.HTMLAttributes<HTMLElement>, 'children'> {
  className?: string;
  monitored: boolean;
  component?: React.ComponentType<React.PropsWithChildren<{ className?: string }>> | 'div' | 'td';
}

function BookStatusCell(props: BookStatusCellProps) {
  const {
    className = styles.status,
    monitored,
    component: Component = VirtualTableRowCell,
    ...otherProps
  } = props;

  return (
    <Component
      className={className}
      {...otherProps}
    >
      <Icon
        className={styles.statusIcon}
        name={monitored ? icons.MONITORED : icons.UNMONITORED}
        title={monitored ? translate('MonitoredAuthorIsMonitored') : translate('MonitoredAuthorIsUnmonitored')}
      />
    </Component>
  );
}

BookStatusCell.defaultProps = {
  className: styles.status,
  component: VirtualTableRowCell
};

export default BookStatusCell;
