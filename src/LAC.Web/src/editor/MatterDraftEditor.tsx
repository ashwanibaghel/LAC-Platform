import { useEffect, useMemo, useRef, useState } from "react";
import type { CSSProperties, MouseEvent, ReactNode } from "react";
import { EditorContent, useEditor } from "@tiptap/react";
import { Slice, Fragment } from "@tiptap/pm/model";
import StarterKit from "@tiptap/starter-kit";
import Underline from "@tiptap/extension-underline";
import { TextStyle } from "@tiptap/extension-text-style";
import Color from "@tiptap/extension-color";
import FontFamily from "@tiptap/extension-font-family";
import TextAlign from "@tiptap/extension-text-align";
import { Table } from "@tiptap/extension-table";
import TableRow from "@tiptap/extension-table-row";
import TableCell from "@tiptap/extension-table-cell";
import TableHeader from "@tiptap/extension-table-header";
import { Link, useNavigate, useParams } from "react-router-dom";
import { useAuth } from "../auth/AuthProvider";
import { EditorToolbar } from "./EditorToolbar";
import { matterDraftPagination, normalizeDraftPages, paginationPluginKey } from "./pagination";
import { DELHI_LAC_NOTING_V1, computeSheetGeometry, getPageTopPx, getPrintableBottomPx, getPrintableTopPx, profilePrintSize, resolvePageProfile } from "./pageProfiles";
import "./matter-editor.css";

const api = "/api";
const emptyDocument = { type: "doc", content: [{ type: "paragraph" }] };
type Draft = { id: string; matterId: string; matterTitle: string; title: string; draftType: string; contentJson: string; revision: number; pageSize: string; orientation: "Portrait" | "Landscape"; marginTopMm: number; marginRightMm: number; marginBottomMm: number; marginLeftMm: number; updatedAt: string };
type Layout = Pick<Draft, "pageSize" | "orientation" | "marginTopMm" | "marginRightMm" | "marginBottomMm" | "marginLeftMm">;

const FontSize = TextStyle.extend({
  addGlobalAttributes() {
    return [{
      types: ["textStyle"],
      attributes: {
        fontSize: {
          default: null,
          parseHTML: (element: HTMLElement) => element.style.fontSize || null,
          renderHTML: (attributes: Record<string, string | null>) => attributes.fontSize ? { style: `font-size: ${attributes.fontSize}` } : {}
        }
      }
    }];
  }
});

async function request<T>(path: string, init?: RequestInit): Promise<T> {
  const response = await fetch(api + path, init);
  if (!response.ok) {
    const body = await response.json().catch(() => null);
    throw new Error(body?.detail || body?.title || "Could not save the draft.");
  }
  return response.status === 204 ? {} as T : response.json() as Promise<T>;
}
const displayDate = (value: string) => new Date(value).toLocaleDateString(undefined, { day: "numeric", month: "short", year: "numeric" });

function IconFileText() {
  return (
    <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
      <path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z" />
      <polyline points="14 2 14 8 20 8" />
      <line x1="16" y1="13" x2="8" y2="13" />
      <line x1="16" y1="17" x2="8" y2="17" />
      <polyline points="10 9 9 9 8 9" />
    </svg>
  );
}

function IconFeather() {
  return (
    <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
      <path d="M20.24 12.24a6 6 0 0 0-8.49-8.49L3 13v5h5l12.24-12.24z" />
      <line x1="16" y1="8" x2="2" y2="22" />
      <line x1="17.5" y1="15" x2="9" y2="15" />
    </svg>
  );
}

function IconSearch() {
  return (
    <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
      <circle cx="11" cy="11" r="8" />
      <line x1="21" y1="21" x2="16.65" y2="16.65" />
    </svg>
  );
}

function IconPlus() {
  return (
    <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.2" strokeLinecap="round" strokeLinejoin="round">
      <line x1="12" y1="5" x2="12" y2="19" />
      <line x1="5" y1="12" x2="19" y2="12" />
    </svg>
  );
}

function IconArrowRight() {
  return (
    <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.2" strokeLinecap="round" strokeLinejoin="round">
      <line x1="5" y1="12" x2="19" y2="12" />
      <polyline points="12 5 19 12 12 19" />
    </svg>
  );
}

function IconSparkles() {
  return (
    <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
      <path d="m12 3-1.912 5.813a2 2 0 0 1-1.275 1.275L3 12l5.813 1.912a2 2 0 0 1 1.275 1.275L12 21l1.912-5.813a2 2 0 0 1 1.275-1.275L21 12l-5.813-1.912a2 2 0 0 1-1.275-1.275L12 3z" />
    </svg>
  );
}

export function MatterDrafts({ matterId }: { matterId: string }) {
  const navigate = useNavigate();
  const { hasPermission } = useAuth();
  const canCreateDraft = hasPermission("Draft.Create");
  const [drafts, setDrafts] = useState<Array<Pick<Draft, "id" | "title" | "draftType" | "updatedAt">>>([]);
  const [loading, setLoading] = useState(true);
  const [creating, setCreating] = useState(false);
  const [title, setTitle] = useState("");
  const [searchQuery, setSearchQuery] = useState("");
  const [filterType, setFilterType] = useState<"All" | "Letter" | "Noting">("All");
  const [error, setError] = useState("");

  useEffect(() => {
    setLoading(true);
    request<typeof drafts>(`/matters/${matterId}/drafts`)
      .then(res => {
        setDrafts(res);
        setLoading(false);
      })
      .catch(e => {
        setError(e.message);
        setLoading(false);
      });
  }, [matterId]);

  const create = async (draftType: string) => {
    const finalTitle = title.trim();
    if (!finalTitle || creating) return;
    setCreating(true);
    setError("");
    try {
      const result = await request<{ id: string }>(`/matters/${matterId}/drafts`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ title: finalTitle, draftType })
      });
      navigate(`/matter-drafts/${result.id}`);
    } catch (e) {
      setError(e instanceof Error ? e.message : "Could not create draft.");
      setCreating(false);
    }
  };

  const letterCount = useMemo(() => drafts.filter(d => d.draftType === "Letter").length, [drafts]);
  const notingCount = useMemo(() => drafts.filter(d => d.draftType === "Noting").length, [drafts]);

  const filteredDrafts = useMemo(() => {
    return drafts.filter(d => {
      const matchesType = filterType === "All" || d.draftType === filterType;
      const matchesSearch = !searchQuery.trim() || d.title.toLowerCase().includes(searchQuery.toLowerCase());
      return matchesType && matchesSearch;
    });
  }, [drafts, filterType, searchQuery]);

  return (
    <div className="matter-drafts-workspace">
      {/* Header & Creation Bar */}
      <div className="drafts-clean-header">
        <div className="drafts-clean-intro">
          <h2 className="drafts-clean-title">Matter Drafts & Office Notes</h2>
          <p className="drafts-clean-subtitle">
            Create, edit, and print official letters and green office noting sheets for this matter.
          </p>
        </div>

        {canCreateDraft && (
          <div className="draft-create-bar">
            <div className="create-input-wrapper">
              <IconFileText />
              <input
                aria-label="Draft document title"
                placeholder="Enter document title (e.g. ADM Reply Letter dt 25-Sep-2026)..."
                value={title}
                onChange={e => setTitle(e.target.value)}
                onKeyDown={e => {
                  if (e.key === "Enter" && title.trim()) {
                    void create("Letter");
                  }
                }}
              />
            </div>
            <div className="create-buttons">
              <button
                type="button"
                className="btn-create-letter"
                onClick={() => void create("Letter")}
                disabled={!title.trim() || creating}
              >
                <IconPlus />
                <span>+ Create Letter</span>
              </button>
              <button
                type="button"
                className="btn-create-noting"
                onClick={() => void create("Noting")}
                disabled={!title.trim() || creating}
              >
                <IconPlus />
                <span>+ Create Noting Sheet</span>
              </button>
            </div>
          </div>
        )}
      </div>

      {error && <div className="drafts-error-banner"><p>{error}</p></div>}

      {/* Filter and Search Bar */}
      <div className="drafts-toolbar">
        <div className="drafts-filter-tabs">
          <button
            type="button"
            className={`filter-tab ${filterType === "All" ? "active" : ""}`}
            onClick={() => setFilterType("All")}
          >
            All Drafts ({drafts.length})
          </button>
          <button
            type="button"
            className={`filter-tab ${filterType === "Letter" ? "active" : ""}`}
            onClick={() => setFilterType("Letter")}
          >
            Letters ({letterCount})
          </button>
          <button
            type="button"
            className={`filter-tab ${filterType === "Noting" ? "active" : ""}`}
            onClick={() => setFilterType("Noting")}
          >
            Noting Sheets ({notingCount})
          </button>
        </div>

        <div className="drafts-search-box">
          <IconSearch />
          <input
            type="text"
            placeholder="Search by title..."
            value={searchQuery}
            onChange={e => setSearchQuery(e.target.value)}
          />
        </div>
      </div>

      {/* Draft Cards Grid */}
      {loading ? (
        <div className="drafts-loading-state">
          <div className="spinner" />
          <p>Loading document drafts…</p>
        </div>
      ) : filteredDrafts.length === 0 ? (
        <div className="drafts-empty-state">
          <div className="empty-icon"><IconFileText /></div>
          <h3>No legal drafts found</h3>
          <p>
            {searchQuery
              ? `No drafts matching "${searchQuery}". Try changing your search.`
              : filterType !== "All"
              ? `No ${filterType.toLowerCase()} drafts created yet.`
              : "No letters or noting sheets created for this matter yet. Click a template above to start."}
          </p>
        </div>
      ) : (
        <div className="drafts-grid">
          {filteredDrafts.map(draft => (
            <div key={draft.id} className={`draft-card card-type-${draft.draftType.toLowerCase()}`}>
              <div className="draft-card-top">
                <span className={`draft-type-tag tag-${draft.draftType.toLowerCase()}`}>
                  {draft.draftType === "Noting" ? <IconFeather /> : <IconFileText />}
                  <span>{draft.draftType === "Noting" ? "Green Noting Sheet" : "Formal Letter"}</span>
                </span>
                <span className="draft-time-tag">Updated {displayDate(draft.updatedAt)}</span>
              </div>

              <h4 className="draft-card-title">{draft.title}</h4>

              <div className="draft-card-meta">
                <span className="meta-item">
                  <span className="meta-label">Layout:</span>
                  <span className="meta-value">{draft.draftType === "Noting" ? "A4 (Fixed 25mm Margin)" : "Standard (A4 / Legal)"}</span>
                </span>
              </div>

              <div className="draft-card-footer">
                <Link className="btn-open-draft" to={`/matter-drafts/${draft.id}`}>
                  <span>Open in Editor Studio</span>
                  <IconArrowRight />
                </Link>
              </div>
            </div>
          ))}
        </div>
      )}
    </div>
  );
}

const CustomTable = Table.extend({
  addAttributes() {
    return {
      ...this.parent?.(),
      tableStyle: {
        default: "table-style-legal",
        parseHTML: element => element.getAttribute("data-table-style") || "table-style-legal",
        renderHTML: attributes => ({
          "data-table-style": attributes.tableStyle || "table-style-legal",
          class: `draft-table ${attributes.tableStyle || "table-style-legal"}`
        })
      }
    };
  }
});

function TopRulerBar({
  profile,
  showRulers,
  geometry,
  onMarginChange,
}: {
  profile: ReturnType<typeof resolvePageProfile>;
  showRulers: boolean;
  geometry: { viewportWidth: number; canvasLeft: number; canvasWidth: number };
  onMarginChange?: (partial: Partial<Layout>) => void;
}) {
  const [activeDrag, setActiveDrag] = useState<"left" | "right" | null>(null);
  const [guideValue, setGuideValue] = useState<number | null>(null);

  if (!showRulers) return null;

  const widthMm = profile.widthMm;
  const leftMargin = profile.marginLeftMm;
  const rightMargin = profile.marginRightMm;
  const isLocked = profile.locked;

  const pxPerMm = geometry.canvasWidth / widthMm;
  const leftMarginPx = geometry.canvasLeft + leftMargin * pxPerMm;
  const rightMarginPx = geometry.canvasLeft + (widthMm - rightMargin) * pxPerMm;

  // Horizontal Ticks (0 at left margin line)
  const topTicks: Array<{ xPx: number; isMajor: boolean; isMid: boolean; label?: number }> = [];
  for (let mm = 0; mm <= widthMm; mm += 1) {
    const distFromLeft = mm - leftMargin;
    const isMajor = Math.abs(distFromLeft) % 10 === 0;
    const isMid = Math.abs(distFromLeft) % 5 === 0 && !isMajor;
    if (isMajor || isMid) {
      let label: number | undefined = undefined;
      if (isMajor && mm !== leftMargin && mm !== widthMm - rightMargin) {
        if (mm < leftMargin) {
          label = Math.round((leftMargin - mm) / 10);
        } else {
          label = Math.round((mm - leftMargin) / 10);
        }
      }
      const xPx = geometry.canvasLeft + mm * pxPerMm;
      topTicks.push({ xPx, isMajor, isMid, label });
    }
  }

  const startDrag = (e: React.MouseEvent, target: "left" | "right") => {
    if (isLocked || !onMarginChange) return;
    e.preventDefault();
    e.stopPropagation();

    setActiveDrag(target);
    const startX = e.clientX;
    const initialLeft = leftMargin;
    const initialRight = rightMargin;

    const handleMouseMove = (moveEvent: MouseEvent) => {
      const deltaX = (moveEvent.clientX - startX) / pxPerMm;
      if (target === "left") {
        const val = Math.max(5, Math.min(widthMm - initialRight - 20, Math.round(initialLeft + deltaX)));
        setGuideValue(val);
        onMarginChange({ marginLeftMm: val });
      } else if (target === "right") {
        const val = Math.max(5, Math.min(widthMm - initialLeft - 20, Math.round(initialRight - deltaX)));
        setGuideValue(val);
        onMarginChange({ marginRightMm: val });
      }
    };

    const handleMouseUp = () => {
      setActiveDrag(null);
      setGuideValue(null);
      window.removeEventListener("mousemove", handleMouseMove);
      window.removeEventListener("mouseup", handleMouseUp);
    };

    window.addEventListener("mousemove", handleMouseMove);
    window.addEventListener("mouseup", handleMouseUp);
  };

  return (
    <div className="draft-top-ruler-bar" aria-hidden="true">
      <svg
        className="ruler-svg"
        viewBox={`0 0 ${geometry.viewportWidth} 20`}
        preserveAspectRatio="none"
      >
        {/* Gray Track Background */}
        <rect x={0} y={0} width={geometry.viewportWidth} height={20} fill="#e2e4e7" />

        {/* White Printable Track */}
        <rect
          x={leftMarginPx}
          y={0}
          width={Math.max(0, rightMarginPx - leftMarginPx)}
          height={20}
          fill="#ffffff"
        />

        {/* Top-Left Corner Square */}
        <rect x={0} y={0} width={20} height={20} fill="#e2e4e7" />
        <line x1={20} y1={0} x2={20} y2={20} stroke="#cbd5e1" strokeWidth="1" />

        {/* Hairline Borders */}
        <line x1={leftMarginPx} y1={0} x2={leftMarginPx} y2={20} stroke="#a8acb0" strokeWidth="1" />
        <line x1={rightMarginPx} y1={0} x2={rightMarginPx} y2={20} stroke="#a8acb0" strokeWidth="1" />
        <line x1={0} y1={19.5} x2={geometry.viewportWidth} y2={19.5} stroke="#b8bcbe" strokeWidth="1" />

        {/* Ticks & Labels */}
        {topTicks.map(t => (
          <g key={`top-tick-${t.xPx}`}>
            <line
              x1={t.xPx}
              y1={t.isMajor ? 11 : 15}
              x2={t.xPx}
              y2={20}
              stroke={t.isMajor ? "#5f6368" : "#9aa0a6"}
              strokeWidth={t.isMajor ? "1" : "0.75"}
            />
            {t.label !== undefined && (
              <text
                x={t.xPx}
                y={8}
                fontSize="10"
                fontFamily="Segoe UI, Calibri, sans-serif"
                fontWeight="600"
                fill="#3c4043"
                textAnchor="middle"
              >
                {t.label}
              </text>
            )}
          </g>
        ))}

        {/* Left Margin Drag Handle */}
        <g
          className={`ruler-handle-group ${isLocked ? "locked" : "draggable"}`}
          onMouseDown={e => startDrag(e, "left")}
          style={{ cursor: isLocked ? "not-allowed" : "col-resize" }}
        >
          <polygon
            points={`${leftMarginPx - 4.5},1 ${leftMarginPx + 4.5},1 ${leftMarginPx},6.5`}
            fill={activeDrag === "left" ? "#2563eb" : "#5f6368"}
            stroke="#3c4043"
            strokeWidth="0.8"
          />
          <polygon
            points={`${leftMarginPx - 4.5},19.5 ${leftMarginPx + 4.5},19.5 ${leftMarginPx},14`}
            fill={activeDrag === "left" ? "#2563eb" : "#5f6368"}
            stroke="#3c4043"
            strokeWidth="0.8"
          />
        </g>

        {/* Right Margin Drag Handle */}
        <g
          className={`ruler-handle-group ${isLocked ? "locked" : "draggable"}`}
          onMouseDown={e => startDrag(e, "right")}
          style={{ cursor: isLocked ? "not-allowed" : "col-resize" }}
        >
          <polygon
            points={`${rightMarginPx - 4.5},19.5 ${rightMarginPx + 4.5},19.5 ${rightMarginPx},14`}
            fill={activeDrag === "right" ? "#2563eb" : "#5f6368"}
            stroke="#3c4043"
            strokeWidth="0.8"
          />
        </g>
      </svg>

      {/* Live Guidelines during drag */}
      {activeDrag === "left" && (
        <div className="ruler-live-guideline vertical" style={{ left: `${leftMarginPx}px` }}>
          <span className="ruler-guide-badge">Left Margin: {guideValue ?? leftMargin} mm</span>
        </div>
      )}
      {activeDrag === "right" && (
        <div className="ruler-live-guideline vertical" style={{ left: `${rightMarginPx}px` }}>
          <span className="ruler-guide-badge">Right Margin: {guideValue ?? rightMargin} mm</span>
        </div>
      )}
    </div>
  );
}

function LeftRulerBar({
  profile,
  showRulers,
  geometry,
  activePageIndex,
  onMarginChange,
}: {
  profile: ReturnType<typeof resolvePageProfile>;
  showRulers: boolean;
  geometry: { viewportHeight: number; canvasTop: number; canvasWidth: number; zoom: number };
  activePageIndex: number;
  onMarginChange?: (partial: Partial<Layout>) => void;
}) {
  const [activeDrag, setActiveDrag] = useState<"top" | "bottom" | null>(null);
  const [guideValue, setGuideValue] = useState<number | null>(null);

  if (!showRulers) return null;

  const heightMm = profile.heightMm;
  const widthMm = profile.widthMm;
  const topMargin = profile.marginTopMm + (profile.reservedTopMm ?? 0);
  const bottomMargin = profile.marginBottomMm;
  const isLocked = profile.locked;

  const geom = computeSheetGeometry(profile);
  const scale = geometry.zoom || 1;

  // Track active page top origin in screen coordinates
  const activePageTopPx = geometry.canvasTop + activePageIndex * geom.pageStridePx * scale;
  const topMarginPx = activePageTopPx + topMargin * geom.pxPerMm * scale;
  const bottomMarginPx = activePageTopPx + (heightMm - bottomMargin) * geom.pxPerMm * scale;

  // Vertical Ticks (0 at top margin line of active page)
  const leftTicks: Array<{ yPx: number; isMajor: boolean; isMid: boolean; label?: number }> = [];
  for (let mm = 0; mm <= heightMm; mm += 1) {
    const distFromTop = mm - topMargin;
    const isMajor = Math.abs(distFromTop) % 10 === 0;
    const isMid = Math.abs(distFromTop) % 5 === 0 && !isMajor;
    if (isMajor || isMid) {
      let label: number | undefined = undefined;
      if (isMajor && mm !== topMargin && mm !== heightMm - bottomMargin) {
        if (mm < topMargin) {
          label = Math.round((topMargin - mm) / 10);
        } else {
          label = Math.round((mm - topMargin) / 10);
        }
      }
      const yPx = activePageTopPx + mm * geom.pxPerMm * scale;
      leftTicks.push({ yPx, isMajor, isMid, label });
    }
  }

  const startDrag = (e: React.MouseEvent, target: "top" | "bottom") => {
    if (isLocked || !onMarginChange) return;
    e.preventDefault();
    e.stopPropagation();

    setActiveDrag(target);
    const startY = e.clientY;
    const initialTop = topMargin;
    const initialBottom = bottomMargin;
    const pxPerMm = geom.pxPerMm * scale;

    const handleMouseMove = (moveEvent: MouseEvent) => {
      const deltaY = (moveEvent.clientY - startY) / pxPerMm;
      if (target === "top") {
        const val = Math.max(5, Math.min(heightMm - initialBottom - 20, Math.round(initialTop + deltaY)));
        setGuideValue(val);
        onMarginChange({ marginTopMm: val });
      } else if (target === "bottom") {
        const val = Math.max(5, Math.min(heightMm - initialTop - 20, Math.round(initialBottom - deltaY)));
        setGuideValue(val);
        onMarginChange({ marginBottomMm: val });
      }
    };

    const handleMouseUp = () => {
      setActiveDrag(null);
      setGuideValue(null);
      window.removeEventListener("mousemove", handleMouseMove);
      window.removeEventListener("mouseup", handleMouseUp);
    };

    window.addEventListener("mousemove", handleMouseMove);
    window.addEventListener("mouseup", handleMouseUp);
  };

  return (
    <div className="draft-left-ruler-bar" aria-hidden="true">
      <svg
        className="ruler-svg"
        viewBox={`0 0 20 ${geometry.viewportHeight}`}
        preserveAspectRatio="none"
      >
        {/* Gray Track Background */}
        <rect x={0} y={0} width={20} height={geometry.viewportHeight} fill="#e2e4e7" />

        {/* White Printable Track */}
        <rect
          x={0}
          y={topMarginPx}
          width={20}
          height={Math.max(0, bottomMarginPx - topMarginPx)}
          fill="#ffffff"
        />

        {/* Track Hairline Borders */}
        <line x1={0} y1={topMarginPx} x2={20} y2={topMarginPx} stroke="#a8acb0" strokeWidth="1" />
        <line x1={0} y1={bottomMarginPx} x2={20} y2={bottomMarginPx} stroke="#a8acb0" strokeWidth="1" />
        <line x1={19.5} y1={0} x2={19.5} y2={geometry.viewportHeight} stroke="#b8bcbe" strokeWidth="1" />

        {/* Ticks & Labels */}
        {leftTicks.map(t => (
          <g key={`left-tick-${t.yPx}`}>
            <line
              x1={t.isMajor ? 10 : 14}
              y1={t.yPx}
              x2={20}
              y2={t.yPx}
              stroke={t.isMajor ? "#5f6368" : "#9aa0a6"}
              strokeWidth={t.isMajor ? "1" : "0.75"}
            />
            {t.label !== undefined && (
              <text
                x={7}
                y={t.yPx}
                fontSize="9.5"
                fontFamily="Segoe UI, Calibri, sans-serif"
                fontWeight="600"
                fill="#3c4043"
                textAnchor="end"
                dominantBaseline="central"
              >
                {t.label}
              </text>
            )}
          </g>
        ))}

        {/* Top Margin Drag Handle */}
        <g
          className={`ruler-handle-group ${isLocked ? "locked" : "draggable"}`}
          onMouseDown={e => startDrag(e, "top")}
          style={{ cursor: isLocked ? "not-allowed" : "row-resize" }}
        >
          <polygon
            points={`19.5,${topMarginPx - 4} 19.5,${topMarginPx + 4} 10.5,${topMarginPx}`}
            fill={activeDrag === "top" ? "#2563eb" : "#5f6368"}
            stroke="#3c4043"
            strokeWidth="0.8"
          />
        </g>

        {/* Bottom Margin Drag Handle */}
        <g
          className={`ruler-handle-group ${isLocked ? "locked" : "draggable"}`}
          onMouseDown={e => startDrag(e, "bottom")}
          style={{ cursor: isLocked ? "not-allowed" : "row-resize" }}
        >
          <polygon
            points={`19.5,${bottomMarginPx - 4} 19.5,${bottomMarginPx + 4} 10.5,${bottomMarginPx}`}
            fill={activeDrag === "bottom" ? "#2563eb" : "#5f6368"}
            stroke="#3c4043"
            strokeWidth="0.8"
          />
        </g>
      </svg>

      {/* Live Guidelines during drag */}
      {activeDrag === "top" && (
        <div className="ruler-live-guideline horizontal" style={{ top: `${topMarginPx}px` }}>
          <span className="ruler-guide-badge">Top Margin: {guideValue ?? topMargin} mm</span>
        </div>
      )}
      {activeDrag === "bottom" && (
        <div className="ruler-live-guideline horizontal" style={{ top: `${bottomMarginPx}px` }}>
          <span className="ruler-guide-badge">Bottom Margin: {guideValue ?? bottomMargin} mm</span>
        </div>
      )}
    </div>
  );
}

function MatterDraftCanvas({
  draft,
  contentJson,
  zoom,
  onChange,
  onMarginChange,
  onPrint,
  pageSetup,
  pageSetupExpanded,
  onTogglePageSetup,
  onZoomChange,
  onSetZoom,
  onResetZoom,
}: {
  draft: Draft;
  contentJson: string;
  zoom: number;
  onChange: (contentJson: string) => void;
  onMarginChange?: (partial: Partial<Layout>) => void;
  onPrint: () => void;
  pageSetup: ReactNode;
  pageSetupExpanded: boolean;
  onTogglePageSetup: () => void;
  onZoomChange: (delta: number) => void;
  onSetZoom?: (zoom: number) => void;
  onResetZoom: () => void;
}) {
  const profile = resolvePageProfile(draft);
  const [pageCount, setPageCount] = useState(1);
  const [showRulers, setShowRulers] = useState(true);

  const pageWrapRef = useRef<HTMLDivElement>(null);
  const canvasRef = useRef<HTMLDivElement>(null);

  const profileRef = useRef(profile);
  profileRef.current = profile;
  const zoomRef = useRef(zoom);
  const prevZoomRef = useRef(zoom);
  zoomRef.current = zoom;

  // ── Single Geometry Calculation Source ────────────────────────────
  const [geometry, setGeometry] = useState({
    viewportWidth: 1200,
    viewportHeight: 800,
    canvasLeft: 200,
    canvasTop: 36,
    canvasWidth: 793,
    canvasHeight: 1122,
    zoom: 1,
  });

  const updateGeometry = useMemo(() => {
    return () => {
      if (!pageWrapRef.current || !canvasRef.current) return;
      const wrapRect = pageWrapRef.current.getBoundingClientRect();
      const canvasRect = canvasRef.current.getBoundingClientRect();

      setGeometry({
        viewportWidth: Math.max(200, wrapRect.width),
        viewportHeight: Math.max(200, wrapRect.height),
        canvasLeft: canvasRect.left - wrapRect.left,
        canvasTop: canvasRect.top - wrapRect.top,
        canvasWidth: canvasRect.width,
        canvasHeight: canvasRect.height,
        zoom: zoomRef.current,
      });
    };
  }, []);

  useEffect(() => {
    let animationFrameId = 0;
    const scheduleUpdate = () => {
      if (animationFrameId) cancelAnimationFrame(animationFrameId);
      animationFrameId = requestAnimationFrame(updateGeometry);
    };

    scheduleUpdate();

    const wrapEl = pageWrapRef.current;
    if (wrapEl) {
      wrapEl.addEventListener("scroll", scheduleUpdate, { passive: true });
    }
    window.addEventListener("resize", scheduleUpdate);

    let observer: ResizeObserver | null = null;
    if (typeof ResizeObserver !== "undefined") {
      observer = new ResizeObserver(scheduleUpdate);
      if (wrapEl) observer.observe(wrapEl);
      if (canvasRef.current) observer.observe(canvasRef.current);
    }

    return () => {
      if (animationFrameId) cancelAnimationFrame(animationFrameId);
      if (wrapEl) wrapEl.removeEventListener("scroll", scheduleUpdate);
      window.removeEventListener("resize", scheduleUpdate);
      observer?.disconnect();
    };
  }, [updateGeometry, zoom, profile, showRulers]);

  // ── Viewport-Anchor Preservation on Zoom ───────────────────────────
  useEffect(() => {
    const prevZoom = prevZoomRef.current;
    const currentZoom = zoom;
    if (prevZoom === currentZoom) return;
    prevZoomRef.current = currentZoom;

    const wrapEl = pageWrapRef.current;
    if (!wrapEl) return;

    const clientHeight = wrapEl.clientHeight;
    const clientWidth = wrapEl.clientWidth;
    const oldScrollTop = wrapEl.scrollTop;
    const oldScrollLeft = wrapEl.scrollLeft;

    const paperOriginY = 36;
    const viewportAnchorY = clientHeight / 2;
    const viewportAnchorX = clientWidth / 2;

    requestAnimationFrame(() => {
      if (!pageWrapRef.current) return;
      if (oldScrollTop < paperOriginY + 20) {
        wrapEl.scrollTop = 0;
      } else {
        const logicalY = (oldScrollTop + viewportAnchorY - paperOriginY) / prevZoom;
        const newScrollTop = paperOriginY + (logicalY * currentZoom) - viewportAnchorY;
        wrapEl.scrollTop = Math.max(0, newScrollTop);
      }

      if (canvasRef.current) {
        const canvasRect = canvasRef.current.getBoundingClientRect();
        const wrapRect = wrapEl.getBoundingClientRect();
        const paperOriginX = canvasRect.left - wrapRect.left + oldScrollLeft;
        const logicalX = (oldScrollLeft + viewportAnchorX - paperOriginX) / prevZoom;
        const newScrollLeft = paperOriginX + (logicalX * currentZoom) - viewportAnchorX;
        wrapEl.scrollLeft = Math.max(0, newScrollLeft);
      }

      updateGeometry();
    });
  }, [zoom, updateGeometry]);

  // ── Pagination (Decoupled from Zoom) ──────────────────────────────
  const pagination = useMemo(
    () =>
      matterDraftPagination({
        getProfile: () => profileRef.current,
        getZoom: () => zoomRef.current,
        onPageCountChange: setPageCount,
      }),
    []
  );

  const extensions = useMemo(
    () => [
      StarterKit,
      Underline,
      FontSize,
      Color,
      FontFamily,
      TextAlign.configure({ types: ["heading", "paragraph"] }),
      CustomTable.configure({ resizable: true }),
      TableRow,
      TableHeader,
      TableCell,
      pagination,
    ],
    [pagination]
  );

  const editor = useEditor({
    extensions,
    content: normalizeDraftPages(contentJson || JSON.stringify(emptyDocument)),
    editorProps: {
      attributes: {
        class: "draft-prosemirror",
      },
      handleScrollToSelection(view) {
        try {
          const { from } = view.state.selection;
          const coords = view.coordsAtPos(from);
          const container = pageWrapRef.current;
          if (!container) return true;

          const contRect = container.getBoundingClientRect();
          const BOTTOM_MARGIN = 40;
          const TOP_MARGIN = 40;

          if (coords.bottom > contRect.bottom - BOTTOM_MARGIN) {
            container.scrollTop += (coords.bottom - (contRect.bottom - BOTTOM_MARGIN));
          } else if (coords.top < contRect.top + TOP_MARGIN && container.scrollTop > 0) {
            container.scrollTop = Math.max(0, container.scrollTop - ((contRect.top + TOP_MARGIN) - coords.top));
          }
          return true;
        } catch {
          return true;
        }
      },
      clipboardTextParser(text, _context, _plain, view) {
        const lines = text.split(/\r?\n/);
        const schema = view.state.schema;
        const nodes = lines.map(line =>
          line ? schema.nodes.paragraph.create(null, schema.text(line)) : schema.nodes.paragraph.create()
        );
        return new Slice(Fragment.from(nodes), 0, 0);
      },
    },
    onUpdate: ({ editor: current }) => onChange(JSON.stringify(current.getJSON())),
  });

  // Decoupled layoutChanged effect: ONLY triggers on profile changes (paper/margins), NOT on zoom!
  useEffect(() => {
    if (editor && !editor.isDestroyed) {
      editor.view.dispatch(editor.view.state.tr.setMeta("layoutChanged", true));
    }
  }, [
    profile.widthMm,
    profile.heightMm,
    profile.marginTopMm,
    profile.marginRightMm,
    profile.marginBottomMm,
    profile.marginLeftMm,
    profile.reservedTopMm,
    editor,
  ]);

  const activePageIndex = useMemo(() => {
    if (editor && !editor.isDestroyed) {
      const { from } = editor.state.selection;
      const pluginState = paginationPluginKey.getState(editor.state);
      if (pluginState && pluginState.breaks) {
        let idx = 0;
        for (const b of pluginState.breaks) {
          if (from >= b.pos) {
            idx = b.pageIndex;
          } else {
            break;
          }
        }
        return Math.max(0, Math.min(pageCount - 1, idx));
      }
    }
    if (pageWrapRef.current) {
      const wrapRect = pageWrapRef.current.getBoundingClientRect();
      const geom = computeSheetGeometry(profile);
      const viewportCenterCanvasY = (wrapRect.height / 2 - geometry.canvasTop) / (zoom || 1);
      const idx = Math.floor(viewportCenterCanvasY / geom.pageStridePx);
      return Math.max(0, Math.min(pageCount - 1, idx));
    }
    return 0;
  }, [editor, pageCount, profile, geometry.canvasTop, zoom]);

  const geom = useMemo(() => computeSheetGeometry(profile), [profile]);
  const extentShellHeight = (pageCount * geom.logicalHeightPx + (pageCount - 1) * geom.sheetGapPx) * zoom;

  const pageStyle = useMemo(
    () =>
      ({
        "--draft-page-width": `${profile.widthMm}mm`,
        "--draft-page-height": `${profile.heightMm}mm`,
        "--draft-top": `${profile.marginTopMm + (profile.reservedTopMm ?? 0)}mm`,
        "--draft-right": `${profile.marginRightMm}mm`,
        "--draft-bottom": `${profile.marginBottomMm}mm`,
        "--draft-left": `${profile.marginLeftMm}mm`,
        "--draft-zoom": zoom,
      }) as CSSProperties,
    [profile, zoom]
  );

  if (!editor) return null;

  return (
    <div className="draft-editor-body">
      <div style={{ position: "relative" }}>
        <EditorToolbar
          editor={editor}
          onPrint={onPrint}
          zoom={zoom}
          onZoomChange={onZoomChange}
          onSetZoom={onSetZoom}
          onResetZoom={onResetZoom}
          pageSetupExpanded={pageSetupExpanded}
          onTogglePageSetup={onTogglePageSetup}
          showRulers={showRulers}
          onToggleRulers={() => setShowRulers(s => !s)}
          pageCount={pageCount}
        />
        {pageSetup}
      </div>
      <TopRulerBar
        profile={profile}
        showRulers={showRulers}
        geometry={geometry}
        onMarginChange={onMarginChange}
      />
      <div className="draft-workspace-row">
        <LeftRulerBar
          profile={profile}
          showRulers={showRulers}
          geometry={geometry}
          activePageIndex={activePageIndex}
          onMarginChange={onMarginChange}
        />
        <div className={`draft-page-wrap ${showRulers ? "has-rulers" : ""}`} ref={pageWrapRef}>
          <div className="draft-workspace-desk">
            <div
              className="draft-extent-shell"
              style={{
                width: `calc(${profile.widthMm}mm * ${zoom})`,
                height: `${extentShellHeight}px`,
              }}
            >
              <div
                className="draft-canvas"
                ref={canvasRef}
                style={{
                  ...pageStyle,
                  transform: `scale(${zoom})`,
                  transformOrigin: "0 0",
                }}
              >
                <div className="draft-backdrop-deck" aria-hidden="true">
                  {Array.from({ length: pageCount }).map((_, i) => (
                    <div key={i} className="draft-sheet-card">
                      <div className="draft-sheet-badge">
                        {draft.draftType === "Noting"
                          ? `Noting Sheet · Page ${i + 1} of ${pageCount} (Provisional)`
                          : `Page ${i + 1} of ${pageCount}`}
                      </div>
                    </div>
                  ))}
                </div>
                <div className="draft-editor-layer">
                  <EditorContent editor={editor} />
                </div>
              </div>
            </div>
          </div>
        </div>
      </div>
    </div>
  );
}

function ArrowLeftIcon() {
  return (
    <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.2" strokeLinecap="round" strokeLinejoin="round">
      <line x1="19" y1="12" x2="5" y2="12" />
      <polyline points="12 19 5 12 12 5" />
    </svg>
  );
}

function ExpandIcon() {
  return (
    <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
      <polyline points="15 3 21 3 21 9" />
      <polyline points="9 21 3 21 3 15" />
      <line x1="21" y1="3" x2="14" y2="10" />
      <line x1="3" y1="21" x2="10" y2="14" />
    </svg>
  );
}

function CompressIcon() {
  return (
    <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
      <polyline points="4 14 10 14 10 20" />
      <polyline points="20 10 14 10 14 4" />
      <line x1="14" y1="10" x2="21" y2="3" />
      <line x1="3" y1="21" x2="10" y2="14" />
    </svg>
  );
}

export function MatterDraftEditorPage() {
  const { id = "" } = useParams();
  const [draft, setDraft] = useState<Draft>();
  const [contentJson, setContentJson] = useState("");
  const [loading, setLoading] = useState(true);
  const [dirty, setDirty] = useState(false);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState("");
  const [focusMode, setFocusMode] = useState(false);
  const [pageSetupExpanded, setPageSetupExpanded] = useState(false);
  const [zoom, setZoom] = useState(1);
  const editGeneration = useRef(0);
  const contentJsonRef = useRef("");

  useEffect(() => {
    request<Draft>(`/matter-drafts/${id}`)
      .then(value => {
        editGeneration.current = 0;
        setDraft(value);
        setContentJson(value.contentJson);
        contentJsonRef.current = value.contentJson;
        setDirty(false);
        setLoading(false);
      })
      .catch(e => {
        setError(e.message);
        setLoading(false);
      });
  }, [id]);

  useEffect(() => {
    if (!dirty) return;
    const warn = (event: BeforeUnloadEvent) => {
      event.preventDefault();
      event.returnValue = "";
    };
    window.addEventListener("beforeunload", warn);
    return () => window.removeEventListener("beforeunload", warn);
  }, [dirty]);

  const markDirty = () => {
    editGeneration.current++;
    setDirty(true);
  };

  const update = (partial: Partial<Draft>) => {
    setDraft(value => value ? { ...value, ...partial } : value);
    markDirty();
  };

  const save = async () => {
    if (!draft || saving) return;
    const savedDraft = draft;
    const savedContent = contentJsonRef.current || contentJson;
    const savedGeneration = editGeneration.current;
    const profile = resolvePageProfile(savedDraft);
    setSaving(true);
    setError("");
    try {
      const result = await request<{ revision: number; updatedAt: string }>(`/matter-drafts/${savedDraft.id}`, {
        method: "PUT",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          title: savedDraft.title,
          contentJson: savedContent,
          pageSize: profile.pageSize === "NotingSheet" ? "A4" : profile.pageSize,
          orientation: savedDraft.draftType === "Noting" ? "Portrait" : savedDraft.orientation,
          marginTopMm: profile.marginTopMm,
          marginRightMm: profile.marginRightMm,
          marginBottomMm: profile.marginBottomMm,
          marginLeftMm: profile.marginLeftMm,
          expectedRevision: savedDraft.revision
        })
      });
      setDraft(current => current ? { ...current, revision: result.revision, updatedAt: result.updatedAt } : current);
      setDirty(editGeneration.current !== savedGeneration);
    } catch (e) {
      setError(e instanceof Error ? e.message : "Could not save draft.");
    } finally {
      setSaving(false);
    }
  };

  if (loading) return <p className="page-state">Loading draft…</p>;
  if (!draft) return <p className="page-state">{error || "Draft not found."}</p>;

  const setLayout = (name: keyof Layout, value: string | number) => update({ [name]: value } as Partial<Draft>);

  const print = () => {
    const profile = resolvePageProfile(draft);
    let style = document.getElementById("matter-draft-print-layout") as HTMLStyleElement | null;
    if (!style) {
      style = document.createElement("style");
      style.id = "matter-draft-print-layout";
      document.head.append(style);
    }
    const top = profile.marginTopMm + (profile.reservedTopMm ?? 0);
    style.textContent = `@media print { @page { size: ${profilePrintSize(profile)}; margin: ${top}mm ${profile.marginRightMm}mm ${profile.marginBottomMm}mm ${profile.marginLeftMm}mm; } }`;
    window.print();
  };

  const backToMatter = (event: MouseEvent<HTMLAnchorElement>) => {
    if (dirty && !window.confirm("You have unsaved changes. Leave this draft?")) {
      event.preventDefault();
    }
  };

  const noting = draft.draftType === "Noting";
  const adjustZoom = (delta: number) => setZoom(value => Math.max(0.5, Math.min(3.0, Math.round((value + delta) * 100) / 100)));
  const setZoomDirect = (val: number) => setZoom(Math.max(0.5, Math.min(3.0, val)));

  const pageSetup = pageSetupExpanded ? (
    <section className="draft-page-setup-panel" aria-label="Page setup">
      {noting ? (
        <div className="draft-noting-layout-info">
          <div className="noting-info-header">
            <strong>Delhi LAC Noting Sheet (Provisional)</strong>
            <span className="noting-locked-badge">Fixed Profile</span>
          </div>
          <span className="noting-info-dims">210 × 297 mm (A4) · Portrait · Margins: Top 25mm, Right 20mm, Bottom 20mm, Left 25mm (File thread clearance)</span>
          <small className="noting-info-note">{DELHI_LAC_NOTING_V1.calibrationNote}</small>
        </div>
      ) : (
        <div className="draft-letter-layout-controls">
          <label>
            Page
            <select value={draft.pageSize} onChange={e => setLayout("pageSize", e.target.value)}>
              <option value="A4">A4 (210 × 297 mm)</option>
              <option value="Legal">Legal (215.9 × 355.6 mm)</option>
            </select>
          </label>
          <label>
            Orientation
            <select value={draft.orientation} onChange={e => setLayout("orientation", e.target.value)}>
              <option value="Portrait">Portrait</option>
              <option value="Landscape">Landscape</option>
            </select>
          </label>
          {(["marginTopMm", "marginRightMm", "marginBottomMm", "marginLeftMm"] as const).map(key => (
            <label key={key}>
              {key.replace("margin", "").replace("Mm", "")}
              <input
                aria-label={`${key} millimetres`}
                type="number"
                min="0"
                max="50"
                value={draft[key]}
                onChange={e => setLayout(key, Math.max(0, Math.min(50, Number(e.target.value))))}
              />
              <small>mm</small>
            </label>
          ))}
        </div>
      )}
    </section>
  ) : null;

  return (
    <div className={`draft-workspace ${focusMode ? "draft-focus-mode" : ""}`}>
      <header className="draft-header">
        <div className="draft-document-identity">
          <Link
            to={`/matters/${draft.matterId}`}
            onClick={backToMatter}
            className="draft-back-link"
            title={`Back to ${draft.matterTitle}`}
          >
            <ArrowLeftIcon />
            <span className="draft-back-text">{draft.matterTitle}</span>
          </Link>
          <span className="draft-crumb-sep">/</span>
          <input
            aria-label="Draft title"
            className="draft-title"
            value={draft.title}
            placeholder="Untitled draft"
            onChange={e => update({ title: e.target.value })}
          />
          <div className="draft-badges-group">
            <span className={`draft-type-badge draft-type-${draft.draftType.toLowerCase()}`}>
              {draft.draftType}
            </span>
            <span className={`draft-status-pill ${dirty ? "status-dirty" : "status-saved"}`}>
              <span className="draft-status-dot" />
              <span>{saving ? "Saving…" : dirty ? "Unsaved changes" : "Saved"}</span>
            </span>
          </div>
        </div>
        <div className="draft-header-actions">
          <button
            type="button"
            className="draft-action-btn"
            onClick={() => setFocusMode(value => !value)}
            title={focusMode ? "Exit full screen" : "Full screen view"}
          >
            {focusMode ? <CompressIcon /> : <ExpandIcon />}
            <span>{focusMode ? "Exit full screen" : "Full screen"}</span>
          </button>
          <button
            type="button"
            className="draft-save"
            onClick={() => void save()}
            disabled={saving || !dirty}
          >
            {saving ? "Saving…" : dirty ? "Save changes" : "Saved"}
          </button>
        </div>
      </header>
      {error && <p className="error draft-error">{error}</p>}
      <MatterDraftCanvas
        key={draft.id}
        draft={draft}
        contentJson={contentJson}
        zoom={zoom}
        onChange={json => {
          contentJsonRef.current = json;
          setContentJson(json);
          markDirty();
        }}
        onMarginChange={partial => update(partial)}
        onPrint={print}
        pageSetup={pageSetup}
        pageSetupExpanded={pageSetupExpanded}
        onTogglePageSetup={() => setPageSetupExpanded(value => !value)}
        onZoomChange={adjustZoom}
        onSetZoom={setZoomDirect}
        onResetZoom={() => setZoom(1)}
      />
    </div>
  );
}

