import React, { useState, useEffect, useRef } from "react";
import type { MatterDocumentSummaryDto, MatterPdfAnnotationDto } from "./notingTypes";
import * as pdfjsLib from "pdfjs-dist";
// @ts-ignore
import pdfjsWorker from "pdfjs-dist/build/pdf.worker.mjs?url";

if (typeof window !== "undefined" && pdfjsLib.GlobalWorkerOptions) {
  pdfjsLib.GlobalWorkerOptions.workerSrc = pdfjsWorker;
}

interface DocumentRailViewerProps {
  matterId: string;
  matterRevision?: number;
  documents: MatterDocumentSummaryDto[];
  activeDocumentId: string | null;
  activePage: number | null;
  canAnnotate: boolean;
  onSelectDocument: (docId: string, page?: number | null) => void;
  onDocumentUploaded: () => void;
}

interface SelectedRegion {
  x: number;
  y: number;
  width: number;
  height: number;
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
  matterRevision,
  documents,
  activeDocumentId,
  activePage,
  canAnnotate,
  onSelectDocument,
  onDocumentUploaded
}) => {
  const [filterType, setFilterType] = useState<"all" | "pdf" | "word">("all");
  const [search, setSearch] = useState("");
  const [showUploadModal, setShowUploadModal] = useState(false);

  // Upload Form State
  const [uploadFile, setUploadFile] = useState<File | null>(null);
  const [uploadRole, setUploadRole] = useState("Evidence");
  const [uploadDisplayName, setUploadDisplayName] = useState("");
  const [uploading, setUploading] = useState(false);
  const [uploadError, setUploadError] = useState<string | null>(null);

  // PDF Page & Annotation State
  const [currentPage, setCurrentPage] = useState<number>(activePage || 1);
  const [totalPages, setTotalPages] = useState<number>(1);
  const [zoom, setZoom] = useState<number>(1.0);
  const [rendering, setRendering] = useState<boolean>(false);
  const [renderError, setRenderError] = useState<string | null>(null);
  const [pageDims, setPageDims] = useState<{ width: number; height: number }>({ width: 600, height: 800 });

  const [annotations, setAnnotations] = useState<MatterPdfAnnotationDto[]>([]);
  const [showAnnotateModal, setShowAnnotateModal] = useState(false);
  const [annoQuote, setAnnoQuote] = useState("");
  const [annoText, setAnnoText] = useState("");
  const [annoSubmitting, setAnnoSubmitting] = useState(false);
  const [selectedAnnotation, setSelectedAnnotation] = useState<MatterPdfAnnotationDto | null>(null);

  // Interactive Region Selection State
  const [isSelectingRegion, setIsSelectingRegion] = useState<boolean>(false);
  const [dragBox, setDragBox] = useState<SelectedRegion | null>(null);
  const dragStartRef = useRef<{ x: number; y: number } | null>(null);
  const dragBoxRef = useRef<SelectedRegion | null>(null);
  const [selectedRegion, setSelectedRegion] = useState<SelectedRegion>({ x: 0.1, y: 0.1, width: 0.8, height: 0.15 });

  const canvasRef = useRef<HTMLCanvasElement | null>(null);
  const interactionLayerRef = useRef<HTMLDivElement | null>(null);
  const [pdfDoc, setPdfDoc] = useState<any>(null);
  const pdfDocRef = useRef<any>(null);
  const renderTaskRef = useRef<any>(null);

  const activeDoc = documents.find((d) => d.id === activeDocumentId) || documents[0] || null;
  const isPdf = activeDoc && (activeDoc.filename.toLowerCase().endsWith(".pdf") || activeDoc.mime === "application/pdf");
  const isWord = activeDoc && (activeDoc.filename.toLowerCase().endsWith(".doc") || activeDoc.filename.toLowerCase().endsWith(".docx"));

  // Update page when activePage prop changes
  useEffect(() => {
    if (activePage && activePage > 0) {
      setCurrentPage(activePage);
    }
  }, [activePage]);

  // Load Annotations for Active Document
  useEffect(() => {
    if (!activeDoc) {
      setAnnotations([]);
      return;
    }

    fetch(`/api/matters/${matterId}/documents/${activeDoc.id}/annotations`, {
      credentials: "include"
    })
      .then(async (res) => (res.ok ? res.json() : []))
      .then((data) => {
        setAnnotations(Array.isArray(data) ? data : []);
      })
      .catch(() => {
        setAnnotations([]);
      });
  }, [matterId, activeDoc?.id, activeDoc?.version]);

  // Load PDF Document via PDF.js
  useEffect(() => {
    if (!activeDoc || !isPdf) {
      pdfDocRef.current = null;
      setPdfDoc(null);
      return;
    }

    let active = true;
    setRenderError(null);
    const pdfUrl = `/api/matters/${matterId}/documents/${activeDoc.id}/content`;

    const loadingTask = pdfjsLib.getDocument({
      url: pdfUrl,
      withCredentials: true
    });

    loadingTask.promise
      .then((loadedPdf) => {
        if (!active) return;
        pdfDocRef.current = loadedPdf;
        setPdfDoc(loadedPdf);
        setTotalPages(loadedPdf.numPages);
      })
      .catch((err) => {
        if (!active) return;
        setPdfDoc(null);
        setRenderError(err.message || "Failed to load PDF document");
      });

    return () => {
      active = false;
      setPdfDoc(null);
      pdfDocRef.current = null;
      loadingTask.destroy();
    };
  }, [matterId, activeDoc?.id, isPdf]);

  // Render Page onto Canvas whenever currentPage, zoom or pdfDoc changes
  useEffect(() => {
    const docToRender = pdfDoc || pdfDocRef.current;
    const canvas = canvasRef.current;
    if (!docToRender || !canvas || !isPdf) return;

    let active = true;
    setRendering(true);

    if (renderTaskRef.current) {
      try {
        renderTaskRef.current.cancel();
      } catch {}
    }

    docToRender.getPage(currentPage).then((page: any) => {
      if (!active) return;

      const viewport = page.getViewport({ scale: zoom });
      canvas.width = viewport.width;
      canvas.height = viewport.height;
      setPageDims({ width: viewport.width, height: viewport.height });

      const ctx = canvas.getContext("2d");
      if (!ctx) return;

      const renderContext = {
        canvasContext: ctx,
        viewport
      };

      const renderTask = page.render(renderContext);
      renderTaskRef.current = renderTask;

      renderTask.promise
        .then(() => {
          if (!active) return;
          setRendering(false);
        })
        .catch((err: any) => {
          if (!active || err?.name === "RenderingCancelledException") return;
          setRendering(false);
        });
    }).catch((err: any) => {
      if (!active) return;
      setRendering(false);
      setRenderError(err.message || "Error rendering page");
    });

    return () => {
      active = false;
      if (renderTaskRef.current) {
        try {
          renderTaskRef.current.cancel();
        } catch {}
      }
    };
  }, [pdfDoc, currentPage, zoom, isPdf, activeDoc?.id]);

  // Interactive Drag-to-Select Region Handlers
  const handleMouseDown = (e: React.MouseEvent<HTMLDivElement>) => {
    if (!interactionLayerRef.current) return;
    const rect = interactionLayerRef.current.getBoundingClientRect();
    const x = Math.max(0, Math.min(1, (e.clientX - rect.left) / rect.width));
    const y = Math.max(0, Math.min(1, (e.clientY - rect.top) / rect.height));

    const initial = { x, y };
    const box = { x, y, width: 0, height: 0 };
    dragStartRef.current = initial;
    dragBoxRef.current = box;
    setDragBox(box);
  };

  const handleMouseMove = (e: React.MouseEvent<HTMLDivElement>) => {
    const start = dragStartRef.current;
    if (!start || !interactionLayerRef.current) return;
    const rect = interactionLayerRef.current.getBoundingClientRect();
    const curX = Math.max(0, Math.min(1, (e.clientX - rect.left) / rect.width));
    const curY = Math.max(0, Math.min(1, (e.clientY - rect.top) / rect.height));

    const left = Math.min(start.x, curX);
    const top = Math.min(start.y, curY);
    const width = Math.abs(curX - start.x);
    const height = Math.abs(curY - start.y);

    const box = { x: left, y: top, width, height };
    dragBoxRef.current = box;
    setDragBox(box);
  };

  const handleMouseUp = () => {
    const curBox = dragBoxRef.current;
    if (curBox && curBox.width > 0.02 && curBox.height > 0.01) {
      setSelectedRegion({
        x: Math.round(curBox.x * 1000) / 1000,
        y: Math.round(curBox.y * 1000) / 1000,
        width: Math.round(curBox.width * 1000) / 1000,
        height: Math.round(curBox.height * 1000) / 1000
      });
      setIsSelectingRegion(false);
      setShowAnnotateModal(true);
    }
    dragStartRef.current = null;
    dragBoxRef.current = null;
    setDragBox(null);
  };

  const filteredDocs = documents.filter((d) => {
    const matchesSearch = d.filename.toLowerCase().includes(search.toLowerCase()) ||
      (d.displayName || "").toLowerCase().includes(search.toLowerCase());
    if (!matchesSearch) return false;

    const isDocPdf = d.filename.toLowerCase().endsWith(".pdf") || d.mime === "application/pdf";
    const isDocWord = d.filename.toLowerCase().endsWith(".doc") || d.filename.toLowerCase().endsWith(".docx");

    if (filterType === "pdf") return isDocPdf;
    if (filterType === "word") return isDocWord;
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
    formData.append("role", uploadRole);
    formData.append("documentRole", uploadRole);
    if (uploadDisplayName.trim()) {
      formData.append("displayName", uploadDisplayName.trim());
    }
    if (typeof matterRevision === "number") {
      formData.append("expectedRevision", String(matterRevision));
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
        region: selectedRegion,
        quote: annoQuote.trim() || undefined,
        text: annoText.trim() || undefined
      };

      const res = await fetch(`/api/matters/${matterId}/documents/${activeDoc.id}/annotations`, {
        method: "POST",
        credentials: "include",
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
          className="document-search-input"
        />
        <div className="document-filter-chips">
          <button
            type="button"
            className={`filter-chip ${filterType === "all" ? "active" : ""}`}
            onClick={() => setFilterType("all")}
          >
            All ({documents.length})
          </button>
          <button
            type="button"
            className={`filter-chip ${filterType === "pdf" ? "active" : ""}`}
            onClick={() => setFilterType("pdf")}
          >
            PDFs
          </button>
          <button
            type="button"
            className={`filter-chip ${filterType === "word" ? "active" : ""}`}
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

      {/* Synchronized Document Viewer Box */}
      <div className="document-viewer-container" style={{ flex: 1, display: "flex", flexDirection: "column", minHeight: 0, overflow: "hidden" }}>
        {activeDoc ? (
          <>
            <div className="document-viewer-toolbar">
              <div style={{ display: "flex", alignItems: "center", gap: "8px", minWidth: 0 }}>
                <strong style={{ fontSize: "12px", color: "#0f172a", overflow: "hidden", textOverflow: "ellipsis", whiteSpace: "nowrap" }}>
                  {activeDoc.filename}
                </strong>
                {isPdf && (
                  <div style={{ display: "flex", alignItems: "center", gap: "4px" }}>
                    <button
                      type="button"
                      className="noting-btn noting-btn-secondary"
                      style={{ padding: "1px 5px", fontSize: "10.5px" }}
                      onClick={() => setCurrentPage(Math.max(1, currentPage - 1))}
                      disabled={currentPage <= 1}
                      title="Previous Page"
                    >
                      ◀
                    </button>
                    <span style={{ fontSize: "11px", color: "#64748b" }}>
                      Page {currentPage} of {totalPages}
                    </span>
                    <button
                      type="button"
                      className="noting-btn noting-btn-secondary"
                      style={{ padding: "1px 5px", fontSize: "10.5px" }}
                      onClick={() => setCurrentPage(Math.min(totalPages, currentPage + 1))}
                      disabled={currentPage >= totalPages}
                      title="Next Page"
                    >
                      ▶
                    </button>
                  </div>
                )}
                {isPdf && annotations.length > 0 && (
                  <span
                    className="pdf-shared-highlights-badge"
                    style={{ fontSize: "11px", color: "#854d0e", background: "#fef08a", padding: "1px 6px", borderRadius: "3px", fontWeight: 600 }}
                    title="Shared highlights visible to all authorized officers"
                  >
                    Shared Highlights ({annotations.length})
                  </span>
                )}
              </div>

              {/* Synchronized Zoom & Region Selection Controls */}
              <div style={{ display: "flex", alignItems: "center", gap: "6px" }}>
                {isPdf && (
                  <>
                    <button
                      type="button"
                      className="noting-btn noting-btn-secondary"
                      style={{ padding: "2px 6px", fontSize: "11px" }}
                      onClick={() => setZoom((z) => Math.max(0.6, Math.round((z - 0.2) * 10) / 10))}
                      title="Zoom Out"
                    >
                      －
                    </button>
                    <span style={{ fontSize: "11px", color: "#475569", minWidth: "36px", textAlign: "center" }}>
                      {Math.round(zoom * 100)}%
                    </span>
                    <button
                      type="button"
                      className="noting-btn noting-btn-secondary"
                      style={{ padding: "2px 6px", fontSize: "11px" }}
                      onClick={() => setZoom((z) => Math.min(2.5, Math.round((z + 0.2) * 10) / 10))}
                      title="Zoom In"
                    >
                      ＋
                    </button>
                    <button
                      type="button"
                      className="noting-btn noting-btn-secondary"
                      style={{ padding: "2px 6px", fontSize: "11px" }}
                      onClick={() => setZoom(1.0)}
                      title="Reset Zoom to 100%"
                    >
                      100%
                    </button>
                  </>
                )}

                {isPdf && canAnnotate && (
                  <button
                    type="button"
                    className={`noting-btn ${isSelectingRegion ? "noting-btn-primary" : "noting-btn-secondary"} add-highlight-btn`}
                    style={{ padding: "2px 6px", fontSize: "11px" }}
                    onClick={() => {
                      if (!isSelectingRegion) {
                        setIsSelectingRegion(true);
                      } else {
                        setShowAnnotateModal(true);
                      }
                    }}
                    title={isSelectingRegion ? "Drag on page to select highlight region" : "Add Page Highlight"}
                  >
                    {isSelectingRegion ? "Draw Region ✎" : "+ Highlight"}
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

            {/* Viewer Content Frame: Synchronized Canvas Page Renderer */}
            {isPdf ? (
              <div
                className="pdf-viewport-scroll"
                style={{
                  flex: 1,
                  overflow: "auto",
                  background: "#334155",
                  padding: "16px",
                  display: "flex",
                  flexDirection: "column",
                  alignItems: "center"
                }}
              >
                {rendering && (
                  <div style={{ color: "#f8fafc", fontSize: "11.5px", marginBottom: "8px" }}>
                    Rendering Page {currentPage}…
                  </div>
                )}
                {renderError && (
                  <div style={{ color: "#fca5a5", fontSize: "12px", marginBottom: "8px" }}>
                    {renderError}
                  </div>
                )}

                {/* Page Wrapper maintaining exact pixel scale for canvas + overlay */}
                <div
                  className="pdf-canvas-page-wrapper"
                  style={{
                    position: "relative",
                    width: `${pageDims.width}px`,
                    height: `${pageDims.height}px`,
                    boxShadow: "0 6px 18px rgba(0,0,0,0.35)",
                    background: "#ffffff"
                  }}
                >
                  <canvas
                    ref={canvasRef}
                    style={{
                      display: "block",
                      width: "100%",
                      height: "100%"
                    }}
                  />

                  {/* Interactive Synchronized Overlay Layer */}
                  <div
                    ref={interactionLayerRef}
                    className="pdf-highlight-interaction-layer"
                    style={{
                      position: "absolute",
                      inset: 0,
                      cursor: isSelectingRegion ? "crosshair" : "default",
                      userSelect: "none"
                    }}
                    onMouseDown={handleMouseDown}
                    onMouseMove={handleMouseMove}
                    onMouseUp={handleMouseUp}
                  >
                    {/* Render Saved Annotations for this page */}
                    {annotations
                      .filter((a) => a.page === currentPage)
                      .map((a) => {
                        let region = { x: 0.1, y: 0.1, width: 0.8, height: 0.15 };
                        try {
                          if (a.regionJson) {
                            const parsed = JSON.parse(a.regionJson);
                            if (parsed && typeof parsed.x === "number") region = parsed;
                          }
                        } catch {}

                        return (
                          <div
                            key={a.id}
                            className="pdf-highlight-overlay"
                            style={{
                              position: "absolute",
                              left: `${region.x * 100}%`,
                              top: `${region.y * 100}%`,
                              width: `${region.width * 100}%`,
                              height: `${region.height * 100}%`,
                              backgroundColor: "rgba(250, 204, 21, 0.35)",
                              border: "2px solid #ca8a04",
                              borderRadius: "3px",
                              pointerEvents: "auto",
                              cursor: "pointer"
                            }}
                            title={`Page ${a.page} Highlight: ${a.quote || a.text || ""} (by ${a.authorName})`}
                            onClick={(e) => {
                              e.stopPropagation();
                              setSelectedAnnotation(a);
                            }}
                          >
                            <span className="pdf-highlight-tag">
                              {a.authorName}: {a.quote || a.text || "Highlight"}
                            </span>
                          </div>
                        );
                      })}

                    {/* Active Drag-to-Select Bounding Box */}
                    {dragBox && (
                      <div
                        className="pdf-selection-drag-box"
                        style={{
                          position: "absolute",
                          left: `${dragBox.x * 100}%`,
                          top: `${dragBox.y * 100}%`,
                          width: `${dragBox.width * 100}%`,
                          height: `${dragBox.height * 100}%`,
                          border: "2px dashed #f59e0b",
                          backgroundColor: "rgba(253, 224, 71, 0.3)",
                          pointerEvents: "none"
                        }}
                      />
                    )}
                  </div>
                </div>

                {/* Selected Annotation Detail Card */}
                {selectedAnnotation && (
                  <div
                    style={{
                      position: "sticky",
                      bottom: "16px",
                      maxWidth: "480px",
                      width: "100%",
                      background: "#ffffff",
                      border: "1px solid #cbd5e1",
                      borderRadius: "6px",
                      padding: "10px 14px",
                      boxShadow: "0 8px 24px rgba(0,0,0,0.2)",
                      zIndex: 30,
                      fontSize: "12px",
                      marginTop: "16px"
                    }}
                  >
                    <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center", marginBottom: "6px" }}>
                      <strong style={{ color: "#854d0e" }}>Shared Highlight (Page {selectedAnnotation.page})</strong>
                      <button
                        type="button"
                        style={{ border: "none", background: "transparent", cursor: "pointer", fontSize: "13px", color: "#64748b" }}
                        onClick={() => setSelectedAnnotation(null)}
                      >
                        ✕
                      </button>
                    </div>
                    {selectedAnnotation.quote && (
                      <div style={{ fontStyle: "italic", color: "#334155", marginBottom: "6px", background: "#fef9c3", padding: "4px 6px", borderRadius: "3px" }}>
                        "{selectedAnnotation.quote}"
                      </div>
                    )}
                    {selectedAnnotation.text && (
                      <div style={{ color: "#0f172a", marginBottom: "6px" }}>
                        {selectedAnnotation.text}
                      </div>
                    )}
                    <div style={{ fontSize: "11px", color: "#64748b" }}>
                      By {selectedAnnotation.authorName} • {new Date(selectedAnnotation.createdAt).toLocaleDateString()}
                    </div>
                  </div>
                )}
              </div>
            ) : isWord ? (
              <div style={{ padding: "24px", textAlign: "center", background: "#f8fafc", flex: 1 }}>
                <div style={{ fontSize: "36px", marginBottom: "12px" }}>📄</div>
                <h4 style={{ margin: "0 0 6px 0", color: "#0f172a" }}>{activeDoc.filename}</h4>
                <p style={{ margin: "0 0 16px 0", color: "#64748b", fontSize: "12.5px" }}>
                  Official Word Document linked to this Matter.
                </p>
                <div style={{ display: "flex", gap: "8px", justifyContent: "center" }}>
                  <a
                    href={`/api/matters/${matterId}/documents/${activeDoc.id}/content`}
                    download={activeDoc.filename}
                    className="noting-btn noting-btn-primary"
                    style={{ textDecoration: "none" }}
                  >
                    Download Document
                  </a>
                  <a
                    href={`/editor?documentId=${activeDoc.id}`}
                    target="_blank"
                    rel="noreferrer"
                    className="noting-btn noting-btn-secondary"
                    style={{ textDecoration: "none" }}
                  >
                    Open in ONLYOFFICE ↗
                  </a>
                </div>
              </div>
            ) : (
              <div style={{ padding: "24px", textAlign: "center", color: "#64748b" }}>
                Select a document from the rail to preview.
              </div>
            )}
          </>
        ) : (
          <div style={{ padding: "24px", textAlign: "center", color: "#94a3b8", fontSize: "13px" }}>
            No documents uploaded yet. Use <strong>+ Upload File</strong> above to attach petitions, reports or orders.
          </div>
        )}
      </div>

      {/* Upload Document Modal */}
      {showUploadModal && (
        <div className="noting-modal-overlay" onClick={() => setShowUploadModal(false)}>
          <div className="noting-modal-card" onClick={(e) => e.stopPropagation()}>
            <div className="noting-modal-header">
              <h3>Upload Matter Document</h3>
              <button className="noting-btn noting-btn-secondary" onClick={() => setShowUploadModal(false)} type="button">✕</button>
            </div>
            <form onSubmit={handleUploadSubmit}>
              <div className="noting-modal-body">
                <div>
                  <label style={{ fontSize: "12px", fontWeight: 600, color: "#334155", display: "block", marginBottom: "4px" }}>
                    Select File (PDF or Word)
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
                <div style={{ fontSize: "11px", color: "#475569", background: "#f8fafc", padding: "6px 8px", borderRadius: "4px" }}>
                  Selected Region: ({Math.round(selectedRegion.x * 100)}%, {Math.round(selectedRegion.y * 100)}%, {Math.round(selectedRegion.width * 100)}%w, {Math.round(selectedRegion.height * 100)}%h)
                </div>
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
