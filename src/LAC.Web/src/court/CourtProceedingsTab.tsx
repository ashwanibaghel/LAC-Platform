import React, { useState, useEffect, useCallback } from "react";
import type { CourtCaseDetailDto, CourtProceedingDto } from "./types";

interface CourtProceedingsTabProps {
  courtCase: CourtCaseDetailDto;
  onRefresh: () => void;
}

export const CourtProceedingsTab: React.FC<CourtProceedingsTabProps> = ({ courtCase, onRefresh }) => {
  const canManage = courtCase?.capabilities?.canManageProceedings ?? false;

  const [proceedings, setProceedings] = useState<CourtProceedingDto[]>([]);
  const [loading, setLoading] = useState(true);
  const [showModal, setShowModal] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [promotingId, setPromotingId] = useState<string | null>(null);

  // Form state
  const [proceedingDate, setProceedingDate] = useState("");
  const [orderType, setOrderType] = useState("Order");
  const [restraintNature, setRestraintNature] = useState("None");
  const [summary, setSummary] = useState("");
  const [nextDate, setNextDate] = useState("");
  const [submitting, setSubmitting] = useState(false);
  const [validationError, setValidationError] = useState<string | null>(null);

  const fetchProceedings = useCallback(async () => {
    try {
      setLoading(true);
      const res = await fetch(`/api/court-cases/${courtCase.id}/proceedings`, { credentials: "include" });
      if (res.ok) {
        const data = await res.json();
        setProceedings(data);
      } else {
        setError("Failed to load proceedings");
      }
    } catch {
      setError("Network error loading proceedings");
    } finally {
      setLoading(false);
    }
  }, [courtCase.id]);

  useEffect(() => {
    void fetchProceedings();
  }, [fetchProceedings]);

  const handlePromote = async (proceedingId: string) => {
    try {
      setPromotingId(proceedingId);
      const res = await fetch(`/api/scheduled-events/from-court-proceeding/${proceedingId}`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        credentials: "include",
        body: JSON.stringify({
          title: `Court Hearing: ${courtCase.caseNumber}`,
          priority: "High",
        }),
      });

      if (res.ok) {
        await fetchProceedings();
        onRefresh();
      } else {
        const err = await res.json().catch(() => ({ error: "Failed to promote to calendar" }));
        alert(err.error || "Failed to promote to calendar");
      }
    } catch {
      alert("Network error promoting to calendar");
    } finally {
      setPromotingId(null);
    }
  };

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    setValidationError(null);

    if (!proceedingDate) {
      setValidationError("Proceeding Date is required.");
      return;
    }

    if (nextDate && proceedingDate && nextDate < proceedingDate) {
      setValidationError("Next Date of Hearing cannot be earlier than the Proceeding Date.");
      return;
    }

    try {
      setSubmitting(true);
      const res = await fetch(`/api/court-cases/${courtCase.id}/proceedings`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        credentials: "include",
        body: JSON.stringify({
          proceedingDate: proceedingDate || null,
          orderType: orderType || null,
          restraintNature: restraintNature === "None" ? null : restraintNature,
          summary: summary || null,
          nextDate: nextDate || null,
          expectedRevision: courtCase.revision,
        }),
      });

      if (res.ok) {
        setShowModal(false);
        setProceedingDate("");
        setOrderType("Order");
        setRestraintNature("None");
        setSummary("");
        setNextDate("");
        await fetchProceedings();
        onRefresh();
      } else {
        const err = await res.json().catch(() => ({ error: "Failed to record proceeding" }));
        setValidationError(err.error || "Failed to record proceeding");
      }
    } catch {
      setValidationError("Network error recording proceeding");
    } finally {
      setSubmitting(false);
    }
  };

  return (
    <div className="court-proceedings-tab">
      <div className="court-section-header">
        <h3>Proceedings & Orders ({proceedings.length})</h3>
        {canManage && (
          <button className="primary-button" onClick={() => setShowModal(true)}>
            + Record Proceeding
          </button>
        )}
      </div>

      {error && <div style={{ color: "#dc2626", marginBottom: "16px" }}>{error}</div>}

      {loading ? (
        <div style={{ padding: "32px", textAlign: "center", color: "#64748b" }}>Loading proceedings...</div>
      ) : proceedings.length === 0 ? (
        <div style={{ background: "#fff", border: "1px solid #e2e8f0", borderRadius: "8px", padding: "40px", textAlign: "center" }}>
          <div style={{ fontSize: "16px", fontWeight: 600, color: "#1e293b", marginBottom: "8px" }}>No proceedings recorded yet</div>
          <div style={{ color: "#64748b", fontSize: "14px" }}>
            Record hearings, interim orders, judgments, and future hearing dates (NDOH).
          </div>
        </div>
      ) : (
        <div style={{ display: "flex", flexDirection: "column", gap: "12px" }}>
          {proceedings.map((p) => (
            <div
              key={p.id}
              style={{
                background: "#fff",
                border: p.isAuthoritativeNdoh ? "2px solid #818cf8" : "1px solid #e2e8f0",
                borderRadius: "8px",
                padding: "16px 20px",
              }}
            >
              <div style={{ display: "flex", justifyContent: "space-between", alignItems: "flex-start", marginBottom: "8px" }}>
                <div style={{ display: "flex", alignItems: "center", gap: "10px" }}>
                  <span style={{ fontSize: "16px", fontWeight: 700, color: "#0f172a" }}>
                    {p.proceedingDate || "Undated"}
                  </span>
                  <span className="court-badge court-badge-ndoh">{p.orderType || "Hearing"}</span>
                  {p.restraintNature && p.restraintNature !== "None" && (
                    <span className="court-badge court-badge-status-stay">
                      ⚠ {p.restraintNature}
                    </span>
                  )}
                  {p.isAuthoritativeNdoh && (
                    <span className="court-badge court-badge-authoritative">
                      Authoritative NDOH Source
                    </span>
                  )}
                </div>

                {p.nextDate && (
                  <div style={{ textAlign: "right", display: "flex", flexDirection: "column", alignItems: "flex-end", gap: "4px" }}>
                    <div>
                      <span style={{ fontSize: "12px", color: "#64748b", textTransform: "uppercase" }}>NDOH: </span>
                      <strong style={{ color: "#4338ca", fontSize: "14px" }}>{p.nextDate}</strong>
                    </div>
                    {courtCase.isProjectedToCalendar ? (
                      <span className="court-badge" style={{ background: "#dcfce7", color: "#166534", fontSize: "11px" }}>
                        ● On Calendar
                      </span>
                    ) : courtCase.capabilities.canPromoteToCalendar ? (
                      <button
                        type="button"
                        className="secondary-button"
                        style={{ fontSize: "11px", padding: "3px 8px" }}
                        onClick={() => handlePromote(p.id)}
                        disabled={promotingId === p.id}
                      >
                        {promotingId === p.id ? "Promoting..." : "📅 Promote to Calendar"}
                      </button>
                    ) : (
                      <span style={{ fontSize: "11px", color: "#94a3b8" }}>○ Not on Calendar</span>
                    )}
                  </div>
                )}
              </div>

              {p.summary && (
                <div style={{ fontSize: "14px", color: "#334155", lineHeight: 1.5, marginTop: "8px" }}>
                  {p.summary}
                </div>
              )}

              <div style={{ marginTop: "12px", fontSize: "12px", color: "#94a3b8" }}>
                Recorded by {p.createdByDisplayName || "Officer"} on {new Date(p.createdAt).toLocaleString()}
              </div>
            </div>
          ))}
        </div>
      )}

      {showModal && (
        <div className="court-modal-backdrop" onClick={() => setShowModal(false)}>
          <div className="court-modal" onClick={(e) => e.stopPropagation()}>
            <div className="court-modal-header">
              <h3>Record Court Proceeding</h3>
              <button className="quiet-button" onClick={() => setShowModal(false)}>✕</button>
            </div>
            <form onSubmit={handleSubmit}>
              <div className="court-modal-body">
                {validationError && (
                  <div style={{ color: "#b91c1c", background: "#fef2f2", padding: "10px", borderRadius: "6px" }}>
                    {validationError}
                  </div>
                )}

                <div>
                  <label style={{ display: "block", fontSize: "13px", fontWeight: 600, color: "#334155", marginBottom: "4px" }}>
                    Proceeding Date *
                  </label>
                  <input
                    type="date"
                    required
                    className="form-input"
                    style={{ width: "100%" }}
                    value={proceedingDate}
                    onChange={(e) => setProceedingDate(e.target.value)}
                  />
                </div>

                <div>
                  <label style={{ display: "block", fontSize: "13px", fontWeight: 600, color: "#334155", marginBottom: "4px" }}>
                    Order Type
                  </label>
                  <select
                    className="form-input"
                    style={{ width: "100%" }}
                    value={orderType}
                    onChange={(e) => setOrderType(e.target.value)}
                  >
                    <option value="Hearing">Hearing</option>
                    <option value="Interim Order">Interim Order</option>
                    <option value="Notice">Notice</option>
                    <option value="Final Judgment">Final Judgment</option>
                    <option value="Adjournment">Adjournment</option>
                    <option value="Written Statement / Reply">Written Statement / Reply</option>
                    <option value="Order">Order</option>
                  </select>
                </div>

                <div>
                  <label style={{ display: "block", fontSize: "13px", fontWeight: 600, color: "#334155", marginBottom: "4px" }}>
                    Restraint / Injunction Nature
                  </label>
                  <select
                    className="form-input"
                    style={{ width: "100%" }}
                    value={restraintNature}
                    onChange={(e) => setRestraintNature(e.target.value)}
                  >
                    <option value="None">None</option>
                    <option value="Stay on Dispossession">Stay on Dispossession</option>
                    <option value="Stay on Award">Stay on Award</option>
                    <option value="Status Quo">Status Quo</option>
                    <option value="Injunction">Injunction</option>
                    <option value="Other Restraint">Other Restraint</option>
                  </select>
                </div>

                <div>
                  <label style={{ display: "block", fontSize: "13px", fontWeight: 600, color: "#334155", marginBottom: "4px" }}>
                    Next Date of Hearing (NDOH)
                  </label>
                  <input
                    type="date"
                    className="form-input"
                    style={{ width: "100%" }}
                    value={nextDate}
                    onChange={(e) => setNextDate(e.target.value)}
                  />
                  <small style={{ color: "#64748b", marginTop: "2px", display: "block" }}>
                    If this is the most recent proceeding, setting this date updates the authoritative schedule and active calendar event.
                  </small>
                </div>

                <div>
                  <label style={{ display: "block", fontSize: "13px", fontWeight: 600, color: "#334155", marginBottom: "4px" }}>
                    Order Summary / Notes
                  </label>
                  <textarea
                    rows={4}
                    className="form-input"
                    style={{ width: "100%" }}
                    placeholder="Brief summary of what transpired, directions issued by court, or next steps required..."
                    value={summary}
                    onChange={(e) => setSummary(e.target.value)}
                  />
                </div>
              </div>

              <div className="court-modal-footer">
                <button type="button" className="secondary-button" onClick={() => setShowModal(false)} disabled={submitting}>
                  Cancel
                </button>
                <button type="submit" className="primary-button" disabled={submitting}>
                  {submitting ? "Saving..." : "Record Proceeding"}
                </button>
              </div>
            </form>
          </div>
        </div>
      )}
    </div>
  );
};
