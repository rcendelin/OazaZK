import { useState } from 'react';
import { NavLink, Outlet } from 'react-router-dom';
import { useAuth } from '../auth/AuthContext';

interface NavItem {
  label: string;
  path: string;
  adminOnly?: boolean;
  phase2?: boolean;
}

const navItems: NavItem[] = [
  { label: 'Přehled', path: '/dashboard' },
  { label: 'Odečty', path: '/readings' },
  { label: 'Import odečtů', path: '/readings/import', adminOnly: true },
  { label: 'Vyúčtování', path: '/billing' },
  { label: 'Dokumenty', path: '/documents', phase2: true },
  { label: 'Hospodaření', path: '/finance', phase2: true },
];

const adminNavItems: NavItem[] = [
  { label: 'Správa domácností', path: '/admin/houses', adminOnly: true },
  { label: 'Správa uživatelů', path: '/admin/users', adminOnly: true },
];

function SidebarContent({ onNavigate }: { onNavigate?: () => void }) {
  const { user, logout } = useAuth();
  const isAdmin = user?.role === 'Admin';

  const linkClasses = ({ isActive }: { isActive: boolean }) =>
    `block rounded-md px-3 py-2 text-sm font-medium transition-colors ${
      isActive
        ? 'bg-blue-600 text-white'
        : 'text-gray-700 hover:bg-gray-100'
    }`;

  return (
    <div className="flex h-full flex-col">
      {/* Logo */}
      <div className="border-b border-gray-200 px-4 py-5">
        <h1 className="text-lg font-bold text-gray-900">Oáza ZK</h1>
        <p className="text-xs text-gray-500">Zadní Kopanina</p>
      </div>

      {/* Navigation */}
      <nav className="flex-1 space-y-1 overflow-y-auto px-3 py-4">
        {navItems
          .filter((item) => !item.adminOnly || isAdmin)
          .map((item) => (
            <NavLink
              key={item.path}
              to={item.path}
              className={linkClasses}
              onClick={onNavigate}
            >
              <span className="flex items-center justify-between">
                {item.label}
                {item.phase2 && (
                  <span className="rounded bg-gray-200 px-1.5 py-0.5 text-xs text-gray-500">
                    Připravujeme
                  </span>
                )}
              </span>
            </NavLink>
          ))}

        {isAdmin && (
          <>
            <div className="my-3 border-t border-gray-200" />
            {adminNavItems.map((item) => (
              <NavLink
                key={item.path}
                to={item.path}
                className={linkClasses}
                onClick={onNavigate}
              >
                {item.label}
              </NavLink>
            ))}
          </>
        )}
      </nav>

      {/* User info */}
      <div className="border-t border-gray-200 px-4 py-3">
        <div className="flex items-center gap-3">
          <div className="flex h-8 w-8 shrink-0 items-center justify-center rounded-full bg-blue-100 text-sm font-medium text-blue-700">
            {user?.name?.charAt(0)?.toUpperCase() ?? '?'}
          </div>
          <div className="min-w-0 flex-1">
            <p className="truncate text-sm font-medium text-gray-900">
              {user?.name ?? 'Uživatel'}
            </p>
            <p className="truncate text-xs text-gray-500">{user?.role}</p>
          </div>
        </div>
        <button
          onClick={logout}
          className="mt-2 w-full rounded-md px-3 py-1.5 text-left text-sm text-gray-600 transition-colors hover:bg-gray-100 hover:text-gray-900"
        >
          Odhlásit se
        </button>
      </div>
    </div>
  );
}

export function Layout() {
  const [isMobileMenuOpen, setIsMobileMenuOpen] = useState(false);

  return (
    <div className="flex h-screen bg-gray-50">
      {/* Desktop sidebar */}
      <aside className="hidden w-56 shrink-0 border-r border-gray-200 bg-white md:block">
        <SidebarContent />
      </aside>

      {/* Mobile overlay */}
      {isMobileMenuOpen && (
        <div
          className="fixed inset-0 z-40 bg-black/30 md:hidden"
          onClick={() => setIsMobileMenuOpen(false)}
        />
      )}

      {/* Mobile sidebar */}
      <aside
        className={`fixed inset-y-0 left-0 z-50 w-64 transform bg-white shadow-xl transition-transform md:hidden ${
          isMobileMenuOpen ? 'translate-x-0' : '-translate-x-full'
        }`}
      >
        <SidebarContent onNavigate={() => setIsMobileMenuOpen(false)} />
      </aside>

      {/* Main content */}
      <div className="flex flex-1 flex-col overflow-hidden">
        {/* Mobile header */}
        <header className="flex items-center border-b border-gray-200 bg-white px-4 py-3 md:hidden">
          <button
            onClick={() => setIsMobileMenuOpen(true)}
            className="rounded-md p-2 text-gray-600 hover:bg-gray-100 hover:text-gray-900"
            aria-label="Otevřít menu"
          >
            <svg
              className="h-6 w-6"
              fill="none"
              viewBox="0 0 24 24"
              strokeWidth="1.5"
              stroke="currentColor"
            >
              <path
                strokeLinecap="round"
                strokeLinejoin="round"
                d="M3.75 6.75h16.5M3.75 12h16.5m-16.5 5.25h16.5"
              />
            </svg>
          </button>
          <h1 className="ml-3 text-lg font-bold text-gray-900">Oáza ZK</h1>
        </header>

        {/* Page content */}
        <main className="flex-1 overflow-y-auto p-6">
          <Outlet />
        </main>
      </div>
    </div>
  );
}
