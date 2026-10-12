import React, { useState } from "react";
import type { NoteAnchor, MatterDocumentSummaryDto, MatterWorkingDraftDto } from "./notingTypes";
import { generateUuid } from "./notingOfficeApi";

interface CiteDocumentModalProps {
  matterId: string;
  anchor: NoteAnchor;
  draftRevision: number;
  documents: MatterDocumentSummaryDto[];
  onClose: () => void;
  onCited: (draft: MatterWorkingDraftDto) => void;
}

export const CiteDocumentModal: React.FC<CiteDocumentModalProps> = ({
  matterId,
  anchor,
  draftRevision: initialRevision,
  documents,
  onClose,
  onCited
}) => {
  const [selectedDocId, setSelectedDocId] = useState(documents[0]?.id || "");
  const [page, setPage] = useState<number | "">("");
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const selectedDoc = documents.find((d) => d.id === selectedDocId);
  const isPdf = selectedDoc?.filename.toLowerCase().endsWith(".pdf") || selectedDoc?.mime === "application/pdf";

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!selectedDocId) {
      setError("Please select a document to cite.");
      return;
    }
    setSubmitting(true);
    setError(null);

    try {
      const pageNum = page !== "" && !isNaN(Number(page)) ? Number(page) : null;

      // Check current draft revision to avoid race
      let draftRevision = initialRevision;
      try {
        const dRes = await fetch(`/api/matters/${matterId}/noting/draft`, { credentials: "include" });
        if (dRes.ok) {
          const etag = dRes.headers.get("ETag");
          const dData: MatterWorkingDraftDto = await dRes.json();
          const rev = etag ? parseInt(etag.replace(/"/g, ""), 10) : dData.revision;
          if (typeof rev === "number" && !isNaN(rev)) {
            draftRevision = rev;
          }
        }
      } catch {}

      const res = await fetch(`/api/matters/${matterId}/noting/draft/citations`, {
        method: "POST",
        credentials: "include",
        headers: {
          "Content-Type": "application/json",
          "Idempotency-Key": generateUuid()
        },
        body: JSON.stringify({
          anchor,
          documentId: selectedDocId,
          page: pageNum,
          region: null,
          expectedRevision: draftRevision
        })
      });

      if (!res.ok) {
        const body = await res.json().catch(() => ({}));
        throw new Error(body.message || `Failed to add citation (${res.status})`);
      }

      const updatedDraft: MatterWorkingDraftDto = await res.json();
      onCited(updatedDraft);
      onClose();
    } catch (err: any) {
      setError(err.message || "Failed to link citation");
    } finally {
      setSubmitting(false);
    }
  };

  return (
    <div className="noting-modal-overlay" onClick={onClose}>
      <div className="noting-modal-card" onClick={(e) => e.stopPropagation()}>
        <div className="noting-modal-header">
          <h3>Cite Matter Document</h3>
          <button className="noting-btn-close" onClick={onClose} type="button">✕</button>
        </div>
        <form onSubmit={handleSubmit}>
          <div className="noting-modal-body">
            <div>
              <label style={{ fontSize: "11px", fontWeight: 700, color: "#64748b", textTransform: "uppercase" }}>
                Referenced Note Text
              </label>
              <div
                style={{
                  background: "#e0f2fe",
                  border: "1px solid #bae6fd",
                  padding: "8px 10px",
                  borderRadius: "4px",
                  fontSize: "12.5px",
                  color: "#0369a1",
                  fontStyle: "italic",
                  marginTop: "4px"
                }}
              >
                "{anchor.quote}"
              </div>
            </div>

            <div>
              <label style={{ fontSize: "12px", fontWeight: 700, color: "#334155", display: "block", marginBottom: "4px" }}>
                Select Document from Matter Rail
              </label>
              {documents.length === 0 ? (
                <div style={{ fontSize: "12px", color: "#64748b" }}>No documents available on this Matter. Upload a document first.</div>
              ) : (
                <select
                  value={selectedDocId}
                  onChange={(e) => setSelectedDocId(e.target.value)}
                  style={{
                    width: "100%",
                    padding: "8px 10px",
                    fontSize: "13px",
                    border: "1px solid #cbd5e1",
                    borderRadius: "4px",
                    background: "#ffffff"
                  }}
                >
                  {documents.map((d) => (
                    <option key={d.id} value={d.id}>
                      {d.filename} ({d.documentRole || "Document"})
                    </option>
                  ))}
                </select>
              )}
            </div>

            {isPdf ? (
              <div>
                <label style={{ fontSize: "12px", fontWeight: 700, color: "#334155", display: "block", marginBottom: "4px" }}>
                  Page Number (Optional for PDF)
                </label>
                <input
                  type="number"
                  min={1}
                  value={page}
                  onChange={(e) => setPage(e.target.value === "" ? "" : Number(e.target.value))}
                  placeholder="e.g. 4"
                  style={{
                    width: "120px",
                    padding: "6px 10px",
                    fontSize: "13px",
                    border: "1px solid #cbd5e1",
                    borderRadius: "4px"
                  }}
                />
              </div>
            ) : (
              <div style={{ fontSize: "11.5px", color: "#64748b" }}>
                Word / Other documents are cited without specific page numbers.
              </div>
            )}

            {error && (
              <div style={{ color: "#b91c1c", fontSize: "12px", background: "#fee2e2", padding: "6px 10px", borderRadius: "4px" }}>
                {error}
              </div>
            )}
          </div>
          <div className="noting-modal-footer">
            <button className="noting-btn noting-btn-secondary" type="button" onClick={onClose} disabled={submitting}>
              Cancel
            </button>
            <button className="noting-btn noting-btn-primary" type="submit" disabled={submitting || documents.length === 0}>
              {submitting ? "Linking..." : "Insert Citation"}
            </button>
          </div>
        </form>
      </div>
    </div>
  );
};
