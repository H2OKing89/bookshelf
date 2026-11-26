declare module '*.module.css';

interface Window {
  onCompleteOauth?: (query: string, onComplete: () => void) => void;
  Readarr: {
    apiKey: string;
    apiRoot: string;
    instanceName: string;
    theme: string;
    urlBase: string;
    version: string;
    isProduction: boolean;
  };
}
