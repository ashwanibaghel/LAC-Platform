import React, { useState, useRef, useEffect } from "react";
import { IconClose, IconFileText } from "../components/Icons";

export interface CoreDocumentUploadModalProps {
  mode: "upload" | "replace";
  isOpen: boolean;
  villageName?: string;
  awardNumber: string;
  awardId: string;
  role: string;
  roleLabel: string;
  documentId?: string;
  currentFileName?: string;
  busy: boolean;
  errorMessage?: string | null;
  onClose: () => void;
  onSubmitUpload: (file: File) => Promise<void> | void;
  onSubmitReplace: (file: File, reason: string) => Promise<void> | void;
}

function formatFileSize(bytes: number): string {
  if (bytes === 0) return "0 Bytes";
  const k = 1024;
  const sizes = ["Bytes", "KB", "MB", "GB"];
  const i = Math.floor(Math.log(bytes) / Math.log(k));
  return parseFloat((bytes / Math.pow(k, i)).toFixed(2)) + " " + sizes[i];
}

export const CoreDocumentUploadModal: React.FC<CoreDocumentUploadModalProps> = ({
  mode,
  isOpen,
  villageName,
  awardNumber,
  roleLabel,
  currentFileName,
  busy,
  errorMessage,
  onClose,
  onSubmitUpload,
  onSubmitReplace,
}) => {
  const [isDragging, setIsDragging] = useState(false);
  const [selectedFile, setSelectedFile] = useState<File | null>(null);
  const [replaceReason, setReplaceReason] = useState("");
  const [clientError, setClientError] = useState<string | null>(null);
  const fileInputRef = useRef<HTMLInputElement>(null);

  // Reset local state when modal opens or target changes
  useEffect(() => {
    if (isOpen) {
      setSelectedFile(null);
      setReplaceReason("");
      setClientError(null);
      setIsDragging(false);
    }
  }, [isOpen, mode]);

  if (!isOpen) return null;

  const validateAndSetFile = (file: File) => {
    const fileName = file.name.toLowerCase();
    const fileType = file.type.toLowerCase();
    if (!fileName.endsWith(".pdf") && fileType !== "application/pdf") {
      setClientError("Only PDF documents (.pdf) are supported.");
      return;
    }
    setClientError(null);
    setSelectedFile(file);
  };

  const handleDragOver = (e: React.DragEvent) => {
    e.preventDefault();
    e.stopPropagation();
    setIsDragging(true);
  };

  const handleDragLeave = (e: React.DragEvent) => {
    e.preventDefault();
    e.stopPropagation();
    setIsDragging(false);
  };

  const handleDrop = (e: React.DragEvent) => {
    e.preventDefault();
    e.stopPropagation();
    setIsDragging(false);
    if (e.dataTransfer.files && e.dataTransfer.files.length > 0) {
      validateAndSetFile(e.dataTransfer.files[0]);
    }
  };

  const handleFileSelect = (e: React.ChangeEvent<HTMLInputElement>) => {
    if (e.target.files && e.target.files.length > 0) {
      validateAndSetFile(e.target.files[0]);
    }
  };

  const handleClearFile = () => {
    setSelectedFile(null);
    setClientError(null);
    if (fileInputRef.current) {
      fileInputRef.current.value = "";
    }
  };

  const handleSubmit = (e: React.FormEvent) => {
    e.preventDefault();
    if (!selectedFile) return;

    if (mode === "replace") {
      if (!replaceReason.trim()) {
        setClientError("A reason is required to replace an official document.");
        return;
      }
      void onSubmitReplace(selectedFile, replaceReason.trim());
    } else {
      void onSubmitUpload(selectedFile);
    }
  };

  const isSubmitDisabled = busy || !selectedFile || (mode === "replace" && !replaceReason.trim());
  const activeError = errorMessage || clientError;

  return (
    <div className="upload-modal-overlay" onClick={onClose}>
      <div className="upload-modal-container" onClick={(e) => e.stopPropagation()}>
        <div className="upload-modal-header">
          <div className="upload-modal-header-titles">
            <h3>{mode === "replace" ? `Replace ${roleLabel}` : `Upload ${roleLabel}`}</h3>
            <p>
              {mode === "replace"
                ? `Replace existing ${roleLabel} document with an updated official copy.`
                : `Attach official ${roleLabel} document to land acquisition award records.`}
            </p>
          </div>
          <button type="button" className="upload-modal-close-btn" onClick={onClose} title="Close dialog">
            <IconClose size={18} />
          </button>
        </div>

        <form onSubmit={handleSubmit} className="upload-modal-body">
          {/* Compact Context Strip */}
          <div className="upload-context-strip">
            {villageName && (
              <>
                <div className="ctx-item">
                  <span className="ctx-lbl">Village</span>
                  <span className="ctx-val">{villageName}</span>
                </div>
                <div className="ctx-sep">•</div>
              </>
            )}
            <div className="ctx-item">
              <span className="ctx-lbl">Award</span>
              <span className="ctx-val">#{awardNumber}</span>
            </div>
            <div className="ctx-sep">•</div>
            <div className="ctx-item">
              <span className="ctx-lbl">Document Role</span>
              <span className="ctx-val badge">{roleLabel}</span>
            </div>
          </div>

          {/* Currently Linked Document Banner (Replace Mode) */}
          {mode === "replace" && (
            <div className="replace-linked-banner">
              <div className="replace-banner-header">
                <span className="banner-tag">Currently Linked</span>
                <span className="banner-name" title={currentFileName || "Attached Document"}>
                  {currentFileName || "Core File Document"}
                </span>
              </div>
              <p className="replace-banner-sub">
                Replacing this document preserves historical evidence. The current document remains safely linked in official record archives.
              </p>
            </div>
          )}

          {/* Error Message */}
          {activeError && <div className="state error">{activeError}</div>}

          {/* Custom Drag-and-Drop Dropzone or Selected File Card */}
          {!selectedFile ? (
            <div
              className={`upload-dropzone ${isDragging ? "dragging" : ""}`}
              onDragOver={handleDragOver}
              onDragLeave={handleDragLeave}
              onDrop={handleDrop}
              onClick={() => fileInputRef.current?.click()}
            >
              <input
                ref={fileInputRef}
                type="file"
                accept=".pdf,application/pdf"
                style={{ display: "none" }}
                onChange={handleFileSelect}
              />
              <div className="dropzone-icon-wrap">
                <svg width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
                  <path d="M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4" />
                  <polyline points="17 8 12 3 7 8" />
                  <line x1="12" y1="3" x2="12" y2="15" />
                </svg>
              </div>
              <div className="dropzone-text">
                <span className="drop-title">
                  Drop official PDF here, or <span className="browse-link">browse files</span>
                </span>
                <span className="drop-hint">PDF format only • Max file size 250 MB</span>
              </div>
            </div>
          ) : (
            <div className="selected-file-card">
              <div className="file-card-icon">
                <IconFileText size={18} />
                <span className="pdf-tag">PDF</span>
              </div>
              <div className="file-card-details">
                <span className="file-card-name" title={selectedFile.name}>
                  {selectedFile.name}
                </span>
                <span className="file-card-size">{formatFileSize(selectedFile.size)}</span>
              </div>
              <button
                type="button"
                className="file-card-remove-btn"
                onClick={handleClearFile}
                title="Remove selected file"
              >
                <IconClose size={16} />
              </button>
            </div>
          )}

          {/* Replacement Reason Field (Replace Mode) */}
          {mode === "replace" && (
            <div className="form-group" style={{ marginTop: "4px" }}>
              <label className="form-label required" style={{ fontWeight: 650, color: "#0f172a" }}>
                Replacement Reason
              </label>
              <input
                type="text"
                className="form-input"
                placeholder="e.g. Replacing with newly signed high-resolution copy"
                value={replaceReason}
                onChange={(e) => setReplaceReason(e.target.value)}
                required
              />
            </div>
          )}

          {/* Dialog Footer Actions */}
          <div className="modal-footer" style={{ borderTop: "1px solid #e2e8f0", paddingTop: "14px", marginTop: "12px" }}>
            <button type="button" className="secondary-button" onClick={onClose} disabled={busy}>
              Cancel
            </button>
            <button type="submit" className="primary-button" disabled={isSubmitDisabled}>
              {busy
                ? mode === "replace"
                  ? "Replacing Document…"
                  : "Uploading Document…"
                : mode === "replace"
                ? "Replace Document"
                : "Upload Document"}
            </button>
          </div>
        </form>
      </div>
    </div>
  );
};
