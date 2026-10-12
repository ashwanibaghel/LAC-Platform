import { useState, useRef, useEffect } from "react";
import { landRecordResponse, LandRecordApiError } from "./LandRecordApiError";
import type { StaffNotification, StaffAward } from "./landRecordsStaffTypes";

interface AwardNotificationsSectionProps {
  awardId: string;
  linkedVillages?: { id: string; name: string }[];
  villageId?: string;
  notifications: StaffNotification[];
  onRefresh: () => void;
}

export function AwardNotificationsSection({
  awardId,
  linkedVillages,
  villageId,
  notifications,
  onRefresh,
}: AwardNotificationsSectionProps) {
  const effectiveVillages = linkedVillages && linkedVillages.length > 0
    ? linkedVillages
    : villageId
      ? [{ id: villageId, name: "Linked Village" }]
      : [];

  const [selectedVillageId, setSelectedVillageId] = useState<string>(() => {
    return effectiveVillages.length === 1 ? effectiveVillages[0].id : "";
  });

  const villageKey = effectiveVillages.map((v) => v.id).join(",");
  useEffect(() => {
    setSelectedVillageId(effectiveVillages.length === 1 ? effectiveVillages[0].id : "");
  }, [awardId, villageKey]);

  const [internalNotifications, setInternalNotifications] = useState<StaffNotification[]>(notifications || []);
  const [localRefresh, setLocalRefresh] = useState(0);

  useEffect(() => {
    let active = true;
    fetch(`/api/awards/${awardId}/staff-workflow?r=${Date.now()}`, { credentials: "include" })
      .then((res) => landRecordResponse<StaffAward>(res))
      .then((data) => {
        if (!active) return;
        if (data?.notifications) {
          setInternalNotifications(data.notifications);
        }
      })
      .catch(() => {});
    return () => {
      active = false;
    };
  }, [awardId, localRefresh]);

  useEffect(() => {
    if (notifications && notifications.length > 0) {
      setInternalNotifications(notifications);
    }
  }, [notifications]);

  const displayNotifications = internalNotifications;

  const [drawerOpen, setDrawerOpen] = useState(false);
  const [uploadMode, setUploadMode] = useState<"New" | "Reuse">("New");
  const [reuseNotificationId, setReuseNotificationId] = useState("");
  const [sectionPreset, setSectionPreset] = useState("4(1)");
  const [customSection, setCustomSection] = useState("");
  const [hasNoNumber, setHasNoNumber] = useState(false);
  const [notificationNumber, setNotificationNumber] = useState("");
  const [numberAbsentReason, setNumberAbsentReason] = useState("");
  const [notificationDate, setNotificationDate] = useState("");
  const [remarks, setRemarks] = useState("");
  const [file, setFile] = useState<File | null>(null);
  const [busy, setBusy] = useState(false);
  const [errorMessage, setErrorMessage] = useState("");
  const [successMessage, setSuccessMessage] = useState("");

  // Edit / Finalize state
  const [editingNotification, setEditingNotification] = useState<StaffNotification | null>(null);
  const [editSectionType, setEditSectionType] = useState("");
  const [editNumber, setEditNumber] = useState("");
  const [editAbsentReason, setEditAbsentReason] = useState("");
  const [editDate, setEditDate] = useState("");
  const [editRemarks, setEditRemarks] = useState("");
  const [editReason, setEditReason] = useState("");
  const [editBusy, setEditBusy] = useState(false);

  const [finalizingNotification, setFinalizingNotification] = useState<StaffNotification | null>(null);
  const [finalizeReason, setFinalizeReason] = useState("");
  const [finalizeBusy, setFinalizeBusy] = useState(false);

  const mutationIdRef = useRef<string>(crypto.randomUUID());

  const handleOpenRegister = () => {
    setUploadMode("New");
    setReuseNotificationId("");
    setSelectedVillageId(effectiveVillages.length === 1 ? effectiveVillages[0].id : "");
    setSectionPreset("4(1)");
    setCustomSection("");
    setHasNoNumber(false);
    setNotificationNumber("");
    setNumberAbsentReason("");
    setNotificationDate("");
    setRemarks("");
    setFile(null);
    setErrorMessage("");
    setDrawerOpen(true);
    mutationIdRef.current = crypto.randomUUID();
  };

  const handleRegisterSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (effectiveVillages.length === 0) {
      setErrorMessage("No Villages are linked to this Award. A valid linked Village is required to register notification documents.");
      return;
    }
    if (!selectedVillageId) {
      setErrorMessage("Please select a target Village for this notification document.");
      return;
    }
    if (!effectiveVillages.some((v) => v.id === selectedVillageId)) {
      setErrorMessage("The selected Village is not linked to this Award. Please select a valid linked Village.");
      return;
    }
    if (!file) {
      setErrorMessage("Please choose a source PDF file for the notification.");
      return;
    }

    const sectionType = sectionPreset === "custom" ? customSection.trim() : sectionPreset;
    if (uploadMode === "New" && !sectionType) {
      setErrorMessage("Section Type is required (e.g. 4(1), 6, 9, 17(1)).");
      return;
    }
    if (uploadMode === "New" && hasNoNumber && !numberAbsentReason.trim()) {
      setErrorMessage("When reference number is absent, an explanation reason is mandatory.");
      return;
    }
    if (uploadMode === "New" && !hasNoNumber && !notificationNumber.trim()) {
      setErrorMessage("Please provide a notification reference number or mark it absent with reason.");
      return;
    }

    setBusy(true);
    setErrorMessage("");

    const form = new FormData();
    form.append("file", file);

    const query = new URLSearchParams({
      mutationId: mutationIdRef.current,
      villageId: selectedVillageId,
    });

    if (uploadMode === "Reuse" && reuseNotificationId) {
      query.set("notificationId", reuseNotificationId);
    } else {
      query.set("sectionType", sectionType);
      if (hasNoNumber) {
        query.set("numberAbsentReason", numberAbsentReason.trim());
      } else {
        query.set("notificationNumber", notificationNumber.trim());
      }
      if (notificationDate) query.set("notificationDate", notificationDate);
      if (remarks.trim()) query.set("remarks", remarks.trim());
    }

    try {
      const res = await fetch(`/api/awards/${awardId}/notification-documents?${query}`, {
        method: "POST",
        credentials: "include",
        body: form,
      });

      await landRecordResponse(res);
      setSuccessMessage("Notification source document registered successfully.");
      setDrawerOpen(false);
      setLocalRefresh((r) => r + 1);
      onRefresh();
    } catch (err) {
      setErrorMessage(err instanceof Error ? err.message : "Failed to register notification document.");
    } finally {
      setBusy(false);
    }
  };

  const handleOpenEdit = (n: StaffNotification) => {
    setEditingNotification(n);
    setEditSectionType(n.sectionType || "");
    setEditNumber(n.notificationNumber || "");
    setEditAbsentReason(n.numberAbsentReason || "");
    setEditDate(n.notificationDate ? n.notificationDate.substring(0, 10) : "");
    setEditRemarks("");
    setEditReason("");
    setErrorMessage("");
  };

  const handleEditSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!editingNotification) return;
    if (!editReason.trim()) {
      setErrorMessage("A reason is mandatory for correcting notification metadata.");
      return;
    }

    setEditBusy(true);
    setErrorMessage("");

    const payload = {
      mutationId: crypto.randomUUID(),
      expectedRevision: editingNotification.revision,
      sectionType: editSectionType.trim(),
      notificationNumber: editNumber.trim() || null,
      notificationDate: editDate || null,
      numberAbsentReason: editAbsentReason.trim() || null,
      remarks: editRemarks.trim() || null,
      reason: editReason.trim(),
    };

    try {
      const res = await fetch(`/api/notifications/${editingNotification.id}/metadata`, {
        method: "PUT",
        credentials: "include",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(payload),
      });

      await landRecordResponse(res);
      setSuccessMessage("Notification metadata updated.");
      setEditingNotification(null);
      setLocalRefresh((r) => r + 1);
      onRefresh();
    } catch (err) {
      if (err instanceof LandRecordApiError && err.code === "VerifiedMetadataProtected") {
        setErrorMessage("This notification is finalized or verified; generic metadata editing is protected.");
      } else {
        setErrorMessage(err instanceof Error ? err.message : "Failed to update notification metadata.");
      }
    } finally {
      setEditBusy(false);
    }
  };

  const handleFinalizeSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!finalizingNotification) return;
    if (!finalizeReason.trim()) {
      setErrorMessage("A reason is mandatory for finalizing a notification.");
      return;
    }

    setFinalizeBusy(true);
    setErrorMessage("");

    const payload = {
      mutationId: crypto.randomUUID(),
      expectedRevision: finalizingNotification.revision,
      reason: finalizeReason.trim(),
    };

    try {
      const res = await fetch(`/api/notifications/${finalizingNotification.id}/finalize`, {
        method: "POST",
        credentials: "include",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(payload),
      });

      await landRecordResponse(res);
      setSuccessMessage("Notification finalized successfully.");
      setFinalizingNotification(null);
      setLocalRefresh((r) => r + 1);
      onRefresh();
    } catch (err) {
      setErrorMessage(err instanceof Error ? err.message : "Finalization failed.");
    } finally {
      setFinalizeBusy(false);
    }
  };

  return (
    <section className="section award-notifications" style={{ marginTop: "24px" }}>
      <div className="section-heading" style={{ display: "flex", justifyContent: "space-between", alignItems: "center" }}>
        <div>
          <p className="section-eyebrow" style={{ margin: 0, fontSize: "11px", fontWeight: 700, color: "#64748b", textTransform: "uppercase" }}>
            Statutory Milestones
          </p>
          <h2 style={{ margin: "2px 0 0 0", fontSize: "20px", color: "#0f172a" }}>Statutory Notifications</h2>
          <span className="hint" style={{ fontSize: "12px", color: "#64748b" }}>
            Sections 4(1), 6, 9, 17(1) & source gazette documents · Multi-document & canonical reuse supported
          </span>
        </div>
        <button type="button" className="secondary-button" onClick={handleOpenRegister}>
          + Register Notification Source
        </button>
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

      {displayNotifications.length === 0 ? (
        <div style={{ padding: "32px", textAlign: "center", border: "1px dashed #cbd5e1", borderRadius: "8px", backgroundColor: "#f8fafc", margin: "16px 0" }}>
          <p style={{ margin: "0 0 10px 0", fontWeight: 600, color: "#334155" }}>
            No statutory notifications linked to this Award yet.
          </p>
          <span style={{ fontSize: "13px", color: "#64748b" }}>
            Register Section 4(1), Section 6 declarations, or Section 9/17 notices with their gazette source PDF.
          </span>
          <div style={{ marginTop: "16px" }}>
            <button type="button" onClick={handleOpenRegister}>
              Register Notification Source
            </button>
          </div>
        </div>
      ) : (
        <div style={{ display: "flex", flexDirection: "column", gap: "10px", marginTop: "14px" }}>
          {displayNotifications.map((n) => (
            <div
              key={n.id}
              className="notification-card"
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
                <span style={{ fontSize: "20px" }}>📰</span>
                <div>
                  <div style={{ display: "flex", alignItems: "center", gap: "8px" }}>
                    <strong style={{ fontSize: "14px", color: "#0f172a" }}>
                      Section {n.sectionType}
                    </strong>
                    <span style={{ fontSize: "13px", color: "#334155" }}>
                      {n.notificationNumber ? `No. ${n.notificationNumber}` : n.numberAbsentReason ? `[No number: ${n.numberAbsentReason}]` : "[No reference]"}
                    </span>
                    <span
                      style={{
                        fontSize: "11px",
                        fontWeight: 600,
                        padding: "2px 6px",
                        borderRadius: "4px",
                        backgroundColor: n.isFinalized ? "#f0fdf4" : "#fef3c7",
                        color: n.isFinalized ? "#166534" : "#92400e",
                      }}
                    >
                      {n.isFinalized ? "Finalized" : "Needs Review / Draft"}
                    </span>
                    {n.needsReview && (
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
                    <span>Issue Date: <strong>{n.notificationDate ? n.notificationDate.substring(0, 10) : "Undated"}</strong> · </span>
                    <span>Revision: {n.revision} · </span>
                    <span>Source Documents: <strong>{n.documents?.length || 0}</strong></span>
                  </div>
                  {n.documents && n.documents.length > 0 && (
                    <div style={{ display: "flex", gap: "10px", marginTop: "6px" }}>
                      {n.documents.map((doc, idx) => (
                        <a
                          key={doc.documentId}
                          href={`/api/documents/${doc.documentId}/content`}
                          target="_blank"
                          rel="noreferrer"
                          style={{ fontSize: "12px", color: "#2563eb", textDecoration: "none" }}
                        >
                          📄 {doc.originalFileName || `Gazette PDF ${idx + 1}`}
                        </a>
                      ))}
                    </div>
                  )}
                </div>
              </div>

              <div style={{ display: "flex", alignItems: "center", gap: "8px" }}>
                {!n.isFinalized && (
                  <>
                    <button
                      type="button"
                      className="quiet-button"
                      onClick={() => handleOpenEdit(n)}
                    >
                      Edit Metadata
                    </button>
                    <button
                      type="button"
                      className="secondary-button"
                      style={{ fontSize: "12px", padding: "4px 10px" }}
                      onClick={() => {
                        setFinalizingNotification(n);
                        setFinalizeReason("");
                        setErrorMessage("");
                      }}
                    >
                      Finalize
                    </button>
                  </>
                )}
              </div>
            </div>
          ))}
        </div>
      )}

      {/* Register Notification Source Drawer */}
      {drawerOpen && (
        <aside className="workflow-drawer" aria-label="Register Notification Document" role="dialog" aria-modal="true">
          <section className="workspace-panel" style={{ maxWidth: "600px", width: "100%", margin: "0 auto" }}>
            <div className="panel-title" style={{ display: "flex", justifyContent: "space-between", alignItems: "center" }}>
              <h3 style={{ margin: 0, fontSize: "18px" }}>Register Notification Source</h3>
              <button type="button" className="quiet-button" onClick={() => setDrawerOpen(false)}>
                ✕ Close
              </button>
            </div>

            <form onSubmit={handleRegisterSubmit} style={{ display: "flex", flexDirection: "column", gap: "14px", marginTop: "12px" }}>
              {/* Target Village Context */}
              {effectiveVillages.length === 0 ? (
                <div
                  role="alert"
                  style={{
                    padding: "10px 12px",
                    background: "#fef2f2",
                    border: "1px solid #fecaca",
                    borderRadius: "6px",
                    color: "#991b1b",
                    fontSize: "13px",
                    fontWeight: 500,
                  }}
                >
                  ⚠ Warning: No Villages are linked to this Award. A valid linked Village is required before registering statutory notifications.
                </div>
              ) : effectiveVillages.length === 1 ? (
                <div
                  style={{
                    padding: "8px 12px",
                    background: "#f8fafc",
                    border: "1px solid #e2e8f0",
                    borderRadius: "6px",
                    fontSize: "13px",
                    display: "flex",
                    alignItems: "center",
                    gap: "6px",
                  }}
                >
                  <strong style={{ color: "#475569" }}>Linked Village:</strong>
                  <span style={{ fontWeight: 600, color: "#0f172a" }}>{effectiveVillages[0].name}</span>
                </div>
              ) : (
                <label>
                  Target Village *
                  <select
                    value={selectedVillageId}
                    onChange={(e) => setSelectedVillageId(e.target.value)}
                    required
                  >
                    <option value="">(Select Target Village)</option>
                    {effectiveVillages.map((v) => (
                      <option key={v.id} value={v.id}>
                        {v.name}
                      </option>
                    ))}
                  </select>
                </label>
              )}

              <div style={{ display: "flex", gap: "16px" }}>
                <label style={{ display: "flex", alignItems: "center", gap: "6px", fontSize: "13px", cursor: "pointer" }}>
                  <input
                    type="radio"
                    name="notifMode"
                    value="New"
                    checked={uploadMode === "New"}
                    onChange={() => setUploadMode("New")}
                  />
                  <strong>Register New Statutory Record</strong>
                </label>
                {displayNotifications.length > 0 && (
                  <label style={{ display: "flex", alignItems: "center", gap: "6px", fontSize: "13px", cursor: "pointer" }}>
                    <input
                      type="radio"
                      name="notifMode"
                      value="Reuse"
                      checked={uploadMode === "Reuse"}
                      onChange={() => setUploadMode("Reuse")}
                    />
                    <strong>Link to Existing Canonical Record</strong>
                  </label>
                )}
              </div>

              {uploadMode === "Reuse" ? (
                <label>
                  Select Canonical Notification *
                  <select
                    value={reuseNotificationId}
                    onChange={(e) => setReuseNotificationId(e.target.value)}
                    required
                  >
                    <option value="">(Select Notification)</option>
                    {displayNotifications.map((n) => (
                      <option key={n.id} value={n.id}>
                        Section {n.sectionType} · {n.notificationNumber || "[No number]"} ({n.notificationDate || "Undated"})
                      </option>
                    ))}
                  </select>
                </label>
              ) : (
                <>
                  <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: "10px" }}>
                    <label>
                      Section Type *
                      <select
                        value={sectionPreset}
                        onChange={(e) => setSectionPreset(e.target.value)}
                      >
                        <option value="4(1)">Section 4(1) - Preliminary Notification</option>
                        <option value="6">Section 6 - Declaration of Acquisition</option>
                        <option value="9">Section 9 - Notice to Persons Interested</option>
                        <option value="17(1)">Section 17(1) - Urgency Clause</option>
                        <option value="custom">Other / Custom Section</option>
                      </select>
                    </label>

                    {sectionPreset === "custom" && (
                      <label>
                        Custom Section *
                        <input
                          type="text"
                          value={customSection}
                          onChange={(e) => setCustomSection(e.target.value)}
                          placeholder="e.g. 1(custom), 11(1)"
                          required
                        />
                      </label>
                    )}
                  </div>

                  <div>
                    <label style={{ display: "flex", alignItems: "center", gap: "6px", fontSize: "13px", cursor: "pointer", marginBottom: "6px" }}>
                      <input
                        type="checkbox"
                        checked={hasNoNumber}
                        onChange={(e) => setHasNoNumber(e.target.checked)}
                      />
                      Reference number is absent in source document (Gazette header unreadable / not printed)
                    </label>

                    {!hasNoNumber ? (
                      <label>
                        Notification Number *
                        <input
                          type="text"
                          value={notificationNumber}
                          onChange={(e) => setNotificationNumber(e.target.value)}
                          placeholder="e.g. F.15(12)/2001/L&B/LA"
                          required
                        />
                      </label>
                    ) : (
                      <label>
                        Reason for Absent Reference Number *
                        <input
                          type="text"
                          value={numberAbsentReason}
                          onChange={(e) => setNumberAbsentReason(e.target.value)}
                          placeholder="State why reference is missing from source evidence"
                          required
                        />
                      </label>
                    )}
                  </div>

                  <label>
                    Notification Date (Source Issue Date) *
                    <input
                      type="date"
                      value={notificationDate}
                      onChange={(e) => setNotificationDate(e.target.value)}
                      required
                    />
                    <small style={{ color: "#64748b", fontSize: "11px" }}>
                      Required for statutory timeline sequencing and finalization.
                    </small>
                  </label>

                  <label>
                    Remarks
                    <textarea
                      rows={2}
                      value={remarks}
                      onChange={(e) => setRemarks(e.target.value)}
                      placeholder="Optional gazette notification remarks"
                    />
                  </label>
                </>
              )}

              <label>
                Source PDF File *
                <input
                  type="file"
                  accept="application/pdf,.pdf"
                  onChange={(e) => setFile(e.target.files?.[0] || null)}
                  required
                />
              </label>

              {errorMessage && (
                <div role="alert" style={{ backgroundColor: "#fef2f2", border: "1px solid #fecaca", color: "#991b1b", padding: "10px 14px", borderRadius: "6px", fontSize: "13px" }}>
                  {errorMessage}
                </div>
              )}

              <div className="form-footer" style={{ display: "flex", justifyContent: "flex-end", gap: "10px", marginTop: "12px", borderTop: "1px solid #e2e8f0", paddingTop: "12px" }}>
                <button type="button" className="quiet-button" onClick={() => setDrawerOpen(false)} disabled={busy}>
                  Cancel
                </button>
                <button type="submit" disabled={busy || !file || effectiveVillages.length === 0 || !selectedVillageId}>
                  {busy ? "Registering…" : "Register Source Document"}
                </button>
              </div>
            </form>
          </section>
        </aside>
      )}

      {/* Edit Notification Metadata Modal */}
      {editingNotification && (
        <aside className="workflow-drawer" aria-label="Edit Notification Metadata" role="dialog" aria-modal="true">
          <section className="workspace-panel" style={{ maxWidth: "560px", width: "100%", margin: "0 auto" }}>
            <div className="panel-title" style={{ display: "flex", justifyContent: "space-between", alignItems: "center" }}>
              <h3 style={{ margin: 0, fontSize: "18px" }}>Edit Notification Metadata</h3>
              <button type="button" className="quiet-button" onClick={() => setEditingNotification(null)}>
                ✕ Close
              </button>
            </div>

            <form onSubmit={handleEditSubmit} style={{ display: "flex", flexDirection: "column", gap: "12px", marginTop: "12px" }}>
              <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: "10px" }}>
                <label>
                  Section Type *
                  <input
                    type="text"
                    value={editSectionType}
                    onChange={(e) => setEditSectionType(e.target.value)}
                    required
                  />
                </label>
                <label>
                  Notification Number
                  <input
                    type="text"
                    value={editNumber}
                    onChange={(e) => setEditNumber(e.target.value)}
                    placeholder="Leave empty if absent"
                  />
                </label>
              </div>

              {!editNumber.trim() && (
                <label>
                  Reason for Absent Number
                  <input
                    type="text"
                    value={editAbsentReason}
                    onChange={(e) => setEditAbsentReason(e.target.value)}
                    placeholder="Mandatory if number is empty"
                  />
                </label>
              )}

              <label>
                Notification Date
                <input
                  type="date"
                  value={editDate}
                  onChange={(e) => setEditDate(e.target.value)}
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

              <label>
                Officer Reason for Correction *
                <textarea
                  rows={2}
                  value={editReason}
                  onChange={(e) => setEditReason(e.target.value)}
                  placeholder="Mandatory explanation for updating statutory record"
                  required
                />
              </label>

              <div className="form-footer" style={{ display: "flex", justifyContent: "flex-end", gap: "10px", marginTop: "12px", borderTop: "1px solid #e2e8f0", paddingTop: "12px" }}>
                <button type="button" className="quiet-button" onClick={() => setEditingNotification(null)} disabled={editBusy}>
                  Cancel
                </button>
                <button type="submit" disabled={editBusy || !editReason.trim()}>
                  {editBusy ? "Saving…" : "Save Metadata"}
                </button>
              </div>
            </form>
          </section>
        </aside>
      )}

      {/* Finalize Dialog */}
      {finalizingNotification && (
        <aside className="workflow-drawer" aria-label="Finalize Notification" role="dialog" aria-modal="true">
          <section className="workspace-panel" style={{ maxWidth: "520px", width: "100%", margin: "0 auto" }}>
            <div className="panel-title" style={{ display: "flex", justifyContent: "space-between", alignItems: "center" }}>
              <h3 style={{ margin: 0, fontSize: "18px" }}>Finalize Notification</h3>
              <button type="button" className="quiet-button" onClick={() => setFinalizingNotification(null)}>
                ✕ Close
              </button>
            </div>

            <form onSubmit={handleFinalizeSubmit} style={{ display: "flex", flexDirection: "column", gap: "14px", marginTop: "12px" }}>
              <div style={{ backgroundColor: "#eff6ff", border: "1px solid #bfdbfe", padding: "10px 14px", borderRadius: "6px", fontSize: "13px", color: "#1e40af" }}>
                <strong>Official Milestone Finalization:</strong>
                <p style={{ margin: "4px 0 0 0" }}>
                  Finalizing Section {finalizingNotification.sectionType} ({finalizingNotification.notificationNumber || "No number"}) locks its canonical status against unreviewed modifications. Requires verified source date and linked PDF.
                </p>
              </div>

              <label>
                Officer Verification Reason *
                <textarea
                  rows={3}
                  value={finalizeReason}
                  onChange={(e) => setFinalizeReason(e.target.value)}
                  placeholder="e.g. Verified against gazette copy on record"
                  required
                />
              </label>

              <div className="form-footer" style={{ display: "flex", justifyContent: "flex-end", gap: "10px", marginTop: "12px", borderTop: "1px solid #e2e8f0", paddingTop: "12px" }}>
                <button type="button" className="quiet-button" onClick={() => setFinalizingNotification(null)} disabled={finalizeBusy}>
                  Cancel
                </button>
                <button
                  type="submit"
                  disabled={finalizeBusy || !finalizeReason.trim()}
                  style={{ backgroundColor: "#16a34a", color: "#ffffff" }}
                >
                  {finalizeBusy ? "Finalizing…" : "Confirm Finalization"}
                </button>
              </div>
            </form>
          </section>
        </aside>
      )}
    </section>
  );
}
