import classNames from 'classnames';
import React, { Component } from 'react';
import { scrollDirections } from 'Helpers/Props';
import type { ScrollDirection } from 'Helpers/Props/scrollDirections';
import styles from './Scroller.css';

const directionClasses: Partial<Record<ScrollDirection, string>> = styles;

export interface ScrollerProps extends React.HTMLAttributes<HTMLDivElement> {
  scrollDirection?: ScrollDirection;
  autoScroll?: boolean;
  scrollTop?: number | null;
  registerScroller?: (element: HTMLDivElement | null) => void;
}

class Scroller extends Component<ScrollerProps> {


  //
  // Lifecycle

  componentDidMount() {
    const {
      scrollDirection = scrollDirections.VERTICAL,
      autoFocus = true,
      scrollTop
    } = this.props;

    if (!this._scroller) {
      return;
    }

    if (scrollTop != null) {
      this._scroller.scrollTop = scrollTop;
    }

    if (autoFocus && scrollDirection !== scrollDirections.NONE) {
      this._scroller.focus({ preventScroll: true });
    }
  }

  private _scroller: HTMLDivElement | null = null;

  //
  // Control

  _setScrollerRef = (ref: HTMLDivElement | null) => {
    this._scroller = ref;

    this.props.registerScroller?.(ref);
  };

  //
  // Render

  render() {
    const {
      className,
      scrollDirection = scrollDirections.VERTICAL,
      autoScroll = true,
      children,
      ...otherProps
    } = this.props;

    delete otherProps.scrollTop;
    delete otherProps.onScroll;
    delete otherProps.registerScroller;

    return (
      <div
        ref={this._setScrollerRef}
        className={classNames(
          className,
          styles.scroller,
          directionClasses[scrollDirection],
          autoScroll && styles.autoScroll
        )}
        tabIndex={-1}
        {...otherProps}
      >
        {children}
      </div>
    );
  }

}

export default Scroller;
