import PropTypes from 'prop-types';
import React from 'react';
import Scroller from 'Components/Scroller/Scroller';
import styles from './MenuContent.css';

function MenuContent(props) {
  const {
    forwardedRef,
    className,
    children,
    ...otherProps
  } = props;

  return (
    <div
      ref={forwardedRef}
      className={className}
      role="menu"
      tabIndex={-1}
      {...otherProps}
    >
      <Scroller className={styles.scroller} autoFocus={false}>
        {children}
      </Scroller>
    </div>
  );
}

MenuContent.propTypes = {
  forwardedRef: PropTypes.func,
  className: PropTypes.string,
  children: PropTypes.node.isRequired
};

MenuContent.defaultProps = {
  className: styles.menuContent
};

export default MenuContent;
