import {
  createContext,
  useCallback,
  useContext,
  useEffect,
  useMemo,
  useState,
  type ReactNode,
} from 'react';
import { useMsal, useIsAuthenticated } from '@azure/msal-react';
import { InteractionRequiredAuthError, InteractionStatus } from '@azure/msal-browser';
import { apiClient } from '../api/client';
import { loginRequest } from './msalConfig';
import type { User } from '../types';

interface AuthContextType {
  user: User | null;
  isAuthenticated: boolean;
  isLoading: boolean;
  login: () => Promise<void>;
  loginWithMagicLink: (email: string) => Promise<void>;
  verifyMagicLink: (token: string, email: string) => Promise<void>;
  logout: () => void;
  getAccessToken: () => Promise<string | null>;
}

const AuthContext = createContext<AuthContextType | null>(null);

interface AuthProviderProps {
  children: ReactNode;
}

export function AuthProvider({ children }: AuthProviderProps) {
  const { instance, inProgress } = useMsal();
  const isMsalAuthenticated = useIsAuthenticated();

  const [user, setUser] = useState<User | null>(null);
  const [magicLinkJwt, setMagicLinkJwt] = useState<string | null>(null);
  const [isLoading, setIsLoading] = useState(true);

  const isAuthenticated = isMsalAuthenticated || magicLinkJwt !== null;

  const getAccessToken = useCallback(async (): Promise<string | null> => {
    if (magicLinkJwt) {
      return magicLinkJwt;
    }

    const accounts = instance.getAllAccounts();
    if (accounts.length === 0) {
      return null;
    }

    try {
      const response = await instance.acquireTokenSilent({
        scopes: loginRequest.scopes,
        account: accounts[0],
      });
      // Use idToken — our backend validates against Entra ID OIDC with
      // audience = clientId, which matches the id_token audience
      return response.idToken;
    } catch (err) {
      if (err instanceof InteractionRequiredAuthError) {
        try {
          await instance.acquireTokenRedirect(loginRequest);
        } catch {
          // Redirect will reload the page
        }
        return null;
      }
      return null;
    }
  }, [instance, magicLinkJwt]);

  // Wire up the API client token provider
  useEffect(() => {
    apiClient.setTokenProvider(getAccessToken);
  }, [getAccessToken]);

  // Fetch user profile when authenticated
  const fetchUserProfile = useCallback(async () => {
    try {
      const profile = await apiClient.get<User>('/auth/me');
      setUser(profile);
    } catch {
      setUser(null);
      setMagicLinkJwt(null);
    }
  }, []);

  useEffect(() => {
    if (inProgress !== InteractionStatus.None) {
      return;
    }

    if (isAuthenticated) {
      void fetchUserProfile().finally(() => setIsLoading(false));
    } else {
      setUser(null);
      setIsLoading(false);
    }
  }, [isAuthenticated, inProgress, fetchUserProfile]);

  const login = useCallback(async () => {
    await instance.loginRedirect(loginRequest);
  }, [instance]);

  const loginWithMagicLink = useCallback(async (email: string) => {
    await apiClient.post('/auth/magic-link', { email });
  }, []);

  const verifyMagicLink = useCallback(
    async (token: string, email: string) => {
      const response = await apiClient.post<{ token: string }>(
        '/auth/magic-link/verify',
        { token, email },
      );
      setMagicLinkJwt(response.token);
    },
    [],
  );

  const logout = useCallback(() => {
    setMagicLinkJwt(null);
    setUser(null);

    const accounts = instance.getAllAccounts();
    if (accounts.length > 0) {
      void instance.logoutRedirect();
    }
  }, [instance]);

  const value = useMemo<AuthContextType>(
    () => ({
      user,
      isAuthenticated,
      isLoading,
      login,
      loginWithMagicLink,
      verifyMagicLink,
      logout,
      getAccessToken,
    }),
    [
      user,
      isAuthenticated,
      isLoading,
      login,
      loginWithMagicLink,
      verifyMagicLink,
      logout,
      getAccessToken,
    ],
  );

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}

export function useAuth(): AuthContextType {
  const context = useContext(AuthContext);
  if (!context) {
    throw new Error('useAuth must be used within an AuthProvider');
  }
  return context;
}
