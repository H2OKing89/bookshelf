import React from 'react';
import { createRoot } from 'react-dom/client';
import createAppStore from 'Store/createAppStore';
import App, { createAppRouter } from './App/App';

import 'Diag/ConsoleApi';

export async function bootstrap() {
  const container = document.getElementById('root');

  if (!container) {
    throw new Error('Missing application root');
  }

  const router = createAppRouter();
  const store = createAppStore();

  createRoot(container).render(<App store={store} router={router} />);
}
