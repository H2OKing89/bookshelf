import PropTypes from 'prop-types';
import React, { Component } from 'react';
import Portal from 'Components/Portal';
import AnchoredPopup from 'Components/Positioning/AnchoredPopup';
import { align } from 'Helpers/Props';
import getUniqueElementId from 'Utilities/getUniqueElementId';
import MenuContext from './MenuContext';
import styles from './Menu.css';

const viewportPadding = 8;
const menuItemSelector = '[role="menuitem"], [role="menuitemradio"], [role="menuitemcheckbox"]';

function enabledMenuItems(menu) {
  return Array.from(menu.querySelectorAll(menuItemSelector)).filter((item) =>
    !item.matches(':disabled, [aria-disabled="true"]') &&
    !item.closest('[hidden], [inert], [aria-hidden="true"]') &&
    item.getClientRects().length > 0 &&
    window.getComputedStyle(item).visibility === 'visible'
  );
}

class Menu extends Component {

  constructor(props, context) {
    super(props, context);

    this._triggerId = getUniqueElementId();
    this._contentId = getUniqueElementId();
    this._menuContext = { closeMenu: this.closeAfterSelection };
    this.state = { isOpen: false };
  }

  componentDidUpdate(prevProps, prevState) {
    if (!prevState.isOpen && this.state.isOpen) {
      this._addListeners();
    } else if (prevState.isOpen && !this.state.isOpen) {
      this._removeListeners();
    }
  }

  componentWillUnmount() {
    if (this.state.isOpen) {
      this._removeListeners();
    }
  }

  // Focus only after the asynchronous positioning operation makes the menu visible.
  onPositioned = () => {
    if (this.state.isOpen) {
      window.cancelAnimationFrame(this._focusFrame);
      this._focusFrame = window.requestAnimationFrame(() => {
        this._focusFrame = undefined;
        this.focusMenuItem(this._initialKey);
      });
    }
  };

  setReference = (node) => {
    this._reference = node;
  };

  onFloatingChange = (node) => {
    this._menu = node;
  };

  getTrigger() {
    return this._reference?.querySelector('[aria-haspopup="menu"]');
  }

  getLimits() {
    const { enforceMaxHeight } = this.props;
    const viewport = window.visualViewport;
    const width = Math.min(document.documentElement.clientWidth, viewport?.width ?? Infinity);
    const height = Math.min(document.documentElement.clientHeight, viewport?.height ?? Infinity);
    const { top, bottom } = this._reference.getBoundingClientRect();
    const viewportTop = viewport?.offsetTop ?? 0;
    const usableHeight = Math.min(height, Math.max(top - viewportTop, viewportTop + height - bottom));

    return {
      maxWidth: Math.max(0, Math.floor(width - 2 * viewportPadding)),
      maxHeight: enforceMaxHeight ? Math.max(0, Math.floor(usableHeight - viewportPadding)) : undefined
    };
  }

  updateLimits = () => {
    const limits = this.getLimits();
    if (limits.maxHeight !== this.state.maxHeight || limits.maxWidth !== this.state.maxWidth) {
      this.setState(limits);
    }
  };

  focusMenuItem(key) {
    if (!this._menu) {
      return;
    }

    const items = enabledMenuItems(this._menu);
    const current = items.indexOf(document.activeElement);
    const last = items.length - 1;
    let next = 0;

    switch (key) {
      case 'Home':
        next = 0;
        break;
      case 'End':
        next = last;
        break;
      case 'ArrowUp':
        next = current <= 0 ? last : current - 1;
        break;
      default:
        next = current < last ? current + 1 : 0;
        break;
    }

    const item = items[next];
    (item ?? this._menu).focus({ preventScroll: true });

    if (item) {
      const scroller = this._menu.firstElementChild;
      const itemRect = item.getBoundingClientRect();
      const scrollerRect = scroller.getBoundingClientRect();

      if (itemRect.top < scrollerRect.top) {
        scroller.scrollTop += itemRect.top - scrollerRect.top;
      } else if (itemRect.bottom > scrollerRect.bottom) {
        scroller.scrollTop += itemRect.bottom - scrollerRect.bottom;
      }
    }
  }

  openMenu(initialKey) {
    this._initialKey = initialKey;
    this.setState({ isOpen: true, ...this.getLimits() });
  }

  closeMenu(restoreFocus = false) {
    if (restoreFocus) {
      this.getTrigger()?.focus({ preventScroll: true });
    }

    this.setState({ isOpen: false });
  }

  closeAfterSelection = () => {
    this.closeMenu(this._menu?.contains(document.activeElement));
  };

  _addListeners() {
    window.addEventListener('resize', this.updateLimits);
    window.addEventListener('scroll', this.onScroll, true);
    window.visualViewport?.addEventListener('resize', this.updateLimits);
    window.visualViewport?.addEventListener('scroll', this.updateLimits);
    document.addEventListener('pointerdown', this.onOutsideInteraction, true);
    document.addEventListener('focusin', this.onOutsideInteraction, true);
  }

  _removeListeners() {
    window.removeEventListener('resize', this.updateLimits);
    window.removeEventListener('scroll', this.onScroll, true);
    window.visualViewport?.removeEventListener('resize', this.updateLimits);
    window.visualViewport?.removeEventListener('scroll', this.updateLimits);
    document.removeEventListener('pointerdown', this.onOutsideInteraction, true);
    document.removeEventListener('focusin', this.onOutsideInteraction, true);
    window.cancelAnimationFrame(this._focusFrame);
    this._focusFrame = undefined;
  }

  onScroll = (event) => {
    if (event.target === window || !this._menu?.contains(event.target)) {
      this.updateLimits();
    }
  };

  onOutsideInteraction = (event) => {
    if (!this._reference?.contains(event.target) && !this._menu?.contains(event.target)) {
      this.closeMenu();
    }
  };

  onTriggerPress = () => {
    if (this.state.isOpen) {
      this.closeMenu();
    } else {
      this.openMenu('Home');
    }
  };

  onTriggerKeyDown = (event) => {
    if (event.defaultPrevented) {
      return;
    }

    if (event.key === 'ArrowDown' || event.key === 'ArrowUp') {
      event.preventDefault();
      const initialKey = event.key === 'ArrowUp' ? 'End' : 'Home';

      if (this.state.isOpen) {
        this.focusMenuItem(initialKey);
      } else {
        this.openMenu(initialKey);
      }
    } else if (event.key === 'Escape' && this.state.isOpen) {
      this.onMenuKeyDown(event);
    }
  };

  onMenuKeyDown = (event) => {
    switch (event.key) {
      case 'Escape':
        event.preventDefault();
        event.stopPropagation();
        if (!event.repeat) {
          this.closeMenu(true);
        }
        break;
      case 'Tab':
        // Allow native Tab and Shift+Tab to continue from the opener's position.
        this.closeMenu(true);
        break;
      case 'ArrowDown':
      case 'ArrowUp':
      case 'Home':
      case 'End':
        event.preventDefault();
        this.focusMenuItem(event.key);
        break;
      case ' ':
        if (event.target.matches('a[href]')) {
          event.preventDefault();
          event.target.click();
        }
        break;
      default:
        break;
    }
  };

  render() {
    const { className, children, alignMenu } = this.props;
    const { isOpen, maxWidth, maxHeight } = this.state;
    const [trigger, content] = React.Children.toArray(children);
    const triggerId = trigger.props.id ?? this._triggerId;

    return (
      <>
        <div ref={this.setReference} className={className}>
          {React.cloneElement(trigger, {
            id: triggerId,
            'aria-haspopup': 'menu',
            'aria-expanded': isOpen,
            'aria-controls': isOpen ? this._contentId : undefined,
            onPress: this.onTriggerPress,
            onKeyDown: this.onTriggerKeyDown
          })}
        </div>

        {
          isOpen &&
            <Portal target={this._reference.closest('[role="dialog"]') ?? undefined}>
              <AnchoredPopup
                reference={this._reference}
                placement={alignMenu === align.RIGHT ? 'bottom-end' : 'bottom-start'}
                strategy="fixed"
                padding={viewportPadding}
                onFloatingChange={this.onFloatingChange}
                onPositioned={this.onPositioned}
              >
                {({ ref, style }) => (
                  <MenuContext.Provider value={this._menuContext}>
                    {React.cloneElement(content, {
                      id: this._contentId,
                      'aria-labelledby': triggerId,
                      forwardedRef: ref,
                      onKeyDown: this.onMenuKeyDown,
                      style: { ...style, maxWidth, maxHeight }
                    })}
                  </MenuContext.Provider>
                )}
              </AnchoredPopup>
            </Portal>
        }
      </>
    );
  }
}

Menu.propTypes = {
  className: PropTypes.string,
  children: PropTypes.node.isRequired,
  alignMenu: PropTypes.oneOf([align.LEFT, align.RIGHT]),
  enforceMaxHeight: PropTypes.bool.isRequired
};

Menu.defaultProps = {
  className: styles.menu,
  alignMenu: align.LEFT,
  enforceMaxHeight: true
};

export default Menu;
