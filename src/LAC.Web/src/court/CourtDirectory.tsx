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

const advancedFilterKeys = ["statuses", "caseType", "advocate", "village", "award", "directions",
  "briefFacts", "sourceOrderLinkState", "deskId", "assignedUserId", "ndohFrom", "ndohTo"];

const formatCourtDate = (date: string) => {
  const parsed = new Date(`${date.slice(0, 10)}T12:00:00`);
  return Number.isNaN(parsed.getTime()) ? date : new Intl.DateTimeFormat("en-IN", {
    day: "2-digit", month: "short", year: "numeric",
  }).format(parsed);
};

const CourtMultiSelect: React.FC<{ label: string; options: string[]; values: string[];
  onToggle: (option: string) => void }> = ({ label, options, values, onToggle }) => (
  <details className="court-multi-select">
    <summary aria-label={`${label} filter`}>{label}{values.length > 0 ? ` · ${values.length}` : ""}<span aria-hidden="true">⌄</span></summary>
    <div className="court-multi-options" role="group" aria-label={`${label} options`}>
      {options.length === 0 ? <small>No options available</small> : options.map(option =>
        <label key={option}><input type="checkbox" checked={values.includes(option)} onChange={() => onToggle(option)} />{option}</label>)}
    </div>
  </details>
);

export const CourtDirectory: React.FC = () => {
  const navigate = useNavigate();
  const { hasPermission } = useAuth();
  const canCreate = hasPermission("Court.Create");
  const [urlParams, setUrlParams] = useSearchParams();
  const [advancedOpen, setAdvancedOpen] = useState(() => advancedFilterKeys.some(key => !!urlParams.get(key)));
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
  const toggleSelected = (name: string, option: string) => {
    const current = selected(name);
    setFilter(name, current.includes(option) ? current.filter(item => item !== option).join(",") : [...current, option].join(","));
  };
  const clearFilters = () => { setUrlParams(new URLSearchParams()); setAdvancedOpen(false); };
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
  const [registerCount, setRegisterCount] = useState<number | null>(null);

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
    void fetch("/api/court-cases?pageSize=1", { credentials: "include" })
      .then(response => response.ok ? response.json() as Promise<CourtCaseListResponse> : null)
      .then(result => { if (result) setRegisterCount(result.totalCount); })
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
  const activeAdvancedCount = advancedFilterKeys.filter(key => !!value(key)).length;
  const hasFilters = Array.from(urlParams.keys()).some(key => key !== "page");
  const firstVisible = totalCount === 0 ? 0 : (page - 1) * pageSize + 1;
  const lastVisible = Math.min(page * pageSize, totalCount);

  return (
    <div className="court-directory-container">
      <div className={`court-directory-header ${registerCount === 0 ? "is-first-run" : ""}`}>
        <div className="court-header-title-block">
          <div className="court-header-eyebrow"><span className="court-pulse-dot" aria-hidden="true" /><span>LAC LITIGATION & JUDICIAL REGISTRY</span></div>
          <h1>Court Matters</h1>
          <p>Operational queue · NDOH · official court updates</p>
        </div>
        <div className="court-directory-actions">
          {registerCount === 0 ? <section className="court-first-run">
            <h2>No court register has been added yet</h2>
            <p>Import your existing LAC court Excel file to begin.</p>
            <div><Link className="primary-button" to="/court-cases/imports">Import Court Excel</Link>
              {canCreate && <button className="secondary-button" onClick={() => setShowNewModal(true)}>Add one case manually</button>}</div>
            <small>Delhi High Court automatic updates will start after DHC matters are imported.</small>
          </section> : <DhcSyncPanel />}
          {canCreate && registerCount !== 0 && <>
            <Link className="secondary-button court-pill-btn" to="/court-cases/imports"><span className="court-btn-icon" aria-hidden="true">📥</span> Import Excel</Link>
            <button className="primary-button court-pill-btn court-glow-btn" onClick={() => { setCreateError(null); setShowNewModal(true); }}>
              <span className="court-btn-icon" aria-hidden="true">+</span> New Court Case</button>
          </>}
        </div>
      </div>

      {registerCount !== 0 && <>

      <nav className="court-queue-nav" aria-label="Court queue quick filters">
        <div className="court-queue-tabs">
          {[["", "All"], ["Today", "Today"], ["Upcoming", "Upcoming"], ["Overdue", "Overdue"], ["NoNdoh", "No NDOH"], ["Disposed", "Disposed"]].map(([key, label]) =>
            <button key={key || "all"} type="button" aria-current={(key === "Disposed" ? value("statuses") === "Disposed" && !value("ndohFilter") : value("ndohFilter") === key && (key !== "" || !value("statuses"))) ? "page" : undefined}
              className={(key === "Disposed" ? value("statuses") === "Disposed" && !value("ndohFilter") : value("ndohFilter") === key && (key !== "" || !value("statuses"))) ? "active" : ""}
              onClick={() => setQuick(key)}>{label}</button>)}
        </div>
        <span className="court-queue-count">{totalCount.toLocaleString("en-IN")} matters</span>
      </nav>

      <section className="court-filter-shell" aria-label="Court case filters">
        <div className="court-primary-filters">
          <label className="court-search-field"><span className="sr-only">Search case, advocate, village or source text</span>
            <input className="form-input" type="search" placeholder="Search case, advocate, village…" value={value("search")} onChange={e => setFilter("search", e.target.value)} />
          </label>
          <label><span className="sr-only">NDOH filter</span>
            <select className="form-input" aria-label="NDOH filter" value={value("ndohFilter")} onChange={e => { setFilter("ndohFilter", e.target.value); if (e.target.value === "CustomRange") setAdvancedOpen(true); }}>
              {[ ["", "NDOH"], ["Today", "Today"], ["Tomorrow", "Tomorrow"], ["Next7Days", "Next 7 days"], ["ThisWeek", "This week"], ["ThisMonth", "This month"], ["Upcoming", "Upcoming"], ["Overdue", "Overdue"], ["NoNdoh", "No NDOH"], ["CustomRange", "Custom range"] ].map(([key,label]) => <option key={key} value={key}>{label}</option>)}
            </select>
          </label>
          <CourtMultiSelect label="Court" options={filterOptions?.courtNames ?? []} values={selected("courtNames")}
            onToggle={option => toggleSelected("courtNames", option)} />
          <label><span className="sr-only">Case number filter</span><input className="form-input" aria-label="Case number filter" value={value("caseNumber")}
            onChange={e => setFilter("caseNumber", e.target.value)} placeholder="Case number" /></label>
          <button type="button" className={`court-more-filters ${advancedOpen ? "active" : ""}`} aria-expanded={advancedOpen}
            aria-controls="court-advanced-filters" onClick={() => setAdvancedOpen(open => !open)}>
            More filters{activeAdvancedCount > 0 ? ` · ${activeAdvancedCount}` : ""}
          </button>
          <button type="button" className="court-clear-filters" onClick={clearFilters}>Clear</button>
        </div>
        {advancedOpen && <div className="court-advanced-filters" id="court-advanced-filters">
          <div className="court-advanced-field"><span>Status</span><CourtMultiSelect label="Status" options={filterOptions?.statuses ?? []}
            values={selected("statuses")} onToggle={option => toggleSelected("statuses", option)} /></div>
          <label>Case type<select className="form-input" value={value("caseType")} onChange={e => setFilter("caseType", e.target.value)}>
            <option value="">All types</option>{filterOptions?.caseTypes?.map(t => <option key={t} value={t}>{t}</option>)}
          </select></label>
          <label>Advocate<input className="form-input" value={value("advocate")} onChange={e => setFilter("advocate", e.target.value)} /></label>
          <label>Village<input className="form-input" value={value("village")} onChange={e => setFilter("village", e.target.value)} /></label>
          <label>Award<input className="form-input" value={value("award")} onChange={e => setFilter("award", e.target.value)} /></label>
          <label>Assigned officer<select className="form-input" value={value("assignedUserId")} onChange={e => setFilter("assignedUserId", e.target.value)}>
            <option value="">All officers</option>{(filterOptions?.directoryOfficers ?? []).map(u => <option key={u.id} value={u.id}>{u.name}</option>)}
          </select></label>
          <details className="court-additional-filters" open={Boolean(value("deskId") || value("sourceOrderLinkState") || value("directions") || value("briefFacts")) || undefined}>
            <summary>Additional filters</summary><div>
              <label>Responsible desk<select className="form-input" value={value("deskId")} onChange={e => setFilter("deskId", e.target.value)}>
                <option value="">All desks</option>{(filterOptions?.viewDesks ?? filterOptions?.desks ?? []).map(d => <option key={d.id} value={d.id}>{d.name}</option>)}
              </select></label>
              <label>Order link<select className="form-input" value={value("sourceOrderLinkState")} onChange={e => setFilter("sourceOrderLinkState", e.target.value)}>
                <option value="">Any</option><option value="ValidHttpUrl">Valid source URL</option><option value="Missing">Missing</option><option value="NeedsReview">Needs review</option>
              </select></label>
              <label>Source directions<input className="form-input" value={value("directions")} onChange={e => setFilter("directions", e.target.value)} /></label>
              <label>Brief facts<input className="form-input" value={value("briefFacts")} onChange={e => setFilter("briefFacts", e.target.value)} /></label>
            </div></details>
          {value("ndohFilter") === "CustomRange" && <>
            <label>NDOH from<input className="form-input" type="date" value={value("ndohFrom")} onChange={e => setFilter("ndohFrom", e.target.value)} /></label>
            <label>NDOH to<input className="form-input" type="date" value={value("ndohTo")} onChange={e => setFilter("ndohTo", e.target.value)} /></label>
          </>}
        </div>}
      </section>

      {error && <div className="court-filter-error" role="alert">{error}</div>}

      <section className="court-results" aria-label="Court matters results">
        <div className="court-results-heading"><h2>Court matters</h2><span>Showing {items.length} of {totalCount.toLocaleString("en-IN")}</span></div>
        <div className="court-table-scroll">
        <table className="court-matters-table">
          <thead>
            <tr>
              <th>Case No.</th><th>Case Title</th><th>Court</th><th>NDOH</th>
              <th>Advocate</th><th>Village / Award</th><th>Desk / Officer</th><th>Action</th>
            </tr>
          </thead>
          <tbody>
            {loading ? (
              <tr><td colSpan={8}><div className="court-loading-state">Updating court queue…</div></td></tr>
            ) : items.length === 0 ? (
              <tr><td colSpan={8}><div className="court-empty-state">
                <strong>{hasFilters ? "No matters match these filters" : "No court matters yet"}</strong>
                <p>{hasFilters ? "Try changing the queue or clearing filters." : "Import an Excel register or create the first court case."}</p>
                <div>{hasFilters ? <button className="secondary-button" onClick={clearFilters}>Clear filters</button> : canCreate && <>
                  <Link className="secondary-button" to="/court-cases/imports">Import Excel</Link>
                  <button className="primary-button" onClick={() => setShowNewModal(true)}>+ New Court Case</button>
                </>}</div>
              </div></td></tr>
            ) : (
              items.map((c) => (
                <tr key={c.id}>
                  <td className="court-case-cell">
                    <Link to={`/court-cases/${c.id}`} className="court-case-link">{c.caseNumber}</Link>
                    {c.caseType && <span className="court-case-type">{c.caseType}</span>}
                  </td>

                  <td className="court-title-cell">{c.caseTitle ? <span className="court-case-title-main">{c.caseTitle}</span> : <span className="court-muted">—</span>}</td>

                  <td className="court-muted-cell">{c.courtName}</td>

                  <td>
                    {c.operationalNdoh ? (
                      <div className="court-ndoh-cell">
                        <strong>{formatCourtDate(c.operationalNdoh)}</strong>
                        {c.operationalNdohSource && <small>{c.operationalNdohSource === "DHC Cause List" ? "DHC Cause List" : c.operationalNdohSource}</small>}
                        {c.daysFromToday !== null && c.queueState === "Overdue" && <small className="court-overdue-note">{Math.abs(c.daysFromToday)} days overdue</small>}
                      </div>
                    ) : <span className="court-muted">—</span>}
                  </td>

                  <td className="court-muted-cell">{c.advocates?.join(", ") || "—"}</td>
                  <td className="court-muted-cell court-desk-cell">
                    {c.sourceVillage && <div>{c.sourceVillage}</div>}
                    {c.sourceAwardNumber && <div className="court-secondary-line">Award {c.sourceAwardNumber}</div>}
                    {!c.sourceVillage && !c.sourceAwardNumber && <div className="court-secondary-line">{c.awardsCount ?? 0} linked awards</div>}
                  </td>

                  <td className="court-muted-cell">
                    <div>{c.responsibleOfficeDeskName || "Unassigned"}</div>
                    {c.assignedUserDisplayName && <div className="court-secondary-line">{c.assignedUserDisplayName}</div>}
                  </td>

                  <td><Link to={`/court-cases/${c.id}`} className="court-open-link" aria-label={`Open ${c.caseNumber}`}>Open →</Link></td>
                </tr>
              ))
            )}
          </tbody>
        </table>
        </div>
      </section>

      {/* Pagination */}
      {totalPages > 1 && (
        <div className="court-pagination">
          <div>Showing {firstVisible}–{lastVisible} of {totalCount.toLocaleString("en-IN")}</div>
          <div className="court-pagination-actions">
            <button
              className="secondary-button"
              disabled={page <= 1}
              onClick={() => setPage(page - 1)}
            >
              ‹ Previous
            </button>
            <span>
              Page {page} of {totalPages}
            </span>
            <button
              className="secondary-button"
              disabled={page >= totalPages}
              onClick={() => setPage(page + 1)}
            >
              Next ›
            </button>
          </div>
        </div>
      )}

      </>}

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
