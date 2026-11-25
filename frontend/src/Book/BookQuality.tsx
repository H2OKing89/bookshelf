import React from 'react';
import type { QualityModel } from 'Book/LibraryModels';
import Label from 'Components/Label';
import { kinds } from 'Helpers/Props';
import type { Kind } from 'Helpers/Props/kinds';
import formatBytes from 'Utilities/Number/formatBytes';
import translate from 'Utilities/String/translate';

function getTooltip(title: string, quality: QualityModel, size: number | null | undefined, isMonitored: boolean, isCutoffNotMet: boolean) {
  const revision = quality.revision;

  if (revision.real && revision.real > 0) {
    title += ' [REAL]';
  }

  if (revision.version && revision.version > 1) {
    title += ' [PROPER]';
  }

  if (size) {
    title += ` - ${formatBytes(size)}`;
  }

  if (!isMonitored) {
    title += ' [Not Monitored]';
  } else if (isCutoffNotMet) {
    title += ' [Cutoff Not Met]';
  }

  return title;
}

function revisionLabel(className: string | undefined, quality: QualityModel, showRevision: boolean) {
  if (!showRevision) {
    return null;
  }

  if (quality.revision.isRepack) {
    return (
      <Label
        className={className}
        kind={kinds.PRIMARY}
        title={translate('Repack')}
      >
        R
      </Label>
    );
  }

  if (quality.revision.version && quality.revision.version > 1) {
    return (
      <Label
        className={className}
        kind={kinds.PRIMARY}
        title={translate('Proper')}
      >
        P
      </Label>
    );
  }
  return null;
}

interface BookQualityProps {
  className?: string;
  title?: string;
  quality?: QualityModel | null;
  size?: number | null;
  isMonitored?: boolean;
  isCutoffNotMet?: boolean;
  showRevision?: boolean;
}

function BookQuality(props: BookQualityProps) {
  const {
    className,
    title = '',
    quality,
    size,
    isMonitored = true,
    isCutoffNotMet = false,
    showRevision = false
  } = props;

  let kind: Kind = kinds.DEFAULT;
  if (!isMonitored) {
    kind = kinds.DISABLED;
  } else if (isCutoffNotMet) {
    kind = kinds.INVERSE;
  }

  if (!quality) {
    return null;
  }

  return (
    <span>
      <Label
        className={className}
        kind={kind}
        title={getTooltip(title, quality, size, isMonitored, isCutoffNotMet)}
      >
        {quality.quality.name}
      </Label>{revisionLabel(className, quality, showRevision)}
    </span>
  );
}

export default BookQuality;
