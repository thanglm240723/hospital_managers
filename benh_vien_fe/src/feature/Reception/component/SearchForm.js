import React from 'react';
import PropTypes from 'prop-types';

class SearchForm extends React.Component {
  state = {
    documentNumber: '', fullName: '', birthDate: '', phone: '',
  };

  handleSubmit = (event) => {
    event.preventDefault();
    const { onSearch } = this.props;
    onSearch(this.state);
  };

  render() {
    const {
      documentNumber, fullName, birthDate, phone,
    } = this.state;
    const { onCreateNew } = this.props;
    return (
      <form className="c-toolbar" onSubmit={this.handleSubmit}>
        <input
          className="c-toolbar__search"
          placeholder="Mã hồ sơ / số giấy tờ"
          value={documentNumber}
          onChange={e => this.setState({ documentNumber: e.target.value })}
          aria-label="Mã hồ sơ hoặc số giấy tờ"
        />
        <input
          className="c-toolbar__search"
          placeholder="Họ tên"
          value={fullName}
          onChange={e => this.setState({ fullName: e.target.value })}
          aria-label="Họ tên"
        />
        <input
          className="c-toolbar__select"
          type="date"
          value={birthDate}
          onChange={e => this.setState({ birthDate: e.target.value })}
          aria-label="Ngày sinh"
        />
        <input
          className="c-toolbar__search"
          placeholder="Điện thoại"
          value={phone}
          onChange={e => this.setState({ phone: e.target.value })}
          aria-label="Điện thoại"
        />
        <button type="submit" className="c-toolbar__action" style={{ marginLeft: 0 }}>Tìm</button>
        <button type="button" className="c-login__secondary" onClick={onCreateNew}>+ Tạo hồ sơ mới</button>
      </form>
    );
  }
}

SearchForm.propTypes = {
  onSearch: PropTypes.func.isRequired,
  onCreateNew: PropTypes.func.isRequired,
};

export default SearchForm;
