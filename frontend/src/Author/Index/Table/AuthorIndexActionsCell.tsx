import React, { Component } from 'react';
import DeleteAuthorModal from 'Author/Delete/DeleteAuthorModal';
import EditAuthorModalConnector from 'Author/Edit/EditAuthorModalConnector';
import type { AuthorId } from 'Book/BookIdentity';
import IconButton from 'Components/Link/IconButton';
import SpinnerIconButton from 'Components/Link/SpinnerIconButton';
import VirtualTableRowCell from 'Components/Table/Cells/VirtualTableRowCell';
import { icons } from 'Helpers/Props';
import translate from 'Utilities/String/translate';

interface AuthorIndexActionsCellProps {
  id: AuthorId;
  isRefreshingAuthor: boolean;
  onRefreshAuthorPress: () => void;
  className?: string;
}

interface AuthorIndexActionsCellState {
  isEditAuthorModalOpen: boolean;
  isDeleteAuthorModalOpen: boolean;
}

class AuthorIndexActionsCell extends Component<AuthorIndexActionsCellProps, AuthorIndexActionsCellState> {

  //
  // Lifecycle

  constructor(props: AuthorIndexActionsCellProps) {
    super(props);

    this.state = {
      isEditAuthorModalOpen: false,
      isDeleteAuthorModalOpen: false
    };
  }

  //
  // Listeners

  onEditAuthorPress = () => {
    this.setState({ isEditAuthorModalOpen: true });
  };

  onEditAuthorModalClose = () => {
    this.setState({ isEditAuthorModalOpen: false });
  };

  onDeleteAuthorPress = () => {
    this.setState({
      isEditAuthorModalOpen: false,
      isDeleteAuthorModalOpen: true
    });
  };

  onDeleteAuthorModalClose = () => {
    this.setState({ isDeleteAuthorModalOpen: false });
  };

  //
  // Render

  render() {
    const {
      id,
      isRefreshingAuthor,
      onRefreshAuthorPress,
      ...otherProps
    } = this.props;

    const {
      isEditAuthorModalOpen,
      isDeleteAuthorModalOpen
    } = this.state;

    return (
      <VirtualTableRowCell
        {...otherProps}
      >
        <SpinnerIconButton
          name={icons.REFRESH}
          title={translate('RefreshAuthor')}
          isSpinning={isRefreshingAuthor}
          onPress={onRefreshAuthorPress}
        />

        <IconButton
          name={icons.EDIT}
          title={translate('EditAuthor')}
          onPress={this.onEditAuthorPress}
        />

        <EditAuthorModalConnector
          isOpen={isEditAuthorModalOpen}
          authorId={id}
          onModalClose={this.onEditAuthorModalClose}
          onDeleteAuthorPress={this.onDeleteAuthorPress}
        />

        <DeleteAuthorModal
          isOpen={isDeleteAuthorModalOpen}
          authorId={id}
          onModalClose={this.onDeleteAuthorModalClose}
        />
      </VirtualTableRowCell>
    );
  }
}

export default AuthorIndexActionsCell;
