import React from 'react';
import { createRoot } from 'react-dom/client';
import createAppStore from 'Store/createAppStore';
import App from './App/App';
import createAppHistory from './App/createAppHistory';

import 'Diag/ConsoleApi';

export async function bootstrap() {
  const container = document.getElementById('root');

  if (!container) {
    throw new Error('Missing application root');
  }

  const history = createAppHistory();
  const store = createAppStore(history);

  createRoot(container).render(<App store={store} history={history} />);
}
