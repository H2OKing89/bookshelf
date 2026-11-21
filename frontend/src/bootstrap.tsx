import { createBrowserHistory } from 'history';
import React from 'react';
import { createRoot } from 'react-dom/client';
import createAppStore from 'Store/createAppStore';
import App from './App/App';

import 'Diag/ConsoleApi';

export async function bootstrap() {
  const container = document.getElementById('root');

  if (!container) {
    throw new Error('Missing application root');
  }

  const history = createBrowserHistory();
  const store = createAppStore(history);

  createRoot(container).render(<App store={store} history={history} />);
}
