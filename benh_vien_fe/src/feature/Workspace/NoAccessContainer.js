import { connect } from 'react-redux';
import { loadMe, logout } from 'feature/Auth/redux/actions';
import NoAccess from './component/NoAccess';

export default connect(null, { loadMe, logout })(NoAccess);
