import Alert from 'Components/Alert';
import Form, { type FormProps } from 'Components/Form/Form';
import FormGroup from 'Components/Form/FormGroup';
import FormInputGroup from 'Components/Form/FormInputGroup';
import FormLabel from 'Components/Form/FormLabel';
import type { ValueChange } from 'Components/Form/formTypes';
import ProviderFieldFormGroup from 'Components/Form/ProviderFieldFormGroup';
import Button from 'Components/Link/Button';
import Link from 'Components/Link/Link';
import SpinnerErrorButton from 'Components/Link/SpinnerErrorButton';
import ModalBody from 'Components/Modal/ModalBody';
import ModalContent from 'Components/Modal/ModalContent';
import ModalFooter from 'Components/Modal/ModalFooter';
import ModalHeader from 'Components/Modal/ModalHeader';
import { inputTypes, kinds } from 'Helpers/Props';
import type { SettingsProjection } from 'Store/Selectors/selectSettings';
import translate from 'Utilities/String/translate';
import { checkDraft, numberDraft, providerFields, textDraft } from '../../../providerSettingsState';
import styles from './EditSpecificationModalContent.css';

export interface EditSpecificationContentProps extends FormProps {
  advancedSettings: boolean;
  item: SettingsProjection;
  onInputChange: (change: ValueChange<unknown>) => void;
  onFieldChange: (change: ValueChange<unknown>) => void;
  onCancelPress: () => void;
  onSavePress: () => void;
  onDeleteSpecificationPress?: () => void;
}

function EditSpecificationModalContent(props: EditSpecificationContentProps) {
  const {
    advancedSettings,
    item,
    onInputChange,
    onFieldChange,
    onCancelPress,
    onSavePress,
    onDeleteSpecificationPress,
    ...otherProps
  } = props;

  const id = numberDraft(item, 'id').value;
  const implementationName = item.implementationName ?? '';
  const name = textDraft(item, 'name');
  const negate = checkDraft(item, 'negate');
  const required = checkDraft(item, 'required');
  const fields = providerFields(item);

  return (
    <ModalContent onModalClose={onCancelPress}>
      <ModalHeader>
        {`${id ? 'Edit' : 'Add'} Condition - ${implementationName}`}
      </ModalHeader>

      <ModalBody>
        <Form
          {...otherProps}
        >
          {
            fields && fields.some((x) => x.label === translate('CustomFormatsSpecificationRegularExpression')) &&
              <Alert kind={kinds.INFO}>
                <div>
                  <div dangerouslySetInnerHTML={{ __html: 'This condition matches using Regular Expressions. Note that the characters <code>\\^$.|?*+()[{</code> have special meanings and need escaping with a <code>\\</code>' }} />
                  {'More details'} <Link to="https://www.regular-expressions.info/tutorial.html">{'Here'}</Link>
                </div>
                <div>
                  {'Regular expressions can be tested '}
                  <Link to="http://regexstorm.net/tester">Here</Link>
                </div>
              </Alert>
          }

          <FormGroup>
            <FormLabel>
              Name
            </FormLabel>

            <FormInputGroup
              type={inputTypes.TEXT}
              name="name"
              {...name}
              onChange={onInputChange}
            />
          </FormGroup>

          {
            fields && fields.map((field) => {
              return (
                <ProviderFieldFormGroup
                  key={field.name}
                  advancedSettings={advancedSettings}
                  provider="specifications"
                  providerData={item}
                  {...field}
                  onChange={onFieldChange}
                />
              );
            })
          }

          <FormGroup>
            <FormLabel>
              Negate
            </FormLabel>

            <FormInputGroup
              type={inputTypes.CHECK}
              name="negate"
              {...negate}
              helpText={translate('NegateHelpText', { 0: implementationName })}
              onChange={onInputChange}
            />
          </FormGroup>

          <FormGroup>
            <FormLabel>
              Required
            </FormLabel>

            <FormInputGroup
              type={inputTypes.CHECK}
              name="required"
              {...required}
              helpText={translate('RequiredHelpText', { 0: implementationName, 1: implementationName })}
              onChange={onInputChange}
            />
          </FormGroup>
        </Form>
      </ModalBody>
      <ModalFooter>
        {
          !!id && onDeleteSpecificationPress &&
            <Button
              className={styles.deleteButton}
              kind={kinds.DANGER}
              onPress={onDeleteSpecificationPress}
            >
              Delete
            </Button>
        }

        <Button
          onPress={onCancelPress}
        >
          Cancel
        </Button>

        <SpinnerErrorButton
          isSpinning={false}
          onPress={onSavePress}
        >
          Save
        </SpinnerErrorButton>
      </ModalFooter>
    </ModalContent>
  );
}

export default EditSpecificationModalContent;
