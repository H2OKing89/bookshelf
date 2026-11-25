import classNames from 'classnames';
import React, { Component } from 'react';
import ReactDOM from 'react-dom';
import FocusLock from 'react-focus-lock';
import ErrorBoundary from 'Components/Error/ErrorBoundary';
import { sizes } from 'Helpers/Props';
import type { Size } from 'Helpers/Props/sizes';
import { isIOS } from 'Utilities/browser';
import * as keyCodes from 'Utilities/Constants/keyCodes';
import getUniqueElementId from 'Utilities/getUniqueElementId';
import { setScrollLock } from 'Utilities/scrollLock';
import { ModalTitleContext } from './ModalContext';
import ModalError from './ModalError';
import styles from './Modal.css';

export interface ModalProps {
  'aria-label'?: string;
  className?: string;
  style?: React.CSSProperties;
  backdropClassName?: string;
  size?: Size;
  children?: React.ReactNode;
  isOpen: boolean;
  closeOnBackgroundClick?: boolean;
  onModalClose: () => void;
}
interface ModalState { isTopModal: boolean }
interface OpenState {
  focusTargets: (Element | null)[];
  scrollX: number;
  scrollY: number;
}
interface ModalLayer {
  props: ModalProps;
  focusTargets: (Element | null)[];
  dialogRef: React.RefObject<HTMLDivElement>;
  setState(state: ModalState): void;
}

const openModals: ModalLayer[] = [];
const sizeClasses: Partial<Record<Size, string>> = styles;
function canRestoreFocus(element: Element | null): element is Element & { focus(options?: FocusOptions): void } {
  return element !== null && 'focus' in element && typeof element.focus === 'function';
}
const backdropClickDistance = 8;
let scrollPosition: { scrollX: number; scrollY: number; bodyTop: string } | null = null;

function topModal(): ModalLayer | undefined {
  return openModals[openModals.length - 1];
}

function captureOpenState(): OpenState {
  return {
    focusTargets: [document.activeElement, ...(topModal()?.focusTargets ?? [])],
    scrollX: window.scrollX,
    scrollY: window.scrollY
  };
}

function onKeyDown(event: KeyboardEvent) {
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

class Modal extends Component<ModalProps, ModalState, OpenState | null> {
  static defaultProps = {
    className: styles.modal,
    backdropClassName: styles.modalBackdrop,
    size: sizes.LARGE,
    closeOnBackgroundClick: true
  };




  //
  // Lifecycle

  constructor(props: ModalProps) {
    super(props);

    this._node = document.getElementById('portal-root');
    this._backgroundRef = null;
    this.dialogRef = React.createRef<HTMLDivElement>();
    this._titleId = `${getUniqueElementId()}-title`;
    this._initialOpenState = props.isOpen ? captureOpenState() : null;
    this.state = { isTopModal: true };
  }

  componentDidMount() {
    if (this.props.isOpen && this._initialOpenState) {
      this._openModal(this._initialOpenState);
      this._initialOpenState = null;
    }
  }

  getSnapshotBeforeUpdate(prevProps: ModalProps): OpenState | null {
    return !prevProps.isOpen && this.props.isOpen ? captureOpenState() : null;
  }

  componentDidUpdate(prevProps: ModalProps, _prevState: ModalState, snapshot: OpenState | null) {
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

  private _node: HTMLElement | null;
  private _backgroundRef: HTMLDivElement | null;
  dialogRef: React.RefObject<HTMLDivElement>;
  private _titleId: string;
  private _initialOpenState: OpenState | null;
  private _backdropPress: { x: number; y: number } | null = null;
  focusTargets: (Element | null)[] = [];

  //
  // Control

  _setBackgroundRef = (ref: HTMLDivElement | null) => {
    this._backgroundRef = ref;
  };

  _openModal({ focusTargets, scrollX, scrollY }: OpenState) {
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
        document.body.classList.add(styles.modalOpenIOS);
      } else {
        document.body.classList.add(styles.modalOpen);
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
        document.body.classList.remove(styles.modalOpenIOS);
        document.body.style.top = scrollPosition.bodyTop;
        window.scrollTo(scrollPosition.scrollX, scrollPosition.scrollY);
        scrollPosition = null;
      } else {
        document.body.classList.remove(styles.modalOpen);
      }
    }

    if (wasTopModal) {
      nextModal?.setState({ isTopModal: true });

      // Wait for the next dialog to become interactive before restoring focus.
      Promise.resolve().then(() => {
        if (topModal() === nextModal) {
          const target = focusTargets.find((element) =>
            canRestoreFocus(element) && element.isConnected &&
            element !== document.body &&
            (!nextModal || nextModal.dialogRef.current?.contains(element))
          );

          if (target && canRestoreFocus(target)) {
            target.focus({ preventScroll: true });
          }
        }
      });
    }
  }

  //
  // Listeners

  onBackdropBeginPress = (event: React.MouseEvent<HTMLDivElement>) => {
    this._backdropPress =
      event.button === 0 &&
      event.target === this._backgroundRef &&
      topModal() === this ?
        { x: event.clientX, y: event.clientY } :
        null;
  };

  onBackdropEndPress = (event: React.MouseEvent<HTMLDivElement>) => {
    const {
      closeOnBackgroundClick = true,
      onModalClose
    } = this.props;
    const press = this._backdropPress;
    this._backdropPress = null;

    if (
      press &&
      event.button === 0 &&
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
      size = sizes.LARGE,
      children,
      isOpen,
      onModalClose,
      'aria-label': ariaLabel
    } = this.props;
    const { isTopModal } = this.state;

    if (!isOpen) {
      return null;
    }

    if (!this._node) {
      throw new Error('Missing modal portal root');
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
                  sizeClasses[size]
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
                  renderError={(failure) => <ModalError {...failure} onModalClose={onModalClose} />}
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

export default Modal;
