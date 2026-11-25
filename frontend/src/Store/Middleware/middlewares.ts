import { applyMiddleware, StoreEnhancer } from 'redux';
import { withExtraArgument } from 'redux-thunk';
import { LegacyDispatch, LegacyRootState, StoreAction } from 'Store/StateTypes';

type DispatchEnhancer = StoreEnhancer<{ dispatch: LegacyDispatch }>;

interface DevToolsWindow extends Window {
  __REDUX_DEVTOOLS_EXTENSION_COMPOSE__?: (
    enhancer: DispatchEnhancer
  ) => DispatchEnhancer;
}

export default function middlewares(): DispatchEnhancer {
  const enhancer = applyMiddleware(withExtraArgument<LegacyRootState, StoreAction>());
  const browser: DevToolsWindow = window;
  // eslint-disable-next-line no-underscore-dangle
  const composeEnhancers = browser.__REDUX_DEVTOOLS_EXTENSION_COMPOSE__;
  return composeEnhancers ? composeEnhancers(enhancer) : enhancer;
}
