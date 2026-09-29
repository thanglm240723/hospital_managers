import { connect } from 'react-redux';
import { hasPermission } from './permissions';

const Can = ({ allowed, children }) => (allowed ? children : null);

export default connect((state, { permission }) => ({ allowed: hasPermission(state.auth, permission) }))(Can);
