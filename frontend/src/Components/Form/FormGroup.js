import classNames from 'classnames';
import PropTypes from 'prop-types';
import React, { useState } from 'react';
import { map } from 'Helpers/elementChildren';
import { inputTypes, sizes } from 'Helpers/Props';
import getUniqueElementId from 'Utilities/getUniqueElementId';
import FormGroupContext from './FormGroupContext';
import FormInputGroup from './FormInputGroup';
import FormLabel from './FormLabel';
import styles from './FormGroup.css';

function findFields(children, fields, rowComponent) {
  React.Children.forEach(children, (child) => {
    if (!React.isValidElement(child) || child.type === rowComponent) {
      return;
    }

    if (child.type === FormLabel) {
      fields.labels.push(child.props);
    } else if (child.type === FormInputGroup) {
      fields.inputs.push(child.props);
    } else if (child.props.children) {
      findFields(child.props.children, fields, rowComponent);
    }
  });
}

function getFieldAssociation(children, rowId, rowComponent) {
  const fields = { labels: [], inputs: [] };
  findFields(children, fields, rowComponent);

  if (fields.labels.length !== 1 || fields.inputs.length !== 1) {
    // Rows with several controls (such as notification triggers) give each
    // control its own name rather than assigning the row label to one of them.
    return null;
  }

  const [label] = fields.labels;
  const [input] = fields.inputs;
  const explicitTarget = label.htmlFor ?? label.name;
  if (explicitTarget && input.id && explicitTarget !== input.id) {
    return null;
  }

  return {
    name: input.name,
    inputId: input.id ?? explicitTarget ?? rowId,
    labelId: label.id ?? `${rowId}-label`,
    isGroup: input.type === inputTypes.BOOKSHELF
  };
}

function FormGroup(props) {
  const {
    className,
    children,
    size,
    advancedSettings,
    isAdvanced,
    ...otherProps
  } = props;
  const [rowId] = useState(getUniqueElementId);

  if (!advancedSettings && isAdvanced) {
    return null;
  }

  const childProps = isAdvanced ? { isAdvanced } : {};

  return (
    <div
      className={classNames(
        className,
        styles[size]
      )}
      {...otherProps}
    >
      <FormGroupContext.Provider value={getFieldAssociation(children, rowId, FormGroup)}>
        {map(children, (child) => React.cloneElement(child, childProps))}
      </FormGroupContext.Provider>
    </div>
  );
}

FormGroup.propTypes = {
  className: PropTypes.string.isRequired,
  children: PropTypes.node.isRequired,
  size: PropTypes.oneOf(sizes.all).isRequired,
  advancedSettings: PropTypes.bool.isRequired,
  isAdvanced: PropTypes.bool.isRequired
};

FormGroup.defaultProps = {
  className: styles.group,
  size: sizes.SMALL,
  advancedSettings: false,
  isAdvanced: false
};

export default FormGroup;
