import React from 'react';
import ReactDOM from 'react-dom';
import { act } from 'react-dom/test-utils';
import { Provider } from 'react-redux';
import { createStore, applyMiddleware } from 'redux';
import thunk from 'redux-thunk';
import createHistory from 'history/createBrowserHistory';
import { ConnectedRouter, connectRouter, routerMiddleware } from 'connected-react-router';
import { Switch, Route } from 'react-router-dom';
import reducer from '../reducer';

// Regression for fix round 1 (task-6.4-review.md): src/index.js used to render routes under
// react-router-dom's <BrowserRouter>, which owns its own private history instance, while
// session handlers (onPasswordChangeRequired, expireSession) pushed on a SEPARATE
// history/createBrowserHistory() instance wired only into the redux-router reducer/middleware.
// A push on that separate instance changed window.location but never re-rendered anything,
// because BrowserRouter never saw it. The fix renders under connected-react-router's
// <ConnectedRouter history={history}> with that same shared `history` instance — this test
// proves pushing on that shared instance drives a re-render, the way src/index.js is now wired.
describe('shared history wiring (ConnectedRouter)', () => {
  let container;
  beforeEach(() => {
    container = document.createElement('div');
    document.body.appendChild(container);
  });
  afterEach(() => {
    ReactDOM.unmountComponentAtNode(container);
    container.remove();
  });

  it('re-renders to the pushed route when the same history instance used to build the store also drives ConnectedRouter', () => {
    const history = createHistory();
    const store = createStore(
      connectRouter(history)(reducer),
      applyMiddleware(routerMiddleware(history), thunk),
    );

    act(() => {
      ReactDOM.render(
        <Provider store={store}>
          <ConnectedRouter history={history}>
            <Switch>
              <Route exact path="/login" render={() => <div data-testid="screen">login</div>} />
              <Route exact path="/change-password" render={() => <div data-testid="screen">change-password</div>} />
            </Switch>
          </ConnectedRouter>
        </Provider>,
        container,
      );
    });

    act(() => { history.push('/login'); });
    expect(container.querySelector('[data-testid="screen"]').textContent).toBe('login');

    // Simulates onPasswordChangeRequired: a session handler pushing on the shared history
    // instance, outside of any event React itself dispatched.
    act(() => { history.push('/change-password'); });
    expect(container.querySelector('[data-testid="screen"]').textContent).toBe('change-password');
  });
});
