import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import './index.css';
import App from './App.tsx';
import { msalInstance } from './auth/msalConfig';

// Initialize MSAL before rendering
msalInstance.initialize()
  .then(() => msalInstance.handleRedirectPromise().catch(() => null))
  .then(() => {
    createRoot(document.getElementById('root')!).render(
      <StrictMode>
        <App />
      </StrictMode>,
    );
  })
  .catch(() => {
    // MSAL init failed — render app anyway, auth will be unavailable
    createRoot(document.getElementById('root')!).render(
      <StrictMode>
        <App />
      </StrictMode>,
    );
  });
