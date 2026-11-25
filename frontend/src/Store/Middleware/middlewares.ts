import { applyMiddleware, compose, StoreEnhancer } from 'redux';
import thunk from 'redux-thunk';

interface DevToolsWindow extends Window {
  __REDUX_DEVTOOLS_EXTENSION_COMPOSE__?: (
    enhancer: StoreEnhancer
  ) => StoreEnhancer;
}

export default function middlewares() {
  const enhancer = applyMiddleware(thunk);
  const browser: DevToolsWindow = window;
  // eslint-disable-next-line no-underscore-dangle
  const composeEnhancers = browser.__REDUX_DEVTOOLS_EXTENSION_COMPOSE__ || compose;
  return composeEnhancers(enhancer);
}
