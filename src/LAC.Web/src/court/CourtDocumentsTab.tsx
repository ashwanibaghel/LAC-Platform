import React, { useState, useEffect, useCallback } from "react";
import type { CourtCaseDetailDto, CourtCaseDocumentDto } from "./types";

interface CourtDocumentsTabProps {
  courtCase: CourtCaseDetailDto;
  onRefresh: () => void;
}

export const CourtDocumentsTab: React.FC<CourtDocumentsTabProps> = ({ courtCase, onRefresh }) => {
  const canManage = courtCase.capabilities?.canManageDocuments ?? false;

  const [documents, setDocuments] = useState<CourtCaseDocumentDto[]>([]);
  const [loading, setLoading] = useState(true);
  const [showUploadModal, setShowUploadModal] = useState(false);
  const [error, setError] = useState<string | null>(null);

  // Form state
  const [file, setFile] = useState<File | null>(null);
  const [documentRole, setDocumentRole] = useState("Court Order");
  const [displayName, setDisplayName] = useState("");
  const [submitting, setSubmitting] = useState(false);
  const [uploadError, setUploadError] = useState<string | null>(null);

  const fetchDocuments = useCallback(async () => {
    try {
      setLoading(true);
      const res = await fetch(`/api/court-cases/${courtCase.id}/documents`, { credentials: "include" });
      if (res.ok) {
        const data = await res.json();
        setDocuments(data);
      } else {
        setError("Failed to load documents");
      }
    } catch {
      setError("Network error loading documents");
    } finally {
      setLoading(false);
    }
  }, [courtCase.id]);

  useEffect(() => {
    void fetchDocuments();
  }, [fetchDocuments]);

  const handleUpload = async (e: React.FormEvent) => {
    e.preventDefault();
    setUploadError(null);

    if (!file) {
      setUploadError("Please select a file to upload.");
      return;
    }

    try {
      setSubmitting(true);
      const formData = new FormData();
      formData.append("file", file);
      formData.append("documentRole", documentRole);
      if (displayName) formData.append("displayName", displayName);
      if (courtCase.revision != null) formData.append("expectedRevision", courtCase.revision.toString());

      const res = await fetch(`/api/court-cases/${courtCase.id}/documents`, {
        method: "POST",
        credentials: "include",
        body: formData,
      });

      if (res.ok) {
        setShowUploadModal(false);
        setFile(null);
        setDisplayName("");
        setDocumentRole("Court Order");
        await fetchDocuments();
        onRefresh();
      } else {
        const err = await res.json().catch(() => ({ error: "Upload failed" }));
        setUploadError(err.error || "Upload failed");
      }
    } catch {
      setUploadError("Network error during file upload");
    } finally {
      setSubmitting(false);
    }
  };

  const handleUnlink = async (docRelationshipId: string) => {
    if (!window.confirm("Are you sure you want to remove this document link from the court case?")) return;
    try {
      const url = courtCase.revision != null
        ? `/api/court-cases/${courtCase.id}/documents/${docRelationshipId}?expectedRevision=${courtCase.revision}`
        : `/api/court-cases/${courtCase.id}/documents/${docRelationshipId}`;
      const res = await fetch(url, {
        method: "DELETE",
        credentials: "include",
      });
      if (res.ok) {
        await fetchDocuments();
        onRefresh();
      } else {
        alert("Failed to unlink document");
      }
    } catch {
      alert("Network error unlinking document");
    }
  };

  const formatFileSize = (bytes: number) => {
    if (bytes < 1024) return `${bytes} B`;
    if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
    return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
  };

  return (
    <div className="court-documents-tab">
      <div className="court-section-header">
        <h3>Litigation Documents & Orders ({documents.length})</h3>
        {canManage && (
          <button className="primary-button" onClick={() => setShowUploadModal(true)}>
            + Upload Document
          </button>
        )}
      </div>

      {error && <div style={{ color: "#dc2626", marginBottom: "16px" }}>{error}</div>}

      {loading ? (
        <div style={{ padding: "32px", textAlign: "center", color: "#64748b" }}>Loading documents...</div>
      ) : documents.length === 0 ? (
        <div style={{ background: "#fff", border: "1px solid #e2e8f0", borderRadius: "8px", padding: "40px", textAlign: "center" }}>
          <div style={{ fontSize: "16px", fontWeight: 600, color: "#1e293b", marginBottom: "8px" }}>No documents attached</div>
          <div style={{ color: "#64748b", fontSize: "14px" }}>
            Attach court orders, notices, petitions, counter-affidavits, and written statements.
          </div>
        </div>
      ) : (
        <div style={{ background: "#fff", border: "1px solid #e2e8f0", borderRadius: "8px", overflow: "hidden" }}>
          <table className="data-table" style={{ width: "100%", borderCollapse: "collapse" }}>
            <thead>
              <tr style={{ background: "#f8fafc", borderBottom: "1px solid #e2e8f0", textAlign: "left" }}>
                <th style={{ padding: "12px 16px" }}>Title / File Name</th>
                <th style={{ padding: "12px 16px" }}>Role</th>
                <th style={{ padding: "12px 16px" }}>Type</th>
                <th style={{ padding: "12px 16px" }}>Size</th>
                <th style={{ padding: "12px 16px" }}>Uploaded</th>
                <th style={{ padding: "12px 16px", textAlign: "right" }}>Actions</th>
              </tr>
            </thead>
            <tbody>
              {documents.map((doc) => (
                <tr key={doc.id} style={{ borderBottom: "1px solid #f1f5f9" }}>
                  <td style={{ padding: "12px 16px" }}>
                    <div style={{ fontWeight: 600, color: "#0f172a" }}>
                      {doc.displayName || doc.originalFileName}
                    </div>
                    {doc.displayName && doc.displayName !== doc.originalFileName && (
                      <div style={{ fontSize: "12px", color: "#64748b" }}>{doc.originalFileName}</div>
                    )}
                  </td>
                  <td style={{ padding: "12px 16px" }}>
                    <span className="court-badge court-badge-ndoh">{doc.documentRole || "Document"}</span>
                  </td>
                  <td style={{ padding: "12px 16px", fontSize: "13px", color: "#64748b" }}>
                    {doc.documentType}
                  </td>
                  <td style={{ padding: "12px 16px", fontSize: "13px", color: "#64748b" }}>
                    {formatFileSize(doc.fileSizeBytes)}
                  </td>
                  <td style={{ padding: "12px 16px", fontSize: "13px", color: "#64748b" }}>
                    <div>{new Date(doc.uploadedAt).toLocaleDateString()}</div>
                    <div style={{ fontSize: "11px", color: "#94a3b8" }}>{doc.uploadedByDisplayName || "Officer"}</div>
                  </td>
                  <td style={{ padding: "12px 16px", textAlign: "right" }}>
                    <div style={{ display: "inline-flex", gap: "8px" }}>
                      <a
                        href={`/api/court-cases/${courtCase.id}/documents/${doc.documentId}/content`}
                        target="_blank"
                        rel="noreferrer"
                        className="quiet-button"
                        style={{ textDecoration: "none", color: "#2563eb", fontWeight: 600 }}
                      >
                        Preview / Download
                      </a>
                      {canManage && (
                        <button
                          className="quiet-button"
                          style={{ color: "#dc2626" }}
                          onClick={() => void handleUnlink(doc.id)}
                          title="Unlink document"
                        >
                          ✕
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

      {showUploadModal && (
        <div className="court-modal-backdrop" onClick={() => setShowUploadModal(false)}>
          <div className="court-modal" onClick={(e) => e.stopPropagation()}>
            <div className="court-modal-header">
              <h3>Upload Court Document</h3>
              <button className="quiet-button" onClick={() => setShowUploadModal(false)}>✕</button>
            </div>
            <form onSubmit={handleUpload}>
              <div className="court-modal-body">
                {uploadError && (
                  <div style={{ color: "#b91c1c", background: "#fef2f2", padding: "10px", borderRadius: "6px" }}>
                    {uploadError}
                  </div>
                )}

                <div>
                  <label style={{ display: "block", fontSize: "13px", fontWeight: 600, color: "#334155", marginBottom: "4px" }}>
                    Select File (PDF, Images) *
                  </label>
                  <input
                    type="file"
                    required
                    accept=".pdf,.png,.jpg,.jpeg"
                    className="form-input"
                    style={{ width: "100%" }}
                    onChange={(e) => setFile(e.target.files?.[0] || null)}
                  />
                </div>

                <div>
                  <label style={{ display: "block", fontSize: "13px", fontWeight: 600, color: "#334155", marginBottom: "4px" }}>
                    Document Role
                  </label>
                  <select
                    className="form-input"
                    style={{ width: "100%" }}
                    value={documentRole}
                    onChange={(e) => setDocumentRole(e.target.value)}
                  >
                    <option value="Court Order">Court Order</option>
                    <option value="Interim Stay Order">Interim Stay Order</option>
                    <option value="Petition / Plaint">Petition / Plaint</option>
                    <option value="Written Statement / Reply">Written Statement / Reply</option>
                    <option value="Counter Affidavit">Counter Affidavit</option>
                    <option value="Rejoinder">Rejoinder</option>
                    <option value="Vakalatnama">Vakalatnama</option>
                    <option value="Notice / Summons">Notice / Summons</option>
                    <option value="Judgment / Decree">Judgment / Decree</option>
                    <option value="Evidence / Exhibit">Evidence / Exhibit</option>
                    <option value="Other">Other</option>
                  </select>
                </div>

                <div>
                  <label style={{ display: "block", fontSize: "13px", fontWeight: 600, color: "#334155", marginBottom: "4px" }}>
                    Display Title (Optional)
                  </label>
                  <input
                    type="text"
                    className="form-input"
                    style={{ width: "100%" }}
                    placeholder="e.g. Interim Stay Order on Khasra 12//4"
                    value={displayName}
                    onChange={(e) => setDisplayName(e.target.value)}
                  />
                </div>
              </div>

              <div className="court-modal-footer">
                <button type="button" className="secondary-button" onClick={() => setShowUploadModal(false)} disabled={submitting}>
                  Cancel
                </button>
                <button type="submit" className="primary-button" disabled={submitting}>
                  {submitting ? "Uploading..." : "Upload Document"}
                </button>
              </div>
            </form>
          </div>
        </div>
      )}
    </div>
  );
};
