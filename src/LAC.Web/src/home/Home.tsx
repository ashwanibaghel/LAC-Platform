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
  IconChevronRight,
  IconArrowRight,
  IconAward
} from "../components/Icons";
import "./home.css";

interface OperationalCounts {
  deskCount?: number;
  workCount?: number;
  attentionCount?: number;
}

export const Home: React.FC = () => {
  const { user, hasPermission } = useAuth();
  const [counts, setCounts] = useState<OperationalCounts>({});

  const userDisplayName = user?.displayName || user?.username || "Officer";
  const designationName = user?.designation?.name || "Additional District Magistrate";

  // Formatted date string
  const todayFormatted = new Intl.DateTimeFormat("en-GB", {
    weekday: "long",
    day: "2-digit",
    month: "long",
    year: "numeric"
  }).format(new Date());

  // Greeting time
  const currentHour = new Date().getHours();
  const greetingTime =
    currentHour < 12 ? "Good morning" : currentHour < 17 ? "Good afternoon" : "Good evening";

  // Fetch summary counts asynchronously using exact authoritative endpoints & keys
  useEffect(() => {
    let active = true;

    // 1. Fetch My Desk summary
    if (hasPermission("Dak.View")) {
      fetch("/api/dak/my-desk?page=0&pageSize=1", { credentials: "include" })
        .then((res) => (res.ok ? res.json() : null))
        .then((data) => {
          if (active && data) {
            setCounts((prev) => ({
              ...prev,
              deskCount: data.summary?.total ?? data.totalCount ?? 0
            }));
          }
        })
        .catch(() => {});
    }

    // 2. Fetch My Work summary (using authoritative summary.totalOpen)
    if (hasPermission("WorkItem.View")) {
      fetch("/api/work-items/my-work?page=0&pageSize=1", { credentials: "include" })
        .then((res) => (res.ok ? res.json() : null))
        .then((data) => {
          if (active && data?.summary) {
            setCounts((prev) => ({
              ...prev,
              workCount: data.summary.totalOpen ?? data.summary.pendingCount ?? 0
            }));
          }
        })
        .catch(() => {});
    }

    // 3. Fetch Needs Attention summary (using authoritative /api/attention/my)
    if (hasPermission("Schedule.View") || hasPermission("WorkItem.View") || hasPermission("Dak.View")) {
      fetch("/api/attention/my?page=1&pageSize=1", { credentials: "include" })
        .then((res) => (res.ok ? res.json() : null))
        .then((data) => {
          if (active && data?.summary) {
            setCounts((prev) => ({
              ...prev,
              attentionCount: data.summary.total ?? data.summary.overdue ?? 0
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
    <div className="home-container-compact">
      {/* 1. Compact Header Area */}
      <div className="home-compact-header">
        <div>
          <h1 className="home-compact-greeting">
            {greetingTime}, {userDisplayName}
          </h1>
          <p className="home-compact-sub">
            {todayFormatted} &middot; {designationName}
          </p>
        </div>

        {/* Quick Actions Strip */}
        <div className="home-actions-strip">
          {hasPermission("Dak.Register") && (
            <Link to="/dak/register" className="home-compact-btn home-compact-btn-primary">
              <IconPlus size={14} />
              <span>Register Inward Dak</span>
            </Link>
          )}
          {hasPermission("WorkItem.Create") && (
            <Link to="/work/new" className="home-compact-btn">
              <IconPlus size={14} />
              <span>Create Work Item</span>
            </Link>
          )}
          {hasPermission("Award.Edit") && (
            <Link to="/awards/import-pdf" className="home-compact-btn">
              <IconAward size={14} />
              <span>Import Award PDF</span>
            </Link>
          )}
        </div>
      </div>

      {/* 2. My Day - Compact Horizontal Row */}
      <section>
        <div className="home-block-header">
          <h2>My Day</h2>
        </div>

        <div className="home-myday-strip">
          {hasPermission("Dak.View") && (
            <Link to="/my-desk" className="home-myday-card tone-amber">
              <div className="home-myday-left">
                <div className="home-myday-icon">
                  <IconDesk size={18} />
                </div>
                <div className="home-myday-info">
                  <span className="home-myday-title">My Desk</span>
                  <span className="home-myday-sub">Physical correspondence</span>
                </div>
              </div>
              {counts.deskCount !== undefined && (
                <span className="home-myday-count">{counts.deskCount}</span>
              )}
            </Link>
          )}

          {(hasPermission("WorkItem.View") || hasPermission("WorkItem.Create")) && (
            <Link to="/my-work" className="home-myday-card">
              <div className="home-myday-left">
                <div className="home-myday-icon">
                  <IconWorkItem size={18} />
                </div>
                <div className="home-myday-info">
                  <span className="home-myday-title">My Work</span>
                  <span className="home-myday-sub">Active tasks & drafts</span>
                </div>
              </div>
              {counts.workCount !== undefined && (
                <span className="home-myday-count">{counts.workCount}</span>
              )}
            </Link>
          )}

          {(hasPermission("Schedule.View") || hasPermission("WorkItem.View") || hasPermission("Dak.View")) && (
            <Link to="/my-attention" className="home-myday-card tone-emerald">
              <div className="home-myday-left">
                <div className="home-myday-icon">
                  <IconAttention size={18} />
                </div>
                <div className="home-myday-info">
                  <span className="home-myday-title">Needs Attention</span>
                  <span className="home-myday-sub">Urgent & overdue items</span>
                </div>
              </div>
              {counts.attentionCount !== undefined && (
                <span className="home-myday-count">{counts.attentionCount}</span>
              )}
            </Link>
          )}

          {hasPermission("Schedule.View") && (
            <Link to="/calendar" className="home-myday-card">
              <div className="home-myday-left">
                <div className="home-myday-icon">
                  <IconCalendar size={18} />
                </div>
                <div className="home-myday-info">
                  <span className="home-myday-title">Calendar</span>
                  <span className="home-myday-sub">Hearings & schedule</span>
                </div>
              </div>
              <IconArrowRight size={14} className="text-slate-400" />
            </Link>
          )}
        </div>
      </section>

      {/* 3. Office Modules Hub - Dense Grid */}
      <section>
        <div className="home-block-header">
          <h2>Office Modules</h2>
        </div>

        <div className="home-dense-modules-grid">
          {/* Module 1: Land Records */}
          <div className="home-dense-card">
            <div className="home-dense-card-head">
              <div className="home-dense-icon">
                <IconLand size={18} />
              </div>
              <div>
                <h3>Land Records</h3>
                <p>Villages, khasras, awards, and khatauni registers.</p>
              </div>
            </div>
            <div className="home-dense-links">
              <Link to="/land-records" className="home-dense-sublink">
                <span>Hierarchy</span>
                <IconChevronRight size={11} />
              </Link>
              <Link to="/villages" className="home-dense-sublink">
                <span>Villages</span>
                <IconChevronRight size={11} />
              </Link>
              <Link to="/awards" className="home-dense-sublink">
                <span>Awards</span>
                <IconChevronRight size={11} />
              </Link>
              <Link to="/imports/lr" className="home-dense-sublink">
                <span>LR Imports</span>
                <IconChevronRight size={11} />
              </Link>
            </div>
          </div>

          {/* Module 2: Matters & Files */}
          {(hasPermission("Matter.View") || hasPermission("Matter.Create")) && (
            <div className="home-dense-card">
              <div className="home-dense-card-head">
                <div className="home-dense-icon">
                  <IconMatters size={18} />
                </div>
                <div>
                  <h3>Matters & Files</h3>
                  <p>Acquisition matters, draft note sheets, and opinions.</p>
                </div>
              </div>
              <div className="home-dense-links">
                <Link to="/matters" className="home-dense-sublink">
                  <span>Matters Directory</span>
                  <IconChevronRight size={11} />
                </Link>
              </div>
            </div>
          )}

          {/* Module 3: Correspondence */}
          {(hasPermission("Dak.View") || hasPermission("Outward.View")) && (
            <div className="home-dense-card">
              <div className="home-dense-card-head">
                <div className="home-dense-icon">
                  <IconDak size={18} />
                </div>
                <div>
                  <h3>Correspondence</h3>
                  <p>Inward dak, desk movement, and outward dispatch.</p>
                </div>
              </div>
              <div className="home-dense-links">
                {hasPermission("Dak.View") && (
                  <Link to="/dak" className="home-dense-sublink">
                    <span>Inward Dak</span>
                    <IconChevronRight size={11} />
                  </Link>
                )}
                {hasPermission("Dak.Register") && (
                  <Link to="/dak/register" className="home-dense-sublink">
                    <span>Register Dak</span>
                    <IconChevronRight size={11} />
                  </Link>
                )}
                {hasPermission("Outward.View") && (
                  <Link to="/outward" className="home-dense-sublink">
                    <span>Dispatch</span>
                    <IconChevronRight size={11} />
                  </Link>
                )}
              </div>
            </div>
          )}

          {/* Module 4: Court & Litigation */}
          {(hasPermission("Court.View") || hasPermission("Court.Create")) && (
            <div className="home-dense-card">
              <div className="home-dense-card-head">
                <div className="home-dense-icon">
                  <IconCourt size={18} />
                </div>
                <div>
                  <h3>Court & Litigation</h3>
                  <p>Court cases, hearing dates, and Section 18 references.</p>
                </div>
              </div>
              <div className="home-dense-links">
                <Link to="/court-cases" className="home-dense-sublink">
                  <span>Court Directory</span>
                  <IconChevronRight size={11} />
                </Link>
              </div>
            </div>
          )}

          {/* Module 5: Branch Oversight */}
          {(hasPermission("WorkItem.View") || hasPermission("Audit.View")) && (
            <div className="home-dense-card">
              <div className="home-dense-card-head">
                <div className="home-dense-icon">
                  <IconPulse size={18} />
                </div>
                <div>
                  <h3>Branch Oversight</h3>
                  <p>Branch pulse, desk workloads, and team activity logs.</p>
                </div>
              </div>
              <div className="home-dense-links">
                {hasPermission("WorkItem.View") && (
                  <Link to="/branch-pulse" className="home-dense-sublink">
                    <span>Branch Pulse</span>
                    <IconChevronRight size={11} />
                  </Link>
                )}
                <Link to="/my-history" className="home-dense-sublink">
                  <span>My History</span>
                  <IconChevronRight size={11} />
                </Link>
                {hasPermission("Audit.View") && (
                  <Link to="/team-activity" className="home-dense-sublink">
                    <span>Team Activity</span>
                    <IconChevronRight size={11} />
                  </Link>
                )}
              </div>
            </div>
          )}

          {/* Module 6: Administration */}
          {(hasPermission("Users.Manage") ||
            hasPermission("Access.Manage") ||
            hasPermission("Audit.View")) && (
            <div className="home-dense-card">
              <div className="home-dense-card-head">
                <div className="home-dense-icon">
                  <IconShield size={18} />
                </div>
                <div>
                  <h3>Administration</h3>
                  <p>User provisioning, access roles, and system audit trail.</p>
                </div>
              </div>
              <div className="home-dense-links">
                {hasPermission("Users.Manage") && (
                  <Link to="/admin/users" className="home-dense-sublink">
                    <span>Users</span>
                    <IconChevronRight size={11} />
                  </Link>
                )}
                {hasPermission("Access.Manage") && (
                  <Link to="/admin/access" className="home-dense-sublink">
                    <span>Access & Roles</span>
                    <IconChevronRight size={11} />
                  </Link>
                )}
                {hasPermission("Audit.View") && (
                  <Link to="/admin/audit-logs" className="home-dense-sublink">
                    <span>Audit Trail</span>
                    <IconChevronRight size={11} />
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
