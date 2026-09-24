import React, { useState, useEffect, useRef } from "react";
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

interface ModuleConfig {
  id: string;
  title: string;
  description: string;
  icon: React.ReactNode;
  checkPermission?: () => boolean;
  links: { label: string; to: string; checkPermission?: () => boolean }[];
}

export const AppShell: React.FC<{ children: React.ReactNode }> = ({ children }) => {
  const location = useLocation();
  const navigate = useNavigate();
  const { user, logout, hasPermission } = useAuth();

  // State
  const [launcherOpen, setLauncherOpen] = useState(false);
  const [searchOpen, setSearchOpen] = useState(false);
  const [searchTerm, setSearchTerm] = useState("");
  const [searchResults, setSearchResults] = useState<any[]>([]);
  const [searchLoading, setSearchLoading] = useState(false);
  const [userMenuOpen, setUserMenuOpen] = useState(false);

  const searchInputRef = useRef<HTMLInputElement>(null);
  const userMenuRef = useRef<HTMLDivElement>(null);

  // Handle Ctrl+K / Cmd+K and Escape
  useEffect(() => {
    const handleKeyDown = (e: KeyboardEvent) => {
      if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === "k") {
        e.preventDefault();
        setSearchOpen(true);
        setTimeout(() => searchInputRef.current?.focus(), 50);
      }
      if (e.key === "Escape") {
        setSearchOpen(false);
        setLauncherOpen(false);
        setUserMenuOpen(false);
      }
    };
    window.addEventListener("keydown", handleKeyDown);
    return () => window.removeEventListener("keydown", handleKeyDown);
  }, []);

  // Close user menu on outside click
  useEffect(() => {
    const handleClickOutside = (e: MouseEvent) => {
      if (userMenuRef.current && !userMenuRef.current.contains(e.target as Node)) {
        setUserMenuOpen(false);
      }
    };
    document.addEventListener("mousedown", handleClickOutside);
    return () => document.removeEventListener("mousedown", handleClickOutside);
  }, []);

  // Debounced Search API call
  useEffect(() => {
    const term = searchTerm.trim();
    if (term.length < 2) {
      setSearchResults([]);
      setSearchLoading(false);
      return;
    }

    let active = true;
    setSearchLoading(true);
    const timer = setTimeout(() => {
      fetch(`${api}/search?q=${encodeURIComponent(term)}`, { credentials: "include" })
        .then((res) => (res.ok ? res.json() : []))
        .then((data) => {
          if (active) {
            setSearchResults(data);
            setSearchLoading(false);
          }
        })
        .catch(() => {
          if (active) {
            setSearchResults([]);
            setSearchLoading(false);
          }
        });
    }, 200);

    return () => {
      active = false;
      clearTimeout(timer);
    };
  }, [searchTerm]);

  // Studio full-screen mode for draft editor (evaluated AFTER all hooks)
  if (location.pathname.startsWith("/matter-drafts/")) {
    return <main className="studio-root" id="main-content" tabIndex={-1}>{children}</main>;
  }

  const selectSearchResult = (route: string) => {
    setSearchTerm("");
    setSearchOpen(false);
    navigate(route);
  };

  // User details
  const userDisplayName = user?.displayName || user?.username || "Officer";
  const userDesignation = user?.designation?.name || null;
  const userInitials = userDisplayName
    .split(" ")
    .map((n) => n[0])
    .slice(0, 2)
    .join("")
    .toUpperCase();

  // Explicit Permission Helpers
  const canAccessMyWork = () => hasPermission("WorkItem.View") || hasPermission("WorkItem.Create");
  const canAccessAttention = () => hasPermission("Schedule.View") || hasPermission("WorkItem.View") || hasPermission("Dak.View");
  const canAccessCorrespondence = () => hasPermission("Dak.View") || hasPermission("Dak.Register") || hasPermission("Outward.View") || hasPermission("Outward.Create");
  const canAccessMatters = () => hasPermission("Matter.View") || hasPermission("Matter.Create");
  const canAccessCourt = () => hasPermission("Court.View") || hasPermission("Court.Create") || hasPermission("Award.View");
  const canAccessOversight = () => hasPermission("WorkItem.View") || hasPermission("Audit.View");
  const canAccessAdmin = () => hasPermission("Users.Manage") || hasPermission("Access.Manage") || hasPermission("Audit.View");

  // Module Launcher Configuration
  const modules: ModuleConfig[] = [
    {
      id: "land",
      title: "Land Records",
      description: "Villages, khasras, awards, and khatauni registers",
      icon: <IconLand size={20} />,
      links: [
        { label: "Administrative Hierarchy", to: "/land-records" },
        { label: "Village Directory", to: "/villages" },
        { label: "Awards Register", to: "/awards" },
        { label: "LR Import Registers", to: "/imports/lr" }
      ]
    },
    {
      id: "matters",
      title: "Matters & Files",
      description: "Acquisition matters, note sheets, and draft files",
      icon: <IconMatters size={20} />,
      checkPermission: canAccessMatters,
      links: [
        { label: "Matters Directory", to: "/matters", checkPermission: canAccessMatters }
      ]
    },
    {
      id: "correspondence",
      title: "Correspondence",
      description: "Inward dak, desk movement, and outward dispatch",
      icon: <IconDak size={20} />,
      checkPermission: canAccessCorrespondence,
      links: [
        { label: "Dak / Inward", to: "/dak", checkPermission: () => hasPermission("Dak.View") || hasPermission("Dak.Register") },
        { label: "Register New Inward", to: "/dak/register", checkPermission: () => hasPermission("Dak.Register") },
        { label: "Outward Dispatch", to: "/outward", checkPermission: () => hasPermission("Outward.View") || hasPermission("Outward.Create") }
      ]
    },
    {
      id: "court",
      title: "Court & Litigation",
      description: "Court cases, hearing dates, order sheets, and references",
      icon: <IconCourt size={20} />,
      checkPermission: canAccessCourt,
      links: [
        { label: "Court Cases Directory", to: "/court-cases", checkPermission: canAccessCourt }
      ]
    },
    {
      id: "oversight",
      title: "Oversight",
      description: "Branch pulse, handler workloads, and team activity",
      icon: <IconPulse size={20} />,
      links: [
        { label: "Branch Pulse", to: "/branch-pulse", checkPermission: () => hasPermission("WorkItem.View") },
        { label: "My History", to: "/my-history" },
        { label: "Team Activity", to: "/team-activity", checkPermission: () => hasPermission("Audit.View") }
      ]
    },
    {
      id: "admin",
      title: "Administration",
      description: "User management, access roles, and audit trail",
      icon: <IconShield size={20} />,
      checkPermission: canAccessAdmin,
      links: [
        { label: "User Management", to: "/admin/users", checkPermission: () => hasPermission("Users.Manage") },
        { label: "Access & Roles", to: "/admin/access", checkPermission: () => hasPermission("Access.Manage") },
        { label: "System Audit Trail", to: "/admin/audit-logs", checkPermission: () => hasPermission("Audit.View") }
      ]
    }
  ];

  // Contextual Sub-Header Navigation
  const path = location.pathname;
  let contextualNav: { categoryTitle: string; links: { label: string; to: string; checkPermission?: () => boolean }[] } | null = null;

  if (path.startsWith("/land-records") || path.startsWith("/subdivisions") || path.startsWith("/districts") || path.startsWith("/villages") || path.startsWith("/khasras") || path.startsWith("/khatauni") || path.startsWith("/awards") || path.startsWith("/imports/lr")) {
    contextualNav = {
      categoryTitle: "Land Records",
      links: [
        { label: "Hierarchy Overview", to: "/land-records" },
        { label: "Villages", to: "/villages" },
        { label: "Awards", to: "/awards" },
        { label: "LR Registers", to: "/imports/lr" }
      ]
    };
  } else if (path.startsWith("/dak") || path.startsWith("/my-desk") || path.startsWith("/outward")) {
    contextualNav = {
      categoryTitle: "Correspondence",
      links: [
        { label: "My Desk", to: "/my-desk", checkPermission: () => hasPermission("Dak.View") },
        { label: "Dak / Inward", to: "/dak", checkPermission: () => hasPermission("Dak.View") || hasPermission("Dak.Register") },
        { label: "Outward / Dispatch", to: "/outward", checkPermission: () => hasPermission("Outward.View") || hasPermission("Outward.Create") }
      ]
    };
  } else if (path.startsWith("/matters") || path.startsWith("/my-work")) {
    contextualNav = {
      categoryTitle: "Matters & Work",
      links: [
        { label: "My Work Queue", to: "/my-work", checkPermission: canAccessMyWork },
        { label: "Matters Directory", to: "/matters", checkPermission: canAccessMatters }
      ]
    };
  } else if (path.startsWith("/court-cases") || path.startsWith("/court")) {
    contextualNav = {
      categoryTitle: "Court & Litigation",
      links: [
        { label: "Court Case Directory", to: "/court-cases", checkPermission: canAccessCourt }
      ]
    };
  } else if (path.startsWith("/branch-pulse") || path.startsWith("/my-history") || path.startsWith("/team-activity")) {
    contextualNav = {
      categoryTitle: "Oversight",
      links: [
        { label: "Branch Pulse", to: "/branch-pulse", checkPermission: () => hasPermission("WorkItem.View") },
        { label: "My History", to: "/my-history" },
        { label: "Team Activity", to: "/team-activity", checkPermission: () => hasPermission("Audit.View") }
      ]
    };
  } else if (path.startsWith("/admin")) {
    contextualNav = {
      categoryTitle: "Administration",
      links: [
        { label: "Users", to: "/admin/users", checkPermission: () => hasPermission("Users.Manage") },
        { label: "Access & Roles", to: "/admin/access", checkPermission: () => hasPermission("Access.Manage") },
        { label: "Audit Trail", to: "/admin/audit-logs", checkPermission: () => hasPermission("Audit.View") }
      ]
    };
  }

  return (
    <div className="lac-shell-root">
      {/* Topbar Header (~54px) */}
      <header className="lac-header">
        <div className="lac-header-left">
          <Link to="/" className="lac-header-brand" title="LAC Home">
            <span className="lac-header-badge">LAC</span>
            <span className="lac-header-title">LAC Platform</span>
          </Link>

          <NavLink
            to="/"
            end
            className={({ isActive }) => `lac-header-link ${isActive ? "active" : ""}`}
          >
            <IconHome size={16} />
            <span>Home</span>
          </NavLink>

          <button
            className={`lac-launcher-trigger ${launcherOpen ? "active" : ""}`}
            onClick={() => setLauncherOpen(!launcherOpen)}
            title="Open Modules Launcher"
          >
            <IconMenu size={16} />
            <span>Apps</span>
          </button>
        </div>

        {/* Center: Global Search */}
        <div className="lac-header-search-wrap">
          <div className="lac-header-search-box">
            <IconSearch size={15} className="lac-search-icon" />
            <input
              ref={searchInputRef}
              value={searchTerm}
              onChange={(e) => {
                setSearchTerm(e.target.value);
                setSearchOpen(true);
              }}
              onFocus={() => setSearchOpen(true)}
              placeholder="Search village, khasra, award, or file…"
            />
            <span className="lac-search-shortcut">Ctrl + K</span>
          </div>

          {/* Search Dropdown */}
          {searchOpen && searchTerm.trim().length >= 2 && (
            <div className="lac-search-dropdown">
              {searchLoading && <div className="lac-search-status">Searching records…</div>}
              {!searchLoading && searchResults.length === 0 && (
                <div className="lac-search-status">No matching records found.</div>
              )}
              {!searchLoading &&
                searchResults.map((result: any, idx: number) => (
                  <button
                    key={`${result.type}-${result.id}-${idx}`}
                    className="lac-search-dropdown-item"
                    onClick={() => selectSearchResult(result.route)}
                  >
                    <span className="lac-search-tag">{result.type}</span>
                    <div className="lac-search-text">
                      <span className="lac-search-label">{result.label}</span>
                      {result.context && (
                        <span className="lac-search-sub">{result.context}</span>
                      )}
                    </div>
                  </button>
                ))}
            </div>
          )}
        </div>

        {/* Right: Actions & User Identity */}
        <div className="lac-header-right">
          {canAccessAttention() && (
            <Link to="/my-attention" className="lac-header-attention-btn" title="Needs Attention">
              <IconAttention size={16} />
              <span>Attention</span>
            </Link>
          )}

          {/* User Profile Menu */}
          <div className="lac-user-profile-wrap" ref={userMenuRef}>
            <button
              className="lac-user-chip-btn"
              onClick={() => setUserMenuOpen(!userMenuOpen)}
              title="User Profile Menu"
            >
              <span className="lac-user-avatar">{userInitials}</span>
              <div className="lac-user-info">
                <span className="lac-user-name">{userDisplayName}</span>
                {userDesignation && <span className="lac-user-role">{userDesignation}</span>}
              </div>
            </button>

            {userMenuOpen && (
              <div className="lac-user-dropdown-menu">
                <div className="lac-user-dropdown-header">
                  <strong>{userDisplayName}</strong>
                  {userDesignation && <span>{userDesignation}</span>}
                </div>
                <button
                  className="lac-user-dropdown-item lac-logout-item"
                  onClick={() => {
                    setUserMenuOpen(false);
                    void logout();
                  }}
                >
                  <IconLogOut size={15} />
                  <span>Sign Out</span>
                </button>
              </div>
            )}
          </div>
        </div>
      </header>

      {/* Floating Module Launcher Popover */}
      {launcherOpen && (
        <div className="lac-launcher-backdrop" onClick={() => setLauncherOpen(false)}>
          <div className="lac-launcher-popover" onClick={(e) => e.stopPropagation()}>
            <div className="lac-launcher-header">
              <h3>All Modules</h3>
              <button
                className="lac-launcher-close"
                onClick={() => setLauncherOpen(false)}
                title="Close"
              >
                &times;
              </button>
            </div>

            <div className="lac-launcher-grid">
              {modules.map((mod) => {
                if (mod.checkPermission && !mod.checkPermission()) return null;

                const validLinks = mod.links.filter(
                  (l) => !l.checkPermission || l.checkPermission()
                );
                if (validLinks.length === 0) return null;

                return (
                  <div key={mod.id} className="lac-launcher-card">
                    <div className="lac-launcher-card-head">
                      <span className="lac-launcher-icon">{mod.icon}</span>
                      <div>
                        <h4>{mod.title}</h4>
                        <p>{mod.description}</p>
                      </div>
                    </div>

                    <div className="lac-launcher-card-links">
                      {validLinks.map((link) => (
                        <Link
                          key={link.to}
                          to={link.to}
                          className="lac-launcher-link"
                          onClick={() => setLauncherOpen(false)}
                        >
                          <span>{link.label}</span>
                          <IconChevronRight size={13} />
                        </Link>
                      ))}
                    </div>
                  </div>
                );
              })}
            </div>
          </div>
        </div>
      )}

      {/* Contextual Sub-Header Navigation */}
      {contextualNav && (
        <div className="lac-contextual-bar">
          <div className="lac-contextual-container">
            <span className="lac-contextual-category">{contextualNav.categoryTitle}:</span>
            <nav className="lac-contextual-nav">
              {contextualNav.links.map((link) => {
                if (link.checkPermission && !link.checkPermission()) return null;
                return (
                  <NavLink
                    key={link.to}
                    to={link.to}
                    end={link.to === path}
                    className={({ isActive }) =>
                      `lac-contextual-tab ${isActive ? "active" : ""}`
                    }
                  >
                    {link.label}
                  </NavLink>
                );
              })}
            </nav>
          </div>
        </div>
      )}

      {/* Main Workspace Content */}
      <main className="lac-body-content" id="main-content" tabIndex={-1}>
        {children}
      </main>
    </div>
  );
};
