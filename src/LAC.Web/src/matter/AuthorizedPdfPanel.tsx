import { useEffect, useRef, useState } from "react";
import * as pdfjs from "pdfjs-dist";
import worker from "pdfjs-dist/build/pdf.worker.mjs?url";
import type { ScreenOnlyGuideDto } from "./notingTypes";
pdfjs.GlobalWorkerOptions.workerSrc = worker;
const noGuides: ScreenOnlyGuideDto[] = [];
export function AuthorizedPdfPanel({ url, title, guides = noGuides }: {
    url: string;
    title: string;
    guides?: ScreenOnlyGuideDto[];
}) {
    const [document, setDocument] = useState<pdfjs.PDFDocumentProxy | null>(null);
    const [page, setPage] = useState(1);
    const [zoom, setZoom] = useState(75);
    const [error, setError] = useState("");
    const [geometry, setGeometry] = useState<{
        width: number;
        height: number;
        guideX?: number;
    } | null>(null);
    const canvas = useRef<HTMLCanvasElement>(null);
    useEffect(() => {
        const abort = new AbortController();
        let task: ReturnType<typeof pdfjs.getDocument> | undefined;
        setDocument(null);
        setGeometry(null);
        setError("");
        setPage(1);
        void (async () => {
            try {
                // Same-origin authenticated bytes only; never render an invented source or external URL.
                const target = new URL(url, window.location.origin);
                if (target.origin !== window.location.origin || !target.pathname.startsWith("/api/"))
                    throw new Error("PDF must use an authorized application endpoint.");
                const response = await fetch(target, { credentials: "include", cache: "no-store", signal: abort.signal });
                if (!response.ok)
                    throw new Error(`PDF unavailable (${response.status}).`);
                if (!(response.headers.get("content-type") || "").includes("application/pdf"))
                    throw new Error("Source is not a PDF.");
                task = pdfjs.getDocument({ data: await response.arrayBuffer() });
                const pdf = await task.promise;
                if (!abort.signal.aborted)
                    setDocument(pdf);
            }
            catch (e) {
                if (!abort.signal.aborted)
                    setError((e as Error).message);
            }
        })();
        return () => { abort.abort(); void task?.destroy(); };
    }, [url]);
    useEffect(() => {
        let cancelled = false;
        let render: pdfjs.RenderTask | undefined;
        setGeometry(null);
        void (async () => {
            try {
                if (!document || !canvas.current)
                    return;
                const p = await document.getPage(page);
                if (cancelled || !canvas.current)
                    return;
                const view = p.getViewport({ scale: zoom / 100 });
                const dpr = window.devicePixelRatio || 1;
                const element = canvas.current;
                element.width = Math.ceil(view.width * dpr);
                element.height = Math.ceil(view.height * dpr);
                element.style.width = `${view.width}px`;
                element.style.height = `${view.height}px`;
                const guide = guides.find(g => g.page === page && g.print === false);
                let guideX: number | undefined;
                if (guide) {
                    const base = p.getViewport({ scale: 1, rotation: 0 });
                    if (p.rotate !== 0 || Math.abs(base.width - 612) > 0.05 || Math.abs(base.height - 1008) > 0.05 || guide.widthMm !== 215.9 || guide.heightMm !== 355.6 || guide.color !== "red" || Math.abs(guide.xMm - (page % 2 ? 45 : 170.9)) > 0.000001 || !Number.isFinite(guide.xMm))
                        throw new Error("Guide requires verified portrait Legal PDF geometry and alternating 45 mm boundaries.");
                    guideX = view.convertToViewportPoint(p.view[0] + guide.xMm * 72 / 25.4, p.view[3])[0];
                }
                setGeometry({ width: view.width, height: view.height, guideX });
                render = p.render({ canvas: element, viewport: view, transform: [dpr, 0, 0, dpr, 0, 0] });
                await render.promise;
            }
            catch (e) {
                if (!cancelled)
                    setError((e as Error).message);
            }
        })();
        return () => { cancelled = true; render?.cancel(); };
    }, [document, page, zoom, guides]);
    return <section className="v4-pdf-panel" aria-label={title}>
    <div className="v4-pdf-tools"><strong title={title}>{title}</strong>
      <button disabled={!document || page === 1} onClick={() => setPage(p => p - 1)} aria-label="Previous PDF page">‹</button>
      <label>Page <input aria-label="PDF page" type="number" min="1" max={document?.numPages || 1} value={page} onChange={e => setPage(Math.max(1, Math.min(document?.numPages || 1, Number(e.target.value) || 1)))}/></label><span>/ {document?.numPages || "…"}</span>
      <label>Zoom <select aria-label="PDF zoom" value={zoom} onChange={e => setZoom(Number(e.target.value))}>{[50, 65, 75, 90, 100, 125, 150].map(v => <option key={v} value={v}>{v}%</option>)}</select></label>
      <button disabled={!document || page === document.numPages} onClick={() => setPage(p => p + 1)} aria-label="Next PDF page">›</button>
      <a href={url} target="_blank" rel="noopener" title={guides.length ? "Print Legal portrait at actual size (100%)" : "Open original authorized PDF"}>{guides.length ? "Clean PDF / Print" : "Open PDF"}</a>
    </div>
    {error && <p role="alert" className="v4-error">{error}</p>}
    {guides.length>0 && <div className="v4-print-note">Print Legal portrait at actual size (100%). Guides and green background are screen only.</div>}
    {!document && !error && <p role="status">Loading authorized PDF…</p>}
    <div className="v4-pdf-scroll"><div className={`v4-pdf-paper ${guides.length ? "v4-guided-paper" : ""}`} style={{ width: geometry?.width, height: geometry?.height }}>
      <canvas ref={canvas} aria-label={`${title}, page ${page}`}/>
      {geometry?.guideX !== undefined && <div className="v4-screen-guide" aria-hidden="true" style={{ left: geometry.guideX }}/>}
    </div></div>
  </section>;
}
