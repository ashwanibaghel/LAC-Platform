import React, { useState, useEffect, useCallback } from "react";
import { Link } from "react-router-dom";
import { useAuth } from "../context/AuthContext";
import { IconFileText, IconMoreVertical, IconChevronLeft } from "../components/Icons";
import "./land.css";

const api = "/api";

export interface VillageCoreRecordsTabProps {
  villageId: string;
  villageName?: string;
}

export interface CoreDocumentItem {
  documentId: string;
  coreDocumentRole: string;
  originalFileName?: string | null;
  uploadedAt?: string | null;
  extractionJobId?: string | null;
  extractionStatus?: string | null;
  totalPages?: number | null;
  processedPages?: number;
  currentStage?: string | null;
  ingestionSessionId?: string | null;
  ingestionStatus?: string | null;
  totalCandidates?: number;
  unresolvedCandidates?: number;
  verifiedWaitingCommit?: number;
  committedCandidates?: number;
  skippedCandidates?: number;
  rejectedCandidates?: number;
  pendingCandidates?: number;
}

export interface VillageAwardRecord {
  id: string;
  awardNumber: string;
  awardDate?: string | null;
  awardType?: string | null;
  extractionJobId?: string | null;
  extractionStatus?: string | null;
  totalPages?: number | null;
  processedPages?: number;
  currentStage?: string | null;
  ingestionSessionId?: string | null;
  ingestionStatus?: string | null;
  totalCandidates?: number;
  unresolvedCandidates?: number;
  verifiedWaitingCommit?: number;
  committedCandidates?: number;
  skippedCandidates?: number;
  rejectedCandidates?: number;
  pendingCandidates?: number;
  roles: Array<{
    role: string;
    count: number;
    available: boolean;
  }>;
  documents: CoreDocumentItem[];
}

function renderDocumentStatusTag(
  doc: CoreDocumentItem | { documentId?: string; extractionStatus?: string | null; ingestionSessionId?: string | null; totalCandidates?: number; unresolvedCandidates?: number; verifiedWaitingCommit?: number; committedCandidates?: number; pendingCandidates?: number; totalPages?: number | null; processedPages?: number; currentStage?: string | null },
  award: VillageAwardRecord,
  key: string,
  canEditAward: boolean,
  analyzingDocId: string | null,
  triggerAnalyze: (awardId: string, documentId: string) => void,
  setActiveMenuId: (id: string | null) => void,
  isPopover: boolean = false
) {
  const docJobStatus = doc.extractionStatus || (key === "Award" ? award.extractionStatus : null);
  const sessionId = doc.ingestionSessionId || (key === "Award" ? award.ingestionSessionId : null);
  const isRunning = (analyzingDocId === doc.documentId || analyzingDocId === award.id) || (Boolean(docJobStatus) && ["Queued", "Extracting", "Analyzing", "BuildingCandidates"].includes(docJobStatus!));

  const total = doc.totalCandidates ?? (key === "Award" ? award.totalCandidates : 0);
  const unresolved = doc.unresolvedCandidates ?? doc.pendingCandidates ?? (key === "Award" ? (award.unresolvedCandidates ?? award.pendingCandidates) : 0);
  const verifiedWaiting = doc.verifiedWaitingCommit ?? (key === "Award" ? award.verifiedWaitingCommit : 0);
  const committed = doc.committedCandidates ?? (key === "Award" ? award.committedCandidates : 0);

  if (key === "StatementA" || key === "PossessionProceeding") {
    return (
      <span className="doc-analysis-subtag neutral" title="Analysis module not available yet" style={isPopover ? { fontSize: "11px" } : undefined}>
        Analysis module not available yet
      </span>
    );
  }

  if (isRunning) {
    const totalPages = doc.totalPages ?? award.totalPages;
    const processedPages = doc.processedPages ?? award.processedPages ?? 0;
    const isProgressKnown = totalPages && totalPages > 0;
    return (
      <span className="doc-analysis-subtag running" style={isPopover ? { fontSize: "11px", display: "inline-flex", width: "fit-content" } : undefined}>
        <span className="pulse-dot" />{" "}
        {isProgressKnown ? `${Math.min(100, Math.round((processedPages / totalPages) * 100))}% · ${processedPages}/${totalPages} pages` : "Analyzing…"}
      </span>
    );
  }

  if (sessionId) {
    let text = "";
    let cls = "doc-analysis-subtag review-ready";
    let titleText = "Click to open review workstation";

    if (unresolved > 0) {
      const resolved = total - unresolved;
      if (resolved > 0) {
        text = `Review · ${unresolved} remaining`;
        titleText = `${resolved} resolved · ${unresolved} remaining`;
      } else {
        text = `Review · ${unresolved} remaining`;
      }
    } else if (verifiedWaiting > 0) {
      text = `Review complete · ${total} reviewed`;
      titleText = `${verifiedWaiting} waiting to commit`;
      cls = "doc-analysis-subtag completed";
    } else if (committed > 0) {
      if (committed === total) {
        text = `${committed} committed`;
      } else {
        text = `${committed} committed · ${total - committed} final`;
      }
      cls = "doc-analysis-subtag completed";
    } else if (total > 0) {
      text = `Review complete · ${total} final`;
      cls = "doc-analysis-subtag completed";
    } else {
      text = `Review workspace`;
    }

    return (
      <Link
        to={`/awards/${award.id}/ingestion/${sessionId}`}
        className={cls}
        title={titleText}
        style={isPopover ? { fontSize: "11.5px", fontWeight: 600, textDecoration: "none" } : undefined}
        onClick={(e) => {
          e.stopPropagation();
          if (isPopover) setActiveMenuId(null);
        }}
      >
        {text} →
      </Link>
    );
  }

  if (key === "Award" && canEditAward) {
    return (
      <button
        type="button"
        className="doc-analysis-subtag trigger"
        style={isPopover ? { border: "none", background: "none", color: "#2563eb", cursor: "pointer", fontSize: "11.5px", fontWeight: 600, padding: 0, textAlign: "left" } : undefined}
        onClick={(e) => {
          e.stopPropagation();
          if (isPopover) setActiveMenuId(null);
          triggerAnalyze(award.id, doc.documentId!);
        }}
        title="Run OCR and document intelligence analysis"
      >
        Analyze Document →
      </button>
    );
  }

  return null;
}

export const VillageCoreRecordsTab: React.FC<VillageCoreRecordsTabProps> = ({ villageId, villageName }) => {
  const { hasPermission } = useAuth();
  const canViewAward = hasPermission("Award.View");
  const canAddAward = hasPermission("Award.Create");
  const canEditAward = hasPermission("Award.Edit");
  const canUploadCore = hasPermission("Award.CoreDocument.Upload");

  const [awards, setAwards] = useState<VillageAwardRecord[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [refresh, setRefresh] = useState(0);

  // Analysis Trigger & Active Menu State
  const [analyzingDocId, setAnalyzingDocId] = useState<string | null>(null);
  const [activeMenuId, setActiveMenuId] = useState<string | null>(null);

  // Modal States for Add / Replace / Remove Core Document
  const [uploadModal, setUploadModal] = useState<{
    isOpen: boolean;
    awardId: string;
    awardNumber: string;
    role: string;
    mode: "add" | "replace";
    targetDocumentId?: string;
    targetFileName?: string;
  }>({ isOpen: false, awardId: "", awardNumber: "", role: "Award", mode: "add" });

  const [removeModal, setRemoveModal] = useState<{
    isOpen: boolean;
    awardId: string;
    awardNumber: string;
    role: string;
    documentId: string;
    fileName?: string;
  }>({ isOpen: false, awardId: "", awardNumber: "", role: "Award", documentId: "" });

  const [selectedFile, setSelectedFile] = useState<File | null>(null);
  const [removalReason, setRemovalReason] = useState("");
  const [busy, setBusy] = useState(false);
  const [message, setMessage] = useState("");

  // Add Award Modal State
  const [addAwardModal, setAddAwardModal] = useState(false);
  const [newAwardNumber, setNewAwardNumber] = useState("");
  const [newAwardDate, setNewAwardDate] = useState("");
  const [newAwardType, setNewAwardType] = useState("General");
  const [newAwardRemarks, setNewAwardRemarks] = useState("");

  // Fetch Core Records Overview
  useEffect(() => {
    if (!villageId) return;
    let active = true;
    setLoading(true);
    setError(null);

    fetch(`${api}/villages/${villageId}/core-records?r=${refresh}`, { credentials: "include" })
      .then(async (res) => {
        if (!res.ok) throw new Error("Could not load core document records.");
        return res.json() as Promise<VillageAwardRecord[]>;
      })
      .then((data) => {
        if (active) {
          setAwards(data);
          setLoading(false);
        }
      })
      .catch((e: any) => {
        if (active) {
          setError(e?.message || "Failed to load core records.");
          setLoading(false);
        }
      });

    return () => { active = false; };
  }, [villageId, refresh]);

  // Polling for active document extractions
  useEffect(() => {
    const hasActiveExtraction = awards.some((a) =>
      ["Queued", "Extracting", "Analyzing", "BuildingCandidates"].includes(a.extractionStatus || "") ||
      a.documents.some((d) => ["Queued", "Extracting", "Analyzing", "BuildingCandidates"].includes(d.extractionStatus || ""))
    );
    if (!hasActiveExtraction) return;

    const timer = setInterval(() => {
      setRefresh((x) => x + 1);
    }, 2500);

    return () => clearInterval(timer);
  }, [awards]);

  // Close menus on outside click
  useEffect(() => {
    const handleOutsideClick = () => setActiveMenuId(null);
    window.addEventListener("click", handleOutsideClick);
    return () => window.removeEventListener("click", handleOutsideClick);
  }, []);

  const openUploadModal = (awardId: string, awardNumber: string, role: string) => {
    setSelectedFile(null);
    setMessage("");
    setUploadModal({ isOpen: true, awardId, awardNumber, role, mode: "add" });
  };

  const openReplaceModal = (awardId: string, awardNumber: string, role: string, documentId: string, fileName?: string) => {
    setSelectedFile(null);
    setMessage("");
    setUploadModal({ isOpen: true, awardId, awardNumber, role, mode: "replace", targetDocumentId: documentId, targetFileName: fileName });
  };

  const openRemoveModal = (awardId: string, awardNumber: string, role: string, documentId: string, fileName?: string) => {
    setRemovalReason("");
    setMessage("");
    setRemoveModal({ isOpen: true, awardId, awardNumber, role, documentId, fileName });
  };

  const handleUploadSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!selectedFile) {
      setMessage("Please select a PDF file to upload.");
      return;
    }
    try {
      setBusy(true);
      setMessage("");
      const formData = new FormData();
      formData.append("file", selectedFile);

      let endpoint = "";
      let method = "POST";

      if (uploadModal.mode === "add") {
        endpoint = `${api}/awards/${uploadModal.awardId}/core-documents?role=${encodeURIComponent(uploadModal.role)}`;
      } else {
        endpoint = `${api}/awards/${uploadModal.awardId}/core-documents/${uploadModal.targetDocumentId}`;
        method = "PUT";
      }

      const res = await fetch(endpoint, {
        method,
        body: formData,
        credentials: "include",
      });

      if (!res.ok) {
        const err = await res.json().catch(() => ({}));
        throw new Error(err.detail || err.message || "Failed to save core document.");
      }

      setUploadModal({ ...uploadModal, isOpen: false });
      setSelectedFile(null);
      setRefresh((x) => x + 1);
    } catch (err: any) {
      setMessage(err?.message || "Upload failed.");
    } finally {
      setBusy(false);
    }
  };

  const handleRemoveSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    try {
      setBusy(true);
      setMessage("");
      const endpoint = `${api}/awards/${removeModal.awardId}/core-documents/${removeModal.documentId}?reason=${encodeURIComponent(removalReason.trim())}`;
      const res = await fetch(endpoint, {
        method: "DELETE",
        credentials: "include",
      });

      if (!res.ok) {
        const err = await res.json().catch(() => ({}));
        throw new Error(err.detail || err.message || "Failed to remove core document.");
      }

      setRemoveModal({ ...removeModal, isOpen: false });
      setRemovalReason("");
      setRefresh((x) => x + 1);
    } catch (err: any) {
      setMessage(err?.message || "Removal failed.");
    } finally {
      setBusy(false);
    }
  };

  const handleAddAwardSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!newAwardNumber.trim()) {
      setMessage("Award number is required.");
      return;
    }
    try {
      setBusy(true);
      setMessage("");
      const res = await fetch(`${api}/villages/${villageId}/awards`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          awardNumber: newAwardNumber.trim(),
          awardDate: newAwardDate || null,
          awardType: newAwardType,
          remarks: newAwardRemarks.trim() || null,
        }),
        credentials: "include",
      });

      if (!res.ok) {
        const err = await res.json().catch(() => ({}));
        throw new Error(err.detail || err.message || "Could not add award.");
      }

      setAddAwardModal(false);
      setNewAwardNumber("");
      setNewAwardDate("");
      setNewAwardType("General");
      setNewAwardRemarks("");
      setRefresh((x) => x + 1);
    } catch (err: any) {
      setMessage(err?.message || "Adding award failed.");
    } finally {
      setBusy(false);
    }
  };

  const openDocumentContent = (documentId: string) => {
    window.open(`${api}/documents/${documentId}/content`, "_blank");
  };

  const triggerAnalyze = async (awardId: string, documentId: string) => {
    try {
      setAnalyzingDocId(documentId);
      setBusy(true);
      setMessage("");
      const endpoint = `${api}/awards/${awardId}/documents/${documentId}/analyze?villageId=${villageId}`;
      const res = await fetch(endpoint, {
        method: "POST",
        credentials: "include",
      });
      if (!res.ok) {
        if (res.status === 403) throw new Error("Access denied: You do not have permission to trigger document analysis.");
        const err = await res.json().catch(() => ({}));
        throw new Error(err.detail || err.message || "Could not start document analysis.");
      }
      setMessage("Document analysis started in background. The worker is parsing source pages.");
      setRefresh((x) => x + 1);
    } catch (e: any) {
      setMessage(e?.message || "Could not start document analysis.");
    } finally {
      setBusy(false);
      setAnalyzingDocId(null);
    }
  };

  // Find active extraction job for banner display
  const activeJobAward = awards.find(
    (a) =>
      ["Queued", "Extracting", "Analyzing", "BuildingCandidates"].includes(a.extractionStatus || "") ||
      a.documents.some((d) => ["Queued", "Extracting", "Analyzing", "BuildingCandidates"].includes(d.extractionStatus || ""))
  );

  const activeJobDoc = activeJobAward?.documents.find((d) =>
    ["Queued", "Extracting", "Analyzing", "BuildingCandidates"].includes(d.extractionStatus || "")
  );

  const isJobActive = Boolean(activeJobAward);
  const activeTotalPages = activeJobDoc?.totalPages ?? activeJobAward?.totalPages ?? 0;
  const activeProcessedPages = activeJobDoc?.processedPages ?? activeJobAward?.processedPages ?? 0;
  const activeStage = activeJobDoc?.currentStage || activeJobAward?.currentStage || "Analyzing document";
  const percentComplete = activeTotalPages > 0 ? Math.min(100, Math.round((activeProcessedPages / activeTotalPages) * 100)) : 0;
  const remainingPages = Math.max(0, activeTotalPages - activeProcessedPages);
  const estimatedSecondsLeft = remainingPages * 2;

  if (loading) return <div className="state loading">Loading Village Core Records…</div>;
  if (error) return <div className="state error">{error}</div>;

  return (
    <div className="village-core-records-tab">
      {/* Top Bar Actions */}
      <div className="tab-header-bar">
        <div>
          <h3>Village Workspace Core Records</h3>
          <p className="tab-subtext">
            Official linked records for {villageName || "Village"}. Upload, analyze, and manage Award PDFs, NM Registers, Statement-A, and Possession Proceedings.
          </p>
        </div>
        {canAddAward && (
          <button type="button" className="btn-primary" onClick={() => setAddAwardModal(true)}>
            + Add Award
          </button>
        )}
      </div>

      {/* Real-time Analysis Progress Card */}
      {isJobActive && (
        <div className="core-analysis-progress-card">
          <div className="progress-card-header">
            <div className="progress-title">
              <span className="pulse-dot" />
              <strong>Document Extraction in Progress</strong>
              <span className="stage-pill">{activeStage}</span>
            </div>
            <span className="percent-text">{percentComplete}% Complete</span>
          </div>

          <div className="progress-track">
            <div className="progress-fill" style={{ width: `${percentComplete}%` }} />
          </div>

          <div className="progress-meta">
            <span>
              Processed <strong>{activeProcessedPages}</strong> of <strong>{activeTotalPages || "?"}</strong> pages
            </span>
            {activeTotalPages > 0 && (
              <span>
                Estimated time remaining: <strong>~{estimatedSecondsLeft} seconds</strong> ({remainingPages} pages left)
              </span>
            )}
          </div>
        </div>
      )}

      {message && <div className="tab-alert-message">{message}</div>}

      {/* Main Records Matrix Table */}
      <div className="table-responsive">
        <table className="core-records-table">
          <thead>
            <tr>
              <th>Award Details</th>
              <th>Award PDF</th>
              <th>NM / ENM Register</th>
              <th>Statement-A</th>
              <th>Possession Proceedings</th>
            </tr>
          </thead>
          <tbody>
            {awards.length === 0 ? (
              <tr>
                <td colSpan={5} style={{ textAlign: "center", padding: "32px", color: "#64748b" }}>
                  No Awards linked to this village yet.
                </td>
              </tr>
            ) : (
              awards.map((award) => (
                <tr key={award.id}>
                  {/* Award Context */}
                  <td className="award-meta-cell">
                    <Link to={`/awards/${award.id}`} className="award-num-link">
                      Award #{award.awardNumber}
                    </Link>
                    <div className="award-sub-meta">
                      {award.awardDate ? new Date(award.awardDate).toLocaleDateString() : "No Date"} · {award.awardType || "General"}
                    </div>
                  </td>

                  {/* Core Document Roles */}
                  {(["Award", "NM", "StatementA", "PossessionProceeding"] as const).map((key) => {
                    const roleDocs = award.documents.filter((d) => d.coreDocumentRole === key);
                    const menuId = `${award.id}-${key}`;

                    // Missing Document Pill
                    if (roleDocs.length === 0) {
                      return (
                        <td key={key}>
                          <div className="core-doc-pill missing">
                            <span className="doc-missing-lbl">Missing</span>
                            {canUploadCore && (
                              <button
                                type="button"
                                className="core-add-btn"
                                onClick={() => openUploadModal(award.id, award.awardNumber, key)}
                                title={`Add ${key} document`}
                              >
                                + Add
                              </button>
                            )}
                          </div>
                        </td>
                      );
                    }

                    // Single Document for this Role
                    if (roleDocs.length === 1) {
                      const doc = roleDocs[0];
                      return (
                        <td key={key}>
                          <div className="core-doc-pill available" style={{ position: "relative" }}>
                            <IconFileText size={14} />
                            <div className="core-doc-info" style={{ flex: 1, minWidth: 0 }}>
                              <span className="doc-status-lbl">Available</span>
                              {doc.originalFileName && (
                                <span className="doc-filename" title={doc.originalFileName}>
                                  {doc.originalFileName}
                                </span>
                              )}

                              {/* Truthful Analysis Status Badge */}
                              {renderDocumentStatusTag(doc, award, key, canEditAward, analyzingDocId, triggerAnalyze, setActiveMenuId, false)}
                            </div>

                            {/* Context Action Menu Button */}
                            <button
                              type="button"
                              className="core-doc-menu-btn"
                              style={{
                                border: "none",
                                background: "transparent",
                                color: "#64748b",
                                cursor: "pointer",
                                padding: "2px 4px",
                                borderRadius: "4px",
                                display: "inline-flex",
                                alignItems: "center",
                              }}
                              onClick={(e) => {
                                e.stopPropagation();
                                setActiveMenuId(activeMenuId === menuId ? null : menuId);
                              }}
                              title="Document Actions"
                            >
                              <IconMoreVertical size={14} />
                            </button>

                            {/* Dropdown Menu */}
                            {activeMenuId === menuId && (
                              <div
                                className="core-doc-dropdown"
                                onClick={(e) => e.stopPropagation()}
                                style={{
                                  position: "absolute",
                                  top: "100%",
                                  right: 0,
                                  zIndex: 30,
                                  marginTop: "4px",
                                  background: "#ffffff",
                                  border: "1px solid #e2e8f0",
                                  borderRadius: "6px",
                                  boxShadow: "0 10px 15px -3px rgba(0,0,0,0.1), 0 4px 6px -4px rgba(0,0,0,0.1)",
                                  minWidth: "160px",
                                  overflow: "hidden",
                                }}
                              >
                                <button
                                  type="button"
                                  style={{
                                    width: "100%",
                                    textAlign: "left",
                                    padding: "8px 12px",
                                    background: "none",
                                    border: "none",
                                    fontSize: "12.5px",
                                    color: "#1e293b",
                                    cursor: "pointer",
                                    fontWeight: 500,
                                  }}
                                  onClick={() => {
                                    setActiveMenuId(null);
                                    openDocumentContent(doc.documentId);
                                  }}
                                >
                                  Open Document
                                </button>

                                {canEditAward && key === "Award" && (
                                  <button
                                    type="button"
                                    style={{
                                      width: "100%",
                                      textAlign: "left",
                                      padding: "8px 12px",
                                      background: "none",
                                      border: "none",
                                      fontSize: "12.5px",
                                      color: "#1e293b",
                                      cursor: "pointer",
                                      fontWeight: 500,
                                      borderTop: "1px solid #f1f5f9",
                                    }}
                                    onClick={() => {
                                      setActiveMenuId(null);
                                      triggerAnalyze(award.id, doc.documentId);
                                    }}
                                  >
                                    Analyze Document
                                  </button>
                                )}

                                {canUploadCore && (
                                  <>
                                    <button
                                      type="button"
                                      style={{
                                        width: "100%",
                                        textAlign: "left",
                                        padding: "8px 12px",
                                        background: "none",
                                        border: "none",
                                        fontSize: "12.5px",
                                        color: "#1e293b",
                                        cursor: "pointer",
                                        fontWeight: 500,
                                        borderTop: "1px solid #f1f5f9",
                                      }}
                                      onClick={() => {
                                        setActiveMenuId(null);
                                        openReplaceModal(award.id, award.awardNumber, key, doc.documentId, doc.originalFileName || undefined);
                                      }}
                                    >
                                      Replace Document
                                    </button>
                                    <button
                                      type="button"
                                      style={{
                                        width: "100%",
                                        textAlign: "left",
                                        padding: "8px 12px",
                                        background: "none",
                                        border: "none",
                                        fontSize: "12.5px",
                                        color: "#dc2626",
                                        cursor: "pointer",
                                        fontWeight: 500,
                                        borderTop: "1px solid #f1f5f9",
                                      }}
                                      onClick={() => {
                                        setActiveMenuId(null);
                                        openRemoveModal(award.id, award.awardNumber, key, doc.documentId, doc.originalFileName || undefined);
                                      }}
                                    >
                                      Remove from Core
                                    </button>
                                  </>
                                )}
                              </div>
                            )}
                          </div>
                        </td>
                      );
                    }

                    // Multiple Documents for this Role
                    return (
                      <td key={key}>
                        <div className="core-doc-pill available" style={{ position: "relative" }}>
                          <IconFileText size={14} />
                          <div className="core-doc-info" style={{ flex: 1, minWidth: 0 }}>
                            <span className="doc-status-lbl">Available · {roleDocs.length} files</span>
                          </div>

                          <button
                            type="button"
                            className="core-doc-menu-btn"
                            style={{
                              border: "none",
                              background: "transparent",
                              color: "#64748b",
                              cursor: "pointer",
                              padding: "2px 4px",
                              borderRadius: "4px",
                              display: "inline-flex",
                              alignItems: "center",
                            }}
                            onClick={(e) => {
                              e.stopPropagation();
                              setActiveMenuId(activeMenuId === menuId ? null : menuId);
                            }}
                            title="View Files"
                          >
                            <IconMoreVertical size={14} />
                          </button>

                          {/* Multi-File Popover Dropdown */}
                          {activeMenuId === menuId && (
                            <div
                              className="core-doc-dropdown"
                              onClick={(e) => e.stopPropagation()}
                              style={{
                                position: "absolute",
                                top: "100%",
                                right: 0,
                                zIndex: 30,
                                marginTop: "4px",
                                background: "#ffffff",
                                border: "1px solid #e2e8f0",
                                borderRadius: "6px",
                                boxShadow: "0 10px 15px -3px rgba(0,0,0,0.1), 0 4px 6px -4px rgba(0,0,0,0.1)",
                                minWidth: "260px",
                                overflow: "hidden",
                                padding: "4px 0",
                              }}
                            >
                              <div style={{ padding: "6px 12px", fontSize: "11px", fontWeight: 700, textTransform: "uppercase", color: "#64748b", borderBottom: "1px solid #f1f5f9" }}>
                                {roleDocs.length} Core Files Attached
                              </div>

                              <div style={{ maxHeight: "200px", overflowY: "auto" }}>
                                {roleDocs.map((doc, idx) => (
                                  <div
                                    key={doc.documentId}
                                    style={{
                                      padding: "8px 12px",
                                      borderBottom: idx < roleDocs.length - 1 ? "1px solid #f1f5f9" : "none",
                                      display: "flex",
                                      flexDirection: "column",
                                      gap: "4px",
                                    }}
                                  >
                                    <span style={{ fontSize: "12.5px", fontWeight: 600, color: "#1e293b", overflow: "hidden", textOverflow: "ellipsis", whiteSpace: "nowrap" }} title={doc.originalFileName || ""}>
                                      {doc.originalFileName}
                                    </span>

                                    {/* Truthful Analysis Status Badge for each file in multi-file role */}
                                    {renderDocumentStatusTag(doc, award, key, canEditAward, analyzingDocId, triggerAnalyze, setActiveMenuId, true)}

                                    <div style={{ display: "flex", gap: "8px", alignItems: "center", marginTop: "2px" }}>
                                      <button
                                        type="button"
                                        style={{ border: "none", background: "none", color: "#2563eb", cursor: "pointer", fontSize: "11.5px", fontWeight: 600, padding: 0 }}
                                        onClick={() => {
                                          setActiveMenuId(null);
                                          openDocumentContent(doc.documentId);
                                        }}
                                      >
                                        Open
                                      </button>
                                      {canUploadCore && (
                                        <>
                                          <span style={{ color: "#cbd5e1", fontSize: "11px" }}>•</span>
                                          <button
                                            type="button"
                                            style={{ border: "none", background: "none", color: "#475569", cursor: "pointer", fontSize: "11.5px", fontWeight: 500, padding: 0 }}
                                            onClick={() => {
                                              setActiveMenuId(null);
                                              openReplaceModal(award.id, award.awardNumber, key, doc.documentId, doc.originalFileName || undefined);
                                            }}
                                          >
                                            Replace
                                          </button>
                                          <span style={{ color: "#cbd5e1", fontSize: "11px" }}>•</span>
                                          <button
                                            type="button"
                                            style={{ border: "none", background: "none", color: "#dc2626", cursor: "pointer", fontSize: "11.5px", fontWeight: 500, padding: 0 }}
                                            onClick={() => {
                                              setActiveMenuId(null);
                                              openRemoveModal(award.id, award.awardNumber, key, doc.documentId, doc.originalFileName || undefined);
                                            }}
                                          >
                                            Remove
                                          </button>
                                        </>
                                      )}
                                    </div>
                                  </div>
                                ))}
                              </div>

                              {canUploadCore && (
                                <button
                                  type="button"
                                  style={{
                                    width: "100%",
                                    textAlign: "left",
                                    padding: "8px 12px",
                                    background: "#f8fafc",
                                    border: "none",
                                    borderTop: "1px solid #e2e8f0",
                                    fontSize: "12px",
                                    color: "#2563eb",
                                    cursor: "pointer",
                                    fontWeight: 600,
                                  }}
                                  onClick={() => {
                                    setActiveMenuId(null);
                                    openUploadModal(award.id, award.awardNumber, key);
                                  }}
                                >
                                  + Add Another File
                                </button>
                              )}
                            </div>
                          )}
                        </div>
                      </td>
                    );
                  })}
                </tr>
              ))
            )}
          </tbody>
        </table>
      </div>

      {/* Add / Replace Core Document Modal */}
      {uploadModal.isOpen && (
        <div className="modal-backdrop" style={{ position: "fixed", inset: 0, background: "rgba(15,23,42,0.6)", zIndex: 100, display: "flex", alignItems: "center", justifyContent: "center" }}>
          <div style={{ background: "#ffffff", borderRadius: "8px", width: "560px", maxWidth: "90vw", padding: "24px", boxShadow: "0 20px 25px -5px rgba(0,0,0,0.1)" }}>
            <h2 style={{ fontSize: "18px", fontWeight: 700, color: "#0f172a", marginBottom: "4px" }}>
              {uploadModal.mode === "add" ? `Add ${uploadModal.role} Core Document` : `Replace ${uploadModal.role} Core Document`}
            </h2>
            <p style={{ fontSize: "13px", color: "#64748b", marginBottom: "16px" }}>
              Award #{uploadModal.awardNumber} {villageName ? `· Village ${villageName}` : ""}
            </p>

            {uploadModal.mode === "replace" && (
              <div style={{ padding: "10px 12px", background: "#fef3c7", border: "1px solid #fde68a", borderRadius: "6px", fontSize: "12.5px", color: "#92400e", marginBottom: "16px" }}>
                Replacing: <strong>{uploadModal.targetFileName}</strong>
              </div>
            )}

            <form onSubmit={handleUploadSubmit}>
              <div style={{ marginBottom: "16px" }}>
                <label style={{ display: "block", fontSize: "12.5px", fontWeight: 600, color: "#334155", marginBottom: "6px" }}>
                  Select Document File (PDF):
                </label>
                <input
                  type="file"
                  accept="application/pdf"
                  onChange={(e) => setSelectedFile(e.target.files?.[0] || null)}
                  style={{ width: "100%", padding: "8px", border: "1px solid #cbd5e1", borderRadius: "6px", fontSize: "13px" }}
                />
              </div>

              {message && <div style={{ fontSize: "12.5px", color: "#dc2626", marginBottom: "12px" }}>{message}</div>}

              <div style={{ display: "flex", justifyContent: "flex-end", gap: "8px", marginTop: "20px" }}>
                <button
                  type="button"
                  className="btn-quiet"
                  disabled={busy}
                  onClick={() => setUploadModal({ ...uploadModal, isOpen: false })}
                >
                  Cancel
                </button>
                <button type="submit" className="btn-primary" disabled={busy || !selectedFile}>
                  {busy ? "Uploading…" : uploadModal.mode === "add" ? "Upload Document" : "Replace Document"}
                </button>
              </div>
            </form>
          </div>
        </div>
      )}

      {/* Remove Core Document Modal */}
      {removeModal.isOpen && (
        <div className="modal-backdrop" style={{ position: "fixed", inset: 0, background: "rgba(15,23,42,0.6)", zIndex: 100, display: "flex", alignItems: "center", justifyContent: "center" }}>
          <div style={{ background: "#ffffff", borderRadius: "8px", width: "520px", maxWidth: "90vw", padding: "24px", boxShadow: "0 20px 25px -5px rgba(0,0,0,0.1)" }}>
            <h2 style={{ fontSize: "18px", fontWeight: 700, color: "#dc2626", marginBottom: "4px" }}>
              Remove {removeModal.role} Core Document
            </h2>
            <p style={{ fontSize: "13px", color: "#64748b", marginBottom: "16px" }}>
              Award #{removeModal.awardNumber} {removeModal.fileName ? `· ${removeModal.fileName}` : ""}
            </p>

            <form onSubmit={handleRemoveSubmit}>
              <div style={{ marginBottom: "16px" }}>
                <label style={{ display: "block", fontSize: "12.5px", fontWeight: 600, color: "#334155", marginBottom: "6px" }}>
                  Reason for Removal (Required):
                </label>
                <textarea
                  rows={3}
                  value={removalReason}
                  onChange={(e) => setRemovalReason(e.target.value)}
                  placeholder="State why this document is being removed from core records…"
                  style={{ width: "100%", padding: "8px 12px", border: "1px solid #cbd5e1", borderRadius: "6px", fontSize: "13px" }}
                />
              </div>

              {message && <div style={{ fontSize: "12.5px", color: "#dc2626", marginBottom: "12px" }}>{message}</div>}

              <div style={{ display: "flex", justifyContent: "flex-end", gap: "8px", marginTop: "20px" }}>
                <button
                  type="button"
                  className="btn-quiet"
                  disabled={busy}
                  onClick={() => setRemoveModal({ ...removeModal, isOpen: false })}
                >
                  Cancel
                </button>
                <button type="submit" className="btn-danger" disabled={busy || !removalReason.trim()}>
                  {busy ? "Removing…" : "Confirm Removal"}
                </button>
              </div>
            </form>
          </div>
        </div>
      )}

      {/* Add Award Modal */}
      {addAwardModal && (
        <div className="modal-backdrop" style={{ position: "fixed", inset: 0, background: "rgba(15,23,42,0.6)", zIndex: 100, display: "flex", alignItems: "center", justifyContent: "center" }}>
          <div style={{ background: "#ffffff", borderRadius: "8px", width: "520px", maxWidth: "90vw", padding: "24px", boxShadow: "0 20px 25px -5px rgba(0,0,0,0.1)" }}>
            <h2 style={{ fontSize: "18px", fontWeight: 700, color: "#0f172a", marginBottom: "4px" }}>
              Add New Award to {villageName || "Village"}
            </h2>
            <p style={{ fontSize: "13px", color: "#64748b", marginBottom: "16px" }}>
              Create an Award record to link core documents and survey numbers.
            </p>

            <form onSubmit={handleAddAwardSubmit}>
              <div style={{ marginBottom: "12px" }}>
                <label style={{ display: "block", fontSize: "12.5px", fontWeight: 600, color: "#334155", marginBottom: "4px" }}>
                  Award Number (Required):
                </label>
                <input
                  type="text"
                  value={newAwardNumber}
                  onChange={(e) => setNewAwardNumber(e.target.value)}
                  placeholder="e.g. 01/2024-LAC"
                  style={{ width: "100%", padding: "8px 12px", border: "1px solid #cbd5e1", borderRadius: "6px", fontSize: "13px" }}
                />
              </div>

              <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: "12px", marginBottom: "12px" }}>
                <div>
                  <label style={{ display: "block", fontSize: "12.5px", fontWeight: 600, color: "#334155", marginBottom: "4px" }}>
                    Award Date:
                  </label>
                  <input
                    type="date"
                    value={newAwardDate}
                    onChange={(e) => setNewAwardDate(e.target.value)}
                    style={{ width: "100%", padding: "8px 12px", border: "1px solid #cbd5e1", borderRadius: "6px", fontSize: "13px" }}
                  />
                </div>

                <div>
                  <label style={{ display: "block", fontSize: "12.5px", fontWeight: 600, color: "#334155", marginBottom: "4px" }}>
                    Award Type:
                  </label>
                  <select
                    value={newAwardType}
                    onChange={(e) => setNewAwardType(e.target.value)}
                    style={{ width: "100%", padding: "8px 12px", border: "1px solid #cbd5e1", borderRadius: "6px", fontSize: "13px" }}
                  >
                    <option value="General">General</option>
                    <option value="Consent">Consent</option>
                    <option value="Supplementary">Supplementary</option>
                  </select>
                </div>
              </div>

              <div style={{ marginBottom: "16px" }}>
                <label style={{ display: "block", fontSize: "12.5px", fontWeight: 600, color: "#334155", marginBottom: "4px" }}>
                  Remarks:
                </label>
                <textarea
                  rows={2}
                  value={newAwardRemarks}
                  onChange={(e) => setNewAwardRemarks(e.target.value)}
                  placeholder="Optional notes or remarks…"
                  style={{ width: "100%", padding: "8px 12px", border: "1px solid #cbd5e1", borderRadius: "6px", fontSize: "13px" }}
                />
              </div>

              {message && <div style={{ fontSize: "12.5px", color: "#dc2626", marginBottom: "12px" }}>{message}</div>}

              <div style={{ display: "flex", justifyContent: "flex-end", gap: "8px", marginTop: "20px" }}>
                <button
                  type="button"
                  className="btn-quiet"
                  disabled={busy}
                  onClick={() => setAddAwardModal(false)}
                >
                  Cancel
                </button>
                <button type="submit" className="btn-primary" disabled={busy || !newAwardNumber.trim()}>
                  {busy ? "Creating…" : "Create Award"}
                </button>
              </div>
            </form>
          </div>
        </div>
      )}
    </div>
  );
};
