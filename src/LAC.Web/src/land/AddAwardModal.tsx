import React, { useState, useRef, useEffect } from "react";
import { IconClose, IconFileText } from "../components/Icons";

export interface AddAwardModalProps {
  isOpen: boolean;
  villageName?: string;
  busy: boolean;
  errorMessage?: string | null;
  onClose: () => void;
  onSubmit: (awardData: { awardNumber: string; awardDate: string; awardType: string }, initialFile: File | null) => Promise<void> | void;
}

function formatFileSize(bytes: number): string {
  if (bytes === 0) return "0 Bytes";
  const k = 1024;
  const sizes = ["Bytes", "KB", "MB", "GB"];
  const i = Math.floor(Math.log(bytes) / Math.log(k));
  return parseFloat((bytes / Math.pow(k, i)).toFixed(2)) + " " + sizes[i];
}

export const AddAwardModal: React.FC<AddAwardModalProps> = ({
  isOpen,
  villageName,
  busy,
  errorMessage,
  onClose,
  onSubmit,
}) => {
  const [awardNumber, setAwardNumber] = useState("");
  const [awardDate, setAwardDate] = useState("");
  const [awardType, setAwardType] = useState("");
  const [isDragging, setIsDragging] = useState(false);
  const [selectedFile, setSelectedFile] = useState<File | null>(null);
  const [clientError, setClientError] = useState<string | null>(null);
  const fileInputRef = useRef<HTMLInputElement>(null);

  useEffect(() => {
    if (isOpen) {
      setAwardNumber("");
      setAwardDate("");
      setAwardType("");
      setSelectedFile(null);
      setClientError(null);
      setIsDragging(false);
    }
  }, [isOpen]);

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
    if (!awardNumber.trim()) {
      setClientError("Award number is required.");
      return;
    }
    void onSubmit({ awardNumber: awardNumber.trim(), awardDate, awardType }, selectedFile);
  };

  const activeError = errorMessage || clientError;

  return (
    <div className="upload-modal-overlay" onClick={onClose}>
      <div className="upload-modal-container" onClick={(e) => e.stopPropagation()}>
        <div className="upload-modal-header">
          <div className="upload-modal-header-titles">
            <h3>Add Award to Village</h3>
            <p>Create a new land acquisition award entry and optionally attach its Award PDF file.</p>
          </div>
          <button type="button" className="upload-modal-close-btn" onClick={onClose} title="Close dialog">
            <IconClose size={18} />
          </button>
        </div>

        <form onSubmit={handleSubmit} className="upload-modal-body">
          {villageName && (
            <div className="upload-context-strip">
              <div className="ctx-item">
                <span className="ctx-lbl">Target Village</span>
                <span className="ctx-val">{villageName}</span>
              </div>
            </div>
          )}

          {activeError && <div className="state error">{activeError}</div>}

          <div className="form-group">
            <label className="form-label required" style={{ fontWeight: 650, color: "#0f172a" }}>
              Award Number
            </label>
            <input
              type="text"
              className="form-input"
              placeholder="e.g. 15/2021-22"
              value={awardNumber}
              onChange={(e) => setAwardNumber(e.target.value)}
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
                value={awardDate}
                onChange={(e) => setAwardDate(e.target.value)}
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
                value={awardType}
                onChange={(e) => setAwardType(e.target.value)}
              />
            </div>
          </div>

          {/* Optional Award PDF Upload Zone */}
          <div className="form-group" style={{ marginTop: "4px" }}>
            <label className="form-label" style={{ fontWeight: 600, color: "#334155", marginBottom: "6px" }}>
              Initial Award PDF File <span style={{ color: "#64748b", fontWeight: 400 }}>(Optional)</span>
            </label>
            {!selectedFile ? (
              <div
                className={`upload-dropzone ${isDragging ? "dragging" : ""}`}
                style={{ padding: "20px 16px" }}
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
                <div className="dropzone-icon-wrap" style={{ width: "38px", height: "38px" }}>
                  <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
                    <path d="M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4" />
                    <polyline points="17 8 12 3 7 8" />
                    <line x1="12" y1="3" x2="12" y2="15" />
                  </svg>
                </div>
                <div className="dropzone-text">
                  <span className="drop-title" style={{ fontSize: "13px" }}>
                    Drop Award PDF here, or <span className="browse-link">browse files</span>
                  </span>
                  <span className="drop-hint" style={{ fontSize: "11px" }}>PDF format only • Max 250 MB</span>
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
          </div>

          <div className="modal-footer" style={{ borderTop: "1px solid #e2e8f0", paddingTop: "14px", marginTop: "12px" }}>
            <button type="button" className="secondary-button" onClick={onClose} disabled={busy}>
              Cancel
            </button>
            <button type="submit" className="primary-button" disabled={busy || !awardNumber.trim()}>
              {busy ? "Adding Award…" : "Add Award"}
            </button>
          </div>
        </form>
      </div>
    </div>
  );
};
