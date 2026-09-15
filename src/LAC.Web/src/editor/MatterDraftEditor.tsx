import { useEffect, useMemo, useRef, useState } from "react";
import type { CSSProperties, MouseEvent, ReactNode } from "react";
import { EditorContent, useEditor } from "@tiptap/react";
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
import { EditorToolbar } from "./EditorToolbar";
import { matterDraftPagination, normalizeDraftPages } from "./pagination";
import { DELHI_LAC_NOTING_V1, profilePrintSize, resolvePageProfile } from "./pageProfiles";
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

export function MatterDrafts({ matterId }: { matterId: string }) {
  const navigate = useNavigate();
  const [drafts, setDrafts] = useState<Array<Pick<Draft, "id" | "title" | "draftType" | "updatedAt">>>([]);
  const [title, setTitle] = useState("");
  const [error, setError] = useState("");
  useEffect(() => { request<typeof drafts>(`/matters/${matterId}/drafts`).then(setDrafts).catch(e => setError(e.message)); }, [matterId]);
  const create = async (draftType: string) => {
    try {
      const result = await request<{ id: string }>(`/matters/${matterId}/drafts`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ title, draftType })
      });
      navigate(`/matter-drafts/${result.id}`);
    } catch (e) {
      setError(e instanceof Error ? e.message : "Could not create draft.");
    }
  };
  return <section className="section draft-list">
    <h2>Drafts</h2>
    <div className="draft-create">
      <input aria-label="Draft title" placeholder="Draft title" value={title} onChange={e => setTitle(e.target.value)} />
      <button onClick={() => void create("Letter")} disabled={!title.trim()}>+ Create Letter</button>
      <button onClick={() => void create("Noting")} disabled={!title.trim()}>+ Create Noting</button>
    </div>
    {error && <p className="error">{error}</p>}
    {drafts.length === 0 ? <p className="hint">No letters or notings yet.</p> : (
      <div className="draft-cards">
        {drafts.map(draft => (
          <article key={draft.id}>
            <strong>{draft.title}</strong>
            <span>{draft.draftType} · Updated {displayDate(draft.updatedAt)}</span>
            <Link className="button-link" to={`/matter-drafts/${draft.id}`}>Open</Link>
          </article>
        ))}
      </div>
    )}
  </section>;
}

function MatterDraftCanvas({
  draft,
  contentJson,
  zoom,
  onChange,
  onPrint,
  pageSetup,
  pageSetupExpanded,
  onTogglePageSetup,
  onZoomChange,
  onResetZoom
}: {
  draft: Draft;
  contentJson: string;
  zoom: number;
  onChange: (contentJson: string) => void;
  onPrint: () => void;
  pageSetup: ReactNode;
  pageSetupExpanded: boolean;
  onTogglePageSetup: () => void;
  onZoomChange: (delta: number) => void;
  onResetZoom: () => void;
}) {
  const profile = resolvePageProfile(draft);
  const [pageCount, setPageCount] = useState(1);

  const profileRef = useRef(profile);
  profileRef.current = profile;
  const zoomRef = useRef(zoom);
  zoomRef.current = zoom;

  const pagination = useMemo(() => matterDraftPagination({
    getProfile: () => profileRef.current,
    getZoom: () => zoomRef.current,
    onPageCountChange: setPageCount,
  }), []);

  const editor = useEditor({
    extensions: [
      StarterKit,
      Underline,
      FontSize,
      Color,
      FontFamily,
      TextAlign.configure({ types: ["heading", "paragraph"] }),
      Table.configure({ resizable: true }),
      TableRow,
      TableHeader,
      TableCell,
      pagination
    ],
    content: normalizeDraftPages(contentJson || JSON.stringify(emptyDocument)),
    editorProps: {
      attributes: {
        class: "draft-prosemirror"
      }
    },
    onUpdate: ({ editor: current }) => onChange(JSON.stringify(current.getJSON()))
  });

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
    zoom,
    editor
  ]);

  const pageStyle = useMemo(() => ({
    "--draft-page-width": `${profile.widthMm}mm`,
    "--draft-page-height": `${profile.heightMm}mm`,
    "--draft-top": `${profile.marginTopMm + (profile.reservedTopMm ?? 0)}mm`,
    "--draft-right": `${profile.marginRightMm}mm`,
    "--draft-bottom": `${profile.marginBottomMm}mm`,
    "--draft-left": `${profile.marginLeftMm}mm`,
    "--draft-zoom": zoom,
  } as CSSProperties), [profile, zoom]);

  if (!editor) return null;

  return (
    <div className="draft-editor-body">
      <EditorToolbar
        editor={editor}
        onPrint={onPrint}
        zoom={zoom}
        onZoomChange={onZoomChange}
        onResetZoom={onResetZoom}
        pageSetupExpanded={pageSetupExpanded}
        onTogglePageSetup={onTogglePageSetup}
        pageCount={pageCount}
      />
      {pageSetup}
      <div className="draft-page-wrap">
        <div className="draft-canvas" style={pageStyle}>
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
  const [headerExpanded, setHeaderExpanded] = useState(false);
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
  const adjustZoom = (delta: number) => setZoom(value => Math.max(0.6, Math.min(1.5, Math.round((value + delta) * 100) / 100)));

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
      <header className={`draft-header ${headerExpanded ? "" : "draft-header-collapsed"}`}>
        <div className="draft-document-identity">
          <Link to={`/matters/${draft.matterId}`} onClick={backToMatter}>← Back to {draft.matterTitle}</Link>
          <input aria-label="Draft title" className="draft-title" value={draft.title} onChange={e => update({ title: e.target.value })} />
          <span><b>{draft.draftType}</b><i />{dirty ? "Unsaved changes" : "Saved"}</span>
        </div>
        <div className="draft-header-actions">
          <button className="secondary-button" onClick={() => setHeaderExpanded(value => !value)}>
            {headerExpanded ? "Collapse header" : "Expand header"}
          </button>
          <button className="secondary-button" onClick={() => setFocusMode(value => !value)}>
            {focusMode ? "Exit full screen" : "Full screen"}
          </button>
          <button className="draft-save" onClick={() => void save()} disabled={saving || !dirty}>
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
        onPrint={print}
        pageSetup={pageSetup}
        pageSetupExpanded={pageSetupExpanded}
        onTogglePageSetup={() => setPageSetupExpanded(value => !value)}
        onZoomChange={adjustZoom}
        onResetZoom={() => setZoom(1)}
      />
    </div>
  );
}

