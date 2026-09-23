import React, { useState, useEffect } from "react";
import { Link } from "react-router-dom";
import { useAuth } from "../auth/AuthProvider";
import { IconPlus, IconChevronRight, IconClose, IconFileText, IconMoreVertical } from "../components/Icons";
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
}

export interface CoreRecordAward {
  id: string;
  awardNumber: string;
  awardDate?: string | null;
  awardType?: string | null;
  roles?: CoreRecordRole[];
  documents?: CoreRecordDocument[];
}

export interface VillageCoreRecordsTabProps {
  villageId: string;
}

export const VillageCoreRecordsTab: React.FC<VillageCoreRecordsTabProps> = ({ villageId }) => {
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
  const [selectedFile, setSelectedFile] = useState<File | null>(null);

  const [replaceModalOpen, setReplaceModalOpen] = useState(false);
  const [replaceTarget, setReplaceTarget] = useState<{ awardId: string; awardNumber: string; role: string; label: string; documentId?: string } | null>(null);
  const [replaceFile, setReplaceFile] = useState<File | null>(null);
  const [replaceReason, setReplaceReason] = useState("");

  const [removeModalOpen, setRemoveModalOpen] = useState(false);
  const [removeTarget, setRemoveTarget] = useState<{ awardId: string; awardNumber: string; role: string; label: string; documentId?: string } | null>(null);
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
  const handleCreateAward = async () => {
    if (!newAward.awardNumber.trim()) {
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
          awardNumber: newAward.awardNumber.trim(),
          awardDate: newAward.awardDate || null,
          awardType: newAward.awardType || null,
          remarks: null,
        }),
        credentials: "include",
      });

      if (!res.ok) {
        const errJson = await res.json().catch(() => ({}));
        throw new Error(errJson.detail || errJson.message || "Could not add Award.");
      }

      setNewAward({ awardNumber: "", awardDate: "", awardType: "" });
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
  const handleUploadDocument = async () => {
    if (!uploadTarget || !selectedFile) {
      setMessage("Please select a file to upload.");
      return;
    }
    setBusy(true);
    setMessage("");

    try {
      const formData = new FormData();
      formData.append("file", selectedFile);

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

      setSelectedFile(null);
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
  const handleReplaceDocument = async () => {
    if (!replaceTarget || !replaceFile) {
      setMessage("Please select a replacement PDF file.");
      return;
    }
    setBusy(true);
    setMessage("");

    try {
      const formData = new FormData();
      formData.append("file", replaceFile);

      const queryParams = new URLSearchParams({
        role: replaceTarget.role,
        ...(replaceReason.trim() ? { reason: replaceReason.trim() } : {}),
      });

      const res = await fetch(
        `${api}/awards/${replaceTarget.awardId}/core-documents?${queryParams.toString()}`,
        {
          method: "PUT",
          body: formData,
          credentials: "include",
        }
      );

      if (!res.ok) {
        const errJson = await res.json().catch(() => ({}));
        throw new Error(errJson.detail || errJson.message || "Failed to replace document.");
      }

      setReplaceFile(null);
      setReplaceReason("");
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
    if (!removeTarget) return;
    if (!removeReason.trim()) {
      setMessage("A reason is required to remove a core document from official records.");
      return;
    }
    setBusy(true);
    setMessage("");

    try {
      const queryParams = new URLSearchParams({
        role: removeTarget.role,
        reason: removeReason.trim(),
      });

      const res = await fetch(
        `${api}/awards/${removeTarget.awardId}/core-documents?${queryParams.toString()}`,
        {
          method: "DELETE",
          credentials: "include",
        }
      );

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
    setSelectedFile(null);
    setMessage("");
    setUploadModalOpen(true);
  };

  const openReplaceModal = (awardId: string, awardNumber: string, role: string, documentId?: string) => {
    const roleLabel = CORE_ROLES.find((r) => r.key === role)?.label || role;
    setReplaceTarget({ awardId, awardNumber, role, label: roleLabel, documentId });
    setReplaceFile(null);
    setReplaceReason("");
    setMessage("");
    setReplaceModalOpen(true);
  };

  const openRemoveModal = (awardId: string, awardNumber: string, role: string, documentId?: string) => {
    const roleLabel = CORE_ROLES.find((r) => r.key === role)?.label || role;
    setRemoveTarget({ awardId, awardNumber, role, label: roleLabel, documentId });
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

  const openDocumentFile = (documentId: string) => {
    window.open(`${api}/documents/${documentId}/file`, "_blank");
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
                    const roleInfo = award.roles?.find((r) => r.role === key);
                    const doc = award.documents?.find((d) => d.coreDocumentRole === key);
                    const isAvailable = roleInfo?.available ?? false;
                    const menuId = `doc-${award.id}-${key}`;

                    return (
                      <td key={key}>
                        {isAvailable ? (
                          <div className="core-doc-pill available" style={{ position: "relative" }}>
                            <IconFileText size={14} />
                            <div className="core-doc-info" style={{ flex: 1, minWidth: 0 }}>
                              <span className="doc-status-lbl">
                                Available {roleInfo && roleInfo.count > 1 ? `(${roleInfo.count})` : ""}
                              </span>
                              {doc?.originalFileName && (
                                <span className="doc-filename" title={doc.originalFileName}>
                                  {doc.originalFileName}
                                </span>
                              )}
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
                                  minWidth: "150px",
                                  overflow: "hidden",
                                }}
                              >
                                {doc?.documentId && (
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
                                      openDocumentFile(doc.documentId);
                                    }}
                                  >
                                    Open Document
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
                                        openReplaceModal(award.id, award.awardNumber, key, doc?.documentId);
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
                                        openRemoveModal(award.id, award.awardNumber, key, doc?.documentId);
                                      }}
                                    >
                                      Remove from Core
                                    </button>
                                  </>
                                )}
                              </div>
                            )}
                          </div>
                        ) : (
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
                        )}
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
      {addAwardModalOpen && (
        <div className="modal-overlay" onClick={() => setAddAwardModalOpen(false)}>
          <div className="modal-container" onClick={(e) => e.stopPropagation()} style={{ maxWidth: "500px" }}>
            <div className="modal-header">
              <h3>Add Award to Village</h3>
              <button className="icon-button" onClick={() => setAddAwardModalOpen(false)}>
                <IconClose size={18} />
              </button>
            </div>

            <div className="modal-body" style={{ display: "flex", flexDirection: "column", gap: "16px" }}>
              {message && <div className="state error">{message}</div>}

              <div className="form-group">
                <label className="form-label required">Award Number</label>
                <input
                  type="text"
                  className="form-input"
                  placeholder="e.g. 15/2021-22"
                  value={newAward.awardNumber}
                  onChange={(e) => setNewAward({ ...newAward, awardNumber: e.target.value })}
                  required
                />
              </div>

              <div className="form-group">
                <label className="form-label">Award Date</label>
                <input
                  type="date"
                  className="form-input"
                  value={newAward.awardDate}
                  onChange={(e) => setNewAward({ ...newAward, awardDate: e.target.value })}
                />
              </div>

              <div className="form-group">
                <label className="form-label">Award Type</label>
                <input
                  type="text"
                  className="form-input"
                  placeholder="e.g. General, Supplementary"
                  value={newAward.awardType}
                  onChange={(e) => setNewAward({ ...newAward, awardType: e.target.value })}
                />
              </div>
            </div>

            <div className="modal-footer">
              <button type="button" className="secondary-button" onClick={() => setAddAwardModalOpen(false)}>
                Cancel
              </button>
              <button
                type="button"
                className="primary-button"
                disabled={busy || !newAward.awardNumber.trim()}
                onClick={() => void handleCreateAward()}
              >
                {busy ? "Adding…" : "Add Award"}
              </button>
            </div>
          </div>
        </div>
      )}

      {/* Edit Award Details Modal */}
      {editAwardModalOpen && editAward && (
        <div className="modal-overlay" onClick={() => setEditAwardModalOpen(false)}>
          <div className="modal-container" onClick={(e) => e.stopPropagation()} style={{ maxWidth: "500px" }}>
            <div className="modal-header">
              <h3>Edit Award Details</h3>
              <button className="icon-button" onClick={() => setEditAwardModalOpen(false)}>
                <IconClose size={18} />
              </button>
            </div>

            <div className="modal-body" style={{ display: "flex", flexDirection: "column", gap: "16px" }}>
              {message && <div className="state error">{message}</div>}

              <div className="form-group">
                <label className="form-label required">Award Number</label>
                <input
                  type="text"
                  className="form-input"
                  placeholder="e.g. 15/2021-22"
                  value={editAward.awardNumber}
                  onChange={(e) => setEditAward({ ...editAward, awardNumber: e.target.value })}
                  required
                />
              </div>

              <div className="form-group">
                <label className="form-label">Award Date</label>
                <input
                  type="date"
                  className="form-input"
                  value={editAward.awardDate}
                  onChange={(e) => setEditAward({ ...editAward, awardDate: e.target.value })}
                />
              </div>

              <div className="form-group">
                <label className="form-label">Award Type</label>
                <input
                  type="text"
                  className="form-input"
                  placeholder="e.g. General, Supplementary"
                  value={editAward.awardType}
                  onChange={(e) => setEditAward({ ...editAward, awardType: e.target.value })}
                />
              </div>
            </div>

            <div className="modal-footer">
              <button type="button" className="secondary-button" onClick={() => setEditAwardModalOpen(false)}>
                Cancel
              </button>
              <button
                type="button"
                className="primary-button"
                disabled={busy || !editAward.awardNumber.trim()}
                onClick={() => void handleUpdateAward()}
              >
                {busy ? "Saving…" : "Save Changes"}
              </button>
            </div>
          </div>
        </div>
      )}

      {/* Upload Core Document Modal */}
      {uploadModalOpen && uploadTarget && (
        <div className="modal-overlay" onClick={() => setUploadModalOpen(false)}>
          <div className="modal-container" onClick={(e) => e.stopPropagation()} style={{ maxWidth: "500px" }}>
            <div className="modal-header">
              <h3>Upload {uploadTarget.label}</h3>
              <button className="icon-button" onClick={() => setUploadModalOpen(false)}>
                <IconClose size={18} />
              </button>
            </div>

            <div className="modal-body" style={{ display: "flex", flexDirection: "column", gap: "16px" }}>
              <p style={{ margin: 0, fontSize: "14px", color: "#475569" }}>
                Target: Award #{uploadTarget.awardNumber} ({uploadTarget.label})
              </p>

              {message && <div className="state error">{message}</div>}

              <div className="form-group">
                <label className="form-label required">Select PDF File</label>
                <input
                  type="file"
                  accept=".pdf"
                  className="form-input"
                  onChange={(e) => setSelectedFile(e.target.files?.[0] || null)}
                />
              </div>
            </div>

            <div className="modal-footer">
              <button type="button" className="secondary-button" onClick={() => setUploadModalOpen(false)}>
                Cancel
              </button>
              <button
                type="button"
                className="primary-button"
                disabled={busy || !selectedFile}
                onClick={() => void handleUploadDocument()}
              >
                {busy ? "Uploading…" : "Upload Document"}
              </button>
            </div>
          </div>
        </div>
      )}

      {/* Replace Core Document Modal */}
      {replaceModalOpen && replaceTarget && (
        <div className="modal-overlay" onClick={() => setReplaceModalOpen(false)}>
          <div className="modal-container" onClick={(e) => e.stopPropagation()} style={{ maxWidth: "500px" }}>
            <div className="modal-header">
              <h3>Replace {replaceTarget.label}</h3>
              <button className="icon-button" onClick={() => setReplaceModalOpen(false)}>
                <IconClose size={18} />
              </button>
            </div>

            <div className="modal-body" style={{ display: "flex", flexDirection: "column", gap: "16px" }}>
              <p style={{ margin: 0, fontSize: "14px", color: "#475569" }}>
                Target: Award #{replaceTarget.awardNumber} ({replaceTarget.label})
              </p>
              <p style={{ margin: 0, fontSize: "12.5px", color: "#64748b" }}>
                Replacing this document preserves historical evidence. The current document will remain linked historically in official record archives.
              </p>

              {message && <div className="state error">{message}</div>}

              <div className="form-group">
                <label className="form-label required">Replacement PDF File</label>
                <input
                  type="file"
                  accept=".pdf"
                  className="form-input"
                  onChange={(e) => setReplaceFile(e.target.files?.[0] || null)}
                />
              </div>

              <div className="form-group">
                <label className="form-label">Replacement Reason</label>
                <input
                  type="text"
                  className="form-input"
                  placeholder="e.g. Replacing with newly signed high-resolution copy"
                  value={replaceReason}
                  onChange={(e) => setReplaceReason(e.target.value)}
                />
              </div>
            </div>

            <div className="modal-footer">
              <button type="button" className="secondary-button" onClick={() => setReplaceModalOpen(false)}>
                Cancel
              </button>
              <button
                type="button"
                className="primary-button"
                disabled={busy || !replaceFile}
                onClick={() => void handleReplaceDocument()}
              >
                {busy ? "Replacing…" : "Replace Document"}
              </button>
            </div>
          </div>
        </div>
      )}

      {/* Remove Core Document Modal */}
      {removeModalOpen && removeTarget && (
        <div className="modal-overlay" onClick={() => setRemoveModalOpen(false)}>
          <div className="modal-container" onClick={(e) => e.stopPropagation()} style={{ maxWidth: "500px" }}>
            <div className="modal-header">
              <h3>Remove {removeTarget.label} from Core</h3>
              <button className="icon-button" onClick={() => setRemoveModalOpen(false)}>
                <IconClose size={18} />
              </button>
            </div>

            <div className="modal-body" style={{ display: "flex", flexDirection: "column", gap: "16px" }}>
              <p style={{ margin: 0, fontSize: "14px", color: "#b91c1c", fontWeight: 600 }}>
                Unlink {removeTarget.label} from Award #{removeTarget.awardNumber}
              </p>
              <p style={{ margin: 0, fontSize: "12.5px", color: "#64748b" }}>
                This action unlinks the core document role from this Award. Physical document files and historical audit logs will be preserved.
              </p>

              {message && <div className="state error">{message}</div>}

              <div className="form-group">
                <label className="form-label required">Reason for Removal</label>
                <input
                  type="text"
                  className="form-input"
                  placeholder="e.g. Erroneously assigned file category"
                  value={removeReason}
                  onChange={(e) => setRemoveReason(e.target.value)}
                  required
                />
              </div>
            </div>

            <div className="modal-footer">
              <button type="button" className="secondary-button" onClick={() => setRemoveModalOpen(false)}>
                Cancel
              </button>
              <button
                type="button"
                className="primary-button"
                style={{ backgroundColor: "#dc2626", borderColor: "#dc2626" }}
                disabled={busy || !removeReason.trim()}
                onClick={() => void handleRemoveDocument()}
              >
                {busy ? "Removing…" : "Remove Core Document"}
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
};
