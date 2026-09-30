import React from 'react';
import ReactDOM from 'react-dom';
import { act } from 'react-dom/test-utils';
import { MemoryRouter } from 'react-router-dom';
import { Provider } from 'react-redux';
import { createStore, applyMiddleware } from 'redux';
import thunk from 'redux-thunk';
import ConnectedLogin from '../Login';

jest.mock('../redux/actions', () => ({
  login: jest.fn(),
}));

const { login } = require('../redux/actions');

function createTestStore() {
  const reducer = (state = { auth: { status: 'anonymous', mustChangePassword: false, sessionMessage: null } }) => state;
  return createStore(reducer, applyMiddleware(thunk));
}

function renderComponent() {
  const store = createTestStore();
  const container = document.createElement('div');
  document.body.appendChild(container);
  act(() => {
    ReactDOM.render(
      <Provider store={store}>
        <MemoryRouter>
          <ConnectedLogin location={{}} />
        </MemoryRouter>
      </Provider>,
      container,
    );
  });
  return { container };
}

function setValue(input, value) {
  const setter = Object.getOwnPropertyDescriptor(window.HTMLInputElement.prototype, 'value').set;
  setter.call(input, value);
  input.dispatchEvent(new Event('input', { bubbles: true }));
}

function fillForm(container) {
  setValue(container.querySelector('#login-email'), 'bacsi@benhvien.vn');
  setValue(container.querySelector('#login-password'), 'MatKhau123!');
}

function submit(container) {
  const form = container.querySelector('form');
  act(() => {
    form.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }));
  });
}

beforeEach(() => {
  login.mockReset();
});

// Epoch phiên đổi giữa chừng (đăng xuất/đăng nhập ở tab khác trong lúc chờ /me): actions.login trả null thay vì
// dispatch AUTH_AUTHENTICATED. Form không được kẹt ở trạng thái "Đang đăng nhập…" mãi mãi.
it('login bị bỏ qua do đổi epoch giữa chừng (trả null): form thoát trạng thái đang gửi, không hiện lỗi', async () => {
  login.mockReturnValue(() => Promise.resolve(null));
  const { container } = renderComponent();
  fillForm(container);

  await act(async () => {
    submit(container);
    await Promise.resolve();
    await Promise.resolve();
  });

  const button = container.querySelector('.c-login__submit');
  expect(button.disabled).toBe(false);
  expect(button.textContent).toBe('Đăng nhập');
  expect(container.querySelector('.c-login__submit[disabled]')).toBeNull();
  expect(container.textContent).not.toContain('Đang đăng nhập…');
});

it('đăng nhập thất bại (401): vẫn thoát trạng thái đang gửi và hiện lỗi như trước', async () => {
  const error = Object.assign(new Error('unauthorized'), { response: { status: 401, data: { title: 'Sai email hoặc mật khẩu.' } } });
  login.mockReturnValue(() => Promise.reject(error));
  const { container } = renderComponent();
  fillForm(container);

  await act(async () => {
    submit(container);
    await Promise.resolve();
    await Promise.resolve();
  });

  const button = container.querySelector('.c-login__submit');
  expect(button.disabled).toBe(false);
  expect(container.textContent).toContain('Sai email hoặc mật khẩu.');
});
