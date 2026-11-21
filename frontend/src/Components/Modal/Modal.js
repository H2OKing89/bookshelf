import classNames from 'classnames';
import elementClass from 'element-class';
import PropTypes from 'prop-types';
import React, { Component } from 'react';
import ReactDOM from 'react-dom';
import FocusLock from 'react-focus-lock';
import ErrorBoundary from 'Components/Error/ErrorBoundary';
import { sizes } from 'Helpers/Props';
import { isIOS } from 'Utilities/browser';
import * as keyCodes from 'Utilities/Constants/keyCodes';
import getUniqueElementId from 'Utilities/getUniqueElementId';
import { setScrollLock } from 'Utilities/scrollLock';
import { ModalTitleContext } from './ModalContext';
import ModalError from './ModalError';
import styles from './Modal.css';

const openModals = [];
const backdropClickDistance = 8;
let scrollPosition = null;

function topModal() {
  return openModals[openModals.length - 1];
}

function captureOpenState() {
  return {
    focusTargets: [document.activeElement, ...(topModal()?.focusTargets ?? [])],
    scrollX: window.scrollX,
    scrollY: window.scrollY
  };
}

function onKeyDown(event) {
  if (
    (event.key === 'Escape' || event.keyCode === keyCodes.ESCAPE) &&
    !event.defaultPrevented &&
    !event.repeat
  ) {
    event.preventDefault();
    event.stopPropagation();

    topModal()?.props.onModalClose();
  }
}

class Modal extends Component {

  //
  // Lifecycle

  constructor(props, context) {
    super(props, context);

    this._node = document.getElementById('portal-root');
    this._backgroundRef = null;
    this.dialogRef = React.createRef();
    this._titleId = `${getUniqueElementId()}-title`;
    this._initialOpenState = props.isOpen ? captureOpenState() : null;
    this.state = { isTopModal: true };
  }

  componentDidMount() {
    if (this.props.isOpen) {
      this._openModal(this._initialOpenState);
      this._initialOpenState = null;
    }
  }

  getSnapshotBeforeUpdate(prevProps) {
    return !prevProps.isOpen && this.props.isOpen ? captureOpenState() : null;
  }

  componentDidUpdate(prevProps, prevState, snapshot) {
    if (snapshot) {
      this._openModal(snapshot);
    } else if (prevProps.isOpen && !this.props.isOpen) {
      this._closeModal();
    }
  }

  componentWillUnmount() {
    if (this.props.isOpen) {
      this._closeModal();
    }
  }

  //
  // Control

  _setBackgroundRef = (ref) => {
    this._backgroundRef = ref;
  };

  _openModal({ focusTargets, scrollX, scrollY }) {
    const previousModal = topModal();

    this.focusTargets = focusTargets;
    openModals.push(this);

    previousModal?.setState({ isTopModal: false });
    if (!this.state.isTopModal) {
      this.setState({ isTopModal: true });
    }

    if (openModals.length === 1) {
      window.addEventListener('keydown', onKeyDown);

      if (isIOS()) {
        setScrollLock(true);
        scrollPosition = { scrollX, scrollY, bodyTop: document.body.style.top };
        document.body.style.top = `${-scrollY}px`;
        elementClass(document.body).add(styles.modalOpenIOS);
      } else {
        elementClass(document.body).add(styles.modalOpen);
      }
    }
  }

  _closeModal() {
    const index = openModals.indexOf(this);
    if (index < 0) {
      return;
    }

    const wasTopModal = topModal() === this;
    const focusTargets = this.focusTargets;
    this.focusTargets = [];
    this._backdropPress = null;
    openModals.splice(index, 1);
    const nextModal = topModal();

    if (openModals.length === 0) {
      window.removeEventListener('keydown', onKeyDown);

      if (scrollPosition) {
        setScrollLock(false);
        elementClass(document.body).remove(styles.modalOpenIOS);
        document.body.style.top = scrollPosition.bodyTop;
        window.scrollTo(scrollPosition.scrollX, scrollPosition.scrollY);
        scrollPosition = null;
      } else {
        elementClass(document.body).remove(styles.modalOpen);
      }
    }

    if (wasTopModal) {
      nextModal?.setState({ isTopModal: true });

      // Wait for the next dialog to become interactive before restoring focus.
      Promise.resolve().then(() => {
        if (topModal() === nextModal) {
          const target = focusTargets.find((element) =>
            element?.isConnected &&
            element !== document.body &&
            (!nextModal || nextModal.dialogRef.current?.contains(element))
          );

          target?.focus({ preventScroll: true });
        }
      });
    }
  }

  //
  // Listeners

  onBackdropBeginPress = (event) => {
    this._backdropPress =
      event.button === 0 &&
      event.target === this._backgroundRef &&
      topModal() === this ?
        { x: event.clientX, y: event.clientY } :
        null;
  };

  onBackdropEndPress = (event) => {
    const {
      closeOnBackgroundClick,
      onModalClose
    } = this.props;
    const press = this._backdropPress;
    this._backdropPress = null;

    if (
      press &&
      event.target === this._backgroundRef &&
      topModal() === this &&
      Math.hypot(event.clientX - press.x, event.clientY - press.y) <= backdropClickDistance &&
      closeOnBackgroundClick
    ) {
      onModalClose();
    }
  };

  //
  // Render

  render() {
    const {
      className,
      style,
      backdropClassName,
      size,
      children,
      isOpen,
      onModalClose,
      'aria-label': ariaLabel
    } = this.props;
    const { isTopModal } = this.state;

    if (!isOpen) {
      return null;
    }

    return ReactDOM.createPortal(
      <ModalTitleContext.Provider value={this._titleId}>
        <FocusLock disabled={!isTopModal} lockProps={{ inert: isTopModal ? undefined : '' }}>
          <div className={styles.modalContainer}>
            <div
              ref={this._setBackgroundRef}
              className={backdropClassName}
              onMouseDown={this.onBackdropBeginPress}
              onMouseUp={this.onBackdropEndPress}
            >
              <div
                ref={this.dialogRef}
                className={classNames(
                  className,
                  styles[size]
                )}
                style={style}
                role="dialog"
                aria-modal={isTopModal || undefined}
                aria-hidden={!isTopModal || undefined}
                aria-labelledby={ariaLabel ? undefined : this._titleId}
                aria-label={ariaLabel}
                tabIndex={-1}
              >
                <ErrorBoundary
                  errorComponent={ModalError}
                  onModalClose={onModalClose}
                >
                  {children}
                </ErrorBoundary>
              </div>
            </div>
          </div>
        </FocusLock>
      </ModalTitleContext.Provider>,
      this._node
    );
  }
}

Modal.propTypes = {
  'aria-label': PropTypes.string,
  className: PropTypes.string,
  style: PropTypes.object,
  backdropClassName: PropTypes.string,
  size: PropTypes.oneOf(sizes.all),
  children: PropTypes.node,
  isOpen: PropTypes.bool.isRequired,
  closeOnBackgroundClick: PropTypes.bool.isRequired,
  onModalClose: PropTypes.func.isRequired
};

Modal.defaultProps = {
  className: styles.modal,
  backdropClassName: styles.modalBackdrop,
  size: sizes.LARGE,
  closeOnBackgroundClick: true
};

export default Modal;
