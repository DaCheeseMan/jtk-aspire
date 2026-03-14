import { useAuth } from 'react-oidc-context';
import { Link, useLocation } from 'react-router-dom';
import './Navbar.css';

export function Navbar() {
  const auth = useAuth();
  const location = useLocation();

  const isActive = (path: string) => location.pathname === path ? 'active' : '';

  return (
    <nav className="navbar">
      <div className="navbar-brand">
        <Link to="/">🎾 Jonsereds TK</Link>
      </div>
      <div className="navbar-links">
        <Link to="/" className={isActive('/')}>Hem</Link>
        {auth.isAuthenticated && (
          <>
            <Link to="/courts" className={isActive('/courts')}>Banor</Link>
            <Link to="/my-bookings" className={isActive('/my-bookings')}>Mina Bokningar</Link>
            <Link to="/profile" className={isActive('/profile')}>Min Profil</Link>
          </>
        )}
      </div>
      <div className="navbar-auth">
        {auth.isAuthenticated ? (
          <div className="user-info">
            <span className="user-name">
              {auth.user?.profile.preferred_username ?? auth.user?.profile.name}
            </span>
            <button className="btn btn-outline" onClick={() => auth.signoutRedirect()}>
              Logga ut
            </button>
          </div>
        ) : (
          <button className="btn btn-primary" onClick={() => auth.signinRedirect()}>
            Logga in
          </button>
        )}
      </div>
    </nav>
  );
}
