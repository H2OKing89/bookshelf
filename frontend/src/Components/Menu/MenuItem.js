import classNames from 'classnames';
import PropTypes from 'prop-types';
import { useCallback, useContext } from 'react';
import Link from 'Components/Link/Link';
import MenuContext from './MenuContext';
import styles from './MenuItem.css';

function MenuItem(props) {
  const {
    className = styles.menuItem,
    children,
    isDisabled = false,
    to,
    onPress,
    ...otherProps
  } = props;
  const menu = useContext(MenuContext);

  const select = useCallback((event) => {
    menu?.closeMenu();
    onPress?.(event);
  }, [menu, onPress]);

  return (
    <Link
      className={classNames(
        className,
        isDisabled && styles.isDisabled
      )}
      role="menuitem"
      {...otherProps}
      tabIndex={-1}
      aria-disabled={isDisabled || undefined}
      isDisabled={isDisabled}
      to={isDisabled ? undefined : to}
      onPress={select}
    >
      {children}
    </Link>
  );
}

MenuItem.propTypes = {
  className: PropTypes.string,
  children: PropTypes.node.isRequired,
  isDisabled: PropTypes.bool,
  to: PropTypes.string,
  onPress: PropTypes.func
};

export default MenuItem;
