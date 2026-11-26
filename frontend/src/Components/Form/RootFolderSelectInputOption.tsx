import classNames from 'classnames';
import type { ReactNode } from 'react';
import formatBytes from 'Utilities/Number/formatBytes';
import EnhancedSelectInputOption, { type EnhancedSelectInputOptionProps } from './EnhancedSelectInputOption';
import styles from './RootFolderSelectInputOption.css';

export interface RootFolderSelectInputOptionProps extends Omit<EnhancedSelectInputOptionProps, 'children'> {
  name?: string;
  value?: ReactNode;
  freeSpace?: number | null;
  authorFolder?: string;
  isWindows?: boolean;
  isMissing?: boolean;
}

function RootFolderSelectInputOption(props: RootFolderSelectInputOptionProps) {
  const {
    id,
    value,
    name = '',
    freeSpace,
    authorFolder,
    isMissing,
    isMobile,
    isWindows,
    ...otherProps
  } = props;

  const slashCharacter = isWindows ? '\\' : '/';

  const text = name === '' ? value : <>[{name}] {value}</>;

  return (
    <EnhancedSelectInputOption
      id={id}
      isMobile={isMobile}
      {...otherProps}
    >
      <div className={classNames(
        styles.optionText,
        isMobile && styles.isMobile
      )}
      >
        <div className={styles.value}>
          {text}

          {
            authorFolder && id !== 'addNew' ?
              <div className={styles.authorFolder}>
                {slashCharacter}
                {authorFolder}
              </div> :
              null
          }
        </div>

        {
          freeSpace == null ?
            null :
            <div className={styles.freeSpace}>
              {formatBytes(freeSpace)} Free
            </div>
        }

        {
          isMissing ?
            <div className={styles.isMissing}>
              Missing
            </div> :
            null
        }
      </div>
    </EnhancedSelectInputOption>
  );
}

export default RootFolderSelectInputOption;
