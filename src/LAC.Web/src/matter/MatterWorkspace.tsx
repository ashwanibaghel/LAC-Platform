import React, { useState, useEffect } from "react";
import { useParams, Link } from "react-router-dom";
import { useAuth } from "../auth/AuthProvider";
import { MatterDrafts } from "../editor/MatterDraftEditor";
import "./matter.css";

interface MatterDocumentItem {
  id: string;
  documentId: string;
  documentRole: string | null;
  displayName: string | null;
  originalFileName: string;
  mimeType: string;
  fileSize: number;
  uploadedAt: string;
}

interface EligibleDocumentItem {
  id: string;
  originalFileName: string;
  documentType: string;
  uploadedAt: string;
  source: string;
}

interface MatterEventItem {
  id: string;
  sequenceNumber: number;
  action: number;
  actionName: string;
  workstreamId: string | null;
  workstreamName: string | null;
  targetWorkstreamId: string | null;
  targetWorkstreamName: string | null;
  reason: string | null;
  actionAt: string;
  actionByUserId: string;
  actionByUserName: string;
}

interface MatterDetail {
  id: string;
  villageId: string;
  villageName: string;
  workstreamId: string | null;
  workstreamName: string | null;
  workstreamCode: string | null;
  title: string;
  matterType: string;
  status: string;
  referenceNumber: string | null;
  remarks: string | null;
  khasraReferenceText: string | null;
  revision: number;
  createdAt: string;
  updatedAt: string;
  events: MatterEventItem[];
}

interface WorkstreamOption {
  id: string;
  name: string;
  code: string;
}

export const MatterWorkspace: React.FC<{ MatterOutwardSection: React.ComponentType<{ matterId: string }> }> = ({
  MatterOutwardSection
}) => {
  const { id = "" } = useParams();
  const { hasPermission } = useAuth();

  const [matter, setMatter] = useState<MatterDetail | null>(null);
  const [documents, setDocuments] = useState<MatterDocumentItem[]>([]);
  const [eligibleDocs, setEligibleDocs] = useState<EligibleDocumentItem[]>([]);
  const [workstreams, setWorkstreams] = useState<WorkstreamOption[]>([]);
  const [selectedDocIds, setSelectedDocIds] = useState<string[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [refresh, setRefresh] = useState(0);

  // Modals & Panels
  const [showEditModal, setShowEditModal] = useState(false);
  const [showReclassifyModal, setShowReclassifyModal] = useState(false);
  const [showArchiveModal, setShowArchiveModal] = useState(false);
  const [showLinkPicker, setShowLinkPicker] = useState(false);

  // Edit State
  const [editTitle, setEditTitle] = useState("");
  const [editRefNo, setEditRefNo] = useState("");
  const [editRemarks, setEditRemarks] = useState("");
  const [editKhasraRef, setEditKhasraRef] = useState("");
  const [editError, setEditError] = useState<string | null>(null);
  const [savingEdit, setSavingEdit] = useState(false);

  // Reclassify State
  const [reclassifyTargetId, setReclassifyTargetId] = useState("");
  const [reclassifyReason, setReclassifyReason] = useState("");
  const [reclassifyError, setReclassifyError] = useState<string | null>(null);
  const [savingReclassify, setSavingReclassify] = useState(false);

  // Archive State
  const [archiveReason, setArchiveReason] = useState("");
  const [archiveError, setArchiveError] = useState<string | null>(null);
  const [savingArchive, setSavingArchive] = useState(false);

  // Upload Document State
  const [uploadFile, setUploadFile] = useState<File | null>(null);
  const [uploadRole, setUploadRole] = useState("Application");
  const [uploadDisplayName, setUploadDisplayName] = useState("");
  const [uploading, setUploading] = useState(false);
  const [uploadError, setUploadError] = useState<string | null>(null);

  // Linking State
  const [linkingDocId, setLinkingDocId] = useState<string | null>(null);
  const [linkRole, setLinkRole] = useState("Other");
  const [linkError, setLinkError] = useState<string | null>(null);

  // Load Matter Details & Documents
  useEffect(() => {
    let active = true;
    setLoading(true);
    setError(null);

    Promise.all([
      fetch(`/api/matters/${id}?r=${refresh}`, { credentials: "include" }),
      fetch(`/api/matters/${id}/documents?r=${refresh}`, { credentials: "include" })
    ])
      .then(async ([resMatter, resDocs]) => {
        if (!resMatter.ok) {
          if (resMatter.status === 403) throw new Error("Access denied: You do not have permission to view this matter.");
          if (resMatter.status === 404) throw new Error("Matter not found.");
          throw new Error("Failed to load matter details.");
        }
        const mData: MatterDetail = await resMatter.json();
        let dData: MatterDocumentItem[] = [];
        if (resDocs.ok) {
          dData = await resDocs.json();
        }
        if (active) {
          setMatter(mData);
          setDocuments(dData);
          setEditTitle(mData.title);
          setEditRefNo(mData.referenceNumber || "");
          setEditRemarks(mData.remarks || "");
          setEditKhasraRef(mData.khasraReferenceText || "");
        }
      })
      .catch((err) => {
        if (active) setError(err.message);
      })
      .finally(() => {
        if (active) setLoading(false);
      });

    return () => {
      active = false;
    };
  }, [id, refresh]);

  // Load Workstreams lookup
  useEffect(() => {
    fetch("/api/matters/context", { credentials: "include" })
      .then((res) => (res.ok ? res.json() : null))
      .then((data) => {
        if (data?.workstreams) {
          setWorkstreams(data.workstreams);
          if (data.workstreams.length > 0 && !reclassifyTargetId) {
            setReclassifyTargetId(data.workstreams[0].id);
          }
        }
      })
      .catch(() => {});
  }, [reclassifyTargetId]);

  // Load Eligible Documents when picker opened
  useEffect(() => {
    if (!showLinkPicker) return;
    fetch(`/api/matters/${id}/eligible-documents?r=${refresh}`, { credentials: "include" })
      .then((res) => (res.ok ? res.json() : []))
      .then((data) => setEligibleDocs(data))
      .catch(() => setEligibleDocs([]));
  }, [id, showLinkPicker, refresh]);

  const toggleSelectDoc = (docId: string) => {
    setSelectedDocIds((prev) => (prev.includes(docId) ? prev.filter((x) => x !== docId) : [...prev, docId]));
  };

  const handleUpdateMetadata = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!matter) return;
    if (!editTitle.trim()) {
      setEditError("Title is required.");
      return;
    }

    try {
      setSavingEdit(true);
      setEditError(null);
      const res = await fetch(`/api/matters/${id}`, {
        method: "PUT",
        headers: { "Content-Type": "application/json" },
        credentials: "include",
        body: JSON.stringify({
          title: editTitle.trim(),
          referenceNumber: editRefNo.trim() || null,
          remarks: editRemarks.trim() || null,
          khasraReferenceText: editKhasraRef.trim() || null,
          expectedRevision: matter.revision
        })
      });

      if (!res.ok) {
        const errorData = await res.json().catch(() => null);
        throw new Error(errorData?.message || errorData?.title || "Failed to update matter details.");
      }

      setShowEditModal(false);
      setRefresh((r) => r + 1);
    } catch (err: any) {
      setEditError(err.message || "Failed to update matter.");
    } finally {
      setSavingEdit(false);
    }
  };

  const handleReclassify = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!matter) return;
    if (!reclassifyTargetId) {
      setReclassifyError("Target workstream is required.");
      return;
    }
    if (!reclassifyReason.trim()) {
      setReclassifyError("Reclassification reason is mandatory.");
      return;
    }

    try {
      setSavingReclassify(true);
      setReclassifyError(null);
      const res = await fetch(`/api/matters/${id}/reclassify`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        credentials: "include",
        body: JSON.stringify({
          targetWorkstreamId: reclassifyTargetId,
          reason: reclassifyReason.trim(),
          expectedRevision: matter.revision
        })
      });

      if (!res.ok) {
        const errorData = await res.json().catch(() => null);
        throw new Error(errorData?.message || errorData?.title || "Failed to reclassify matter.");
      }

      setShowReclassifyModal(false);
      setReclassifyReason("");
      setRefresh((r) => r + 1);
    } catch (err: any) {
      setReclassifyError(err.message || "Failed to reclassify.");
    } finally {
      setSavingReclassify(false);
    }
  };

  const handleArchive = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!matter) return;
    if (!archiveReason.trim()) {
      setArchiveError("Archival reason is mandatory.");
      return;
    }

    try {
      setSavingArchive(true);
      setArchiveError(null);
      const res = await fetch(`/api/matters/${id}/archive`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        credentials: "include",
        body: JSON.stringify({
          reason: archiveReason.trim(),
          expectedRevision: matter.revision
        })
      });

      if (!res.ok) {
        const errorData = await res.json().catch(() => null);
        throw new Error(errorData?.message || errorData?.title || "Failed to archive matter.");
      }

      setShowArchiveModal(false);
      setArchiveReason("");
      setRefresh((r) => r + 1);
    } catch (err: any) {
      setArchiveError(err.message || "Failed to archive.");
    } finally {
      setSavingArchive(false);
    }
  };

  const handleUploadDocument = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!matter || !uploadFile) return;

    try {
      setUploading(true);
      setUploadError(null);
      const form = new FormData();
      form.append("file", uploadFile);
      form.append("role", uploadRole);
      if (uploadDisplayName.trim()) form.append("displayName", uploadDisplayName.trim());
      form.append("expectedRevision", matter.revision.toString());

      const res = await fetch(`/api/matters/${id}/documents`, {
        method: "POST",
        credentials: "include",
        body: form
      });

      if (!res.ok) {
        const errData = await res.json().catch(() => null);
        throw new Error(errData?.message || errData?.title || "Failed to upload document.");
      }

      setUploadFile(null);
      setUploadDisplayName("");
      setRefresh((r) => r + 1);
    } catch (err: any) {
      setUploadError(err.message || "Document upload failed.");
    } finally {
      setUploading(false);
    }
  };

  const handleLinkExisting = async (docId: string) => {
    if (!matter) return;

    try {
      setLinkingDocId(docId);
      setLinkError(null);
      const res = await fetch(`/api/matters/${id}/documents/link`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        credentials: "include",
        body: JSON.stringify({
          documentId: docId,
          role: linkRole,
          displayName: null,
          expectedRevision: matter.revision
        })
      });

      if (!res.ok) {
        const errData = await res.json().catch(() => null);
        throw new Error(errData?.message || errData?.title || "Failed to link document.");
      }

      setShowLinkPicker(false);
      setRefresh((r) => r + 1);
    } catch (err: any) {
      setLinkError(err.message || "Failed to link document.");
    } finally {
      setLinkingDocId(null);
    }
  };

  const handleExportZip = async () => {
    if (!matter || selectedDocIds.length === 0) return;
    try {
      const res = await fetch(`/api/matters/${id}/export`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        credentials: "include",
        body: JSON.stringify({ documentIds: selectedDocIds })
      });

      if (!res.ok) {
        const errData = await res.json().catch(() => null);
        alert(errData?.message || "Failed to export selected documents.");
        return;
      }

      const blob = await res.blob();
      const url = URL.createObjectURL(blob);
      const a = document.createElement("a");
      a.href = url;
      a.download = `matter-${id.slice(0, 8)}-documents.zip`;
      a.click();
      URL.revokeObjectURL(url);
    } catch {
      alert("Failed to export documents.");
    }
  };

  if (loading) return <div className="state loading">Loading matter workspace...</div>;
  if (error || !matter) return <div className="state error">{error || "Matter not found."}</div>;

  const isArchived = matter.status === "Archived";
  const canEdit = !isArchived && hasPermission("Matter.Edit");
  const canManageDocs = !isArchived && hasPermission("Matter.Document.Manage");
  const canArchive = !isArchived && hasPermission("Matter.Archive");

  return (
    <>
      <nav className="breadcrumbs" aria-label="Breadcrumb">
        <Link to="/matters">Matters</Link>
        <span className="breadcrumb-separator">/</span>
        <Link to={`/villages/${matter.villageId}`}>{matter.villageName}</Link>
        <span className="breadcrumb-separator">/</span>
        <span>{matter.title}</span>
      </nav>

      <div className="page-header">
        <div className="page-title-row">
          <div>
            <div className="page-eyebrow">
              {matter.matterType} · Village: {matter.villageName} · Rev {matter.revision}
            </div>
            <h1 className="page-title">{matter.title}</h1>
          </div>
          <div style={{ display: "flex", gap: 8, alignItems: "center" }}>
            {matter.workstreamName ? (
              <span className="matter-badge-workstream" title={`Code: ${matter.workstreamCode}`}>
                {matter.workstreamName}
              </span>
            ) : (
              <span className="matter-badge-unclassified" title="Legacy unclassified matter">
                [Unclassified]
              </span>
            )}
            <span className={isArchived ? "matter-badge-archived" : "matter-badge-open"}>{matter.status}</span>
          </div>
        </div>

        {isArchived && (
          <div className="matter-archived-banner">
            <span>🔒</span>
            <span>
              This matter is <strong>Archived</strong> and in a terminal read-only state. No further edits, uploads,
              or document links are permitted.
            </span>
          </div>
        )}

        {/* Action Controls */}
        {!isArchived && (
          <div className="matter-actions-strip">
            {canEdit && (
              <>
                <button onClick={() => setShowEditModal(true)}>Edit Details</button>
                <button onClick={() => setShowReclassifyModal(true)}>Reclassify Workstream</button>
              </>
            )}
            {canArchive && (
              <button className="quiet-button" style={{ color: "#b91c1c" }} onClick={() => setShowArchiveModal(true)}>
                Archive Matter
              </button>
            )}
          </div>
        )}
      </div>

      {/* Metadata & Case Information */}
      <section className="section">
        <h2>Case Details</h2>
        <div className="field-grid" style={{ marginBottom: 12 }}>
          <div>
            <strong>Reference Number:</strong> {matter.referenceNumber || "—"}
          </div>
          <div>
            <strong>Village:</strong>{" "}
            <Link to={`/villages/${matter.villageId}`}>{matter.villageName}</Link>
          </div>
          <div>
            <strong>Matter Type:</strong> {matter.matterType}
          </div>
          <div>
            <strong>Khasra Reference:</strong> {matter.khasraReferenceText || "—"}
          </div>
          <div style={{ gridColumn: "span 2" }}>
            <strong>Remarks / Background:</strong> {matter.remarks || "—"}
          </div>
        </div>
      </section>

      {/* Matter Documents */}
      <section className="section">
        <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center", marginBottom: 12 }}>
          <div>
            <h2>Matter Documents</h2>
            <span className="hint">Direct evidentiary documents uploaded or linked specifically to this matter.</span>
          </div>
          {canManageDocs && (
            <div style={{ display: "flex", gap: 8 }}>
              <button
                disabled={selectedDocIds.length === 0}
                onClick={() => void handleExportZip()}
                title="Export selected documents as a secure ZIP package"
              >
                Export Selected ({selectedDocIds.length})
              </button>
              <button onClick={() => setShowLinkPicker((v) => !v)}>
                {showLinkPicker ? "Close Picker" : "+ Link Existing Document"}
              </button>
            </div>
          )}
        </div>

        {/* Upload Document Form */}
        {canManageDocs && (
          <form onSubmit={handleUploadDocument} className="field-grid" style={{ background: "#f8fafc", padding: 16, borderRadius: 8, marginBottom: 16 }}>
            <label>
              Document Role *
              <select value={uploadRole} onChange={(e) => setUploadRole(e.target.value)}>
                {["Application", "Court Order", "ADM Letter", "Joint Declaration", "Khatoni", "Demarcation", "Correspondence", "Other"].map((r) => (
                  <option key={r} value={r}>
                    {r}
                  </option>
                ))}
              </select>
            </label>

            <label>
              Display Name (optional)
              <input
                type="text"
                value={uploadDisplayName}
                onChange={(e) => setUploadDisplayName(e.target.value)}
                placeholder="e.g. Order dt 12-05-2024"
              />
            </label>

            <label style={{ gridColumn: "span 2" }}>
              Select File *
              <input
                type="file"
                onChange={(e) => setUploadFile(e.target.files?.[0] || null)}
                required
              />
            </label>

            {uploadError && <div style={{ color: "#b91c1c", gridColumn: "span 2" }}>{uploadError}</div>}

            <div style={{ gridColumn: "span 2" }}>
              <button type="submit" disabled={uploading || !uploadFile}>
                {uploading ? "Uploading..." : "Upload Document"}
              </button>
            </div>
          </form>
        )}

        {/* Existing Document Picker Drawer */}
        {showLinkPicker && (
          <aside className="section" style={{ background: "#f0f9ff", border: "1px solid #bae6fd", padding: 16, borderRadius: 8, marginBottom: 16 }}>
            <h3>Link Existing Document</h3>
            <span className="hint">Select an eligible document from linked Award or Land Record families.</span>
            {linkError && <p style={{ color: "#b91c1c" }}>{linkError}</p>}

            <div style={{ margin: "12px 0", display: "flex", gap: 8, alignItems: "center" }}>
              <label>
                Role:
                <select value={linkRole} onChange={(e) => setLinkRole(e.target.value)} style={{ marginLeft: 6 }}>
                  {["Application", "Court Order", "ADM Letter", "Joint Declaration", "Khatoni", "Demarcation", "Correspondence", "Other"].map((r) => (
                    <option key={r} value={r}>
                      {r}
                    </option>
                  ))}
                </select>
              </label>
            </div>

            {eligibleDocs.length === 0 ? (
              <p>No eligible documents available for linking.</p>
            ) : (
              <div style={{ maxHeight: 240, overflowY: "auto" }}>
                {eligibleDocs.map((doc) => (
                  <div
                    key={doc.id}
                    style={{
                      display: "flex",
                      justifyContent: "space-between",
                      alignItems: "center",
                      padding: "8px 0",
                      borderBottom: "1px solid #e2e8f0"
                    }}
                  >
                    <div>
                      <strong>{doc.originalFileName}</strong>
                      <div style={{ fontSize: 12, color: "#64748b" }}>
                        {doc.source} · {new Date(doc.uploadedAt).toLocaleDateString()}
                      </div>
                    </div>
                    <button
                      disabled={linkingDocId === doc.id}
                      onClick={() => void handleLinkExisting(doc.id)}
                    >
                      {linkingDocId === doc.id ? "Linking..." : "Attach"}
                    </button>
                  </div>
                ))}
              </div>
            )}
          </aside>
        )}

        {/* Documents Table */}
        {documents.length === 0 ? (
          <div className="state empty">
            <strong>No documents linked to this matter.</strong>
            <span>Upload or link case files above.</span>
          </div>
        ) : (
          <table className="data-table">
            <thead>
              <tr>
                <th style={{ width: 40 }}>
                  <input
                    type="checkbox"
                    checked={documents.length > 0 && selectedDocIds.length === documents.length}
                    onChange={(e) => {
                      if (e.target.checked) setSelectedDocIds(documents.map((d) => d.documentId));
                      else setSelectedDocIds([]);
                    }}
                  />
                </th>
                <th>Role</th>
                <th>Name / File</th>
                <th>Size</th>
                <th>Uploaded</th>
                <th>Action</th>
              </tr>
            </thead>
            <tbody>
              {documents.map((d) => (
                <tr key={d.id}>
                  <td>
                    <input
                      type="checkbox"
                      checked={selectedDocIds.includes(d.documentId)}
                      onChange={() => toggleSelectDoc(d.documentId)}
                    />
                  </td>
                  <td>
                    <strong>{d.documentRole || "Other"}</strong>
                  </td>
                  <td>
                    <div>{d.displayName || d.originalFileName}</div>
                    {d.displayName && <small style={{ color: "#64748b" }}>{d.originalFileName}</small>}
                  </td>
                  <td>{(d.fileSize / 1024).toFixed(1)} KB</td>
                  <td>{new Date(d.uploadedAt).toLocaleDateString()}</td>
                  <td>
                    <a
                      href={`/api/matters/${id}/documents/${d.documentId}/content`}
                      target="_blank"
                      rel="noreferrer"
                      className="quiet-button"
                    >
                      View / Download
                    </a>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </section>

      {/* Matter Drafts Section */}
      <MatterDrafts matterId={id} />

      {/* Matter Outward Communications Section */}
      <MatterOutwardSection matterId={id} />

      {/* Matter Audit & Activity History */}
      {matter.events && matter.events.length > 0 && (
        <section className="section">
          <h2>Activity & Audit History</h2>
          <div>
            {matter.events.map((ev) => (
              <div key={ev.id} className="matter-event-item">
                <span className="matter-event-seq">#{ev.sequenceNumber}</span>
                <strong>{ev.actionName}</strong>
                {ev.workstreamName && (
                  <span>
                    {" "}
                    · Workstream: <em>{ev.workstreamName}</em>
                  </span>
                )}
                {ev.targetWorkstreamName && (
                  <span>
                    {" "}
                    ➔ <em>{ev.targetWorkstreamName}</em>
                  </span>
                )}
                {ev.reason && <span> — &ldquo;{ev.reason}&rdquo;</span>}
                <div className="matter-event-meta">
                  By {ev.actionByUserName} on {new Date(ev.actionAt).toLocaleString()}
                </div>
              </div>
            ))}
          </div>
        </section>
      )}

      {/* Edit Details Modal */}
      {showEditModal && (
        <div className="matter-modal-overlay">
          <div className="matter-modal-card">
            <h3 className="matter-modal-title">Edit Matter Details</h3>
            {editError && <p style={{ color: "#b91c1c", marginBottom: 12 }}>{editError}</p>}
            <form onSubmit={handleUpdateMetadata}>
              <div className="field-grid">
                <label style={{ gridColumn: "span 2" }}>
                  Title *
                  <input
                    type="text"
                    value={editTitle}
                    onChange={(e) => setEditTitle(e.target.value)}
                    required
                  />
                </label>

                <label>
                  Reference Number
                  <input
                    type="text"
                    value={editRefNo}
                    onChange={(e) => setEditRefNo(e.target.value)}
                  />
                </label>

                <label>
                  Khasra Reference
                  <input
                    type="text"
                    value={editKhasraRef}
                    onChange={(e) => setEditKhasraRef(e.target.value)}
                  />
                </label>

                <label style={{ gridColumn: "span 2" }}>
                  Remarks
                  <textarea
                    value={editRemarks}
                    onChange={(e) => setEditRemarks(e.target.value)}
                    rows={3}
                  />
                </label>
              </div>

              <div className="matter-modal-actions">
                <button type="button" className="quiet-button" onClick={() => setShowEditModal(false)}>
                  Cancel
                </button>
                <button type="submit" disabled={savingEdit || !editTitle.trim()}>
                  {savingEdit ? "Saving..." : "Save Changes"}
                </button>
              </div>
            </form>
          </div>
        </div>
      )}

      {/* Reclassify Workstream Modal */}
      {showReclassifyModal && (
        <div className="matter-modal-overlay">
          <div className="matter-modal-card">
            <h3 className="matter-modal-title">Reclassify Matter Workstream</h3>
            <p className="hint">
              Changing the matter workstream alters resource ownership and visibility across teams.
            </p>
            {reclassifyError && <p style={{ color: "#b91c1c", marginBottom: 12 }}>{reclassifyError}</p>}
            <form onSubmit={handleReclassify}>
              <div className="field-grid">
                <label style={{ gridColumn: "span 2" }}>
                  Target Workstream *
                  <select
                    value={reclassifyTargetId}
                    onChange={(e) => setReclassifyTargetId(e.target.value)}
                    required
                  >
                    {workstreams.map((w) => (
                      <option key={w.id} value={w.id}>
                        {w.name} ({w.code})
                      </option>
                    ))}
                  </select>
                </label>

                <label style={{ gridColumn: "span 2" }}>
                  Mandatory Reason *
                  <textarea
                    value={reclassifyReason}
                    onChange={(e) => setReclassifyReason(e.target.value)}
                    placeholder="Provide official justification for reclassification..."
                    rows={3}
                    required
                  />
                </label>
              </div>

              <div className="matter-modal-actions">
                <button type="button" className="quiet-button" onClick={() => setShowReclassifyModal(false)}>
                  Cancel
                </button>
                <button type="submit" disabled={savingReclassify || !reclassifyReason.trim()}>
                  {savingReclassify ? "Reclassifying..." : "Confirm Reclassification"}
                </button>
              </div>
            </form>
          </div>
        </div>
      )}

      {/* Archive Matter Modal */}
      {showArchiveModal && (
        <div className="matter-modal-overlay">
          <div className="matter-modal-card">
            <h3 className="matter-modal-title">Archive Official Matter</h3>
            <p className="hint" style={{ color: "#b91c1c" }}>
              Warning: Archiving a matter is a terminal state. Once archived, the matter and its documents cannot be
              modified, uploaded to, or linked.
            </p>
            {archiveError && <p style={{ color: "#b91c1c", marginBottom: 12 }}>{archiveError}</p>}
            <form onSubmit={handleArchive}>
              <div className="field-grid">
                <label style={{ gridColumn: "span 2" }}>
                  Archival Reason *
                  <textarea
                    value={archiveReason}
                    onChange={(e) => setArchiveReason(e.target.value)}
                    placeholder="Provide reason for closing/archiving this matter..."
                    rows={3}
                    required
                  />
                </label>
              </div>

              <div className="matter-modal-actions">
                <button type="button" className="quiet-button" onClick={() => setShowArchiveModal(false)}>
                  Cancel
                </button>
                <button
                  type="submit"
                  className="primary"
                  style={{ backgroundColor: "#b91c1c" }}
                  disabled={savingArchive || !archiveReason.trim()}
                >
                  {savingArchive ? "Archiving..." : "Confirm Archival"}
                </button>
              </div>
            </form>
          </div>
        </div>
      )}
    </>
  );
};
