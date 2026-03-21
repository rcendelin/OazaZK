import { BrowserRouter, Routes, Route, Navigate } from 'react-router-dom';

function App() {
  return (
    <BrowserRouter>
      <Routes>
        <Route path="/login" element={<div>Login Page</div>} />
        <Route path="/dashboard" element={<div>Dashboard</div>} />
        <Route path="/readings" element={<div>Readings Overview</div>} />
        <Route path="/readings/import" element={<div>Import Readings</div>} />
        <Route path="/billing" element={<div>Billing</div>} />
        <Route path="/documents" element={<div>Documents</div>} />
        <Route path="/finance" element={<div>Finance</div>} />
        <Route path="/admin/houses" element={<div>Houses Admin</div>} />
        <Route path="/admin/users" element={<div>Users Admin</div>} />
        <Route path="/" element={<Navigate to="/dashboard" replace />} />
      </Routes>
    </BrowserRouter>
  );
}

export default App;
