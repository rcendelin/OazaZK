import { useState, type FormEvent } from 'react';
import { Navigate } from 'react-router-dom';
import { useAuth } from '../auth/AuthContext';
import { ApiError } from '../api/client';

export function LoginPage() {
  const { isAuthenticated, isLoading, login, loginWithMagicLink } = useAuth();
  const [email, setEmail] = useState('');
  const [isSending, setIsSending] = useState(false);
  const [isMsalLoading, setIsMsalLoading] = useState(false);
  const [successMessage, setSuccessMessage] = useState('');
  const [errorMessage, setErrorMessage] = useState('');

  if (isLoading) {
    return (
      <div className="flex min-h-screen items-center justify-center bg-gradient-to-br from-blue-50 to-gray-100">
        <div className="h-8 w-8 animate-spin rounded-full border-4 border-blue-600 border-t-transparent" />
      </div>
    );
  }

  if (isAuthenticated) {
    return <Navigate to="/dashboard" replace />;
  }

  const handleMsalLogin = async () => {
    setErrorMessage('');
    setIsMsalLoading(true);
    try {
      await login();
    } catch {
      setErrorMessage('Přihlášení přes Microsoft se nezdařilo.');
      setIsMsalLoading(false);
    }
  };

  const handleMagicLink = async (e: FormEvent) => {
    e.preventDefault();
    setErrorMessage('');
    setSuccessMessage('');

    if (!email.trim()) {
      setErrorMessage('Zadejte emailovou adresu.');
      return;
    }

    setIsSending(true);
    try {
      await loginWithMagicLink(email.trim());
      setSuccessMessage('Odkaz byl odeslán na váš email.');
      setEmail('');
    } catch (err) {
      if (err instanceof ApiError) {
        setErrorMessage(err.message);
      } else {
        setErrorMessage('Odeslání odkazu se nezdařilo. Zkuste to prosím znovu.');
      }
    } finally {
      setIsSending(false);
    }
  };

  return (
    <div className="flex min-h-screen items-center justify-center bg-gradient-to-br from-blue-50 to-gray-100 px-4">
      <div className="w-full max-w-md rounded-xl bg-white p-8 shadow-lg">
        {/* Header */}
        <div className="mb-8 text-center">
          <h1 className="text-2xl font-bold text-gray-900">
            Oáza Zadní Kopanina
          </h1>
          <p className="mt-1 text-sm text-gray-500">
            Přihlášení do portálu
          </p>
        </div>

        {/* Microsoft login */}
        <button
          onClick={handleMsalLogin}
          disabled={isMsalLoading}
          className="flex w-full items-center justify-center gap-2 rounded-lg bg-blue-600 px-4 py-3 text-sm font-medium text-white transition-colors hover:bg-blue-700 focus:outline-none focus:ring-2 focus:ring-blue-500 focus:ring-offset-2 disabled:cursor-not-allowed disabled:opacity-60"
        >
          {isMsalLoading ? (
            <div className="h-5 w-5 animate-spin rounded-full border-2 border-white border-t-transparent" />
          ) : (
            <svg className="h-5 w-5" viewBox="0 0 21 21" fill="none">
              <rect x="1" y="1" width="9" height="9" fill="#f25022" />
              <rect x="11" y="1" width="9" height="9" fill="#7fba00" />
              <rect x="1" y="11" width="9" height="9" fill="#00a4ef" />
              <rect x="11" y="11" width="9" height="9" fill="#ffb900" />
            </svg>
          )}
          Přihlásit přes Microsoft
        </button>

        {/* Divider */}
        <div className="my-6 flex items-center gap-3">
          <div className="h-px flex-1 bg-gray-200" />
          <span className="text-sm text-gray-400">nebo</span>
          <div className="h-px flex-1 bg-gray-200" />
        </div>

        {/* Magic link form */}
        <form onSubmit={handleMagicLink} className="space-y-4">
          <div>
            <label
              htmlFor="email"
              className="block text-sm font-medium text-gray-700"
            >
              Email
            </label>
            <input
              id="email"
              type="email"
              value={email}
              onChange={(e) => setEmail(e.target.value)}
              placeholder="vas@email.cz"
              autoComplete="email"
              className="mt-1 block w-full rounded-lg border border-gray-300 px-3 py-2.5 text-sm shadow-sm transition-colors placeholder:text-gray-400 focus:border-blue-500 focus:outline-none focus:ring-2 focus:ring-blue-500/20"
            />
          </div>
          <button
            type="submit"
            disabled={isSending}
            className="w-full rounded-lg border border-blue-600 bg-white px-4 py-2.5 text-sm font-medium text-blue-600 transition-colors hover:bg-blue-50 focus:outline-none focus:ring-2 focus:ring-blue-500 focus:ring-offset-2 disabled:cursor-not-allowed disabled:opacity-60"
          >
            {isSending ? 'Odesílám...' : 'Poslat přihlašovací odkaz'}
          </button>
        </form>

        {/* Messages */}
        {successMessage && (
          <div className="mt-4 rounded-lg bg-green-50 p-3 text-sm text-green-700">
            {successMessage}
          </div>
        )}
        {errorMessage && (
          <div className="mt-4 rounded-lg bg-red-50 p-3 text-sm text-red-700">
            {errorMessage}
          </div>
        )}
      </div>
    </div>
  );
}
