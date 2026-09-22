import React, { useState, useEffect } from "react";
import { Link } from "react-router-dom";
import { useAuth } from "../auth/AuthProvider";
import { IconPlus, IconChevronRight, IconClose } from "../components/Icons";
import "./land.css";

const api = "/api";

function date(value?: string | null) {
  if (!value) return "—";
  const raw = String(value);
  const parsed = new Date(/^\d{4}-\d{2}-\d{2}$/.test(raw) ? raw + "T00:00:00" : raw);
  if (Number.isNaN(parsed.getTime())) return "—";
  return new Intl.DateTimeFormat("en-GB", { day: "2-digit", month: "short", year: "numeric" }).format(parsed);
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
  // Exact permission codes as required
  const canAddAward = hasPermission("Award.Create");
  const canUploadCore = hasPermission("Award.CoreDocument.Upload");

  const [refresh, setRefresh] = useState(0);
  const [records, setRecords] = useState<CoreRecordAward[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  // Modal States
  const [addAwardModalOpen, setAddAwardModalOpen] = useState(false);
  const [newAward, setNewAward] = useState({ awardNumber: "", awardDate: "", awardType: "" });

  const [uploadModalOpen, setUploadModalOpen] = useState(false);
  const [uploadTarget, setUploadTarget] = useState<{ awardId: string; awardNumber: string; role: string } | null>(null);
  const [selectedFile, setSelectedFile] = useState<File | null>(null);

  const [busy, setBusy] = useState(false);
  const [message, setMessage] = useState("");

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

  const openUploadModal = (awardId: string, awardNumber: string, role: string) => {
    setUploadTarget({ awardId, awardNumber, role });
    setSelectedFile(null);
    setMessage("");
    setUploadModalOpen(true);
  };

  if (loading) return <div className="state loading">Loading core records matrix…</div>;
  if (error) return <div className="state error">{error}</div>;

  return (
    <div className="village-core-records-tab">
      <div className="section-heading" style={{ marginBottom: "20px" }}>
        <div>
          <h2>Core Records Matrix</h2>
          <span>Presence of authoritative documents across acquisition awards.</span>
        </div>
        {canAddAward && (
          <button className="primary-button" onClick={() => setAddAwardModalOpen(true)}>
            <IconPlus size={16} /> Add Award
          </button>
        )}
      </div>

      <div className="table-wrap">
        <table>
          <thead>
            <tr>
              <th scope="col">Award Reference</th>
              <th scope="col">Award Date</th>
              {CORE_ROLES.map((role) => (
                <th scope="col" key={role.key}>
                  {role.label}
                </th>
              ))}
              <th scope="col">Action</th>
            </tr>
          </thead>
          <tbody>
            {records.length === 0 ? (
              <tr>
                <td colSpan={3 + CORE_ROLES.length} style={{ padding: "20px", textAlign: "center", color: "#64748b" }}>
                  No awards linked to this village yet.
                </td>
              </tr>
            ) : (
              records.map((award) => (
                <tr key={award.id}>
                  <td>
                    <Link to={`/awards/${award.id}`} className="entity-link" style={{ fontWeight: 700 }}>
                      Award #{award.awardNumber}
                    </Link>
                  </td>
                  <td>{date(award.awardDate)}</td>

                  {CORE_ROLES.map(({ key }) => {
                    const roleInfo = award.roles?.find((r) => r.role === key);
                    const doc = award.documents?.find((d) => d.coreDocumentRole === key);
                    const isAvailable = roleInfo?.available ?? false;

                    return (
                      <td key={key}>
                        {isAvailable ? (
                          <div style={{ display: "flex", flexDirection: "column", gap: "2px" }}>
                            <span style={{ fontSize: "13px", fontWeight: 600, color: "#16a34a" }}>
                              ✓ Available {roleInfo && roleInfo.count > 1 ? `(${roleInfo.count})` : ""}
                            </span>
                            {doc?.originalFileName && (
                              <span style={{ fontSize: "11px", color: "#64748b" }}>{doc.originalFileName}</span>
                            )}
                          </div>
                        ) : (
                          <div>
                            <span style={{ fontSize: "12px", color: "#94a3b8" }}>Missing</span>
                            {canUploadCore && (
                              <button
                                className="text-action"
                                style={{
                                  display: "inline-block",
                                  marginLeft: "8px",
                                  fontSize: "12px",
                                  background: "none",
                                  border: "none",
                                  padding: 0,
                                  cursor: "pointer",
                                }}
                                onClick={() => openUploadModal(award.id, award.awardNumber, key)}
                              >
                                Upload
                              </button>
                            )}
                          </div>
                        )}
                      </td>
                    );
                  })}

                  <td>
                    <Link
                      to={`/awards/${award.id}`}
                      className="text-action"
                      style={{ display: "inline-flex", alignItems: "center", gap: "4px" }}
                    >
                      <span>Open Workspace</span>
                      <IconChevronRight size={14} />
                    </Link>
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

      {/* Upload Core Document Modal */}
      {uploadModalOpen && uploadTarget && (
        <div className="modal-overlay" onClick={() => setUploadModalOpen(false)}>
          <div className="modal-container" onClick={(e) => e.stopPropagation()} style={{ maxWidth: "500px" }}>
            <div className="modal-header">
              <h3>Upload {CORE_ROLES.find((r) => r.key === uploadTarget.role)?.label}</h3>
              <button className="icon-button" onClick={() => setUploadModalOpen(false)}>
                <IconClose size={18} />
              </button>
            </div>

            <div className="modal-body" style={{ display: "flex", flexDirection: "column", gap: "16px" }}>
              <p style={{ margin: 0, fontSize: "14px", color: "#475569" }}>
                Target: Award #{uploadTarget.awardNumber} ({uploadTarget.role})
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
    </div>
  );
};
