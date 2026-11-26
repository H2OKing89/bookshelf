import { Component } from 'react';
import Card from 'Components/Card';
import FieldSet from 'Components/FieldSet';
import Icon from 'Components/Icon';
import PageSectionContent from 'Components/Page/PageSectionContent';
import { icons } from 'Helpers/Props';
import translate from 'Utilities/String/translate';
import type { ImportListCard } from '../../providerCardState';
import AddImportListModal from './AddImportListModal';
import EditImportListModalConnector from './EditImportListModalConnector';
import ImportList from './ImportList';
import styles from './ImportLists.css';

interface Props {
  isFetching: boolean;
  isPopulated: boolean;
  error?: unknown;
  items: ImportListCard[];
  onConfirmDeleteImportList: (id: number) => void;
}
interface State {
  isAddImportListModalOpen: boolean;
  isEditImportListModalOpen: boolean;
}

class ImportLists extends Component<Props, State> {

  //
  // Lifecycle

  constructor(props: Props) {
    super(props);

    this.state = {
      isAddImportListModalOpen: false,
      isEditImportListModalOpen: false
    };
  }

  //
  // Listeners

  onAddImportListPress = () => {
    this.setState({ isAddImportListModalOpen: true });
  };

  onAddImportListModalClose = ({ listSelected = false }: { listSelected?: boolean } = {}) => {
    this.setState({
      isAddImportListModalOpen: false,
      isEditImportListModalOpen: listSelected
    });
  };

  onEditImportListModalClose = () => {
    this.setState({ isEditImportListModalOpen: false });
  };

  //
  // Render

  render() {
    const {
      items,
      onConfirmDeleteImportList,
      ...otherProps
    } = this.props;

    const {
      isAddImportListModalOpen,
      isEditImportListModalOpen
    } = this.state;

    return (
      <FieldSet
        legend={translate('ImportLists')}
      >
        <PageSectionContent
          errorMessage={translate('UnableToLoadLists')}
          {...otherProps}
        >
          <div className={styles.lists}>
            {
              items.map((item) => {
                return (
                  <ImportList
                    key={item.id}
                    {...item}
                    onConfirmDeleteImportList={onConfirmDeleteImportList}
                  />
                );
              })
            }

            <Card
              className={styles.addList}
              aria-label={translate('AddList')}
              onPress={this.onAddImportListPress}
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

          <AddImportListModal
            isOpen={isAddImportListModalOpen}
            onModalClose={this.onAddImportListModalClose}
          />

          <EditImportListModalConnector
            isOpen={isEditImportListModalOpen}
            onModalClose={this.onEditImportListModalClose}
          />
        </PageSectionContent>
      </FieldSet>
    );
  }
}

export default ImportLists;
