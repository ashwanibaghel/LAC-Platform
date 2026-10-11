import React, { useState, useEffect } from "react";
import { useNavigate } from "react-router-dom";
import type { DakDetail } from "./types";
import type { MatterOptionsResponse, MatterOptionItem } from "../matter/notingTypes";

interface DakMatterLinkModalProps {
  dak: DakDetail;
  onClose: () => void;
  onSuccess: () => void;
}

function getUuid(): string {
  if (typeof crypto !== "undefined" && typeof crypto.randomUUID === "function") {
    return crypto.randomUUID();
  }
  return "xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx".replace(/[xy]/g, (c) => {
    const r = (Math.random() * 16) | 0;
    const v = c === "x" ? r : (r & 0x3) | 0x8;
    return v.toString(16);
  });
}

export const DakMatterLinkModal: React.FC<DakMatterLinkModalProps> = ({
  dak,
  onClose,
  onSuccess
}) => {
  const navigate = useNavigate();
  const [mode, setMode] = useState<"create" | "link">("create");

  // Village classification state
  const isClassified =
    dak.villageLinks &&
    dak.villageLinks.length > 0 &&
    (dak as any).villageClassification === "VillageSpecific";

  const [classifiedVillageId, setClassifiedVillageId] = useState<string>(
    dak.villageLinks?.[0]?.entityId || ""
  );
  const [classifying, setClassifying] = useState<boolean>(false);
  const [classificationDone, setClassificationDone] = useState<boolean>(isClassified);

  // Create Matter Form
  const [matterTitle, setMatterTitle] = useState<string>(dak.subject);
  const [workstreams, setWorkstreams] = useState<Array<{ id: string; name: string }>>([]);
  const [selectedWorkstreamId, setSelectedWorkstreamId] = useState<string>("");

  // Link Existing Matter Form
  const [searchQuery, setSearchQuery] = useState<string>("");
  const [matterOptions, setMatterOptions] = useState<MatterOptionItem[]>([]);
  const [loadingOptions, setLoadingOptions] = useState<boolean>(false);
  const [selectedMatterId, setSelectedMatterId] = useState<string>("");

  const [submitting, setSubmitting] = useState<boolean>(false);
  const [error, setError] = useState<string | null>(null);

  // Load Workstreams
  useEffect(() => {
    fetch("/api/workstreams", { credentials: "include" })
      .then(async (res) => (res.ok ? res.json() : []))
      .then((data) => {
        if (Array.isArray(data)) {
          setWorkstreams(data);
          if (data.length > 0) {
            const defaultStream = data.find((w: any) => w.code === "DakCorrespondence") || data[0];
            setSelectedWorkstreamId(defaultStream.id);
          }
        }
      })
      .catch(() => {});
  }, []);

  // Classify Village if needed
  const handleClassifyVillage = async () => {
    if (!classifiedVillageId) {
      setError("Please select an authoritative Village for this Dak.");
      return;
    }
    setClassifying(true);
    setError(null);

    try {
      const res = await fetch(`/api/dak/${dak.id}/village-classification`, {
        method: "PUT",
        headers: {
          "Content-Type": "application/json",
          "Idempotency-Key": getUuid()
        },
        body: JSON.stringify({
          classification: "VillageSpecific",
          villageIds: [classifiedVillageId],
          expectedRevision: dak.revision
        })
      });

      if (!res.ok) {
        const body = await res.json().catch(() => ({}));
        throw new Error(body.message || "Failed to classify village");
      }

      setClassificationDone(true);
      dak.revision++; // update local expected revision
    } catch (err: any) {
      setError(err.message || "Classification failed");
    } finally {
      setClassifying(false);
    }
  };

  // Fetch Existing Matter Options
  useEffect(() => {
    if (mode !== "link" || !classifiedVillageId) return;
    setLoadingOptions(true);
    setError(null);

    const query = searchQuery ? `&search=${encodeURIComponent(searchQuery)}` : "";
    fetch(`/api/dak/${dak.id}/matter-options?villageId=${classifiedVillageId}${query}`, {
      credentials: "include"
    })
      .then(async (res) => {
        if (!res.ok) {
          const body = await res.json().catch(() => ({}));
          throw new Error(body.message || "Failed to load compatible Matters");
        }
        return res.json() as Promise<MatterOptionsResponse>;
      })
      .then((data) => {
        setMatterOptions(data.matters || []);
        if (data.matters && data.matters.length > 0 && !selectedMatterId) {
          setSelectedMatterId(data.matters[0].id);
        }
      })
      .catch((err: any) => {
        setError(err.message);
      })
      .finally(() => {
        setLoadingOptions(false);
      });
  }, [mode, dak.id, classifiedVillageId, searchQuery, selectedMatterId]);

  // Execute Create or Link Mutation
  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!classifiedVillageId) {
      setError("Authoritative Village is required.");
      return;
    }
    if (!selectedWorkstreamId) {
      setError("Please select a Workstream.");
      return;
    }

    setSubmitting(true);
    setError(null);

    try {
      const payload: any = {
        villageId: classifiedVillageId,
        workstreamId: selectedWorkstreamId,
        expectedRevision: dak.revision
      };

      if (mode === "create") {
        if (!matterTitle.trim()) {
          throw new Error("Matter Title is required.");
        }
        payload.title = matterTitle.trim();
        payload.matterType = "Dak Correspondence";
      } else {
        if (!selectedMatterId) {
          throw new Error("Please select an existing Matter to link.");
        }
        payload.existingMatterId = selectedMatterId;
      }

      const res = await fetch(`/api/dak/${dak.id}/matter-workspace`, {
        method: "POST",
        headers: {
          "Content-Type": "application/json",
          "Idempotency-Key": getUuid()
        },
        body: JSON.stringify(payload)
      });

      if (!res.ok) {
        const body = await res.json().catch(() => ({}));
        throw new Error(body.message || `Failed to create/link Matter workspace (${res.status})`);
      }

      const result = await res.json();
      onSuccess();
      onClose();
      // Navigate directly into the shared Matter workspace with source Dak context
      navigate(`/matters/${result.matterId}?sourceDakId=${dak.id}`);
    } catch (err: any) {
      setError(err.message || "Action failed");
    } finally {
      setSubmitting(false);
    }
  };

  const villageName = dak.villageLinks?.[0]?.displayName || "Selected Village";

  return (
    <div className="noting-modal-overlay" onClick={onClose}>
      <div className="noting-modal-card" onClick={(e) => e.stopPropagation()} style={{ maxWidth: "560px" }}>
        <div className="noting-modal-header">
          <h3>Open Matter Workspace for Dak {dak.diaryNumber}</h3>
          <button className="noting-btn noting-btn-secondary" onClick={onClose} type="button">✕</button>
        </div>

        {/* Step 1: Village Classification Check */}
        {!classificationDone ? (
          <div className="noting-modal-body">
            <div style={{ background: "#fffbeb", border: "1px solid #fde68a", padding: "12px", borderRadius: "6px", color: "#92400e", fontSize: "12.5px" }}>
              <strong>Village Classification Required:</strong> Every Matter must belong to exactly one authoritative Village. Please confirm the Village classification for this Dak before creating or linking a Matter.
            </div>

            <div>
              <label style={{ fontSize: "12px", fontWeight: 600, color: "#334155", display: "block", marginBottom: "4px" }}>
                Classify Authoritative Village <span style={{ color: "#b91c1c" }}>*</span>
              </label>
              {dak.villageLinks && dak.villageLinks.length > 1 ? (
                <select
                  value={classifiedVillageId}
                  onChange={(e) => setClassifiedVillageId(e.target.value)}
                  style={{ width: "100%", padding: "7px 10px", fontSize: "13px", border: "1px solid #cbd5e1", borderRadius: "4px" }}
                >
                  {dak.villageLinks.map((v) => (
                    <option key={v.linkId} value={v.entityId || ""}>
                      📍 {v.displayName}
                    </option>
                  ))}
                </select>
              ) : dak.villageLinks && dak.villageLinks.length === 1 ? (
                <div style={{ fontSize: "13px", fontWeight: 600, color: "#0f172a", background: "#f1f5f9", padding: "8px 10px", borderRadius: "4px" }}>
                  📍 {villageName}
                </div>
              ) : (
                <div style={{ color: "#b91c1c", fontSize: "12.5px" }}>
                  This Dak has no linked Village. Please link a Village in the Dak detail first.
                </div>
              )}
            </div>

            {error && (
              <div style={{ color: "#b91c1c", fontSize: "12px", background: "#fee2e2", padding: "6px 10px", borderRadius: "4px" }}>
                {error}
              </div>
            )}

            <div className="noting-modal-footer" style={{ padding: "12px 0 0 0" }}>
              <button className="noting-btn noting-btn-secondary" type="button" onClick={onClose}>
                Cancel
              </button>
              <button
                className="noting-btn noting-btn-primary"
                type="button"
                onClick={handleClassifyVillage}
                disabled={classifying || !classifiedVillageId}
              >
                {classifying ? "Classifying..." : "Confirm Village Classification"}
              </button>
            </div>
          </div>
        ) : (
          /* Step 2: Create Matter vs Link Existing */
          <form onSubmit={handleSubmit}>
            <div className="noting-modal-body">
              {/* Tabs */}
              <div style={{ display: "flex", gap: "8px", borderBottom: "1px solid #e2e8f0", paddingBottom: "10px" }}>
                <button
                  type="button"
                  className={`noting-btn ${mode === "create" ? "noting-btn-primary" : "noting-btn-secondary"}`}
                  onClick={() => setMode("create")}
                >
                  Create New Matter
                </button>
                <button
                  type="button"
                  className={`noting-btn ${mode === "link" ? "noting-btn-primary" : "noting-btn-secondary"}`}
                  onClick={() => setMode("link")}
                >
                  Link Existing Matter
                </button>
              </div>

              <div style={{ fontSize: "12px", color: "#475569" }}>
                Village Scope: <strong>{villageName}</strong> (Authoritative)
              </div>

              {mode === "create" ? (
                <>
                  <div>
                    <label style={{ fontSize: "12px", fontWeight: 600, color: "#334155", display: "block", marginBottom: "4px" }}>
                      Matter Title / Subject <span style={{ color: "#b91c1c" }}>*</span>
                    </label>
                    <input
                      type="text"
                      value={matterTitle}
                      onChange={(e) => setMatterTitle(e.target.value)}
                      placeholder="Enter Matter subject..."
                      style={{ width: "100%", padding: "7px 10px", fontSize: "13px", border: "1px solid #cbd5e1", borderRadius: "4px" }}
                    />
                  </div>

                  <div>
                    <label style={{ fontSize: "12px", fontWeight: 600, color: "#334155", display: "block", marginBottom: "4px" }}>
                      Workstream <span style={{ color: "#b91c1c" }}>*</span>
                    </label>
                    <select
                      value={selectedWorkstreamId}
                      onChange={(e) => setSelectedWorkstreamId(e.target.value)}
                      style={{ width: "100%", padding: "7px 10px", fontSize: "13px", border: "1px solid #cbd5e1", borderRadius: "4px", background: "#ffffff" }}
                    >
                      {workstreams.map((w) => (
                        <option key={w.id} value={w.id}>
                          {w.name}
                        </option>
                      ))}
                    </select>
                  </div>
                </>
              ) : (
                <>
                  <div>
                    <label style={{ fontSize: "12px", fontWeight: 600, color: "#334155", display: "block", marginBottom: "4px" }}>
                      Search Existing Matters in {villageName}
                    </label>
                    <input
                      type="text"
                      value={searchQuery}
                      onChange={(e) => setSearchQuery(e.target.value)}
                      placeholder="Type to filter matters..."
                      style={{ width: "100%", padding: "7px 10px", fontSize: "13px", border: "1px solid #cbd5e1", borderRadius: "4px", marginBottom: "8px" }}
                    />

                    {loadingOptions ? (
                      <div style={{ fontSize: "12px", color: "#64748b", padding: "8px" }}>Loading compatible matters…</div>
                    ) : matterOptions.length === 0 ? (
                      <div style={{ fontSize: "12px", color: "#64748b", padding: "8px", background: "#f8fafc", borderRadius: "4px" }}>
                        No existing Matters found for this Village. Use "Create New Matter" instead.
                      </div>
                    ) : (
                      <div style={{ maxHeight: "160px", overflowY: "auto", border: "1px solid #cbd5e1", borderRadius: "4px" }}>
                        {matterOptions.map((m) => (
                          <div
                            key={m.id}
                            style={{
                              padding: "8px 10px",
                              borderBottom: "1px solid #f1f5f9",
                              background: selectedMatterId === m.id ? "#eff6ff" : "#ffffff",
                              cursor: "pointer",
                              fontSize: "12.5px"
                            }}
                            onClick={() => setSelectedMatterId(m.id)}
                          >
                            <strong>{m.title}</strong>
                            <div style={{ fontSize: "11px", color: "#64748b" }}>Matter ID: {m.id.slice(0, 8)}... (rev {m.revision})</div>
                          </div>
                        ))}
                      </div>
                    )}
                  </div>

                  <div>
                    <label style={{ fontSize: "12px", fontWeight: 600, color: "#334155", display: "block", marginBottom: "4px" }}>
                      Workstream <span style={{ color: "#b91c1c" }}>*</span>
                    </label>
                    <select
                      value={selectedWorkstreamId}
                      onChange={(e) => setSelectedWorkstreamId(e.target.value)}
                      style={{ width: "100%", padding: "7px 10px", fontSize: "13px", border: "1px solid #cbd5e1", borderRadius: "4px", background: "#ffffff" }}
                    >
                      {workstreams.map((w) => (
                        <option key={w.id} value={w.id}>
                          {w.name}
                        </option>
                      ))}
                    </select>
                  </div>
                </>
              )}

              {error && (
                <div style={{ color: "#b91c1c", fontSize: "12px", background: "#fee2e2", padding: "6px 10px", borderRadius: "4px" }}>
                  {error}
                </div>
              )}
            </div>

            <div className="noting-modal-footer">
              <button className="noting-btn noting-btn-secondary" type="button" onClick={onClose} disabled={submitting}>
                Cancel
              </button>
              <button
                className="noting-btn noting-btn-primary"
                type="submit"
                disabled={submitting || (mode === "link" && !selectedMatterId)}
              >
                {submitting
                  ? "Processing..."
                  : mode === "create"
                  ? "Create Matter & Open Workspace"
                  : "Link & Open Workspace"}
              </button>
            </div>
          </form>
        )}
      </div>
    </div>
  );
};
