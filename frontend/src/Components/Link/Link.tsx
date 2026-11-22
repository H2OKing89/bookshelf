import classNames from 'classnames';
import React, {
  ComponentClass,
  FunctionComponent,
  SyntheticEvent,
  useCallback,
} from 'react';
import { Link as RouterLink } from 'react-router-dom';
import styles from './Link.css';

export interface LinkProps extends React.AllHTMLAttributes<HTMLAnchorElement> {
  className?: string;
  component?:
    | string
    | FunctionComponent<LinkProps>
    | ComponentClass<LinkProps, unknown>;
  to?: string;
  target?: string;
  isDisabled?: boolean;
  noRouter?: boolean;
  onPress?(event: SyntheticEvent): void;
}
function Link(props: LinkProps) {
  const {
    className,
    component = 'button',
    to,
    target,
    type,
    isDisabled,
    noRouter = false,
    onPress,
    ...otherProps
  } = props;

  const onClick = useCallback(
    (event: SyntheticEvent) => {
      if (isDisabled) {
        event.preventDefault();
      } else {
        onPress?.(event);
      }
    },
    [isDisabled, onPress]
  );

  const linkClassName = classNames(
    className,
    styles.link,
    to && styles.to,
    isDisabled && 'isDisabled'
  );
  const isExternal = to && /\w+:\/\//.test(to);

  if (to && !isExternal && !noRouter) {
    return (
      <RouterLink
        {...otherProps}
        className={linkClassName}
        to={`/${to.replace(/^\//, '')}`}
        target={target}
        type={type}
        onClick={onClick}
      />
    );
  }

  const element = to ? 'a' : component;
  const linkProps: React.AllHTMLAttributes<HTMLAnchorElement> = {
    className: linkClassName,
    target,
    type,
  };

  if (to) {
    linkProps.href = to;
    linkProps.target = target || (isExternal ? '_blank' : '_self');
    if (isExternal) {
      linkProps.rel = 'noreferrer';
    }
  } else if (element === 'button' || element === 'input') {
    linkProps.type = type || 'button';
    linkProps.disabled = isDisabled;
  }

  return React.createElement(element, { ...otherProps, ...linkProps, onClick });
}

export default Link;
