import { useEffect, useRef, useState } from 'react';
import { Navigate, useSearchParams, Link } from 'react-router-dom';
import { useAuth } from '../auth/AuthContext';
import { ApiError } from '../api/client';

export function MagicLinkVerifyPage() {
  const [searchParams] = useSearchParams();
  const { verifyMagicLink, isAuthenticated, isLoading } = useAuth();
  const [error, setError] = useState('');
  const [isVerifying, setIsVerifying] = useState(true);
  const hasStarted = useRef(false);

  const token = searchParams.get('token');
  const email = searchParams.get('email');

  useEffect(() => {
    if (hasStarted.current) {
      return;
    }
    hasStarted.current = true;

    if (!token || !email) {
      setError('Neplatný odkaz. Chybí token nebo email.');
      setIsVerifying(false);
      return;
    }

    void (async () => {
      try {
        await verifyMagicLink(token, email);
      } catch (err) {
        if (err instanceof ApiError) {
          setError(err.message);
        } else {
          setError('Ověření se nezdařilo. Odkaz mohl vypršet.');
        }
      } finally {
        setIsVerifying(false);
      }
    })();
  }, [token, email, verifyMagicLink]);

  if (isAuthenticated && !isLoading) {
    return <Navigate to="/dashboard" replace />;
  }

  return (
    <div className="flex min-h-screen items-center justify-center bg-gradient-to-br from-blue-50 to-gray-100 px-4">
      <div className="w-full max-w-md rounded-xl bg-white p-8 text-center shadow-lg">
        <h1 className="text-xl font-bold text-gray-900">
          Ověření přihlášení
        </h1>

        {isVerifying && (
          <div className="mt-6 flex flex-col items-center gap-3">
            <div className="h-8 w-8 animate-spin rounded-full border-4 border-blue-600 border-t-transparent" />
            <p className="text-sm text-gray-600">Ověřuji přihlašovací odkaz...</p>
          </div>
        )}

        {error && (
          <div className="mt-6 space-y-4">
            <div className="rounded-lg bg-red-50 p-3 text-sm text-red-700">
              {error}
            </div>
            <Link
              to="/login"
              className="inline-block text-sm text-blue-600 underline hover:text-blue-800"
            >
              Zpět na přihlášení
            </Link>
          </div>
        )}
      </div>
    </div>
  );
}
