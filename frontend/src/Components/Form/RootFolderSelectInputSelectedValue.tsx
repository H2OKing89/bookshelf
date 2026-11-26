import formatBytes from 'Utilities/Number/formatBytes';
import EnhancedSelectInputSelectedValue, { type EnhancedSelectInputSelectedValueProps } from './EnhancedSelectInputSelectedValue';
import styles from './RootFolderSelectInputSelectedValue.css';

export interface RootFolderSelectInputSelectedValueProps extends Omit<EnhancedSelectInputSelectedValueProps, 'children'> {
  name?: string;
  value?: string;
  freeSpace?: number | null;
  authorFolder?: string;
  isWindows?: boolean;
  includeFreeSpace?: boolean;
}

function RootFolderSelectInputSelectedValue(props: RootFolderSelectInputSelectedValueProps) {
  const {
    name = '',
    value,
    freeSpace,
    authorFolder,
    includeFreeSpace = true,
    isWindows,
    ...otherProps
  } = props;

  const slashCharacter = isWindows ? '\\' : '/';

  const text = name === '' ? value : `[${name}] ${value}`;

  return (
    <EnhancedSelectInputSelectedValue
      className={styles.selectedValue}
      {...otherProps}
    >
      <div className={styles.pathContainer}>
        <div className={styles.path}>
          {text}
        </div>

        {
          authorFolder ?
            <div className={styles.authorFolder}>
              {slashCharacter}
              {authorFolder}
            </div> :
            null
        }
      </div>

      {
        freeSpace != null && includeFreeSpace &&
          <div className={styles.freeSpace}>
            {formatBytes(freeSpace)} Free
          </div>
      }
    </EnhancedSelectInputSelectedValue>
  );
}

export default RootFolderSelectInputSelectedValue;
