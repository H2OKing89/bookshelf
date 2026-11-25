import React from 'react';
import DescriptionListItemDescription from './DescriptionListItemDescription';
import DescriptionListItemTitle from './DescriptionListItemTitle';

export interface DescriptionListItemProps {
  titleClassName?: string;
  descriptionClassName?: string;
  title?: React.ReactNode;
  data?: React.ReactNode;
}

export default function DescriptionListItem({ titleClassName, descriptionClassName, title, data }: DescriptionListItemProps) {
  return (
    <div>
      <DescriptionListItemTitle className={titleClassName}>{title}</DescriptionListItemTitle>
      <DescriptionListItemDescription className={descriptionClassName}>{data}</DescriptionListItemDescription>
    </div>
  );
}
