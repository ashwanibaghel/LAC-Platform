import React, { useState, useEffect } from "react";
import { Link } from "react-router-dom";
import type { CoreDocumentRole } from "./smartCoreIntakeAdapter";
import {
  coreIntakeAdapter,
  guessDocumentRole,
} from "./smartCoreIntakeAdapter";
import "./land.css";

interface VillageCoreRecordsProps {
  id: string;
}

interface CoreDocumentRecord {
  documentId: string;
  coreDocumentRole: CoreDocumentRole | null;
  originalFileName: string;
  uploadedAt: string;
}

interface RoleCount {
  role: CoreDocumentRole;
  count: number;
  available: boolean;
}

interface AwardCoreRecord {
  id: string;
  awardNumber: string;
  awardDate: string | null;
  awardType: string | null;
  roles: RoleCount[];
  documents: CoreDocumentRecord[];
}

const CORE_ROLES: Array<{
  role: CoreDocumentRole;
  label: string;
  description: string;
}> = [
  {
    role: "Award",
    label: "Award PDF",
    description: "Official award notification & declaration",
  },
  {
    role: "NM",
    label: "Naksha Muntazmin (NM)",
    description: "Field measurement & parcel map schedule",
  },
  {
    role: "StatementA",
    label: "Statement A",
    description: "Compensation assessment & ownership register",
  },
  {
    role: "PossessionProceeding",
    label: "Possession Proceeding",
    description: "Site takeover & possession memorandum",
  },
];

function formatDate(iso?: string | null): string {
  if (!iso) return "—";
  try {
    const d = new Date(iso);
    if (isNaN(d.getTime())) return iso;
    return d.toLocaleDateString("en-IN", {
      day: "2-digit",
      month: "short",
      year: "numeric",
    });
  } catch {
    return iso;
  }
}

export const VillageCoreRecordsWorkspace: React.FC<VillageCoreRecordsProps> = ({
  id,
}) => {
  const [refresh, setRefresh] = useState(0);
  const [records, setRecords] = useState<AwardCoreRecord[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  // Modals state
  const [showUploadModal, setShowUploadModal] = useState(false);
  const [showAddAwardModal, setShowAddAwardModal] = useState(false);
  const [quickAttach, setQuickAttach] = useState<{
    awardId: string;
    awardNumber: string;
    role: CoreDocumentRole;
  } | null>(null);

  // Manual Award creation state
  const [manualAward, setManualAward] = useState({
    awardNumber: "",
    awardDate: "",
    awardType: "Regular",
  });
  const [creatingAward, setCreatingAward] = useState(false);
  const [awardError, setAwardError] = useState("");

  // Fetch core records
  useEffect(() => {
    let active = true;
    setLoading(true);
    fetch(`/api/villages/${id}/core-records?r=${refresh}`, {
      credentials: "include",
    })
      .then((r) => {
        if (!r.ok) throw new Error(`Failed to load core records (${r.status})`);
        return r.json();
      })
      .then((data) => {
        if (active) {
          setRecords(Array.isArray(data) ? data : []);
          setLoading(false);
        }
      })
      .catch((err) => {
        if (active) {
          setError(err.message || "Failed to load core records.");
          setLoading(false);
        }
      });
    return () => {
      active = false;
    };
  }, [id, refresh]);

  // Create manual award
  const handleCreateAward = async () => {
    if (!manualAward.awardNumber.trim()) {
      setAwardError("Award number is required.");
      return;
    }
    setCreatingAward(true);
    setAwardError("");
    try {
      const res = await fetch(`/api/villages/${id}/awards`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          awardNumber: manualAward.awardNumber.trim(),
          awardDate: manualAward.awardDate || null,
          awardType: manualAward.awardType || null,
          remarks: null,
        }),
        credentials: "include",
      });
      if (!res.ok) {
        const text = await res.text();
        throw new Error(text || "Could not add award.");
      }
      setManualAward({ awardNumber: "", awardDate: "", awardType: "Regular" });
      setShowAddAwardModal(false);
      setRefresh((x) => x + 1);
    } catch (e: any) {
      setAwardError(e.message || "Error creating award.");
    } finally {
      setCreatingAward(false);
    }
  };

  return (
    <div className="village-core-workspace" style={{ marginTop: "12px" }}>
      {/* 1. Header with primary action button and secondary manual fallback */}
      <div className="village-core-header">
        <div>
          <h2 className="village-core-title">Award Core Records & Documents</h2>
          <p className="village-core-subtitle">
            Statutory acquisition instruments: Award PDFs, Naksha Muntazmins (NM),
            Statement A registers, and Possession proceedings.
          </p>
        </div>
        <div className="village-core-actions">
          <button
            className="village-btn village-btn-outline"
            onClick={() => {
              setAwardError("");
              setShowAddAwardModal(true);
            }}
          >
            + Add Award Manually
          </button>
          <button
            className="village-btn village-btn-primary"
            onClick={() => setShowUploadModal(true)}
          >
            + Upload Core Documents
          </button>
        </div>
      </div>

      {loading ? (
        <div className="land-loading-container" style={{ padding: "40px", textAlign: "center" }}>
          <div className="land-spinner" />
          <p style={{ marginTop: "12px", color: "#64748b", fontSize: "14px" }}>
            Loading core records & document sets…
          </p>
        </div>
      ) : error ? (
        <div className="land-error-banner">
          <span>⚠️</span>
          <div>{error}</div>
        </div>
      ) : records.length === 0 ? (
        <div className="village-empty-state">
          <p className="village-empty-title">No Awards Registered Yet</p>
          <p className="village-empty-desc">
            Use "+ Upload Core Documents" to ingest acquisition PDFs or "+ Add Award Manually" to register the first award for this village.
          </p>
        </div>
      ) : (
        <div className="village-award-cards-list">
          {records.map((award) => {
            return (
              <div key={award.id} className="village-award-card">
                {/* Award Card Top Bar */}
                <div className="village-award-card-header">
                  <div className="village-award-title-group">
                    <Link
                      to={`/awards/${award.id}`}
                      className="village-award-card-num"
                    >
                      Award {award.awardNumber}
                    </Link>
                    <div className="village-award-meta-row">
                      <span className="village-award-meta-item">
                        📅 {formatDate(award.awardDate)}
                      </span>
                      <span className="village-award-meta-sep">·</span>
                      <span className="village-award-meta-item">
                        {award.awardType || "Acquisition Award"}
                      </span>
                    </div>
                  </div>

                  <div className="village-award-card-cta">
                    <Link
                      to={`/awards/${award.id}`}
                      className="village-open-award-link"
                    >
                      Open Award Workspace &rarr;
                    </Link>
                  </div>
                </div>

                {/* 4 Core Document Slots */}
                <div className="village-core-docs-grid">
                  {CORE_ROLES.map(({ role, label, description }) => {
                    // Match document for this role from award.documents
                    const doc = award.documents?.find(
                      (d) => d.coreDocumentRole === role
                    );

                    return (
                      <div
                        key={role}
                        className={`village-doc-slot ${doc ? "slot-filled" : "slot-empty"}`}
                      >
                        <div className="village-doc-slot-header">
                          <span className="village-doc-role-name">{label}</span>
                          {doc ? (
                            <span className="village-doc-badge badge-uploaded">
                              Uploaded
                            </span>
                          ) : (
                            <span className="village-doc-badge badge-missing">
                              Missing
                            </span>
                          )}
                        </div>

                        <p className="village-doc-slot-desc">{description}</p>

                        <div className="village-doc-slot-content">
                          {doc ? (
                            <div className="village-doc-file-info">
                              <div
                                className="village-doc-filename"
                                title={doc.originalFileName}
                              >
                                📄 {doc.originalFileName}
                              </div>
                              <div className="village-doc-date">
                                Attached {formatDate(doc.uploadedAt)}
                              </div>
                              <div style={{ marginTop: "8px" }}>
                                <a
                                  href={`/api/documents/${doc.documentId}/content`}
                                  target="_blank"
                                  rel="noopener noreferrer"
                                  className="village-doc-view-btn"
                                >
                                  View PDF ↗
                                </a>
                              </div>
                            </div>
                          ) : (
                            <div className="village-doc-missing-box">
                              <span className="village-doc-missing-text">
                                — Not uploaded
                              </span>
                              <button
                                className="village-doc-attach-btn"
                                onClick={() =>
                                  setQuickAttach({
                                    awardId: award.id,
                                    awardNumber: award.awardNumber,
                                    role,
                                  })
                                }
                              >
                                + Attach
                              </button>
                            </div>
                          )}
                        </div>
                      </div>
                    );
                  })}
                </div>
              </div>
            );
          })}
        </div>
      )}

      {/* 2. Upload Core Documents Modal (Smart Intake Prepared) */}
      {showUploadModal && (
        <CoreDocumentUploadModal
          villageId={id}
          awards={records}
          onClose={() => setShowUploadModal(false)}
          onSuccess={() => {
            setShowUploadModal(false);
            setRefresh((x) => x + 1);
          }}
        />
      )}

      {/* 3. Add Award Manually Modal */}
      {showAddAwardModal && (
        <div
          className="modal-backdrop"
          onClick={() => setShowAddAwardModal(false)}
        >
          <div
            className="modal-card"
            style={{ maxWidth: "500px" }}
            onClick={(e) => e.stopPropagation()}
          >
            <div className="village-modal-header">
              <h3 className="village-modal-title">Add Award Manually</h3>
              <button
                className="village-modal-close"
                onClick={() => setShowAddAwardModal(false)}
              >
                &times;
              </button>
            </div>

            {awardError && (
              <div className="land-error-banner" style={{ marginBottom: "14px" }}>
                <span>⚠️</span>
                <div>{awardError}</div>
              </div>
            )}

            <div style={{ display: "grid", gap: "14px", marginTop: "12px" }}>
              <div className="form-group">
                <label style={{ fontWeight: 600, fontSize: "13px" }}>
                  Award Number *
                </label>
                <input
                  type="text"
                  placeholder="e.g. 30/2002-03"
                  value={manualAward.awardNumber}
                  onChange={(e) =>
                    setManualAward({
                      ...manualAward,
                      awardNumber: e.target.value,
                    })
                  }
                  style={{
                    width: "100%",
                    padding: "8px 10px",
                    borderRadius: "6px",
                    border: "1px solid #cbd5e1",
                  }}
                  autoFocus
                />
              </div>

              <div className="form-group">
                <label style={{ fontWeight: 600, fontSize: "13px" }}>
                  Award Date
                </label>
                <input
                  type="date"
                  value={manualAward.awardDate}
                  onChange={(e) =>
                    setManualAward({
                      ...manualAward,
                      awardDate: e.target.value,
                    })
                  }
                  style={{
                    width: "100%",
                    padding: "8px 10px",
                    borderRadius: "6px",
                    border: "1px solid #cbd5e1",
                  }}
                />
              </div>

              <div className="form-group">
                <label style={{ fontWeight: 600, fontSize: "13px" }}>
                  Award Type
                </label>
                <select
                  value={manualAward.awardType}
                  onChange={(e) =>
                    setManualAward({
                      ...manualAward,
                      awardType: e.target.value,
                    })
                  }
                  style={{
                    width: "100%",
                    padding: "8px 10px",
                    borderRadius: "6px",
                    border: "1px solid #cbd5e1",
                  }}
                >
                  <option value="Regular">Regular</option>
                  <option value="Supplementary">Supplementary</option>
                  <option value="Special">Special</option>
                </select>
              </div>
            </div>

            <div className="village-modal-footer">
              <button
                className="village-btn village-btn-outline"
                onClick={() => setShowAddAwardModal(false)}
                disabled={creatingAward}
              >
                Cancel
              </button>
              <button
                className="village-btn village-btn-primary"
                onClick={() => void handleCreateAward()}
                disabled={creatingAward || !manualAward.awardNumber.trim()}
              >
                {creatingAward ? "Saving…" : "Save Award"}
              </button>
            </div>
          </div>
        </div>
      )}

      {/* 4. Quick Single Attach Modal */}
      {quickAttach && (
        <QuickAttachModal
          awardId={quickAttach.awardId}
          awardNumber={quickAttach.awardNumber}
          role={quickAttach.role}
          onClose={() => setQuickAttach(null)}
          onSuccess={() => {
            setQuickAttach(null);
            setRefresh((x) => x + 1);
          }}
        />
      )}
    </div>
  );
};

/**
 * Premium Core Document Upload Modal with Drag/Drop area,
 * Multi-file queue, and Smart Intake typed contract preparation.
 */
interface CoreDocumentUploadModalProps {
  villageId: string;
  awards: AwardCoreRecord[];
  onClose: () => void;
  onSuccess: () => void;
}

interface QueuedFile {
  id: string;
  file: File;
  role: CoreDocumentRole;
  targetAwardId: string;
  status: "idle" | "uploading" | "success" | "error";
  error?: string;
}

const CoreDocumentUploadModal: React.FC<CoreDocumentUploadModalProps> = ({
  villageId,
  awards,
  onClose,
  onSuccess,
}) => {
  const [queue, setQueue] = useState<QueuedFile[]>([]);
  const [isDragging, setIsDragging] = useState(false);
  const [selectedAwardId, setSelectedAwardId] = useState<string>(
    awards[0]?.id || ""
  );
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [generalError, setGeneralError] = useState("");

  const handleFilesAdded = (files: FileList | File[]) => {
    const newItems: QueuedFile[] = [];
    for (let i = 0; i < files.length; i++) {
      const file = files[i];
      if (file.type === "application/pdf" || file.name.toLowerCase().endsWith(".pdf")) {
        newItems.push({
          id: `${file.name}-${Date.now()}-${i}`,
          file,
          role: guessDocumentRole(file.name),
          targetAwardId: selectedAwardId,
          status: "idle",
        });
      }
    }
    if (newItems.length > 0) {
      setQueue((prev) => [...prev, ...newItems]);
      setGeneralError("");
    }
  };

  const handleDrop = (e: React.DragEvent) => {
    e.preventDefault();
    setIsDragging(false);
    if (e.dataTransfer?.files) {
      handleFilesAdded(e.dataTransfer.files);
    }
  };

  const handleRemove = (fileId: string) => {
    setQueue((prev) => prev.filter((item) => item.id !== fileId));
  };

  const handleRoleChange = (fileId: string, newRole: CoreDocumentRole) => {
    setQueue((prev) =>
      prev.map((item) => (item.id === fileId ? { ...item, role: newRole } : item))
    );
  };

  const handleAwardChange = (fileId: string, awardId: string) => {
    setQueue((prev) =>
      prev.map((item) =>
        item.id === fileId ? { ...item, targetAwardId: awardId } : item
      )
    );
  };

  const handleUploadAll = async () => {
    if (queue.length === 0) return;
    setIsSubmitting(true);
    setGeneralError("");

    let successCount = 0;
    const updatedQueue = [...queue];

    for (let i = 0; i < updatedQueue.length; i++) {
      const item = updatedQueue[i];
      if (!item.targetAwardId) {
        item.status = "error";
        item.error = "Select a target award.";
        continue;
      }

      item.status = "uploading";
      setQueue([...updatedQueue]);

      const result = await coreIntakeAdapter.uploadSingleDocument(
        item.targetAwardId,
        item.role,
        item.file
      );

      if (result.ok) {
        item.status = "success";
        successCount++;
      } else {
        item.status = "error";
        item.error = result.error || "Upload failed.";
      }
      setQueue([...updatedQueue]);
    }

    setIsSubmitting(false);
    if (successCount === queue.length) {
      onSuccess();
    } else if (successCount > 0) {
      setGeneralError(
        `Uploaded ${successCount} of ${queue.length} files. Review remaining items.`
      );
    }
  };

  return (
    <div className="modal-backdrop" onClick={onClose}>
      <div
        className="modal-card"
        style={{ maxWidth: "680px" }}
        onClick={(e) => e.stopPropagation()}
      >
        <div className="village-modal-header">
          <div>
            <h3 className="village-modal-title">Upload Core Acquisition Documents</h3>
            <p className="village-modal-subtitle">
              Select or drop Award, Naksha Muntazmin, Statement A, or Possession PDFs.
            </p>
          </div>
          <button className="village-modal-close" onClick={onClose}>
            &times;
          </button>
        </div>

        {generalError && (
          <div className="land-error-banner" style={{ margin: "10px 0" }}>
            <span>⚠️</span>
            <div>{generalError}</div>
          </div>
        )}

        {/* Global default award picker for this queue */}
        {awards.length > 0 && (
          <div
            style={{
              background: "#f8fafc",
              padding: "10px 14px",
              borderRadius: "8px",
              border: "1px solid #e2e8f0",
              margin: "12px 0",
              display: "flex",
              alignItems: "center",
              justifyContent: "space-between",
            }}
          >
            <label style={{ fontSize: "12.5px", fontWeight: 650, color: "#334155" }}>
              Target Award:
            </label>
            <select
              value={selectedAwardId}
              onChange={(e) => {
                const newId = e.target.value;
                setSelectedAwardId(newId);
                // Also update any queued files that haven't been customized
                setQueue((prev) =>
                  prev.map((q) => ({ ...q, targetAwardId: newId }))
                );
              }}
              style={{
                padding: "6px 10px",
                borderRadius: "6px",
                border: "1px solid #cbd5e1",
                fontSize: "13px",
                fontWeight: 600,
                color: "#0f172a",
              }}
            >
              {awards.map((a) => (
                <option key={a.id} value={a.id}>
                  Award {a.awardNumber} ({formatDate(a.awardDate)})
                </option>
              ))}
            </select>
          </div>
        )}

        {/* Drag and Drop Box */}
        <div
          className={`village-dropzone ${isDragging ? "dragging" : ""}`}
          onDragOver={(e) => {
            e.preventDefault();
            setIsDragging(true);
          }}
          onDragLeave={() => setIsDragging(false)}
          onDrop={handleDrop}
          onClick={() => {
            document.getElementById("core-file-input")?.click();
          }}
        >
          <input
            id="core-file-input"
            type="file"
            accept="application/pdf,.pdf"
            multiple
            style={{ display: "none" }}
            onChange={(e) => {
              if (e.target.files) handleFilesAdded(e.target.files);
            }}
          />
          <div className="village-dropzone-icon">📥</div>
          <div className="village-dropzone-text">
            <strong>Drag and drop PDF files here</strong>, or{" "}
            <span style={{ color: "#2563eb", textDecoration: "underline" }}>
              browse from computer
            </span>
          </div>
          <div className="village-dropzone-hint">
            Supports Award PDFs, Naksha Muntazmin (NM), Statement A, and Possession proceedings
          </div>
        </div>

        {/* File Queue List */}
        {queue.length > 0 && (
          <div className="village-queue-list">
            <div className="village-queue-heading">
              <span>Ready for Upload ({queue.length})</span>
            </div>
            {queue.map((item) => (
              <div key={item.id} className="village-queue-item">
                <div className="village-queue-file-desc">
                  <span className="village-queue-filename">📄 {item.file.name}</span>
                  <span className="village-queue-filesize">
                    ({(item.file.size / 1024).toFixed(0)} KB)
                  </span>
                  {item.error && (
                    <span className="village-queue-error">{item.error}</span>
                  )}
                  {item.status === "success" && (
                    <span className="village-queue-success">✓ Uploaded</span>
                  )}
                </div>

                <div className="village-queue-controls">
                  <select
                    value={item.role}
                    onChange={(e) =>
                      handleRoleChange(item.id, e.target.value as CoreDocumentRole)
                    }
                    className="village-queue-role-select"
                    disabled={isSubmitting || item.status === "success"}
                  >
                    <option value="Award">Award PDF</option>
                    <option value="NM">Naksha Muntazmin (NM)</option>
                    <option value="StatementA">Statement A</option>
                    <option value="PossessionProceeding">
                      Possession Proceeding
                    </option>
                  </select>

                  {awards.length > 1 && (
                    <select
                      value={item.targetAwardId}
                      onChange={(e) => handleAwardChange(item.id, e.target.value)}
                      className="village-queue-role-select"
                      disabled={isSubmitting || item.status === "success"}
                    >
                      {awards.map((a) => (
                        <option key={a.id} value={a.id}>
                          Awd {a.awardNumber}
                        </option>
                      ))}
                    </select>
                  )}

                  {item.status !== "success" && (
                    <button
                      className="village-queue-remove-btn"
                      onClick={() => handleRemove(item.id)}
                      disabled={isSubmitting}
                      title="Remove from queue"
                    >
                      &times;
                    </button>
                  )}
                </div>
              </div>
            ))}
          </div>
        )}

        <div className="village-modal-footer">
          <button
            className="village-btn village-btn-outline"
            onClick={onClose}
            disabled={isSubmitting}
          >
            Cancel
          </button>
          <button
            className="village-btn village-btn-primary"
            onClick={() => void handleUploadAll()}
            disabled={isSubmitting || queue.length === 0}
          >
            {isSubmitting
              ? "Uploading…"
              : `Upload ${queue.length} Document${queue.length === 1 ? "" : "s"}`}
          </button>
        </div>
      </div>
    </div>
  );
};

/**
 * Quick Single Document Attach Modal
 */
interface QuickAttachModalProps {
  awardId: string;
  awardNumber: string;
  role: CoreDocumentRole;
  onClose: () => void;
  onSuccess: () => void;
}

const QuickAttachModal: React.FC<QuickAttachModalProps> = ({
  awardId,
  awardNumber,
  role,
  onClose,
  onSuccess,
}) => {
  const [file, setFile] = useState<File | null>(null);
  const [uploading, setUploading] = useState(false);
  const [error, setError] = useState("");

  const roleLabel =
    CORE_ROLES.find((r) => r.role === role)?.label || role;

  const handleUpload = async () => {
    if (!file) return;
    setUploading(true);
    setError("");

    const result = await coreIntakeAdapter.uploadSingleDocument(
      awardId,
      role,
      file
    );

    setUploading(false);
    if (result.ok) {
      onSuccess();
    } else {
      setError(result.error || "Failed to upload document.");
    }
  };

  return (
    <div className="modal-backdrop" onClick={onClose}>
      <div
        className="modal-card"
        style={{ maxWidth: "460px" }}
        onClick={(e) => e.stopPropagation()}
      >
        <div className="village-modal-header">
          <div>
            <h3 className="village-modal-title">Attach {roleLabel}</h3>
            <p className="village-modal-subtitle">
              Award {awardNumber}
            </p>
          </div>
          <button className="village-modal-close" onClick={onClose}>
            &times;
          </button>
        </div>

        {error && (
          <div className="land-error-banner" style={{ margin: "10px 0" }}>
            <span>⚠️</span>
            <div>{error}</div>
          </div>
        )}

        <div style={{ marginTop: "16px" }}>
          <label style={{ display: "block", fontSize: "13px", fontWeight: 600, marginBottom: "8px" }}>
            Select PDF File *
          </label>
          <input
            type="file"
            accept="application/pdf,.pdf"
            onChange={(e) => {
              if (e.target.files?.[0]) setFile(e.target.files[0]);
            }}
            style={{
              width: "100%",
              padding: "8px",
              borderRadius: "6px",
              border: "1px solid #cbd5e1",
            }}
          />
        </div>

        <div className="village-modal-footer">
          <button
            className="village-btn village-btn-outline"
            onClick={onClose}
            disabled={uploading}
          >
            Cancel
          </button>
          <button
            className="village-btn village-btn-primary"
            onClick={() => void handleUpload()}
            disabled={uploading || !file}
          >
            {uploading ? "Uploading…" : "Attach Document"}
          </button>
        </div>
      </div>
    </div>
  );
};
