import React, { useState, useEffect, useRef } from "react";
import { useParams, Link } from "react-router-dom";
import { useAuth } from "../auth/AuthProvider";
import { MatterDrafts } from "../editor/MatterDraftEditor";
import {
  IconLock,
  IconMoreVertical,
  IconUpload,
  IconLink,
  IconDownload,
  IconClose,
  IconEdit,
  IconArchive,
  IconPlus,
  IconFileText,
  IconFile,
  IconBuilding,
  IconArrowRight,
  IconHistory
} from "../components/Icons";
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

type WorkspaceTab = "overview" | "documents" | "drafts" | "outward" | "activity";

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

  // Tab State
  const [activeTab, setActiveTab] = useState<WorkspaceTab>("overview");

  // Overflow Menu State
  const [showOverflow, setShowOverflow] = useState(false);
  const overflowRef = useRef<HTMLDivElement>(null);

  // Modals & Drawers
  const [showEditModal, setShowEditModal] = useState(false);
  const [showReclassifyModal, setShowReclassifyModal] = useState(false);
  const [showArchiveModal, setShowArchiveModal] = useState(false);
  const [showUploadModal, setShowUploadModal] = useState(false);
  const [showLinkDrawer, setShowLinkDrawer] = useState(false);

  // Edit Form State
  const [editTitle, setEditTitle] = useState("");
  const [editStatus, setEditStatus] = useState("");
  const [editRefNo, setEditRefNo] = useState("");
  const [editRemarks, setEditRemarks] = useState("");
  const [editKhasraRef, setEditKhasraRef] = useState("");
  const [editError, setEditError] = useState<string | null>(null);
  const [savingEdit, setSavingEdit] = useState(false);

  // Reclassify Form State
  const [reclassifyTargetId, setReclassifyTargetId] = useState("");
  const [reclassifyReason, setReclassifyReason] = useState("");
  const [reclassifyError, setReclassifyError] = useState<string | null>(null);
  const [savingReclassify, setSavingReclassify] = useState(false);

  // Archive Form State
  const [archiveReason, setArchiveReason] = useState("");
  const [archiveError, setArchiveError] = useState<string | null>(null);
  const [savingArchive, setSavingArchive] = useState(false);

  // Upload Form State
  const [uploadFile, setUploadFile] = useState<File | null>(null);
  const [uploadRole, setUploadRole] = useState("Application");
  const [uploadDisplayName, setUploadDisplayName] = useState("");
  const [uploading, setUploading] = useState(false);
  const [uploadError, setUploadError] = useState<string | null>(null);

  // Linking Form State
  const [linkingDocId, setLinkingDocId] = useState<string | null>(null);
  const [linkRole, setLinkRole] = useState("Other");
  const [linkError, setLinkError] = useState<string | null>(null);

  // Close overflow menu on outside click
  useEffect(() => {
    const handleClickOutside = (e: MouseEvent) => {
      if (overflowRef.current && !overflowRef.current.contains(e.target as Node)) {
        setShowOverflow(false);
      }
    };
    document.addEventListener("mousedown", handleClickOutside);
    return () => document.removeEventListener("mousedown", handleClickOutside);
  }, []);

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
          setEditStatus(mData.status || "Open");
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

  // Load Eligible Documents when drawer opened
  useEffect(() => {
    if (!showLinkDrawer) return;
    fetch(`/api/matters/${id}/eligible-documents?r=${refresh}`, { credentials: "include" })
      .then((res) => (res.ok ? res.json() : []))
      .then((data) => setEligibleDocs(data))
      .catch(() => setEligibleDocs([]));
  }, [id, showLinkDrawer, refresh]);

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
          status: editStatus.trim() || null,
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
      setShowUploadModal(false);
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

      setShowLinkDrawer(false);
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
  const canAssignWork = !isArchived && hasPermission("WorkItem.Create");
  const canEdit = !isArchived && hasPermission("Matter.Edit");
  const canManageDocs = !isArchived && hasPermission("Matter.Document.Manage");
  const canArchive = !isArchived && hasPermission("Matter.Archive");

  const recentEvents = matter.events ? matter.events.slice(0, 4) : [];

  return (
    <div className="matter-workspace-shell">
      {/* Top Identity Block */}
      <div className="matter-identity-card">
        <nav className="matter-breadcrumbs" aria-label="Breadcrumb">
          <Link to="/matters">Matters</Link>
          <span className="matter-breadcrumb-sep">/</span>
          <Link to={`/villages/${matter.villageId}`}>{matter.villageName}</Link>
          <span className="matter-breadcrumb-sep">/</span>
          <span style={{ color: "#0f172a", fontWeight: 600 }}>{matter.title}</span>
        </nav>

        <div className="matter-header-main">
          <div className="matter-header-title-block">
            <h1>{matter.title}</h1>
            <div className="matter-header-meta-row">
              <span className="matter-meta-item">
                <IconBuilding size={14} style={{ color: "#64748b" }} />
                <Link to={`/villages/${matter.villageId}`} style={{ color: "#0369a1", fontWeight: 600, textDecoration: "none" }}>
                  {matter.villageName}
                </Link>
              </span>
              <span>·</span>
              <span className="matter-meta-item">Type: <strong>{matter.matterType}</strong></span>
              <span>·</span>
              <span className="matter-meta-item">
                Ref: <strong style={{ fontFamily: "monospace" }}>{matter.referenceNumber || "—"}</strong>
              </span>
              <span>·</span>
              {matter.workstreamName ? (
                <span className="matter-badge-workstream" title={`Workstream Code: ${matter.workstreamCode}`}>
                  {matter.workstreamName} ({matter.workstreamCode})
                </span>
              ) : (
                <span className="matter-badge-unclassified" title="Legacy unclassified matter">
                  [Unclassified]
                </span>
              )}
              <span className={isArchived ? "matter-badge-archived" : "matter-badge-open"}>
                {matter.status}
              </span>
              <span className="matter-revision-tag">Rev {matter.revision}</span>
            </div>
          </div>
        </div>

        {/* Locked / Read-Only Banner for Archived Matters */}
        {isArchived && (
          <div className="matter-archived-banner">
            <IconLock size={16} />
            <span>
              This matter is <strong>Archived</strong> and in a terminal read-only state. No further edits, uploads, or document links are permitted.
            </span>
          </div>
        )}

        {/* Action Controls Toolbar */}
        {!isArchived && (
          <div className="matter-actions-toolbar">
            {canAssignWork && (
              <Link to={`/work/new?matterId=${matter.id}`} className="btn-action-primary">
                <IconPlus size={15} />
                + Assign Work
              </Link>
            )}

            {canEdit && (
              <button
                type="button"
                className="btn-action-secondary"
                onClick={() => {
                  setEditTitle(matter.title);
                  setEditStatus(matter.status || "Open");
                  setEditRefNo(matter.referenceNumber || "");
                  setEditRemarks(matter.remarks || "");
                  setEditKhasraRef(matter.khasraReferenceText || "");
                  setEditError(null);
                  setShowEditModal(true);
                }}
              >
                <IconEdit size={14} />
                Edit Details
              </button>
            )}

            {canManageDocs && (
              <>
                <button
                  type="button"
                  className="btn-action-secondary"
                  onClick={() => {
                    setUploadError(null);
                    setShowUploadModal(true);
                  }}
                >
                  <IconUpload size={14} />
                  + Upload Document
                </button>

                <button
                  type="button"
                  className="btn-action-secondary"
                  onClick={() => {
                    setLinkError(null);
                    setShowLinkDrawer(true);
                  }}
                >
                  <IconLink size={14} />
                  Link Existing
                </button>
              </>
            )}

            {/* Overflow Menu button for Administrative Actions */}
            {(canEdit || canArchive) && (
              <div className="matter-overflow-wrap" ref={overflowRef}>
                <button
                  type="button"
                  className="matter-overflow-btn"
                  onClick={() => setShowOverflow((v) => !v)}
                  title="More actions"
                >
                  <IconMoreVertical size={16} />
                </button>

                {showOverflow && (
                  <div className="matter-overflow-menu">
                    {canEdit && (
                      <button
                        type="button"
                        className="matter-overflow-item"
                        onClick={() => {
                          setShowOverflow(false);
                          setReclassifyError(null);
                          setShowReclassifyModal(true);
                        }}
                      >
                        <IconBuilding size={14} />
                        Reclassify Workstream
                      </button>
                    )}

                    {canArchive && (
                      <button
                        type="button"
                        className="matter-overflow-item danger"
                        onClick={() => {
                          setShowOverflow(false);
                          setArchiveError(null);
                          setShowArchiveModal(true);
                        }}
                      >
                        <IconArchive size={14} />
                        Archive Matter
                      </button>
                    )}
                  </div>
                )}
              </div>
            )}
          </div>
        )}
      </div>

      {/* Internal Workspace Tabs Bar */}
      <div>
        <nav className="matter-tabs-nav" aria-label="Matter Work Surfaces">
          <button
            type="button"
            className={`matter-tab-btn ${activeTab === "overview" ? "active" : ""}`}
            onClick={() => setActiveTab("overview")}
          >
            Overview
          </button>
          <button
            type="button"
            className={`matter-tab-btn ${activeTab === "documents" ? "active" : ""}`}
            onClick={() => setActiveTab("documents")}
          >
            Documents
            <span className="matter-tab-count">{documents.length}</span>
          </button>
          <button
            type="button"
            className={`matter-tab-btn ${activeTab === "drafts" ? "active" : ""}`}
            onClick={() => setActiveTab("drafts")}
          >
            Drafts
          </button>
          <button
            type="button"
            className={`matter-tab-btn ${activeTab === "outward" ? "active" : ""}`}
            onClick={() => setActiveTab("outward")}
          >
            Outward
          </button>
          <button
            type="button"
            className={`matter-tab-btn ${activeTab === "activity" ? "active" : ""}`}
            onClick={() => setActiveTab("activity")}
          >
            Activity
            <span className="matter-tab-count">{matter.events ? matter.events.length : 0}</span>
          </button>
        </nav>

        {/* Tab Body Surfaces */}
        <div className="matter-tab-body">
          {/* TAB 1: OVERVIEW */}
          {activeTab === "overview" && (
            <div className="matter-overview-grid">
              {/* Primary Facts Panel */}
              <div className="matter-card-panel">
                <h3>Primary Details & Facts</h3>
                <div className="matter-facts-grid">
                  <div className="matter-fact-item">
                    <span className="matter-fact-label">Reference Number</span>
                    <span className="matter-fact-value" style={{ fontFamily: "monospace" }}>
                      {matter.referenceNumber || "—"}
                    </span>
                  </div>

                  <div className="matter-fact-item">
                    <span className="matter-fact-label">Official Village</span>
                    <span className="matter-fact-value">
                      <Link to={`/villages/${matter.villageId}`} style={{ color: "#0284c7", textDecoration: "none", fontWeight: 600 }}>
                        {matter.villageName}
                      </Link>
                    </span>
                  </div>

                  <div className="matter-fact-item">
                    <span className="matter-fact-label">Matter Type</span>
                    <span className="matter-fact-value">{matter.matterType}</span>
                  </div>

                  <div className="matter-fact-item">
                    <span className="matter-fact-label">Workstream</span>
                    <span className="matter-fact-value">
                      {matter.workstreamName ? (
                        <span className="matter-badge-workstream">
                          {matter.workstreamName} ({matter.workstreamCode})
                        </span>
                      ) : (
                        <span className="matter-badge-unclassified">[Unclassified]</span>
                      )}
                    </span>
                  </div>

                  <div className="matter-fact-item">
                    <span className="matter-fact-label">Khasra Reference</span>
                    <span className="matter-fact-value">{matter.khasraReferenceText || "—"}</span>
                  </div>

                  <div className="matter-fact-item">
                    <span className="matter-fact-label">Status & Revision</span>
                    <span className="matter-fact-value">
                      {matter.status} (Rev {matter.revision})
                    </span>
                  </div>

                  <div className="matter-fact-item">
                    <span className="matter-fact-label">Created At</span>
                    <span className="matter-fact-value">{new Date(matter.createdAt).toLocaleString()}</span>
                  </div>

                  <div className="matter-fact-item">
                    <span className="matter-fact-label">Last Updated</span>
                    <span className="matter-fact-value">{new Date(matter.updatedAt).toLocaleString()}</span>
                  </div>
                </div>
              </div>

              {/* Remarks & Activity Preview Panel */}
              <div className="matter-card-panel">
                <h3>Remarks & Background Context</h3>
                <div className="matter-remarks-box">
                  {matter.remarks || <span style={{ color: "#94a3b8", italic: "true" }}>No administrative remarks recorded.</span>}
                </div>

                <div style={{ marginTop: 20 }}>
                  <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center", marginBottom: 10 }}>
                    <h4 style={{ margin: 0, fontSize: "13px", fontWeight: 700, color: "#0f172a" }}>
                      Recent Activity Preview
                    </h4>
                    {matter.events && matter.events.length > 0 && (
                      <button
                        type="button"
                        style={{ border: "none", background: "transparent", color: "#0284c7", fontSize: "12px", fontWeight: 600, cursor: "pointer" }}
                        onClick={() => setActiveTab("activity")}
                      >
                        View full activity ({matter.events.length}) →
                      </button>
                    )}
                  </div>

                  {recentEvents.length === 0 ? (
                    <div style={{ fontSize: "12px", color: "#94a3b8" }}>No activity recorded yet.</div>
                  ) : (
                    <div className="matter-timeline">
                      {recentEvents.map((ev) => (
                        <div key={ev.id} className="matter-timeline-item">
                          <div className="matter-timeline-dot" />
                          <div className="matter-timeline-content">
                            <div className="matter-timeline-header">{ev.actionName}</div>
                            <div className="matter-timeline-meta">
                              By {ev.actionByUserName} · {new Date(ev.actionAt).toLocaleDateString()}
                            </div>
                          </div>
                        </div>
                      ))}
                    </div>
                  )}
                </div>
              </div>
            </div>
          )}

          {/* TAB 2: DOCUMENTS */}
          {activeTab === "documents" && (
            <div>
              <div className="matter-docs-toolbar">
                <div>
                  <h3 style={{ margin: "0 0 2px 0", fontSize: "15px", fontWeight: 700, color: "#0f172a" }}>
                    Matter Documents Workspace
                  </h3>
                  <span className="hint">Direct evidentiary documents uploaded or linked to this matter.</span>
                </div>

                <div className="matter-docs-actions">
                  {selectedDocIds.length > 0 && (
                    <button
                      type="button"
                      className="btn-action-primary"
                      onClick={() => void handleExportZip()}
                    >
                      <IconDownload size={14} />
                      Export Selected ({selectedDocIds.length})
                    </button>
                  )}

                  {canManageDocs && (
                    <>
                      <button
                        type="button"
                        className="btn-action-secondary"
                        onClick={() => {
                          setUploadError(null);
                          setShowUploadModal(true);
                        }}
                      >
                        <IconUpload size={14} />
                        + Upload Document
                      </button>

                      <button
                        type="button"
                        className="btn-action-secondary"
                        onClick={() => {
                          setLinkError(null);
                          setShowLinkDrawer(true);
                        }}
                      >
                        <IconLink size={14} />
                        Link Existing
                      </button>
                    </>
                  )}
                </div>
              </div>

              {/* Selection Bar */}
              {selectedDocIds.length > 0 && (
                <div className="matter-selection-banner">
                  <span>{selectedDocIds.length} document(s) selected for export.</span>
                  <button
                    type="button"
                    style={{ border: "none", background: "transparent", cursor: "pointer", color: "#0369a1", fontSize: "12px", fontWeight: 600 }}
                    onClick={() => setSelectedDocIds([])}
                  >
                    Deselect all
                  </button>
                </div>
              )}

              {/* Documents Table */}
              {documents.length === 0 ? (
                <div className="state empty" style={{ padding: "36px" }}>
                  <IconFileText size={32} style={{ color: "#94a3b8", marginBottom: 8 }} />
                  <strong>No documents linked to this matter</strong>
                  <span>Upload a new document or link an existing record above.</span>
                </div>
              ) : (
                <table className="matter-directory-table">
                  <thead>
                    <tr>
                      <th style={{ width: 36 }}>
                        <input
                          type="checkbox"
                          checked={documents.length > 0 && selectedDocIds.length === documents.length}
                          onChange={(e) => {
                            if (e.target.checked) setSelectedDocIds(documents.map((d) => d.documentId));
                            else setSelectedDocIds([]);
                          }}
                        />
                      </th>
                      <th style={{ width: "20%" }}>Role</th>
                      <th style={{ width: "35%" }}>Name / File</th>
                      <th style={{ width: "15%" }}>Size</th>
                      <th style={{ width: "18%" }}>Uploaded</th>
                      <th style={{ width: "12%" }}>Action</th>
                    </tr>
                  </thead>
                  <tbody>
                    {documents.map((d) => (
                      <tr key={d.id} className="matter-table-row">
                        <td onClick={(e) => e.stopPropagation()}>
                          <input
                            type="checkbox"
                            checked={selectedDocIds.includes(d.documentId)}
                            onChange={() => toggleSelectDoc(d.documentId)}
                          />
                        </td>
                        <td>
                          <span className="matter-badge-workstream">
                            {d.documentRole || "Other"}
                          </span>
                        </td>
                        <td>
                          <div style={{ fontWeight: 600, color: "#0f172a" }}>
                            {d.displayName || d.originalFileName}
                          </div>
                          {d.displayName && (
                            <div style={{ fontSize: "11px", color: "#64748b" }}>{d.originalFileName}</div>
                          )}
                        </td>
                        <td>
                          <span style={{ fontSize: "12px", color: "#475569" }}>
                            {(d.fileSize / 1024).toFixed(1)} KB
                          </span>
                        </td>
                        <td>
                          <span style={{ fontSize: "12px", color: "#475569" }}>
                            {new Date(d.uploadedAt).toLocaleDateString()}
                          </span>
                        </td>
                        <td>
                          <a
                            href={`/api/matters/${id}/documents/${d.documentId}/content`}
                            target="_blank"
                            rel="noreferrer"
                            className="btn-action-secondary"
                            style={{ padding: "3px 8px", fontSize: "12px", textDecoration: "none" }}
                          >
                            View / Download
                          </a>
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              )}
            </div>
          )}

          {/* TAB 3: DRAFTS */}
          {activeTab === "drafts" && (
            <div>
              <MatterDrafts matterId={id} />
            </div>
          )}

          {/* TAB 4: OUTWARD */}
          {activeTab === "outward" && (
            <div>
              <MatterOutwardSection matterId={id} />
            </div>
          )}

          {/* TAB 5: ACTIVITY */}
          {activeTab === "activity" && (
            <div>
              <div style={{ marginBottom: 16 }}>
                <h3 style={{ margin: "0 0 2px 0", fontSize: "15px", fontWeight: 700, color: "#0f172a" }}>
                  Matter Audit & Event Timeline
                </h3>
                <span className="hint">Immutable chronological activity history for official compliance auditing.</span>
              </div>

              {!matter.events || matter.events.length === 0 ? (
                <div className="state empty" style={{ padding: "36px" }}>
                  <IconHistory size={32} style={{ color: "#94a3b8", marginBottom: 8 }} />
                  <strong>No audit events recorded</strong>
                </div>
              ) : (
                <div className="matter-timeline" style={{ marginTop: 12 }}>
                  {matter.events.map((ev) => (
                    <div key={ev.id} className="matter-timeline-item">
                      <div className="matter-timeline-dot" />
                      <div className="matter-timeline-content">
                        <div className="matter-timeline-header">
                          <span style={{ color: "#0284c7", fontWeight: 700, marginRight: 6 }}>
                            #{ev.sequenceNumber}
                          </span>
                          {ev.actionName}
                          {ev.workstreamName && (
                            <span style={{ fontWeight: 400, color: "#475569" }}>
                              {" "}· Workstream: <em>{ev.workstreamName}</em>
                            </span>
                          )}
                          {ev.targetWorkstreamName && (
                            <span style={{ fontWeight: 400, color: "#0284c7" }}>
                              {" "}➔ <em>{ev.targetWorkstreamName}</em>
                            </span>
                          )}
                        </div>
                        {ev.reason && <div className="matter-timeline-reason">&ldquo;{ev.reason}&rdquo;</div>}
                        <div className="matter-timeline-meta">
                          Action by <strong>{ev.actionByUserName}</strong> on {new Date(ev.actionAt).toLocaleString()}
                        </div>
                      </div>
                    </div>
                  ))}
                </div>
              )}
            </div>
          )}
        </div>
      </div>

      {/* Upload Document Modal */}
      {showUploadModal && (
        <div className="matter-modal-overlay" onClick={() => setShowUploadModal(false)}>
          <div className="matter-modal-card" onClick={(e) => e.stopPropagation()}>
            <div className="matter-modal-header">
              <h3 className="matter-modal-title">Upload Matter Document</h3>
              <button
                type="button"
                style={{ border: "none", background: "transparent", cursor: "pointer", color: "#64748b" }}
                onClick={() => setShowUploadModal(false)}
              >
                <IconClose size={18} />
              </button>
            </div>

            {uploadError && (
              <div style={{ background: "#fef2f2", border: "1px solid #fecaca", color: "#b91c1c", padding: "8px 12px", borderRadius: "6px", marginBottom: 14, fontSize: "13px" }}>
                {uploadError}
              </div>
            )}

            <form onSubmit={handleUploadDocument}>
              <div className="field-grid">
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
                    placeholder="e.g. High Court Order dt 12-05-2026"
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
              </div>

              <div className="matter-modal-actions">
                <button
                  type="button"
                  className="btn-action-secondary"
                  onClick={() => setShowUploadModal(false)}
                >
                  Cancel
                </button>
                <button
                  type="submit"
                  className="btn-action-primary"
                  disabled={uploading || !uploadFile}
                >
                  {uploading ? "Uploading..." : "Upload Document"}
                </button>
              </div>
            </form>
          </div>
        </div>
      )}

      {/* Link Existing Document Right Drawer */}
      {showLinkDrawer && (
        <div className="matter-drawer-overlay" onClick={() => setShowLinkDrawer(false)}>
          <div className="matter-right-drawer" onClick={(e) => e.stopPropagation()}>
            <div className="matter-drawer-header">
              <div>
                <h3 style={{ margin: 0, fontSize: "16px", fontWeight: 700, color: "#0f172a" }}>
                  Link Existing Document
                </h3>
                <span className="hint">Select eligible document from Award or Land Record families.</span>
              </div>
              <button
                type="button"
                style={{ border: "none", background: "transparent", cursor: "pointer", color: "#64748b" }}
                onClick={() => setShowLinkDrawer(false)}
              >
                <IconClose size={18} />
              </button>
            </div>

            {linkError && (
              <div style={{ background: "#fef2f2", border: "1px solid #fecaca", color: "#b91c1c", padding: "8px 12px", borderRadius: "6px", fontSize: "13px" }}>
                {linkError}
              </div>
            )}

            <div>
              <label className="form-label" style={{ fontWeight: 600, fontSize: "13px" }}>
                Target Role for Matter Link:
              </label>
              <select
                value={linkRole}
                onChange={(e) => setLinkRole(e.target.value)}
                className="matter-filter-select"
                style={{ width: "100%", marginTop: 4 }}
              >
                {["Application", "Court Order", "ADM Letter", "Joint Declaration", "Khatoni", "Demarcation", "Correspondence", "Other"].map((r) => (
                  <option key={r} value={r}>
                    {r}
                  </option>
                ))}
              </select>
            </div>

            <div style={{ flex: 1, overflowY: "auto", display: "flex", flexDirection: "column", gap: 8 }}>
              {eligibleDocs.length === 0 ? (
                <div className="state empty" style={{ padding: "24px" }}>
                  <span>No eligible documents available for linking.</span>
                </div>
              ) : (
                eligibleDocs.map((doc) => (
                  <div
                    key={doc.id}
                    style={{
                      display: "flex",
                      justifyContent: "space-between",
                      alignItems: "center",
                      padding: "10px 12px",
                      background: "#f8fafc",
                      border: "1px solid #e2e8f0",
                      borderRadius: "6px"
                    }}
                  >
                    <div style={{ minWidth: 0, flex: 1, paddingRight: 8 }}>
                      <div style={{ fontWeight: 600, fontSize: "13px", color: "#0f172a", overflow: "hidden", textOverflow: "ellipsis", whiteSpace: "nowrap" }}>
                        {doc.originalFileName}
                      </div>
                      <div style={{ fontSize: "11px", color: "#64748b" }}>
                        {doc.source} · {new Date(doc.uploadedAt).toLocaleDateString()}
                      </div>
                    </div>
                    <button
                      type="button"
                      className="btn-action-secondary"
                      style={{ padding: "4px 10px", fontSize: "12px" }}
                      disabled={linkingDocId === doc.id}
                      onClick={() => void handleLinkExisting(doc.id)}
                    >
                      {linkingDocId === doc.id ? "Linking..." : "Attach"}
                    </button>
                  </div>
                ))
              )}
            </div>
          </div>
        </div>
      )}

      {/* Edit Details Modal */}
      {showEditModal && (
        <div className="matter-modal-overlay" onClick={() => setShowEditModal(false)}>
          <div className="matter-modal-card" onClick={(e) => e.stopPropagation()}>
            <div className="matter-modal-header">
              <h3 className="matter-modal-title">Edit Matter Details</h3>
              <button
                type="button"
                style={{ border: "none", background: "transparent", cursor: "pointer", color: "#64748b" }}
                onClick={() => setShowEditModal(false)}
              >
                <IconClose size={18} />
              </button>
            </div>

            {editError && (
              <div style={{ background: "#fef2f2", border: "1px solid #fecaca", color: "#b91c1c", padding: "8px 12px", borderRadius: "6px", marginBottom: 14, fontSize: "13px" }}>
                {editError}
              </div>
            )}

            <form onSubmit={handleUpdateMetadata}>
              <div className="field-grid">
                <label style={{ gridColumn: "span 2" }}>
                  Matter Title *
                  <input
                    type="text"
                    value={editTitle}
                    onChange={(e) => setEditTitle(e.target.value)}
                    required
                  />
                </label>

                <label>
                  Status
                  <input
                    type="text"
                    value={editStatus}
                    onChange={(e) => setEditStatus(e.target.value)}
                    placeholder="e.g. Open, Pending, Disposed"
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

                <label style={{ gridColumn: "span 2" }}>
                  Khasra Reference
                  <input
                    type="text"
                    value={editKhasraRef}
                    onChange={(e) => setEditKhasraRef(e.target.value)}
                  />
                </label>

                <label style={{ gridColumn: "span 2" }}>
                  Remarks / Background Notes
                  <textarea
                    value={editRemarks}
                    onChange={(e) => setEditRemarks(e.target.value)}
                    rows={3}
                  />
                </label>
              </div>

              <div className="matter-modal-actions">
                <button
                  type="button"
                  className="btn-action-secondary"
                  onClick={() => setShowEditModal(false)}
                >
                  Cancel
                </button>
                <button
                  type="submit"
                  className="btn-action-primary"
                  disabled={savingEdit || !editTitle.trim()}
                >
                  {savingEdit ? "Saving..." : "Save Changes"}
                </button>
              </div>
            </form>
          </div>
        </div>
      )}

      {/* Reclassify Workstream Modal */}
      {showReclassifyModal && (
        <div className="matter-modal-overlay" onClick={() => setShowReclassifyModal(false)}>
          <div className="matter-modal-card" onClick={(e) => e.stopPropagation()}>
            <div className="matter-modal-header">
              <div>
                <h3 className="matter-modal-title">Reclassify Workstream</h3>
                <span className="hint">Alters official workstream classification and administrative ownership.</span>
              </div>
              <button
                type="button"
                style={{ border: "none", background: "transparent", cursor: "pointer", color: "#64748b" }}
                onClick={() => setShowReclassifyModal(false)}
              >
                <IconClose size={18} />
              </button>
            </div>

            {reclassifyError && (
              <div style={{ background: "#fef2f2", border: "1px solid #fecaca", color: "#b91c1c", padding: "8px 12px", borderRadius: "6px", marginBottom: 14, fontSize: "13px" }}>
                {reclassifyError}
              </div>
            )}

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
                  Mandatory Reclassification Reason *
                  <textarea
                    value={reclassifyReason}
                    onChange={(e) => setReclassifyReason(e.target.value)}
                    placeholder="Provide official administrative justification for reclassification..."
                    rows={3}
                    required
                  />
                </label>
              </div>

              <div className="matter-modal-actions">
                <button
                  type="button"
                  className="btn-action-secondary"
                  onClick={() => setShowReclassifyModal(false)}
                >
                  Cancel
                </button>
                <button
                  type="submit"
                  className="btn-action-primary"
                  disabled={savingReclassify || !reclassifyReason.trim()}
                >
                  {savingReclassify ? "Reclassifying..." : "Confirm Reclassification"}
                </button>
              </div>
            </form>
          </div>
        </div>
      )}

      {/* Archive Matter Modal */}
      {showArchiveModal && (
        <div className="matter-modal-overlay" onClick={() => setShowArchiveModal(false)}>
          <div className="matter-modal-card" onClick={(e) => e.stopPropagation()}>
            <div className="matter-modal-header">
              <div>
                <h3 className="matter-modal-title" style={{ color: "#991b1b" }}>
                  Archive Official Matter
                </h3>
                <span className="hint" style={{ color: "#b91c1c" }}>
                  Warning: Archiving a matter is a terminal state. Once archived, no further edits, uploads, or links are permitted.
                </span>
              </div>
              <button
                type="button"
                style={{ border: "none", background: "transparent", cursor: "pointer", color: "#64748b" }}
                onClick={() => setShowArchiveModal(false)}
              >
                <IconClose size={18} />
              </button>
            </div>

            {archiveError && (
              <div style={{ background: "#fef2f2", border: "1px solid #fecaca", color: "#b91c1c", padding: "8px 12px", borderRadius: "6px", marginBottom: 14, fontSize: "13px" }}>
                {archiveError}
              </div>
            )}

            <form onSubmit={handleArchive}>
              <div className="field-grid">
                <label style={{ gridColumn: "span 2" }}>
                  Mandatory Archival Reason *
                  <textarea
                    value={archiveReason}
                    onChange={(e) => setArchiveReason(e.target.value)}
                    placeholder="Provide official reason for archiving this matter..."
                    rows={3}
                    required
                  />
                </label>
              </div>

              <div className="matter-modal-actions">
                <button
                  type="button"
                  className="btn-action-secondary"
                  onClick={() => setShowArchiveModal(false)}
                >
                  Cancel
                </button>
                <button
                  type="submit"
                  className="btn-action-primary"
                  style={{ background: "#b91c1c", borderColor: "#b91c1c" }}
                  disabled={savingArchive || !archiveReason.trim()}
                >
                  {savingArchive ? "Archiving..." : "Confirm Archival"}
                </button>
              </div>
            </form>
          </div>
        </div>
      )}
    </div>
  );
};
