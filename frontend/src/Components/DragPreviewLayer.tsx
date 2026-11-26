import { type HTMLAttributes } from 'react';
import styles from './DragPreviewLayer.css';

function DragPreviewLayer({ children, className = styles.dragLayer, ...otherProps }: HTMLAttributes<HTMLDivElement>) {
  return (
    <div {...otherProps} className={className} aria-hidden={true}>
      {children}
    </div>
  );
}

export default DragPreviewLayer;
