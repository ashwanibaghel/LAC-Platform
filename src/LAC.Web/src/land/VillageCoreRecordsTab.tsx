import React, { useState, useEffect } from "react";
import { Link } from "react-router-dom";
import { useAuth } from "../auth/AuthProvider";
import { IconPlus, IconChevronRight, IconClose, IconFileText, IconMoreVertical } from "../components/Icons";
import { CoreDocumentUploadModal } from "./CoreDocumentUploadModal";
import { AddAwardModal } from "./AddAwardModal";
import "./land.css";

const api = "/api";

function date(value?: string | null) {
  if (!value) return "—";
  const raw = String(value);
  const parsed = new Date(/^\d{4}-\d{2}-\d{2}$/.test(raw) ? raw + "T00:00:00" : raw);
  if (Number.isNaN(parsed.getTime())) return "—";
  return new Intl.DateTimeFormat("en-GB", { day: "2-digit", month: "short", year: "numeric" }).format(parsed);
}

function formatDateInput(value?: string | null) {
  if (!value) return "";
  const raw = String(value);
  if (/^\d{4}-\d{2}-\d{2}$/.test(raw)) return raw;
  const parsed = new Date(raw);
  if (Number.isNaN(parsed.getTime())) return "";
  return parsed.toISOString().split("T")[0];
}

const CORE_ROLES = [
  { key: "Award", label: "Award PDF" },
  { key: "NM", label: "NM / ENM Register" },
  { key: "StatementA", label: "Statement-A" },
  { key: "PossessionProceeding", label: "Possession Proceedings" },
] as const;

export interface CoreRecordRole {
  role: string;
  count: number;
  available: boolean;
}

export interface CoreRecordDocument {
  documentId: string;
  coreDocumentRole: string;
  originalFileName: string;
  uploadedAt: string;
  extractionJobId?: string | null;
  extractionStatus?: string | null;
  ingestionSessionId?: string | null;
  ingestionStatus?: string | null;
  totalCandidates?: number;
  pendingCandidates?: number;
}

export interface CoreRecordAward {
  id: string;
  awardNumber: string;
  awardDate?: string | null;
  awardType?: string | null;
  extractionJobId?: string | null;
  extractionStatus?: string | null;
  ingestionSessionId?: string | null;
  ingestionStatus?: string | null;
  totalCandidates?: number;
  pendingCandidates?: number;
  roles?: CoreRecordRole[];
  documents?: CoreRecordDocument[];
}

export interface VillageCoreRecordsTabProps {
  villageId: string;
  villageName?: string;
}

export const VillageCoreRecordsTab: React.FC<VillageCoreRecordsTabProps> = ({ villageId, villageName }) => {
  const { hasPermission } = useAuth();
  const canViewAward = hasPermission("Award.View");
  const canAddAward = hasPermission("Award.Create");
  const canEditAward = hasPermission("Award.Edit");
  const canUploadCore = hasPermission("Award.CoreDocument.Upload");

  const [refresh, setRefresh] = useState(0);
  const [records, setRecords] = useState<CoreRecordAward[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  // Active Open Menu State ('doc-awardId-role' or 'award-awardId')
  const [activeMenuId, setActiveMenuId] = useState<string | null>(null);

  // Modal States
  const [addAwardModalOpen, setAddAwardModalOpen] = useState(false);
  const [newAward, setNewAward] = useState({ awardNumber: "", awardDate: "", awardType: "" });

  const [editAwardModalOpen, setEditAwardModalOpen] = useState(false);
  const [editAward, setEditAward] = useState<{ id: string; awardNumber: string; awardDate: string; awardType: string } | null>(null);

  const [uploadModalOpen, setUploadModalOpen] = useState(false);
  const [uploadTarget, setUploadTarget] = useState<{ awardId: string; awardNumber: string; role: string; label: string } | null>(null);

  const [replaceModalOpen, setReplaceModalOpen] = useState(false);
  const [replaceTarget, setReplaceTarget] = useState<{ awardId: string; awardNumber: string; role: string; label: string; documentId: string; fileName?: string } | null>(null);

  const [removeModalOpen, setRemoveModalOpen] = useState(false);
  const [removeTarget, setRemoveTarget] = useState<{ awardId: string; awardNumber: string; role: string; label: string; documentId: string; fileName?: string } | null>(null);
  const [removeReason, setRemoveReason] = useState("");

  const [busy, setBusy] = useState(false);
  const [message, setMessage] = useState("");

  // Close context menu on outside click
  useEffect(() => {
    const handleOutsideClick = () => setActiveMenuId(null);
    window.addEventListener("click", handleOutsideClick);
    return () => window.removeEventListener("click", handleOutsideClick);
  }, []);

  // Fetch core records
  useEffect(() => {
    if (!villageId) return;
    let active = true;
    setLoading(true);
    setError(null);

    fetch(`${api}/villages/${villageId}/core-records?r=${refresh}`, { credentials: "include" })
      .then(async (r) => {
        if (!r.ok) {
          if (r.status === 403) throw new Error("Access denied: You do not have permission to view core records.");
          throw new Error("Could not load core records.");
        }
        return r.json() as Promise<CoreRecordAward[]>;
      })
      .then((d) => {
        if (active) {
          setRecords(d);
          setLoading(false);
        }
      })
      .catch((e: any) => {
        if (active) {
          setError(e?.message || "Core records unavailable.");
          setLoading(false);
        }
      });
    return () => {
      active = false;
    };
  }, [villageId, refresh]);

  // Handle Add Award
  const handleCreateAward = async (
    awardData: { awardNumber: string; awardDate: string; awardType: string },
    initialFile: File | null
  ) => {
    if (!awardData.awardNumber.trim()) {
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
          awardNumber: awardData.awardNumber.trim(),
          awardDate: awardData.awardDate || null,
          awardType: awardData.awardType || null,
          remarks: null,
        }),
        credentials: "include",
      });

      if (!res.ok) {
        const errJson = await res.json().catch(() => ({}));
        throw new Error(errJson.detail || errJson.message || "Could not add Award.");
      }

      const created = (await res.json()) as { id: string };

      // If initial Award PDF file was attached, upload it immediately
      if (initialFile && created?.id) {
        const formData = new FormData();
        formData.append("file", initialFile);

        const uploadRes = await fetch(
          `${api}/awards/${created.id}/core-documents?role=Award`,
          {
            method: "POST",
            body: formData,
            credentials: "include",
          }
        );

        if (!uploadRes.ok) {
          console.warn("Award created, but initial PDF upload failed.");
        }
      }

      setAddAwardModalOpen(false);
      setRefresh((x) => x + 1);
    } catch (e: any) {
      setMessage(e?.message || "Could not add Award.");
    } finally {
      setBusy(false);
    }
  };

  // Handle Edit Award Details
  const handleUpdateAward = async () => {
    if (!editAward || !editAward.awardNumber.trim()) {
      setMessage("Award number is required.");
      return;
    }
    try {
      setBusy(true);
      setMessage("");
      const res = await fetch(`${api}/awards/${editAward.id}`, {
        method: "PUT",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          awardNumber: editAward.awardNumber.trim(),
          awardDate: editAward.awardDate || null,
          awardType: editAward.awardType || null,
        }),
        credentials: "include",
      });

      if (!res.ok) {
        const errJson = await res.json().catch(() => ({}));
        throw new Error(errJson.detail || errJson.message || "Could not update Award details.");
      }

      setEditAwardModalOpen(false);
      setEditAward(null);
      setRefresh((x) => x + 1);
    } catch (e: any) {
      setMessage(e?.message || "Could not update Award details.");
    } finally {
      setBusy(false);
    }
  };

  // Handle Core Document Upload
  const handleUploadDocument = async (file: File) => {
    if (!uploadTarget) {
      setMessage("Target document context is missing.");
      return;
    }
    setBusy(true);
    setMessage("");

    try {
      const formData = new FormData();
      formData.append("file", file);

      const res = await fetch(
        `${api}/awards/${uploadTarget.awardId}/core-documents?role=${encodeURIComponent(uploadTarget.role)}`,
        {
          method: "POST",
          body: formData,
          credentials: "include",
        }
      );

      if (!res.ok) {
        const errJson = await res.json().catch(() => ({}));
        throw new Error(errJson.detail || errJson.message || "Failed to upload document.");
      }

      setUploadModalOpen(false);
      setUploadTarget(null);
      setRefresh((x) => x + 1);
    } catch (e: any) {
      setMessage(e?.message || "Failed to upload document.");
    } finally {
      setBusy(false);
    }
  };

  // Handle Replace Document
  const handleReplaceDocument = async (file: File, reason: string) => {
    if (!replaceTarget || !replaceTarget.documentId) {
      setMessage("Target document identifier is missing for replacement.");
      return;
    }
    if (!reason.trim()) {
      setMessage("A reason is required to replace an official core document.");
      return;
    }
    setBusy(true);
    setMessage("");

    try {
      const formData = new FormData();
      formData.append("file", file);

      const endpoint = `${api}/awards/${replaceTarget.awardId}/core-documents/${replaceTarget.documentId}?reason=${encodeURIComponent(reason.trim())}`;

      const res = await fetch(endpoint, {
        method: "PUT",
        body: formData,
        credentials: "include",
      });

      if (!res.ok) {
        const errJson = await res.json().catch(() => ({}));
        throw new Error(errJson.detail || errJson.message || "Failed to replace document.");
      }

      setReplaceModalOpen(false);
      setReplaceTarget(null);
      setRefresh((x) => x + 1);
    } catch (e: any) {
      setMessage(e?.message || "Failed to replace document.");
    } finally {
      setBusy(false);
    }
  };

  // Handle Remove Core Document
  const handleRemoveDocument = async () => {
    if (!removeTarget || !removeTarget.documentId) {
      setMessage("Target document identifier is missing for removal.");
      return;
    }
    if (!removeReason.trim()) {
      setMessage("A reason is required to remove a core document from official records.");
      return;
    }
    setBusy(true);
    setMessage("");

    try {
      const endpoint = `${api}/awards/${removeTarget.awardId}/core-documents/${removeTarget.documentId}?reason=${encodeURIComponent(removeReason.trim())}`;

      const res = await fetch(endpoint, {
        method: "DELETE",
        credentials: "include",
      });

      if (!res.ok) {
        const errJson = await res.json().catch(() => ({}));
        throw new Error(errJson.detail || errJson.message || "Failed to remove core document.");
      }

      setRemoveReason("");
      setRemoveModalOpen(false);
      setRemoveTarget(null);
      setRefresh((x) => x + 1);
    } catch (e: any) {
      setMessage(e?.message || "Failed to remove core document.");
    } finally {
      setBusy(false);
    }
  };

  const openUploadModal = (awardId: string, awardNumber: string, role: string) => {
    const roleLabel = CORE_ROLES.find((r) => r.key === role)?.label || role;
    setUploadTarget({ awardId, awardNumber, role, label: roleLabel });
    setMessage("");
    setUploadModalOpen(true);
  };

  const openReplaceModal = (awardId: string, awardNumber: string, role: string, documentId: string, fileName?: string) => {
    const roleLabel = CORE_ROLES.find((r) => r.key === role)?.label || role;
    setReplaceTarget({ awardId, awardNumber, role, label: roleLabel, documentId, fileName });
    setMessage("");
    setReplaceModalOpen(true);
  };

  const openRemoveModal = (awardId: string, awardNumber: string, role: string, documentId: string, fileName?: string) => {
    const roleLabel = CORE_ROLES.find((r) => r.key === role)?.label || role;
    setRemoveTarget({ awardId, awardNumber, role, label: roleLabel, documentId, fileName });
    setRemoveReason("");
    setMessage("");
    setRemoveModalOpen(true);
  };

  const openEditAwardModal = (award: CoreRecordAward) => {
    setEditAward({
      id: award.id,
      awardNumber: award.awardNumber,
      awardDate: formatDateInput(award.awardDate),
      awardType: award.awardType || "",
    });
    setMessage("");
    setEditAwardModalOpen(true);
  };

  const openDocumentContent = (documentId: string) => {
    window.open(`${api}/documents/${documentId}/content`, "_blank");
  };

  const triggerAnalyze = async (awardId: string, documentId?: string) => {
    try {
      setBusy(true);
      setMessage("");
      const endpoint = documentId
        ? `${api}/awards/${awardId}/documents/${documentId}/analyze?villageId=${villageId}`
        : `${api}/awards/${awardId}/extract?villageId=${villageId}`;
      const res = await fetch(endpoint, {
        method: "POST",
        credentials: "include",
      });
      if (!res.ok) {
        const err = await res.json().catch(() => ({}));
        throw new Error(err.detail || err.message || "Could not start document analysis.");
      }
      setMessage("Analysis started in background for Award PDF.");
      setRefresh((x) => x + 1);
    } catch (e: any) {
      setMessage(e?.message || "Could not start document analysis.");
    } finally {
      setBusy(false);
    }
  };

  if (loading) return <div className="state loading">Loading core records matrix…</div>;
  if (error) return <div className="state error">{error}</div>;

  return (
    <div className="village-core-records-tab">
      <div className="section-heading" style={{ marginBottom: "20px" }}>
        <div>
          <h2>Core Records Completeness Matrix</h2>
          <span>Authoritative foundational document availability across land acquisition awards.</span>
        </div>
        {canAddAward && (
          <button className="primary-button" onClick={() => setAddAwardModalOpen(true)}>
            <IconPlus size={15} /> Add Award
          </button>
        )}
      </div>

      <div className="core-matrix-wrap">
        <table className="core-matrix-table">
          <thead>
            <tr>
              <th scope="col" style={{ width: "22%" }}>Award Reference</th>
              <th scope="col" style={{ width: "12%" }}>Award Date</th>
              {CORE_ROLES.map((role) => (
                <th scope="col" key={role.key} style={{ width: "15%" }}>
                  {role.label}
                </th>
              ))}
              <th scope="col" style={{ width: "13%", textAlign: "right" }}>Action</th>
            </tr>
          </thead>
          <tbody>
            {records.length === 0 ? (
              <tr>
                <td colSpan={3 + CORE_ROLES.length} style={{ padding: "24px", textAlign: "center", color: "#64748b" }}>
                  No awards linked to this village yet.
                </td>
              </tr>
            ) : (
              records.map((award) => (
                <tr key={award.id}>
                  <td>
                    <div style={{ display: "flex", alignItems: "flex-start", justifyContent: "space-between" }}>
                      <div>
                        {canViewAward ? (
                          <Link to={`/awards/${award.id}`} className="entity-link" style={{ fontWeight: 700, fontSize: "14px" }}>
                            Award #{award.awardNumber}
                          </Link>
                        ) : (
                          <span style={{ fontWeight: 700, fontSize: "14px" }}>Award #{award.awardNumber}</span>
                        )}
                        {award.awardType && (
                          <div style={{ fontSize: "11.5px", color: "#64748b", marginTop: "2px" }}>
                            {award.awardType}
                          </div>
                        )}
                      </div>
                    </div>
                  </td>
                  <td>
                    <span style={{ fontSize: "13px", color: "#334155" }}>{date(award.awardDate)}</span>
                  </td>

                  {CORE_ROLES.map(({ key }) => {
                    const roleDocs = award.documents?.filter((d) => d.coreDocumentRole === key) || [];
                    const isAvailable = roleDocs.length > 0;
                    const menuId = `doc-${award.id}-${key}`;

                    if (!isAvailable) {
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
                      const jobStatus = doc.extractionStatus || (key === "Award" ? award.extractionStatus : null);
                      const sessionId = doc.ingestionSessionId || (key === "Award" ? award.ingestionSessionId : null);
                      const pending = doc.pendingCandidates ?? (key === "Award" ? award.pendingCandidates : 0);
                      const total = doc.totalCandidates ?? (key === "Award" ? award.totalCandidates : 0);
                      const isRunning = jobStatus && ["Queued", "Extracting", "Analyzing", "BuildingCandidates"].includes(jobStatus);

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

                              {/* Analysis Subtag Badges */}
                              {key === "StatementA" || key === "PossessionProceeding" ? (
                                <span className="doc-analysis-subtag neutral" title="Analysis module not available yet">
                                  Analysis module not available yet
                                </span>
                              ) : isRunning ? (
                                <span className="doc-analysis-subtag running" title="Extraction & analysis in progress">
                                  <span className="pulse-dot" /> Analyzing…
                                </span>
                              ) : sessionId ? (
                                pending > 0 ? (
                                  <Link
                                    to={`/awards/${award.id}/ingestion/${sessionId}`}
                                    className="doc-analysis-subtag review-ready"
                                    title="Click to review extracted findings"
                                    onClick={(e) => e.stopPropagation()}
                                  >
                                    {total > pending && total - pending > 0
                                      ? `Review in Progress (${total - pending}/${total})`
                                      : `Review Ready (${pending} findings)`} →
                                  </Link>
                                ) : (
                                  <span className="doc-analysis-subtag completed" title="All extracted findings confirmed">
                                    Completed ({total} verified)
                                  </span>
                                )
                              ) : key === "Award" ? (
                                <button
                                  type="button"
                                  className="doc-analysis-subtag trigger"
                                  onClick={(e) => {
                                    e.stopPropagation();
                                    triggerAnalyze(award.id, doc.documentId);
                                  }}
                                  title="Run OCR and document intelligence analysis"
                                >
                                  Analyze Document →
                                </button>
                              ) : null}
                            </div>

                            {/* Subtle ⋯ Document Action Menu Button */}
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

                            {/* Contextual Document Action Dropdown */}
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

                                {key === "Award" && (
                                  <>
                                    {sessionId && (
                                      <Link
                                        to={`/awards/${award.id}/ingestion/${sessionId}`}
                                        style={{
                                          width: "100%",
                                          textAlign: "left",
                                          padding: "8px 12px",
                                          background: "none",
                                          border: "none",
                                          fontSize: "12.5px",
                                          color: "#2563eb",
                                          cursor: "pointer",
                                          fontWeight: 600,
                                          display: "block",
                                          textDecoration: "none",
                                          borderTop: "1px solid #f1f5f9",
                                        }}
                                        onClick={() => setActiveMenuId(null)}
                                      >
                                        Review Findings
                                      </Link>
                                    )}
                                    {!isRunning && (
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
                                  </>
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
                                        openReplaceModal(award.id, award.awardNumber, key, doc.documentId, doc.originalFileName);
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
                                        openRemoveModal(award.id, award.awardNumber, key, doc.documentId, doc.originalFileName);
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
                                    <span style={{ fontSize: "12.5px", fontWeight: 600, color: "#1e293b", overflow: "hidden", textOverflow: "ellipsis", whiteSpace: "nowrap" }} title={doc.originalFileName}>
                                      {doc.originalFileName}
                                    </span>
                                    <div style={{ display: "flex", gap: "8px", alignItems: "center" }}>
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
                                              openReplaceModal(award.id, award.awardNumber, key, doc.documentId, doc.originalFileName);
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
                                              openRemoveModal(award.id, award.awardNumber, key, doc.documentId, doc.originalFileName);
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

                  <td style={{ textAlign: "right" }}>
                    <div style={{ display: "inline-flex", alignItems: "center", gap: "8px", position: "relative" }}>
                      {canViewAward ? (
                        <Link
                          to={`/awards/${award.id}`}
                          className="text-action"
                          style={{ display: "inline-flex", alignItems: "center", gap: "4px" }}
                        >
                          <span>Workspace</span>
                          <IconChevronRight size={14} />
                        </Link>
                      ) : (
                        <span style={{ fontSize: "12.5px", color: "#94a3b8" }}>Read-only</span>
                      )}

                      {/* Subtle ⋯ Award Action Menu Button */}
                      {(canEditAward || canViewAward) && (
                        <>
                          <button
                            type="button"
                            style={{
                              border: "none",
                              background: "transparent",
                              color: "#64748b",
                              cursor: "pointer",
                              padding: "4px",
                              borderRadius: "4px",
                              display: "inline-flex",
                              alignItems: "center",
                            }}
                            onClick={(e) => {
                              e.stopPropagation();
                              const menuId = `award-${award.id}`;
                              setActiveMenuId(activeMenuId === menuId ? null : menuId);
                            }}
                            title="Award Actions"
                          >
                            <IconMoreVertical size={15} />
                          </button>

                          {activeMenuId === `award-${award.id}` && (
                            <div
                              className="award-action-dropdown"
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
                                minWidth: "150px",
                                overflow: "hidden",
                                textAlign: "left",
                              }}
                            >
                              {canEditAward && (
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
                                    openEditAwardModal(award);
                                  }}
                                >
                                  Edit Award Details
                                </button>
                              )}
                              {canViewAward && (
                                <Link
                                  to={`/awards/${award.id}`}
                                  style={{
                                    display: "block",
                                    width: "100%",
                                    textAlign: "left",
                                    padding: "8px 12px",
                                    fontSize: "12.5px",
                                    color: "#1e293b",
                                    textDecoration: "none",
                                    fontWeight: 500,
                                    borderTop: canEditAward ? "1px solid #f1f5f9" : "none",
                                  }}
                                  onClick={() => setActiveMenuId(null)}
                                >
                                  Open Workspace
                                </Link>
                              )}
                            </div>
                          )}
                        </>
                      )}
                    </div>
                  </td>
                </tr>
              ))
            )}
          </tbody>
        </table>
      </div>

      {/* Add Award Modal */}
      <AddAwardModal
        isOpen={addAwardModalOpen}
        villageName={villageName}
        busy={busy}
        errorMessage={message}
        onClose={() => {
          setAddAwardModalOpen(false);
          setMessage("");
        }}
        onSubmit={handleCreateAward}
      />

      {/* Edit Award Details Modal */}
      {editAwardModalOpen && editAward && (
        <div className="upload-modal-overlay" onClick={() => setEditAwardModalOpen(false)}>
          <div className="upload-modal-container" onClick={(e) => e.stopPropagation()}>
            <div className="upload-modal-header">
              <div className="upload-modal-header-titles">
                <h3>Edit Award Details</h3>
                <p>Update official identification metadata for Award #{editAward.awardNumber}.</p>
              </div>
              <button type="button" className="upload-modal-close-btn" onClick={() => setEditAwardModalOpen(false)} title="Close dialog">
                <IconClose size={18} />
              </button>
            </div>

            <form
              onSubmit={(e) => {
                e.preventDefault();
                void handleUpdateAward();
              }}
              className="upload-modal-body"
            >
              {villageName && (
                <div className="upload-context-strip">
                  <div className="ctx-item">
                    <span className="ctx-lbl">Target Village</span>
                    <span className="ctx-val">{villageName}</span>
                  </div>
                  <div className="ctx-sep">•</div>
                  <div className="ctx-item">
                    <span className="ctx-lbl">Award</span>
                    <span className="ctx-val">#{editAward.awardNumber}</span>
                  </div>
                </div>
              )}

              {message && <div className="state error">{message}</div>}

              <div className="form-group">
                <label className="form-label required" style={{ fontWeight: 650, color: "#0f172a" }}>
                  Award Number
                </label>
                <input
                  type="text"
                  className="form-input"
                  placeholder="e.g. 15/2021-22"
                  value={editAward.awardNumber}
                  onChange={(e) => setEditAward({ ...editAward, awardNumber: e.target.value })}
                  required
                />
              </div>

              <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: "12px" }}>
                <div className="form-group">
                  <label className="form-label" style={{ fontWeight: 600, color: "#334155" }}>
                    Award Date
                  </label>
                  <input
                    type="date"
                    className="form-input"
                    value={editAward.awardDate}
                    onChange={(e) => setEditAward({ ...editAward, awardDate: e.target.value })}
                  />
                </div>

                <div className="form-group">
                  <label className="form-label" style={{ fontWeight: 600, color: "#334155" }}>
                    Award Type
                  </label>
                  <input
                    type="text"
                    className="form-input"
                    placeholder="e.g. General, Supplementary"
                    value={editAward.awardType}
                    onChange={(e) => setEditAward({ ...editAward, awardType: e.target.value })}
                  />
                </div>
              </div>

              <div className="modal-footer" style={{ borderTop: "1px solid #e2e8f0", paddingTop: "14px", marginTop: "12px" }}>
                <button type="button" className="secondary-button" onClick={() => setEditAwardModalOpen(false)} disabled={busy}>
                  Cancel
                </button>
                <button type="submit" className="primary-button" disabled={busy || !editAward.awardNumber.trim()}>
                  {busy ? "Saving Changes…" : "Save Changes"}
                </button>
              </div>
            </form>
          </div>
        </div>
      )}

      {/* Upload Core Document Modal */}
      {uploadTarget && (
        <CoreDocumentUploadModal
          mode="upload"
          isOpen={uploadModalOpen}
          villageName={villageName}
          awardNumber={uploadTarget.awardNumber}
          awardId={uploadTarget.awardId}
          role={uploadTarget.role}
          roleLabel={uploadTarget.label}
          busy={busy}
          errorMessage={message}
          onClose={() => {
            setUploadModalOpen(false);
            setUploadTarget(null);
            setMessage("");
          }}
          onSubmitUpload={handleUploadDocument}
          onSubmitReplace={() => {}}
        />
      )}

      {/* Replace Core Document Modal */}
      {replaceTarget && (
        <CoreDocumentUploadModal
          mode="replace"
          isOpen={replaceModalOpen}
          villageName={villageName}
          awardNumber={replaceTarget.awardNumber}
          awardId={replaceTarget.awardId}
          role={replaceTarget.role}
          roleLabel={replaceTarget.label}
          documentId={replaceTarget.documentId}
          currentFileName={replaceTarget.fileName}
          busy={busy}
          errorMessage={message}
          onClose={() => {
            setReplaceModalOpen(false);
            setReplaceTarget(null);
            setMessage("");
          }}
          onSubmitUpload={() => {}}
          onSubmitReplace={handleReplaceDocument}
        />
      )}

      {/* Remove Core Document Modal */}
      {removeModalOpen && removeTarget && (
        <div className="upload-modal-overlay" onClick={() => setRemoveModalOpen(false)}>
          <div className="upload-modal-container" onClick={(e) => e.stopPropagation()}>
            <div className="upload-modal-header">
              <div className="upload-modal-header-titles">
                <h3 style={{ color: "#dc2626" }}>Remove {removeTarget.label} from Core</h3>
                <p>Unlink official document role from Award #{removeTarget.awardNumber}.</p>
              </div>
              <button type="button" className="upload-modal-close-btn" onClick={() => setRemoveModalOpen(false)} title="Close dialog">
                <IconClose size={18} />
              </button>
            </div>

            <form
              onSubmit={(e) => {
                e.preventDefault();
                void handleRemoveDocument();
              }}
              className="upload-modal-body"
            >
              {villageName && (
                <div className="upload-context-strip">
                  <div className="ctx-item">
                    <span className="ctx-lbl">Target Village</span>
                    <span className="ctx-val">{villageName}</span>
                  </div>
                  <div className="ctx-sep">•</div>
                  <div className="ctx-item">
                    <span className="ctx-lbl">Award</span>
                    <span className="ctx-val">#{removeTarget.awardNumber}</span>
                  </div>
                  <div className="ctx-sep">•</div>
                  <div className="ctx-item">
                    <span className="ctx-lbl">Role</span>
                    <span className="ctx-val badge" style={{ background: "#fef2f2", color: "#dc2626", borderColor: "#fca5a5" }}>
                      {removeTarget.label}
                    </span>
                  </div>
                </div>
              )}

              <div style={{ background: "#fef2f2", border: "1px solid #fca5a5", borderRadius: "8px", padding: "12px 14px" }}>
                <p style={{ margin: "0 0 4px", fontSize: "13px", color: "#991b1b", fontWeight: 700 }}>
                  Unlink {removeTarget.label} {removeTarget.fileName ? `(${removeTarget.fileName})` : ""}
                </p>
                <p style={{ margin: 0, fontSize: "12px", color: "#b91c1c" }}>
                  This action unlinks the document role from official active records. Physical files and historical audit logs remain safely preserved.
                </p>
              </div>

              {message && <div className="state error">{message}</div>}

              <div className="form-group">
                <label className="form-label required" style={{ fontWeight: 650, color: "#0f172a" }}>
                  Reason for Removal
                </label>
                <input
                  type="text"
                  className="form-input"
                  placeholder="e.g. Erroneously assigned file category"
                  value={removeReason}
                  onChange={(e) => setRemoveReason(e.target.value)}
                  required
                />
              </div>

              <div className="modal-footer" style={{ borderTop: "1px solid #e2e8f0", paddingTop: "14px", marginTop: "12px" }}>
                <button type="button" className="secondary-button" onClick={() => setRemoveModalOpen(false)} disabled={busy}>
                  Cancel
                </button>
                <button
                  type="submit"
                  className="primary-button"
                  style={{ backgroundColor: "#dc2626", borderColor: "#dc2626" }}
                  disabled={busy || !removeReason.trim()}
                >
                  {busy ? "Removing…" : "Remove Core Document"}
                </button>
              </div>
            </form>
          </div>
        </div>
      )}
    </div>
  );
};
