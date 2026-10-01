import React from 'react';
import ReactDOM from 'react-dom';
import { act } from 'react-dom/test-utils';
import { Provider } from 'react-redux';
import { createStore, applyMiddleware } from 'redux';
import thunk from 'redux-thunk';
import createMemoryHistory from 'history/createMemoryHistory';
import { ConnectedRouter, connectRouter, routerMiddleware } from 'connected-react-router';
import { Switch, Route } from 'react-router-dom';
import reducer from 'reducer';
import PrivateRoute from 'feature/Auth/PrivateRoute';
import PermissionRoute from 'feature/Auth/PermissionRoute';
import { AUTH_AUTHENTICATED } from 'feature/Auth/redux/actionTypes';
import { StartContainer, NoAccessContainer } from 'feature/Workspace';
import { AppLayout } from 'feature/Shell';

// Bộ dựng app thử nghiệm: router thật (ConnectedRouter + store thật), màn đích giả ghi nhận khi mount (thay cho gọi API).
export const pageMounts = [];

const page = name => class Page extends React.Component {
  componentDidMount() {
    pageMounts.push(name);
  }

  render() {
    return (
      <AppLayout>
        <div data-testid="screen">{name}</div>
      </AppLayout>
    );
  }
};

const Users = page('users');
const Roles = page('roles');
const Patients = page('patients');
const Intake = page('intake');
const Queue = page('clinic-queue');
const Vitals = page('vitals');

export const AppRoutes = () => (
  <Switch>
    <Route exact path="/login" render={() => <div data-testid="screen">login</div>} />
    <PrivateRoute exact path="/change-password" render={() => <div data-testid="screen">change-password</div>} />
    <PrivateRoute exact path="/start" component={StartContainer} />
    <PrivateRoute exact path="/no-access" component={NoAccessContainer} />
    <PermissionRoute exact path="/admin/users" component={Users} />
    <PermissionRoute exact path="/admin/roles" component={Roles} />
    <PermissionRoute exact path="/reception/patients" component={Patients} />
    <PermissionRoute exact path="/reception/intake/:patientId" component={Intake} />
    <PermissionRoute exact path="/clinic/queue" component={Queue} />
    <PermissionRoute exact path="/vitals" component={Vitals} />
    <PrivateRoute path="/" component={StartContainer} />
  </Switch>
);

export function me(permissions, extra = {}) {
  return {
    type: AUTH_AUTHENTICATED,
    payload: {
      id: 'u1',
      email: 'u1@benhvien.vn',
      fullName: 'Người dùng',
      permissions,
      mustChangePassword: false,
      ...extra,
    },
  };
}

export function mountApp({ path = '/start', permissions = null, extra } = {}) {
  pageMounts.length = 0;
  const history = createMemoryHistory({ initialEntries: [path] });
  const store = createStore(connectRouter(history)(reducer), applyMiddleware(routerMiddleware(history), thunk));
  if (permissions) store.dispatch(me(permissions, extra));
  const container = document.createElement('div');
  document.body.appendChild(container);
  act(() => {
    ReactDOM.render(
      <Provider store={store}>
        <ConnectedRouter history={history}>
          <AppRoutes />
        </ConnectedRouter>
      </Provider>,
      container,
    );
  });
  return {
    store,
    history,
    container,
    screen: () => {
      const el = container.querySelector('[data-testid="screen"]');
      return el ? el.textContent : null;
    },
    unmount: () => {
      ReactDOM.unmountComponentAtNode(container);
      container.remove();
    },
  };
}

export function click(el) {
  act(() => {
    el.dispatchEvent(new MouseEvent('click', { bubbles: true }));
  });
}

export function change(select, value) {
  act(() => {
    // eslint-disable-next-line no-param-reassign
    select.value = value;
    select.dispatchEvent(new Event('change', { bubbles: true }));
  });
}
