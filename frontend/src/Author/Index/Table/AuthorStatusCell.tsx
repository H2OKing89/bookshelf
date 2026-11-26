import React from 'react';
import type { AuthorStatus } from 'Author/Author';
import { getAuthorStatusDetails } from 'Author/AuthorStatus';
import Icon from 'Components/Icon';
import VirtualTableRowCell from 'Components/Table/Cells/TableRowCell';
import { icons } from 'Helpers/Props';
import translate from 'Utilities/String/translate';
import styles from './AuthorStatusCell.css';

interface AuthorStatusCellProps extends Omit<React.HTMLAttributes<HTMLElement>, 'children'> {
  className?: string;
  monitored: boolean;
  status: AuthorStatus;
  component?: React.ComponentType<React.PropsWithChildren<{ className?: string }>> | 'div' | 'td';
}

function AuthorStatusCell(props: AuthorStatusCellProps) {
  const {
    className = styles.status,
    monitored,
    status,
    component: Component = VirtualTableRowCell,
    ...otherProps
  } = props;

  const statusDetails = getAuthorStatusDetails(status);

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

      <Icon
        className={styles.statusIcon}
        name={statusDetails.icon}
        title={`${statusDetails.title}: ${statusDetails.message}`}
      />
    </Component>
  );
}

export default AuthorStatusCell;
