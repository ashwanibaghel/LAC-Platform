import React, { useState, useEffect } from "react";
import { Link, useNavigate, useSearchParams } from "react-router-dom";
import type {
  CourtCaseListItemDto,
  CourtCaseListResponse,
  CourtFilterOptionsDto,
} from "./types";
import { useAuth } from "../auth/AuthProvider";
import { DhcSyncPanel } from "./DhcSyncPanel";
import "./court.css";

export const CourtDirectory: React.FC = () => {
  const navigate = useNavigate();
  const { hasPermission } = useAuth();
  const canCreate = hasPermission("Court.Create");
  const [urlParams, setUrlParams] = useSearchParams();
  const page = Math.max(1, Number(urlParams.get("page") || "1") || 1);
  const pageSize = 20;
  const value = (name: string) => urlParams.get(name) || "";
  const selected = (name: string) => value(name).split(",").filter(Boolean);
  const setFilter = (name: string, next: string) => {
    const params = new URLSearchParams(urlParams);
    if (next.trim()) params.set(name, next.trim()); else params.delete(name);
    params.delete("page");
    setUrlParams(params, { replace: ["search", "caseNumber", "advocate", "village", "award", "directions", "briefFacts"].includes(name) });
  };
  const setPage = (next: number) => {
    const params = new URLSearchParams(urlParams);
    if (next <= 1) params.delete("page"); else params.set("page", String(next));
    setUrlParams(params);
  };
  const setQuick = (quick: string) => {
    const params = new URLSearchParams(urlParams);
    params.delete("page");
    if (quick === "Disposed") {
      params.delete("ndohFilter");
      params.set("statuses", "Disposed");
    } else {
      params.delete("statuses");
      if (quick) params.set("ndohFilter", quick); else params.delete("ndohFilter");
    }
    setUrlParams(params);
  };

  const [items, setItems] = useState<CourtCaseListItemDto[]>([]);
  const [totalCount, setTotalCount] = useState(0);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [filterOptions, setFilterOptions] = useState<CourtFilterOptionsDto | null>(null);

  // New Case Modal
  const [showNewModal, setShowNewModal] = useState(false);
  const [newCaseNumber, setNewCaseNumber] = useState("");
  const [newCourtName, setNewCourtName] = useState("");
  const [newCaseTitle, setNewCaseTitle] = useState("");
  const [newCaseType, setNewCaseType] = useState("");
  const [newCurrentStatus, setNewCurrentStatus] = useState("");
  const [newFiledDate, setNewFiledDate] = useState("");
  const [newDeskId, setNewDeskId] = useState("");
  const [newUserId, setNewUserId] = useState("");
  const [newRemarks, setNewRemarks] = useState("");
  const [submitting, setSubmitting] = useState(false);
  const [createError, setCreateError] = useState<string | null>(null);

  // Fetch filter options
  useEffect(() => {
    void fetch("/api/court-cases/filter-options", { credentials: "include" })
      .then((res) => (res.ok ? res.json() : null))
      .then((data: CourtFilterOptionsDto | null) => {
        if (data) setFilterOptions(data);
      })
      .catch(() => {});
  }, []);

  useEffect(() => {
    const controller = new AbortController();
    const timer = window.setTimeout(async () => {
      try {
        setLoading(true);
        setError(null);
        const params = new URLSearchParams(urlParams);
        params.set("pageSize", String(pageSize));
        const res = await fetch(`/api/court-cases?${params.toString()}`, { credentials: "include", signal: controller.signal });
        if (!res.ok) throw new Error("Failed to load court cases");
        const data: CourtCaseListResponse = await res.json();
        setItems(data.items);
        setTotalCount(data.totalCount);
      } catch (reason) {
        if (!controller.signal.aborted) setError(reason instanceof Error ? reason.message : "Network error loading court cases");
      } finally {
        if (!controller.signal.aborted) setLoading(false);
      }
    }, 200);
    return () => { window.clearTimeout(timer); controller.abort(); };
  }, [urlParams, pageSize]);

  const handleCreateCase = async (e: React.FormEvent) => {
    e.preventDefault();
    setCreateError(null);

    if (!newCaseNumber.trim() || !newCourtName.trim()) {
      setCreateError("Case Number and Court Name are required.");
      return;
    }

    try {
      setSubmitting(true);
      const res = await fetch("/api/court-cases", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        credentials: "include",
        body: JSON.stringify({
          caseNumber: newCaseNumber.trim(),
          courtName: newCourtName.trim(),
          caseTitle: newCaseTitle.trim() || null,
          caseType: newCaseType.trim() || null,
          currentStatus: newCurrentStatus || null,
          filedDate: newFiledDate || null,
          responsibleOfficeDeskId: newDeskId || null,
          assignedUserId: newUserId || null,
          remarks: newRemarks.trim() || null,
        }),
      });

      if (res.ok) {
        const result = await res.json();
        setShowNewModal(false);
        navigate(`/court-cases/${result.id}`);
      } else {
        const err = await res.json().catch(() => ({ error: "Failed to create court case" }));
        setCreateError(err.error || "Failed to create court case");
      }
    } catch {
      setCreateError("Network error creating court case");
    } finally {
      setSubmitting(false);
    }
  };

  const totalPages = Math.ceil(totalCount / pageSize);

  return (
    <div className="court-directory-container" style={{ padding: "24px", maxWidth: "1400px", margin: "0 auto" }}>
      {/* Directory Header */}
      <div className="court-directory-header">
        <div>
          <h1 style={{ margin: 0, fontSize: "28px", fontWeight: 700, color: "#0f172a" }}>
            Court operational queue
          </h1>
          <p style={{ margin: "4px 0 0 0", color: "#64748b", fontSize: "14px" }}>
            Pending listings first, then overdue and undated matters. Court proceedings and accepted official DHC listings drive NDOH—not Calendar dates.
          </p>
        </div>

        {canCreate && (<div style={{ display: "flex", gap: "8px" }}><Link className="secondary-button" to="/court-cases/imports">Import Excel</Link><button className="primary-button" onClick={() => { setCreateError(null); setShowNewModal(true); }}>+ New Court Case</button></div>)}
      </div>

      <DhcSyncPanel />

      <div style={{ display: "flex", gap: 8, flexWrap: "wrap", margin: "18px 0 12px" }} aria-label="Court queue quick filters">
        {[["", "All queue"], ["Today", "Today"], ["Upcoming", "Upcoming"], ["Overdue", "Overdue"], ["NoNdoh", "No NDOH"], ["Disposed", "Disposed"]].map(([key, label]) =>
          <button key={key || "all"} type="button" className={(key === "Disposed" ? value("statuses") === "Disposed" && !value("ndohFilter") : value("ndohFilter") === key && (key !== "" || !value("statuses"))) ? "primary-button" : "secondary-button"}
            onClick={() => setQuick(key)}>{label}</button>)}
      </div>
      <div className="court-filters-bar" style={{ display: "grid", gridTemplateColumns: "repeat(auto-fit,minmax(180px,1fr))", gap: 10, alignItems: "end" }}>
        <label>Search all
          <input className="form-input" type="search" placeholder="Case, advocate, source text…" value={value("search")} onChange={e => setFilter("search", e.target.value)} />
        </label>
        <label>NDOH
          <select className="form-input" value={value("ndohFilter")} onChange={e => setFilter("ndohFilter", e.target.value)}>
            {[ ["", "All"], ["Today", "Today"], ["Tomorrow", "Tomorrow"], ["Next7Days", "Next 7 days"], ["ThisWeek", "This week"], ["ThisMonth", "This month"], ["Upcoming", "Upcoming"], ["Overdue", "Overdue"], ["NoNdoh", "No NDOH"], ["CustomRange", "Custom range"] ].map(([key,label]) => <option key={key} value={key}>{label}</option>)}
          </select>
        </label>
        {value("ndohFilter") === "CustomRange" && <>
          <label>NDOH from<input className="form-input" type="date" value={value("ndohFrom")} onChange={e => setFilter("ndohFrom", e.target.value)} /></label>
          <label>NDOH to<input className="form-input" type="date" value={value("ndohTo")} onChange={e => setFilter("ndohTo", e.target.value)} /></label>
        </>}
        <label>Status (Ctrl-click for multiple)
          <select multiple size={Math.min(4, Math.max(2, filterOptions?.statuses.length || 2))} className="form-input" value={selected("statuses")}
            onChange={e => setFilter("statuses", Array.from(e.target.selectedOptions, option => option.value).join(","))}>
            {filterOptions?.statuses.map(st => <option key={st} value={st}>{st}</option>)}
          </select>
        </label>
        <label>Court (Ctrl-click for multiple)
          <select multiple size={Math.min(4, Math.max(2, filterOptions?.courtNames.length || 2))} className="form-input" value={selected("courtNames")}
            onChange={e => setFilter("courtNames", Array.from(e.target.selectedOptions, option => option.value).join(","))}>
            {filterOptions?.courtNames.map(cn => <option key={cn} value={cn}>{cn}</option>)}
          </select>
        </label>
        <label>Case type<select className="form-input" value={value("caseType")} onChange={e => setFilter("caseType", e.target.value)}>
          <option value="">All types</option>{filterOptions?.caseTypes?.map(t => <option key={t} value={t}>{t}</option>)}
        </select></label>
        <label>Case number<input className="form-input" value={value("caseNumber")} onChange={e => setFilter("caseNumber", e.target.value)} placeholder="e.g. 3352/2024" /></label>
        <label>Advocate<input className="form-input" value={value("advocate")} onChange={e => setFilter("advocate", e.target.value)} /></label>
        <label>Village (linked or source)<input className="form-input" value={value("village")} onChange={e => setFilter("village", e.target.value)} /></label>
        <label>Award (linked or source)<input className="form-input" value={value("award")} onChange={e => setFilter("award", e.target.value)} /></label>
        <label>Source directions<input className="form-input" value={value("directions")} onChange={e => setFilter("directions", e.target.value)} /></label>
        <label>Source brief facts<input className="form-input" value={value("briefFacts")} onChange={e => setFilter("briefFacts", e.target.value)} /></label>
        <label>Last-order source link<select className="form-input" value={value("sourceOrderLinkState")} onChange={e => setFilter("sourceOrderLinkState", e.target.value)}>
          <option value="">Any</option><option value="ValidHttpUrl">Valid source URL</option><option value="Missing">Missing</option><option value="NeedsReview">Needs review</option>
        </select></label>
        <label>Responsible desk<select className="form-input" value={value("deskId")} onChange={e => setFilter("deskId", e.target.value)}>
          <option value="">All desks</option>{(filterOptions?.viewDesks ?? filterOptions?.desks ?? []).map(d => <option key={d.id} value={d.id}>{d.name}</option>)}
        </select></label>
        <label>Assigned officer<select className="form-input" value={value("assignedUserId")} onChange={e => setFilter("assignedUserId", e.target.value)}>
          <option value="">All officers</option>{(filterOptions?.directoryOfficers ?? []).map(u => <option key={u.id} value={u.id}>{u.name}</option>)}
        </select></label>
        <button type="button" className="quiet-button" onClick={() => setUrlParams(new URLSearchParams())}>Clear filters</button>
      </div>

      {error && <div style={{ color: "#dc2626", marginBottom: "16px" }}>{error}</div>}

      {/* Results Table */}
      <div style={{ background: "#fff", border: "1px solid #e2e8f0", borderRadius: "8px", overflow: "hidden" }}>
        <table className="data-table" style={{ width: "100%", borderCollapse: "collapse" }}>
          <thead>
            <tr style={{ background: "#f8fafc", borderBottom: "1px solid #e2e8f0", textAlign: "left" }}>
              <th style={{ padding: "12px 16px" }}>Case Details</th>
              <th style={{ padding: "12px 16px" }}>Court / Forum</th>
              <th style={{ padding: "12px 16px" }}>Operational NDOH</th>
              <th style={{ padding: "12px 16px" }}>Queue / Status</th>
              <th style={{ padding: "12px 16px" }}>Advocate</th>
              <th style={{ padding: "12px 16px" }}>Village / Award</th>
              <th style={{ padding: "12px 16px" }}>Desk / Officer</th>
              <th style={{ padding: "12px 16px", textAlign: "right" }}>Actions</th>
            </tr>
          </thead>
          <tbody>
            {loading ? (
              <tr>
                <td colSpan={8} style={{ padding: "32px", textAlign: "center", color: "#64748b" }}>
                  Loading cases...
                </td>
              </tr>
            ) : items.length === 0 ? (
              <tr>
                <td colSpan={8} style={{ padding: "40px", textAlign: "center", color: "#64748b" }}>
                  No court cases found matching the criteria.
                </td>
              </tr>
            ) : (
              items.map((c) => (
                <tr key={c.id} style={{ borderBottom: "1px solid #f1f5f9", opacity: c.queueState === "Disposed" ? 0.7 : 1 }}>
                  <td style={{ padding: "12px 16px" }}>
                    <Link
                      to={`/court-cases/${c.id}`}
                      style={{ fontWeight: 700, color: "#2563eb", textDecoration: "none", fontSize: "15px" }}
                    >
                      {c.caseNumber}
                    </Link>
                    {c.caseTitle && (
                      <div style={{ fontSize: "13px", color: "#475569", marginTop: "2px" }}>
                        {c.caseTitle}
                      </div>
                    )}
                    {c.caseType && (
                      <div style={{ fontSize: "11px", color: "#94a3b8", marginTop: "2px" }}>
                        Type: {c.caseType}
                      </div>
                    )}
                  </td>

                  <td style={{ padding: "12px 16px", fontSize: "14px", color: "#334155" }}>
                    {c.courtName}
                  </td>

                  <td style={{ padding: "12px 16px" }}>
                    {c.operationalNdoh ? (
                      <div style={{ display: "flex", flexDirection: "column", gap: "2px" }}>
                        <span className="court-badge court-badge-ndoh">
                          {c.operationalNdoh}
                        </span>
                        {c.operationalNdohSource && <small style={{ color: "#64748b" }}>{c.operationalNdohSource === "DHC Cause List" ? "Official DHC cause list" : c.operationalNdohSource}</small>}
                        {c.daysFromToday !== null && c.queueState === "Overdue" && <small style={{ color: "#b45309" }}>{Math.abs(c.daysFromToday)} day(s) overdue</small>}
                      </div>
                    ) : (
                      <span style={{ color: "#94a3b8", fontSize: "13px" }}>—</span>
                    )}
                  </td>

                  <td style={{ padding: "12px 16px" }}>
                    <strong style={{ display: "block", color: c.queueState === "Overdue" ? "#b45309" : "#334155" }}>
                      {c.queueState === "NoNdoh" ? "No NDOH" : c.queueState}
                    </strong>
                    {c.currentStatus ? (
                      <span
                        className={`court-badge ${
                          c.currentStatus.toLowerCase() === "disposed"
                            ? "court-badge-status-disposed"
                            : c.currentStatus.toLowerCase() === "stay"
                            ? "court-badge-status-stay"
                            : "court-badge-status-pending"
                        }`}
                      >
                        {c.currentStatus}
                      </span>
                    ) : (
                      <span style={{ color: "#94a3b8", fontSize: "13px" }}>—</span>
                    )}
                  </td>

                  <td style={{ padding: "12px 16px", fontSize: "12px" }}>{c.advocates?.join(", ") || "—"}</td>
                  <td style={{ padding: "12px 16px", fontSize: "12px", color: "#64748b" }}>
                    {c.sourceVillage && <div>Source village: {c.sourceVillage}</div>}
                    {c.sourceAwardNumber && <div>Source award: {c.sourceAwardNumber}</div>}
                    {!c.sourceVillage && !c.sourceAwardNumber && <div>Linked awards: {c.awardsCount ?? "—"}</div>}
                  </td>

                  <td style={{ padding: "12px 16px", fontSize: "13px" }}>
                    <div style={{ color: "#334155", fontWeight: 500 }}>
                      {c.responsibleOfficeDeskName || "Unassigned"}
                    </div>
                    {c.assignedUserDisplayName && (
                      <div style={{ color: "#64748b", fontSize: "11px" }}>
                        {c.assignedUserDisplayName}
                      </div>
                    )}
                  </td>

                  <td style={{ padding: "12px 16px", textAlign: "right" }}>
                    <Link
                      to={`/court-cases/${c.id}`}
                      className="secondary-button"
                      style={{ textDecoration: "none", fontSize: "13px", padding: "6px 12px" }}
                    >
                      Open Case →
                    </Link>
                  </td>
                </tr>
              ))
            )}
          </tbody>
        </table>
      </div>

      {/* Pagination */}
      {totalPages > 1 && (
        <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center", marginTop: "16px" }}>
          <div style={{ fontSize: "13px", color: "#64748b" }}>
            Showing {items.length} of {totalCount} court cases
          </div>
          <div style={{ display: "flex", gap: "8px" }}>
            <button
              className="secondary-button"
              disabled={page <= 1}
              onClick={() => setPage(page - 1)}
            >
              Previous
            </button>
            <span style={{ display: "flex", alignItems: "center", padding: "0 8px", fontSize: "14px" }}>
              Page {page} of {totalPages}
            </span>
            <button
              className="secondary-button"
              disabled={page >= totalPages}
              onClick={() => setPage(page + 1)}
            >
              Next
            </button>
          </div>
        </div>
      )}

      {/* New Court Case Modal */}
      {showNewModal && (
        <div className="court-modal-backdrop" onClick={() => setShowNewModal(false)}>
          <div className="court-modal" onClick={(e) => e.stopPropagation()}>
            <div className="court-modal-header">
              <h3>Create Standalone Court Case</h3>
              <button className="quiet-button" onClick={() => setShowNewModal(false)}>✕</button>
            </div>
            <form onSubmit={handleCreateCase}>
              <div className="court-modal-body">
                {createError && (
                  <div style={{ color: "#b91c1c", background: "#fef2f2", padding: "10px", borderRadius: "6px" }}>
                    {createError}
                  </div>
                )}

                <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: "12px" }}>
                  <div>
                    <label style={{ display: "block", fontSize: "13px", fontWeight: 600, color: "#334155", marginBottom: "4px" }}>
                      Case Number *
                    </label>
                    <input
                      type="text"
                      required
                      className="form-input"
                      style={{ width: "100%" }}
                      placeholder="e.g. W.P.(C) 4120/2026"
                      value={newCaseNumber}
                      onChange={(e) => setNewCaseNumber(e.target.value)}
                    />
                  </div>

                  <div>
                    <label style={{ display: "block", fontSize: "13px", fontWeight: 600, color: "#334155", marginBottom: "4px" }}>
                      Court / Forum Name *
                    </label>
                    <input
                      type="text"
                      required
                      className="form-input"
                      style={{ width: "100%" }}
                      placeholder="e.g. High Court of Delhi"
                      value={newCourtName}
                      onChange={(e) => setNewCourtName(e.target.value)}
                    />
                  </div>
                </div>

                <div>
                  <label style={{ display: "block", fontSize: "13px", fontWeight: 600, color: "#334155", marginBottom: "4px" }}>
                    Case Title / Parties
                  </label>
                  <input
                    type="text"
                    className="form-input"
                    style={{ width: "100%" }}
                    placeholder="e.g. Ram Kumar & Ors. v. Union of India & LAC"
                    value={newCaseTitle}
                    onChange={(e) => setNewCaseTitle(e.target.value)}
                  />
                </div>

                <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: "12px" }}>
                  <div>
                    <label style={{ display: "block", fontSize: "13px", fontWeight: 600, color: "#334155", marginBottom: "4px" }}>
                      Case Type
                    </label>
                    <input
                      type="text"
                      className="form-input"
                      style={{ width: "100%" }}
                      placeholder="e.g. Writ Petition, Reference, Appeal"
                      value={newCaseType}
                      onChange={(e) => setNewCaseType(e.target.value)}
                    />
                  </div>

                  <div>
                    <label style={{ display: "block", fontSize: "13px", fontWeight: 600, color: "#334155", marginBottom: "4px" }}>
                      Current Status
                    </label>
                    <select
                      className="form-input"
                      style={{ width: "100%" }}
                      value={newCurrentStatus}
                      onChange={(e) => setNewCurrentStatus(e.target.value)}
                    >
                      <option value="">— Not Specified —</option>
                      <option value="Pending">Pending</option>
                      <option value="Stay Granted">Stay Granted</option>
                      <option value="Interim Order Active">Interim Order Active</option>
                      <option value="Disposed">Disposed</option>
                    </select>
                  </div>
                </div>

                <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: "12px" }}>
                  <div>
                    <label style={{ display: "block", fontSize: "13px", fontWeight: 600, color: "#334155", marginBottom: "4px" }}>
                      Filed Date
                    </label>
                    <input
                      type="date"
                      className="form-input"
                      style={{ width: "100%" }}
                      value={newFiledDate}
                      onChange={(e) => setNewFiledDate(e.target.value)}
                    />
                  </div>

                  <div>
                    <label style={{ display: "block", fontSize: "13px", fontWeight: 600, color: "#334155", marginBottom: "4px" }}>
                      Responsible Desk
                    </label>
                    <select
                      className="form-input"
                      style={{ width: "100%" }}
                      value={newDeskId}
                      onChange={(e) => setNewDeskId(e.target.value)}
                    >
                      <option value="">Unassigned</option>
                      {(filterOptions?.createDesks ?? filterOptions?.desks ?? []).map((d) => (
                        <option key={d.id} value={d.id}>{d.name} {d.workstreamName ? `(${d.workstreamName})` : ""}</option>
                      ))}
                    </select>
                  </div>
                </div>

                <div>
                  <label style={{ display: "block", fontSize: "13px", fontWeight: 600, color: "#334155", marginBottom: "4px" }}>
                    Assigned Officer
                  </label>
                  <select
                    className="form-input"
                    style={{ width: "100%" }}
                    value={newUserId}
                    onChange={(e) => setNewUserId(e.target.value)}
                  >
                    <option value="">Unassigned</option>
                    {(filterOptions?.officers || filterOptions?.assignedUsers || []).map((u) => (
                      <option key={u.id} value={u.id}>{u.displayName || u.name}</option>
                    ))}
                  </select>
                </div>

                <div>
                  <label style={{ display: "block", fontSize: "13px", fontWeight: 600, color: "#334155", marginBottom: "4px" }}>
                    Remarks / Background
                  </label>
                  <textarea
                    rows={3}
                    className="form-input"
                    style={{ width: "100%" }}
                    placeholder="Brief background of dispute or acquisition reference..."
                    value={newRemarks}
                    onChange={(e) => setNewRemarks(e.target.value)}
                  />
                </div>
              </div>

              <div className="court-modal-footer">
                <button type="button" className="secondary-button" onClick={() => setShowNewModal(false)} disabled={submitting}>
                  Cancel
                </button>
                <button type="submit" className="primary-button" disabled={submitting}>
                  {submitting ? "Creating..." : "Create Court Case"}
                </button>
              </div>
            </form>
          </div>
        </div>
      )}
    </div>
  );
};
