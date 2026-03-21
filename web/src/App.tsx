import { BrowserRouter, Routes, Route, Navigate } from 'react-router-dom';
import { MsalProvider } from '@azure/msal-react';
import { msalInstance } from './auth/msalConfig';
import { AuthProvider } from './auth/AuthContext';
import { ProtectedRoute } from './components/ProtectedRoute';
import { Layout } from './components/Layout';
import { LoginPage } from './pages/LoginPage';
import { MagicLinkVerifyPage } from './pages/MagicLinkVerifyPage';
import { DashboardPage } from './pages/DashboardPage';
import { ReadingsOverviewPage } from './pages/ReadingsOverviewPage';
import { ReadingsImportPage } from './pages/ReadingsImportPage';
import { BillingPage } from './pages/BillingPage';
import { DocumentsPage } from './pages/DocumentsPage';
import { FinancePage } from './pages/FinancePage';
import { HousesPage } from './pages/admin/HousesPage';
import { UsersPage } from './pages/admin/UsersPage';

function App() {
  return (
    <MsalProvider instance={msalInstance}>
      <AuthProvider>
        <BrowserRouter>
          <Routes>
            <Route path="/login" element={<LoginPage />} />
            <Route path="/auth/verify" element={<MagicLinkVerifyPage />} />
            <Route
              element={
                <ProtectedRoute>
                  <Layout />
                </ProtectedRoute>
              }
            >
              <Route path="/dashboard" element={<DashboardPage />} />
              <Route path="/readings" element={<ReadingsOverviewPage />} />
              <Route
                path="/readings/import"
                element={
                  <ProtectedRoute requiredRole="Admin">
                    <ReadingsImportPage />
                  </ProtectedRoute>
                }
              />
              <Route path="/billing" element={<BillingPage />} />
              <Route path="/documents" element={<DocumentsPage />} />
              <Route path="/finance" element={<FinancePage />} />
              <Route
                path="/admin/houses"
                element={
                  <ProtectedRoute requiredRole="Admin">
                    <HousesPage />
                  </ProtectedRoute>
                }
              />
              <Route
                path="/admin/users"
                element={
                  <ProtectedRoute requiredRole="Admin">
                    <UsersPage />
                  </ProtectedRoute>
                }
              />
            </Route>
            <Route path="/" element={<Navigate to="/dashboard" replace />} />
          </Routes>
        </BrowserRouter>
      </AuthProvider>
    </MsalProvider>
  );
}

export default App;
