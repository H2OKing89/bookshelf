import { Component } from 'react';
import Form, { type FormProps } from 'Components/Form/Form';
import FormGroup from 'Components/Form/FormGroup';
import FormInputGroup from 'Components/Form/FormInputGroup';
import FormLabel from 'Components/Form/FormLabel';
import type { SelectOption, ValueChange } from 'Components/Form/formTypes';
import Button from 'Components/Link/Button';
import SpinnerErrorButton from 'Components/Link/SpinnerErrorButton';
import LoadingIndicator from 'Components/Loading/LoadingIndicator';
import Measure from 'Components/Measure';
import ModalBody from 'Components/Modal/ModalBody';
import ModalContent from 'Components/Modal/ModalContent';
import ModalFooter from 'Components/Modal/ModalFooter';
import ModalHeader from 'Components/Modal/ModalHeader';
import { inputTypes, kinds, sizes } from 'Helpers/Props';
import dimensions from 'Styles/Variables/dimensions';
import translate from 'Utilities/String/translate';
import QualityProfileFormatItems, { type QualityFormatScoreProps } from './QualityProfileFormatItems';
import QualityProfileItems, { type QualityItemEditorProps } from './QualityProfileItems';
import type { ProfileDraft, ProfileFormatItem,qualityProfileDrafts } from './qualityProfileSettingsState';
import QualityProfileSizeRules from './QualityProfileSizeRules';
import styles from './EditQualityProfileModalContent.css';

const MODAL_BODY_PADDING = parseInt(dimensions.modalBodyPadding);

function getCustomFormatRender(formatItems: ProfileDraft<ProfileFormatItem[]>, otherProps: QualityFormatScoreProps) {
  return (
    <QualityProfileFormatItems
      profileFormatItems={formatItems.value}
      errors={formatItems.errors}
      warnings={formatItems.warnings}
      {...otherProps}
    />
  );
}

interface Props extends FormProps, QualityFormatScoreProps, QualityItemEditorProps {
  editGroups: boolean;
  isFetching: boolean;
  error?: unknown;
  isSaving: boolean;
  saveError?: unknown;
  qualities: SelectOption[];
  profile: ReturnType<typeof qualityProfileDrafts>;
  isInUse: boolean;
  onInputChange: (change: ValueChange<unknown>) => void;
  onCutoffChange: (change: ValueChange<string | number>) => void;
  onSavePress: () => void;
  onModalClose: () => void;
  onDeleteQualityProfilePress?: () => void;
  onContentHeightChange: (height: number) => void;
}
interface State {
  headerHeight: number;
  bodyHeight: number;
  footerHeight: number;
}

class EditQualityProfileModalContent extends Component<Props, State> {

  //
  // Lifecycle

  constructor(props: Props) {
    super(props);

    this.state = {
      headerHeight: 0,
      bodyHeight: 0,
      footerHeight: 0
    };
  }

  componentDidUpdate(_prevProps: Props, prevState: State) {
    const {
      headerHeight,
      bodyHeight,
      footerHeight
    } = this.state;

    if (
      headerHeight > 0 &&
      bodyHeight > 0 &&
      footerHeight > 0 &&
      (
        headerHeight !== prevState.headerHeight ||
        bodyHeight !== prevState.bodyHeight ||
        footerHeight !== prevState.footerHeight
      )
    ) {
      const padding = MODAL_BODY_PADDING * 2;

      this.props.onContentHeightChange(
        headerHeight + bodyHeight + footerHeight + padding
      );
    }
  }

  //
  // Listeners

  onHeaderMeasure = ({ height }: { height: number }) => {
    if (height > this.state.headerHeight) {
      this.setState({ headerHeight: height });
    }
  };

  onBodyMeasure = ({ height }: { height: number }) => {

    if (height > this.state.bodyHeight) {
      this.setState({ bodyHeight: height });
    }
  };

  onFooterMeasure = ({ height }: { height: number }) => {
    if (height > this.state.footerHeight) {
      this.setState({ footerHeight: height });
    }
  };

  //
  // Render

  render() {
    const {
      editGroups,
      isFetching,
      error,
      isSaving,
      saveError,
      qualities,
      profile,
      isInUse,
      onInputChange,
      onCutoffChange,
      onSavePress,
      onModalClose,
      onDeleteQualityProfilePress,
      ...otherProps
    } = this.props;

    const {
      id,
      name,
      upgradeAllowed,
      cutoff,
      minFormatScore,
      cutoffFormatScore,
      minUpgradeFormatScore,
      items,
      formatItems
    } = profile;

    return (
      <ModalContent onModalClose={onModalClose}>
        <Measure
          onMeasure={this.onHeaderMeasure}
        >
          <ModalHeader>
            {id ? 'Edit Quality Profile' : 'Add Quality Profile'}
          </ModalHeader>
        </Measure>

        <ModalBody>
          <Measure
            onMeasure={this.onBodyMeasure}
          >
            {
              isFetching &&
                <LoadingIndicator />
            }

            {
              !isFetching && (!!error || !profile.valid) &&
                <div>
                  {translate('UnableToAddANewQualityProfilePleaseTryAgain')}
                </div>
            }

            {
              !isFetching && !error && profile.valid &&
                <Form {...otherProps}>
                  <div className={styles.formGroupsContainer}>
                    <div className={styles.formGroupWrapper}>
                      <FormGroup size={sizes.EXTRA_SMALL}>
                        <FormLabel size={sizes.SMALL}>
                          Name
                        </FormLabel>

                        <FormInputGroup
                          type={inputTypes.TEXT}
                          name="name"
                          {...name}
                          onChange={onInputChange}
                        />
                      </FormGroup>

                      <FormGroup size={sizes.EXTRA_SMALL}>
                        <FormLabel size={sizes.SMALL}>
                          {translate('UpgradesAllowed')}
                        </FormLabel>

                        <FormInputGroup
                          type={inputTypes.CHECK}
                          name="upgradeAllowed"
                          {...upgradeAllowed}
                          helpText={translate('UpgradeAllowedHelpText')}
                          onChange={onInputChange}
                        />
                      </FormGroup>

                      {
                        upgradeAllowed.value &&
                          <FormGroup size={sizes.EXTRA_SMALL}>
                            <FormLabel size={sizes.SMALL}>
                              Upgrade Until
                            </FormLabel>

                            <FormInputGroup
                              type={inputTypes.SELECT}
                              name="cutoff"
                              {...cutoff}
                              values={qualities}
                              helpText={translate('CutoffHelpText')}
                              onChange={onCutoffChange}
                            />
                          </FormGroup>
                      }

                      {
                        formatItems.value.length > 0 &&
                          <FormGroup size={sizes.EXTRA_SMALL}>
                            <FormLabel size={sizes.SMALL}>
                              Minimum Custom Format Score
                            </FormLabel>

                            <FormInputGroup
                              type={inputTypes.NUMBER}
                              name="minFormatScore"
                              {...minFormatScore}
                              helpText={translate('MinFormatScoreHelpText')}
                              onChange={onInputChange}
                            />
                          </FormGroup>
                      }

                      {
                        upgradeAllowed.value && formatItems.value.length > 0 &&
                          <FormGroup size={sizes.EXTRA_SMALL}>
                            <FormLabel size={sizes.SMALL}>
                              Upgrade Until Custom Format Score
                            </FormLabel>

                            <FormInputGroup
                              type={inputTypes.NUMBER}
                              name="cutoffFormatScore"
                              {...cutoffFormatScore}
                              helpText={translate('CutoffFormatScoreHelpText')}
                              onChange={onInputChange}
                            />
                          </FormGroup>
                      }

                      {
                        upgradeAllowed.value && formatItems.value.length > 0 &&
                          <FormGroup size={sizes.EXTRA_SMALL}>
                            <FormLabel size={sizes.SMALL}>
                              {translate('MinUpgradeFormatScore')}
                            </FormLabel>

                            <FormInputGroup
                              type={inputTypes.NUMBER}
                              name="minUpgradeFormatScore"
                              min={0}
                              {...minUpgradeFormatScore}
                              helpText={translate('MinUpgradeFormatScoreHelpText')}
                              onChange={onInputChange}
                            />
                          </FormGroup>
                      }

                      <div className={styles.formatItemLarge}>
                        {getCustomFormatRender(formatItems, otherProps)}
                      </div>
                    </div>

                    <div className={styles.formGroupWrapper}>
                      <QualityProfileItems
                        editGroups={editGroups}
                        qualityProfileItems={items.value}
                        errors={items.errors}
                        warnings={items.warnings}
                        {...otherProps}
                      />
                      <QualityProfileSizeRules items={items.value} onInputChange={onInputChange} />
                    </div>

                    <div className={styles.formatItemSmall}>
                      {getCustomFormatRender(formatItems, otherProps)}
                    </div>
                  </div>
                </Form>
            }
          </Measure>
        </ModalBody>

        <Measure
          onMeasure={this.onFooterMeasure}
        >
          <ModalFooter>
            {
              id ?
                <div
                  className={styles.deleteButtonContainer}
                  title={isInUse ? translate('IsInUseCantDeleteAQualityProfileThatIsAttachedToAnAuthorOrImportList') : undefined}
                >
                  <Button
                    kind={kinds.DANGER}
                    isDisabled={isInUse}
                    onPress={onDeleteQualityProfilePress}
                  >
                    Delete
                  </Button>
                </div> :
                null
            }

            <Button
              onPress={onModalClose}
            >
              Cancel
            </Button>

            <SpinnerErrorButton
              action="save"
              isSpinning={isSaving}
              isDisabled={!profile.valid}
              error={saveError}
              onPress={onSavePress}
            >
              Save
            </SpinnerErrorButton>
          </ModalFooter>
        </Measure>
      </ModalContent>
    );
  }
}

export default EditQualityProfileModalContent;
