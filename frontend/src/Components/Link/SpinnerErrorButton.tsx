import React, { Component } from 'react';
import ActionStatus from 'Components/ActionStatus';
import Icon from 'Components/Icon';
import SpinnerButton, { type SpinnerButtonProps } from 'Components/Link/SpinnerButton';
import { icons, kinds } from 'Helpers/Props';
import type { Kind } from 'Helpers/Props/kinds';
import translate from 'Utilities/String/translate';
import styles from './SpinnerErrorButton.css';

type Action = 'action' | 'save' | 'test';
type Result = 'pending' | 'success' | 'warning' | 'error';
type CompletedResult = Exclude<Result, 'pending'>;

const resultLabels: Record<Action, Partial<Record<Result, string>>> = {
  action: {
    pending: 'ActionInProgress',
    success: 'ActionSucceeded',
    warning: 'ActionCompletedWithWarnings',
    error: 'ActionFailed'
  },
  save: {
    pending: 'SavingSettings',
    warning: 'SaveCompletedWithWarnings',
    error: 'SaveFailed'
  },
  test: {
    pending: 'TestingConnection',
    success: 'TestSucceeded',
    warning: 'TestCompletedWithWarnings',
    error: 'TestFailed'
  }
};

function getResult(error: unknown): CompletedResult | null {
  if (typeof error === 'object' && error !== null && 'aborted' in error && error.aborted) {
    return null;
  }
  if (!error) {
    return 'success';
  }
  if (typeof error === 'object' && 'status' in error && error.status === 400 && 'responseJSON' in error && Array.isArray(error.responseJSON)) {
    const failures: unknown[] = error.responseJSON;
    if (failures.length && failures.every((failure) => typeof failure === 'object' && failure !== null && 'isWarning' in failure && failure.isWarning === true)) {
      return 'warning';
    }
  }
  return 'error';
}

export interface SpinnerErrorButtonProps extends SpinnerButtonProps {
  action?: Action;
  error?: unknown;
  children: React.ReactNode;
}

interface SpinnerErrorButtonState {
  result: CompletedResult | null;
}

class SpinnerErrorButton extends Component<SpinnerErrorButtonProps, SpinnerErrorButtonState> {
  static defaultProps = { action: 'action' as const };

  state: SpinnerErrorButtonState = { result: null };

  componentDidUpdate(previous: SpinnerErrorButtonProps) {
    const { isSpinning, error } = this.props;

    if (previous.isSpinning === isSpinning) {
      return;
    }

    clearTimeout(this._resultTimeout);

    const result = isSpinning ? null : getResult(error);
    this.setState({ result });

    if (result) {
      this._resultTimeout = setTimeout(() => this.setState({ result: null }), 3000);
    }
  }

  componentWillUnmount() {
    clearTimeout(this._resultTimeout);
  }

  private _resultTimeout: ReturnType<typeof setTimeout> | undefined;

  render() {
    const {
      action = 'action',
      kind,
      isSpinning,
      children,
      ...otherProps
    } = this.props;
    delete otherProps.error;
    const result = isSpinning ? 'pending' : this.state.result;
    const showIcon = result && result !== 'pending';
    const label = result ? resultLabels[action][result] : undefined;

    let iconName = icons.CHECK;
    let iconKind: Kind = kind === kinds.PRIMARY ? kinds.DEFAULT : kinds.SUCCESS;

    if (result === 'warning') {
      iconName = icons.WARNING;
      iconKind = kinds.WARNING;
    } else if (result === 'error') {
      iconName = icons.DANGER;
      iconKind = kinds.DANGER;
    }

    return (
      <>
        <SpinnerButton
          kind={kind}
          isSpinning={isSpinning}
          aria-label={typeof children === 'string' ? children : undefined}
          aria-busy={isSpinning || undefined}
          {...otherProps}
        >
          <span className={showIcon ? styles.showIcon : undefined}>
            {
              showIcon &&
                <span className={styles.iconContainer}>
                  <Icon name={iconName} kind={iconKind} aria-hidden={true} />
                </span>
            }
            <span className={styles.label}>{children}</span>
          </span>
        </SpinnerButton>
        <ActionStatus message={label ? translate(label) : undefined} isError={result === 'error'} />
      </>
    );
  }
}


export default SpinnerErrorButton;
