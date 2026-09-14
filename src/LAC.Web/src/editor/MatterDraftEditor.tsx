import { useEffect, useMemo, useState } from "react";
import type { CSSProperties } from "react";
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
import "./matter-editor.css";

const api = "/api";
const emptyDocument = { type: "doc", content: [{ type: "paragraph" }] };
type Draft = { id: string; matterId: string; matterTitle: string; title: string; draftType: string; contentJson: string; revision: number; pageSize: string; orientation: "Portrait" | "Landscape"; marginTopMm: number; marginRightMm: number; marginBottomMm: number; marginLeftMm: number; updatedAt: string };
type Layout = Pick<Draft, "pageSize" | "orientation" | "marginTopMm" | "marginRightMm" | "marginBottomMm" | "marginLeftMm">;

const FontSize = TextStyle.extend({ addGlobalAttributes() { return [{ types: ["textStyle"], attributes: { fontSize: { default: null, parseHTML: (element: HTMLElement) => element.style.fontSize || null, renderHTML: (attributes: Record<string, string | null>) => attributes.fontSize ? { style: `font-size: ${attributes.fontSize}` } : {} } } }]; } });

async function request<T>(path: string, init?: RequestInit): Promise<T> { const response = await fetch(api + path, init); if (!response.ok) { const body = await response.json().catch(() => null); throw new Error(body?.detail || body?.title || "Could not save the draft."); } return response.status === 204 ? {} as T : response.json() as Promise<T>; }
const displayDate = (value: string) => new Date(value).toLocaleDateString(undefined, { day: "numeric", month: "short", year: "numeric" });

export function MatterDrafts({ matterId }: { matterId: string }) {
  const navigate = useNavigate(); const [drafts, setDrafts] = useState<Array<Pick<Draft, "id" | "title" | "draftType" | "updatedAt">>>([]); const [title, setTitle] = useState(""); const [error, setError] = useState("");
  useEffect(() => { request<typeof drafts>(`/matters/${matterId}/drafts`).then(setDrafts).catch(e => setError(e.message)); }, [matterId]);
  const create = async (draftType: string) => { try { const result = await request<{ id: string }>(`/matters/${matterId}/drafts`, { method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify({ title, draftType }) }); navigate(`/matter-drafts/${result.id}`); } catch (e) { setError(e instanceof Error ? e.message : "Could not create draft."); } };
  return <section className="section draft-list"><h2>Drafts</h2><div className="draft-create"><input aria-label="Draft title" placeholder="Draft title" value={title} onChange={e => setTitle(e.target.value)} /><button onClick={() => void create("Letter")} disabled={!title.trim()}>+ Create Letter</button><button onClick={() => void create("Noting")} disabled={!title.trim()}>+ Create Noting</button></div>{error && <p className="error">{error}</p>}{drafts.length === 0 ? <p className="hint">No letters or notings yet.</p> : <div className="draft-cards">{drafts.map(draft => <article key={draft.id}><strong>{draft.title}</strong><span>{draft.draftType} · Updated {displayDate(draft.updatedAt)}</span><Link className="button-link" to={`/matter-drafts/${draft.id}`}>Open</Link></article>)}</div>}</section>;
}

function MatterDraftCanvas({ draft, onChange }: { draft: Draft; onChange: (contentJson: string) => void }) {
  const editor = useEditor({ extensions: [StarterKit, Underline, FontSize, Color, FontFamily, TextAlign.configure({ types: ["heading", "paragraph"] }), Table.configure({ resizable: true }), TableRow, TableHeader, TableCell], content: JSON.parse(draft.contentJson || JSON.stringify(emptyDocument)), editorProps: { attributes: { class: "draft-prosemirror" } }, onUpdate: ({ editor: current }) => onChange(JSON.stringify(current.getJSON())) });
  const pageStyle = useMemo(() => ({ "--draft-top": `${draft.marginTopMm}mm`, "--draft-right": `${draft.marginRightMm}mm`, "--draft-bottom": `${draft.marginBottomMm}mm`, "--draft-left": `${draft.marginLeftMm}mm` } as CSSProperties), [draft.marginTopMm, draft.marginRightMm, draft.marginBottomMm, draft.marginLeftMm]);
  if (!editor) return null;
  return <div className="draft-editor-body"><EditorToolbar editor={editor} onPrint={() => window.print()} /><div className={`draft-page-wrap ${draft.orientation.toLowerCase()}`}><main className="draft-page" style={pageStyle}><EditorContent editor={editor} /></main></div></div>;
}

export function MatterDraftEditorPage() {
  const { id = "" } = useParams(); const [draft, setDraft] = useState<Draft>(); const [contentJson, setContentJson] = useState(""); const [loading, setLoading] = useState(true); const [dirty, setDirty] = useState(false); const [saving, setSaving] = useState(false); const [error, setError] = useState("");
  useEffect(() => { request<Draft>(`/matter-drafts/${id}`).then(value => { setDraft(value); setContentJson(value.contentJson); setLoading(false); }).catch(e => { setError(e.message); setLoading(false); }); }, [id]);
  const update = (partial: Partial<Draft>) => { setDraft(value => value ? { ...value, ...partial } : value); setDirty(true); };
  const save = async () => { if (!draft) return; setSaving(true); setError(""); try { const result = await request<{ revision: number; updatedAt: string }>(`/matter-drafts/${draft.id}`, { method: "PUT", headers: { "Content-Type": "application/json" }, body: JSON.stringify({ title: draft.title, contentJson, pageSize: draft.pageSize, orientation: draft.orientation, marginTopMm: draft.marginTopMm, marginRightMm: draft.marginRightMm, marginBottomMm: draft.marginBottomMm, marginLeftMm: draft.marginLeftMm, expectedRevision: draft.revision }) }); setDraft({ ...draft, revision: result.revision, updatedAt: result.updatedAt, contentJson }); setDirty(false); } catch (e) { setError(e instanceof Error ? e.message : "Could not save draft."); } finally { setSaving(false); } };
  if (loading) return <p className="page-state">Loading draft…</p>; if (!draft) return <p className="page-state">{error || "Draft not found."}</p>;
  const setLayout = (name: keyof Layout, value: string | number) => update({ [name]: value } as Partial<Draft>);
  return <div className="draft-workspace"><header className="draft-header"><div><Link to={`/matters/${draft.matterId}`}>← {draft.matterTitle}</Link><input className="draft-title" value={draft.title} onChange={e => update({ title: e.target.value })} /><span>{draft.draftType} · {dirty ? "Unsaved changes" : "Saved"}</span></div><button onClick={() => void save()} disabled={saving || !dirty}>{saving ? "Saving…" : "Save"}</button></header>{error && <p className="error draft-error">{error}</p>}<section className="draft-layout-controls"><label>Page<select value={draft.pageSize} onChange={e => setLayout("pageSize", e.target.value)}><option>A4</option></select></label><label>Orientation<select value={draft.orientation} onChange={e => setLayout("orientation", e.target.value)}><option>Portrait</option><option>Landscape</option></select></label>{(["marginTopMm", "marginRightMm", "marginBottomMm", "marginLeftMm"] as const).map(key => <label key={key}>{key.replace("margin", "").replace("Mm", "")} mm<input type="number" min="0" max="50" value={draft[key]} onChange={e => setLayout(key, Number(e.target.value))} /></label>)}</section><MatterDraftCanvas key={draft.id} draft={draft} onChange={json => { setContentJson(json); setDirty(true); }} /></div>;
}
