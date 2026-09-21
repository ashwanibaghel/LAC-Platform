import React, { useState, useEffect, useCallback } from "react";
import { Link } from "react-router-dom";
import { CourtCaseDetailDto, CourtCaseLinkedWorkDto } from "./types";
import { useAuth } from "../auth/AuthProvider";

interface CourtWorkTabProps {
  courtCase: CourtCaseDetailDto;
  onRefresh: () => void;
}

export const CourtWorkTab: React.FC<CourtWorkTabProps> = ({ courtCase, onRefresh }) => {
  const { hasPermission } = useAuth();
  const canEdit = hasPermission("Court.Edit") || hasPermission("Award.Edit");

  const [workData, setWorkData] = useState<CourtCaseLinkedWorkDto | null>(null);
  const [loading, setLoading] = useState(true);
  const [showLinkModal, setShowLinkModal] = useState(false);
  const [matterIdInput, setMatterIdInput] = useState("");
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [modalError, setModalError] = useState<string | null>(null);

  const fetchWork = useCallback(async () => {
    try {
      setLoading(true);
      const res = await fetch(`/api/court-cases/${courtCase.id}/work`, { credentials: "include" });
      if (res.ok) {
        const data = await res.json();
        setWorkData(data);
      } else {
        setError("Failed to load linked work");
      }
    } catch {
      setError("Network error loading linked work");
    } finally {
      setLoading(false);
    }
  }, [courtCase.id]);

  useEffect(() => {
    void fetchWork();
  }, [fetchWork]);

  const handleLinkMatter = async (e: React.FormEvent) => {
    e.preventDefault();
    setModalError(null);
    try {
      setSubmitting(true);
      const res = await fetch(`/api/court-cases/${courtCase.id}/matters`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        credentials: "include",
        body: JSON.stringify({ matterId: matterIdInput.trim() }),
      });
      if (res.ok) {
        setShowLinkModal(false);
        setMatterIdInput("");
        await fetchWork();
        onRefresh();
      } else {
        const err = await res.json().catch(() => ({ error: "Failed to link matter" }));
        setModalError(err.error || "Failed to link matter");
      }
    } catch {
      setModalError("Network error linking matter");
    } finally {
      setSubmitting(false);
    }
  };

  const handleUnlinkMatter = async (matterId: string) => {
    if (!window.confirm("Are you sure you want to unlink this Matter from the court case?")) return;
    try {
      const res = await fetch(`/api/court-cases/${courtCase.id}/matters/${matterId}`, {
        method: "DELETE",
        credentials: "include",
      });
      if (res.ok) {
        await fetchWork();
        onRefresh();
      } else {
        alert("Failed to unlink matter");
      }
    } catch {
      alert("Network error unlinking matter");
    }
  };

  return (
    <div className="court-work-tab">
      <div className="court-section-header">
        <h3>Connected Legal Matters & Work</h3>
        {canEdit && (
          <button className="primary-button" onClick={() => { setModalError(null); setMatterIdInput(""); setShowLinkModal(true); }}>
            + Link Matter
          </button>
        )}
      </div>

      {error && <div style={{ color: "#dc2626", marginBottom: "16px" }}>{error}</div>}

      <div className="court-overview-cards" style={{ marginBottom: "20px" }}>
        <div className="court-card">
          <div className="court-card-title">Connected Matters</div>
          <div className="court-card-value">{workData?.matters.length ?? 0}</div>
        </div>
        <div className="court-card">
          <div className="court-card-title">Active Legal Drafts</div>
          <div className="court-card-value">{workData?.totalDraftCount ?? 0}</div>
        </div>
        <div className="court-card">
          <div className="court-card-title">Related Work Items</div>
          <div className="court-card-value">{workData?.totalWorkItemCount ?? 0}</div>
        </div>
      </div>

      {loading ? (
        <div style={{ padding: "32px", textAlign: "center", color: "#64748b" }}>Loading connected work...</div>
      ) : !workData || workData.matters.length === 0 ? (
        <div style={{ background: "#fff", border: "1px solid #e2e8f0", borderRadius: "8px", padding: "40px", textAlign: "center" }}>
          <div style={{ fontSize: "16px", fontWeight: 600, color: "#1e293b", marginBottom: "8px" }}>No matters linked</div>
          <div style={{ color: "#64748b", fontSize: "14px" }}>
            Link matters to manage legal opinions, draft replies, writ responses, and operational tasks.
          </div>
        </div>
      ) : (
        <div style={{ display: "flex", flexDirection: "column", gap: "12px" }}>
          {workData.matters.map((m) => (
            <div
              key={m.matterId}
              style={{
                background: "#fff",
                border: "1px solid #e2e8f0",
                borderRadius: "8px",
                padding: "16px 20px",
                display: "flex",
                justifyContent: "space-between",
                alignItems: "center",
              }}
            >
              <div>
                <div style={{ display: "flex", alignItems: "center", gap: "8px", marginBottom: "4px" }}>
                  <span className="court-badge court-badge-ndoh">{m.workstreamName}</span>
                  <span className="court-badge court-badge-authoritative">{m.matterType}</span>
                  <span style={{ fontSize: "12px", color: "#64748b" }}>Status: {m.status}</span>
                </div>
                <Link
                  to={`/matters/${m.matterId}`}
                  style={{ fontSize: "16px", fontWeight: 700, color: "#2563eb", textDecoration: "none" }}
                >
                  {m.title}
                </Link>
                <div style={{ display: "flex", gap: "16px", marginTop: "6px", fontSize: "13px", color: "#475569" }}>
                  <span>📝 {m.draftCount} Drafts</span>
                  <span>⚙ {m.workItemCount} Work Items</span>
                </div>
              </div>

              <div style={{ display: "flex", alignItems: "center", gap: "12px" }}>
                <Link to={`/matters/${m.matterId}`} className="secondary-button" style={{ textDecoration: "none" }}>
                  Open Matter →
                </Link>
                {canEdit && (
                  <button
                    className="quiet-button"
                    style={{ color: "#dc2626" }}
                    onClick={() => void handleUnlinkMatter(m.matterId)}
                    title="Unlink Matter"
                  >
                    ✕
                  </button>
                )}
              </div>
            </div>
          ))}
        </div>
      )}

      {showLinkModal && (
        <div className="court-modal-backdrop" onClick={() => setShowLinkModal(false)}>
          <div className="court-modal" onClick={(e) => e.stopPropagation()}>
            <div className="court-modal-header">
              <h3>Link Legal Matter</h3>
              <button className="quiet-button" onClick={() => setShowLinkModal(false)}>✕</button>
            </div>
            <form onSubmit={handleLinkMatter}>
              <div className="court-modal-body">
                {modalError && <div style={{ color: "#b91c1c", background: "#fef2f2", padding: "10px", borderRadius: "6px" }}>{modalError}</div>}
                <div>
                  <label style={{ display: "block", fontSize: "13px", fontWeight: 600, color: "#334155", marginBottom: "4px" }}>
                    Matter GUID *
                  </label>
                  <input
                    type="text"
                    required
                    className="form-input"
                    style={{ width: "100%" }}
                    placeholder="Enter Matter ID"
                    value={matterIdInput}
                    onChange={(e) => setMatterIdInput(e.target.value)}
                  />
                </div>
              </div>
              <div className="court-modal-footer">
                <button type="button" className="secondary-button" onClick={() => setShowLinkModal(false)} disabled={submitting}>Cancel</button>
                <button type="submit" className="primary-button" disabled={submitting}>{submitting ? "Linking..." : "Link Matter"}</button>
              </div>
            </form>
          </div>
        </div>
      )}
    </div>
  );
};
