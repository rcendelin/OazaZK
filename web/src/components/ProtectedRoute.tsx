import type { ReactNode } from 'react';
import { Navigate } from 'react-router-dom';
import { useAuth } from '../auth/AuthContext';
import { Spinner } from './Spinner';
import { ShieldAlert } from 'lucide-react';
import type { UserRole } from '../types';

interface ProtectedRouteProps {
  children: ReactNode;
  requiredRole?: UserRole;
}

export function ProtectedRoute({ children, requiredRole }: ProtectedRouteProps) {
  const { isAuthenticated, isLoading, user } = useAuth();

  if (isLoading) {
    return (
      <div className="flex min-h-screen items-center justify-center bg-surface">
        <div className="flex flex-col items-center gap-4">
          <Spinner size="lg" />
          <p className="text-sm text-text-muted">Ověřuji přihlášení...</p>
        </div>
      </div>
    );
  }

  if (!isAuthenticated) {
    return <Navigate to="/login" replace />;
  }

  if (requiredRole && user?.role !== requiredRole && user?.role !== 'Admin') {
    return (
      <div className="flex min-h-screen items-center justify-center bg-surface">
        <div className="mx-4 max-w-sm rounded-2xl bg-surface-raised p-8 text-center shadow-card">
          <div className="mx-auto flex h-14 w-14 items-center justify-center rounded-2xl bg-danger-light text-danger">
            <ShieldAlert size={28} />
          </div>
          <h2 className="mt-4 text-lg font-semibold text-text-primary">
            Přístup odepřen
          </h2>
          <p className="mt-2 text-sm text-text-secondary">
            Pro zobrazení této stránky nemáte dostatečná oprávnění.
          </p>
          <a
            href="/dashboard"
            className="mt-4 inline-block text-sm font-medium text-accent hover:text-accent-hover"
          >
            Zpět na přehled
          </a>
        </div>
      </div>
    );
  }

  return <>{children}</>;
}
