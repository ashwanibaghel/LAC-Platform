import React, { useEffect, useState } from "react";
import { Link } from "react-router-dom";
import { useAuth } from "../auth/AuthProvider";
import { useCalculator } from "../calculator/CalculatorContext";
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
  const { openCalculator } = useCalculator();
  const [counts, setCounts] = useState<OperationalCounts>({});

  const userDisplayName = user?.displayName || user?.username || "Officer";
  const designationName = user?.designation?.name || null;

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

  // Explicit Permission Helpers
  const canAccessMyWork = () => hasPermission("WorkItem.View") || hasPermission("WorkItem.Create");
  const canAccessAttention = () => hasPermission("Schedule.View") || hasPermission("WorkItem.View") || hasPermission("Dak.View");
  const canAccessCorrespondence = () => hasPermission("Dak.View") || hasPermission("Dak.Register") || hasPermission("Outward.View") || hasPermission("Outward.Create");
  const canAccessMatters = () => hasPermission("Matter.View") || hasPermission("Matter.Create");
  const canAccessCourt = () => hasPermission("Court.View") || hasPermission("Court.Create") || hasPermission("Award.View");
  const canAccessOversight = () => hasPermission("WorkItem.View") || hasPermission("Audit.View");
  const canAccessAdmin = () => hasPermission("Users.Manage") || hasPermission("Access.Manage") || hasPermission("Audit.View");

  // Dynamic Primary Destination Resolvers (Permission-Aware)
  const getCorrespondenceTarget = () => {
    if (hasPermission("Dak.View") || hasPermission("Dak.Register")) return "/dak";
    if (hasPermission("Outward.View") || hasPermission("Outward.Create")) return "/outward";
    return "/dak";
  };

  const getOversightTarget = () => {
    if (hasPermission("WorkItem.View")) return "/branch-pulse";
    if (hasPermission("Audit.View")) return "/team-activity";
    return "/branch-pulse";
  };

  const getAdminTarget = () => {
    if (hasPermission("Users.Manage")) return "/admin/users";
    if (hasPermission("Access.Manage")) return "/admin/access";
    if (hasPermission("Audit.View")) return "/admin/audit-logs";
    return "/admin/users";
  };

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
              deskCount: data.summary?.total ?? data.totalCount
            }));
          }
        })
        .catch(() => {});
    }

    // 2. Fetch My Work summary (using ONLY data.summary.totalOpen)
    if (canAccessMyWork()) {
      fetch("/api/work-items/my-work?page=0&pageSize=1", { credentials: "include" })
        .then((res) => (res.ok ? res.json() : null))
        .then((data) => {
          if (active && data?.summary?.totalOpen !== undefined) {
            setCounts((prev) => ({
              ...prev,
              workCount: data.summary.totalOpen
            }));
          }
        })
        .catch(() => {});
    }

    // 3. Fetch Needs Attention summary (using ONLY data.totalCount from /api/attention/my)
    if (canAccessAttention()) {
      fetch("/api/attention/my?page=1&pageSize=1", { credentials: "include" })
        .then((res) => (res.ok ? res.json() : null))
        .then((data) => {
          if (active && data?.totalCount !== undefined) {
            setCounts((prev) => ({
              ...prev,
              attentionCount: data.totalCount
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
            {todayFormatted}
            {designationName && <span> &middot; {designationName}</span>}
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

          {canAccessMyWork() && (
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

          {canAccessAttention() && (
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

      {/* 3. Office Modules Command Center Cards */}
      <section>
        <div className="home-block-header">
          <h2>Office Modules</h2>
        </div>

        <div className="home-dense-modules-grid">
          <button type="button" onClick={openCalculator} className="home-clean-card calc-home-card">
            <div className="home-clean-card-head"><div className="home-clean-icon">▦</div><div className="home-clean-info"><h3>Land &amp; Area Calculator</h3><p>Revenue conversions &amp; calculator.</p></div></div>
            <div className="home-clean-action"><span>Open</span><IconArrowRight size={14} /></div>
          </button>
          {/* Module 1: Land Records */}
          <Link to="/land-records" className="home-clean-card">
            <div className="home-clean-card-head">
              <div className="home-clean-icon">
                <IconLand size={18} />
              </div>
              <div className="home-clean-info">
                <h3>Land Records</h3>
                <p>Villages, khasras, awards, and khatauni registers.</p>
              </div>
            </div>
            <div className="home-clean-action">
              <span>Open Land Records</span>
              <IconArrowRight size={14} />
            </div>
          </Link>

          {/* Module 2: Matters & Files */}
          {canAccessMatters() && (
            <Link to="/matters" className="home-clean-card">
              <div className="home-clean-card-head">
                <div className="home-clean-icon">
                  <IconMatters size={18} />
                </div>
                <div className="home-clean-info">
                  <h3>Matters & Files</h3>
                  <p>Acquisition matters, draft note sheets, and opinions.</p>
                </div>
              </div>
              <div className="home-clean-action">
                <span>Open Matters</span>
                <IconArrowRight size={14} />
              </div>
            </Link>
          )}

          {/* Module 3: Correspondence (Dynamic Landing) */}
          {canAccessCorrespondence() && (
            <Link to={getCorrespondenceTarget()} className="home-clean-card">
              <div className="home-clean-card-head">
                <div className="home-clean-icon">
                  <IconDak size={18} />
                </div>
                <div className="home-clean-info">
                  <h3>Correspondence</h3>
                  <p>Inward dak, desk movement, and outward dispatch.</p>
                </div>
              </div>
              <div className="home-clean-action">
                <span>Open Correspondence</span>
                <IconArrowRight size={14} />
              </div>
            </Link>
          )}

          {/* Module 4: Court & Litigation */}
          {canAccessCourt() && (
            <Link to="/court-cases" className="home-clean-card">
              <div className="home-clean-card-head">
                <div className="home-clean-icon">
                  <IconCourt size={18} />
                </div>
                <div className="home-clean-info">
                  <h3>Court & Litigation</h3>
                  <p>Court cases, hearing dates, and Section 18 references.</p>
                </div>
              </div>
              <div className="home-clean-action">
                <span>Open Court Cases</span>
                <IconArrowRight size={14} />
              </div>
            </Link>
          )}

          {/* Module 5: Branch Oversight (Dynamic Landing) */}
          {canAccessOversight() && (
            <Link to={getOversightTarget()} className="home-clean-card">
              <div className="home-clean-card-head">
                <div className="home-clean-icon">
                  <IconPulse size={18} />
                </div>
                <div className="home-clean-info">
                  <h3>Branch Oversight</h3>
                  <p>Branch pulse, desk workloads, and team activity logs.</p>
                </div>
              </div>
              <div className="home-clean-action">
                <span>Open Oversight</span>
                <IconArrowRight size={14} />
              </div>
            </Link>
          )}

          {/* Module 6: Administration (Dynamic Landing) */}
          {canAccessAdmin() && (
            <Link to={getAdminTarget()} className="home-clean-card">
              <div className="home-clean-card-head">
                <div className="home-clean-icon">
                  <IconShield size={18} />
                </div>
                <div className="home-clean-info">
                  <h3>Administration</h3>
                  <p>User provisioning, access roles, and system audit trail.</p>
                </div>
              </div>
              <div className="home-clean-action">
                <span>Open Administration</span>
                <IconArrowRight size={14} />
              </div>
            </Link>
          )}
        </div>
      </section>
    </div>
  );
};
