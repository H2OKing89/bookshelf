import PropTypes from 'prop-types';
import React, { Component } from 'react';
import { connect } from 'react-redux';
import { useLocation } from 'react-router-dom';
import { createSelector } from 'reselect';
import { clearSearchResults, getSearchResults } from 'Store/Actions/searchActions';
import { fetchRootFolders } from 'Store/Actions/settingsActions';
import parseUrl from 'Utilities/String/parseUrl';
import AddNewItem from './AddNewItem';

function createMapStateToProps() {
  return createSelector(
    (state) => state.search,
    (state) => state.authors.items.length,
    (state, { query }) => query,
    (search, existingAuthorsCount, query) => {
      return {
        ...search,
        term: query,
        hasExistingAuthors: existingAuthorsCount > 0
      };
    }
  );
}

const mapDispatchToProps = {
  getSearchResults,
  clearSearchResults,
  fetchRootFolders
};

class AddNewItemSearch extends Component {

  //
  // Lifecycle

  constructor(props, context) {
    super(props, context);

    this._searchTimeout = null;
  }

  componentDidMount() {
    this.props.fetchRootFolders();
  }

  componentWillUnmount() {
    if (this._searchTimeout) {
      clearTimeout(this._searchTimeout);
    }

    this.props.clearSearchResults();
  }

  //
  // Listeners

  onSearchChange = (term) => {
    if (this._searchTimeout) {
      clearTimeout(this._searchTimeout);
    }

    if (term.trim() === '') {
      this.props.clearSearchResults();
    } else {
      this._searchTimeout = setTimeout(() => {
        this.props.getSearchResults({ term });
      }, 300);
    }
  };

  onClearSearch = () => {
    this.props.clearSearchResults();
  };

  //
  // Render

  render() {
    const {
      term,
      ...otherProps
    } = this.props;

    return (
      <AddNewItem
        term={term}
        {...otherProps}
        onSearchChange={this.onSearchChange}
        onClearSearch={this.onClearSearch}
      />
    );
  }
}

AddNewItemSearch.propTypes = {
  term: PropTypes.string,
  getSearchResults: PropTypes.func.isRequired,
  clearSearchResults: PropTypes.func.isRequired,
  fetchRootFolders: PropTypes.func.isRequired
};

const ConnectedAddNewItem = connect(createMapStateToProps, mapDispatchToProps)(AddNewItemSearch);

export default function AddNewItemConnector() {
  const { search } = useLocation();
  const { params } = parseUrl(search);

  return <ConnectedAddNewItem query={params.term} />;
}
