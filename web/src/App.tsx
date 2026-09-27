import { BrowserRouter, Routes, Route, Navigate } from 'react-router-dom';
import { MsalProvider } from '@azure/msal-react';
import { msalInstance } from './auth/msalConfig';
import { AuthProvider } from './auth/AuthContext';
import { ProtectedRoute } from './components/ProtectedRoute';
import { Layout } from './components/Layout';
import { EnvironmentBanner } from './components/EnvironmentBanner';
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
import { MetersPage } from './pages/admin/MetersPage';
import { AuditLogPage } from './pages/admin/AuditLogPage';
import { CostComponentsPage } from './pages/admin/CostComponentsPage';
import { OpeningBalancesPage } from './pages/admin/OpeningBalancesPage';
import { CostsPage } from './pages/CostsPage';
import { WaterPage } from './pages/WaterPage';
import { LedgerPage } from './pages/LedgerPage';
import { InterimClosingsPage } from './pages/InterimClosingsPage';
import { CashBookPage } from './pages/CashBookPage';
import { ReadingsListPage } from './pages/ReadingsListPage';
import { AdvancesPage } from './pages/AdvancesPage';
import { BankImportPage } from './pages/BankImportPage';
import { SaldoPage } from './pages/SaldoPage';
import { InvoicesOverviewPage } from './pages/InvoicesOverviewPage';
import { JakToFungujePage } from './pages/JakToFungujePage';

function App() {
  return (
    <MsalProvider instance={msalInstance}>
      <AuthProvider>
        <EnvironmentBanner />
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
                path="/readings/list"
                element={
                  <ProtectedRoute requiredRole="Admin">
                    <ReadingsListPage />
                  </ProtectedRoute>
                }
              />
              <Route
                path="/readings/import"
                element={
                  <ProtectedRoute requiredRole="Admin">
                    <ReadingsImportPage />
                  </ProtectedRoute>
                }
              />
              <Route path="/advances" element={<AdvancesPage />} />
              <Route
                path="/advances/import"
                element={
                  <ProtectedRoute requiredRole="Admin">
                    <BankImportPage />
                  </ProtectedRoute>
                }
              />
              <Route path="/saldo" element={<SaldoPage />} />
              <Route path="/saldo-domu" element={<LedgerPage />} />
              <Route path="/pokladna" element={<CashBookPage />} />
              <Route path="/billing" element={<BillingPage />} />
              <Route
                path="/mezizaverky"
                element={
                  <ProtectedRoute requiredRole="Accountant">
                    <InterimClosingsPage />
                  </ProtectedRoute>
                }
              />
              <Route
                path="/voda"
                element={
                  <ProtectedRoute requiredRole="Accountant">
                    <WaterPage />
                  </ProtectedRoute>
                }
              />
              <Route
                path="/naklady"
                element={
                  <ProtectedRoute requiredRole="Accountant">
                    <CostsPage />
                  </ProtectedRoute>
                }
              />
              <Route
                path="/prehled-faktur"
                element={
                  <ProtectedRoute requiredRole="Accountant">
                    <InvoicesOverviewPage />
                  </ProtectedRoute>
                }
              />
              <Route path="/documents" element={<DocumentsPage />} />
              <Route path="/finance" element={<FinancePage />} />
              <Route path="/jak-to-funguje" element={<JakToFungujePage />} />
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
              <Route
                path="/admin/cost-components"
                element={
                  <ProtectedRoute requiredRole="Admin">
                    <CostComponentsPage />
                  </ProtectedRoute>
                }
              />
              <Route
                path="/admin/opening-balances"
                element={
                  <ProtectedRoute requiredRole="Admin">
                    <OpeningBalancesPage />
                  </ProtectedRoute>
                }
              />
              <Route
                path="/admin/audit"
                element={
                  <ProtectedRoute requiredRole="Admin">
                    <AuditLogPage />
                  </ProtectedRoute>
                }
              />
              <Route
                path="/admin/meters"
                element={
                  <ProtectedRoute requiredRole="Admin">
                    <MetersPage />
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
