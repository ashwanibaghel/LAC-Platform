import React, { useState, useEffect, useCallback } from "react";
import { Link, useNavigate } from "react-router-dom";
import type {
  CourtCaseListItemDto,
  CourtCaseListResponse,
  CourtFilterOptionsDto,
} from "./types";
import { useAuth } from "../auth/AuthProvider";
import "./court.css";

export const CourtDirectory: React.FC = () => {
  const navigate = useNavigate();
  const { hasPermission } = useAuth();
  const canCreate = hasPermission("Court.Create");

  const [items, setItems] = useState<CourtCaseListItemDto[]>([]);
  const [totalCount, setTotalCount] = useState(0);
  const [page, setPage] = useState(1);
  const [pageSize] = useState(20);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  // Filters
  const [searchTerm, setSearchTerm] = useState("");
  const [selectedCourt, setSelectedCourt] = useState("");
  const [selectedStatus, setSelectedStatus] = useState("");
  const [selectedDeskId, setSelectedDeskId] = useState("");
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

  // Fetch court cases
  const fetchCases = useCallback(async () => {
    try {
      setLoading(true);
      setError(null);
      const params = new URLSearchParams();
      params.set("page", page.toString());
      params.set("pageSize", pageSize.toString());
      if (searchTerm.trim()) params.set("search", searchTerm.trim());
      if (selectedCourt) params.set("courtName", selectedCourt);
      if (selectedStatus) params.set("currentStatus", selectedStatus);
      if (selectedDeskId) params.set("deskId", selectedDeskId);

      const res = await fetch(`/api/court-cases?${params.toString()}`, { credentials: "include" });
      if (res.ok) {
        const data: CourtCaseListResponse = await res.json();
        setItems(data.items);
        setTotalCount(data.totalCount);
      } else {
        setError("Failed to load court cases");
      }
    } catch {
      setError("Network error loading court cases");
    } finally {
      setLoading(false);
    }
  }, [page, pageSize, searchTerm, selectedCourt, selectedStatus, selectedDeskId]);

  useEffect(() => {
    void fetchCases();
  }, [fetchCases]);

  const handleSearchSubmit = (e: React.FormEvent) => {
    e.preventDefault();
    setPage(1);
    void fetchCases();
  };

  const handleResetFilters = () => {
    setSearchTerm("");
    setSelectedCourt("");
    setSelectedStatus("");
    setSelectedDeskId("");
    setPage(1);
  };

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
            🏛 Court & Litigation Workspace
          </h1>
          <p style={{ margin: "4px 0 0 0", color: "#64748b", fontSize: "14px" }}>
            Comprehensive directory of pending writ petitions, land acquisition references, appeals, and court proceedings.
          </p>
        </div>

        {canCreate && (
          <button className="primary-button" onClick={() => { setCreateError(null); setShowNewModal(true); }}>
            + New Court Case
          </button>
        )}
      </div>

      {/* Filter Bar */}
      <form onSubmit={handleSearchSubmit} className="court-filters-bar">
        <input
          type="text"
          className="form-input court-search-input"
          placeholder="Search by case number, title, or forum..."
          value={searchTerm}
          onChange={(e) => setSearchTerm(e.target.value)}
        />

        <select
          className="form-input court-filter-select"
          value={selectedCourt}
          onChange={(e) => { setSelectedCourt(e.target.value); setPage(1); }}
        >
          <option value="">All Courts / Forums</option>
          {filterOptions?.courtNames.map((cn) => (
            <option key={cn} value={cn}>{cn}</option>
          ))}
        </select>

        <select
          className="form-input court-filter-select"
          value={selectedStatus}
          onChange={(e) => { setSelectedStatus(e.target.value); setPage(1); }}
        >
          <option value="">All Statuses</option>
          {filterOptions?.statuses.map((st) => (
            <option key={st} value={st}>{st}</option>
          ))}
        </select>

        <select
          className="form-input court-filter-select"
          value={selectedDeskId}
          onChange={(e) => { setSelectedDeskId(e.target.value); setPage(1); }}
        >
          <option value="">All Desks</option>
          {(filterOptions?.viewDesks ?? filterOptions?.desks ?? []).map((d) => (
            <option key={d.id} value={d.id}>{d.name} ({d.workstreamName})</option>
          ))}
        </select>

        <button type="submit" className="secondary-button">
          Filter
        </button>

        {(searchTerm || selectedCourt || selectedStatus || selectedDeskId) && (
          <button type="button" className="quiet-button" onClick={handleResetFilters}>
            Reset
          </button>
        )}
      </form>

      {error && <div style={{ color: "#dc2626", marginBottom: "16px" }}>{error}</div>}

      {/* Results Table */}
      <div style={{ background: "#fff", border: "1px solid #e2e8f0", borderRadius: "8px", overflow: "hidden" }}>
        <table className="data-table" style={{ width: "100%", borderCollapse: "collapse" }}>
          <thead>
            <tr style={{ background: "#f8fafc", borderBottom: "1px solid #e2e8f0", textAlign: "left" }}>
              <th style={{ padding: "12px 16px" }}>Case Details</th>
              <th style={{ padding: "12px 16px" }}>Court / Forum</th>
              <th style={{ padding: "12px 16px" }}>Next Hearing Date (NDOH)</th>
              <th style={{ padding: "12px 16px" }}>Status</th>
              <th style={{ padding: "12px 16px" }}>Desk / Officer</th>
              <th style={{ padding: "12px 16px" }}>Connections</th>
              <th style={{ padding: "12px 16px", textAlign: "right" }}>Actions</th>
            </tr>
          </thead>
          <tbody>
            {loading ? (
              <tr>
                <td colSpan={7} style={{ padding: "32px", textAlign: "center", color: "#64748b" }}>
                  Loading cases...
                </td>
              </tr>
            ) : items.length === 0 ? (
              <tr>
                <td colSpan={7} style={{ padding: "40px", textAlign: "center", color: "#64748b" }}>
                  No court cases found matching the criteria.
                </td>
              </tr>
            ) : (
              items.map((c) => (
                <tr key={c.id} style={{ borderBottom: "1px solid #f1f5f9" }}>
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
                    {c.authoritativeNextDate || c.activeScheduleNextDate || c.nextHearingDate ? (
                      <div style={{ display: "flex", flexDirection: "column", gap: "2px" }}>
                        <span className="court-badge court-badge-ndoh">
                          📅 {c.authoritativeNextDate || c.activeScheduleNextDate || c.nextHearingDate}
                        </span>
                        {c.isProjectedToCalendar ? (
                          <span style={{ fontSize: "11px", color: "#16a34a", fontWeight: 600 }}>● On Calendar</span>
                        ) : (
                          <span style={{ fontSize: "11px", color: "#94a3b8" }}>○ Not on Calendar</span>
                        )}
                      </div>
                    ) : (
                      <span style={{ color: "#94a3b8", fontSize: "13px" }}>—</span>
                    )}
                  </td>

                  <td style={{ padding: "12px 16px" }}>
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

                  <td style={{ padding: "12px 16px", fontSize: "12px", color: "#64748b" }}>
                    <div>Awards: {c.awardsCount !== null && c.awardsCount !== undefined ? c.awardsCount : "—"}</div>
                    <div>Matters: {c.mattersCount !== null && c.mattersCount !== undefined ? c.mattersCount : "—"}</div>
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
              onClick={() => setPage((p) => p - 1)}
            >
              Previous
            </button>
            <span style={{ display: "flex", alignItems: "center", padding: "0 8px", fontSize: "14px" }}>
              Page {page} of {totalPages}
            </span>
            <button
              className="secondary-button"
              disabled={page >= totalPages}
              onClick={() => setPage((p) => p + 1)}
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
