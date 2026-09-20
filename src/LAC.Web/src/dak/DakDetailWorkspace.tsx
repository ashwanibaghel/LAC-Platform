import React, { useState, useEffect, useCallback } from "react";
import { useParams, Link, useNavigate } from "react-router-dom";
import { useAuth } from "../auth/AuthProvider";
import type { DakDetail, DakCategory } from "./types";
import { DakTimeline } from "./DakTimeline";
import { DakMovementModal } from "./DakMovementModal";
import "./dak.css";

export const DakDetailWorkspace: React.FC = () => {
  const { id = "" } = useParams();
  const navigate = useNavigate();
  const { hasPermission } = useAuth();

  const [dak, setDak] = useState<DakDetail | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [activeTab, setActiveTab] = useState<"overview" | "documents" | "links" | "timeline">("overview");

  // Modals state
  const [movementModalMode, setMovementModalMode] = useState<"move" | "dispose" | "cancel" | null>(null);
  const [showEditModal, setShowEditModal] = useState(false);
  const [showAttachModal, setShowAttachModal] = useState(false);
  const [showLinkModal, setShowLinkModal] = useState(false);

  // Edit metadata form state
  const [editSubject, setEditSubject] = useState("");
  const [editSenderName, setEditSenderName] = useState("");
  const [editSenderDesignation, setEditSenderDesignation] = useState("");
  const [editSenderDepartment, setEditSenderDepartment] = useState("");
  const [editSenderAddress, setEditSenderAddress] = useState("");
  const [editSenderRef, setEditSenderRef] = useState("");
  const [editLetterDate, setEditLetterDate] = useState("");
  const [editInwardMode, setEditInwardMode] = useState("");
  const [editPriority, setEditPriority] = useState<"Routine" | "Urgent" | "Immediate">("Routine");
  const [editDueDate, setEditDueDate] = useState("");
  const [editCategoryId, setEditCategoryId] = useState("");
  const [editWorkstreamId, setEditWorkstreamId] = useState("");
  const [categories, setCategories] = useState<DakCategory[]>([]);
  const [workstreams, setWorkstreams] = useState<{ id: string; name: string }[]>([]);

  // Add Attachment form state
  const [attachFile, setAttachFile] = useState<File | null>(null);
  const [attachTitle, setAttachTitle] = useState("");
  const [attachType, setAttachType] = useState("Enclosure");
  const [attaching, setAttaching] = useState(false);

  // Add Link form state
  const [linkType, setLinkType] = useState<"Village" | "Award" | "Matter" | "Khasra">("Award");
  const [linkEntityId, setLinkEntityId] = useState("");
  const [linking, setLinking] = useState(false);

  // Outward replies
  const [outwardReplies, setOutwardReplies] = useState<
    { id: string; outwardNumber: string; outwardDate: string; subject: string; status: string; recipientName: string }[]
  >([]);

  const loadDak = useCallback(async () => {
    if (!id) return;
    try {
      setLoading(true);
      setError(null);
      const res = await fetch(`/api/dak/${id}`, { credentials: "include" });
      if (!res.ok) {
        if (res.status === 403) throw new Error("Access denied: You do not have permission to view this Dak.");
        if (res.status === 404) throw new Error("Dak not found.");
        throw new Error("Failed to load Dak details.");
      }
      const data = (await res.json()) as DakDetail;
      setDak(data);

      // Pre-fill edit modal
      setEditSubject(data.subject);
      setEditSenderName(data.senderName);
      setEditSenderDesignation(data.senderDesignation || "");
      setEditSenderDepartment(data.senderDepartment || "");
      setEditSenderAddress(data.senderAddress || "");
      setEditSenderRef(data.senderReferenceNumber || "");
      setEditLetterDate(data.senderLetterDate || "");
      setEditInwardMode(data.inwardMode);
      setEditPriority(data.priority);
      setEditDueDate(data.dueDate || "");
      setEditCategoryId(data.categoryId || "");
      setEditWorkstreamId(data.workstreamId || "");

      // Load outward replies
      fetch(`/api/outward?dakId=${id}`, { credentials: "include" })
        .then((r) => (r.ok ? (r.json() as Promise<{ items: { id: string; outwardNumber: string; outwardDate: string; subject: string; status: string; recipientName: string }[] }>) : null))
        .then((d) => {
          if (d?.items) setOutwardReplies(d.items);
        })
        .catch(() => {});
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : "Failed to load Dak.");
    } finally {
      setLoading(false);
    }
  }, [id]);

  useEffect(() => {
    void loadDak();
  }, [loadDak]);

  useEffect(() => {
    if (!id) return;
    // Load categories & workstreams for editing via operational lookup
    fetch(`/api/dak/${id}/lookups/edit`, { credentials: "include" })
      .then((r) => {
        if (!r.ok) return null;
        return r.json() as Promise<{ categories: DakCategory[]; workstreams: { id: string; name: string }[] }>;
      })
      .then((data) => {
        if (data) {
          setCategories(data.categories.filter((c) => c.isActive));
          setWorkstreams(data.workstreams);
        }
      })
      .catch(() => {});
  }, [id]);

  const handleSaveMetadata = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!dak) return;
    try {
      const res = await fetch(`/api/dak/${dak.id}`, {
        method: "PUT",
        headers: { "Content-Type": "application/json" },
        credentials: "include",
        body: JSON.stringify({
          subject: editSubject.trim(),
          senderName: editSenderName.trim(),
          senderDesignation: editSenderDesignation.trim() || null,
          senderDepartment: editSenderDepartment.trim() || null,
          senderAddress: editSenderAddress.trim() || null,
          senderReferenceNumber: editSenderRef.trim() || null,
          senderLetterDate: editLetterDate || null,
          inwardMode: editInwardMode.trim(),
          priority: editPriority,
          dueDate: editDueDate || null,
          categoryId: editCategoryId || null,
          workstreamId: editWorkstreamId || null,
          expectedRevision: dak.revision,
        }),
      });

      if (!res.ok) {
        const d = await res.json().catch(() => null);
        throw new Error(d?.detail || d?.message || "Failed to update metadata.");
      }

      setShowEditModal(false);
      await loadDak();
    } catch (err: unknown) {
      alert(err instanceof Error ? err.message : "Error saving metadata.");
    }
  };

  const handleAddAttachment = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!dak || !attachFile) return;
    try {
      setAttaching(true);
      const fd = new FormData();
      fd.append("file", attachFile);
      fd.append("title", attachTitle.trim() || attachFile.name);
      fd.append("attachmentType", attachType);

      const res = await fetch(`/api/dak/${dak.id}/attachments`, {
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
      await loadDak();
    } catch (err: unknown) {
      alert(err instanceof Error ? err.message : "Error uploading attachment.");
    } finally {
      setAttaching(false);
    }
  };

  const handleDeleteAttachment = async (attachmentId: string) => {
    if (!dak || !confirm("Are you sure you want to remove this attachment?")) return;
    try {
      const res = await fetch(`/api/dak/${dak.id}/attachments/${attachmentId}`, {
        method: "DELETE",
        credentials: "include",
      });
      if (!res.ok) throw new Error("Failed to delete attachment.");
      await loadDak();
    } catch (err: unknown) {
      alert(err instanceof Error ? err.message : "Error deleting attachment.");
    }
  };

  const handleAddLink = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!dak || !linkEntityId.trim()) return;
    try {
      setLinking(true);
      const endpointType = linkType.toLowerCase() + "s";
      const res = await fetch(`/api/dak/${dak.id}/links/${endpointType}`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        credentials: "include",
        body: JSON.stringify({ entityId: linkEntityId.trim() }),
      });

      if (!res.ok) {
        const d = await res.json().catch(() => null);
        throw new Error(d?.detail || d?.message || "Failed to link entity.");
      }

      setLinkEntityId("");
      setShowLinkModal(false);
      await loadDak();
    } catch (err: unknown) {
      alert(err instanceof Error ? err.message : "Error creating link.");
    } finally {
      setLinking(false);
    }
  };

  const handleDeleteLink = async (linkTypeParam: string, linkId: string) => {
    if (!dak || !confirm("Remove this association?")) return;
    try {
      const endpointType = linkTypeParam.toLowerCase() + "s";
      const res = await fetch(`/api/dak/${dak.id}/links/${endpointType}/${linkId}`, {
        method: "DELETE",
        credentials: "include",
      });
      if (!res.ok) throw new Error("Failed to remove link.");
      await loadDak();
    } catch (err: unknown) {
      alert(err instanceof Error ? err.message : "Error removing link.");
    }
  };

  if (loading) return <div className="state"><strong>Loading Dak record...</strong></div>;
  if (error || !dak) return <div className="state error"><strong>Error:</strong> {error || "Dak not found."}</div>;

  const isTerminal = dak.status === "Disposed" || dak.status === "Cancelled";
  const canAssignWork = hasPermission("WorkItem.Create") && !isTerminal;
  const canMove = hasPermission("Dak.Move") && !isTerminal;
  const canDispose = hasPermission("Dak.Dispose") && !isTerminal;
  const canCancel = hasPermission("Dak.Cancel") && !isTerminal;
  const canEdit = hasPermission("Dak.Edit") && !isTerminal;

  return (
    <div className="dak-detail-workspace">
      <div className="breadcrumbs">
        <Link to="/">Home</Link> <i>/</i> <Link to="/dak">Dak / Inward</Link> <i>/</i> <span>{dak.diaryNumber}</span>
      </div>

      <div className="page-header">
        <div>
          <div className="eyebrow" style={{ display: "flex", gap: "10px", alignItems: "center" }}>
            <span>Inward Dak #{dak.diaryNumber}</span>
            <span className={`priority-pill priority-${dak.priority.toLowerCase()}`}>{dak.priority}</span>
            <span className={`status-pill status-${dak.status.toLowerCase()}`}>{dak.status}</span>
          </div>
          <h1>{dak.subject}</h1>
          <p className="subtext">
            Received from <strong>{dak.senderName}</strong>
            {dak.senderDepartment ? ` (${dak.senderDepartment})` : ""} on{" "}
            {new Date(dak.receivedDate).toLocaleDateString("en-IN", { dateStyle: "long" })} via {dak.inwardMode}.
          </p>
        </div>
      </div>

      {/* Custody Warning Banner */}
      {dak.currentAssignment?.needsAttention && (
        <div className="dak-attention-banner">
          <span style={{ fontSize: "20px" }}>⚠️</span>
          <div>
            <strong>Custody Alert: Attention Required</strong>
            <div>
              The assigned office desk (<em>{dak.currentAssignment.deskName}</em>) is inactive or the assigned user is no longer an active eligible member of that desk. Immediate reassignment is advised.
            </div>
          </div>
        </div>
      )}

      {/* Custody & Action Card */}
      <div className="dak-custody-card">
        <div className="dak-custody-info">
          <span className="dak-custody-title">Current Custody & Routing</span>
          {dak.currentAssignment ? (
            <>
              <span className="dak-custody-desk">
                {dak.currentAssignment.deskName} ({dak.currentAssignment.deskCode})
              </span>
              <span className="dak-custody-user">
                Officer: <strong>{dak.currentAssignment.assignedUserDisplayName || "General Desk Assignment"}</strong>
                {" • "}
                Marked by: {dak.currentAssignment.assignedByDisplayName} on{" "}
                {new Date(dak.currentAssignment.assignedAt).toLocaleString("en-IN", { dateStyle: "medium", timeStyle: "short" })}
              </span>
              {dak.currentAssignment.instructions && (
                <span style={{ fontSize: "12px", color: "#1d4ed8", marginTop: "4px" }}>
                  <em>Note: {dak.currentAssignment.instructions}</em>
                </span>
              )}
            </>
          ) : (
            <span className="dak-custody-desk" style={{ color: "#64748b" }}>
              Unassigned / Intake Queue (Reception)
            </span>
          )}
        </div>

        <div className="dak-custody-actions">
          {canAssignWork && (
            <Link
              to={`/work/new?dakId=${dak.id}`}
              className="primary-button"
              style={{
                display: "inline-flex",
                alignItems: "center",
                gap: 6,
                textDecoration: "none",
                fontWeight: 500,
                background: "#0284c7",
              }}
            >
              + Assign Work
            </Link>
          )}

          {canMove && (
            <button
              className="primary-button"
              onClick={() => setMovementModalMode("move")}
            >
              {dak.currentAssignment ? "Forward / Return ➔" : "Mark to Desk ➔"}
            </button>
          )}

          {canEdit && (
            <button className="secondary-button" onClick={() => setShowEditModal(true)}>
              Edit Details
            </button>
          )}

          {canDispose && (
            <button className="secondary-button" onClick={() => setMovementModalMode("dispose")}>
              Dispose Dak
            </button>
          )}

          {canCancel && (
            <button className="secondary-button" style={{ color: "#b91c1c" }} onClick={() => setMovementModalMode("cancel")}>
              Cancel Entry
            </button>
          )}
        </div>
      </div>

      {/* Tabs */}
      <div style={{ display: "flex", gap: "10px", margin: "18px 0" }}>
        <button
          className={`secondary-button ${activeTab === "overview" ? "active" : ""}`}
          onClick={() => setActiveTab("overview")}
        >
          Overview & Details
        </button>
        <button
          className={`secondary-button ${activeTab === "documents" ? "active" : ""}`}
          onClick={() => setActiveTab("documents")}
        >
          Documents & Attachments ({dak.attachments.length + (dak.mainDocumentId ? 1 : 0)})
        </button>
        <button
          className={`secondary-button ${activeTab === "links" ? "active" : ""}`}
          onClick={() => setActiveTab("links")}
        >
          Linked Context ({dak.villageLinks.length + dak.awardLinks.length + dak.matterLinks.length + dak.khasraLinks.length})
        </button>
        <button
          className={`secondary-button ${activeTab === "timeline" ? "active" : ""}`}
          onClick={() => setActiveTab("timeline")}
        >
          Movement Timeline
        </button>
      </div>

      {/* Tab 1: Overview */}
      {activeTab === "overview" && (
        <div className="dak-grid-2col">
          <div className="info-section">
            <h2>Correspondence Metadata</h2>
            <dl>
              <div>
                <dt>Diary Number</dt>
                <dd><strong>{dak.diaryNumber}</strong></dd>
              </div>
              <div>
                <dt>Received Date</dt>
                <dd>{new Date(dak.receivedDate).toLocaleDateString("en-IN", { dateStyle: "long" })}</dd>
              </div>
              <div>
                <dt>Inward Mode</dt>
                <dd>{dak.inwardMode}</dd>
              </div>
              <div>
                <dt>Priority</dt>
                <dd><span className={`priority-pill priority-${dak.priority.toLowerCase()}`}>{dak.priority}</span></dd>
              </div>
              <div>
                <dt>Action Due Date</dt>
                <dd>{dak.dueDate ? new Date(dak.dueDate).toLocaleDateString("en-IN", { dateStyle: "long" }) : "None specified"}</dd>
              </div>
              <div>
                <dt>Category</dt>
                <dd>{dak.categoryName || "Unclassified"}</dd>
              </div>
              <div>
                <dt>Functional Workstream</dt>
                <dd>{dak.workstreamName || "General / Unassigned"}</dd>
              </div>
              <div>
                <dt>Status</dt>
                <dd><span className={`status-pill status-${dak.status.toLowerCase()}`}>{dak.status}</span></dd>
              </div>
              <div>
                <dt>Concurrency Revision</dt>
                <dd>Rev #{dak.revision}</dd>
              </div>
            </dl>
          </div>

          <div className="info-section">
            <h2>Sender Information & References</h2>
            <dl>
              <div>
                <dt>Sender Name</dt>
                <dd><strong>{dak.senderName}</strong></dd>
              </div>
              <div>
                <dt>Designation</dt>
                <dd>{dak.senderDesignation || "—"}</dd>
              </div>
              <div>
                <dt>Department / Entity</dt>
                <dd>{dak.senderDepartment || "—"}</dd>
              </div>
              <div>
                <dt>Letter Reference No.</dt>
                <dd>{dak.senderReferenceNumber || "—"}</dd>
              </div>
              <div>
                <dt>Letter Date</dt>
                <dd>{dak.senderLetterDate ? new Date(dak.senderLetterDate).toLocaleDateString("en-IN", { dateStyle: "long" }) : "—"}</dd>
              </div>
              <div>
                <dt>Postal / Contact Address</dt>
                <dd>{dak.senderAddress || "—"}</dd>
              </div>
              <div>
                <dt>Registered By</dt>
                <dd>{dak.createdBy || "System"}</dd>
              </div>
              <div>
                <dt>Registered At</dt>
                <dd>{new Date(dak.createdAt).toLocaleString("en-IN", { dateStyle: "medium", timeStyle: "short" })}</dd>
              </div>
            </dl>
          </div>
        </div>
      )}

      {/* Tab 2: Documents & Attachments */}
      {activeTab === "documents" && (
        <div>
          <div className="section-heading">
            <div>
              <h2>Scanned Correspondence & Supporting Documents</h2>
              <span>Primary inward scan and attached exhibits, annexures, or court notices.</span>
            </div>
            {canEdit && (
              <button className="primary-button" onClick={() => setShowAttachModal(true)}>
                + Add Attachment
              </button>
            )}
          </div>

          {dak.mainDocumentId ? (
            <div className="workspace-panel" style={{ marginBottom: "20px" }}>
              <div className="panel-title">
                <h3>Primary Inward Document</h3>
                <a
                  className="primary-button"
                  href={`/api/dak/${dak.id}/content`}
                  target="_blank"
                  rel="noreferrer"
                >
                  View / Download Document
                </a>
              </div>
              <p>File: <strong>{dak.mainDocumentFileName || "Main Document"}</strong></p>
            </div>
          ) : (
            <div className="state">No primary inward scan was uploaded during registration.</div>
          )}

          <h3>Additional Enclosures & Annexures</h3>
          {dak.attachments.length === 0 ? (
            <p className="subtext">No additional attachments uploaded.</p>
          ) : (
            <div className="table-wrap">
              <table>
                <thead>
                  <tr>
                    <th>#</th>
                    <th>Document Title</th>
                    <th>Type</th>
                    <th>Original File</th>
                    <th>Uploaded Date</th>
                    <th>Action</th>
                  </tr>
                </thead>
                <tbody>
                  {dak.attachments.map((a) => (
                    <tr key={a.id}>
                      <td>{a.sequenceOrder}</td>
                      <td><strong>{a.title}</strong></td>
                      <td>{a.attachmentType}</td>
                      <td>{a.originalFileName}</td>
                      <td>{new Date(a.createdAt).toLocaleDateString("en-IN", { dateStyle: "medium" })}</td>
                      <td>
                        <div style={{ display: "flex", gap: "8px" }}>
                          <a
                            className="text-action"
                            href={`/api/dak/${dak.id}/attachments/${a.id}/content`}
                            target="_blank"
                            rel="noreferrer"
                          >
                            Download
                          </a>
                          {canEdit && (
                            <button
                              className="text-action"
                              style={{ color: "#ef4444" }}
                              onClick={() => void handleDeleteAttachment(a.id)}
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

      {/* Tab 3: Linked Entities */}
      {activeTab === "links" && (
        <div>
          <div className="section-heading">
            <div>
              <h2>Domain Cross-References</h2>
              <span>Link this correspondence to specific Villages, Awards, Court Matters, or Khasras.</span>
            </div>
            {canEdit && (
              <button className="primary-button" onClick={() => setShowLinkModal(true)}>
                + Add Cross-Reference
              </button>
            )}
          </div>

          <div className="dak-grid-2col">
            <div className="info-section">
              <h3>Linked Awards ({dak.awardLinks.length})</h3>
              {dak.awardLinks.length === 0 ? (
                <p className="subtext">No awards linked.</p>
              ) : (
                dak.awardLinks.map((l) => (
                  <span key={l.linkId} className="dak-link-badge">
                    <Link to={`/awards/${l.entityId}`}>Award: {l.displayName}</Link>
                    {canEdit && (
                      <button className="dak-link-remove" onClick={() => void handleDeleteLink("awards", l.linkId)}>
                        ✕
                      </button>
                    )}
                  </span>
                ))
              )}

              <h3 style={{ marginTop: "20px" }}>Linked Villages ({dak.villageLinks.length})</h3>
              {dak.villageLinks.length === 0 ? (
                <p className="subtext">No villages linked.</p>
              ) : (
                dak.villageLinks.map((l) => (
                  <span key={l.linkId} className="dak-link-badge">
                    <Link to={`/villages/${l.entityId}`}>Village: {l.displayName}</Link>
                    {canEdit && (
                      <button className="dak-link-remove" onClick={() => void handleDeleteLink("villages", l.linkId)}>
                        ✕
                      </button>
                    )}
                  </span>
                ))
              )}
            </div>

            <div className="info-section">
              <h3>Linked Court Matters ({dak.matterLinks.length})</h3>
              {dak.matterLinks.length === 0 ? (
                <p className="subtext">No court matters linked.</p>
              ) : (
                dak.matterLinks.map((l) => (
                  <span key={l.linkId} className="dak-link-badge">
                    <Link to={`/matters/${l.entityId}`}>Matter: {l.displayName}</Link>
                    {canEdit && (
                      <button className="dak-link-remove" onClick={() => void handleDeleteLink("matters", l.linkId)}>
                        ✕
                      </button>
                    )}
                  </span>
                ))
              )}

              <h3 style={{ marginTop: "20px" }}>Linked Khasras ({dak.khasraLinks.length})</h3>
              {dak.khasraLinks.length === 0 ? (
                <p className="subtext">No khasras linked.</p>
              ) : (
                dak.khasraLinks.map((l) => (
                  <span key={l.linkId} className="dak-link-badge">
                    <Link to={`/khasras/${l.entityId}`}>Khasra: {l.displayName}</Link>
                    {canEdit && (
                      <button className="dak-link-remove" onClick={() => void handleDeleteLink("khasras", l.linkId)}>
                        ✕
                      </button>
                    )}
                  </span>
                ))
              )}
            </div>
          </div>

          {/* Outward Dispatches / Replies */}
          <div style={{ marginTop: "24px", paddingTop: "20px", borderTop: "1px solid #e2e8f0" }}>
            <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center", marginBottom: "12px" }}>
              <div>
                <h3 style={{ margin: 0, fontSize: "16px", color: "#1e3a58" }}>
                  Outward Replies & Dispatches ({outwardReplies.length})
                </h3>
                <span style={{ fontSize: "12px", color: "#64748b" }}>
                  Official outward letters issued in response to or referencing this Dak.
                </span>
              </div>
              {hasPermission("Outward.Create") && (
                <button
                  className="primary-button"
                  style={{ fontSize: "12px", padding: "4px 10px" }}
                  onClick={() => navigate(`/outward/new?dakId=${dak.id}`)}
                >
                  + Draft Outward Reply
                </button>
              )}
            </div>

            {outwardReplies.length === 0 ? (
              <p className="subtext">No outward replies have been issued for this Dak yet.</p>
            ) : (
              <div className="table-responsive">
                <table className="data-table">
                  <thead>
                    <tr>
                      <th>Outward No.</th>
                      <th>Subject</th>
                      <th>Recipient</th>
                      <th>Date</th>
                      <th>Status</th>
                      <th style={{ textAlign: "right" }}>Action</th>
                    </tr>
                  </thead>
                  <tbody>
                    {outwardReplies.map((r) => (
                      <tr key={r.id}>
                        <td>
                          <Link to={`/outward/${r.id}`} style={{ fontWeight: 600, color: "#1e609e" }}>
                            {r.outwardNumber}
                          </Link>
                        </td>
                        <td>{r.subject}</td>
                        <td>{r.recipientName}</td>
                        <td>{r.outwardDate}</td>
                        <td>
                          <span className={`outward-pill outward-pill-${r.status.toLowerCase()}`}>
                            {r.status}
                          </span>
                        </td>
                        <td style={{ textAlign: "right" }}>
                          <button
                            className="secondary-button"
                            style={{ fontSize: "11px", padding: "2px 8px" }}
                            onClick={() => navigate(`/outward/${r.id}`)}
                          >
                            Open
                          </button>
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            )}
          </div>
        </div>
      )}

      {/* Tab 4: Movement Timeline */}
      {activeTab === "timeline" && (
        <div>
          <h2>Official Movement & Noting History</h2>
          <p className="subtext">Immutable audit trail of all custody transfers, officer assignments, notings, and instructions.</p>
          <DakTimeline dakId={dak.id} />
        </div>
      )}

      {/* Movement Modal */}
      {movementModalMode && (
        <DakMovementModal
          dak={dak}
          mode={movementModalMode}
          onClose={() => setMovementModalMode(null)}
          onSuccess={() => {
            setMovementModalMode(null);
            void loadDak();
          }}
        />
      )}

      {/* Edit Metadata Modal */}
      {showEditModal && (
        <div className="modal-backdrop">
          <div className="modal-card" style={{ maxWidth: "720px" }}>
            <h3>Edit Dak Classification & Details</h3>
            <form onSubmit={handleSaveMetadata}>
              <div className="field-grid" style={{ marginBottom: "14px" }}>
                <label className="span-two">
                  Subject *
                  <input
                    type="text"
                    required
                    value={editSubject}
                    onChange={(e) => setEditSubject(e.target.value)}
                  />
                </label>
                <label>
                  Sender Name *
                  <input
                    type="text"
                    required
                    value={editSenderName}
                    onChange={(e) => setEditSenderName(e.target.value)}
                  />
                </label>
                <label>
                  Sender Designation
                  <input
                    type="text"
                    value={editSenderDesignation}
                    onChange={(e) => setEditSenderDesignation(e.target.value)}
                  />
                </label>
                <label>
                  Department / Entity
                  <input
                    type="text"
                    value={editSenderDepartment}
                    onChange={(e) => setEditSenderDepartment(e.target.value)}
                  />
                </label>
                <label>
                  Sender Reference No.
                  <input
                    type="text"
                    value={editSenderRef}
                    onChange={(e) => setEditSenderRef(e.target.value)}
                  />
                </label>
                <label>
                  Letter Date
                  <input
                    type="date"
                    value={editLetterDate}
                    onChange={(e) => setEditLetterDate(e.target.value)}
                  />
                </label>
                <label>
                  Inward Mode
                  <input
                    type="text"
                    value={editInwardMode}
                    onChange={(e) => setEditInwardMode(e.target.value)}
                  />
                </label>
                <label>
                  Priority
                  <select
                    value={editPriority}
                    onChange={(e) => setEditPriority(e.target.value as "Routine" | "Urgent" | "Immediate")}
                  >
                    <option value="Routine">Routine</option>
                    <option value="Urgent">Urgent</option>
                    <option value="Immediate">Immediate</option>
                  </select>
                </label>
                <label>
                  Action Due Date
                  <input
                    type="date"
                    value={editDueDate}
                    onChange={(e) => setEditDueDate(e.target.value)}
                  />
                </label>
                <label>
                  Category
                  <select value={editCategoryId} onChange={(e) => setEditCategoryId(e.target.value)}>
                    <option value="">-- Unclassified --</option>
                    {categories.map((c) => (
                      <option key={c.id} value={c.id}>{c.name}</option>
                    ))}
                  </select>
                </label>
                <label>
                  Workstream
                  <select value={editWorkstreamId} onChange={(e) => setEditWorkstreamId(e.target.value)}>
                    <option value="">-- Unassigned --</option>
                    {workstreams.map((w) => (
                      <option key={w.id} value={w.id}>{w.name}</option>
                    ))}
                  </select>
                </label>
                <label className="span-two">
                  Sender Address
                  <textarea
                    value={editSenderAddress}
                    onChange={(e) => setEditSenderAddress(e.target.value)}
                    rows={2}
                  />
                </label>
              </div>
              <div className="modal-actions" style={{ display: "flex", justifyContent: "flex-end", gap: "10px" }}>
                <button type="button" className="secondary-button" onClick={() => setShowEditModal(false)}>
                  Cancel
                </button>
                <button type="submit" className="primary-button">
                  Save Changes
                </button>
              </div>
            </form>
          </div>
        </div>
      )}

      {/* Add Attachment Modal */}
      {showAttachModal && (
        <div className="modal-backdrop">
          <div className="modal-card">
            <h3>Add Attachment</h3>
            <form onSubmit={handleAddAttachment}>
              <div className="field-group" style={{ marginBottom: "14px" }}>
                <label>File *</label>
                <input
                  type="file"
                  required
                  onChange={(e) => setAttachFile(e.target.files?.[0] || null)}
                />
              </div>
              <div className="field-group" style={{ marginBottom: "14px" }}>
                <label>Attachment Title</label>
                <input
                  type="text"
                  placeholder="e.g. High Court Notice / Site Inspection Report"
                  value={attachTitle}
                  onChange={(e) => setAttachTitle(e.target.value)}
                />
              </div>
              <div className="field-group" style={{ marginBottom: "14px" }}>
                <label>Attachment Type</label>
                <select value={attachType} onChange={(e) => setAttachType(e.target.value)}>
                  <option value="Enclosure">Enclosure</option>
                  <option value="Annexure">Annexure</option>
                  <option value="CourtNotice">Court Notice</option>
                  <option value="InspectionReport">Inspection Report</option>
                  <option value="Other">Other Supporting Document</option>
                </select>
              </div>
              <div className="modal-actions" style={{ display: "flex", justifyContent: "flex-end", gap: "10px" }}>
                <button type="button" className="secondary-button" onClick={() => setShowAttachModal(false)} disabled={attaching}>
                  Cancel
                </button>
                <button type="submit" className="primary-button" disabled={attaching || !attachFile}>
                  {attaching ? "Uploading..." : "Upload Attachment"}
                </button>
              </div>
            </form>
          </div>
        </div>
      )}

      {/* Add Link Modal */}
      {showLinkModal && (
        <div className="modal-backdrop">
          <div className="modal-card">
            <h3>Link Domain Entity</h3>
            <form onSubmit={handleAddLink}>
              <div className="field-group" style={{ marginBottom: "14px" }}>
                <label>Target Entity Type</label>
                <select
                  value={linkType}
                  onChange={(e) => setLinkType(e.target.value as "Village" | "Award" | "Matter" | "Khasra")}
                >
                  <option value="Award">Award</option>
                  <option value="Matter">Court Matter</option>
                  <option value="Village">Village</option>
                  <option value="Khasra">Khasra</option>
                </select>
              </div>
              <div className="field-group" style={{ marginBottom: "14px" }}>
                <label>Entity ID (UUID) *</label>
                <input
                  type="text"
                  required
                  placeholder="Enter Entity UUID (e.g. from Award, Matter, Village URL)"
                  value={linkEntityId}
                  onChange={(e) => setLinkEntityId(e.target.value)}
                />
              </div>
              <div className="modal-actions" style={{ display: "flex", justifyContent: "flex-end", gap: "10px" }}>
                <button type="button" className="secondary-button" onClick={() => setShowLinkModal(false)} disabled={linking}>
                  Cancel
                </button>
                <button type="submit" className="primary-button" disabled={linking || !linkEntityId.trim()}>
                  {linking ? "Linking..." : "Create Link"}
                </button>
              </div>
            </form>
          </div>
        </div>
      )}
    </div>
  );
};
