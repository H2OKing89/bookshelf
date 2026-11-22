import { QueryClientProvider } from '@tanstack/react-query';
import { ConnectedRouter } from 'connected-react-router';
import PropTypes from 'prop-types';
import React from 'react';
import DocumentTitle from 'react-document-title';
import { Provider } from 'react-redux';
import { SavedSettingsStatus } from 'Components/ActionStatus';
import PageConnector from 'Components/Page/PageConnector';
import UnsavedNavigationModal from 'Settings/UnsavedNavigationModal';
import ApplyTheme from './ApplyTheme';
import AppRoutes from './AppRoutes';
import queryClient from './queryClient';

function App({ store, history }) {
  return (
    <DocumentTitle title={window.Readarr.instanceName}>
      <QueryClientProvider client={queryClient}>
        <Provider store={store}>
          <ConnectedRouter history={history}>
            <ApplyTheme>
              <>
                <PageConnector>
                  <AppRoutes app={App} />
                </PageConnector>
                <SavedSettingsStatus />
                <UnsavedNavigationModal />
              </>
            </ApplyTheme>
          </ConnectedRouter>
        </Provider>
      </QueryClientProvider>
    </DocumentTitle>
  );
}

App.propTypes = {
  store: PropTypes.object.isRequired,
  history: PropTypes.object.isRequired
};

export default App;
