import './polyfills';
import 'Styles/globals.css';
import './index.css';

const initializeUrl = `${
  window.Readarr.urlBase
}/initialize.json?t=${Date.now()}`;
const response = await fetch(initializeUrl);

// The development proxy can serve the UI shell before authentication. Let the
// backend own the actual login flow and preserve the current navigation target.
const redirectUrl = new URL(response.url);
if (response.redirected && redirectUrl.pathname === `${window.Readarr.urlBase}/login`) {
  redirectUrl.searchParams.set('returnUrl', `${window.location.pathname}${window.location.search}${window.location.hash}`);
  window.location.replace(`${redirectUrl.pathname}${redirectUrl.search}`);
} else {
  if (!response.ok) {
    throw new Error(`Initialization failed (${response.status}).`);
  }
  window.Readarr = await response.json();
  const { bootstrap } = await import('./bootstrap');
  await bootstrap();
}
