import React from 'react';
import PropTypes from 'prop-types';
import { connect } from 'react-redux';
import { AppLayout } from 'feature/Shell';
import {
  searchPatients, checkDuplicates, createPatient,
} from './redux/action';
import SearchForm from './component/SearchForm';
import ResultsTable from './component/ResultsTable';
import CreatePatientDialog from './component/CreatePatientDialog';

class SearchContainer extends React.Component {
  state = { creating: false };

  render() {
    const {
      results, searchPatients: onSearch, checkDuplicates: onCheckDuplicates, createPatient: onCreate, history,
    } = this.props;
    const { creating } = this.state;

    return (
      <AppLayout>
        <div className="c-page">
          <h1 className="c-page__title">Tìm hồ sơ bệnh nhân</h1>
          <SearchForm onSearch={onSearch} onCreateNew={() => this.setState({ creating: true })} />
          <ResultsTable items={results} onIntake={id => history.push(`/reception/intake/${id}`)} />
          {creating && (
            <CreatePatientDialog
              onCheckDuplicates={onCheckDuplicates}
              onCreate={onCreate}
              onDone={(id) => { this.setState({ creating: false }); history.push(`/reception/intake/${id}`); }}
              onClose={() => this.setState({ creating: false })}
            />
          )}
        </div>
      </AppLayout>
    );
  }
}

SearchContainer.propTypes = {
  results: PropTypes.arrayOf(PropTypes.object).isRequired,
  searchPatients: PropTypes.func.isRequired,
  checkDuplicates: PropTypes.func.isRequired,
  createPatient: PropTypes.func.isRequired,
  history: PropTypes.shape({ push: PropTypes.func }).isRequired,
};

const mapStateToProps = state => ({ results: state.reception.results });

export default connect(mapStateToProps, { searchPatients, checkDuplicates, createPatient })(SearchContainer);
