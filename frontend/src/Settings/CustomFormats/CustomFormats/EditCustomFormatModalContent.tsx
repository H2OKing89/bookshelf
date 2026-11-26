import { Component } from 'react';
import Alert from 'Components/Alert';
import Card from 'Components/Card';
import FieldSet from 'Components/FieldSet';
import Form, { type FormProps } from 'Components/Form/Form';
import FormGroup from 'Components/Form/FormGroup';
import FormInputGroup from 'Components/Form/FormInputGroup';
import FormLabel from 'Components/Form/FormLabel';
import type { ValueChange } from 'Components/Form/formTypes';
import Icon from 'Components/Icon';
import Button from 'Components/Link/Button';
import SpinnerErrorButton from 'Components/Link/SpinnerErrorButton';
import LoadingIndicator from 'Components/Loading/LoadingIndicator';
import ModalBody from 'Components/Modal/ModalBody';
import ModalContent from 'Components/Modal/ModalContent';
import ModalFooter from 'Components/Modal/ModalFooter';
import ModalHeader from 'Components/Modal/ModalHeader';
import { icons, inputTypes, kinds } from 'Helpers/Props';
import type { SettingsProjection } from 'Store/Selectors/selectSettings';
import translate from 'Utilities/String/translate';
import { checkDraft, numberDraft, textDraft } from '../../providerSettingsState';
import type { SpecificationCard } from '../customFormatState';
import ImportCustomFormatModal from './ImportCustomFormatModal';
import AddSpecificationModal from './Specifications/AddSpecificationModal';
import EditSpecificationModalConnector from './Specifications/EditSpecificationModalConnector';
import Specification from './Specifications/Specification';
import styles from './EditCustomFormatModalContent.css';

export interface EditFormatContentProps extends FormProps {
  isFetching: boolean;
  error?: unknown;
  isSaving: boolean;
  saveError?: unknown;
  item: SettingsProjection;
  specificationsPopulated: boolean;
  specifications: SpecificationCard[];
  onInputChange: (change: ValueChange<unknown>) => void;
  onSavePress: () => void;
  onModalClose: () => void;
  onDeleteCustomFormatPress?: () => void;
  onCloneSpecificationPress: (id: number) => void;
  onConfirmDeleteSpecification: (id: number) => void;
}
interface State {
  isAddSpecificationModalOpen: boolean;
  isEditSpecificationModalOpen: boolean;
  isImportCustomFormatModalOpen: boolean;
}

class EditCustomFormatModalContent extends Component<EditFormatContentProps, State> {

  //
  // Lifecycle

  constructor(props: EditFormatContentProps) {
    super(props);

    this.state = {
      isAddSpecificationModalOpen: false,
      isEditSpecificationModalOpen: false,
      isImportCustomFormatModalOpen: false
    };
  }

  //
  // Listeners

  onAddSpecificationPress = () => {
    this.setState({ isAddSpecificationModalOpen: true });
  };

  onAddSpecificationModalClose = ({ specificationSelected = false }: { specificationSelected?: boolean } = {}) => {
    this.setState({
      isAddSpecificationModalOpen: false,
      isEditSpecificationModalOpen: specificationSelected
    });
  };

  onEditSpecificationModalClose = () => {
    this.setState({ isEditSpecificationModalOpen: false });
  };

  onImportPress = () => {
    this.setState({ isImportCustomFormatModalOpen: true });
  };

  onImportCustomFormatModalClose = () => {
    this.setState({ isImportCustomFormatModalOpen: false });
  };

  //
  // Render

  render() {
    const {
      isFetching,
      error,
      isSaving,
      saveError,
      item,
      specificationsPopulated,
      specifications,
      onInputChange,
      onSavePress,
      onModalClose,
      onDeleteCustomFormatPress,
      onCloneSpecificationPress,
      onConfirmDeleteSpecification,
      ...otherProps
    } = this.props;

    const {
      isAddSpecificationModalOpen,
      isEditSpecificationModalOpen,
      isImportCustomFormatModalOpen
    } = this.state;

    const id = numberDraft(item, 'id').value;
    const name = textDraft(item, 'name');
    const includeCustomFormatWhenRenaming = checkDraft(item, 'includeCustomFormatWhenRenaming');

    return (
      <ModalContent onModalClose={onModalClose}>

        <ModalHeader>
          {id ? 'Edit Custom Format' : 'Add Custom Format'}
        </ModalHeader>

        <ModalBody>
          <div>
            {
              isFetching &&
                <LoadingIndicator />
            }

            {
              !isFetching && !!error &&
                <div>
                  {'Unable to add a new custom format, please try again.'}
                </div>
            }

            {
              !isFetching && !error && specificationsPopulated &&
                <div>
                  <Form
                    {...otherProps}
                  >
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

                    <FormGroup>
                      <FormLabel>{'Include Custom Format when Renaming'}</FormLabel>

                      <FormInputGroup
                        type={inputTypes.CHECK}
                        name="includeCustomFormatWhenRenaming"
                        helpText={translate('IncludeCustomFormatWhenRenamingHelpText')}
                        {...includeCustomFormatWhenRenaming}
                        onChange={onInputChange}
                      />
                    </FormGroup>
                  </Form>

                  <FieldSet legend={translate('Conditions')}>
                    <Alert kind={kinds.INFO}>
                      <div>
                        {translate('CustomFormatsSettingsTriggerInfo')}
                      </div>
                    </Alert>
                    <div className={styles.customFormats}>
                      {
                        specifications.map((tag) => {
                          return (
                            <Specification
                              key={tag.id}
                              {...tag}
                              onCloneSpecificationPress={onCloneSpecificationPress}
                              onConfirmDeleteSpecification={onConfirmDeleteSpecification}
                            />
                          );
                        })
                      }

                      <Card
                        className={styles.addSpecification}
                        onPress={this.onAddSpecificationPress}
                      >
                        <div className={styles.center}>
                          <Icon
                            name={icons.ADD}
                            size={45}
                          />
                        </div>
                      </Card>
                    </div>
                  </FieldSet>

                  <AddSpecificationModal
                    isOpen={isAddSpecificationModalOpen}
                    onModalClose={this.onAddSpecificationModalClose}
                  />

                  <EditSpecificationModalConnector
                    isOpen={isEditSpecificationModalOpen}
                    onModalClose={this.onEditSpecificationModalClose}
                  />

                  <ImportCustomFormatModal
                    isOpen={isImportCustomFormatModalOpen}
                    onModalClose={this.onImportCustomFormatModalClose}
                  />

                </div>
            }
          </div>
        </ModalBody>
        <ModalFooter>
          <div className={styles.rightButtons}>
            {
              !!id && onDeleteCustomFormatPress &&
                <Button
                  className={styles.deleteButton}
                  kind={kinds.DANGER}
                  onPress={onDeleteCustomFormatPress}
                >
                  Delete
                </Button>
            }

            <Button
              className={styles.deleteButton}
              onPress={this.onImportPress}
            >
              Import
            </Button>
          </div>

          <Button
            onPress={onModalClose}
          >
            Cancel
          </Button>

          <SpinnerErrorButton
            action="save"
            isSpinning={isSaving}
            error={saveError}
            onPress={onSavePress}
            isDisabled={!specificationsPopulated || isFetching || !!error}
          >
            Save
          </SpinnerErrorButton>
        </ModalFooter>
      </ModalContent>
    );
  }
}

export default EditCustomFormatModalContent;
