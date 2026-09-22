import React, { useState, useEffect } from "react";
import { Link, NavLink, useLocation, useNavigate } from "react-router-dom";
import { useAuth } from "../auth/AuthProvider";
import {
  IconHome,
  IconDesk,
  IconWorkItem,
  IconAttention,
  IconCalendar,
  IconDak,
  IconOutward,
  IconMatters,
  IconLand,
  IconCourt,
  IconPulse,
  IconHistory,
  IconTeam,
  IconUsers,
  IconShield,
  IconAudit,
  IconSearch,
  IconMenu,
  IconLogOut,
  IconChevronRight
} from "./Icons";

const api = "/api";

function GlobalSearchInput() {
  const [term, setTerm] = useState("");
  const [delayed, setDelayed] = useState("");
  const [results, setResults] = useState<any[]>([]);
  const [loading, setLoading] = useState(false);
  const [open, setOpen] = useState(false);
  const navigate = useNavigate();

  useEffect(() => {
    const timer = window.setTimeout(() => setDelayed(term.trim()), 250);
    return () => window.clearTimeout(timer);
  }, [term]);

  useEffect(() => {
    if (delayed.length < 2) {
      setResults([]);
      setLoading(false);
      return;
    }
    let active = true;
    setLoading(true);
    fetch(`${api}/search?q=${encodeURIComponent(delayed)}`, { credentials: "include" })
      .then((res) => (res.ok ? res.json() : []))
      .then((data) => {
        if (active) {
          setResults(data);
          setLoading(false);
          setOpen(true);
        }
      })
      .catch(() => {
        if (active) {
          setResults([]);
          setLoading(false);
        }
      });
    return () => {
      active = false;
    };
  }, [delayed]);

  const choose = (target: string) => {
    setTerm("");
    setOpen(false);
    navigate(target);
  };

  return (
    <div className="lac-topbar-search">
      <div className="lac-search-box">
        <IconSearch size={16} className="text-slate-400" />
        <input
          value={term}
          onChange={(e) => setTerm(e.target.value)}
          onFocus={() => term.length >= 2 && setOpen(true)}
          placeholder="Search village, khasra, award, or record…"
        />
        <span className="lac-search-shortcut">Ctrl + K</span>
      </div>

      {open && term.length >= 2 && (
        <div className="lac-search-results-dropdown" onMouseLeave={() => setOpen(false)}>
          {loading && (
            <div className="p-3 text-xs text-slate-500">Searching records…</div>
          )}
          {!loading && results.length === 0 && (
            <div className="p-3 text-xs text-slate-500">No matching records found.</div>
          )}
          {!loading &&
            results.map((result: any, idx: number) => (
              <button
                key={`${result.type}-${result.id}-${idx}`}
                className="lac-search-item"
                onClick={() => choose(result.route)}
              >
                <span className="lac-search-type-badge">{result.type}</span>
                <div className="lac-search-item-info">
                  <span className="lac-search-item-title">{result.label}</span>
                  {result.context && (
                    <span className="lac-search-item-context">{result.context}</span>
                  )}
                </div>
              </button>
            ))}
        </div>
      )}
    </div>
  );
}

interface NavItemConfig {
  label: string;
  to: string;
  icon: React.ReactNode;
  permission?: string;
  exact?: boolean;
}

interface NavGroupConfig {
  title: string;
  items: NavItemConfig[];
}

export const AppShell: React.FC<{ children: React.ReactNode }> = ({ children }) => {
  const location = useLocation();
  const { user, logout, hasPermission } = useAuth();
  const [collapsed, setCollapsed] = useState(false);

  if (location.pathname.startsWith("/matter-drafts/")) {
    return <main className="studio-root" id="main-content" tabIndex={-1}>{children}</main>;
  }

  const navGroups: NavGroupConfig[] = [
    {
      title: "Overview",
      items: [{ label: "Home", to: "/", icon: <IconHome size={18} />, exact: true }]
    },
    {
      title: "My Day",
      items: [
        { label: "My Desk", to: "/my-desk", icon: <IconDesk size={18} />, permission: "Dak.View" },
        { label: "My Work", to: "/my-work", icon: <IconWorkItem size={18} />, permission: "WorkItem.View" },
        { label: "Needs Attention", to: "/my-attention", icon: <IconAttention size={18} />, permission: "Schedule.View" },
        { label: "Calendar", to: "/calendar", icon: <IconCalendar size={18} />, permission: "Schedule.View" }
      ]
    },
    {
      title: "Correspondence",
      items: [
        { label: "Dak / Inward", to: "/dak", icon: <IconDak size={18} />, permission: "Dak.View" },
        { label: "Outward / Dispatch", to: "/outward", icon: <IconOutward size={18} />, permission: "Outward.View" }
      ]
    },
    {
      title: "Matters & Files",
      items: [
        { label: "Matters", to: "/matters", icon: <IconMatters size={18} />, permission: "Matter.View" }
      ]
    },
    {
      title: "Land Records",
      items: [
        { label: "Land Records", to: "/land-records", icon: <IconLand size={18} /> },
        { label: "Villages", to: "/villages", icon: <IconLand size={18} /> },
        { label: "Awards", to: "/awards", icon: <IconLand size={18} /> },
        { label: "LR Registers", to: "/imports/lr", icon: <IconLand size={18} /> }
      ]
    },
    {
      title: "Court & Litigation",
      items: [
        { label: "Court Cases", to: "/court-cases", icon: <IconCourt size={18} />, permission: "Court.View" }
      ]
    },
    {
      title: "Oversight",
      items: [
        { label: "Branch Pulse", to: "/branch-pulse", icon: <IconPulse size={18} />, permission: "WorkItem.View" },
        { label: "My History", to: "/my-history", icon: <IconHistory size={18} /> },
        { label: "Team Activity", to: "/team-activity", icon: <IconTeam size={18} />, permission: "Audit.View" }
      ]
    },
    {
      title: "Administration",
      items: [
        { label: "Users", to: "/admin/users", icon: <IconUsers size={18} />, permission: "Users.Manage" },
        { label: "Access & Roles", to: "/admin/access", icon: <IconShield size={18} />, permission: "Access.Manage" },
        { label: "Audit Trail", to: "/admin/audit-logs", icon: <IconAudit size={18} />, permission: "Audit.View" }
      ]
    }
  ];

  // User initials
  const userDisplayName = user?.displayName || user?.username || "Officer";
  const userInitials = userDisplayName
    .split(" ")
    .map((n) => n[0])
    .slice(0, 2)
    .join("")
    .toUpperCase();

  return (
    <div className="app-shell-v2">
      {/* Sidebar / Left Rail Navigation */}
      <aside className={`lac-sidebar ${collapsed ? "lac-sidebar-collapsed" : ""}`}>
        <div className="lac-sidebar-brand">
          <Link to="/" className="lac-brand-logo">
            <span className="lac-brand-badge">LAC</span>
            {!collapsed && (
              <div className="lac-brand-text">
                <span className="lac-brand-title">LAC Platform</span>
                <span className="lac-brand-subtitle">Govt Workspace</span>
              </div>
            )}
          </Link>
          <button
            className="lac-sidebar-toggle"
            onClick={() => setCollapsed(!collapsed)}
            title={collapsed ? "Expand navigation" : "Collapse navigation"}
            aria-label="Toggle navigation"
          >
            <IconMenu size={16} />
          </button>
        </div>

        <div className="lac-sidebar-scroll">
          {navGroups.map((group, gIdx) => {
            const visibleItems = group.items.filter(
              (item) => !item.permission || hasPermission(item.permission)
            );
            if (visibleItems.length === 0) return null;

            return (
              <div key={`group-${gIdx}`} className="lac-nav-group">
                <span className="lac-nav-group-title">{group.title}</span>
                {visibleItems.map((item) => (
                  <NavLink
                    key={item.to}
                    to={item.to}
                    end={item.exact}
                    className={({ isActive }) =>
                      `lac-nav-link ${isActive ? "active" : ""}`
                    }
                    title={collapsed ? item.label : undefined}
                  >
                    <span className="lac-nav-icon">{item.icon}</span>
                    <span className="lac-nav-label">{item.label}</span>
                  </NavLink>
                ))}
              </div>
            );
          })}
        </div>
      </aside>

      {/* Main Workspace Workspace */}
      <div className="lac-workspace-v2">
        <header className="lac-topbar-v2">
          <div className="lac-topbar-brand-title">
            <h1>Land Acquisition Cell</h1>
            <span>District Administrative Workspace</span>
          </div>

          <GlobalSearchInput />

          <div className="lac-topbar-user">
            <div className="lac-user-badge-wrap">
              <div className="lac-user-avatar">{userInitials}</div>
              <div className="lac-user-details">
                <span className="lac-user-name">{userDisplayName}</span>
                <span className="lac-user-role-pill">
                  {user?.designation?.name || "LAC Officer"}
                </span>
              </div>
            </div>

            <button
              className="lac-logout-btn"
              onClick={() => void logout()}
              title="Sign Out"
            >
              <IconLogOut size={15} />
              <span>Sign Out</span>
            </button>
          </div>
        </header>

        <main className="lac-main-content-v2" id="main-content" tabIndex={-1}>
          {children}
        </main>
      </div>
    </div>
  );
};
