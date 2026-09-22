import React, { useState, useEffect } from "react";
import { Link } from "react-router-dom";
import { useAuth } from "../auth/AuthProvider";
import { IconPlus, IconChevronRight, IconClose } from "../components/Icons";
import "./land.css";

const api = "/api";

interface MatterItem {
  id: string;
  title: string;
  matterType?: string;
  status: string;
  workstreamName?: string;
  workstreamCode?: string;
  referenceNumber?: string;
  khasraReferenceText?: string;
  award?: { awardId: string; awardNumber: string } | null;
}

interface WorkstreamOption {
  id: string;
  name: string;
  code: string;
}

interface AwardOption {
  id: string;
  awardNumber: string;
}

export interface VillageMattersTabProps {
  villageId: string;
}

export const VillageMattersTab: React.FC<VillageMattersTabProps> = ({ villageId }) => {
  const { hasPermission } = useAuth();
  const canCreateMatter = hasPermission("Matter.Create");

  const [matters, setMatters] = useState<MatterItem[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  // New Matter Modal state
  const [isModalOpen, setIsModalOpen] = useState(false);
  const [workstreams, setWorkstreams] = useState<WorkstreamOption[]>([]);
  const [matterTypes, setMatterTypes] = useState<string[]>([]);
  const [awards, setAwards] = useState<AwardOption[]>([]);

  // Form state
  const [title, setTitle] = useState("");
  const [workstreamId, setWorkstreamId] = useState("");
  const [matterType, setMatterType] = useState("");
  const [awardId, setAwardId] = useState("");
  const [khasraReferenceText, setKhasraReferenceText] = useState("");
  const [referenceNumber, setReferenceNumber] = useState("");
  const [remarks, setRemarks] = useState("");
  const [submitting, setSubmitting] = useState(false);
  const [formError, setFormError] = useState<string | null>(null);

  const loadMatters = async () => {
    setLoading(true);
    setError(null);
    try {
      const res = await fetch(`${api}/villages/${villageId}/matters`, { credentials: "include" });
      if (!res.ok) {
        if (res.status === 403) throw new Error("Access denied: You do not have permission to view matters.");
        throw new Error("Failed to fetch village matters.");
      }
      const data = await res.json();
      setMatters(data);
    } catch (err: any) {
      setError(err?.message || "Error loading matters.");
    } finally {
      setLoading(false);
    }
  };

  const loadModalContext = async () => {
    try {
      // Load context for workstreams & matterTypes
      const ctxRes = await fetch(`${api}/matters/context`, { credentials: "include" });
      if (ctxRes.ok) {
        const ctxData = await ctxRes.json();
        if (ctxData.workstreams?.length) {
          setWorkstreams(ctxData.workstreams);
          if (!workstreamId) {
            setWorkstreamId(ctxData.workstreams[0].id);
          }
        }
        if (ctxData.matterTypes?.length) {
          setMatterTypes(ctxData.matterTypes);
          if (!matterType) {
            setMatterType(ctxData.matterTypes[0]);
          }
        }
      }

      // Load awards for this village with required page=0
      const awRes = await fetch(`${api}/villages/${villageId}/awards?page=0&pageSize=100`, { credentials: "include" });
      if (awRes.ok) {
        const awData = await awRes.json();
        setAwards(awData.items || []);
      }
    } catch (e) {
      console.error("Failed to load modal context", e);
    }
  };

  useEffect(() => {
    if (!villageId) return;
    loadMatters();
  }, [villageId]);

  const handleOpenModal = () => {
    setTitle("");
    setAwardId("");
    setKhasraReferenceText("");
    setReferenceNumber("");
    setRemarks("");
    setFormError(null);
    setIsModalOpen(true);
    loadModalContext();
  };

  const handleCreateMatter = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!title.trim()) {
      setFormError("Matter title is required.");
      return;
    }
    if (!workstreamId) {
      setFormError("Workstream selection is required.");
      return;
    }

    setSubmitting(true);
    setFormError(null);
    try {
      const payload = {
        title: title.trim(),
        workstreamId: workstreamId,
        matterType: matterType || "Court Case",
        awardId: awardId || null,
        khasraReferenceText: khasraReferenceText.trim() || null,
        referenceNumber: referenceNumber.trim() || null,
        remarks: remarks.trim() || null,
      };

      const res = await fetch(`${api}/villages/${villageId}/matters`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        credentials: "include",
        body: JSON.stringify(payload),
      });

      if (!res.ok) {
        const errJson = await res.json().catch(() => ({}));
        throw new Error(errJson.detail || errJson.message || "Failed to create matter.");
      }

      setIsModalOpen(false);
      loadMatters();
    } catch (err: any) {
      setFormError(err?.message || "An error occurred while creating the matter.");
    } finally {
      setSubmitting(false);
    }
  };

  return (
    <div className="village-matters-tab">
      <div className="section-heading" style={{ marginBottom: "20px" }}>
        <div>
          <h2>Village Matters</h2>
          <span>Legal matters, court cases, and acquisition files active in this village.</span>
        </div>
        {canCreateMatter && (
          <button className="primary-button" onClick={handleOpenModal}>
            <IconPlus size={16} /> New Matter
          </button>
        )}
      </div>

      {loading ? (
        <div className="state loading">Loading village matters…</div>
      ) : error ? (
        <div className="state error">{error}</div>
      ) : (
        <div className="table-wrap">
          <table>
            <thead>
              <tr>
                <th scope="col">Title</th>
                <th scope="col">Workstream</th>
                <th scope="col">Type</th>
                <th scope="col">Award Ref</th>
                <th scope="col">Status</th>
                <th scope="col">Action</th>
              </tr>
            </thead>
            <tbody>
              {matters.length === 0 ? (
                <tr>
                  <td colSpan={6} className="text-center-muted" style={{ padding: "20px", textAlign: "center", color: "#64748b" }}>
                    No active matters associated with this village.
                  </td>
                </tr>
              ) : (
                matters.map((m) => (
                  <tr key={m.id}>
                    <td>
                      <div>
                        <Link to={`/matters/${m.id}`} className="entity-link" style={{ fontWeight: 650 }}>
                          {m.title}
                        </Link>
                        {m.referenceNumber && (
                          <div style={{ fontSize: "12px", color: "#64748b" }}>
                            Ref: {m.referenceNumber}
                          </div>
                        )}
                      </div>
                    </td>
                    <td>{m.workstreamName || m.workstreamCode || "—"}</td>
                    <td>{m.matterType || "General"}</td>
                    <td>{m.award?.awardNumber ? `Award #${m.award.awardNumber}` : "—"}</td>
                    <td>
                      <span className={`status-badge status-${m.status.toLowerCase()}`}>
                        {m.status}
                      </span>
                    </td>
                    <td>
                      <Link
                        to={`/matters/${m.id}`}
                        className="text-action"
                        style={{ display: "inline-flex", alignItems: "center", gap: "4px" }}
                      >
                        <span>Open Matter</span>
                        <IconChevronRight size={14} />
                      </Link>
                    </td>
                  </tr>
                ))
              )}
            </tbody>
          </table>
        </div>
      )}

      {/* New Matter Modal */}
      {isModalOpen && (
        <div className="modal-overlay" onClick={() => setIsModalOpen(false)}>
          <div className="modal-container" onClick={(e) => e.stopPropagation()} style={{ maxWidth: "560px" }}>
            <div className="modal-header">
              <h3>Create New Village Matter</h3>
              <button className="icon-button" onClick={() => setIsModalOpen(false)}>
                <IconClose size={18} />
              </button>
            </div>

            <form onSubmit={handleCreateMatter}>
              <div className="modal-body" style={{ display: "flex", flexDirection: "column", gap: "16px" }}>
                {formError && <div className="state error">{formError}</div>}

                <div className="form-group">
                  <label className="form-label required">Matter Title</label>
                  <input
                    type="text"
                    className="form-input"
                    placeholder="e.g. Compensation Appeal Case 25/2024"
                    value={title}
                    onChange={(e) => setTitle(e.target.value)}
                    required
                  />
                </div>

                <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: "16px" }}>
                  <div className="form-group">
                    <label className="form-label required">Workstream</label>
                    <select
                      className="form-input"
                      value={workstreamId}
                      onChange={(e) => setWorkstreamId(e.target.value)}
                      required
                    >
                      <option value="" disabled>
                        Select Workstream…
                      </option>
                      {workstreams.map((ws) => (
                        <option key={ws.id} value={ws.id}>
                          {ws.name} ({ws.code})
                        </option>
                      ))}
                    </select>
                  </div>

                  <div className="form-group">
                    <label className="form-label">Matter Type</label>
                    <select
                      className="form-input"
                      value={matterType}
                      onChange={(e) => setMatterType(e.target.value)}
                    >
                      {(matterTypes.length ? matterTypes : ["Court Case", "Compensation", "Land Acquisition", "General", "Other"]).map((t) => (
                        <option key={t} value={t}>
                          {t}
                        </option>
                      ))}
                    </select>
                  </div>
                </div>

                <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: "16px" }}>
                  <div className="form-group">
                    <label className="form-label">Award Reference</label>
                    <select className="form-input" value={awardId} onChange={(e) => setAwardId(e.target.value)}>
                      <option value="">No Award Link</option>
                      {awards.map((a) => (
                        <option key={a.id} value={a.id}>
                          Award #{a.awardNumber}
                        </option>
                      ))}
                    </select>
                  </div>

                  <div className="form-group">
                    <label className="form-label">Reference Number</label>
                    <input
                      type="text"
                      className="form-input"
                      placeholder="e.g. LAC/2024/104"
                      value={referenceNumber}
                      onChange={(e) => setReferenceNumber(e.target.value)}
                    />
                  </div>
                </div>

                <div className="form-group">
                  <label className="form-label">Khasra Reference Text</label>
                  <input
                    type="text"
                    className="form-input"
                    placeholder="e.g. Khasra 12//14, 15//1"
                    value={khasraReferenceText}
                    onChange={(e) => setKhasraReferenceText(e.target.value)}
                  />
                </div>

                <div className="form-group">
                  <label className="form-label">Remarks</label>
                  <textarea
                    className="form-input"
                    rows={3}
                    placeholder="Additional context or background remarks…"
                    value={remarks}
                    onChange={(e) => setRemarks(e.target.value)}
                  />
                </div>
              </div>

              <div className="modal-footer">
                <button type="button" className="secondary-button" onClick={() => setIsModalOpen(false)}>
                  Cancel
                </button>
                <button type="submit" className="primary-button" disabled={submitting}>
                  {submitting ? "Creating…" : "Create Matter"}
                </button>
              </div>
            </form>
          </div>
        </div>
      )}
    </div>
  );
};
