import React from 'react';
import ReactDOM from 'react-dom';
import { act } from 'react-dom/test-utils';
import { MemoryRouter } from 'react-router-dom';
import { Provider } from 'react-redux';
import { createStore, applyMiddleware } from 'redux';
import thunk from 'redux-thunk';
import ConnectedChangePassword from '../ChangePassword';

jest.mock('../redux/actions', () => ({
  changePassword: jest.fn(),
  loadMe: jest.fn(),
  logout: jest.fn(() => ({ type: 'noop' })),
}));

const { changePassword, loadMe } = require('../redux/actions');

function createTestStore() {
  const reducer = (state = { auth: { mustChangePassword: false } }) => state;
  return createStore(reducer, applyMiddleware(thunk));
}

function renderComponent({ history } = {}) {
  const store = createTestStore();
  const replace = jest.fn();
  const container = document.createElement('div');
  document.body.appendChild(container);
  act(() => {
    ReactDOM.render(
      <Provider store={store}>
        <MemoryRouter>
          <ConnectedChangePassword history={{ replace, ...(history || {}) }} />
        </MemoryRouter>
      </Provider>,
      container,
    );
  });
  return { container, replace };
}

function setValue(input, value) {
  const setter = Object.getOwnPropertyDescriptor(window.HTMLInputElement.prototype, 'value').set;
  setter.call(input, value);
  input.dispatchEvent(new Event('input', { bubbles: true }));
}

function fillForm(container) {
  setValue(container.querySelector('#cp-current'), 'OldPassword1!');
  setValue(container.querySelector('#cp-new'), 'NewPassword1!!');
  setValue(container.querySelector('#cp-confirm'), 'NewPassword1!!');
}

function submit(container) {
  const form = container.querySelector('form');
  act(() => {
    form.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }));
  });
}

beforeEach(() => {
  changePassword.mockReset();
  loadMe.mockReset();
});

it('double-click while submitting: only one POST', async () => {
  let resolveChange;
  changePassword.mockReturnValue(() => new Promise((resolve) => { resolveChange = resolve; }));
  loadMe.mockReturnValue(() => Promise.resolve({ mustChangePassword: false }));
  const { container } = renderComponent();
  fillForm(container);

  submit(container);
  submit(container);

  expect(changePassword).toHaveBeenCalledTimes(1);

  await act(async () => {
    resolveChange();
    await Promise.resolve();
    await Promise.resolve();
  });
});

it('400 error shows message on the right field', async () => {
  const error = { response: { data: { title: 'Dữ liệu không hợp lệ.', errors: { currentPassword: ['Mật khẩu hiện tại không đúng.'] } } } };
  changePassword.mockReturnValue(() => Promise.reject(error));
  const { container } = renderComponent();
  fillForm(container);

  await act(async () => {
    submit(container);
    await Promise.resolve();
    await Promise.resolve();
  });

  expect(container.querySelector('.c-login__field-error').textContent).toBe('Mật khẩu hiện tại không đúng.');
});

it('403 shows an appropriate error message', async () => {
  const error = { response: { data: { title: 'Không xác thực được yêu cầu. Vui lòng tải lại trang.' } } };
  changePassword.mockReturnValue(() => Promise.reject(error));
  const { container } = renderComponent();
  fillForm(container);

  await act(async () => {
    submit(container);
    await Promise.resolve();
    await Promise.resolve();
  });

  expect(container.textContent).toContain('Không xác thực được yêu cầu. Vui lòng tải lại trang.');
});

it('success: navigates to /start and the form no longer holds the password', async () => {
  changePassword.mockReturnValue(() => Promise.resolve({ accessToken: 't', expiresAtUtc: new Date().toISOString() }));
  loadMe.mockReturnValue(() => Promise.resolve({ mustChangePassword: false }));
  const { container, replace } = renderComponent();
  fillForm(container);

  await act(async () => {
    submit(container);
    await Promise.resolve();
    await Promise.resolve();
    await Promise.resolve();
  });

  expect(replace).toHaveBeenCalledWith('/start');
  const current = container.querySelector('#cp-current');
  expect(current === null || current.value === '').toBe(true);
});

it('POST 200 + GET me fails: shows the changed status; retry only calls loadMe, no resending the old password', async () => {
  changePassword.mockReturnValue(() => Promise.resolve({ accessToken: 't', expiresAtUtc: new Date().toISOString() }));
  loadMe.mockReturnValueOnce(() => Promise.reject(new Error('network')));
  const { container, replace } = renderComponent();
  fillForm(container);

  await act(async () => {
    submit(container);
    await Promise.resolve();
    await Promise.resolve();
    await Promise.resolve();
  });

  expect(container.textContent).toContain('Mật khẩu đã đổi. Chưa tải lại được thông tin tài khoản.');
  expect(replace).not.toHaveBeenCalled();

  loadMe.mockReturnValueOnce(() => Promise.resolve({ mustChangePassword: false }));
  const retryButton = Array.from(container.querySelectorAll('button')).find(b => b.textContent === 'Thử lại');
  await act(async () => {
    retryButton.dispatchEvent(new MouseEvent('click', { bubbles: true }));
    await Promise.resolve();
    await Promise.resolve();
  });

  expect(changePassword).toHaveBeenCalledTimes(1);
  expect(loadMe).toHaveBeenCalledTimes(2);
  expect(replace).toHaveBeenCalledWith('/start');
});
