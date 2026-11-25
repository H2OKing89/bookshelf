import { QueryClientProvider } from '@tanstack/react-query';
import PropTypes from 'prop-types';
import React from 'react';
import { Provider } from 'react-redux';
import { createBrowserRouter, Outlet, RouterProvider } from 'react-router-dom';
import { SavedSettingsStatus } from 'Components/ActionStatus';
import { DocumentTitleProvider } from 'Components/Page/DocumentTitle';
import PageConnector from 'Components/Page/PageConnector';
import UnsavedNavigationProvider from 'Settings/UnsavedNavigationProvider';
import ApplyTheme from './ApplyTheme';
import appRoutes from './AppRoutes';
import migrateBrowserHistory from './migrateBrowserHistory';
import queryClient from './queryClient';

function PageLayout() {
  return (
    <ApplyTheme>
      <UnsavedNavigationProvider>
        <PageConnector>
          <Outlet />
        </PageConnector>
        <SavedSettingsStatus />
      </UnsavedNavigationProvider>
    </ApplyTheme>
  );
}

export function createAppRouter() {
  const updateEntry = migrateBrowserHistory(window);
  const router = createBrowserRouter([
    { Component: PageLayout, children: appRoutes }
  ], { basename: window.Readarr.urlBase || undefined });

  if (updateEntry) {
    let currentKey = router.state.location.key;
    router.subscribe(({ location }) => {
      if (location.key !== currentKey) {
        currentKey = location.key;
        updateEntry();
      }
    });
  }

  return router;
}

function App({ store, router }) {
  return (
    <DocumentTitleProvider title={window.Readarr.instanceName}>
      <QueryClientProvider client={queryClient}>
        <Provider store={store}>
          <RouterProvider router={router} />
        </Provider>
      </QueryClientProvider>
    </DocumentTitleProvider>
  );
}

App.propTypes = {
  store: PropTypes.object.isRequired,
  router: PropTypes.object.isRequired
};

export default App;
