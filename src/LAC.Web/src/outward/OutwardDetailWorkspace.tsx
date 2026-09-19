import React, { useState, useEffect, useCallback } from "react";
import { useParams, Link, useNavigate } from "react-router-dom";
import { useAuth } from "../auth/AuthProvider";
import type { OutwardDetail, OutwardRegistrationContext } from "./types";
import "./outward.css";

export const OutwardDetailWorkspace: React.FC = () => {
  const { id } = useParams<{ id: string }>();
  const navigate = useNavigate();
  const { hasPermission } = useAuth();

  const [outward, setOutward] = useState<OutwardDetail | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [activeTab, setActiveTab] = useState<"overview" | "documents" | "links" | "events">("overview");

  // Context options for lookups
  const [context, setContext] = useState<OutwardRegistrationContext | null>(null);

  // Modals state
  const [showDispatchModal, setShowDispatchModal] = useState(false);
  const [showCancelModal, setShowCancelModal] = useState(false);
  const [showEditMetadataModal, setShowEditMetadataModal] = useState(false);
  const [showMainDocModal, setShowMainDocModal] = useState(false);
  const [showAttachModal, setShowAttachModal] = useState(false);
  const [showLinkDakModal, setShowLinkDakModal] = useState(false);
  const [showLinkMatterModal, setShowLinkMatterModal] = useState(false);

  // Dispatch form state
  const [dispatchDate, setDispatchDate] = useState(() => new Date().toISOString().split("T")[0]);
  const [dispatchMode, setDispatchMode] = useState("SpeedPost");
  const [dispatchRef, setDispatchRef] = useState("");
  const [dispatchRemarks, setDispatchRemarks] = useState("");
  const [dispatching, setDispatching] = useState(false);

  // Cancel form state
  const [cancellationReason, setCancellationReason] = useState("");
  const [cancelling, setCancelling] = useState(false);

  // Edit metadata form state
  const [editSubject, setEditSubject] = useState("");
  const [editRecipientName, setEditRecipientName] = useState("");
  const [editRecipientDesignation, setEditRecipientDesignation] = useState("");
  const [editRecipientDepartment, setEditRecipientDepartment] = useState("");
  const [editRecipientAddress, setEditRecipientAddress] = useState("");
  const [editRecipientEmail, setEditRecipientEmail] = useState("");
  const [editRecipientPhone, setEditRecipientPhone] = useState("");
  const [editOfficeRef, setEditOfficeRef] = useState("");
  const [editRemarks, setEditRemarks] = useState("");
  const [editDeskId, setEditDeskId] = useState("");
  const [editWorkstreamId, setEditWorkstreamId] = useState("");
  const [savingMetadata, setSavingMetadata] = useState(false);

  // Document upload state
  const [mainDocFile, setMainDocFile] = useState<File | null>(null);
  const [uploadingMainDoc, setUploadingMainDoc] = useState(false);

  // Attachment upload state
  const [attachFile, setAttachFile] = useState<File | null>(null);
  const [attachTitle, setAttachTitle] = useState("");
  const [attachType, setAttachType] = useState("Annexure");
  const [uploadingAttach, setUploadingAttach] = useState(false);

  // Link Dak state
  const [dakLookupId, setDakLookupId] = useState("");
  const [dakRelType, setDakRelType] = useState("PrimaryReply");
  const [dakIsPrimary, setDakIsPrimary] = useState(false);
  const [dakRemarks, setDakRemarks] = useState("");
  const [linkingDak, setLinkingDak] = useState(false);

  // Link Matter state
  const [matterLookupId, setMatterLookupId] = useState("");
  const [linkingMatter, setLinkingMatter] = useState(false);

  const loadOutward = useCallback(async () => {
    if (!id) return;
    try {
      setLoading(true);
      setError(null);
      const res = await fetch(`/api/outward/${id}`, { credentials: "include" });
      if (!res.ok) {
        if (res.status === 403) throw new Error("Access denied: You do not have permission to view this Outward record.");
        if (res.status === 404) throw new Error("Outward record not found.");
        throw new Error("Failed to load Outward record.");
      }
      const data = (await res.json()) as OutwardDetail;
      setOutward(data);

      // Pre-fill edit metadata form
      setEditSubject(data.subject);
      setEditRecipientName(data.recipientName);
      setEditRecipientDesignation(data.recipientDesignation || "");
      setEditRecipientDepartment(data.recipientDepartment || "");
      setEditRecipientAddress(data.recipientAddress || "");
      setEditRecipientEmail(data.recipientEmail || "");
      setEditRecipientPhone(data.recipientPhone || "");
      setEditOfficeRef(data.officeReferenceNumber || "");
      setEditRemarks(data.remarks || "");
      setEditDeskId(data.issuingDeskId);
      setEditWorkstreamId(data.workstreamId || "");
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : "Failed to load Outward record.");
    } finally {
      setLoading(false);
    }
  }, [id]);

  useEffect(() => {
    void loadOutward();
    fetch("/api/outward/context", { credentials: "include" })
      .then((r) => (r.ok ? (r.json() as Promise<OutwardRegistrationContext>) : null))
      .then((d) => {
        if (d) setContext(d);
      })
      .catch(() => {});
  }, [loadOutward]);

  // Dispatch Action
  const handleDispatch = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!outward) return;
    try {
      setDispatching(true);
      const res = await fetch(`/api/outward/${outward.id}/dispatch`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        credentials: "include",
        body: JSON.stringify({
          dispatchDate,
          dispatchMode,
          dispatchReferenceNumber: dispatchRef.trim() || undefined,
          remarks: dispatchRemarks.trim() || undefined,
          expectedRevision: outward.revision,
        }),
      });
      if (!res.ok) {
        const d = await res.json().catch(() => null);
        throw new Error(d?.detail || d?.message || "Failed to dispatch Outward record.");
      }
      setShowDispatchModal(false);
      await loadOutward();
    } catch (err: unknown) {
      alert(err instanceof Error ? err.message : "Dispatch failed.");
    } finally {
      setDispatching(false);
    }
  };

  // Cancel Action
  const handleCancel = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!outward) return;
    if (!cancellationReason.trim() || cancellationReason.trim().length < 5) {
      alert("A meaningful cancellation reason (at least 5 characters) is mandatory.");
      return;
    }
    try {
      setCancelling(true);
      const res = await fetch(`/api/outward/${outward.id}/cancel`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        credentials: "include",
        body: JSON.stringify({
          cancellationReason: cancellationReason.trim(),
          expectedRevision: outward.revision,
        }),
      });
      if (!res.ok) {
        const d = await res.json().catch(() => null);
        throw new Error(d?.detail || d?.message || "Failed to cancel Outward record.");
      }
      setShowCancelModal(false);
      await loadOutward();
    } catch (err: unknown) {
      alert(err instanceof Error ? err.message : "Cancellation failed.");
    } finally {
      setCancelling(false);
    }
  };

  // Edit Metadata Action
  const handleSaveMetadata = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!outward) return;
    try {
      setSavingMetadata(true);
      const res = await fetch(`/api/outward/${outward.id}`, {
        method: "PUT",
        headers: { "Content-Type": "application/json" },
        credentials: "include",
        body: JSON.stringify({
          subject: editSubject.trim(),
          recipientName: editRecipientName.trim(),
          recipientDesignation: editRecipientDesignation.trim() || undefined,
          recipientDepartment: editRecipientDepartment.trim() || undefined,
          recipientAddress: editRecipientAddress.trim() || undefined,
          recipientEmail: editRecipientEmail.trim() || undefined,
          recipientPhone: editRecipientPhone.trim() || undefined,
          issuingDeskId: editDeskId,
          workstreamId: editWorkstreamId || undefined,
          officeReferenceNumber: editOfficeRef.trim() || undefined,
          remarks: editRemarks.trim() || undefined,
          expectedRevision: outward.revision,
        }),
      });
      if (!res.ok) {
        const d = await res.json().catch(() => null);
        throw new Error(d?.detail || d?.message || "Failed to update metadata.");
      }
      setShowEditMetadataModal(false);
      await loadOutward();
    } catch (err: unknown) {
      alert(err instanceof Error ? err.message : "Metadata update failed.");
    } finally {
      setSavingMetadata(false);
    }
  };

  // Upload/Replace Main Document
  const handleUploadMainDoc = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!outward || !mainDocFile) return;
    try {
      setUploadingMainDoc(true);
      const fd = new FormData();
      fd.append("file", mainDocFile);
      fd.append("expectedRevision", outward.revision.toString());

      const res = await fetch(`/api/outward/${outward.id}/document`, {
        method: "PUT",
        credentials: "include",
        body: fd,
      });
      if (!res.ok) {
        const d = await res.json().catch(() => null);
        throw new Error(d?.detail || d?.message || "Failed to upload document.");
      }
      setMainDocFile(null);
      setShowMainDocModal(false);
      await loadOutward();
    } catch (err: unknown) {
      alert(err instanceof Error ? err.message : "Document upload failed.");
    } finally {
      setUploadingMainDoc(false);
    }
  };

  // Add Attachment
  const handleAddAttachment = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!outward || !attachFile) return;
    try {
      setUploadingAttach(true);
      const fd = new FormData();
      fd.append("file", attachFile);
      fd.append("title", attachTitle.trim() || attachFile.name);
      fd.append("attachmentType", attachType);
      fd.append("expectedRevision", outward.revision.toString());

      const res = await fetch(`/api/outward/${outward.id}/attachments`, {
        method: "POST",
        credentials: "include",
        body: fd,
      });
      if (!res.ok) {
        const d = await res.json().catch(() => null);
        throw new Error(d?.detail || d?.message || "Failed to upload attachment.");
      }
      setAttachFile(null);
      setAttachTitle("");
      setShowAttachModal(false);
      await loadOutward();
    } catch (err: unknown) {
      alert(err instanceof Error ? err.message : "Attachment upload failed.");
    } finally {
      setUploadingAttach(false);
    }
  };

  // Delete Attachment
  const handleDeleteAttachment = async (attachmentId: string) => {
    if (!outward || !confirm("Are you sure you want to remove this attachment?")) return;
    try {
      const res = await fetch(`/api/outward/${outward.id}/attachments/${attachmentId}?expectedRevision=${outward.revision}`, {
        method: "DELETE",
        credentials: "include",
      });
      if (!res.ok) {
        const d = await res.json().catch(() => null);
        throw new Error(d?.detail || d?.message || "Failed to remove attachment.");
      }
      await loadOutward();
    } catch (err: unknown) {
      alert(err instanceof Error ? err.message : "Attachment deletion failed.");
    }
  };

  // Link Dak
  const handleLinkDak = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!outward || !dakLookupId.trim()) return;
    try {
      setLinkingDak(true);
      const res = await fetch(`/api/outward/${outward.id}/dak-links`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        credentials: "include",
        body: JSON.stringify({
          dakId: dakLookupId.trim(),
          relationshipType: dakRelType,
          isPrimary: dakIsPrimary,
          remarks: dakRemarks.trim() || undefined,
          expectedRevision: outward.revision,
        }),
      });
      if (!res.ok) {
        const d = await res.json().catch(() => null);
        throw new Error(d?.detail || d?.message || "Failed to link Dak.");
      }
      setDakLookupId("");
      setDakRemarks("");
      setDakIsPrimary(false);
      setShowLinkDakModal(false);
      await loadOutward();
    } catch (err: unknown) {
      alert(err instanceof Error ? err.message : "Linking Dak failed.");
    } finally {
      setLinkingDak(false);
    }
  };

  // Unlink Dak
  const handleUnlinkDak = async (linkId: string) => {
    if (!outward || !confirm("Are you sure you want to unlink this Dak record?")) return;
    try {
      const res = await fetch(`/api/outward/${outward.id}/dak-links/${linkId}?expectedRevision=${outward.revision}`, {
        method: "DELETE",
        credentials: "include",
      });
      if (!res.ok) {
        const d = await res.json().catch(() => null);
        throw new Error(d?.detail || d?.message || "Failed to unlink Dak.");
      }
      await loadOutward();
    } catch (err: unknown) {
      alert(err instanceof Error ? err.message : "Unlinking Dak failed.");
    }
  };

  // Link Matter
  const handleSaveMatterLink = async (newMatterId: string | null) => {
    if (!outward) return;
    try {
      setLinkingMatter(true);
      const res = await fetch(`/api/outward/${outward.id}`, {
        method: "PUT",
        headers: { "Content-Type": "application/json" },
        credentials: "include",
        body: JSON.stringify({
          subject: outward.subject,
          recipientName: outward.recipientName,
          recipientDesignation: outward.recipientDesignation,
          recipientDepartment: outward.recipientDepartment,
          recipientAddress: outward.recipientAddress,
          recipientEmail: outward.recipientEmail,
          recipientPhone: outward.recipientPhone,
          issuingDeskId: outward.issuingDeskId,
          workstreamId: outward.workstreamId,
          officeReferenceNumber: outward.officeReferenceNumber,
          remarks: outward.remarks,
          matterId: newMatterId,
          expectedRevision: outward.revision,
        }),
      });
      if (!res.ok) {
        const d = await res.json().catch(() => null);
        throw new Error(d?.detail || d?.message || "Failed to update Matter link.");
      }
      setShowLinkMatterModal(false);
      await loadOutward();
    } catch (err: unknown) {
      alert(err instanceof Error ? err.message : "Matter linking failed.");
    } finally {
      setLinkingMatter(false);
    }
  };

  if (loading) {
    return <div className="state loading">Loading Outward details...</div>;
  }

  if (error || !outward) {
    return (
      <div className="state error">
        {error || "Outward record not found."}
        <div style={{ marginTop: "12px" }}>
          <Link to="/outward" className="secondary-button">
            Back to Outward Register
          </Link>
        </div>
      </div>
    );
  }

  const isRegistered = outward.status === "Registered";
  const isDispatched = outward.status === "Dispatched";
  const isCancelled = outward.status === "Cancelled";

  return (
    <div className="outward-workspace-page">
      <div className="breadcrumbs">
        <Link to="/">Home</Link> <i>/</i> <Link to="/outward">Outward Register</Link> <i>/</i>{" "}
        <span>{outward.outwardNumber}</span>
      </div>

      {/* Main Header */}
      <div className="page-header">
        <div>
          <div className="eyebrow">Outward Dispatch Record</div>
          <h1>{outward.outwardNumber}</h1>
          <p style={{ color: "#475569" }}>
            Date: <strong>{outward.outwardDate}</strong> | Subject: <em>{outward.subject}</em>
          </p>
        </div>

        <div className="outward-header-actions">
          {isRegistered && hasPermission("Outward.Edit") && (
            <button className="secondary-button" onClick={() => setShowEditMetadataModal(true)}>
              Edit Metadata
            </button>
          )}
          {isRegistered && hasPermission("Outward.Dispatch") && (
            <button className="primary-button" style={{ background: "#16a34a", borderColor: "#15803d" }} onClick={() => setShowDispatchModal(true)}>
              🚀 Dispatch Record
            </button>
          )}
          {isRegistered && hasPermission("Outward.Cancel") && (
            <button className="secondary-button" style={{ color: "#dc2626", borderColor: "#fca5a5" }} onClick={() => setShowCancelModal(true)}>
              Cancel Outward
            </button>
          )}
        </div>
      </div>

      {/* Sealed Banner for Dispatched */}
      {isDispatched && (
        <div className="outward-dispatch-card">
          <div style={{ display: "flex", justifyContent: "space-between", alignItems: "flex-start" }}>
            <div>
              <strong>✅ Official Record Sealed & Dispatched</strong>
              <div style={{ marginTop: "6px", fontSize: "13px" }}>
                Mode: <strong>{outward.dispatchMode}</strong> | Date: <strong>{outward.dispatchDate}</strong>
                {outward.dispatchReferenceNumber && (
                  <span> | Consignment / Tracking Ref: <strong>{outward.dispatchReferenceNumber}</strong></span>
                )}
              </div>
              <div style={{ marginTop: "4px", fontSize: "12px", color: "#15803d" }}>
                Dispatched by {outward.dispatchedByDisplayName || "Authorized Officer"} on{" "}
                {outward.dispatchedAt ? new Date(outward.dispatchedAt).toLocaleString() : ""}
              </div>
            </div>
            <span className="outward-pill outward-pill-dispatched">Sealed Record</span>
          </div>
        </div>
      )}

      {/* Sealed Banner for Cancelled */}
      {isCancelled && (
        <div className="outward-cancel-card">
          <div style={{ display: "flex", justifyContent: "space-between", alignItems: "flex-start" }}>
            <div>
              <strong>❌ Outward Communication Cancelled & Revoked</strong>
              <div style={{ marginTop: "6px", fontSize: "13px" }}>
                Reason: <em>"{outward.cancellationReason}"</em>
              </div>
              <div style={{ marginTop: "4px", fontSize: "12px", color: "#b91c1c" }}>
                Cancelled by {outward.cancelledByDisplayName || "Authorized Officer"} on{" "}
                {outward.cancelledAt ? new Date(outward.cancelledAt).toLocaleString() : ""}
              </div>
            </div>
            <span className="outward-pill outward-pill-cancelled">Revoked Record</span>
          </div>
        </div>
      )}

      {/* Status & Custody Banner */}
      <div className="outward-status-banner">
        <div className="outward-status-info">
          <div className="outward-status-label">Issuing Office Desk</div>
          <div className="outward-desk-title">
            {outward.issuingDeskName} ({outward.issuingDeskCode})
          </div>
          <div className="outward-status-sub">
            {outward.workstreamName ? `Functional Workstream: ${outward.workstreamName}` : "General Workstream"}
            {" • "}Revision #{outward.revision}
          </div>
        </div>

        <div>
          {outward.status === "Dispatched" ? (
            <span className="outward-pill outward-pill-dispatched">Dispatched</span>
          ) : outward.status === "Cancelled" ? (
            <span className="outward-pill outward-pill-cancelled">Cancelled</span>
          ) : (
            <span className="outward-pill outward-pill-registered">Registered (Draft/Issued)</span>
          )}
        </div>
      </div>

      {/* Navigation Tabs */}
      <div className="tabs-nav" style={{ marginBottom: "20px" }}>
        <button
          className={`secondary-button ${activeTab === "overview" ? "active" : ""}`}
          onClick={() => setActiveTab("overview")}
        >
          Overview & Recipient
        </button>
        <button
          className={`secondary-button ${activeTab === "documents" ? "active" : ""}`}
          onClick={() => setActiveTab("documents")}
        >
          Documents ({outward.mainDocumentId ? 1 : 0} Main + {outward.attachments.length} Attachments)
        </button>
        <button
          className={`secondary-button ${activeTab === "links" ? "active" : ""}`}
          onClick={() => setActiveTab("links")}
        >
          Linked Context ({outward.dakLinks.length} Dak, {outward.matterId ? "1 Matter" : "0 Matters"})
        </button>
        <button
          className={`secondary-button ${activeTab === "events" ? "active" : ""}`}
          onClick={() => setActiveTab("events")}
        >
          Event Journal ({outward.events.length})
        </button>
      </div>

      {/* TAB 1: OVERVIEW */}
      {activeTab === "overview" && (
        <div className="dak-grid-2col">
          <div className="info-section">
            <h2>Dispatch Identification & Origin</h2>
            <dl>
              <div>
                <dt>Outward Number</dt>
                <dd><strong>{outward.outwardNumber}</strong></dd>
              </div>
              <div>
                <dt>Outward Date</dt>
                <dd>{outward.outwardDate}</dd>
              </div>
              <div>
                <dt>Issuing Desk</dt>
                <dd>{outward.issuingDeskName} ({outward.issuingDeskCode})</dd>
              </div>
              <div>
                <dt>Workstream</dt>
                <dd>{outward.workstreamName || "None assigned"}</dd>
              </div>
              <div>
                <dt>Office Reference No.</dt>
                <dd>{outward.officeReferenceNumber || "—"}</dd>
              </div>
              <div>
                <dt>Lifecycle Status</dt>
                <dd>
                  <strong>{outward.status}</strong> (Rev #{outward.revision})
                </dd>
              </div>
              <div>
                <dt>Internal Remarks</dt>
                <dd>{outward.remarks || "—"}</dd>
              </div>
            </dl>
          </div>

          <div className="info-section">
            <h2>Recipient Details</h2>
            <dl>
              <div>
                <dt>Recipient Name</dt>
                <dd><strong>{outward.recipientName}</strong></dd>
              </div>
              <div>
                <dt>Designation</dt>
                <dd>{outward.recipientDesignation || "—"}</dd>
              </div>
              <div>
                <dt>Department / Office</dt>
                <dd>{outward.recipientDepartment || "—"}</dd>
              </div>
              <div>
                <dt>Postal Address</dt>
                <dd>{outward.recipientAddress || "—"}</dd>
              </div>
              <div>
                <dt>Email</dt>
                <dd>{outward.recipientEmail || "—"}</dd>
              </div>
              <div>
                <dt>Phone / Contact</dt>
                <dd>{outward.recipientPhone || "—"}</dd>
              </div>
            </dl>
          </div>
        </div>
      )}

      {/* TAB 2: DOCUMENTS */}
      {activeTab === "documents" && (
        <div>
          <div className="section-heading" style={{ display: "flex", justifyContent: "space-between", alignItems: "center", marginBottom: "16px" }}>
            <div>
              <h2>Outward Documents & Enclosures</h2>
              <p className="subtext">Primary dispatch letter and supporting attachments / postal receipts.</p>
            </div>
            {isRegistered && hasPermission("Outward.Edit") && (
              <div style={{ display: "flex", gap: "8px" }}>
                <button className="secondary-button" onClick={() => setShowMainDocModal(true)}>
                  {outward.mainDocumentId ? "Replace Main Document" : "Upload Main Document"}
                </button>
                <button className="primary-button" onClick={() => setShowAttachModal(true)}>
                  + Add Attachment
                </button>
              </div>
            )}
          </div>

          {/* Main Document Section */}
          <div style={{ background: "#f8fafc", border: "1px solid #e2e8f0", borderRadius: "8px", padding: "16px", marginBottom: "24px" }}>
            <h3 style={{ marginTop: 0, fontSize: "15px", color: "#1e293b" }}>Primary Outward Document</h3>
            {outward.mainDocumentId ? (
              <div style={{ display: "flex", alignItems: "center", justifyContent: "space-between" }}>
                <div style={{ display: "flex", alignItems: "center", gap: "12px" }}>
                  <span style={{ fontSize: "28px" }}>📄</span>
                  <div>
                    <div style={{ fontWeight: 600, color: "#1e293b" }}>
                      {outward.mainDocumentFileName || "Main Document"}
                    </div>
                    <div style={{ fontSize: "12px", color: "#64748b" }}>Official outward signed copy</div>
                  </div>
                </div>
                <a
                  href={`/api/outward/${outward.id}/document/content`}
                  target="_blank"
                  rel="noreferrer"
                  className="primary-button"
                  style={{ textDecoration: "none" }}
                >
                  View / Download Document
                </a>
              </div>
            ) : (
              <div style={{ color: "#64748b", fontStyle: "italic", padding: "8px 0" }}>
                No primary outward document uploaded yet.
                {isRegistered && hasPermission("Outward.Edit") && " Click 'Upload Main Document' above."}
              </div>
            )}
          </div>

          {/* Attachments Section */}
          <h3>Enclosures & Additional Attachments ({outward.attachments.length})</h3>
          {outward.attachments.length === 0 ? (
            <p className="subtext">No additional enclosures or postal receipts attached.</p>
          ) : (
            <div className="table-responsive">
              <table className="data-table">
                <thead>
                  <tr>
                    <th style={{ width: "40px" }}>#</th>
                    <th>Title & File</th>
                    <th style={{ width: "140px" }}>Type</th>
                    <th style={{ width: "100px" }}>Size</th>
                    <th style={{ width: "140px" }}>Date</th>
                    <th style={{ width: "120px", textAlign: "right" }}>Actions</th>
                  </tr>
                </thead>
                <tbody>
                  {outward.attachments.map((att, idx) => (
                    <tr key={att.id}>
                      <td>{idx + 1}</td>
                      <td>
                        <div style={{ fontWeight: 600, color: "#1e293b" }}>{att.title}</div>
                        <div style={{ fontSize: "12px", color: "#64748b" }}>{att.originalFileName}</div>
                      </td>
                      <td>
                        <span className="outward-pill outward-pill-registered">{att.attachmentType}</span>
                      </td>
                      <td style={{ fontSize: "12px", color: "#64748b" }}>
                        {(att.fileSizeBytes / 1024).toFixed(1)} KB
                      </td>
                      <td style={{ fontSize: "12px", color: "#64748b" }}>
                        {new Date(att.createdAt).toLocaleDateString()}
                      </td>
                      <td style={{ textAlign: "right" }}>
                        <div style={{ display: "flex", gap: "8px", justifyContent: "flex-end" }}>
                          <a
                            href={`/api/outward/${outward.id}/attachments/${att.id}/content`}
                            target="_blank"
                            rel="noreferrer"
                            className="secondary-button"
                            style={{ fontSize: "11px", padding: "2px 8px" }}
                          >
                            View
                          </a>
                          {isRegistered && hasPermission("Outward.Edit") && (
                            <button
                              className="secondary-button"
                              style={{ fontSize: "11px", padding: "2px 8px", color: "#dc2626", borderColor: "#fca5a5" }}
                              onClick={() => void handleDeleteAttachment(att.id)}
                            >
                              Remove
                            </button>
                          )}
                        </div>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </div>
      )}

      {/* TAB 3: LINKED CONTEXT */}
      {activeTab === "links" && (
        <div>
          {/* Linked Dak Correspondence */}
          <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center", marginBottom: "16px" }}>
            <div>
              <h2>Linked Inward Dak Correspondence ({outward.dakLinks.length})</h2>
              <p className="subtext">Inward letters and references to which this outward dispatch responds or relates.</p>
            </div>
            {isRegistered && hasPermission("Outward.Edit") && (
              <button className="secondary-button" onClick={() => setShowLinkDakModal(true)}>
                + Link Inward Dak
              </button>
            )}
          </div>

          {outward.dakLinks.length === 0 ? (
            <div style={{ color: "#64748b", fontStyle: "italic", marginBottom: "32px" }}>
              No inward Dak correspondence linked to this outward communication.
            </div>
          ) : (
            <div className="table-responsive" style={{ marginBottom: "32px" }}>
              <table className="data-table">
                <thead>
                  <tr>
                    <th>Diary No. & Subject</th>
                    <th>Sender & Date</th>
                    <th>Relationship</th>
                    <th>Status</th>
                    <th style={{ textAlign: "right" }}>Actions</th>
                  </tr>
                </thead>
                <tbody>
                  {outward.dakLinks.map((link) => (
                    <tr key={link.linkId}>
                      <td>
                        <Link to={`/dak/${link.dakId}`} style={{ fontWeight: 600, color: "#1e609e" }}>
                          {link.diaryNumber}
                        </Link>
                        <div style={{ fontSize: "12px", color: "#334155", marginTop: "2px" }}>
                          {link.subject}
                        </div>
                      </td>
                      <td>
                        <div style={{ fontSize: "13px" }}>{link.senderName}</div>
                        <div style={{ fontSize: "11px", color: "#64748b" }}>Recd: {link.receivedDate}</div>
                      </td>
                      <td>
                        <span className="outward-pill outward-pill-registered">
                          {link.relationshipType}
                        </span>
                        {link.isPrimary && (
                          <span className="outward-pill outward-pill-dispatched" style={{ marginLeft: "4px" }}>
                            Primary Reply
                          </span>
                        )}
                      </td>
                      <td>
                        <div style={{ fontSize: "12px", color: "#64748b" }}>
                          Linked {new Date(link.linkedAt).toLocaleDateString()}
                        </div>
                      </td>
                      <td style={{ textAlign: "right" }}>
                        <div style={{ display: "flex", gap: "8px", justifyContent: "flex-end" }}>
                          <button
                            className="secondary-button"
                            style={{ fontSize: "11px", padding: "2px 8px" }}
                            onClick={() => navigate(`/dak/${link.dakId}`)}
                          >
                            Open Dak
                          </button>
                          {isRegistered && hasPermission("Outward.Edit") && (
                            <button
                              className="secondary-button"
                              style={{ fontSize: "11px", padding: "2px 8px", color: "#dc2626", borderColor: "#fca5a5" }}
                              onClick={() => void handleUnlinkDak(link.linkId)}
                            >
                              Unlink
                            </button>
                          )}
                        </div>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}

          {/* Linked Legal Matter */}
          <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center", marginBottom: "16px" }}>
            <div>
              <h2>Linked Legal Matter</h2>
              <p className="subtext">Case, petition, or legal dispute associated with this outward dispatch.</p>
            </div>
            {isRegistered && hasPermission("Outward.Edit") && (
              <button className="secondary-button" onClick={() => setShowLinkMatterModal(true)}>
                {outward.matterId ? "Change Matter Link" : "+ Link Matter"}
              </button>
            )}
          </div>

          {outward.matterId ? (
            <div style={{ background: "#f8fafc", border: "1px solid #e2e8f0", borderRadius: "8px", padding: "16px", display: "flex", justifyContent: "space-between", alignItems: "center" }}>
              <div>
                <div style={{ fontWeight: 700, fontSize: "15px", color: "#1e3a58" }}>
                  Matter #{outward.matterNumber}
                </div>
                <div style={{ fontSize: "13px", color: "#334155", marginTop: "4px" }}>
                  {outward.matterTitle}
                </div>
              </div>
              <div style={{ display: "flex", gap: "8px" }}>
                <button
                  className="primary-button"
                  style={{ fontSize: "12px", padding: "4px 10px" }}
                  onClick={() => navigate(`/matters/${outward.matterId}`)}
                >
                  Open Matter Workspace
                </button>
                {isRegistered && hasPermission("Outward.Edit") && (
                  <button
                    className="secondary-button"
                    style={{ fontSize: "12px", padding: "4px 10px", color: "#dc2626" }}
                    onClick={() => void handleSaveMatterLink(null)}
                  >
                    Unlink Matter
                  </button>
                )}
              </div>
            </div>
          ) : (
            <div style={{ color: "#64748b", fontStyle: "italic" }}>
              No legal matter is currently linked to this outward communication.
            </div>
          )}
        </div>
      )}

      {/* TAB 4: EVENT JOURNAL */}
      {activeTab === "events" && (
        <div>
          <div className="section-heading">
            <h2>Immutable Outward Event Journal ({outward.events.length})</h2>
            <p className="subtext">
              Durable append-only domain events recording every official action and dispatch milestone.
            </p>
          </div>

          <div className="event-journal">
            {outward.events.map((ev) => {
              let badgeClass = "registered";
              if (ev.action === "Dispatched") badgeClass = "dispatched";
              if (ev.action === "Cancelled") badgeClass = "cancelled";

              return (
                <div key={ev.id} className="event-item">
                  <div className={`event-badge ${badgeClass}`}>{ev.sequenceNumber}</div>
                  <div className="event-header">
                    <div className="event-action">
                      {ev.action === "Registered" && "Initial Registration"}
                      {ev.action === "MetadataUpdated" && "Metadata Updated"}
                      {ev.action === "MainDocumentChanged" && "Main Document Changed"}
                      {ev.action === "AttachmentAdded" && "Attachment Enclosed"}
                      {ev.action === "AttachmentRemoved" && "Attachment Removed"}
                      {ev.action === "DakLinkAdded" && "Inward Dak Linked"}
                      {ev.action === "DakLinkRemoved" && "Inward Dak Unlinked"}
                      {ev.action === "Dispatched" && "🚀 Dispatched & Sealed"}
                      {ev.action === "Cancelled" && "❌ Cancelled & Revoked"}
                    </div>
                    <div className="event-date">
                      {new Date(ev.actionAt).toLocaleString()}
                    </div>
                  </div>

                  <div className="event-actor">
                    Action by: <strong>{ev.actionByDisplayName}</strong>
                  </div>

                  {ev.remarks && (
                    <div className="event-meta">
                      Remarks: <em>{ev.remarks}</em>
                    </div>
                  )}

                  {ev.documentFileName && (
                    <div className="event-meta">
                      Document: <strong>{ev.documentFileName}</strong>
                    </div>
                  )}

                  {ev.targetEntitySnapshot && (
                    <div className="event-meta">
                      Entity Reference: <code>{ev.targetEntitySnapshot}</code>
                    </div>
                  )}

                  {ev.dispatchMode && (
                    <div className="event-meta">
                      Dispatch Mode: <strong>{ev.dispatchMode}</strong> | Date: <strong>{ev.dispatchDate}</strong>
                      {ev.dispatchReferenceNumber && ` | Ref: ${ev.dispatchReferenceNumber}`}
                    </div>
                  )}

                  {ev.cancellationReason && (
                    <div className="event-meta" style={{ color: "#dc2626", background: "#fef2f2" }}>
                      Cancellation Reason: <strong>{ev.cancellationReason}</strong>
                    </div>
                  )}
                </div>
              );
            })}
          </div>
        </div>
      )}

      {/* DISPATCH MODAL */}
      {showDispatchModal && (
        <div className="outward-modal-overlay">
          <div className="outward-modal-content">
            <h2 className="outward-modal-title">Record Official Dispatch</h2>
            <p style={{ fontSize: "13px", color: "#64748b", marginBottom: "16px" }}>
              Seals this Outward record as Dispatched. Once dispatched, metadata, documents, and links are permanently locked.
            </p>

            <form onSubmit={handleDispatch}>
              <div style={{ display: "flex", flexDirection: "column", gap: "14px" }}>
                <label style={{ display: "flex", flexDirection: "column", gap: "4px", fontSize: "13px", fontWeight: 600 }}>
                  Dispatch Date *
                  <input
                    type="date"
                    required
                    value={dispatchDate}
                    onChange={(e) => setDispatchDate(e.target.value)}
                    style={{ padding: "8px", borderRadius: "5px", border: "1px solid #cbd5e1" }}
                  />
                </label>

                <label style={{ display: "flex", flexDirection: "column", gap: "4px", fontSize: "13px", fontWeight: 600 }}>
                  Dispatch Mode *
                  <select
                    required
                    value={dispatchMode}
                    onChange={(e) => setDispatchMode(e.target.value)}
                    style={{ padding: "8px", borderRadius: "5px", border: "1px solid #cbd5e1" }}
                  >
                    <option value="SpeedPost">Speed Post</option>
                    <option value="RegisteredPost">Registered Post</option>
                    <option value="ByHand">By Hand</option>
                    <option value="SpecialMessenger">Special Messenger</option>
                    <option value="Courier">Courier</option>
                    <option value="Email">Email</option>
                    <option value="Other">Other</option>
                  </select>
                </label>

                <label style={{ display: "flex", flexDirection: "column", gap: "4px", fontSize: "13px", fontWeight: 600 }}>
                  Dispatch Reference / Consignment No.
                  <input
                    type="text"
                    placeholder="e.g. ED123456789IN or Peon Book Entry No"
                    value={dispatchRef}
                    onChange={(e) => setDispatchRef(e.target.value)}
                    style={{ padding: "8px", borderRadius: "5px", border: "1px solid #cbd5e1" }}
                  />
                </label>

                <label style={{ display: "flex", flexDirection: "column", gap: "4px", fontSize: "13px", fontWeight: 600 }}>
                  Dispatch Remarks (Optional)
                  <textarea
                    rows={2}
                    placeholder="Dispatch clerk remarks..."
                    value={dispatchRemarks}
                    onChange={(e) => setDispatchRemarks(e.target.value)}
                    style={{ padding: "8px", borderRadius: "5px", border: "1px solid #cbd5e1" }}
                  />
                </label>
              </div>

              <div className="outward-modal-actions">
                <button
                  type="button"
                  className="secondary-button"
                  onClick={() => setShowDispatchModal(false)}
                  disabled={dispatching}
                >
                  Cancel
                </button>
                <button
                  type="submit"
                  className="primary-button"
                  style={{ background: "#16a34a", borderColor: "#15803d" }}
                  disabled={dispatching}
                >
                  {dispatching ? "Confirming Dispatch..." : "Seal & Dispatch"}
                </button>
              </div>
            </form>
          </div>
        </div>
      )}

      {/* CANCEL MODAL */}
      {showCancelModal && (
        <div className="outward-modal-overlay">
          <div className="outward-modal-content">
            <h2 className="outward-modal-title" style={{ color: "#dc2626" }}>Cancel Outward Communication</h2>
            <p style={{ fontSize: "13px", color: "#64748b", marginBottom: "16px" }}>
              Permanently marks this Outward dispatch as Cancelled. The outward number remains reserved and cannot be reused.
            </p>

            <form onSubmit={handleCancel}>
              <div style={{ display: "flex", flexDirection: "column", gap: "14px" }}>
                <label style={{ display: "flex", flexDirection: "column", gap: "4px", fontSize: "13px", fontWeight: 600 }}>
                  Cancellation Reason * (min 5 characters)
                  <textarea
                    rows={3}
                    required
                    placeholder="Provide official administrative justification for cancelling this dispatch..."
                    value={cancellationReason}
                    onChange={(e) => setCancellationReason(e.target.value)}
                    style={{ padding: "8px", borderRadius: "5px", border: "1px solid #cbd5e1" }}
                  />
                </label>
              </div>

              <div className="outward-modal-actions">
                <button
                  type="button"
                  className="secondary-button"
                  onClick={() => setShowCancelModal(false)}
                  disabled={cancelling}
                >
                  Back
                </button>
                <button
                  type="submit"
                  className="primary-button"
                  style={{ background: "#dc2626", borderColor: "#b91c1c" }}
                  disabled={cancelling}
                >
                  {cancelling ? "Cancelling..." : "Confirm Cancellation"}
                </button>
              </div>
            </form>
          </div>
        </div>
      )}

      {/* EDIT METADATA MODAL */}
      {showEditMetadataModal && (
        <div className="outward-modal-overlay">
          <div className="outward-modal-content" style={{ maxWidth: "600px" }}>
            <h2 className="outward-modal-title">Edit Outward Metadata</h2>

            <form onSubmit={handleSaveMetadata}>
              <div style={{ display: "flex", flexDirection: "column", gap: "12px", maxHeight: "60vh", overflowY: "auto", paddingRight: "8px" }}>
                <label style={{ display: "flex", flexDirection: "column", gap: "4px", fontSize: "13px", fontWeight: 600 }}>
                  Subject *
                  <input
                    type="text"
                    required
                    value={editSubject}
                    onChange={(e) => setEditSubject(e.target.value)}
                    style={{ padding: "8px", borderRadius: "5px", border: "1px solid #cbd5e1" }}
                  />
                </label>

                <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: "10px" }}>
                  <label style={{ display: "flex", flexDirection: "column", gap: "4px", fontSize: "13px", fontWeight: 600 }}>
                    Recipient Name *
                    <input
                      type="text"
                      required
                      value={editRecipientName}
                      onChange={(e) => setEditRecipientName(e.target.value)}
                      style={{ padding: "8px", borderRadius: "5px", border: "1px solid #cbd5e1" }}
                    />
                  </label>

                  <label style={{ display: "flex", flexDirection: "column", gap: "4px", fontSize: "13px", fontWeight: 600 }}>
                    Recipient Designation
                    <input
                      type="text"
                      value={editRecipientDesignation}
                      onChange={(e) => setEditRecipientDesignation(e.target.value)}
                      style={{ padding: "8px", borderRadius: "5px", border: "1px solid #cbd5e1" }}
                    />
                  </label>
                </div>

                <label style={{ display: "flex", flexDirection: "column", gap: "4px", fontSize: "13px", fontWeight: 600 }}>
                  Recipient Department / Office
                  <input
                    type="text"
                    value={editRecipientDepartment}
                    onChange={(e) => setEditRecipientDepartment(e.target.value)}
                    style={{ padding: "8px", borderRadius: "5px", border: "1px solid #cbd5e1" }}
                  />
                </label>

                <label style={{ display: "flex", flexDirection: "column", gap: "4px", fontSize: "13px", fontWeight: 600 }}>
                  Recipient Address
                  <textarea
                    rows={2}
                    value={editRecipientAddress}
                    onChange={(e) => setEditRecipientAddress(e.target.value)}
                    style={{ padding: "8px", borderRadius: "5px", border: "1px solid #cbd5e1" }}
                  />
                </label>

                <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: "10px" }}>
                  <label style={{ display: "flex", flexDirection: "column", gap: "4px", fontSize: "13px", fontWeight: 600 }}>
                    Recipient Email
                    <input
                      type="email"
                      value={editRecipientEmail}
                      onChange={(e) => setEditRecipientEmail(e.target.value)}
                      style={{ padding: "8px", borderRadius: "5px", border: "1px solid #cbd5e1" }}
                    />
                  </label>

                  <label style={{ display: "flex", flexDirection: "column", gap: "4px", fontSize: "13px", fontWeight: 600 }}>
                    Recipient Phone
                    <input
                      type="tel"
                      value={editRecipientPhone}
                      onChange={(e) => setEditRecipientPhone(e.target.value)}
                      style={{ padding: "8px", borderRadius: "5px", border: "1px solid #cbd5e1" }}
                    />
                  </label>
                </div>

                <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: "10px" }}>
                  <label style={{ display: "flex", flexDirection: "column", gap: "4px", fontSize: "13px", fontWeight: 600 }}>
                    Issuing Office Desk *
                    <select
                      required
                      value={editDeskId}
                      onChange={(e) => setEditDeskId(e.target.value)}
                      style={{ padding: "8px", borderRadius: "5px", border: "1px solid #cbd5e1" }}
                    >
                      {context?.desks.map((d) => (
                        <option key={d.id} value={d.id}>
                          {d.name} ({d.code})
                        </option>
                      ))}
                    </select>
                  </label>

                  <label style={{ display: "flex", flexDirection: "column", gap: "4px", fontSize: "13px", fontWeight: 600 }}>
                    Workstream
                    <select
                      value={editWorkstreamId}
                      onChange={(e) => setEditWorkstreamId(e.target.value)}
                      style={{ padding: "8px", borderRadius: "5px", border: "1px solid #cbd5e1" }}
                    >
                      <option value="">None</option>
                      {context?.workstreams.map((w) => (
                        <option key={w.id} value={w.id}>
                          {w.name} ({w.code})
                        </option>
                      ))}
                    </select>
                  </label>
                </div>

                <label style={{ display: "flex", flexDirection: "column", gap: "4px", fontSize: "13px", fontWeight: 600 }}>
                  Office Reference Number
                  <input
                    type="text"
                    value={editOfficeRef}
                    onChange={(e) => setEditOfficeRef(e.target.value)}
                    style={{ padding: "8px", borderRadius: "5px", border: "1px solid #cbd5e1" }}
                  />
                </label>

                <label style={{ display: "flex", flexDirection: "column", gap: "4px", fontSize: "13px", fontWeight: 600 }}>
                  Remarks
                  <textarea
                    rows={2}
                    value={editRemarks}
                    onChange={(e) => setEditRemarks(e.target.value)}
                    style={{ padding: "8px", borderRadius: "5px", border: "1px solid #cbd5e1" }}
                  />
                </label>
              </div>

              <div className="outward-modal-actions">
                <button
                  type="button"
                  className="secondary-button"
                  onClick={() => setShowEditMetadataModal(false)}
                  disabled={savingMetadata}
                >
                  Cancel
                </button>
                <button type="submit" className="primary-button" disabled={savingMetadata}>
                  {savingMetadata ? "Saving..." : "Save Changes"}
                </button>
              </div>
            </form>
          </div>
        </div>
      )}

      {/* UPLOAD MAIN DOC MODAL */}
      {showMainDocModal && (
        <div className="outward-modal-overlay">
          <div className="outward-modal-content">
            <h2 className="outward-modal-title">Upload / Replace Primary Document</h2>
            <form onSubmit={handleUploadMainDoc}>
              <div style={{ display: "flex", flexDirection: "column", gap: "14px" }}>
                <label style={{ display: "flex", flexDirection: "column", gap: "4px", fontSize: "13px", fontWeight: 600 }}>
                  Document File * (PDF or scan)
                  <input
                    type="file"
                    required
                    accept=".pdf,.png,.jpg,.jpeg,.doc,.docx"
                    onChange={(e) => setMainDocFile(e.target.files ? e.target.files[0] : null)}
                  />
                </label>
              </div>

              <div className="outward-modal-actions">
                <button
                  type="button"
                  className="secondary-button"
                  onClick={() => setShowMainDocModal(false)}
                  disabled={uploadingMainDoc}
                >
                  Cancel
                </button>
                <button type="submit" className="primary-button" disabled={uploadingMainDoc || !mainDocFile}>
                  {uploadingMainDoc ? "Uploading..." : "Upload Document"}
                </button>
              </div>
            </form>
          </div>
        </div>
      )}

      {/* ADD ATTACHMENT MODAL */}
      {showAttachModal && (
        <div className="outward-modal-overlay">
          <div className="outward-modal-content">
            <h2 className="outward-modal-title">Add Enclosure / Attachment</h2>
            <form onSubmit={handleAddAttachment}>
              <div style={{ display: "flex", flexDirection: "column", gap: "14px" }}>
                <label style={{ display: "flex", flexDirection: "column", gap: "4px", fontSize: "13px", fontWeight: 600 }}>
                  File *
                  <input
                    type="file"
                    required
                    onChange={(e) => {
                      const f = e.target.files ? e.target.files[0] : null;
                      setAttachFile(f);
                      if (f && !attachTitle) setAttachTitle(f.name);
                    }}
                  />
                </label>

                <label style={{ display: "flex", flexDirection: "column", gap: "4px", fontSize: "13px", fontWeight: 600 }}>
                  Document Title *
                  <input
                    type="text"
                    required
                    placeholder="e.g. Annexure A - Field Inspection Report"
                    value={attachTitle}
                    onChange={(e) => setAttachTitle(e.target.value)}
                    style={{ padding: "8px", borderRadius: "5px", border: "1px solid #cbd5e1" }}
                  />
                </label>

                <label style={{ display: "flex", flexDirection: "column", gap: "4px", fontSize: "13px", fontWeight: 600 }}>
                  Attachment Type *
                  <select
                    value={attachType}
                    onChange={(e) => setAttachType(e.target.value)}
                    style={{ padding: "8px", borderRadius: "5px", border: "1px solid #cbd5e1" }}
                  >
                    <option value="Annexure">Annexure</option>
                    <option value="CopyTo">Copy To / Endorsement</option>
                    <option value="PostalReceipt">Postal / Dispatch Receipt</option>
                    <option value="OfficeNote">Office Note</option>
                    <option value="Other">Other</option>
                  </select>
                </label>
              </div>

              <div className="outward-modal-actions">
                <button
                  type="button"
                  className="secondary-button"
                  onClick={() => setShowAttachModal(false)}
                  disabled={uploadingAttach}
                >
                  Cancel
                </button>
                <button type="submit" className="primary-button" disabled={uploadingAttach || !attachFile}>
                  {uploadingAttach ? "Enclosing..." : "Attach File"}
                </button>
              </div>
            </form>
          </div>
        </div>
      )}

      {/* LINK DAK MODAL */}
      {showLinkDakModal && (
        <div className="outward-modal-overlay">
          <div className="outward-modal-content">
            <h2 className="outward-modal-title">Link Inward Dak Correspondence</h2>
            <form onSubmit={handleLinkDak}>
              <div style={{ display: "flex", flexDirection: "column", gap: "14px" }}>
                <label style={{ display: "flex", flexDirection: "column", gap: "4px", fontSize: "13px", fontWeight: 600 }}>
                  Dak Record ID (GUID) *
                  <input
                    type="text"
                    required
                    placeholder="e.g. c3b7f... or copy from Dak URL"
                    value={dakLookupId}
                    onChange={(e) => setDakLookupId(e.target.value)}
                    style={{ padding: "8px", borderRadius: "5px", border: "1px solid #cbd5e1" }}
                  />
                </label>

                <label style={{ display: "flex", flexDirection: "column", gap: "4px", fontSize: "13px", fontWeight: 600 }}>
                  Relationship Type *
                  <select
                    value={dakRelType}
                    onChange={(e) => setDakRelType(e.target.value)}
                    style={{ padding: "8px", borderRadius: "5px", border: "1px solid #cbd5e1" }}
                  >
                    <option value="PrimaryReply">Primary Reply</option>
                    <option value="RelatedPetition">Related Petition</option>
                    <option value="Reference">Reference</option>
                  </select>
                </label>

                <label style={{ display: "flex", alignItems: "center", gap: "8px", fontSize: "13px", cursor: "pointer" }}>
                  <input
                    type="checkbox"
                    checked={dakIsPrimary}
                    onChange={(e) => setDakIsPrimary(e.target.checked)}
                  />
                  <strong>Designate as Primary Reply</strong> (at most one per Outward)
                </label>

                <label style={{ display: "flex", flexDirection: "column", gap: "4px", fontSize: "13px", fontWeight: 600 }}>
                  Link Remarks (Optional)
                  <input
                    type="text"
                    placeholder="e.g. Action compliance on complaint"
                    value={dakRemarks}
                    onChange={(e) => setDakRemarks(e.target.value)}
                    style={{ padding: "8px", borderRadius: "5px", border: "1px solid #cbd5e1" }}
                  />
                </label>
              </div>

              <div className="outward-modal-actions">
                <button
                  type="button"
                  className="secondary-button"
                  onClick={() => setShowLinkDakModal(false)}
                  disabled={linkingDak}
                >
                  Cancel
                </button>
                <button type="submit" className="primary-button" disabled={linkingDak || !dakLookupId.trim()}>
                  {linkingDak ? "Linking..." : "Confirm Link"}
                </button>
              </div>
            </form>
          </div>
        </div>
      )}

      {/* LINK MATTER MODAL */}
      {showLinkMatterModal && (
        <div className="outward-modal-overlay">
          <div className="outward-modal-content">
            <h2 className="outward-modal-title">Link Legal Matter</h2>
            <div style={{ display: "flex", flexDirection: "column", gap: "14px" }}>
              <label style={{ display: "flex", flexDirection: "column", gap: "4px", fontSize: "13px", fontWeight: 600 }}>
                Matter ID (GUID) *
                <input
                  type="text"
                  required
                  placeholder="e.g. copy from Matter workspace URL"
                  value={matterLookupId}
                  onChange={(e) => setMatterLookupId(e.target.value)}
                  style={{ padding: "8px", borderRadius: "5px", border: "1px solid #cbd5e1" }}
                />
              </label>

              <div className="outward-modal-actions">
                <button
                  type="button"
                  className="secondary-button"
                  onClick={() => setShowLinkMatterModal(false)}
                  disabled={linkingMatter}
                >
                  Cancel
                </button>
                <button
                  type="button"
                  className="primary-button"
                  disabled={linkingMatter || !matterLookupId.trim()}
                  onClick={() => void handleSaveMatterLink(matterLookupId.trim())}
                >
                  {linkingMatter ? "Linking..." : "Save Matter Link"}
                </button>
              </div>
            </div>
          </div>
        </div>
      )}
    </div>
  );
};
