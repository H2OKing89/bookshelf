import React from 'react';
import Alert from 'Components/Alert';
import { kinds } from 'Helpers/Props';
import type { ValidationMessage } from './formTypes';
import styles from './Form.css';

export interface FormProps {
  children?: React.ReactNode;
  validationErrors?: readonly ValidationMessage[];
  validationWarnings?: readonly ValidationMessage[];
}

function Form(props: FormProps) {
  const {
    children,
    validationErrors = [],
    validationWarnings = []
  } = props;

  return (
    <div>
      {
        validationErrors.length || validationWarnings.length ?
          <div className={styles.validationFailures}>
            {
              validationErrors.map((error, index) => {
                return (
                  <Alert
                    key={index}
                    kind={kinds.DANGER}
                  >
                    {error.errorMessage}
                  </Alert>
                );
              })
            }

            {
              validationWarnings.map((warning, index) => {
                return (
                  <Alert
                    key={index}
                    kind={kinds.WARNING}
                  >
                    {warning.errorMessage}
                  </Alert>
                );
              })
            }
          </div> :
          null
      }

      {children}
    </div>
  );
}

export default Form;
