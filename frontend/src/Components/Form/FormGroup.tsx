import classNames from 'classnames';
import React, { useState } from 'react';
import { inputTypes, sizes } from 'Helpers/Props';
import type { Size } from 'Helpers/Props/sizes';
import getUniqueElementId from 'Utilities/getUniqueElementId';
import FormGroupContext from './FormGroupContext';
import FormInputGroup from './FormInputGroup';
import FormLabel from './FormLabel';
import type { FieldAssociation } from './formTypes';
import styles from './FormGroup.css';

export interface FormGroupProps extends React.HTMLAttributes<HTMLDivElement> {
  size?: Size;
  advancedSettings?: boolean;
  isAdvanced?: boolean;
}

interface FieldNodeProps {
  name?: string;
  id?: string;
  htmlFor?: string;
  type?: string;
  children?: React.ReactNode;
}

interface FieldNodes {
  labels: FieldNodeProps[];
  inputs: FieldNodeProps[];
}

const sizeClasses: Partial<Record<Size, string>> = styles;

function findFields(children: React.ReactNode, fields: FieldNodes, rowComponent: typeof FormGroup) {
  React.Children.forEach(children, (child) => {
    if (!React.isValidElement<FieldNodeProps>(child) || child.type === rowComponent) {
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

function getFieldAssociation(children: React.ReactNode, rowId: string, rowComponent: typeof FormGroup): FieldAssociation | null {
  const fields: FieldNodes = { labels: [], inputs: [] };
  findFields(children, fields, rowComponent);

  if (fields.labels.length !== 1 || fields.inputs.length !== 1) {
    // Rows with several controls (such as notification triggers) give each
    // control its own name rather than assigning the row label to one of them.
    return null;
  }

  const [label] = fields.labels;
  const [input] = fields.inputs;
  if (typeof input.name !== 'string') {
    return null;
  }

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

function FormGroup(props: FormGroupProps) {
  const {
    className = styles.group,
    children,
    size = sizes.SMALL,
    advancedSettings = false,
    isAdvanced = false,
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
        sizeClasses[size]
      )}
      {...otherProps}
    >
      <FormGroupContext.Provider value={getFieldAssociation(children, rowId, FormGroup)}>
        {React.Children.map(children, (child) => React.isValidElement<{ isAdvanced?: boolean }>(child) ?
          React.cloneElement(child, childProps) :
          child)}
      </FormGroupContext.Provider>
    </div>
  );
}

export default FormGroup;
