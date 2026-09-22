import React, { useState, useEffect, useCallback } from "react";
import { useParams, Link, useNavigate } from "react-router-dom";
import type { CourtCaseDetailDto, CourtFilterOptionsDto } from "./types";
import { CourtOverviewTab } from "./CourtOverviewTab";
import { CourtProceedingsTab } from "./CourtProceedingsTab";
import { CourtDocumentsTab } from "./CourtDocumentsTab";
import { CourtLinkedRecordsTab } from "./CourtLinkedRecordsTab";
import { CourtWorkTab } from "./CourtWorkTab";
import { CourtTimelineTab } from "./CourtTimelineTab";
import { useAuth } from "../auth/AuthProvider";
import "./court.css";

export const CourtCaseWorkspace: React.FC = () => {
  const { id } = useParams<{ id: string }>();
  const navigate = useNavigate();
  const { hasPermission } = useAuth();

  const [courtCase, setCourtCase] = useState<CourtCaseDetailDto | null>(null);
  const [filterOptions, setFilterOptions] = useState<CourtFilterOptionsDto | null>(null);
  const [activeTab, setActiveTab] = useState<"overview" | "proceedings" | "documents" | "records" | "work" | "timeline">("overview");
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  // Modals
  const [showEditModal, setShowEditModal] = useState(false);
  const [showReassignModal, setShowReassignModal] = useState(false);
  const [submitting, setSubmitting] = useState(false);
  const [modalError, setModalError] = useState<string | null>(null);

  // Edit form state
  const [editTitle, setEditTitle] = useState("");
  const [editType, setEditType] = useState("");
  const [editStatus, setEditStatus] = useState("");
  const [editFiledDate, setEditFiledDate] = useState("");
  const [editDisposedDate, setEditDisposedDate] = useState("");
  const [editRemarks, setEditRemarks] = useState("");

  // Reassign form state
  const [reassignDeskId, setReassignDeskId] = useState("");
  const [reassignUserId, setReassignUserId] = useState("");
  const [reassignNotes, setReassignNotes] = useState("");

  const canEdit = courtCase?.capabilities?.canEdit ?? false;
  const canAssign = courtCase?.capabilities?.canAssign ?? false;

  const fetchCaseDetail = useCallback(async () => {
    if (!id) return;
    try {
      setLoading(true);
      const res = await fetch(`/api/court-cases/${id}`, { credentials: "include" });
      if (res.ok) {
        const data: CourtCaseDetailDto = await res.json();
        setCourtCase(data);
      } else if (res.status === 404) {
        setError("Court Case not found");
      } else if (res.status === 403) {
        setError("You do not have permission to view this Court Case.");
      } else {
        setError("Failed to load Court Case details.");
      }
    } catch {
      setError("Network error loading Court Case");
    } finally {
      setLoading(false);
    }
  }, [id]);

  useEffect(() => {
    void fetchCaseDetail();
    // Load filter options for desk/user dropdowns
    void fetch("/api/court-cases/filter-options", { credentials: "include" })
      .then((r) => (r.ok ? r.json() : null))
      .then((data) => setFilterOptions(data))
      .catch(() => {});
  }, [fetchCaseDetail]);

  const openEditModal = () => {
    if (!courtCase) return;
    setEditTitle(courtCase.caseTitle || "");
    setEditType(courtCase.caseType || "");
    setEditStatus(courtCase.currentStatus || "");
    setEditFiledDate(courtCase.filedDate || "");
    setEditDisposedDate(courtCase.disposedDate || "");
    setEditRemarks(courtCase.remarks || "");
    setModalError(null);
    setShowEditModal(true);
  };

  const handleUpdateMetadata = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!courtCase) return;
    setModalError(null);
    try {
      setSubmitting(true);
      const res = await fetch(`/api/court-cases/${courtCase.id}`, {
        method: "PUT",
        headers: { "Content-Type": "application/json" },
        credentials: "include",
        body: JSON.stringify({
          caseTitle: editTitle || null,
          caseType: editType || null,
          currentStatus: editStatus || null,
          filedDate: editFiledDate || null,
          disposedDate: editDisposedDate || null,
          remarks: editRemarks || null,
          expectedRevision: courtCase.revision,
        }),
      });

      if (res.ok) {
        setShowEditModal(false);
        await fetchCaseDetail();
      } else {
        const err = await res.json().catch(() => ({ error: "Update failed" }));
        setModalError(err.error || "Update failed");
      }
    } catch {
      setModalError("Network error updating case metadata");
    } finally {
      setSubmitting(false);
    }
  };

  const openReassignModal = () => {
    if (!courtCase) return;
    setReassignDeskId(courtCase.responsibleOfficeDeskId || "");
    setReassignUserId(courtCase.assignedUserId || "");
    setReassignNotes("");
    setModalError(null);
    setShowReassignModal(true);
  };

  const handleReassign = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!courtCase) return;
    setModalError(null);
    try {
      setSubmitting(true);
      const res = await fetch(`/api/court-cases/${courtCase.id}/reassign`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        credentials: "include",
        body: JSON.stringify({
          targetResponsibleDeskId: reassignDeskId || null,
          targetAssignedUserId: reassignUserId || null,
          reassignmentNotes: reassignNotes || null,
          expectedRevision: courtCase.revision,
        }),
      });

      if (res.ok) {
        setShowReassignModal(false);
        await fetchCaseDetail();
      } else {
        const err = await res.json().catch(() => ({ error: "Reassignment failed" }));
        setModalError(err.error || "Reassignment failed");
      }
    } catch {
      setModalError("Network error during case reassignment");
    } finally {
      setSubmitting(false);
    }
  };

  if (loading) {
    return <div style={{ padding: "40px", textAlign: "center", color: "#64748b" }}>Loading Court Case Workspace...</div>;
  }

  if (error || !courtCase) {
    return (
      <div style={{ padding: "40px", textAlign: "center" }}>
        <div style={{ color: "#dc2626", fontSize: "16px", marginBottom: "16px" }}>{error || "Case not found"}</div>
        <button className="secondary-button" onClick={() => navigate("/court-cases")}>← Back to Court Directory</button>
      </div>
    );
  }

  return (
    <div className="court-workspace-container" style={{ padding: "24px", maxWidth: "1400px", margin: "0 auto" }}>
      {/* Breadcrumb navigation */}
      <div style={{ marginBottom: "16px", fontSize: "14px", color: "#64748b" }}>
        <Link to="/court-cases" style={{ color: "#2563eb", textDecoration: "none" }}>Court Directory</Link>
        {" / "}
        <span style={{ color: "#0f172a", fontWeight: 600 }}>{courtCase.caseNumber}</span>
      </div>

      {/* Workspace Header */}
      <div className="court-workspace-header">
        <div className="court-workspace-header-top">
          <div>
            <h1 className="court-case-number-heading">
              <span>🏛 {courtCase.caseNumber}</span>
              {courtCase.currentStatus ? (
                <span
                  className={`court-badge ${
                    courtCase.currentStatus.toLowerCase() === "disposed"
                      ? "court-badge-status-disposed"
                      : courtCase.currentStatus.toLowerCase() === "stay"
                      ? "court-badge-status-stay"
                      : "court-badge-status-pending"
                  }`}
                >
                  {courtCase.currentStatus}
                </span>
              ) : (
                <span style={{ color: "#94a3b8", fontSize: "13px" }}>—</span>
              )}
              {courtCase.isProjectedToCalendar ? (
                <span className="court-badge" style={{ background: "#dcfce7", color: "#166534", fontSize: "11px" }}>
                  ● On Calendar
                </span>
              ) : (
                <span className="court-badge" style={{ background: "#f1f5f9", color: "#64748b", fontSize: "11px" }}>
                  ○ Not on Calendar
                </span>
              )}
              <span className="court-badge court-badge-ndoh" style={{ fontSize: "11px" }}>
                Rev {courtCase.revision}
              </span>
            </h1>
            <p className="court-case-title-sub">
              {courtCase.courtName} {courtCase.caseTitle ? `— ${courtCase.caseTitle}` : ""}
            </p>
          </div>

          <div className="court-header-actions">
            {canEdit && (
              <button className="secondary-button" onClick={openEditModal}>
                ✎ Edit Metadata
              </button>
            )}
            {canAssign && (
              <button className="secondary-button" onClick={openReassignModal}>
                👥 Reassign
              </button>
            )}
          </div>
        </div>

        <div className="court-metadata-grid">
          <div className="court-meta-item">
            <span className="court-meta-label">Court / Forum</span>
            <span className="court-meta-value">{courtCase.courtName}</span>
          </div>
          <div className="court-meta-item">
            <span className="court-meta-label">Responsible Desk</span>
            <span className="court-meta-value">{courtCase.responsibleOfficeDeskName || "Unassigned"}</span>
          </div>
          <div className="court-meta-item">
            <span className="court-meta-label">Assigned Officer</span>
            <span className="court-meta-value">{courtCase.assignedUserDisplayName || "Unassigned"}</span>
          </div>
          <div className="court-meta-item">
            <span className="court-meta-label">Next Date (NDOH)</span>
            <span className="court-meta-value" style={{ color: (courtCase.authoritativeNextDate || courtCase.activeScheduleNextDate || courtCase.nextHearingDate) ? "#4338ca" : "#64748b", fontWeight: 700 }}>
              {courtCase.authoritativeNextDate || courtCase.activeScheduleNextDate || courtCase.nextHearingDate || "None Scheduled"}
            </span>
          </div>
          <div className="court-meta-item">
            <span className="court-meta-label">Filed Date</span>
            <span className="court-meta-value">{courtCase.filedDate || "—"}</span>
          </div>
        </div>
      </div>

      {/* Tabs */}
      <div className="court-tabs">
        <button
          className={`court-tab-button ${activeTab === "overview" ? "active" : ""}`}
          onClick={() => setActiveTab("overview")}
        >
          Overview
        </button>
        <button
          className={`court-tab-button ${activeTab === "proceedings" ? "active" : ""}`}
          onClick={() => setActiveTab("proceedings")}
        >
          Proceedings & Orders ({courtCase.proceedingsCount ?? courtCase.proceedingCount ?? 0})
        </button>
        <button
          className={`court-tab-button ${activeTab === "documents" ? "active" : ""}`}
          onClick={() => setActiveTab("documents")}
        >
          Documents ({courtCase.documentsCount ?? courtCase.documentCount ?? 0})
        </button>
        <button
          className={`court-tab-button ${activeTab === "records" ? "active" : ""}`}
          onClick={() => setActiveTab("records")}
        >
          Linked Records ({(courtCase.awardsCount ?? courtCase.awards.length) + (courtCase.khasrasCount ?? courtCase.khasras.length) + courtCase.parties.length})
        </button>
        <button
          className={`court-tab-button ${activeTab === "work" ? "active" : ""}`}
          onClick={() => setActiveTab("work")}
        >
          Connected Work ({courtCase.mattersCount ?? courtCase.matters.length})
        </button>
        <button
          className={`court-tab-button ${activeTab === "timeline" ? "active" : ""}`}
          onClick={() => setActiveTab("timeline")}
        >
          Audit History
        </button>
      </div>

      {/* Tab Contents */}
      <div className="court-tab-content">
        {activeTab === "overview" && <CourtOverviewTab courtCase={courtCase} />}
        {activeTab === "proceedings" && <CourtProceedingsTab courtCase={courtCase} onRefresh={fetchCaseDetail} />}
        {activeTab === "documents" && <CourtDocumentsTab courtCase={courtCase} onRefresh={fetchCaseDetail} />}
        {activeTab === "records" && <CourtLinkedRecordsTab courtCase={courtCase} onRefresh={fetchCaseDetail} />}
        {activeTab === "work" && <CourtWorkTab courtCase={courtCase} onRefresh={fetchCaseDetail} />}
        {activeTab === "timeline" && <CourtTimelineTab courtCase={courtCase} />}
      </div>

      {/* Edit Metadata Modal */}
      {showEditModal && (
        <div className="court-modal-backdrop" onClick={() => setShowEditModal(false)}>
          <div className="court-modal" onClick={(e) => e.stopPropagation()}>
            <div className="court-modal-header">
              <h3>Edit Case Metadata</h3>
              <button className="quiet-button" onClick={() => setShowEditModal(false)}>✕</button>
            </div>
            <form onSubmit={handleUpdateMetadata}>
              <div className="court-modal-body">
                {modalError && <div style={{ color: "#b91c1c", background: "#fef2f2", padding: "10px", borderRadius: "6px" }}>{modalError}</div>}
                <div>
                  <label style={{ display: "block", fontSize: "13px", fontWeight: 600, color: "#334155", marginBottom: "4px" }}>Case Title</label>
                  <input type="text" className="form-input" style={{ width: "100%" }} value={editTitle} onChange={(e) => setEditTitle(e.target.value)} />
                </div>
                <div>
                  <label style={{ display: "block", fontSize: "13px", fontWeight: 600, color: "#334155", marginBottom: "4px" }}>Case Type</label>
                  <input type="text" className="form-input" style={{ width: "100%" }} placeholder="e.g. Writ Petition (Civil), Appeal, Contempt" value={editType} onChange={(e) => setEditType(e.target.value)} />
                </div>
                <div>
                  <label style={{ display: "block", fontSize: "13px", fontWeight: 600, color: "#334155", marginBottom: "4px" }}>Status</label>
                  <select className="form-input" style={{ width: "100%" }} value={editStatus} onChange={(e) => setEditStatus(e.target.value)}>
                    <option value="">— Not Specified —</option>
                    <option value="Pending">Pending</option>
                    <option value="Disposed">Disposed</option>
                    <option value="Stay Granted">Stay Granted</option>
                    <option value="Interim Order Active">Interim Order Active</option>
                    <option value="Dismissed">Dismissed</option>
                    <option value="Transferred">Transferred</option>
                  </select>
                </div>
                <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: "12px" }}>
                  <div>
                    <label style={{ display: "block", fontSize: "13px", fontWeight: 600, color: "#334155", marginBottom: "4px" }}>Filed Date</label>
                    <input type="date" className="form-input" style={{ width: "100%" }} value={editFiledDate} onChange={(e) => setEditFiledDate(e.target.value)} />
                  </div>
                  <div>
                    <label style={{ display: "block", fontSize: "13px", fontWeight: 600, color: "#334155", marginBottom: "4px" }}>Disposed Date</label>
                    <input type="date" className="form-input" style={{ width: "100%" }} value={editDisposedDate} onChange={(e) => setEditDisposedDate(e.target.value)} />
                  </div>
                </div>
                <div>
                  <label style={{ display: "block", fontSize: "13px", fontWeight: 600, color: "#334155", marginBottom: "4px" }}>Remarks</label>
                  <textarea rows={3} className="form-input" style={{ width: "100%" }} value={editRemarks} onChange={(e) => setEditRemarks(e.target.value)} />
                </div>
              </div>
              <div className="court-modal-footer">
                <button type="button" className="secondary-button" onClick={() => setShowEditModal(false)} disabled={submitting}>Cancel</button>
                <button type="submit" className="primary-button" disabled={submitting}>{submitting ? "Saving..." : "Save Changes"}</button>
              </div>
            </form>
          </div>
        </div>
      )}

      {/* Reassign Modal */}
      {showReassignModal && (
        <div className="court-modal-backdrop" onClick={() => setShowReassignModal(false)}>
          <div className="court-modal" onClick={(e) => e.stopPropagation()}>
            <div className="court-modal-header">
              <h3>Reassign Court Case</h3>
              <button className="quiet-button" onClick={() => setShowReassignModal(false)}>✕</button>
            </div>
            <form onSubmit={handleReassign}>
              <div className="court-modal-body">
                {modalError && <div style={{ color: "#b91c1c", background: "#fef2f2", padding: "10px", borderRadius: "6px" }}>{modalError}</div>}
                <div>
                  <label style={{ display: "block", fontSize: "13px", fontWeight: 600, color: "#334155", marginBottom: "4px" }}>Responsible Office Desk</label>
                  <select className="form-input" style={{ width: "100%" }} value={reassignDeskId} onChange={(e) => setReassignDeskId(e.target.value)}>
                    <option value="">Unassigned</option>
                    {filterOptions?.desks.map((d) => (
                      <option key={d.id} value={d.id}>
                        {d.name} {d.workstreamName ? `(${d.workstreamName})` : ""}
                      </option>
                    ))}
                  </select>
                </div>
                <div>
                  <label style={{ display: "block", fontSize: "13px", fontWeight: 600, color: "#334155", marginBottom: "4px" }}>Assigned Officer</label>
                  <select className="form-input" style={{ width: "100%" }} value={reassignUserId} onChange={(e) => setReassignUserId(e.target.value)}>
                    <option value="">Unassigned</option>
                    {(filterOptions?.officers || filterOptions?.assignedUsers || []).map((u) => (
                      <option key={u.id} value={u.id}>
                        {u.displayName || u.name}
                      </option>
                    ))}
                  </select>
                </div>
                <div>
                  <label style={{ display: "block", fontSize: "13px", fontWeight: 600, color: "#334155", marginBottom: "4px" }}>Reassignment Notes</label>
                  <textarea rows={3} className="form-input" style={{ width: "100%" }} placeholder="Reason or instructions for reassignment..." value={reassignNotes} onChange={(e) => setReassignNotes(e.target.value)} />
                </div>
              </div>
              <div className="court-modal-footer">
                <button type="button" className="secondary-button" onClick={() => setShowReassignModal(false)} disabled={submitting}>Cancel</button>
                <button type="submit" className="primary-button" disabled={submitting}>{submitting ? "Reassigning..." : "Confirm Reassignment"}</button>
              </div>
            </form>
          </div>
        </div>
      )}
    </div>
  );
};
