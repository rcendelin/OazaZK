import { useState, type FormEvent } from 'react';
import { Navigate } from 'react-router-dom';
import { useAuth } from '../auth/AuthContext';
import { ApiError } from '../api/client';
import { Droplets, Mail, ArrowRight } from 'lucide-react';
import { Spinner } from '../components/Spinner';

export function LoginPage() {
  const { isAuthenticated, isLoading, login, loginWithMagicLink } = useAuth();
  const [email, setEmail] = useState('');
  const [isSending, setIsSending] = useState(false);
  const [isMsalLoading, setIsMsalLoading] = useState(false);
  const [successMessage, setSuccessMessage] = useState('');
  const [errorMessage, setErrorMessage] = useState('');

  if (isLoading) {
    return (
      <div className="flex min-h-screen items-center justify-center bg-sidebar">
        <Spinner size="lg" />
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
    <div className="flex min-h-screen">
      {/* Left panel — branding */}
      <div className="hidden w-1/2 flex-col justify-between bg-sidebar p-12 lg:flex">
        <div>
          <div className="flex items-center gap-3">
            <div className="flex h-11 w-11 items-center justify-center rounded-xl bg-accent text-white">
              <Droplets size={24} />
            </div>
            <span className="text-xl font-bold text-white tracking-tight">Oáza ZK</span>
          </div>
        </div>
        <div>
          <h2 className="text-4xl font-bold leading-tight text-white">
            Komunitní portál<br />
            <span className="text-accent">Zadní Kopanina</span>
          </h2>
          <p className="mt-4 max-w-md text-lg text-sidebar-text leading-relaxed">
            Správa společného vodovodu, odečty vodoměrů, vyúčtování a sdílené dokumenty — vše na jednom místě.
          </p>
        </div>
        <p className="text-sm text-sidebar-text/50">
          8 domácností &middot; 1 společenství
        </p>
      </div>

      {/* Right panel — login form */}
      <div className="flex flex-1 items-center justify-center bg-surface px-4">
        <div className="w-full max-w-md">
          {/* Mobile logo */}
          <div className="mb-10 flex items-center gap-3 lg:hidden">
            <div className="flex h-10 w-10 items-center justify-center rounded-xl bg-accent text-white">
              <Droplets size={22} />
            </div>
            <span className="text-lg font-bold text-text-primary">Oáza Zadní Kopanina</span>
          </div>

          <h1 className="text-2xl font-bold text-text-primary">Přihlášení</h1>
          <p className="mt-2 text-sm text-text-secondary">
            Vyberte způsob přihlášení do portálu
          </p>

          {/* Microsoft login */}
          <button
            onClick={handleMsalLogin}
            disabled={isMsalLoading}
            className="mt-8 flex w-full items-center justify-center gap-3 rounded-xl bg-sidebar px-4 py-3.5 text-sm font-medium text-white transition-all hover:bg-sidebar-hover active:scale-[0.99] disabled:cursor-not-allowed disabled:opacity-60"
          >
            {isMsalLoading ? (
              <Spinner size="sm" />
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
          <div className="my-8 flex items-center gap-4">
            <div className="h-px flex-1 bg-border" />
            <span className="text-xs font-medium text-text-muted uppercase tracking-wider">nebo</span>
            <div className="h-px flex-1 bg-border" />
          </div>

          {/* Magic link form */}
          <form onSubmit={handleMagicLink} className="space-y-4">
            <div>
              <label
                htmlFor="email"
                className="mb-1.5 block text-sm font-medium text-text-primary"
              >
                Email
              </label>
              <div className="relative">
                <Mail size={18} className="absolute left-3.5 top-1/2 -translate-y-1/2 text-text-muted" />
                <input
                  id="email"
                  type="email"
                  value={email}
                  onChange={(e) => setEmail(e.target.value)}
                  placeholder="vas@email.cz"
                  autoComplete="email"
                  className="block w-full rounded-xl border border-border bg-surface-raised pl-11 pr-4 py-3 text-sm transition-all placeholder:text-text-muted focus:border-accent focus:outline-none focus:ring-2 focus:ring-accent/20"
                />
              </div>
            </div>
            <button
              type="submit"
              disabled={isSending}
              className="flex w-full items-center justify-center gap-2 rounded-xl bg-accent px-4 py-3 text-sm font-medium text-white transition-all hover:bg-accent-hover active:scale-[0.99] disabled:cursor-not-allowed disabled:opacity-60"
            >
              {isSending ? (
                <>
                  <Spinner size="sm" />
                  Odesílám...
                </>
              ) : (
                <>
                  Poslat přihlašovací odkaz
                  <ArrowRight size={16} />
                </>
              )}
            </button>
          </form>

          {/* Messages */}
          {successMessage && (
            <div className="mt-6 rounded-xl bg-success-light p-4">
              <p className="text-sm font-medium text-success">{successMessage}</p>
            </div>
          )}
          {errorMessage && (
            <div className="mt-6 rounded-xl bg-danger-light p-4">
              <p className="text-sm font-medium text-danger">{errorMessage}</p>
            </div>
          )}
        </div>
      </div>
    </div>
  );
}
