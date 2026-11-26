import { Component } from 'react';
import Card from 'Components/Card';
import FieldSet from 'Components/FieldSet';
import Icon from 'Components/Icon';
import PageSectionContent from 'Components/Page/PageSectionContent';
import { icons } from 'Helpers/Props';
import translate from 'Utilities/String/translate';
import type { FormatCard } from '../customFormatState';
import CustomFormat from './CustomFormat';
import EditCustomFormatModalConnector from './EditCustomFormatModalConnector';
import styles from './CustomFormats.css';

interface Props {
  isFetching: boolean;
  isPopulated: boolean;
  error?: unknown;
  isDeleting: boolean;
  items: FormatCard[];
  onConfirmDeleteCustomFormat: (id: number) => void;
  onCloneCustomFormatPress: (id: number) => void;
}
interface State {
  isCustomFormatModalOpen: boolean;
  tagsFromId?: number;
}

class CustomFormats extends Component<Props, State> {

  //
  // Lifecycle

  constructor(props: Props) {
    super(props);

    this.state = {
      isCustomFormatModalOpen: false,
      tagsFromId: undefined
    };
  }

  //
  // Listeners

  onCloneCustomFormatPress = (id: number) => {
    this.props.onCloneCustomFormatPress(id);
    this.setState({
      isCustomFormatModalOpen: true,
      tagsFromId: id
    });
  };

  onEditCustomFormatPress = () => {
    this.setState({ isCustomFormatModalOpen: true });
  };

  onModalClose = () => {
    this.setState({
      isCustomFormatModalOpen: false,
      tagsFromId: undefined
    });
  };

  //
  // Render

  render() {
    const {
      items,
      isDeleting,
      onConfirmDeleteCustomFormat,
      ...otherProps
    } = this.props;

    return (
      <FieldSet legend={translate('CustomFormats')}>
        <PageSectionContent
          errorMessage={translate('UnableToLoadCustomFormats')}
          {...otherProps}
        >
          <div className={styles.customFormats}>
            {
              items.map((item) => {
                return (
                  <CustomFormat
                    key={item.id}
                    {...item}
                    isDeleting={isDeleting}
                    onConfirmDeleteCustomFormat={onConfirmDeleteCustomFormat}
                    onCloneCustomFormatPress={this.onCloneCustomFormatPress}
                  />
                );
              })
            }

            <Card
              className={styles.addCustomFormat}
              aria-label={translate('AddCustomFormat')}
              onPress={this.onEditCustomFormatPress}
            >
              <div className={styles.center}>
                <Icon
                  name={icons.ADD}
                  aria-hidden={true}
                  size={45}
                />
              </div>
            </Card>
          </div>

          <EditCustomFormatModalConnector
            isOpen={this.state.isCustomFormatModalOpen}
            tagsFromId={this.state.tagsFromId}
            onModalClose={this.onModalClose}
          />

        </PageSectionContent>
      </FieldSet>
    );
  }
}

export default CustomFormats;
