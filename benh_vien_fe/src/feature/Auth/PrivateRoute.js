import React from 'react';
import { connect } from 'react-redux';
import { Route, Redirect } from 'react-router-dom';

const PrivateRoute = ({
  component: Component, render, status, mustChangePassword, ...rest
}) => (
  <Route
    {...rest}
    render={(props) => {
      if (status === 'booting') return null;
      if (status !== 'authenticated') {
        return <Redirect to={{ pathname: '/login', state: { from: props.location } }} />;
      }
      if (mustChangePassword && props.location.pathname !== '/change-password') {
        return <Redirect to="/change-password" />;
      }
      return render ? render(props) : <Component {...props} />;
    }}
  />
);

export default connect(state => ({
  status: state.auth.status,
  mustChangePassword: state.auth.mustChangePassword,
}))(PrivateRoute);
