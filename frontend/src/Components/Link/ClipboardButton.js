import PropTypes from 'prop-types';
import { Component } from 'react';
import ActionStatus from 'Components/ActionStatus';
import FormInputButton from 'Components/Form/FormInputButton';
import Icon from 'Components/Icon';
import { icons, kinds } from 'Helpers/Props';
import translate from 'Utilities/String/translate';
import styles from './ClipboardButton.css';

function copyWithSelection(value, trigger) {
  const input = document.createElement('textarea');
  input.value = value;
  input.className = styles.copyInput;
  input.readOnly = true;
  input.tabIndex = -1;
  input.setAttribute('aria-hidden', 'true');
  trigger.parentElement.appendChild(input);
  try {
    input.select();
    return document.execCommand('copy');
  } finally {
    input.remove();
    trigger.focus({ preventScroll: true });
  }
}

class ClipboardButton extends Component {

  state = { result: null };

  componentDidMount() {
    this._mounted = true;
  }

  componentWillUnmount() {
    this._mounted = false;
    clearTimeout(this._resultTimeout);
  }

  onCopyPress = async (event) => {
    if (this.props.isDisabled || this._copying) {
      return;
    }

    const { value, onPress } = this.props;
    const trigger = event.currentTarget;
    this._copying = true;
    clearTimeout(this._resultTimeout);
    this.setState({ result: 'pending' });
    onPress?.(event);

    let result = 'error';
    try {
      if (navigator.clipboard?.writeText) {
        await navigator.clipboard.writeText(value);
        result = 'success';
      } else {
        result = copyWithSelection(value, trigger) ? 'success' : 'error';
      }
    } catch {
      // Permission and availability errors leave the result as a failure.
    }
    this._copying = false;

    if (this._mounted) {
      this.setState({ result });
      this._resultTimeout = setTimeout(() => this.setState({ result: null }), 3000);
    }
  };

  render() {
    const {
      value,
      className,
      title,
      onPress,
      ...otherProps
    } = this.props;
    const { result } = this.state;
    const actionLabel = title || translate('CopyToClipboard');
    const showStateIcon = result && result !== 'pending';
    const isError = result === 'error';
    const label = {
      pending: 'CopyToClipboardInProgress',
      success: 'CopiedToClipboard',
      error: 'CopyToClipboardError'
    }[result];

    return (
      <>
        <FormInputButton
          className={className}
          title={actionLabel}
          aria-label={actionLabel}
          aria-busy={result === 'pending' || undefined}
          {...otherProps}
          onPress={this.onCopyPress}
        >
          <span className={showStateIcon ? styles.showStateIcon : undefined}>
            {
              showStateIcon &&
                <span className={styles.stateIconContainer}>
                  <Icon
                    name={isError ? icons.DANGER : icons.CHECK}
                    kind={isError ? kinds.DANGER : kinds.SUCCESS}
                    aria-hidden={true}
                  />
                </span>
            }
            <span className={styles.clipboardIconContainer}>
              <Icon name={icons.CLIPBOARD} aria-hidden={true} />
            </span>
          </span>
        </FormInputButton>
        <ActionStatus message={label ? translate(label) : undefined} isError={isError} />
      </>
    );
  }
}

ClipboardButton.propTypes = {
  className: PropTypes.string.isRequired,
  isDisabled: PropTypes.bool,
  title: PropTypes.string,
  value: PropTypes.string.isRequired,
  onPress: PropTypes.func
};

ClipboardButton.defaultProps = {
  className: styles.button
};

export default ClipboardButton;
