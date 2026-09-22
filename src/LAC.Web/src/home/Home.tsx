import React, { useEffect, useState } from "react";
import { Link } from "react-router-dom";
import { useAuth } from "../auth/AuthProvider";
import {
  IconDesk,
  IconWorkItem,
  IconAttention,
  IconCalendar,
  IconLand,
  IconMatters,
  IconDak,
  IconCourt,
  IconPulse,
  IconShield,
  IconPlus,
  IconSearch,
  IconChevronRight,
  IconArrowRight,
  IconAward
} from "../components/Icons";
import "./home.css";

interface OperationalCounts {
  deskCount?: number;
  workCount?: number;
  attentionCount?: number;
  calendarCount?: number;
}

export const Home: React.FC = () => {
  const { user, hasPermission } = useAuth();
  const [counts, setCounts] = useState<OperationalCounts>({});

  const userDisplayName = user?.displayName || user?.username || "Officer";
  const designationName = user?.designation?.name || "Administrative Officer";

  // Formatted currentDate
  const todayFormatted = new Intl.DateTimeFormat("en-GB", {
    weekday: "long",
    day: "2-digit",
    month: "long",
    year: "numeric"
  }).format(new Date());

  // Greeting time calculation
  const currentHour = new Date().getHours();
  const greetingTime =
    currentHour < 12 ? "Good morning" : currentHour < 17 ? "Good afternoon" : "Good evening";

  // Fetch operational summary counts gracefully
  useEffect(() => {
    let active = true;

    // Fetch My Desk summary
    if (hasPermission("Dak.View")) {
      fetch("/api/dak/my-desk?page=0&pageSize=1", { credentials: "include" })
        .then((res) => (res.ok ? res.json() : null))
        .then((data) => {
          if (active && data?.summary) {
            setCounts((prev) => ({
              ...prev,
              deskCount: data.summary.total ?? data.totalCount
            }));
          }
        })
        .catch(() => {});
    }

    // Fetch My Work summary
    if (hasPermission("WorkItem.View")) {
      fetch("/api/work-items/my-work?page=0&pageSize=1", { credentials: "include" })
        .then((res) => (res.ok ? res.json() : null))
        .then((data) => {
          if (active && data?.summary) {
            setCounts((prev) => ({
              ...prev,
              workCount: data.summary.pendingCount ?? data.summary.totalCount
            }));
          }
        })
        .catch(() => {});
    }

    // Fetch My Attention summary
    if (hasPermission("Schedule.View")) {
      fetch("/api/attention/my-attention?page=1&pageSize=1", { credentials: "include" })
        .then((res) => (res.ok ? res.json() : null))
        .then((data) => {
          if (active && data?.summary) {
            setCounts((prev) => ({
              ...prev,
              attentionCount: data.summary.total ?? data.summary.overdue
            }));
          }
        })
        .catch(() => {});
    }

    return () => {
      active = false;
    };
  }, [hasPermission]);

  return (
    <div className="home-container">
      {/* 1. Executive Banner */}
      <section className="home-executive-banner">
        <div className="home-banner-main">
          <div className="home-banner-eyebrow">
            <span className="home-date-pill">{todayFormatted}</span>
          </div>
          <h1 className="home-banner-title">
            {greetingTime}, {userDisplayName}
          </h1>
          <p className="home-banner-subtitle">
            Welcome to the Land Acquisition Cell digital working environment. Review your daily work queue, correspondence desk, land records, and court matters below.
          </p>
        </div>

        <div className="home-banner-meta">
          <div className="home-meta-badge">
            <span className="home-status-dot" />
            <span>{designationName}</span>
          </div>
          <div className="home-meta-badge" style={{ fontSize: "11px", opacity: 0.85 }}>
            LAC District Portal · Active
          </div>
        </div>
      </section>

      {/* 2. Quick Action Triggers */}
      <section className="home-quick-actions">
        {hasPermission("Dak.Register") && (
          <Link to="/dak/register" className="home-action-btn home-action-btn-primary">
            <IconPlus size={16} />
            <span>Register Inward Dak</span>
          </Link>
        )}
        {hasPermission("WorkItem.Create") && (
          <Link to="/work/new" className="home-action-btn">
            <IconPlus size={16} />
            <span>Create Work Item</span>
          </Link>
        )}
        {hasPermission("Award.Edit") && (
          <Link to="/awards/import-pdf" className="home-action-btn">
            <IconAward size={16} />
            <span>Import Award PDF</span>
          </Link>
        )}
        <Link to="/land-records" className="home-action-btn">
          <IconSearch size={16} />
          <span>Explore Land Records Hierarchy</span>
        </Link>
      </section>

      {/* 3. Your Work Today (Daily Operational Surfaces) */}
      <section>
        <div className="home-section-header">
          <div>
            <h2>Your Work Today</h2>
            <p>Operational queues and immediate action items requiring your attention.</p>
          </div>
        </div>

        <div className="home-daily-grid">
          {hasPermission("Dak.View") && (
            <Link to="/my-desk" className="home-work-card tone-amber">
              <div className="home-work-card-top">
                <div className="home-work-icon-wrap">
                  <IconDesk size={22} />
                </div>
                {counts.deskCount !== undefined && (
                  <span className="home-work-count-badge">{counts.deskCount}</span>
                )}
              </div>
              <div className="home-work-card-body">
                <h3 className="home-work-card-title">My Desk</h3>
                <p className="home-work-card-desc">
                  Incoming official dak, physical files, and correspondence assigned to your desk.
                </p>
                <div className="home-work-card-action">
                  <span>Open Desk Queue</span>
                  <IconArrowRight size={14} />
                </div>
              </div>
            </Link>
          )}

          {(hasPermission("WorkItem.View") || hasPermission("WorkItem.Create")) && (
            <Link to="/my-work" className="home-work-card">
              <div className="home-work-card-top">
                <div className="home-work-icon-wrap">
                  <IconWorkItem size={22} />
                </div>
                {counts.workCount !== undefined && (
                  <span className="home-work-count-badge">{counts.workCount}</span>
                )}
              </div>
              <div className="home-work-card-body">
                <h3 className="home-work-card-title">My Work</h3>
                <p className="home-work-card-desc">
                  Active work items, assigned tasks, and document drafting files.
                </p>
                <div className="home-work-card-action">
                  <span>Open Work Items</span>
                  <IconArrowRight size={14} />
                </div>
              </div>
            </Link>
          )}

          {hasPermission("Schedule.View") && (
            <Link to="/my-attention" className="home-work-card tone-emerald">
              <div className="home-work-card-top">
                <div className="home-work-icon-wrap">
                  <IconAttention size={22} />
                </div>
                {counts.attentionCount !== undefined && (
                  <span className="home-work-count-badge">{counts.attentionCount}</span>
                )}
              </div>
              <div className="home-work-card-body">
                <h3 className="home-work-card-title">Needs Attention</h3>
                <p className="home-work-card-desc">
                  Urgent deadlines, pending reviews, and items requiring immediate decision.
                </p>
                <div className="home-work-card-action">
                  <span>View Attention Items</span>
                  <IconArrowRight size={14} />
                </div>
              </div>
            </Link>
          )}

          {hasPermission("Schedule.View") && (
            <Link to="/calendar" className="home-work-card">
              <div className="home-work-card-top">
                <div className="home-work-icon-wrap">
                  <IconCalendar size={22} />
                </div>
              </div>
              <div className="home-work-card-body">
                <h3 className="home-work-card-title">Calendar & Schedule</h3>
                <p className="home-work-card-desc">
                  Upcoming court hearings, office meetings, and scheduled deadlines.
                </p>
                <div className="home-work-card-action">
                  <span>Open Calendar</span>
                  <IconArrowRight size={14} />
                </div>
              </div>
            </Link>
          )}
        </div>
      </section>

      {/* 4. Office Module Hub */}
      <section>
        <div className="home-section-header">
          <div>
            <h2>Office Modules Hub</h2>
            <p>Access core domain capability centers and departmental workspace modules.</p>
          </div>
        </div>

        <div className="home-modules-grid">
          {/* Module 1: Land Records */}
          <div className="home-module-card">
            <div className="home-module-header">
              <div className="home-module-icon">
                <IconLand size={22} />
              </div>
              <div className="home-module-header-text">
                <h3 className="home-module-title">Land Records</h3>
                <p className="home-module-desc">
                  Villages, sub-divisions, Khasra numbers, Awards, and Khatauni registers.
                </p>
              </div>
            </div>

            <div className="home-module-links">
              <Link to="/land-records" className="home-module-sublink">
                <span>Administrative Hierarchy</span>
                <IconChevronRight size={14} />
              </Link>
              <Link to="/villages" className="home-module-sublink">
                <span>Village Directory</span>
                <IconChevronRight size={14} />
              </Link>
              <Link to="/awards" className="home-module-sublink">
                <span>Awards Register</span>
                <IconChevronRight size={14} />
              </Link>
              <Link to="/imports/lr" className="home-module-sublink">
                <span>LR Import Registers</span>
                <IconChevronRight size={14} />
              </Link>
            </div>
          </div>

          {/* Module 2: Matters & Case Files */}
          {(hasPermission("Matter.View") || hasPermission("Matter.Create")) && (
            <div className="home-module-card">
              <div className="home-module-header">
                <div className="home-module-icon">
                  <IconMatters size={22} />
                </div>
                <div className="home-module-header-text">
                  <h3 className="home-module-title">Matters & Files</h3>
                  <p className="home-module-desc">
                    Acquisition matters, note sheets, draft opinions, and office file tracking.
                  </p>
                </div>
              </div>

              <div className="home-module-links">
                <Link to="/matters" className="home-module-sublink">
                  <span>Matters Directory</span>
                  <IconChevronRight size={14} />
                </Link>
              </div>
            </div>
          )}

          {/* Module 3: Correspondence */}
          {(hasPermission("Dak.View") || hasPermission("Outward.View")) && (
            <div className="home-module-card">
              <div className="home-module-header">
                <div className="home-module-icon">
                  <IconDak size={22} />
                </div>
                <div className="home-module-header-text">
                  <h3 className="home-module-title">Correspondence</h3>
                  <p className="home-module-desc">
                    Inward Dak registration, physical file receipts, desk movement, and dispatch.
                  </p>
                </div>
              </div>

              <div className="home-module-links">
                {hasPermission("Dak.View") && (
                  <Link to="/dak" className="home-module-sublink">
                    <span>Dak / Inward Register</span>
                    <IconChevronRight size={14} />
                  </Link>
                )}
                {hasPermission("Dak.Register") && (
                  <Link to="/dak/register" className="home-module-sublink">
                    <span>Register Inward Dak</span>
                    <IconChevronRight size={14} />
                  </Link>
                )}
                {hasPermission("Outward.View") && (
                  <Link to="/outward" className="home-module-sublink">
                    <span>Outward Dispatch</span>
                    <IconChevronRight size={14} />
                  </Link>
                )}
              </div>
            </div>
          )}

          {/* Module 4: Court & Litigation */}
          {(hasPermission("Court.View") || hasPermission("Court.Create")) && (
            <div className="home-module-card">
              <div className="home-module-header">
                <div className="home-module-icon">
                  <IconCourt size={22} />
                </div>
                <div className="home-module-header-text">
                  <h3 className="home-module-title">Court & Litigation</h3>
                  <p className="home-module-desc">
                    Court cases, Section 18/28A references, hearing dates, and order records.
                  </p>
                </div>
              </div>

              <div className="home-module-links">
                <Link to="/court-cases" className="home-module-sublink">
                  <span>Court Case Directory</span>
                  <IconChevronRight size={14} />
                </Link>
              </div>
            </div>
          )}

          {/* Module 5: Oversight & Analytics */}
          {(hasPermission("WorkItem.View") || hasPermission("Audit.View")) && (
            <div className="home-module-card">
              <div className="home-module-header">
                <div className="home-module-icon">
                  <IconPulse size={22} />
                </div>
                <div className="home-module-header-text">
                  <h3 className="home-module-title">Branch Oversight</h3>
                  <p className="home-module-desc">
                    Branch pulse monitoring, handler workloads, personal activity, and team audit logs.
                  </p>
                </div>
              </div>

              <div className="home-module-links">
                {hasPermission("WorkItem.View") && (
                  <Link to="/branch-pulse" className="home-module-sublink">
                    <span>Branch Pulse Monitor</span>
                    <IconChevronRight size={14} />
                  </Link>
                )}
                <Link to="/my-history" className="home-module-sublink">
                  <span>My Activity History</span>
                  <IconChevronRight size={14} />
                </Link>
                {hasPermission("Audit.View") && (
                  <Link to="/team-activity" className="home-module-sublink">
                    <span>Team Activity Log</span>
                    <IconChevronRight size={14} />
                  </Link>
                )}
              </div>
            </div>
          )}

          {/* Module 6: Administration */}
          {(hasPermission("Users.Manage") ||
            hasPermission("Access.Manage") ||
            hasPermission("Audit.View")) && (
            <div className="home-module-card">
              <div className="home-module-header">
                <div className="home-module-icon">
                  <IconShield size={22} />
                </div>
                <div className="home-module-header-text">
                  <h3 className="home-module-title">Administration</h3>
                  <p className="home-module-desc">
                    User account management, designation setup, RBAC security, and audit trail.
                  </p>
                </div>
              </div>

              <div className="home-module-links">
                {hasPermission("Users.Manage") && (
                  <Link to="/admin/users" className="home-module-sublink">
                    <span>User Management</span>
                    <IconChevronRight size={14} />
                  </Link>
                )}
                {hasPermission("Access.Manage") && (
                  <Link to="/admin/access" className="home-module-sublink">
                    <span>Access & Roles</span>
                    <IconChevronRight size={14} />
                  </Link>
                )}
                {hasPermission("Audit.View") && (
                  <Link to="/admin/audit-logs" className="home-module-sublink">
                    <span>System Audit Trail</span>
                    <IconChevronRight size={14} />
                  </Link>
                )}
              </div>
            </div>
          )}
        </div>
      </section>
    </div>
  );
};
