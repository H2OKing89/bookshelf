import classNames from 'classnames';
import { ColorImpairedConsumer } from 'App/ColorImpairedContext';
import { kinds, sizes } from 'Helpers/Props';
import type { Kind } from 'Helpers/Props/kinds';
import type { Size } from 'Helpers/Props/sizes';
import styles from './ProgressBar.css';

const styleClasses: Partial<Record<Kind | Size, string>> = styles;

export interface ProgressBarProps {
  className?: string;
  containerClassName?: string;
  title?: string;
  progress: number;
  precision?: number;
  showText?: boolean;
  text?: string;
  kind?: Kind;
  size?: Size;
  width?: number;
}

function ProgressBar(props: ProgressBarProps) {
  const {
    className = styles.progressBar,
    containerClassName = styles.container,
    title,
    progress,
    precision = 1,
    showText = false,
    text,
    kind = kinds.PRIMARY,
    size = sizes.MEDIUM,
    width
  } = props;

  const progressPercent = `${progress.toFixed(precision)}%`;
  const progressText = text || progressPercent;
  const actualWidth = width ? `${width}px` : '100%';

  return (
    <ColorImpairedConsumer>
      {(enableColorImpairedMode) => {
        return (
          <div
            className={classNames(
              containerClassName,
              styleClasses[size]
            )}
            title={title}
            style={{ width: actualWidth }}
          >
            {
              showText && width ?
                <div
                  className={classNames(styles.backTextContainer, styleClasses[kind])}
                  style={{ width: actualWidth }}
                >
                  <div className={styles.backText}>
                    <div>
                      {progressText}
                    </div>
                  </div>
                </div> :
                null
            }

            <div
              className={classNames(
                className,
                styleClasses[kind],
                enableColorImpairedMode && 'colorImpaired'
              )}
              role="meter"
              aria-label={`Progress Bar at ${progress.toFixed(0)}%`}
              aria-valuenow={Number(progress.toFixed(0))}
              aria-valuemin={0}
              aria-valuemax={100}
              style={{ width: progressPercent }}
            />

            {
              showText ?
                <div
                  className={classNames(styles.frontTextContainer, styleClasses[kind])}
                  style={{ width: progressPercent }}
                >
                  <div
                    className={styles.frontText}
                    style={{ width: actualWidth }}
                  >
                    <div>
                      {progressText}
                    </div>
                  </div>
                </div> :
                null
            }
          </div>
        );
      }}
    </ColorImpairedConsumer>
  );
}


export default ProgressBar;
