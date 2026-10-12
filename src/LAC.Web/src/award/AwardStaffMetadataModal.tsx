import { useEffect, useRef, useState } from "react";
import { landRecordResponse, LandRecordApiError } from "./LandRecordApiError";
import type { StaffAward, AwardMetadataInput, AwardMutationResult } from "./landRecordsStaffTypes";

interface AwardStaffMetadataModalProps {
  awardId: string;
  onClose: () => void;
  onSaved: () => void;
}

interface PreviewConflict {
  awardId: string;
  awardNumber: string;
  awardDate?: string | null;
  actRegime?: string | null;
}

export function AwardStaffMetadataModal({
  awardId,
  onClose,
  onSaved,
}: AwardStaffMetadataModalProps) {
  const [data, setData] = useState<StaffAward | null>(null);
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [previewing, setPreviewing] = useState(false);
  const [previewResult, setPreviewResult] = useState<{ checked: boolean; conflicts: PreviewConflict[] } | null>(null);
  const [errorMessage, setErrorMessage] = useState("");
  const mutationIdRef = useRef<string>(crypto.randomUUID());

  // Form fields
  const [awardNumber, setAwardNumber] = useState("");
  const [awardDate, setAwardDate] = useState("");
  const [awardType, setAwardType] = useState("");
  const [actRegime, setActRegime] = useState("");
  const [purpose, setPurpose] = useState("");
  const [remarks, setRemarks] = useState("");
  const [reason, setReason] = useState("");

  useEffect(() => {
    let active = true;
    setLoading(true);
    setErrorMessage("");
    fetch(`/api/awards/${awardId}/staff-workflow`, { credentials: "include" })
      .then((res) => landRecordResponse<StaffAward>(res))
      .then((workflow) => {
        if (!active) return;
        setData(workflow);
        setAwardNumber(workflow.awardNumber || "");
        setAwardDate(workflow.awardDate ? workflow.awardDate.substring(0, 10) : "");
        setAwardType(workflow.awardType || "");
        setActRegime(workflow.actRegime || "LAA1894");
        setPurpose(workflow.purpose || "");
        setRemarks(workflow.remarks || "");
        setLoading(false);
      })
      .catch((err) => {
        if (!active) return;
        setErrorMessage(err instanceof Error ? err.message : "Failed to load award metadata.");
        setLoading(false);
      });

    return () => {
      active = false;
    };
  }, [awardId]);

  const handlePreview = async () => {
    if (!data || !awardNumber.trim()) return;
    setPreviewing(true);
    setErrorMessage("");
    try {
      const input: AwardMetadataInput = {
        mutationId: mutationIdRef.current,
        expectedRevision: data.revision,
        awardNumber: awardNumber.trim(),
        awardDate: awardDate.trim() || null,
        awardType: awardType.trim() || null,
        actRegime: actRegime.trim() || null,
        purpose: purpose.trim() || null,
        acquisitionProjectId: data.acquisitionProjectId || null,
        remarks: remarks.trim() || null,
        reason: reason.trim() || "Preview duplicate check",
      };

      const res = await fetch(`/api/awards/${awardId}/metadata/preview`, {
        method: "POST",
        credentials: "include",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(input),
      });

      const result = await landRecordResponse<{ conflicts: PreviewConflict[] }>(res);
      setPreviewResult({ checked: true, conflicts: result.conflicts || [] });
    } catch (err) {
      setErrorMessage(err instanceof Error ? err.message : "Preview request failed.");
    } finally {
      setPreviewing(false);
    }
  };

  const handleSave = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!data) return;
    if (!awardNumber.trim()) {
      setErrorMessage("Award number is required.");
      return;
    }
    if (!reason.trim()) {
      setErrorMessage("A reason for this official correction is required.");
      return;
    }

    setSaving(true);
    setErrorMessage("");

    try {
      const input: AwardMetadataInput = {
        mutationId: mutationIdRef.current,
        expectedRevision: data.revision,
        awardNumber: awardNumber.trim(),
        awardDate: awardDate.trim() || null,
        awardType: awardType.trim() || null,
        actRegime: actRegime.trim() || null,
        purpose: purpose.trim() || null,
        acquisitionProjectId: data.acquisitionProjectId || null,
        remarks: remarks.trim() || null,
        reason: reason.trim(),
      };

      const res = await fetch(`/api/awards/${awardId}/metadata`, {
        method: "PUT",
        credentials: "include",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(input),
      });

      await landRecordResponse<AwardMutationResult>(res);
      onSaved();
    } catch (err) {
      if (err instanceof LandRecordApiError && err.code === "RevisionConflict") {
        setErrorMessage("Revision conflict: This Award was modified concurrently. Please reload.");
      } else {
        setErrorMessage(err instanceof Error ? err.message : "Failed to update award metadata.");
      }
    } finally {
      setSaving(false);
    }
  };

  return (
    <aside className="workflow-drawer" aria-label="Edit Award Metadata" role="dialog" aria-modal="true">
      <section className="workspace-panel" style={{ maxWidth: "680px", width: "100%", margin: "0 auto" }}>
        <div className="panel-title" style={{ display: "flex", justifyContent: "space-between", alignItems: "center" }}>
          <div>
            <h3 style={{ margin: 0, fontSize: "18px", color: "#0f172a" }}>Edit Award Metadata</h3>
            <span style={{ fontSize: "12px", color: "#64748b" }}>
              Official record correction · Revision {data?.revision ?? 0}
            </span>
          </div>
          <button type="button" className="quiet-button" onClick={onClose}>
            ✕ Close
          </button>
        </div>

        {loading ? (
          <div style={{ padding: "40px 0", textAlign: "center", color: "#64748b" }}>
            Loading Award details…
          </div>
        ) : (
          <form onSubmit={handleSave} style={{ display: "flex", flexDirection: "column", gap: "16px", marginTop: "12px" }}>
            {data && data.metadataProvenance === "SourceReviewed" && (
              <div style={{ backgroundColor: "#eff6ff", border: "1px solid #bfdbfe", borderRadius: "6px", padding: "10px 14px", fontSize: "13px", color: "#1e40af" }}>
                <strong>✓ Source-Reviewed Provenance:</strong> This Award has {data.sourceFacts?.length || 0} facts verified against original source documents. Generic changes to verified facts are protected.
                {data.sourceFacts && data.sourceFacts.length > 0 && (
                  <ul style={{ margin: "6px 0 0 16px", padding: 0 }}>
                    {data.sourceFacts.map((fact, idx) => (
                      <li key={idx}>
                        <code>{fact.factName}</code> verified by {fact.verifiedBy} (page {fact.pageNumber})
                      </li>
                    ))}
                  </ul>
                )}
              </div>
            )}

            {data && data.metadataProvenance === "Manual" && (
              <div style={{ backgroundColor: "#f8fafc", border: "1px solid #e2e8f0", borderRadius: "6px", padding: "10px 14px", fontSize: "13px", color: "#475569" }}>
                ℹ️ <strong>Manual Record:</strong> Metadata was entered manually without source document verification.
              </div>
            )}

            <div className="field-grid" style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: "12px" }}>
              <label>
                Award Number *
                <input
                  type="text"
                  value={awardNumber}
                  onChange={(e) => {
                    setAwardNumber(e.target.value);
                    setPreviewResult(null);
                  }}
                  required
                />
              </label>

              <label>
                Award Date
                <input
                  type="date"
                  value={awardDate}
                  onChange={(e) => {
                    setAwardDate(e.target.value);
                    setPreviewResult(null);
                  }}
                />
              </label>

              <label>
                Award Type
                <select value={awardType} onChange={(e) => setAwardType(e.target.value)}>
                  <option value="">(Select / General)</option>
                  <option value="Permanent">Permanent</option>
                  <option value="Supplementary">Supplementary</option>
                  <option value="Consent">Consent</option>
                  <option value="Temporary">Temporary</option>
                </select>
              </label>

              <label>
                Act Regime
                <select value={actRegime} onChange={(e) => setActRegime(e.target.value)}>
                  <option value="LAA1894">Land Acquisition Act 1894</option>
                  <option value="RFCTLARR2013">RFCTLARR Act 2013</option>
                  <option value="NHAI1956">National Highways Act 1956</option>
                  <option value="DDA1957">Delhi Development Act 1957</option>
                </select>
              </label>

              <label style={{ gridColumn: "span 2" }}>
                Acquisition Purpose
                <input
                  type="text"
                  value={purpose}
                  onChange={(e) => setPurpose(e.target.value)}
                  placeholder="e.g. Construction of Drain / Road Widening"
                />
              </label>

              <label style={{ gridColumn: "span 2" }}>
                Administrative Remarks
                <input
                  type="text"
                  value={remarks}
                  onChange={(e) => setRemarks(e.target.value)}
                  placeholder="Optional reference notes"
                />
              </label>

              <label style={{ gridColumn: "span 2" }}>
                Reason for Official Correction *
                <textarea
                  rows={2}
                  value={reason}
                  onChange={(e) => setReason(e.target.value)}
                  placeholder="State the justification, gazette, or register reference for this change"
                  required
                />
              </label>
            </div>

            {/* Preview duplicate verification */}
            <div style={{ display: "flex", alignItems: "center", gap: "10px" }}>
              <button
                type="button"
                className="secondary-button"
                onClick={handlePreview}
                disabled={previewing || !awardNumber.trim()}
              >
                {previewing ? "Checking…" : "Preview Duplicate Check"}
              </button>
              {previewResult?.checked && previewResult.conflicts.length === 0 && (
                <span style={{ color: "#16a34a", fontSize: "13px", fontWeight: 600 }}>
                  ✓ No duplicate Award number conflicts found.
                </span>
              )}
            </div>

            {previewResult?.checked && previewResult.conflicts.length > 0 && (
              <div role="alert" style={{ backgroundColor: "#fef2f2", border: "1px solid #fecaca", borderRadius: "6px", padding: "10px 14px", fontSize: "13px", color: "#991b1b" }}>
                <strong>⚠️ Potential duplicate conflict detected:</strong>
                <ul style={{ margin: "4px 0 0 16px", padding: 0 }}>
                  {previewResult.conflicts.map((c) => (
                    <li key={c.awardId}>
                      Award {c.awardNumber} ({c.actRegime || "N/A"}) · Date: {c.awardDate || "N/A"}
                    </li>
                  ))}
                </ul>
              </div>
            )}

            {errorMessage && (
              <div role="alert" style={{ backgroundColor: "#fef2f2", border: "1px solid #fecaca", borderRadius: "6px", padding: "10px 14px", fontSize: "13px", color: "#b91c1c" }}>
                {errorMessage}
              </div>
            )}

            <div className="form-footer" style={{ display: "flex", justifyContent: "flex-end", gap: "10px", marginTop: "12px", borderTop: "1px solid #e2e8f0", paddingTop: "12px" }}>
              <button type="button" className="quiet-button" onClick={onClose} disabled={saving}>
                Cancel
              </button>
              <button type="submit" disabled={saving || !awardNumber.trim() || !reason.trim()}>
                {saving ? "Saving Changes…" : "Save Metadata Correction"}
              </button>
            </div>
          </form>
        )}
      </section>
    </aside>
  );
}
