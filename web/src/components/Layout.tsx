import { useState } from 'react';
import { NavLink, Outlet, useLocation } from 'react-router-dom';
import { useAuth } from '../auth/AuthContext';

interface NavItem {
  label: string;
  path: string;
  adminOnly?: boolean;
  children?: NavItem[];
}

const navItems: NavItem[] = [
  { label: 'Přehled', path: '/dashboard' },
  { label: 'Dokumenty', path: '/documents' },
  {
    label: 'Hospodaření',
    path: '/finance',
    children: [
      { label: 'Zálohy', path: '/advances' },
      { label: 'Vyúčtování', path: '/billing' },
    ],
  },
  {
    label: 'Odečty',
    path: '/readings',
    children: [
      { label: 'Seznam odečtů', path: '/readings/list', adminOnly: true },
      { label: 'Import odečtů', path: '/readings/import', adminOnly: true },
    ],
  },
];

const adminNavItems: NavItem[] = [
  { label: 'Správa domácností', path: '/admin/houses', adminOnly: true },
  { label: 'Správa uživatelů', path: '/admin/users', adminOnly: true },
  { label: 'Správa vodoměrů', path: '/admin/meters', adminOnly: true },
];

function SidebarContent({ onNavigate }: { onNavigate?: () => void }) {
  const { user, logout } = useAuth();
  const isAdmin = user?.role === 'Admin';
  const location = useLocation();

  const linkClasses = ({ isActive }: { isActive: boolean }) =>
    `block rounded-md px-3 py-2 text-sm font-medium transition-colors ${
      isActive
        ? 'bg-blue-600 text-white'
        : 'text-gray-700 hover:bg-gray-100'
    }`;

  const childLinkClasses = ({ isActive }: { isActive: boolean }) =>
    `block rounded-md pl-7 pr-3 py-1.5 text-sm transition-colors ${
      isActive
        ? 'bg-blue-100 text-blue-700 font-medium'
        : 'text-gray-500 hover:bg-gray-50 hover:text-gray-700'
    }`;

  const isParentActive = (item: NavItem): boolean => {
    if (location.pathname === item.path || location.pathname.startsWith(item.path + '/')) return true;
    return item.children?.some((c) => location.pathname === c.path || location.pathname.startsWith(c.path + '/')) ?? false;
  };

  return (
    <div className="flex h-full flex-col">
      {/* Logo */}
      <div className="border-b border-gray-200 px-4 py-5">
        <h1 className="text-lg font-bold text-gray-900">Oáza ZK</h1>
        <p className="text-xs text-gray-500">Zadní Kopanina</p>
      </div>

      {/* Navigation */}
      <nav className="flex-1 space-y-0.5 overflow-y-auto px-3 py-4">
        {navItems
          .filter((item) => !item.adminOnly || isAdmin)
          .map((item) => (
            <div key={item.path}>
              <NavLink
                to={item.path}
                end={!!item.children}
                className={linkClasses}
                onClick={onNavigate}
              >
                {item.label}
              </NavLink>
              {item.children && isParentActive(item) && (
                <div className="mt-0.5 mb-1 space-y-0.5">
                  {item.children
                    .filter((child) => !child.adminOnly || isAdmin)
                    .map((child) => (
                      <NavLink
                        key={child.path}
                        to={child.path}
                        className={childLinkClasses}
                        onClick={onNavigate}
                      >
                        {child.label}
                      </NavLink>
                    ))}
                </div>
              )}
            </div>
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
    <div className="flex min-h-screen bg-gray-50">
      {/* Desktop sidebar */}
      <aside className="hidden w-56 shrink-0 border-r border-gray-200 bg-white md:block">
        <SidebarContent />
      </aside>

      {/* Mobile hamburger */}
      <div className="fixed left-4 top-4 z-50 md:hidden">
        <button
          onClick={() => setIsMobileMenuOpen(!isMobileMenuOpen)}
          className="rounded-md border bg-white p-2 shadow-sm"
          aria-label="Toggle menu"
        >
          <svg className="h-5 w-5" fill="none" viewBox="0 0 24 24" stroke="currentColor">
            {isMobileMenuOpen ? (
              <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M6 18L18 6M6 6l12 12" />
            ) : (
              <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M4 6h16M4 12h16M4 18h16" />
            )}
          </svg>
        </button>
      </div>

      {/* Mobile overlay */}
      {isMobileMenuOpen && (
        <>
          <div className="fixed inset-0 z-40 bg-black/30" onClick={() => setIsMobileMenuOpen(false)} />
          <aside className="fixed inset-y-0 left-0 z-40 w-64 bg-white shadow-lg">
            <SidebarContent onNavigate={() => setIsMobileMenuOpen(false)} />
          </aside>
        </>
      )}

      {/* Main content */}
      <main className="flex-1 overflow-auto">
        <div className="mx-auto max-w-6xl px-4 py-6 md:px-8">
          <Outlet />
        </div>
      </main>
    </div>
  );
}
