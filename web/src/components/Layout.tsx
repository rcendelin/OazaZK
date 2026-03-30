import { useState } from 'react';
import { NavLink, Outlet, useLocation } from 'react-router-dom';
import { useAuth } from '../auth/AuthContext';
import {
  LayoutDashboard,
  FileText,
  Wallet,
  Banknote,
  Receipt,
  Droplets,
  List,
  Upload,
  Home,
  Users,
  Gauge,
  LogOut,
  Menu,
  X,
  ChevronDown,
  ChevronRight,
} from 'lucide-react';
import type { ReactNode } from 'react';

interface NavItem {
  label: string;
  path: string;
  icon: ReactNode;
  adminOnly?: boolean;
  children?: NavItem[];
}

const iconSize = 18;

const navItems: NavItem[] = [
  { label: 'Přehled', path: '/dashboard', icon: <LayoutDashboard size={iconSize} /> },
  { label: 'Dokumenty', path: '/documents', icon: <FileText size={iconSize} /> },
  {
    label: 'Hospodaření',
    path: '/finance',
    icon: <Wallet size={iconSize} />,
    children: [
      { label: 'Zálohy', path: '/advances', icon: <Banknote size={iconSize} /> },
      { label: 'Vyúčtování', path: '/billing', icon: <Receipt size={iconSize} /> },
    ],
  },
  {
    label: 'Odečty',
    path: '/readings',
    icon: <Droplets size={iconSize} />,
    children: [
      { label: 'Seznam odečtů', path: '/readings/list', icon: <List size={iconSize} />, adminOnly: true },
      { label: 'Import odečtů', path: '/readings/import', icon: <Upload size={iconSize} />, adminOnly: true },
    ],
  },
];

const adminNavItems: NavItem[] = [
  { label: 'Domácnosti', path: '/admin/houses', icon: <Home size={iconSize} />, adminOnly: true },
  { label: 'Uživatelé', path: '/admin/users', icon: <Users size={iconSize} />, adminOnly: true },
  { label: 'Vodoměry', path: '/admin/meters', icon: <Gauge size={iconSize} />, adminOnly: true },
];

function SidebarContent({ onNavigate }: { onNavigate?: () => void }) {
  const { user, logout } = useAuth();
  const isAdmin = user?.role === 'Admin';
  const location = useLocation();

  const isParentActive = (item: NavItem): boolean => {
    if (location.pathname === item.path || location.pathname.startsWith(item.path + '/')) return true;
    return item.children?.some((c) => location.pathname === c.path || location.pathname.startsWith(c.path + '/')) ?? false;
  };

  const linkClasses = (isActive: boolean) =>
    `group flex items-center gap-3 rounded-lg px-3 py-2.5 text-sm font-medium transition-all duration-150 ${
      isActive
        ? 'bg-sidebar-active text-sidebar-text-active shadow-sm'
        : 'text-sidebar-text hover:bg-sidebar-hover hover:text-sidebar-text-active'
    }`;

  const childLinkClasses = (isActive: boolean) =>
    `flex items-center gap-3 rounded-lg pl-10 pr-3 py-2 text-[13px] transition-all duration-150 ${
      isActive
        ? 'text-sidebar-text-active font-medium'
        : 'text-sidebar-text hover:text-sidebar-text-active'
    }`;

  return (
    <div className="flex h-full flex-col bg-sidebar">
      {/* Logo */}
      <div className="px-5 py-6">
        <div className="flex items-center gap-3">
          <div className="flex h-9 w-9 items-center justify-center rounded-xl bg-sidebar-active text-white font-bold text-sm">
            O
          </div>
          <div>
            <h1 className="text-base font-bold text-white tracking-tight">Oáza ZK</h1>
            <p className="text-[11px] text-sidebar-text leading-none">Zadní Kopanina</p>
          </div>
        </div>
      </div>

      {/* Navigation */}
      <nav className="flex-1 space-y-1 overflow-y-auto px-3 pb-4">
        {navItems
          .filter((item) => !item.adminOnly || isAdmin)
          .map((item) => {
            const active = isParentActive(item);
            return (
              <div key={item.path}>
                <NavLink
                  to={item.path}
                  end={!!item.children}
                  className={() => linkClasses(location.pathname === item.path)}
                  onClick={onNavigate}
                >
                  {item.icon}
                  <span className="flex-1">{item.label}</span>
                  {item.children && (
                    active
                      ? <ChevronDown size={14} className="text-sidebar-text" />
                      : <ChevronRight size={14} className="text-sidebar-text" />
                  )}
                </NavLink>
                {item.children && active && (
                  <div className="mt-1 space-y-0.5">
                    {item.children
                      .filter((child) => !child.adminOnly || isAdmin)
                      .map((child) => (
                        <NavLink
                          key={child.path}
                          to={child.path}
                          className={({ isActive }) => childLinkClasses(isActive)}
                          onClick={onNavigate}
                        >
                          {child.icon}
                          {child.label}
                        </NavLink>
                      ))}
                  </div>
                )}
              </div>
            );
          })}

        {isAdmin && (
          <>
            <div className="my-4 mx-2 border-t border-white/10" />
            <p className="px-3 pb-1.5 text-[10px] font-semibold uppercase tracking-widest text-sidebar-text/60">
              Administrace
            </p>
            {adminNavItems.map((item) => (
              <NavLink
                key={item.path}
                to={item.path}
                className={({ isActive }) => linkClasses(isActive)}
                onClick={onNavigate}
              >
                {item.icon}
                {item.label}
              </NavLink>
            ))}
          </>
        )}
      </nav>

      {/* User card */}
      <div className="border-t border-white/10 px-3 py-4">
        <div className="flex items-center gap-3 rounded-lg px-3 py-2">
          <div className="flex h-9 w-9 shrink-0 items-center justify-center rounded-full bg-gradient-to-br from-accent to-indigo-400 text-sm font-bold text-white">
            {user?.name?.charAt(0)?.toUpperCase() ?? '?'}
          </div>
          <div className="min-w-0 flex-1">
            <p className="truncate text-sm font-medium text-white">
              {user?.name ?? 'Uživatel'}
            </p>
            <p className="truncate text-[11px] text-sidebar-text">{user?.role === 'Admin' ? 'Administrátor' : user?.role === 'Accountant' ? 'Účetní' : 'Člen'}</p>
          </div>
        </div>
        <button
          onClick={logout}
          className="mt-1 flex w-full items-center gap-3 rounded-lg px-3 py-2 text-sm text-sidebar-text transition-all hover:bg-sidebar-hover hover:text-white"
        >
          <LogOut size={iconSize} />
          Odhlásit se
        </button>
      </div>
    </div>
  );
}

export function Layout() {
  const [isMobileMenuOpen, setIsMobileMenuOpen] = useState(false);

  return (
    <div className="flex min-h-screen bg-surface">
      {/* Desktop sidebar */}
      <aside className="hidden w-[260px] shrink-0 lg:block">
        <div className="fixed inset-y-0 left-0 w-[260px]">
          <SidebarContent />
        </div>
      </aside>

      {/* Mobile hamburger */}
      <div className="fixed left-4 top-4 z-50 lg:hidden">
        <button
          onClick={() => setIsMobileMenuOpen(!isMobileMenuOpen)}
          className="flex h-10 w-10 items-center justify-center rounded-xl bg-sidebar text-white shadow-lg transition-transform active:scale-95"
          aria-label="Toggle menu"
        >
          {isMobileMenuOpen ? <X size={20} /> : <Menu size={20} />}
        </button>
      </div>

      {/* Mobile overlay */}
      {isMobileMenuOpen && (
        <>
          <div
            className="fixed inset-0 z-40 bg-black/40 backdrop-blur-sm"
            onClick={() => setIsMobileMenuOpen(false)}
          />
          <aside className="fixed inset-y-0 left-0 z-40 w-[280px] animate-slide-in shadow-2xl">
            <SidebarContent onNavigate={() => setIsMobileMenuOpen(false)} />
          </aside>
        </>
      )}

      {/* Main content */}
      <main className="flex-1 overflow-auto">
        <div className="animate-fade-in mx-auto max-w-6xl px-4 py-8 lg:px-8">
          <Outlet />
        </div>
      </main>
    </div>
  );
}
