import React from 'react';
import PropTypes from 'prop-types';

const BIRTH_MODES = [
  { value: 'full', label: 'Biết đầy đủ ngày sinh' },
  { value: 'year', label: 'Chỉ biết năm sinh' },
  { value: 'unknown', label: 'Chưa biết ngày sinh' },
];

class CreatePatientDialog extends React.Component {
  state = {
    fullName: '',
    gender: 'male',
    birthMode: 'full',
    birthDate: '',
    birthYear: '',
    address: '',
    phone: '',
    documentNumber: '',
    step: 'form',
    duplicates: [],
    forceCreateReason: '',
    checking: false,
  };

  handleSubmit = async (event) => {
    event.preventDefault();
    const { onCheckDuplicates } = this.props;
    const { fullName } = this.state;
    this.setState({ checking: true });
    const duplicates = await onCheckDuplicates({ fullName });
    if (duplicates.length > 0) {
      this.setState({ checking: false, step: 'duplicates', duplicates });
      return;
    }
    await this.submitCreate();
  };

  handleForceCreate = async () => {
    const { forceCreateReason } = this.state;
    if (!forceCreateReason.trim()) return;
    await this.submitCreate();
  };

  submitCreate = async () => {
    const { onCreate, onDone } = this.props;
    const {
      birthMode, fullName, gender, address, phone, documentNumber,
    } = this.state;
    const patient = await onCreate({
      fullName,
      gender,
      birthDate: this.buildBirthDate(),
      birthYearOnly: birthMode === 'year',
      address,
      phone,
      documentNumber: documentNumber || null,
    });
    this.setState({ checking: false });
    onDone(patient.id);
  };

  buildBirthDate() {
    const { birthMode, birthDate, birthYear } = this.state;
    if (birthMode === 'full') return birthDate;
    if (birthMode === 'year') return birthYear ? `${birthYear}-01-01` : '';
    return '';
  }

  render() {
    const { onClose, onDone } = this.props;
    const {
      fullName, gender, birthMode, birthDate, birthYear, address, phone, documentNumber,
      step, duplicates, forceCreateReason, checking,
    } = this.state;

    if (step === 'duplicates') {
      return (
        <div className="c-confirm-dialog" role="dialog" aria-modal="true" aria-label="Hồ sơ nghi trùng">
          <div className="c-confirm-dialog__backdrop" onClick={onClose} role="presentation" />
          <div className="c-confirm-dialog__box">
            <h2 className="c-confirm-dialog__title">Có hồ sơ nghi trùng</h2>
            <p className="c-confirm-dialog__desc">Hệ thống tìm thấy hồ sơ trùng họ tên. Chọn dùng hồ sơ cũ hoặc vẫn tạo mới.</p>
            <table className="c-table">
              <thead>
                <tr>
                  <th>Họ tên</th>
                  <th>Ngày sinh</th>
                  <th>Điện thoại</th>
                  <th />
                </tr>
              </thead>
              <tbody>
                {duplicates.map(d => (
                  <tr key={d.id}>
                    <td>{d.fullName}</td>
                    <td>{d.birthDate}</td>
                    <td>{d.phone}</td>
                    <td>
                      <button type="button" className="c-toolbar__action" style={{ marginLeft: 0 }} onClick={() => onDone(d.id)}>
                        Dùng hồ sơ này
                      </button>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>

            <div className="c-login__field" style={{ marginTop: 12 }}>
              {/* eslint-disable-next-line jsx-a11y/label-has-associated-control */}
              <label className="c-login__label" htmlFor="force-reason">Lý do vẫn tạo mới (bắt buộc)</label>
              <input
                id="force-reason"
                className="c-login__input"
                value={forceCreateReason}
                onChange={e => this.setState({ forceCreateReason: e.target.value })}
              />
            </div>
            <div className="c-confirm-dialog__actions">
              <button type="button" className="c-login__secondary" onClick={onClose}>Hủy</button>
              <button type="button" className="c-login__submit" disabled={!forceCreateReason.trim()} onClick={this.handleForceCreate}>
                Vẫn tạo mới
              </button>
            </div>
          </div>
        </div>
      );
    }

    return (
      <div className="c-confirm-dialog" role="dialog" aria-modal="true" aria-label="Tạo hồ sơ mới">
        <div className="c-confirm-dialog__backdrop" onClick={onClose} role="presentation" />
        <form className="c-confirm-dialog__box" onSubmit={this.handleSubmit}>
          <h2 className="c-confirm-dialog__title">Tạo hồ sơ bệnh nhân mới</h2>

          <div className="c-login__field">
            {/* eslint-disable-next-line jsx-a11y/label-has-associated-control */}
            <label className="c-login__label" htmlFor="np-name">Họ tên</label>
            <input id="np-name" className="c-login__input" required value={fullName} onChange={e => this.setState({ fullName: e.target.value })} />
          </div>

          <fieldset className="c-login__field">
            <legend className="c-login__label">Giới tính</legend>
            <label className="c-checkbox" htmlFor="np-gender-male">
              <input id="np-gender-male" type="radio" checked={gender === 'male'} onChange={() => this.setState({ gender: 'male' })} />
              {' Nam'}
            </label>
            <label className="c-checkbox" htmlFor="np-gender-female">
              <input id="np-gender-female" type="radio" checked={gender === 'female'} onChange={() => this.setState({ gender: 'female' })} />
              {' Nữ'}
            </label>
          </fieldset>

          <fieldset className="c-login__field">
            <legend className="c-login__label">Ngày sinh</legend>
            {BIRTH_MODES.map(mode => (
              <label key={mode.value} className="c-checkbox" htmlFor={`np-birth-${mode.value}`}>
                <input
                  id={`np-birth-${mode.value}`}
                  type="radio"
                  checked={birthMode === mode.value}
                  onChange={() => this.setState({ birthMode: mode.value })}
                />
                {mode.label}
              </label>
            ))}
            {birthMode === 'full' && (
              <input type="date" className="c-login__input" value={birthDate} required onChange={e => this.setState({ birthDate: e.target.value })} />
            )}
            {birthMode === 'year' && (
              <input type="number" className="c-login__input" placeholder="Năm sinh" value={birthYear} required onChange={e => this.setState({ birthYear: e.target.value })} />
            )}
          </fieldset>

          <div className="c-login__field">
            {/* eslint-disable-next-line jsx-a11y/label-has-associated-control */}
            <label className="c-login__label" htmlFor="np-address">Địa chỉ</label>
            <input id="np-address" className="c-login__input" value={address} onChange={e => this.setState({ address: e.target.value })} />
          </div>
          <div className="c-login__field">
            {/* eslint-disable-next-line jsx-a11y/label-has-associated-control */}
            <label className="c-login__label" htmlFor="np-phone">Điện thoại</label>
            <input id="np-phone" className="c-login__input" value={phone} onChange={e => this.setState({ phone: e.target.value })} />
          </div>
          <div className="c-login__field">
            {/* eslint-disable-next-line jsx-a11y/label-has-associated-control */}
            <label className="c-login__label" htmlFor="np-doc">Số giấy tờ tùy thân (tùy chọn)</label>
            <input id="np-doc" className="c-login__input" value={documentNumber} onChange={e => this.setState({ documentNumber: e.target.value })} />
          </div>

          <div className="c-confirm-dialog__actions">
            <button type="button" className="c-login__secondary" onClick={onClose} disabled={checking}>Hủy</button>
            <button type="submit" className="c-login__submit" disabled={checking}>{checking ? 'Đang kiểm tra…' : 'Tiếp tục'}</button>
          </div>
        </form>
      </div>
    );
  }
}

CreatePatientDialog.propTypes = {
  onCheckDuplicates: PropTypes.func.isRequired,
  onCreate: PropTypes.func.isRequired,
  onDone: PropTypes.func.isRequired,
  onClose: PropTypes.func.isRequired,
};

export default CreatePatientDialog;
