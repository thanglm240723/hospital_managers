import React from 'react';
import ReactDOM from 'react-dom';
import { BrowserRouter, Switch, Route } from 'react-router-dom';

import { Provider } from 'react-redux';
import thunk from 'redux-thunk';
import createHistory from 'history/createBrowserHistory';
import { applyMiddleware, compose, createStore } from 'redux';
import { connectRouter, routerMiddleware } from 'connected-react-router';

import getMuiTheme from 'material-ui/styles/getMuiTheme';
import MuiThemeProvider from 'material-ui/styles/MuiThemeProvider';

import { Loading } from './feature';
import {
  Login, ChangePassword, PrivateRoute, bootAuth, expireSession, AUTH_LOGGED_OUT,
} from './feature/Auth';
import { setSessionHandlers } from './service/http';
import { startAuthSync, onRemoteLogout } from './feature/Auth/session/refreshCoordinator';
import { startRefreshScheduler } from './feature/Auth/session/refreshScheduler';
import combinedReducers from './reducer';
import * as serviceWorker from './serviceWorker';

import './scss/index.scss';

const history = createHistory();
const middleware = routerMiddleware(history);

// const composeEnhancers = process.env.NODE_ENV === 'production'
//   ? compose
//   : window.__REDUX_DEVTOOLS_EXTENSION_COMPOSE__ === undefined // eslint-disable-line
//     ? compose
//     : window.__REDUX_DEVTOOLS_EXTENSION_COMPOSE__; // eslint-disable-line
const composeEnhancers =
  process.env.NODE_ENV === 'development' && window.__REDUX_DEVTOOLS_EXTENSION_COMPOSE__ ? window.__REDUX_DEVTOOLS_EXTENSION_COMPOSE__ : compose;

const store = createStore(
  connectRouter(history)(combinedReducers),
  composeEnhancers(
    applyMiddleware(middleware, thunk),
  ),
);

// Phiên đăng nhập: token trong RAM, refresh chủ động, đồng bộ đa tab.
setSessionHandlers({
  onSessionExpired: () => store.dispatch(expireSession()),
  onPasswordChangeRequired: () => history.push('/change-password'),
});
onRemoteLogout(() => store.dispatch({ type: AUTH_LOGGED_OUT }));
startAuthSync();
startRefreshScheduler(() => store.dispatch(expireSession()));
store.dispatch(bootAuth());

const App = () => <div className="c-panel">HMS</div>;

const ReactApp = () => (
  <BrowserRouter>
    <Provider store={store}>
      <MuiThemeProvider muiTheme={getMuiTheme()}>
        <React.Fragment>
          <Loading />
          <Switch>
            <Route exact path="/login" component={Login} />
            <PrivateRoute exact path="/change-password" component={ChangePassword} />
            <PrivateRoute path="/" component={App} />
          </Switch>
        </React.Fragment>
      </MuiThemeProvider>
    </Provider>
  </BrowserRouter>
);

ReactDOM.render(<ReactApp />, document.getElementById('root'));

// If you want your app to work offline and load faster, you can change
// unregister() to register() below. Note this comes with some pitfalls.
// Learn more about service workers: http://bit.ly/CRA-PWA
serviceWorker.unregister();
