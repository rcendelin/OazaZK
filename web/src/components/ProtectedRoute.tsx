import { Navigate } from 'react-router-dom';
import { useAuth } from '../auth/AuthContext';
import type { UserRole } from '../types';
import type { ReactNode } from 'react';

interface ProtectedRouteProps {
  children: ReactNode;
  requiredRole?: UserRole;
}

export function ProtectedRoute({ children, requiredRole }: ProtectedRouteProps) {
  const { isAuthenticated, isLoading, user } = useAuth();

  if (isLoading) {
    return (
      <div className="flex h-screen items-center justify-center">
        <div className="h-8 w-8 animate-spin rounded-full border-4 border-blue-600 border-t-transparent" />
      </div>
    );
  }

  if (!isAuthenticated) {
    return <Navigate to="/login" replace />;
  }

  if (requiredRole && user?.role !== requiredRole && user?.role !== 'Admin') {
    return (
      <div className="flex h-screen flex-col items-center justify-center gap-4">
        <h1 className="text-2xl font-bold text-gray-800">403</h1>
        <p className="text-gray-600">Nemáte oprávnění k zobrazení této stránky.</p>
        <a href="/dashboard" className="text-blue-600 underline hover:text-blue-800">
          Zpět na přehled
        </a>
      </div>
    );
  }

  return <>{children}</>;
}
