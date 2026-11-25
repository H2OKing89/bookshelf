declare module 'react-text-truncate' {
  import type { Component, ElementType, ReactNode } from 'react';

  interface TextTruncateProps {
    containerClassName?: string;
    element?: string;
    line?: number | boolean;
    text?: string;
    textElement?: ElementType;
    textTruncateChild?: ReactNode;
    truncateText?: string;
    maxCalculateTimes?: number;
  }

  export default class TextTruncate extends Component<TextTruncateProps> {}
}
