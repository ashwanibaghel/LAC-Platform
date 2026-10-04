import React, { useState, useEffect, useRef } from "react";
import { Link } from "react-router-dom";
import type {
  CoreDocumentRole,
  IntakeItem,
  IntakeMatchState,
} from "./smartCoreIntakeAdapter";
import {
  smartIntake,
  guessDocumentRole,
  confidenceLabel,
  roleLabel,
  matchStateLabel,
} from "./smartCoreIntakeAdapter";
import "./land.css";

// ─── Prop / data types ───────────────────────────────────────────────────────

interface VillageCoreRecordsProps {
  id: string;
}

interface CoreDocumentEntry {
  documentId: string;
  role: string;
  coreDocumentRole: CoreDocumentRole | null;
  originalFileName: string;
  uploadedAt: string;
  status: string;
  mimeType?: string;
  viewRoute?: string;
  downloadRoute?: string;
}

interface RoleBucket {
  role: CoreDocumentRole;
  count: number;
  available: boolean;
  documents: CoreDocumentEntry[];
}

interface AwardCoreRecord {
  id: string;
  awardNumber: string;
  awardDate: string | null;
  awardType: string | null;
  roles: RoleBucket[];
  /** Legacy flat list — still present for compatibility, but we prefer roles[].documents */
  documents?: CoreDocumentEntry[];
}

// ─── Constants ───────────────────────────────────────────────────────────────

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

// ─── Helpers ─────────────────────────────────────────────────────────────────

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

function docsForRole(award: AwardCoreRecord, role: CoreDocumentRole): CoreDocumentEntry[] {
  // Prefer nested roles array (new API shape)
  const bucket = award.roles?.find((r) => r.role === role);
  if (bucket && bucket.documents?.length) return bucket.documents;
  // Fallback to flat documents (old API shape / compatibility)
  return (award.documents ?? []).filter((d) => d.coreDocumentRole === role);
}

function matchStateBadgeClass(state: IntakeMatchState): string {
  switch (state) {
    case "MatchedExistingAward": return "intake-badge intake-badge-matched";
    case "ProposedNewAward": return "intake-badge intake-badge-proposed";
    case "NeedsOfficerReview": return "intake-badge intake-badge-review";
  }
}

// ─── Main component ───────────────────────────────────────────────────────────

export const VillageCoreRecordsWorkspace: React.FC<VillageCoreRecordsProps> = ({ id }) => {
  const [refresh, setRefresh] = useState(0);
  const [records, setRecords] = useState<AwardCoreRecord[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  // Modal state
  const [showUploadModal, setShowUploadModal] = useState(false);
  const [showAddAwardModal, setShowAddAwardModal] = useState(false);
  const [quickAttach, setQuickAttach] = useState<{
    awardId: string;
    awardNumber: string;
    role: CoreDocumentRole;
  } | null>(null);

  // Manual Award creation
  const [manualAward, setManualAward] = useState({
    awardNumber: "",
    awardDate: "",
    awardType: "Regular",
  });
  const [creatingAward, setCreatingAward] = useState(false);
  const [awardError, setAwardError] = useState("");

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
      {/* Header */}
      <div className="village-core-header">
        <div>
          <h2 className="village-core-title">Award Core Records &amp; Documents</h2>
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

      {/* Records list */}
      {loading ? (
        <div className="land-loading-container" style={{ padding: "40px", textAlign: "center" }}>
          <div className="land-spinner" />
          <p style={{ marginTop: "12px", color: "#64748b", fontSize: "14px" }}>
            Loading core records &amp; document sets…
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
            Use "Upload Core Documents" to ingest acquisition PDFs — the system will
            detect Award numbers and document types automatically. Or add an Award manually.
          </p>
        </div>
      ) : (
        <div className="village-award-cards-list">
          {records.map((award) => (
            <AwardCoreCard
              key={award.id}
              award={award}
              onAttach={(awardId, awardNumber, role) =>
                setQuickAttach({ awardId, awardNumber, role })
              }
            />
          ))}
        </div>
      )}

      {/* Smart Intake Upload Modal */}
      {showUploadModal && (
        <SmartCoreUploadModal
          villageId={id}
          awards={records}
          onClose={() => setShowUploadModal(false)}
          onSuccess={() => {
            setShowUploadModal(false);
            setRefresh((x) => x + 1);
          }}
        />
      )}

      {/* Add Award Manually Modal */}
      {showAddAwardModal && (
        <div className="modal-backdrop" onClick={() => setShowAddAwardModal(false)}>
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
                <label style={{ fontWeight: 600, fontSize: "13px" }}>Award Number *</label>
                <input
                  type="text"
                  placeholder="e.g. 30/2002-03"
                  value={manualAward.awardNumber}
                  onChange={(e) =>
                    setManualAward({ ...manualAward, awardNumber: e.target.value })
                  }
                  style={{ width: "100%", padding: "8px 10px", borderRadius: "6px", border: "1px solid #cbd5e1" }}
                  autoFocus
                />
              </div>
              <div className="form-group">
                <label style={{ fontWeight: 600, fontSize: "13px" }}>Award Date</label>
                <input
                  type="date"
                  value={manualAward.awardDate}
                  onChange={(e) =>
                    setManualAward({ ...manualAward, awardDate: e.target.value })
                  }
                  style={{ width: "100%", padding: "8px 10px", borderRadius: "6px", border: "1px solid #cbd5e1" }}
                />
              </div>
              <div className="form-group">
                <label style={{ fontWeight: 600, fontSize: "13px" }}>Award Type</label>
                <select
                  value={manualAward.awardType}
                  onChange={(e) =>
                    setManualAward({ ...manualAward, awardType: e.target.value })
                  }
                  style={{ width: "100%", padding: "8px 10px", borderRadius: "6px", border: "1px solid #cbd5e1" }}
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

      {/* Quick Attach Modal (now routes through Smart Intake) */}
      {quickAttach && (
        <QuickAttachModal
          villageId={id}
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

// ─── Award Core Card ──────────────────────────────────────────────────────────

interface AwardCoreCardProps {
  award: AwardCoreRecord;
  onAttach: (awardId: string, awardNumber: string, role: CoreDocumentRole) => void;
}

const AwardCoreCard: React.FC<AwardCoreCardProps> = ({ award, onAttach }) => {
  return (
    <div className="village-award-card">
      <div className="village-award-card-header">
        <div className="village-award-title-group">
          <Link to={`/awards/${award.id}`} className="village-award-card-num">
            Award {award.awardNumber}
          </Link>
          <div className="village-award-meta-row">
            <span className="village-award-meta-item">📅 {formatDate(award.awardDate)}</span>
            <span className="village-award-meta-sep">·</span>
            <span className="village-award-meta-item">{award.awardType || "Acquisition Award"}</span>
          </div>
        </div>
        <div className="village-award-card-cta">
          <Link to={`/awards/${award.id}`} className="village-open-award-link">
            Open Award Workspace &rarr;
          </Link>
        </div>
      </div>

      <div className="village-core-docs-grid">
        {CORE_ROLES.map(({ role, label, description }) => {
          const docs = docsForRole(award, role);
          const hasDocs = docs.length > 0;

          return (
            <div key={role} className={`village-doc-slot ${hasDocs ? "slot-filled" : "slot-empty"}`}>
              <div className="village-doc-slot-header">
                <span className="village-doc-role-name">{label}</span>
                {hasDocs ? (
                  <span className="village-doc-badge badge-uploaded">Uploaded</span>
                ) : (
                  <span className="village-doc-badge badge-missing">Missing</span>
                )}
              </div>

              <p className="village-doc-slot-desc">{description}</p>

              <div className="village-doc-slot-content">
                {hasDocs ? (
                  <div className="village-doc-file-info">
                    {docs.map((doc) => (
                      <div key={doc.documentId} className="village-doc-file-entry">
                        <div className="village-doc-filename" title={doc.originalFileName}>
                          📄 {doc.originalFileName}
                        </div>
                        <div className="village-doc-date">
                          Attached {formatDate(doc.uploadedAt)}
                        </div>
                        <div style={{ marginTop: "6px" }}>
                          <a
                            href={doc.viewRoute || `/api/documents/${doc.documentId}/content`}
                            target="_blank"
                            rel="noopener noreferrer"
                            className="village-doc-view-btn"
                          >
                            View PDF ↗
                          </a>
                        </div>
                      </div>
                    ))}
                  </div>
                ) : (
                  <div className="village-doc-missing-box">
                    <span className="village-doc-missing-text">— Not uploaded</span>
                    <button
                      className="village-doc-attach-btn"
                      onClick={() => onAttach(award.id, award.awardNumber, role)}
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
};

// ─── Smart Core Upload Modal ──────────────────────────────────────────────────

type UploadPhase = "select" | "uploading" | "review" | "done";

interface QueuedFile {
  id: string;
  file: File;
  /** Intake result after upload, or null if still pending / errored */
  intake: IntakeItem | null;
  uploadError: string | null;
}

interface SmartCoreUploadModalProps {
  villageId: string;
  awards: AwardCoreRecord[];
  onClose: () => void;
  onSuccess: () => void;
}

const SmartCoreUploadModal: React.FC<SmartCoreUploadModalProps> = ({
  villageId,
  awards,
  onClose,
  onSuccess,
}) => {
  const [phase, setPhase] = useState<UploadPhase>("select");
  const [files, setFiles] = useState<File[]>([]);
  const [isDragging, setIsDragging] = useState(false);
  const [uploadError, setUploadError] = useState("");
  const [queuedItems, setQueuedItems] = useState<QueuedFile[]>([]);
  const [confirming, setConfirming] = useState<Record<string, boolean>>({});
  const fileInputRef = useRef<HTMLInputElement>(null);

  const addFiles = (incoming: FileList | File[]) => {
    const pdfs: File[] = [];
    for (let i = 0; i < incoming.length; i++) {
      const f = incoming[i];
      if (f.type === "application/pdf" || f.name.toLowerCase().endsWith(".pdf")) {
        pdfs.push(f);
      }
    }
    if (pdfs.length === 0) return;
    setFiles((prev) => {
      const existing = new Set(prev.map((f) => f.name + f.size));
      return [...prev, ...pdfs.filter((f) => !existing.has(f.name + f.size))];
    });
    setUploadError("");
  };

  const removeFile = (idx: number) => {
    setFiles((prev) => prev.filter((_, i) => i !== idx));
  };

  const handleUpload = async () => {
    if (files.length === 0) return;
    setPhase("uploading");
    setUploadError("");

    const result = await smartIntake.uploadBatch(villageId, files);
    if (!result.ok) {
      setUploadError(result.error);
      setPhase("select");
      return;
    }

    const items: QueuedFile[] = result.result.items.map((item, i) => ({
      id: `item-${i}-${item.fileName}`,
      file: files[i] ?? new File([], item.fileName),
      intake: item.intake,
      uploadError: item.error,
    }));

    setQueuedItems(items);
    setPhase("review");
  };

  // How many intakes still need officer action
  const pendingCount = queuedItems.filter(
    (q) => q.intake && q.intake.status !== "Confirmed"
  ).length;

  const handleConfirmExisting = async (
    q: QueuedFile,
    documentRole: CoreDocumentRole,
    awardId: string
  ) => {
    if (!q.intake) return;
    setConfirming((prev) => ({ ...prev, [q.id]: true }));
    const res = await smartIntake.confirmExistingAward(q.intake.intakeId, {
      documentRole,
      awardId,
    });
    setConfirming((prev) => ({ ...prev, [q.id]: false }));
    if (res.ok) {
      setQueuedItems((prev) =>
        prev.map((item) =>
          item.id === q.id ? { ...item, intake: res.intake } : item
        )
      );
    } else {
      setQueuedItems((prev) =>
        prev.map((item) =>
          item.id === q.id
            ? { ...item, uploadError: res.error }
            : item
        )
      );
    }
  };

  const handleConfirmNewAward = async (
    q: QueuedFile,
    documentRole: CoreDocumentRole,
    awardNumber: string,
    awardDate: string | null,
    awardType: string | null
  ) => {
    if (!q.intake) return;
    setConfirming((prev) => ({ ...prev, [q.id]: true }));
    const res = await smartIntake.confirmNewAward(q.intake.intakeId, {
      documentRole,
      createAward: {
        awardNumber,
        awardDate: awardDate || null,
        awardType: awardType || null,
        confirmed: true,
      },
    });
    setConfirming((prev) => ({ ...prev, [q.id]: false }));
    if (res.ok) {
      setQueuedItems((prev) =>
        prev.map((item) =>
          item.id === q.id ? { ...item, intake: res.intake } : item
        )
      );
    } else {
      setQueuedItems((prev) =>
        prev.map((item) =>
          item.id === q.id
            ? { ...item, uploadError: res.error }
            : item
        )
      );
    }
  };

  const handleDoneAll = () => {
    onSuccess();
  };

  return (
    <div className="modal-backdrop" onClick={phase === "select" ? onClose : undefined}>
      <div
        className="modal-card intake-modal"
        onClick={(e) => e.stopPropagation()}
      >
        <div className="village-modal-header">
          <div>
            <h3 className="village-modal-title">Upload Core Acquisition Documents</h3>
            <p className="village-modal-subtitle">
              {phase === "select" && "Drop PDFs — the system detects Award numbers and document types automatically."}
              {phase === "uploading" && "Uploading and analysing…"}
              {phase === "review" && `Review detection results and confirm each document. ${pendingCount > 0 ? `${pendingCount} pending.` : "All confirmed!"}`}
              {phase === "done" && "All documents confirmed and linked."}
            </p>
          </div>
          <button className="village-modal-close" onClick={onClose}>
            &times;
          </button>
        </div>

        {uploadError && (
          <div className="land-error-banner" style={{ margin: "10px 0" }}>
            <span>⚠️</span>
            <div>{uploadError}</div>
          </div>
        )}

        {/* ── Phase: SELECT ── */}
        {phase === "select" && (
          <>
            <div
              className={`village-dropzone ${isDragging ? "dragging" : ""}`}
              onDragOver={(e) => { e.preventDefault(); setIsDragging(true); }}
              onDragLeave={() => setIsDragging(false)}
              onDrop={(e) => { e.preventDefault(); setIsDragging(false); if (e.dataTransfer?.files) addFiles(e.dataTransfer.files); }}
              onClick={() => fileInputRef.current?.click()}
            >
              <input
                ref={fileInputRef}
                type="file"
                accept="application/pdf,.pdf"
                multiple
                style={{ display: "none" }}
                onChange={(e) => { if (e.target.files) addFiles(e.target.files); }}
              />
              <div className="village-dropzone-icon">📥</div>
              <div className="village-dropzone-text">
                <strong>Drag and drop PDF files here</strong>, or{" "}
                <span style={{ color: "#2563eb", textDecoration: "underline" }}>browse from computer</span>
              </div>
              <div className="village-dropzone-hint">
                Up to 20 PDFs · 50 MB each · Award, NM, Statement A, Possession Proceeding
              </div>
            </div>

            {files.length > 0 && (
              <div className="village-queue-list" style={{ marginTop: "12px" }}>
                <div className="village-queue-heading">
                  <span>Selected ({files.length})</span>
                </div>
                {files.map((f, i) => (
                  <div key={`${f.name}-${i}`} className="village-queue-item">
                    <div className="village-queue-file-desc">
                      <span className="village-queue-filename">📄 {f.name}</span>
                      <span className="village-queue-filesize">
                        ({(f.size / 1024).toFixed(0)} KB)
                      </span>
                      <span className="intake-role-hint">
                        Likely: {roleLabel(guessDocumentRole(f.name))}
                      </span>
                    </div>
                    <button
                      className="village-queue-remove-btn"
                      onClick={() => removeFile(i)}
                      title="Remove"
                    >
                      &times;
                    </button>
                  </div>
                ))}
              </div>
            )}

            <div className="village-modal-footer">
              <button className="village-btn village-btn-outline" onClick={onClose}>
                Cancel
              </button>
              <button
                className="village-btn village-btn-primary"
                onClick={() => void handleUpload()}
                disabled={files.length === 0}
              >
                Analyse {files.length > 0 ? `${files.length} ` : ""}Document{files.length === 1 ? "" : "s"}
              </button>
            </div>
          </>
        )}

        {/* ── Phase: UPLOADING ── */}
        {phase === "uploading" && (
          <div style={{ padding: "40px", textAlign: "center" }}>
            <div className="land-spinner" style={{ margin: "0 auto" }} />
            <p style={{ marginTop: "16px", color: "#64748b", fontSize: "14px" }}>
              Uploading {files.length} file{files.length === 1 ? "" : "s"} and running document classifier…
            </p>
          </div>
        )}

        {/* ── Phase: REVIEW ── */}
        {phase === "review" && (
          <>
            <div className="intake-review-list">
              {queuedItems.map((q) => (
                <IntakeReviewCard
                  key={q.id}
                  item={q}
                  awards={awards}
                  isConfirming={!!confirming[q.id]}
                  onConfirmExisting={(role, awardId) =>
                    void handleConfirmExisting(q, role, awardId)
                  }
                  onConfirmNewAward={(role, num, date, type) =>
                    void handleConfirmNewAward(q, role, num, date, type)
                  }
                />
              ))}
            </div>

            <div className="village-modal-footer">
              <button className="village-btn village-btn-outline" onClick={onClose}>
                Close
              </button>
              <button
                className="village-btn village-btn-primary"
                onClick={handleDoneAll}
                disabled={pendingCount > 0}
              >
                {pendingCount > 0
                  ? `${pendingCount} Pending Confirmation`
                  : "Done — Refresh Records"}
              </button>
            </div>
          </>
        )}
      </div>
    </div>
  );
};

// ─── Per-file Intake Review Card ─────────────────────────────────────────────

interface IntakeReviewCardProps {
  item: QueuedFile;
  awards: AwardCoreRecord[];
  isConfirming: boolean;
  onConfirmExisting: (role: CoreDocumentRole, awardId: string) => void;
  onConfirmNewAward: (
    role: CoreDocumentRole,
    awardNumber: string,
    awardDate: string | null,
    awardType: string | null
  ) => void;
}

const IntakeReviewCard: React.FC<IntakeReviewCardProps> = ({
  item,
  awards,
  isConfirming,
  onConfirmExisting,
  onConfirmNewAward,
}) => {
  const { intake, uploadError } = item;
  const [showEvidence, setShowEvidence] = useState(false);

  // Editable officer overrides (pre-filled from detection)
  const [selectedRole, setSelectedRole] = useState<CoreDocumentRole>(
    (intake?.detectedRole as CoreDocumentRole) === "Unknown"
      ? "Award"
      : (intake?.detectedRole as CoreDocumentRole) ?? "Award"
  );
  const [selectedAwardId, setSelectedAwardId] = useState<string>(
    intake?.matchedAwardId ?? awards[0]?.id ?? ""
  );

  // New Award form (for ProposedNewAward)
  const [newAwardNumber, setNewAwardNumber] = useState(
    intake?.detectedAwardNumber ?? ""
  );
  const [newAwardDate, setNewAwardDate] = useState(
    intake?.detectedAwardDate ?? ""
  );
  const [newAwardType, setNewAwardType] = useState(
    intake?.detectedAwardType ?? ""
  );
  const [showNewAwardForm, setShowNewAwardForm] = useState(false);

  if (uploadError && !intake) {
    return (
      <div className="intake-card intake-card-error">
        <div className="intake-card-filename">📄 {item.file.name}</div>
        <div className="intake-error-msg">⚠️ {uploadError}</div>
      </div>
    );
  }

  if (!intake) {
    return (
      <div className="intake-card intake-card-error">
        <div className="intake-card-filename">📄 {item.file.name}</div>
        <div className="intake-error-msg">⚠️ No classification result.</div>
      </div>
    );
  }

  const isConfirmed = intake.status === "Confirmed";
  const conf = confidenceLabel(intake.confidence);

  if (isConfirmed) {
    return (
      <div className="intake-card intake-card-confirmed">
        <div className="intake-card-top">
          <span className="intake-card-filename">📄 {intake.fileName}</span>
          <span className="intake-badge intake-badge-confirmed">✓ Confirmed</span>
        </div>
        <div className="intake-confirmed-summary">
          <span className="intake-confirmed-role">{roleLabel(intake.confirmedRole)}</span>
          {intake.confirmedAwardId && (
            <span className="intake-confirmed-award">
              → Award {awards.find((a) => a.id === intake.confirmedAwardId)?.awardNumber ?? "linked"}
            </span>
          )}
        </div>
        {intake.isDuplicate && (
          <div className="intake-duplicate-note">ℹ️ Exact duplicate — original document reused.</div>
        )}
      </div>
    );
  }

  return (
    <div className={`intake-card ${uploadError ? "intake-card-error" : "intake-card-pending"}`}>
      {/* Top row */}
      <div className="intake-card-top">
        <span className="intake-card-filename">📄 {intake.fileName}</span>
        <span className={matchStateBadgeClass(intake.matchState)}>
          {matchStateLabel(intake.matchState)}
        </span>
      </div>

      {/* Detection summary */}
      <div className="intake-detection-row">
        <div className="intake-detection-item">
          <span className="intake-det-label">Detected type</span>
          <span className="intake-det-value">{roleLabel(intake.detectedRole)}</span>
        </div>
        {intake.detectedAwardNumber && (
          <div className="intake-detection-item">
            <span className="intake-det-label">Award no.</span>
            <span className="intake-det-value">{intake.detectedAwardNumber}</span>
          </div>
        )}
        {intake.detectedAwardDate && (
          <div className="intake-detection-item">
            <span className="intake-det-label">Award date</span>
            <span className="intake-det-value">{formatDate(intake.detectedAwardDate)}</span>
          </div>
        )}
        <div className="intake-detection-item">
          <span className="intake-det-label">Confidence</span>
          <span className={`intake-det-value intake-conf-${conf.level}`}>{conf.text}</span>
        </div>
      </div>

      {/* Evidence accordion */}
      {intake.evidence && intake.evidence.length > 0 && (
        <div className="intake-evidence-section">
          <button
            className="intake-evidence-toggle"
            onClick={() => setShowEvidence((v) => !v)}
          >
            {showEvidence ? "▾" : "▸"} Evidence ({intake.evidence.length})
          </button>
          {showEvidence && (
            <div className="intake-evidence-list">
              {intake.evidence.map((ev, i) => (
                <div key={i} className="intake-evidence-item">
                  <span className="intake-ev-field">{ev.field}</span>
                  <span className="intake-ev-page">p.{ev.pageNumber}</span>
                  <span className="intake-ev-text">"{ev.sourceText}"</span>
                </div>
              ))}
            </div>
          )}
        </div>
      )}

      {uploadError && (
        <div className="intake-error-msg">⚠️ {uploadError}</div>
      )}

      {/* Officer confirmation controls */}
      <div className="intake-confirm-section">
        {/* Role override */}
        <div className="intake-field-row">
          <label className="intake-field-label">Document type</label>
          <select
            className="intake-select"
            value={selectedRole}
            onChange={(e) => setSelectedRole(e.target.value as CoreDocumentRole)}
            disabled={isConfirming}
          >
            <option value="Award">Award PDF</option>
            <option value="NM">Naksha Muntazmin (NM)</option>
            <option value="StatementA">Statement A</option>
            <option value="PossessionProceeding">Possession Proceeding</option>
          </select>
        </div>

        {/* MatchedExistingAward or NeedsOfficerReview → award picker + confirm */}
        {(intake.matchState === "MatchedExistingAward" ||
          intake.matchState === "NeedsOfficerReview") && (
          <>
            <div className="intake-field-row">
              <label className="intake-field-label">Link to Award</label>
              <select
                className="intake-select"
                value={selectedAwardId}
                onChange={(e) => setSelectedAwardId(e.target.value)}
                disabled={isConfirming}
              >
                {awards.map((a) => (
                  <option key={a.id} value={a.id}>
                    Award {a.awardNumber}
                    {a.awardDate ? ` · ${formatDate(a.awardDate)}` : ""}
                  </option>
                ))}
              </select>
            </div>
            <div className="intake-action-row">
              <button
                className="village-btn village-btn-primary"
                disabled={isConfirming || !selectedAwardId}
                onClick={() =>
                  onConfirmExisting(selectedRole, selectedAwardId)
                }
              >
                {isConfirming ? "Confirming…" : "Confirm & Link"}
              </button>
            </div>
          </>
        )}

        {/* ProposedNewAward → review form + confirm */}
        {intake.matchState === "ProposedNewAward" && (
          <>
            {!showNewAwardForm ? (
              <div className="intake-action-row">
                <div className="intake-proposed-note">
                  No matching Award found for this village. Review the detected details and confirm creation of a new Award.
                </div>
                <div style={{ display: "flex", gap: "8px" }}>
                  {awards.length > 0 && (
                    <>
                      <select
                        className="intake-select"
                        value={selectedAwardId}
                        onChange={(e) => setSelectedAwardId(e.target.value)}
                        disabled={isConfirming}
                        style={{ flex: 1 }}
                      >
                        {awards.map((a) => (
                          <option key={a.id} value={a.id}>
                            Award {a.awardNumber}
                          </option>
                        ))}
                      </select>
                      <button
                        className="village-btn village-btn-outline"
                        disabled={isConfirming || !selectedAwardId}
                        onClick={() => onConfirmExisting(selectedRole, selectedAwardId)}
                      >
                        {isConfirming ? "Confirming…" : "Link Existing"}
                      </button>
                    </>
                  )}
                  <button
                    className="village-btn village-btn-primary"
                    onClick={() => setShowNewAwardForm(true)}
                    disabled={isConfirming}
                  >
                    Review &amp; Create Award
                  </button>
                </div>
              </div>
            ) : (
              <div className="intake-new-award-form">
                <div className="intake-new-award-title">Create New Award</div>
                <div className="intake-field-row">
                  <label className="intake-field-label">Award Number *</label>
                  <input
                    className="intake-input"
                    type="text"
                    value={newAwardNumber}
                    onChange={(e) => setNewAwardNumber(e.target.value)}
                    placeholder="e.g. 30/2002-03"
                    disabled={isConfirming}
                  />
                </div>
                <div className="intake-field-row">
                  <label className="intake-field-label">Award Date</label>
                  <input
                    className="intake-input"
                    type="date"
                    value={newAwardDate}
                    onChange={(e) => setNewAwardDate(e.target.value)}
                    disabled={isConfirming}
                  />
                </div>
                <div className="intake-field-row">
                  <label className="intake-field-label">Award Type</label>
                  <input
                    className="intake-input"
                    type="text"
                    value={newAwardType}
                    onChange={(e) => setNewAwardType(e.target.value)}
                    placeholder="e.g. Main, Supplementary"
                    disabled={isConfirming}
                  />
                </div>
                <div className="intake-action-row">
                  <button
                    className="village-btn village-btn-outline"
                    onClick={() => setShowNewAwardForm(false)}
                    disabled={isConfirming}
                  >
                    Back
                  </button>
                  <button
                    className="village-btn village-btn-primary"
                    disabled={isConfirming || !newAwardNumber.trim()}
                    onClick={() =>
                      onConfirmNewAward(
                        selectedRole,
                        newAwardNumber.trim(),
                        newAwardDate || null,
                        newAwardType || null
                      )
                    }
                  >
                    {isConfirming ? "Creating…" : "Confirm & Create Award"}
                  </button>
                </div>
              </div>
            )}
          </>
        )}
      </div>
    </div>
  );
};

// ─── Quick Attach Modal (now routes through Smart Intake) ─────────────────────

interface QuickAttachModalProps {
  villageId: string;
  awardId: string;
  awardNumber: string;
  role: CoreDocumentRole;
  onClose: () => void;
  onSuccess: () => void;
}

const QuickAttachModal: React.FC<QuickAttachModalProps> = ({
  villageId,
  awardId,
  awardNumber,
  role,
  onClose,
  onSuccess,
}) => {
  const [file, setFile] = useState<File | null>(null);
  const [phase, setPhase] = useState<"select" | "uploading" | "review" | "done">("select");
  const [intake, setIntake] = useState<IntakeItem | null>(null);
  const [error, setError] = useState("");
  const [confirming, setConfirming] = useState(false);

  // Officer overrides
  const [selectedRole, setSelectedRole] = useState<CoreDocumentRole>(role);

  const handleUpload = async () => {
    if (!file) return;
    setPhase("uploading");
    setError("");

    const res = await smartIntake.uploadBatch(villageId, [file]);
    if (!res.ok) {
      setError(res.error);
      setPhase("select");
      return;
    }

    const firstItem = res.result.items[0];
    if (!firstItem.intake) {
      setError(firstItem.error || "Classification failed.");
      setPhase("select");
      return;
    }

    // Pre-seed selected role from detection if not Unknown
    if (firstItem.intake.detectedRole && firstItem.intake.detectedRole !== "Unknown") {
      setSelectedRole(firstItem.intake.detectedRole as CoreDocumentRole);
    }

    setIntake(firstItem.intake);
    setPhase("review");
  };

  const handleConfirm = async () => {
    if (!intake) return;
    setConfirming(true);
    setError("");

    const res = await smartIntake.confirmExistingAward(intake.intakeId, {
      documentRole: selectedRole,
      awardId,
    });

    setConfirming(false);
    if (res.ok) {
      onSuccess();
    } else {
      setError(res.error);
    }
  };

  const roleDisplayLabel =
    CORE_ROLES.find((r) => r.role === role)?.label || role;

  return (
    <div className="modal-backdrop" onClick={phase === "select" ? onClose : undefined}>
      <div
        className="modal-card"
        style={{ maxWidth: "500px" }}
        onClick={(e) => e.stopPropagation()}
      >
        <div className="village-modal-header">
          <div>
            <h3 className="village-modal-title">Attach {roleDisplayLabel}</h3>
            <p className="village-modal-subtitle">Award {awardNumber}</p>
          </div>
          <button className="village-modal-close" onClick={onClose}>&times;</button>
        </div>

        {error && (
          <div className="land-error-banner" style={{ margin: "10px 0" }}>
            <span>⚠️</span>
            <div>{error}</div>
          </div>
        )}

        {phase === "select" && (
          <>
            <div style={{ marginTop: "16px" }}>
              <label style={{ display: "block", fontSize: "13px", fontWeight: 600, marginBottom: "8px" }}>
                Select PDF File *
              </label>
              <input
                type="file"
                accept="application/pdf,.pdf"
                onChange={(e) => { if (e.target.files?.[0]) setFile(e.target.files[0]); }}
                style={{ width: "100%", padding: "8px", borderRadius: "6px", border: "1px solid #cbd5e1" }}
              />
              {file && (
                <p style={{ fontSize: "12px", color: "#64748b", marginTop: "6px" }}>
                  📄 {file.name} ({(file.size / 1024).toFixed(0)} KB)
                </p>
              )}
            </div>
            <div className="village-modal-footer">
              <button className="village-btn village-btn-outline" onClick={onClose}>
                Cancel
              </button>
              <button
                className="village-btn village-btn-primary"
                onClick={() => void handleUpload()}
                disabled={!file}
              >
                Analyse &amp; Attach
              </button>
            </div>
          </>
        )}

        {phase === "uploading" && (
          <div style={{ padding: "32px", textAlign: "center" }}>
            <div className="land-spinner" style={{ margin: "0 auto" }} />
            <p style={{ marginTop: "12px", color: "#64748b", fontSize: "14px" }}>
              Uploading and classifying…
            </p>
          </div>
        )}

        {phase === "review" && intake && (
          <>
            <div className="intake-detection-row" style={{ marginTop: "16px" }}>
              <div className="intake-detection-item">
                <span className="intake-det-label">Detected type</span>
                <span className="intake-det-value">{roleLabel(intake.detectedRole)}</span>
              </div>
              {intake.detectedAwardNumber && (
                <div className="intake-detection-item">
                  <span className="intake-det-label">Award no.</span>
                  <span className="intake-det-value">{intake.detectedAwardNumber}</span>
                </div>
              )}
              <div className="intake-detection-item">
                <span className="intake-det-label">Confidence</span>
                <span className={`intake-det-value intake-conf-${confidenceLabel(intake.confidence).level}`}>
                  {confidenceLabel(intake.confidence).text}
                </span>
              </div>
            </div>

            <div className="intake-field-row" style={{ marginTop: "12px" }}>
              <label className="intake-field-label">Confirm document type</label>
              <select
                className="intake-select"
                value={selectedRole}
                onChange={(e) => setSelectedRole(e.target.value as CoreDocumentRole)}
                disabled={confirming}
              >
                <option value="Award">Award PDF</option>
                <option value="NM">Naksha Muntazmin (NM)</option>
                <option value="StatementA">Statement A</option>
                <option value="PossessionProceeding">Possession Proceeding</option>
              </select>
            </div>

            {intake.isDuplicate && (
              <div className="intake-duplicate-note">
                ℹ️ This file was already uploaded — the original document will be linked.
              </div>
            )}

            <div className="village-modal-footer">
              <button className="village-btn village-btn-outline" onClick={onClose} disabled={confirming}>
                Cancel
              </button>
              <button
                className="village-btn village-btn-primary"
                onClick={() => void handleConfirm()}
                disabled={confirming}
              >
                {confirming ? "Confirming…" : "Confirm & Attach"}
              </button>
            </div>
          </>
        )}
      </div>
    </div>
  );
};
