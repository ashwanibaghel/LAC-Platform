import React, { useState, useEffect, useRef, useCallback } from "react";
import * as pdfjsLib from "pdfjs-dist";
// @ts-ignore
import pdfjsWorker from "pdfjs-dist/build/pdf.worker.mjs?url";
import type { GreenSheetMetadataDto, ScreenOnlyGuideDto } from "./notingTypes";

if (typeof window !== "undefined" && pdfjsLib.GlobalWorkerOptions) {
  pdfjsLib.GlobalWorkerOptions.workerSrc = pdfjsWorker;
}

interface GreenSheetViewerProps {
  matterId: string;
  sheetId: string | null;
  metadata?: GreenSheetMetadataDto | null;
  onRefresh?: () => void;
  isLoading?: boolean;
}

export const GreenSheetViewer: React.FC<GreenSheetViewerProps> = ({
  matterId,
  sheetId,
  metadata,
  onRefresh,
  isLoading = false
}) => {
  const [pdfDoc, setPdfDoc] = useState<any>(null);
  const [pageCount, setPageCount] = useState<number>(0);
  const [scale, setScale] = useState<number>(0.95);
  const [loadingPdf, setLoadingPdf] = useState<boolean>(false);
  const [pdfError, setPdfError] = useState<string | null>(null);

  const containerRef = useRef<HTMLDivElement | null>(null);
  const canvasRefs = useRef<{ [page: number]: HTMLCanvasElement | null }>({});

  // Fetch and load PDF
  useEffect(() => {
    if (!sheetId) {
      setPdfDoc(null);
      setPageCount(0);
      return;
    }

    let active = true;
    setLoadingPdf(true);
    setPdfError(null);

    fetch(`/api/noting-green-sheets/${sheetId}/pdf`, {
      credentials: "include",
      headers: { "Cache-Control": "no-store" }
    })
      .then(async (res) => {
        if (!res.ok) {
          throw new Error(`Failed to stream Green Sheet PDF (${res.status})`);
        }
        return res.arrayBuffer();
      })
      .then(async (data) => {
        if (!active) return;
        const loadingTask = pdfjsLib.getDocument({
          data,
          cMapUrl: "https://unpkg.com/pdfjs-dist@6.4.299/cmaps/",
          cMapPacked: true
        });
        const doc = await loadingTask.promise;
        if (active) {
          setPdfDoc(doc);
          setPageCount(doc.numPages);
          setLoadingPdf(false);
        }
      })
      .catch((err) => {
        if (active) {
          setPdfError(err.message || "Failed to render Green Sheet PDF");
          setLoadingPdf(false);
        }
      });

    return () => {
      active = false;
    };
  }, [sheetId]);

  // Render individual page onto canvas
  const renderPage = useCallback(
    async (pageNumber: number, canvas: HTMLCanvasElement) => {
      if (!pdfDoc) return;
      try {
        const page = await pdfDoc.getPage(pageNumber);
        const viewport = page.getViewport({ scale });
        const ctx = canvas.getContext("2d");
        if (!ctx) return;

        // Support high-DPI displays
        const dpr = window.devicePixelRatio || 1;
        canvas.width = Math.floor(viewport.width * dpr);
        canvas.height = Math.floor(viewport.height * dpr);
        canvas.style.width = `${Math.floor(viewport.width)}px`;
        canvas.style.height = `${Math.floor(viewport.height)}px`;

        ctx.setTransform(dpr, 0, 0, dpr, 0, 0);

        const renderContext = {
          canvasContext: ctx,
          viewport: viewport
        };
        await page.render(renderContext).promise;
      } catch (err: any) {
        // Ignored if cancelled during zoom
      }
    },
    [pdfDoc, scale]
  );

  // Re-render all pages when pdfDoc or scale changes
  useEffect(() => {
    if (!pdfDoc) return;
    for (let p = 1; p <= pageCount; p++) {
      const c = canvasRefs.current[p];
      if (c) {
        renderPage(p, c);
      }
    }
  }, [pdfDoc, pageCount, scale, renderPage]);

  // Download / Clean Authoritative Print Handler
  const handlePrintCleanPdf = () => {
    if (!sheetId) return;
    const printUrl = `/api/noting-green-sheets/${sheetId}/pdf`;
    const win = window.open(printUrl, "_blank");
    if (!win) {
      // Fallback: trigger download
      const link = document.createElement("a");
      link.href = printUrl;
      link.download = `Green_Sheet_${matterId}.pdf`;
      link.click();
    }
  };

  const handleZoomIn = () => setScale((prev) => Math.min(2.0, Number((prev + 0.15).toFixed(2))));
  const handleZoomOut = () => setScale((prev) => Math.max(0.6, Number((prev - 0.15).toFixed(2))));
  const handleFitWidth = () => {
    if (!containerRef.current) return;
    const availableWidth = containerRef.current.clientWidth - 48; // padding
    // Legal width is 215.9mm = ~612pt in 72dpi. Standard PDF point width = 612
    const targetScale = Math.max(0.6, Math.min(1.8, availableWidth / 612));
    setScale(Number(targetScale.toFixed(2)));
  };

  // Helper to compute guide X position in pixels for page
  const getGuideXPixels = (pageNumber: number): number => {
    // If metadata contains explicit screenOnlyGuides, find matching page
    if (metadata?.screenOnlyGuides) {
      const guide = metadata.screenOnlyGuides.find((g: ScreenOnlyGuideDto) => g.page === pageNumber);
      if (guide) {
        // guide.xMm / guide.widthMm * renderedWidth
        const renderedWidth = 612 * scale;
        return (guide.xMm / guide.widthMm) * renderedWidth;
      }
    }
    // Standard rule: odd = 45mm inside margin, even = 170.9mm inside margin
    const renderedWidth = 612 * scale;
    const xMm = pageNumber % 2 === 1 ? 45.0 : 170.9;
    return (xMm / 215.9) * renderedWidth;
  };

  return (
    <div className="green-sheet-viewer-root" ref={containerRef}>
      {/* Top Controls Toolbar */}
      <div className="green-sheet-toolbar">
        <div className="green-sheet-toolbar-left">
          <span className="green-sheet-title-tag">
            <span className="green-legal-indicator-dot" />
            OFFICIAL GREEN LEGAL SHEET
          </span>
          {sheetId && (
            <span className="green-sheet-meta-pill">
              {pageCount > 0 ? `${pageCount} page${pageCount > 1 ? "s" : ""}` : "Loading..."} • Legal Portrait (215.9 × 355.6 mm)
            </span>
          )}
          {metadata?.pdfSha256 && (
            <span className="green-sheet-hash-pill" title={`SHA256: ${metadata.pdfSha256}`}>
              SHA: {metadata.pdfSha256.substring(0, 8)}…
            </span>
          )}
        </div>

        <div className="green-sheet-toolbar-right">
          <div className="green-sheet-zoom-group">
            <button
              type="button"
              className="noting-icon-btn"
              onClick={handleZoomOut}
              title="Zoom Out"
              disabled={scale <= 0.6}
            >
              −
            </button>
            <span className="zoom-level-text">{Math.round(scale * 100)}%</span>
            <button
              type="button"
              className="noting-icon-btn"
              onClick={handleZoomIn}
              title="Zoom In"
              disabled={scale >= 2.0}
            >
              +
            </button>
            <button
              type="button"
              className="noting-text-btn"
              onClick={handleFitWidth}
              title="Fit to Width"
            >
              Fit Width
            </button>
          </div>

          {onRefresh && (
            <button
              type="button"
              className="noting-refresh-btn"
              onClick={onRefresh}
              disabled={isLoading || loadingPdf}
              title="Refresh Continuous File"
            >
              ↻ Refresh
            </button>
          )}

          {sheetId && (
            <button
              type="button"
              className="noting-print-btn"
              onClick={handlePrintCleanPdf}
              title="Print Clean Legal PDF (Actual Size 100% without screen guides)"
            >
              🖨 Print / Download PDF
            </button>
          )}
        </div>
      </div>

      {/* Main Document Scrollport */}
      <div className="green-sheet-scrollport">
        {loadingPdf || isLoading ? (
          <div className="green-sheet-loading-state">
            <div className="noting-spinner" />
            <p>Rendering official continuous Green Legal file…</p>
          </div>
        ) : pdfError ? (
          <div className="green-sheet-error-state">
            <div className="error-icon">⚠️</div>
            <h4>Failed to load Green Sheet</h4>
            <p>{pdfError}</p>
            {onRefresh && (
              <button type="button" className="noting-btn-retry" onClick={onRefresh}>
                Retry
              </button>
            )}
          </div>
        ) : sheetId && pageCount > 0 ? (
          <div className="green-sheet-pages-container">
            {Array.from({ length: pageCount }, (_, i) => i + 1).map((pNum) => {
              const guideX = getGuideXPixels(pNum);
              return (
                <div key={pNum} className="green-legal-page-card">
                  {/* Pale Green Sheet Canvas Container */}
                  <div className="green-page-canvas-wrapper" style={{ backgroundColor: "#edf7f0" }}>
                    <canvas
                      ref={(el) => {
                        canvasRefs.current[pNum] = el;
                        if (el && pdfDoc) {
                          renderPage(pNum, el);
                        }
                      }}
                      className="green-sheet-pdf-canvas"
                    />

                    {/* Alternating Screen-Only Red Margin Guide Line (Odd: 45mm, Even: 170.9mm) */}
                    <div
                      className="screen-only-red-margin-guide"
                      style={{
                        left: `${guideX}px`
                      }}
                      title={`Screen-only margin guide (Page ${pNum}: ${pNum % 2 === 1 ? "45mm" : "170.9mm"})`}
                    />

                    {/* Screen-only watermarked page badge */}
                    <div className="screen-only-page-indicator">
                      P. {pNum}
                    </div>
                  </div>

                  <div className="page-card-subtext">
                    Page {pNum} of {pageCount} • Legal Mirror Margin ({pNum % 2 === 1 ? "Odd / Inside 45mm" : "Even / Inside 170.9mm"})
                  </div>
                </div>
              );
            })}
          </div>
        ) : (
          /* Empty Sheet State: Official Legal Sheet Template Placeholder */
          <div className="green-sheet-empty-container">
            <div className="green-legal-blank-sheet" style={{ width: `${Math.round(612 * scale)}px`, minHeight: `${Math.round(1008 * scale)}px` }}>
              {/* Screen-only red margin guide line on the blank sheet at 45mm */}
              <div
                className="screen-only-red-margin-guide"
                style={{
                  left: `${(45.0 / 215.9) * 612 * scale}px`
                }}
              />

              <div className="blank-sheet-inner-content" style={{ marginLeft: `${(50.0 / 215.9) * 612 * scale}px` }}>
                <div className="official-crest-header">
                  <div className="crest-title">GOVERNMENT OF NCT OF DELHI</div>
                  <div className="crest-subtitle">OFFICE OF THE LAND ACQUISITION COLLECTOR</div>
                  <div className="crest-file-banner">OFFICIAL NOTE SHEET — CONTINUOUS LEGAL FILE</div>
                </div>

                <div className="blank-sheet-notice">
                  <div className="notice-icon">📋</div>
                  <h4>No official notes submitted yet</h4>
                  <p>
                    This is the permanent continuous Green Legal file for this Matter. When Note #1 is authored and submitted, its verified text, signatures, and legal reservations will appear here.
                  </p>
                  <p className="sub-instruction">
                    Use the <strong>Note Editor</strong> on the right to compose your draft.
                  </p>
                </div>
              </div>
            </div>
          </div>
        )}
      </div>
    </div>
  );
};
