import { useState, useEffect, useRef } from "react";
import { landRecordResponse, LandRecordApiError, type PdfOwnershipConflict } from "./LandRecordApiError";
import { AwardPdfOwnershipConflictPanel } from "./AwardPdfOwnershipConflictPanel";
import type { CoreAttachment, CoreRole } from "./landRecordsStaffTypes";

interface AwardCoreDocumentsSectionProps {
  awardId: string;
  linkedVillages: { id: string; name: string }[];
}

export function AwardCoreDocumentsSection({
  awardId,
  linkedVillages,
}: AwardCoreDocumentsSectionProps) {
  const [attachments, setAttachments] = useState<CoreAttachment[]>([]);
  const [loading, setLoading] = useState(true);
  const [refresh, setRefresh] = useState(0);
  const [errorMessage, setErrorMessage] = useState("");
  const [successMessage, setSuccessMessage] = useState("");
  const [showDetached, setShowDetached] = useState(false);

  // Upload drawer state
  const [uploadDrawerOpen, setUploadDrawerOpen] = useState(false);
  const [selectedRole, setSelectedRole] = useState<CoreRole>("Award");
  const [uploadMode, setUploadMode] = useState<"Direct" | "UploadAndAnalyze">("Direct");
  const [uploadVillageId, setUploadVillageId] = useState<string>(linkedVillages[0]?.id || "");
  const [uploadLegalDate, setUploadLegalDate] = useState("");
  const [uploadRefNumber, setUploadRefNumber] = useState("");
  const [uploadPartNumber, setUploadPartNumber] = useState("");
  const [uploadTitle, setUploadTitle] = useState("");
  const [uploadRemarks, setUploadRemarks] = useState("");
  const [uploadFile, setUploadFile] = useState<File | null>(null);
  const [uploadBusy, setUploadBusy] = useState(false);
  const [uploadConflict, setUploadConflict] = useState<PdfOwnershipConflict | null>(null);

  // Edit / Detach modal state
  const [activeAttachment, setActiveAttachment] = useState<CoreAttachment | null>(null);
  const [editAction, setEditAction] = useState<"Update" | "Detach" | "Restore">("Update");
  const [editLegalDate, setEditLegalDate] = useState("");
  const [editRefNumber, setEditRefNumber] = useState("");
  const [editPartNumber, setEditPartNumber] = useState("");
  const [editTitle, setEditTitle] = useState("");
  const [editRemarks, setEditRemarks] = useState("");
  const [editReason, setEditReason] = useState("");
  const [editBusy, setEditBusy] = useState(false);
  const editMutationIdRef = useRef<string>(crypto.randomUUID());

  useEffect(() => {
    let active = true;
    setLoading(true);
    setErrorMessage("");

    fetch(`/api/awards/${awardId}/core-documents?r=${refresh}`, { credentials: "include" })
      .then((res) => landRecordResponse<CoreAttachment[]>(res))
      .then((data) => {
        if (!active) return;
        setAttachments(data || []);
        setLoading(false);
      })
      .catch((err) => {
        if (!active) return;
        setErrorMessage(err instanceof Error ? err.message : "Failed to load core documents.");
        setLoading(false);
      });

    return () => {
      active = false;
    };
  }, [awardId, refresh]);

  const activeRecords = attachments.filter((a) => !a.isDetached);
  const detachedRecords = attachments.filter((a) => a.isDetached);

  const handleOpenUpload = (defaultRole: CoreRole = "Award") => {
    setSelectedRole(defaultRole);
    setUploadMode("Direct");
    setUploadVillageId(linkedVillages[0]?.id || "");
    setUploadLegalDate("");
    setUploadRefNumber("");
    setUploadPartNumber("");
    setUploadTitle("");
    setUploadRemarks("");
    setUploadFile(null);
    setUploadConflict(null);
    setUploadDrawerOpen(true);
  };

  const handleUploadSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!uploadFile) {
      setErrorMessage("Please choose a PDF file to upload.");
      return;
    }
    if (selectedRole === "PossessionProceeding" && !uploadLegalDate) {
      setErrorMessage("Legal Date is strictly required for Possession Proceedings.");
      return;
    }

    setUploadBusy(true);
    setErrorMessage("");
    setUploadConflict(null);

    const form = new FormData();
    form.append("file", uploadFile);

    const query = new URLSearchParams({
      role: selectedRole,
      mode: uploadMode,
    });
    if (uploadVillageId) query.set("villageId", uploadVillageId);
    if (uploadLegalDate) query.set("legalDate", uploadLegalDate);
    if (uploadRefNumber.trim()) query.set("referenceNumber", uploadRefNumber.trim());
    if (uploadPartNumber.trim()) query.set("partNumber", uploadPartNumber.trim());
    if (uploadTitle.trim()) query.set("title", uploadTitle.trim());
    if (uploadRemarks.trim()) query.set("remarks", uploadRemarks.trim());

    try {
      const res = await fetch(`/api/awards/${awardId}/core-documents?${query}`, {
        method: "POST",
        credentials: "include",
        body: form,
      });

      const result = await landRecordResponse<{ duplicate?: boolean; documentId: string }>(res);
      setSuccessMessage(
        result.duplicate
          ? "PDF already attached. Existing source record reused."
          : `Document uploaded and linked as ${selectedRole}.`
      );
      setUploadDrawerOpen(false);
      setRefresh((r) => r + 1);
    } catch (err) {
      if (err instanceof LandRecordApiError && err.code === "PDF_OWNERSHIP_CONFLICT") {
        setUploadConflict(err.conflicts || null);
      } else {
        setErrorMessage(err instanceof Error ? err.message : "Upload failed.");
      }
    } finally {
      setUploadBusy(false);
    }
  };

  const handleOpenEdit = (att: CoreAttachment, action: "Update" | "Detach" | "Restore") => {
    setActiveAttachment(att);
    setEditAction(action);
    setEditLegalDate(att.legalDate ? att.legalDate.substring(0, 10) : "");
    setEditRefNumber(att.referenceNumber || "");
    setEditPartNumber(att.partNumber || "");
    setEditTitle(att.title || "");
    setEditRemarks(att.remarks || "");
    setEditReason("");
    editMutationIdRef.current = crypto.randomUUID();
  };

  const handleEditSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!activeAttachment) return;
    if (!editReason.trim()) {
      setErrorMessage("A reason is mandatory for modifying attachment relationships.");
      return;
    }

    setEditBusy(true);
    setErrorMessage("");

    const payload = {
      mutationId: editMutationIdRef.current,
      expectedRevision: activeAttachment.revision,
      action: editAction,
      legalDate: editLegalDate || null,
      referenceNumber: editRefNumber.trim() || null,
      partNumber: editPartNumber.trim() || null,
      title: editTitle.trim() || null,
      remarks: editRemarks.trim() || null,
      reason: editReason.trim(),
    };

    try {
      const res = await fetch(
        `/api/awards/${awardId}/core-document-links/${activeAttachment.attachmentId}`,
        {
          method: "PUT",
          credentials: "include",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify(payload),
        }
      );

      await landRecordResponse(res);
      setSuccessMessage(
        editAction === "Detach"
          ? "Document detached from active list. History retained."
          : editAction === "Restore"
          ? "Document attachment restored."
          : "Attachment metadata updated."
      );
      setActiveAttachment(null);
      setRefresh((r) => r + 1);
    } catch (err) {
      setErrorMessage(err instanceof Error ? err.message : "Failed to update attachment.");
    } finally {
      setEditBusy(false);
    }
  };

  const roleBuckets: { role: CoreRole; label: string; icon: string; count: number }[] = [
    { role: "Award", label: "Award PDF", icon: "📜", count: activeRecords.filter((a) => a.role === "Award").length },
    { role: "PossessionProceeding", label: "Possession Proceedings", icon: "📌", count: activeRecords.filter((a) => a.role === "PossessionProceeding").length },
    { role: "StatementA", label: "Statement A", icon: "📊", count: activeRecords.filter((a) => a.role === "StatementA").length },
    { role: "NM", label: "Naksha Muntazamin (NM)", icon: "🗺️", count: activeRecords.filter((a) => a.role === "NM").length },
  ];

  return (
    <section className="section award-documents" style={{ marginTop: "24px" }}>
      <div className="section-heading" style={{ display: "flex", justifyContent: "space-between", alignItems: "center" }}>
        <div>
          <p className="section-eyebrow" style={{ margin: 0, fontSize: "11px", fontWeight: 700, color: "#64748b", textTransform: "uppercase" }}>
            Official Evidentiary Graph
          </p>
          <h2 style={{ margin: "2px 0 0 0", fontSize: "20px", color: "#0f172a" }}>Core Documents</h2>
          <span className="hint" style={{ fontSize: "12px", color: "#64748b" }}>
            Multiple documents per role supported · Full audit trail & provenance retained
          </span>
        </div>
        <div style={{ display: "flex", gap: "8px" }}>
          <button type="button" className="secondary-button" onClick={() => handleOpenUpload("Award")}>
            + Add Document
          </button>
        </div>
      </div>

      {successMessage && (
        <div role="status" style={{ backgroundColor: "#f0fdf4", border: "1px solid #bbf7d0", color: "#166534", padding: "10px 14px", borderRadius: "6px", margin: "12px 0", fontSize: "13px" }}>
          ✓ {successMessage}
        </div>
      )}

      {errorMessage && (
        <div role="alert" style={{ backgroundColor: "#fef2f2", border: "1px solid #fecaca", color: "#991b1b", padding: "10px 14px", borderRadius: "6px", margin: "12px 0", fontSize: "13px" }}>
          ⚠️ {errorMessage}
        </div>
      )}

      {/* Role Summary Badges */}
      <div style={{ display: "flex", gap: "12px", margin: "16px 0", flexWrap: "wrap" }}>
        {roleBuckets.map((b) => (
          <div
            key={b.role}
            style={{
              display: "flex",
              alignItems: "center",
              gap: "8px",
              padding: "8px 14px",
              borderRadius: "6px",
              backgroundColor: b.count > 0 ? "#f8fafc" : "#fafafa",
              border: `1px solid ${b.count > 0 ? "#cbd5e1" : "#e2e8f0"}`,
              fontSize: "13px",
            }}
          >
            <span>{b.icon}</span>
            <strong>{b.label}:</strong>
            <span style={{ fontWeight: 700, color: b.count > 0 ? "#0f172a" : "#94a3b8" }}>
              {b.count} {b.count === 1 ? "file" : "files"}
            </span>
            <button
              type="button"
              className="quiet-button"
              style={{ fontSize: "11px", padding: "2px 6px", marginLeft: "4px" }}
              onClick={() => handleOpenUpload(b.role)}
            >
              + Add
            </button>
          </div>
        ))}
      </div>

      {loading ? (
        <div style={{ padding: "30px 0", textAlign: "center", color: "#64748b" }}>
          Loading core documents…
        </div>
      ) : activeRecords.length === 0 ? (
        <div style={{ padding: "32px", textAlign: "center", border: "1px dashed #cbd5e1", borderRadius: "8px", backgroundColor: "#f8fafc", margin: "16px 0" }}>
          <p style={{ margin: "0 0 10px 0", fontWeight: 600, color: "#334155" }}>
            No core documents uploaded for this Award yet.
          </p>
          <span style={{ fontSize: "13px", color: "#64748b" }}>
            Upload the primary Award PDF, Possession Proceedings, Statement A, or NM to establish source evidence.
          </span>
          <div style={{ marginTop: "16px" }}>
            <button type="button" onClick={() => handleOpenUpload("Award")}>
              Upload Award PDF
            </button>
          </div>
        </div>
      ) : (
        <div style={{ display: "flex", flexDirection: "column", gap: "10px", marginTop: "12px" }}>
          {activeRecords.map((att) => (
            <div
              key={att.attachmentId}
              className="award-document-row"
              style={{
                display: "flex",
                alignItems: "center",
                justifyContent: "space-between",
                padding: "12px 16px",
                backgroundColor: "#ffffff",
                border: "1px solid #e2e8f0",
                borderRadius: "8px",
              }}
            >
              <div style={{ display: "flex", alignItems: "center", gap: "12px" }}>
                <span style={{ fontSize: "20px" }}>
                  {att.role === "Award" ? "📜" : att.role === "PossessionProceeding" ? "📌" : att.role === "StatementA" ? "📊" : "🗺️"}
                </span>
                <div>
                  <div style={{ display: "flex", alignItems: "center", gap: "8px" }}>
                    <strong style={{ fontSize: "14px", color: "#0f172a" }}>
                      {att.originalFileName}
                    </strong>
                    <span
                      style={{
                        fontSize: "11px",
                        fontWeight: 600,
                        padding: "2px 6px",
                        borderRadius: "4px",
                        backgroundColor: "#f1f5f9",
                        color: "#475569",
                      }}
                    >
                      {att.role}
                    </span>
                    {att.isSharedSource && (
                      <span
                        style={{
                          fontSize: "11px",
                          fontWeight: 600,
                          padding: "2px 6px",
                          borderRadius: "4px",
                          backgroundColor: "#fef3c7",
                          color: "#92400e",
                        }}
                      >
                        Shared Source
                      </span>
                    )}
                    {att.needsReview && (
                      <span
                        style={{
                          fontSize: "11px",
                          fontWeight: 600,
                          padding: "2px 6px",
                          borderRadius: "4px",
                          backgroundColor: "#fef2f2",
                          color: "#991b1b",
                        }}
                      >
                        Needs Review
                      </span>
                    )}
                  </div>
                  <div style={{ fontSize: "12px", color: "#64748b", marginTop: "2px" }}>
                    {att.legalDate && <span>Legal Date: <strong>{att.legalDate.substring(0, 10)}</strong> · </span>}
                    {att.partNumber && <span>Part: {att.partNumber} · </span>}
                    {att.referenceNumber && <span>Ref: {att.referenceNumber} · </span>}
                    {att.title && <span>Title: {att.title} · </span>}
                    <span>Uploaded: {att.uploadedAt.substring(0, 10)}</span>
                  </div>
                </div>
              </div>

              <div style={{ display: "flex", alignItems: "center", gap: "8px" }}>
                <a
                  href={`/api/documents/${att.documentId}/content`}
                  target="_blank"
                  rel="noreferrer"
                  className="quiet-button"
                  style={{ textDecoration: "none", display: "inline-flex", alignItems: "center" }}
                >
                  View PDF
                </a>
                <button
                  type="button"
                  className="quiet-button"
                  onClick={() => handleOpenEdit(att, "Update")}
                >
                  Edit Details
                </button>
                <button
                  type="button"
                  className="quiet-button"
                  style={{ color: "#dc2626" }}
                  onClick={() => handleOpenEdit(att, "Detach")}
                >
                  Detach
                </button>
              </div>
            </div>
          ))}
        </div>
      )}

      {/* Detached History Section */}
      {detachedRecords.length > 0 && (
        <div style={{ marginTop: "20px" }}>
          <button
            type="button"
            className="quiet-button"
            style={{ fontSize: "12px", color: "#64748b" }}
            onClick={() => setShowDetached(!showDetached)}
          >
            {showDetached ? "▼ Hide" : "▶ Show"} {detachedRecords.length} Detached Historical {detachedRecords.length === 1 ? "Record" : "Records"}
          </button>

          {showDetached && (
            <div style={{ display: "flex", flexDirection: "column", gap: "8px", marginTop: "8px" }}>
              {detachedRecords.map((att) => (
                <div
                  key={att.attachmentId}
                  style={{
                    display: "flex",
                    alignItems: "center",
                    justifyContent: "space-between",
                    padding: "8px 14px",
                    backgroundColor: "#f8fafc",
                    border: "1px dashed #cbd5e1",
                    borderRadius: "6px",
                    opacity: 0.85,
                  }}
                >
                  <div>
                    <span style={{ fontSize: "13px", color: "#64748b" }}>
                      [Detached] {att.originalFileName} ({att.role})
                    </span>
                  </div>
                  <button
                    type="button"
                    className="quiet-button"
                    style={{ color: "#16a34a" }}
                    onClick={() => handleOpenEdit(att, "Restore")}
                  >
                    Restore Attachment
                  </button>
                </div>
              ))}
            </div>
          )}
        </div>
      )}

      {/* Upload Drawer */}
      {uploadDrawerOpen && (
        <aside className="workflow-drawer" aria-label="Upload Core Document" role="dialog" aria-modal="true">
          <section className="workspace-panel" style={{ maxWidth: "600px", width: "100%", margin: "0 auto" }}>
            <div className="panel-title" style={{ display: "flex", justifyContent: "space-between", alignItems: "center" }}>
              <h3 style={{ margin: 0, fontSize: "18px" }}>Upload Core Document</h3>
              <button type="button" className="quiet-button" onClick={() => setUploadDrawerOpen(false)}>
                ✕ Close
              </button>
            </div>

            {uploadConflict ? (
              <div style={{ marginTop: "16px" }}>
                <AwardPdfOwnershipConflictPanel
                  conflict={uploadConflict}
                  onResolved={(_res) => {
                    setUploadConflict(null);
                    setUploadDrawerOpen(false);
                    setSuccessMessage("Ownership link resolved successfully.");
                    setRefresh((r) => r + 1);
                  }}
                />
              </div>
            ) : (
              <form onSubmit={handleUploadSubmit} style={{ display: "flex", flexDirection: "column", gap: "14px", marginTop: "12px" }}>
                <label>
                  Document Role *
                  <select
                    value={selectedRole}
                    onChange={(e) => {
                      setSelectedRole(e.target.value as CoreRole);
                      if (e.target.value !== "Award") setUploadMode("Direct");
                    }}
                  >
                    <option value="Award">Award PDF (Official pronouncement)</option>
                    <option value="PossessionProceeding">Possession Proceeding (Site takeover memo)</option>
                    <option value="StatementA">Statement A (Compensation schedule)</option>
                    <option value="NM">Naksha Muntazamin (Field map schedule)</option>
                  </select>
                </label>

                {/* Upload Mode Selector: Direct vs UploadAndAnalyze */}
                <div style={{ backgroundColor: "#f8fafc", border: "1px solid #e2e8f0", borderRadius: "6px", padding: "10px 12px" }}>
                  <label style={{ display: "block", marginBottom: "6px", fontWeight: 600, fontSize: "12px" }}>
                    Processing Mode
                  </label>
                  <div style={{ display: "flex", gap: "16px" }}>
                    <label style={{ display: "flex", alignItems: "center", gap: "6px", fontSize: "13px", cursor: "pointer" }}>
                      <input
                        type="radio"
                        name="uploadMode"
                        value="Direct"
                        checked={uploadMode === "Direct"}
                        onChange={() => setUploadMode("Direct")}
                      />
                      <span>
                        <strong>Direct:</strong> Store & link source directly (No OCR or confidence scores)
                      </span>
                    </label>
                    {selectedRole === "Award" && (
                      <label style={{ display: "flex", alignItems: "center", gap: "6px", fontSize: "13px", cursor: "pointer" }}>
                        <input
                          type="radio"
                          name="uploadMode"
                          value="UploadAndAnalyze"
                          checked={uploadMode === "UploadAndAnalyze"}
                          onChange={() => setUploadMode("UploadAndAnalyze")}
                        />
                        <span>
                          <strong>Upload & Analyze:</strong> Queue background OCR extraction
                        </span>
                      </label>
                    )}
                  </div>
                </div>

                {linkedVillages.length > 1 && (
                  <label>
                    Target Village *
                    <select value={uploadVillageId} onChange={(e) => setUploadVillageId(e.target.value)}>
                      {linkedVillages.map((v) => (
                        <option key={v.id} value={v.id}>
                          {v.name}
                        </option>
                      ))}
                    </select>
                  </label>
                )}

                {selectedRole === "PossessionProceeding" && (
                  <label>
                    Legal Possession Date *
                    <input
                      type="date"
                      value={uploadLegalDate}
                      onChange={(e) => setUploadLegalDate(e.target.value)}
                      required
                    />
                    <small style={{ color: "#64748b", fontSize: "11px" }}>
                      Actual date when land was physically or statutorily possessed. Required for legal timeline.
                    </small>
                  </label>
                )}

                {selectedRole !== "PossessionProceeding" && (
                  <label>
                    Legal / Document Date (Optional)
                    <input
                      type="date"
                      value={uploadLegalDate}
                      onChange={(e) => setUploadLegalDate(e.target.value)}
                    />
                  </label>
                )}

                <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: "10px" }}>
                  <label>
                    Part Number
                    <input
                      type="text"
                      placeholder="e.g. Part 1, Supplementary"
                      value={uploadPartNumber}
                      onChange={(e) => setUploadPartNumber(e.target.value)}
                    />
                  </label>
                  <label>
                    Reference Number
                    <input
                      type="text"
                      placeholder="e.g. Ref 42/LAC"
                      value={uploadRefNumber}
                      onChange={(e) => setUploadRefNumber(e.target.value)}
                    />
                  </label>
                </div>

                <label>
                  Title / Description
                  <input
                    type="text"
                    placeholder="Optional title or schedule name"
                    value={uploadTitle}
                    onChange={(e) => setUploadTitle(e.target.value)}
                  />
                </label>

                <label>
                  Remarks
                  <textarea
                    rows={2}
                    placeholder="Optional provenance remarks"
                    value={uploadRemarks}
                    onChange={(e) => setUploadRemarks(e.target.value)}
                  />
                </label>

                <label>
                  PDF Source File *
                  <input
                    type="file"
                    accept="application/pdf,.pdf"
                    onChange={(e) => setUploadFile(e.target.files?.[0] || null)}
                    required
                  />
                </label>

                <div className="form-footer" style={{ display: "flex", justifyContent: "flex-end", gap: "10px", marginTop: "12px", borderTop: "1px solid #e2e8f0", paddingTop: "12px" }}>
                  <button type="button" className="quiet-button" onClick={() => setUploadDrawerOpen(false)} disabled={uploadBusy}>
                    Cancel
                  </button>
                  <button type="submit" disabled={uploadBusy || !uploadFile || (selectedRole === "PossessionProceeding" && !uploadLegalDate)}>
                    {uploadBusy ? "Uploading…" : "Upload & Link"}
                  </button>
                </div>
              </form>
            )}
          </section>
        </aside>
      )}

      {/* Edit / Detach Modal */}
      {activeAttachment && (
        <aside className="workflow-drawer" aria-label="Attachment Action" role="dialog" aria-modal="true">
          <section className="workspace-panel" style={{ maxWidth: "560px", width: "100%", margin: "0 auto" }}>
            <div className="panel-title" style={{ display: "flex", justifyContent: "space-between", alignItems: "center" }}>
              <h3 style={{ margin: 0, fontSize: "18px" }}>
                {editAction === "Detach" ? "Detach Attachment" : editAction === "Restore" ? "Restore Attachment" : "Edit Attachment Details"}
              </h3>
              <button type="button" className="quiet-button" onClick={() => setActiveAttachment(null)}>
                ✕ Close
              </button>
            </div>

            <form onSubmit={handleEditSubmit} style={{ display: "flex", flexDirection: "column", gap: "12px", marginTop: "12px" }}>
              <p style={{ margin: 0, fontSize: "13px", color: "#475569" }}>
                File: <strong>{activeAttachment.originalFileName}</strong> ({activeAttachment.role})
              </p>

              {editAction === "Update" && (
                <>
                  <label>
                    Legal Date
                    <input
                      type="date"
                      value={editLegalDate}
                      onChange={(e) => setEditLegalDate(e.target.value)}
                    />
                  </label>
                  <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: "10px" }}>
                    <label>
                      Part Number
                      <input
                        type="text"
                        value={editPartNumber}
                        onChange={(e) => setEditPartNumber(e.target.value)}
                      />
                    </label>
                    <label>
                      Reference Number
                      <input
                        type="text"
                        value={editRefNumber}
                        onChange={(e) => setEditRefNumber(e.target.value)}
                      />
                    </label>
                  </div>
                  <label>
                    Title
                    <input
                      type="text"
                      value={editTitle}
                      onChange={(e) => setEditTitle(e.target.value)}
                    />
                  </label>
                  <label>
                    Remarks
                    <textarea
                      rows={2}
                      value={editRemarks}
                      onChange={(e) => setEditRemarks(e.target.value)}
                    />
                  </label>
                </>
              )}

              {editAction === "Detach" && (
                <div style={{ backgroundColor: "#fef2f2", border: "1px solid #fecaca", padding: "10px 14px", borderRadius: "6px", fontSize: "13px", color: "#991b1b" }}>
                  <strong>Confirm Detach:</strong> The attachment will be hidden from active lists and the village timeline.
                  Source bytes, hashes, and extraction history will remain in the database audit log.
                </div>
              )}

              {editAction === "Restore" && (
                <div style={{ backgroundColor: "#eff6ff", border: "1px solid #bfdbfe", padding: "10px 14px", borderRadius: "6px", fontSize: "13px", color: "#1e40af" }}>
                  <strong>Confirm Restore:</strong> The attachment will be reactivated in official lists without creating duplicate files.
                </div>
              )}

              <label>
                Officer Reason *
                <textarea
                  rows={2}
                  value={editReason}
                  onChange={(e) => setEditReason(e.target.value)}
                  placeholder="Official justification for this correction"
                  required
                />
              </label>

              <div className="form-footer" style={{ display: "flex", justifyContent: "flex-end", gap: "10px", marginTop: "12px", borderTop: "1px solid #e2e8f0", paddingTop: "12px" }}>
                <button type="button" className="quiet-button" onClick={() => setActiveAttachment(null)} disabled={editBusy}>
                  Cancel
                </button>
                <button
                  type="submit"
                  disabled={editBusy || !editReason.trim()}
                  style={{
                    backgroundColor: editAction === "Detach" ? "#dc2626" : "#2563eb",
                    color: "#ffffff",
                  }}
                >
                  {editBusy ? "Saving…" : editAction === "Detach" ? "Confirm Detach" : editAction === "Restore" ? "Restore" : "Save Changes"}
                </button>
              </div>
            </form>
          </section>
        </aside>
      )}
    </section>
  );
}
