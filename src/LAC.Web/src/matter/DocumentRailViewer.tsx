import React, { useState, useEffect } from "react";
import type { MatterDocumentSummaryDto, MatterPdfAnnotationDto } from "./notingTypes";

interface DocumentRailViewerProps {
  matterId: string;
  documents: MatterDocumentSummaryDto[];
  activeDocumentId: string | null;
  activePage: number | null;
  canAnnotate: boolean;
  onSelectDocument: (docId: string, page?: number | null) => void;
  onDocumentUploaded: () => void;
}

function getUuid(): string {
  if (typeof crypto !== "undefined" && typeof crypto.randomUUID === "function") {
    return crypto.randomUUID();
  }
  return "xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx".replace(/[xy]/g, (c) => {
    const r = (Math.random() * 16) | 0;
    const v = c === "x" ? r : (r & 0x3) | 0x8;
    return v.toString(16);
  });
}

export const DocumentRailViewer: React.FC<DocumentRailViewerProps> = ({
  matterId,
  documents,
  activeDocumentId,
  activePage,
  canAnnotate,
  onSelectDocument,
  onDocumentUploaded
}) => {
  const [search, setSearch] = useState("");
  const [filterType, setFilterType] = useState<"all" | "pdf" | "word">("all");
  const [currentPage, setCurrentPage] = useState<number>(activePage || 1);
  const [annotations, setAnnotations] = useState<MatterPdfAnnotationDto[]>([]);
  const [loadingAnnotations, setLoadingAnnotations] = useState(false);

  // Upload modal state
  const [showUploadModal, setShowUploadModal] = useState(false);
  const [uploadFile, setUploadFile] = useState<File | null>(null);
  const [uploadRole, setUploadRole] = useState("Evidence");
  const [uploadDisplayName, setUploadDisplayName] = useState("");
  const [uploading, setUploading] = useState(false);
  const [uploadError, setUploadError] = useState<string | null>(null);

  // New annotation state
  const [showAnnotateModal, setShowAnnotateModal] = useState(false);
  const [annoQuote, setAnnoQuote] = useState("");
  const [annoText, setAnnoText] = useState("");
  const [annoSubmitting, setAnnoSubmitting] = useState(false);

  const activeDoc = documents.find((d) => d.id === activeDocumentId) || documents[0] || null;

  useEffect(() => {
    if (activePage) {
      setCurrentPage(activePage);
    }
  }, [activePage]);

  // Load PDF annotations for active document
  useEffect(() => {
    if (!activeDoc) return;
    const isPdf = activeDoc.filename.toLowerCase().endsWith(".pdf") || activeDoc.mime === "application/pdf";
    if (!isPdf) {
      setAnnotations([]);
      return;
    }

    setLoadingAnnotations(true);
    fetch(`/api/matters/${matterId}/documents/${activeDoc.id}/annotations`, { credentials: "include" })
      .then(async (res) => {
        if (!res.ok) return [];
        return res.json() as Promise<MatterPdfAnnotationDto[]>;
      })
      .then((data) => {
        setAnnotations(Array.isArray(data) ? data : []);
      })
      .catch(() => {
        setAnnotations([]);
      })
      .finally(() => {
        setLoadingAnnotations(false);
      });
  }, [matterId, activeDoc?.id, activeDoc?.version]);

  const filteredDocs = documents.filter((d) => {
    const matchesSearch = d.filename.toLowerCase().includes(search.toLowerCase()) ||
      (d.displayName || "").toLowerCase().includes(search.toLowerCase());
    if (!matchesSearch) return false;

    const isPdf = d.filename.toLowerCase().endsWith(".pdf") || d.mime === "application/pdf";
    const isWord = d.filename.toLowerCase().endsWith(".doc") || d.filename.toLowerCase().endsWith(".docx");

    if (filterType === "pdf") return isPdf;
    if (filterType === "word") return isWord;
    return true;
  });

  const handleUploadSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!uploadFile) {
      setUploadError("Please select a PDF or Word document.");
      return;
    }

    const ext = uploadFile.name.toLowerCase();
    if (!ext.endsWith(".pdf") && !ext.endsWith(".docx") && !ext.endsWith(".doc")) {
      setUploadError("Only PDF and Word (.docx/.doc) files are supported for Matter Noting.");
      return;
    }

    setUploading(true);
    setUploadError(null);

    const formData = new FormData();
    formData.append("file", uploadFile);
    formData.append("documentRole", uploadRole);
    if (uploadDisplayName.trim()) {
      formData.append("displayName", uploadDisplayName.trim());
    }

    try {
      const res = await fetch(`/api/matters/${matterId}/documents`, {
        method: "POST",
        credentials: "include",
        body: formData
      });

      if (!res.ok) {
        const body = await res.json().catch(() => ({}));
        throw new Error(body.message || `Upload failed with status ${res.status}`);
      }

      setShowUploadModal(false);
      setUploadFile(null);
      setUploadDisplayName("");
      onDocumentUploaded();
    } catch (err: any) {
      setUploadError(err.message || "Failed to upload document");
    } finally {
      setUploading(false);
    }
  };

  const handleCreateAnnotation = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!activeDoc) return;
    setAnnoSubmitting(true);

    try {
      const payload = {
        page: currentPage,
        region: { x: 0.1, y: 0.1, width: 0.8, height: 0.15 },
        quote: annoQuote.trim() || undefined,
        text: annoText.trim() || undefined
      };

      const res = await fetch(`/api/matters/${matterId}/documents/${activeDoc.id}/annotations`, {
        method: "POST",
        headers: {
          "Content-Type": "application/json",
          "Idempotency-Key": getUuid()
        },
        body: JSON.stringify(payload)
      });

      if (!res.ok) {
        const body = await res.json().catch(() => ({}));
        throw new Error(body.message || "Failed to add PDF annotation");
      }

      const created: MatterPdfAnnotationDto = await res.json();
      setAnnotations((prev) => [...prev, created]);
      setShowAnnotateModal(false);
      setAnnoQuote("");
      setAnnoText("");
    } catch (err: any) {
      alert(err.message || "Annotation error");
    } finally {
      setAnnoSubmitting(false);
    }
  };

  const isPdf = activeDoc && (activeDoc.filename.toLowerCase().endsWith(".pdf") || activeDoc.mime === "application/pdf");
  const isWord = activeDoc && (activeDoc.filename.toLowerCase().endsWith(".doc") || activeDoc.filename.toLowerCase().endsWith(".docx"));

  return (
    <div style={{ display: "flex", flexDirection: "column", height: "100%", overflow: "hidden" }}>
      {/* Document Rail Header */}
      <div className="document-rail-header">
        <div>
          <h3>Matter Documents ({documents.length})</h3>
          <span style={{ fontSize: "11px", color: "#64748b" }}>Shared across all working officers</span>
        </div>
        <button
          type="button"
          className="noting-btn noting-btn-primary"
          style={{ padding: "4px 8px", fontSize: "11px" }}
          onClick={() => setShowUploadModal(true)}
        >
          + Upload File
        </button>
      </div>

      {/* Filter & Search Bar */}
      <div className="document-rail-search">
        <input
          type="text"
          placeholder="Search documents by name or role..."
          value={search}
          onChange={(e) => setSearch(e.target.value)}
        />
        <div style={{ display: "flex", gap: "6px", marginTop: "6px" }}>
          <button
            type="button"
            className={`noting-btn ${filterType === "all" ? "noting-btn-primary" : "noting-btn-secondary"}`}
            style={{ padding: "2px 8px", fontSize: "10.5px" }}
            onClick={() => setFilterType("all")}
          >
            All ({documents.length})
          </button>
          <button
            type="button"
            className={`noting-btn ${filterType === "pdf" ? "noting-btn-primary" : "noting-btn-secondary"}`}
            style={{ padding: "2px 8px", fontSize: "10.5px" }}
            onClick={() => setFilterType("pdf")}
          >
            PDFs
          </button>
          <button
            type="button"
            className={`noting-btn ${filterType === "word" ? "noting-btn-primary" : "noting-btn-secondary"}`}
            style={{ padding: "2px 8px", fontSize: "10.5px" }}
            onClick={() => setFilterType("word")}
          >
            Word
          </button>
        </div>
      </div>

      {/* Document List */}
      <div className="document-cards-list">
        {filteredDocs.length === 0 ? (
          <div style={{ padding: "12px", textAlign: "center", color: "#94a3b8", fontSize: "12px" }}>
            No matching documents found.
          </div>
        ) : (
          filteredDocs.map((doc) => {
            const isDocPdf = doc.filename.toLowerCase().endsWith(".pdf") || doc.mime === "application/pdf";
            const isDocWord = doc.filename.toLowerCase().endsWith(".doc") || doc.filename.toLowerCase().endsWith(".docx");
            const isSelected = activeDoc?.id === doc.id;

            return (
              <div
                key={doc.id}
                className={`document-rail-card ${isSelected ? "selected" : ""}`}
                onClick={() => onSelectDocument(doc.id, 1)}
              >
                <div className="document-card-meta">
                  <span className="document-card-name" title={doc.displayName || doc.filename}>
                    {doc.displayName || doc.filename}
                  </span>
                  <div className="document-card-sub">
                    <span>{doc.documentRole || "Document"}</span>
                    <span>•</span>
                    <span>v{doc.version}</span>
                  </div>
                </div>
                <span
                  className={`document-type-badge ${isDocPdf ? "pdf" : isDocWord ? "word" : "other"}`}
                >
                  {isDocPdf ? "PDF" : isDocWord ? "DOC" : "FILE"}
                </span>
              </div>
            );
          })
        )}
      </div>

      {/* Embedded Document Viewer Box */}
      <div className="document-viewer-container">
        {activeDoc ? (
          <>
            <div className="document-viewer-toolbar">
              <div style={{ display: "flex", alignItems: "center", gap: "8px", minWidth: 0 }}>
                <strong style={{ fontSize: "12px", color: "#0f172a", overflow: "hidden", textOverflow: "ellipsis", whiteSpace: "nowrap" }}>
                  {activeDoc.filename}
                </strong>
                {isPdf && (
                  <span style={{ fontSize: "11px", color: "#64748b" }}>
                    Page {currentPage}
                  </span>
                )}
              </div>

              <div style={{ display: "flex", alignItems: "center", gap: "6px" }}>
                {isPdf && canAnnotate && (
                  <button
                    type="button"
                    className="noting-btn noting-btn-secondary"
                    style={{ padding: "2px 6px", fontSize: "11px" }}
                    onClick={() => setShowAnnotateModal(true)}
                  >
                    + Highlight
                  </button>
                )}
                <a
                  href={`/api/matters/${matterId}/documents/${activeDoc.id}/content`}
                  target="_blank"
                  rel="noreferrer"
                  className="noting-btn noting-btn-secondary"
                  style={{ padding: "2px 6px", fontSize: "11px", textDecoration: "none" }}
                >
                  Open ↗
                </a>
              </div>
            </div>

            {/* Viewer Content Frame */}
            {isPdf ? (
              <div style={{ flex: 1, display: "flex", flexDirection: "column", position: "relative", overflow: "hidden" }}>
                <iframe
                  src={`/api/matters/${matterId}/documents/${activeDoc.id}/content#page=${currentPage}`}
                  title={activeDoc.filename}
                  className="document-viewer-frame"
                />

                {/* Shared annotations indicator */}
                {annotations.length > 0 && (
                  <div
                    style={{
                      position: "absolute",
                      bottom: "8px",
                      left: "8px",
                      right: "8px",
                      background: "rgba(254, 243, 199, 0.95)",
                      border: "1px solid #fde68a",
                      padding: "6px 10px",
                      borderRadius: "4px",
                      fontSize: "11.5px",
                      color: "#92400e",
                      maxHeight: "60px",
                      overflowY: "auto"
                    }}
                  >
                    <strong>Shared Highlights ({annotations.length}){loadingAnnotations ? " (updating...)" : ""}:</strong>
                    {annotations.map((a) => (
                      <div key={a.id}>
                        p.{a.page}: {a.quote ? `"${a.quote}"` : a.text} — by {a.authorName}
                      </div>
                    ))}
                  </div>
                )}
              </div>
            ) : isWord ? (
              <div className="document-viewer-content" style={{ justifyContent: "center" }}>
                <div style={{ textAlign: "center", padding: "16px" }}>
                  <div style={{ fontSize: "28px", marginBottom: "8px" }}>📄</div>
                  <strong style={{ fontSize: "13px", color: "#1e293b", display: "block", marginBottom: "4px" }}>
                    Word Document ({activeDoc.filename})
                  </strong>
                  <p style={{ fontSize: "12px", color: "#64748b", margin: "0 0 12px 0" }}>
                    Preview and collaborative edits are integrated via ONLYOFFICE / secure download.
                  </p>
                  <a
                    href={`/api/matters/${matterId}/documents/${activeDoc.id}/content`}
                    className="noting-btn noting-btn-primary"
                    style={{ textDecoration: "none" }}
                    download
                  >
                    Download Original
                  </a>
                </div>
              </div>
            ) : (
              <div className="document-viewer-content" style={{ justifyContent: "center" }}>
                <div style={{ textAlign: "center", padding: "16px" }}>
                  <p style={{ fontSize: "12px", color: "#64748b" }}>
                    Format preview unavailable. Use download link to view safely.
                  </p>
                  <a
                    href={`/api/matters/${matterId}/documents/${activeDoc.id}/content`}
                    className="noting-btn noting-btn-secondary"
                    style={{ textDecoration: "none" }}
                    download
                  >
                    Download File
                  </a>
                </div>
              </div>
            )}
          </>
        ) : (
          <div style={{ padding: "24px", textAlign: "center", color: "#94a3b8", fontSize: "12px" }}>
            Select a document from the rail to preview.
          </div>
        )}
      </div>

      {/* Upload File Modal */}
      {showUploadModal && (
        <div className="noting-modal-overlay" onClick={() => setShowUploadModal(false)}>
          <div className="noting-modal-card" onClick={(e) => e.stopPropagation()}>
            <div className="noting-modal-header">
              <h3>Upload Document to Matter</h3>
              <button className="noting-btn noting-btn-secondary" onClick={() => setShowUploadModal(false)} type="button">✕</button>
            </div>
            <form onSubmit={handleUploadSubmit}>
              <div className="noting-modal-body">
                <div>
                  <label style={{ fontSize: "12px", fontWeight: 600, color: "#334155", display: "block", marginBottom: "4px" }}>
                    Choose File (PDF or Word) <span style={{ color: "#b91c1c" }}>*</span>
                  </label>
                  <input
                    type="file"
                    accept=".pdf,.docx,.doc"
                    onChange={(e) => setUploadFile(e.target.files?.[0] || null)}
                    style={{ fontSize: "12.5px" }}
                  />
                </div>

                <div>
                  <label style={{ fontSize: "12px", fontWeight: 600, color: "#334155", display: "block", marginBottom: "4px" }}>
                    Document Role / Category
                  </label>
                  <select
                    value={uploadRole}
                    onChange={(e) => setUploadRole(e.target.value)}
                    style={{ width: "100%", padding: "7px 10px", fontSize: "13px", border: "1px solid #cbd5e1", borderRadius: "4px" }}
                  >
                    <option value="Application">Application / Petition</option>
                    <option value="Evidence">Evidence / Supporting Record</option>
                    <option value="Report">Report / Valuation</option>
                    <option value="Order">Order / Notification</option>
                    <option value="Other">Other</option>
                  </select>
                </div>

                <div>
                  <label style={{ fontSize: "12px", fontWeight: 600, color: "#334155", display: "block", marginBottom: "4px" }}>
                    Display Title (Optional)
                  </label>
                  <input
                    type="text"
                    value={uploadDisplayName}
                    onChange={(e) => setUploadDisplayName(e.target.value)}
                    placeholder="e.g. Valuation Report dated 12-Oct"
                    style={{ width: "100%", padding: "7px 10px", fontSize: "13px", border: "1px solid #cbd5e1", borderRadius: "4px" }}
                  />
                </div>

                {uploadError && (
                  <div style={{ color: "#b91c1c", fontSize: "12px", background: "#fee2e2", padding: "6px 10px", borderRadius: "4px" }}>
                    {uploadError}
                  </div>
                )}
              </div>
              <div className="noting-modal-footer">
                <button className="noting-btn noting-btn-secondary" type="button" onClick={() => setShowUploadModal(false)} disabled={uploading}>
                  Cancel
                </button>
                <button className="noting-btn noting-btn-primary" type="submit" disabled={uploading || !uploadFile}>
                  {uploading ? "Uploading..." : "Upload & Link"}
                </button>
              </div>
            </form>
          </div>
        </div>
      )}

      {/* Add PDF Annotation Modal */}
      {showAnnotateModal && (
        <div className="noting-modal-overlay" onClick={() => setShowAnnotateModal(false)}>
          <div className="noting-modal-card" onClick={(e) => e.stopPropagation()}>
            <div className="noting-modal-header">
              <h3>Add Shared PDF Highlight (Page {currentPage})</h3>
              <button className="noting-btn noting-btn-secondary" onClick={() => setShowAnnotateModal(false)} type="button">✕</button>
            </div>
            <form onSubmit={handleCreateAnnotation}>
              <div className="noting-modal-body">
                <div>
                  <label style={{ fontSize: "12px", fontWeight: 600, color: "#334155", display: "block", marginBottom: "4px" }}>
                    Excerpt / Quote (Optional)
                  </label>
                  <input
                    type="text"
                    value={annoQuote}
                    onChange={(e) => setAnnoQuote(e.target.value)}
                    placeholder="e.g. Total compensation assessed at ₹45,00,000"
                    style={{ width: "100%", padding: "7px 10px", fontSize: "13px", border: "1px solid #cbd5e1", borderRadius: "4px" }}
                  />
                </div>
                <div>
                  <label style={{ fontSize: "12px", fontWeight: 600, color: "#334155", display: "block", marginBottom: "4px" }}>
                    Annotation Remark / Note
                  </label>
                  <textarea
                    value={annoText}
                    onChange={(e) => setAnnoText(e.target.value)}
                    placeholder="Enter observation on this page..."
                    rows={3}
                    style={{ width: "100%", padding: "7px 10px", fontSize: "13px", border: "1px solid #cbd5e1", borderRadius: "4px" }}
                  />
                </div>
              </div>
              <div className="noting-modal-footer">
                <button className="noting-btn noting-btn-secondary" type="button" onClick={() => setShowAnnotateModal(false)} disabled={annoSubmitting}>
                  Cancel
                </button>
                <button className="noting-btn noting-btn-primary" type="submit" disabled={annoSubmitting}>
                  {annoSubmitting ? "Saving..." : "Save Highlight"}
                </button>
              </div>
            </form>
          </div>
        </div>
      )}
    </div>
  );
};
