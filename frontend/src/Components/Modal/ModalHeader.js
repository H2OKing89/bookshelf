import PropTypes from 'prop-types';
import React, { useContext } from 'react';
import { ModalTitleContext } from './ModalContext';
import styles from './ModalHeader.css';

function ModalHeader({ children, ...otherProps }) {
  const titleId = useContext(ModalTitleContext);

  return (
    <h2
      className={styles.modalHeader}
      {...otherProps}
      id={titleId}
    >
      {children}
    </h2>
  );
}

ModalHeader.propTypes = {
  children: PropTypes.node
};

export default ModalHeader;
