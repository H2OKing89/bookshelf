import PropTypes from 'prop-types';
import React, { Component } from 'react';
import ActionStatus from 'Components/ActionStatus';
import Icon from 'Components/Icon';
import SpinnerButton from 'Components/Link/SpinnerButton';
import { icons, kinds } from 'Helpers/Props';
import translate from 'Utilities/String/translate';
import styles from './SpinnerErrorButton.css';

const resultLabels = {
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

function getResult(error) {
  if (error?.aborted) {
    return null;
  }

  if (!error) {
    return 'success';
  }

  const failures = error.responseJSON;
  return error.status === 400 && Array.isArray(failures) && failures.length &&
    failures.every((failure) => failure?.isWarning === true) ?
    'warning' :
    'error';
}

class SpinnerErrorButton extends Component {

  state = { result: null };

  componentDidUpdate(previous) {
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

  render() {
    const {
      action,
      kind,
      isSpinning,
      error,
      children,
      ...otherProps
    } = this.props;
    const result = isSpinning ? 'pending' : this.state.result;
    const showIcon = result && result !== 'pending';
    const label = resultLabels[action][result];

    let iconName = icons.CHECK;
    let iconKind = kind === kinds.PRIMARY ? kinds.DEFAULT : kinds.SUCCESS;

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

SpinnerErrorButton.propTypes = {
  action: PropTypes.oneOf(Object.keys(resultLabels)),
  kind: PropTypes.oneOf(kinds.all),
  isSpinning: PropTypes.bool.isRequired,
  error: PropTypes.object,
  children: PropTypes.node.isRequired
};

SpinnerErrorButton.defaultProps = {
  action: 'action'
};

export default SpinnerErrorButton;
