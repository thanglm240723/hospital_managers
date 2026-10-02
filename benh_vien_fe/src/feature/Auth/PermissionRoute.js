import React from 'react';
import PropTypes from 'prop-types';
import { connect } from 'react-redux';
import { Redirect } from 'react-router-dom';
import { AppLayout } from 'feature/Shell';
import { selectWorkspace as selectWorkspaceAction } from 'feature/Workspace/redux/action';
import { findRoute, routeStatus, isWorkspaceAllowed } from 'feature/Workspace/routeAccess';
import { FEATURE_AVAILABILITY } from 'feature/Workspace/availability';
import FeaturePending from 'feature/Workspace/component/FeaturePending';
import PrivateRoute from './PrivateRoute';

// Quyết định dùng chung registry với sidebar/default route. Chỉ để điều hướng UI — API vẫn là nơi quyết định quyền.
export function decideRoute({
  path, url, permissions, selectedId, availability,
}) {
  const route = findRoute(path);
  if (!route) return { kind: 'render' };
  const status = routeStatus(url, permissions, availability);
  if (status === 'forbidden') return { kind: 'forbidden' };
  if (!selectedId || !isWorkspaceAllowed(selectedId, permissions, availability)) return { kind: 'pick' };
  if (selectedId !== route.workspaceId) return { kind: 'switch', workspaceId: route.workspaceId };
  return { kind: status === 'ok' ? 'render' : 'unavailable' };
}

class RouteGate extends React.Component {
  componentDidMount() {
    this.sync();
  }

  componentDidUpdate() {
    this.sync();
  }

  decision() {
    const {
      match, permissions, selectedId,
    } = this.props;
    return decideRoute({
      path: match.path, url: match.url, permissions, selectedId, availability: FEATURE_AVAILABILITY,
    });
  }

  // Người dùng đã chọn khu vực trong lần login này và mở URL của khu vực khác mà họ có quyền: chuyển khu vực
  // (như dùng dropdown). Dispatch ở lifecycle, không trong render.
  sync() {
    const decision = this.decision();
    const { selectWorkspace } = this.props;
    if (decision.kind === 'switch') selectWorkspace(decision.workspaceId);
  }

  render() {
    const { component: Component, permissions, selectedId, selectWorkspace, ...routeProps } = this.props;
    const decision = this.decision();
    switch (decision.kind) {
      case 'forbidden':
        // Không mount màn (không gọi API); /start tính lại khu vực/màn mặc định.
        return <Redirect to="/start" />;
      case 'pick':
        return <Redirect to={{ pathname: '/start', state: { from: routeProps.location } }} />;
      case 'switch':
        return null;
      case 'unavailable':
        return (
          <AppLayout>
            <FeaturePending />
          </AppLayout>
        );
      default:
        return <Component {...routeProps} />;
    }
  }
}

RouteGate.propTypes = {
  component: PropTypes.elementType.isRequired,
  match: PropTypes.shape({ path: PropTypes.string, url: PropTypes.string }).isRequired,
  location: PropTypes.shape({ pathname: PropTypes.string }).isRequired,
  permissions: PropTypes.arrayOf(PropTypes.string),
  selectedId: PropTypes.string,
  selectWorkspace: PropTypes.func.isRequired,
};

RouteGate.defaultProps = { permissions: [], selectedId: null };

const ConnectedGate = connect(
  state => ({ permissions: state.auth.permissions, selectedId: state.workspace.selectedId }),
  { selectWorkspace: selectWorkspaceAction },
)(RouteGate);

// Route cần đăng nhập + đổi mật khẩu (PrivateRoute) + quyền/khu vực/availability theo registry.
const PermissionRoute = ({ component, ...rest }) => <PrivateRoute {...rest} render={props => <ConnectedGate {...props} component={component} />} />;

PermissionRoute.propTypes = { component: PropTypes.elementType.isRequired };

export default PermissionRoute;
