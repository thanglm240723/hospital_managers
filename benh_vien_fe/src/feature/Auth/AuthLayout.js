import React from 'react';

/* eslint-disable react/prop-types */

// Điền theo bệnh viện triển khai.
export const HOSPITAL_NAME = '[TÊN BỆNH VIỆN]';
export const IT_EXTENSION = '[SỐ MÁY LẺ]';

export const Icon = ({ children, size = 22 }) => (
  <svg width={size} height={size} viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
    {children}
  </svg>
);

const ALERT_ICONS = {
  error: [<circle key="c" cx="12" cy="12" r="9" />, <path key="p" d="M12 7v6M12 16.5v.5" />],
  wait: [<circle key="c" cx="12" cy="12" r="9" />, <path key="p" d="M12 7v5l3 2" />],
  info: [<circle key="c" cx="12" cy="12" r="9" />, <path key="p" d="M12 11v6M12 7.5v.5" />],
};

export const Alert = ({ kind, children }) => (
  <div className={`c-login__alert c-login__alert--${kind}`} role={kind === 'info' ? 'status' : 'alert'}>
    <Icon>{ALERT_ICONS[kind]}</Icon>
    <span>{children}</span>
  </div>
);

export const AuthLayout = ({ children }) => (
  <div className="c-login">
    <aside className="c-login__aside">
      <div className="c-login__brand">
        <div className="c-login__logo">
          <Icon size={26}><path d="M12 5v14M5 12h14" /></Icon>
        </div>
        {HOSPITAL_NAME}
      </div>
      <div>
        <p className="c-login__headline">Hệ thống quản lý bệnh viện</p>
        <p className="c-login__tagline">Hồ sơ bệnh nhân, chỉ định và lịch trực — ở một nơi.</p>
      </div>
      <div className="c-login__notice">
        <Icon>
          <rect x="3" y="4" width="18" height="12" rx="2" />
          <path d="M8 20h8M12 16v4" />
        </Icon>
        <span>Máy trạm dùng chung — hãy đăng xuất khi rời máy.</span>
      </div>
    </aside>
    <main className="c-login__main">{children}</main>
  </div>
);

// Ô mật khẩu có nút hiện/ẩn và cảnh báo Caps Lock.
export class PasswordField extends React.Component {
  state = { visible: false, capsLock: false };

  input = React.createRef();

  toggle = () => this.setState(state => ({ visible: !state.visible }));

  handleKey = (event) => {
    if (event.getModifierState) this.setState({ capsLock: event.getModifierState('CapsLock') });
  };

  focus() {
    if (this.input.current) this.input.current.focus();
  }

  render() {
    const {
      id, label, hint, error, ...inputProps
    } = this.props;
    const { visible, capsLock } = this.state;
    return (
      <div className="c-login__field">
        {/* eslint-disable-next-line jsx-a11y/label-has-associated-control */}
        <label className="c-login__label" htmlFor={id}>{label}</label>
        <div className="c-login__password">
          <input
            {...inputProps}
            id={id}
            ref={this.input}
            className={`c-login__input${error ? ' is-invalid' : ''}`}
            type={visible ? 'text' : 'password'}
            onKeyUp={this.handleKey}
            onKeyDown={this.handleKey}
            aria-invalid={!!error}
          />
          <button
            type="button"
            className="c-login__toggle"
            onClick={this.toggle}
            aria-label={visible ? 'Ẩn mật khẩu' : 'Hiện mật khẩu'}
            aria-pressed={visible}
          >
            {visible
              ? <Icon><path d="M3 3l18 18M10.6 6.1A9.8 9.8 0 0 1 12 6c5 0 9 6 9 6a15 15 0 0 1-3 3.5M6.6 6.6C4.3 8.1 3 12 3 12s4 6 9 6a8.7 8.7 0 0 0 4.4-1.2M9.9 9.9a3 3 0 0 0 4.2 4.2" /></Icon>
              : (
                <Icon>
                  <path d="M3 12s4-6 9-6 9 6 9 6-4 6-9 6-9-6-9-6z" />
                  <circle cx="12" cy="12" r="3" />
                </Icon>
              )}
          </button>
        </div>
        {capsLock && (
          <div className="c-login__caps" role="status">
            <Icon size={18}><path d="M12 4l7 8h-4v6H9v-6H5z" /></Icon>
            <span>Caps Lock đang bật</span>
          </div>
        )}
        {error && <p className="c-login__field-error">{error}</p>}
        {!error && hint && <p className="c-login__hint">{hint}</p>}
      </div>
    );
  }
}
