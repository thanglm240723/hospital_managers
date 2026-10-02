import { connect } from 'react-redux';
import { loadMe, logout } from 'feature/Auth/redux/actions';
import { getAvailableWorkspaces } from './routeAccess';
import { FEATURE_AVAILABILITY } from './availability';
import NoAccess from './component/NoAccess';

const mapStateToProps = state => ({
  hasWorkspaces: getAvailableWorkspaces(state.auth.permissions, FEATURE_AVAILABILITY).length > 0,
});

export default connect(mapStateToProps, { loadMe, logout })(NoAccess);
