import React from 'react';
import ReactDOM from 'react-dom';
import { act } from 'react-dom/test-utils';
import { Provider } from 'react-redux';
import { createStore, applyMiddleware } from 'redux';
import thunk from 'redux-thunk';
import reducer from '../../../reducer';
import { Loading, loadingAction } from '..';

// Smoke test: Jest + jsdom + React render + Redux store wiring.
describe('Loading feature', () => {
  let container;
  beforeEach(() => {
    container = document.createElement('div');
    document.body.appendChild(container);
  });
  afterEach(() => {
    ReactDOM.unmountComponentAtNode(container);
    container.remove();
  });

  it('shows the spinner while the wrapped promise runs and hides it after (even on error)', async () => {
    const store = createStore(reducer, applyMiddleware(thunk));
    act(() => {
      ReactDOM.render(
        <Provider store={store}>
          <Loading />
        </Provider>,
        container,
      );
    });
    expect(container.querySelector('[data-testid="loading-modal"]')).toBeNull();

    let release;
    const pending = store.dispatch(loadingAction(() => new Promise((_, reject) => { release = reject; })));
    expect(store.getState().loadingModal.count).toBe(1);
    expect(container.querySelector('[data-testid="loading-modal"]')).not.toBeNull();

    release(new Error('boom'));
    await expect(pending).rejects.toThrow('boom');
    expect(store.getState().loadingModal.count).toBe(0);
    expect(container.querySelector('[data-testid="loading-modal"]')).toBeNull();
  });

  it('has a jsdom environment with document.cookie', () => {
    document.cookie = 'XSRF-TOKEN=abc';
    expect(document.cookie).toContain('XSRF-TOKEN=abc');
  });
});
