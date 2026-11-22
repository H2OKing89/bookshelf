import React from 'react';
import Icon from 'Components/Icon';
import { icons } from 'Helpers/Props';
import translate from 'Utilities/String/translate';

interface QueueColumn {
  name: string;
  label?: string | (() => string) | React.ReactNode;
  columnLabel?: () => string;
  isVisible: boolean;
  isSortable?: boolean;
  isModifiable?: boolean;
}

const queueColumns: QueueColumn[] = [
  {
    name: 'status',
    columnLabel: () => translate('Status'),
    isSortable: true,
    isVisible: true,
    isModifiable: false
  },
  {
    name: 'authorMetadata.sortName',
    label: () => translate('Author'),
    isSortable: true,
    isVisible: true
  },
  {
    name: 'books.title',
    label: () => translate('BookTitle'),
    isSortable: true,
    isVisible: true
  },
  {
    name: 'books.releaseDate',
    label: () => translate('ReleaseDate'),
    isSortable: true,
    isVisible: false
  },
  {
    name: 'quality',
    label: () => translate('Quality'),
    isSortable: true,
    isVisible: true
  },
  {
    name: 'customFormats',
    label: 'Formats',
    isSortable: false,
    isVisible: true
  },
  {
    name: 'customFormatScore',
    columnLabel: () => translate('CustomFormatScore'),
    label: React.createElement(Icon, {
      name: icons.SCORE,
      title: () => translate('CustomFormatScore')
    }),
    isVisible: false
  },
  {
    name: 'protocol',
    label: () => translate('Protocol'),
    isSortable: true,
    isVisible: false
  },
  {
    name: 'indexer',
    label: () => translate('Indexer'),
    isSortable: true,
    isVisible: false
  },
  {
    name: 'downloadClient',
    label: () => translate('DownloadClient'),
    isSortable: true,
    isVisible: false
  },
  {
    name: 'title',
    label: () => translate('ReleaseTitle'),
    isSortable: true,
    isVisible: false
  },
  {
    name: 'size',
    label: () => translate('Size'),
    isSortable: true,
    isVisible: false
  },
  {
    name: 'outputPath',
    label: () => translate('OutputPath'),
    isSortable: false,
    isVisible: false
  },
  {
    name: 'estimatedCompletionTime',
    label: () => translate('TimeLeft'),
    isSortable: true,
    isVisible: true
  },
  {
    name: 'progress',
    label: () => translate('Progress'),
    isSortable: true,
    isVisible: true
  },
  {
    name: 'actions',
    columnLabel: () => translate('Actions'),
    isVisible: true,
    isModifiable: false
  }
];

export default queueColumns;
