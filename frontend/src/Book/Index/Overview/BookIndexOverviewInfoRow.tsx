import type { IconDefinition } from '@fortawesome/fontawesome-svg-core';
import React from 'react';
import Icon from 'Components/Icon';
import styles from './BookIndexOverviewInfoRow.css';

interface BookIndexOverviewInfoRowProps {
  title?: string;
  iconName: IconDefinition;
  label: React.ReactNode;
}

function BookIndexOverviewInfoRow(props: BookIndexOverviewInfoRowProps) {
  const {
    title,
    iconName,
    label
  } = props;

  return (
    <div
      className={styles.infoRow}
      title={title}
    >
      <Icon
        className={styles.icon}
        name={iconName}
        size={14}
      />

      {label}
    </div>
  );
}

export default BookIndexOverviewInfoRow;
