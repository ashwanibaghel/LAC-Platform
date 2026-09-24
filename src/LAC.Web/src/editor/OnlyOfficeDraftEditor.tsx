import { useEffect, useRef, useState } from "react";
import { Link, useParams } from "react-router-dom";
import { MatterDraftEditorPage as LegacyEditor } from "./MatterDraftEditor";
import "./onlyoffice-editor.css";

type Draft = {
  id: string; matterId: string; title: string; draftType: string; status: string;
  officeEnabled: boolean; officeDocumentId: string | null;
};
type OfficeConfig = {
  documentType: "word" | "cell" | "slide";
  document: { fileType: string; key: string; title: string; url: string; permissions: Record<string, boolean> };
  editorConfig: Record<string, unknown>;
  token: string;
};
type OfficeInstance = { destroyEditor: () => void };
type DocsApi = { DocEditor: new (id: string, config: OfficeConfig & Record<string, unknown>) => OfficeInstance };
declare global { interface Window { DocsAPI?: DocsApi } }

const scripts = new Map<string, Promise<DocsApi>>();
function loadOfficeScript(url: string): Promise<DocsApi> {
  const existing = scripts.get(url);
  if (existing) return existing;
  const promise = new Promise<DocsApi>((resolve, reject) => {
    const script = document.createElement("script");
    const fail = () => {
      clearTimeout(timer);
      script.remove();
      scripts.delete(url);
      reject(new Error("Could not load ONLYOFFICE. Check that Document Server is running and reachable."));
    };
    const timer = window.setTimeout(fail, 30000);
    script.src = url;
    script.async = true;
    script.onerror = fail;
    script.onload = () => {
      clearTimeout(timer);
      if (window.DocsAPI) resolve(window.DocsAPI);
      else fail();
    };
    document.head.appendChild(script);
  });
  scripts.set(url, promise);
  return promise;
}

async function request<T>(url: string, signal: AbortSignal): Promise<T> {
  const response = await fetch(url, { credentials: "include", signal, cache: "no-store" });
  if (!response.ok) {
    const problem = await response.json().catch(() => null);
    throw new Error(problem?.detail || problem?.title || problem?.message || `Could not open draft (${response.status}).`);
  }
  return response.json() as Promise<T>;
}

// The host is file-format agnostic; future XLSX configs can use documentType "cell".
function OfficeFrame({ draftId }: { draftId: string }) {
  const host = useRef<HTMLDivElement>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");
  const [attempt, setAttempt] = useState(0);

  useEffect(() => {
    const controller = new AbortController();
    const container = host.current!;
    let editor: OfficeInstance | undefined;
    let readyTimeout: number | undefined;
    const start = async () => {
      const data = await request<{ scriptUrl: string; config: OfficeConfig }>(
        `/api/matter-drafts/${draftId}/office-config`, controller.signal);
      const api = await loadOfficeScript(data.scriptUrl);
      if (controller.signal.aborted) return;
      // DocsAPI replaces this child; React owns only the stable parent container.
      const placeholder = document.createElement("div");
      placeholder.id = `office-${crypto.randomUUID()}`;
      container.replaceChildren(placeholder);
      readyTimeout = window.setTimeout(() => {
        setLoading(false);
        setError("ONLYOFFICE has not opened the document. Check Document Server and the API connection.");
      }, 60000);
      editor = new api.DocEditor(placeholder.id, {
        ...data.config, width: "100%", height: "100%",
        events: {
          onDocumentReady: () => { clearTimeout(readyTimeout); setLoading(false); setError(""); },
          onError: () => { clearTimeout(readyTimeout); setLoading(false); setError("ONLYOFFICE reported an error. Check Document Server connectivity and configuration."); }
        }
      });
    };
    void start().catch((failure: unknown) => {
      if (!controller.signal.aborted) {
        clearTimeout(readyTimeout);
        setLoading(false);
        setError(failure instanceof Error ? failure.message : "Could not open ONLYOFFICE.");
      }
    });
    return () => {
      controller.abort();
      clearTimeout(readyTimeout);
      editor?.destroyEditor();
      container.replaceChildren();
    };
  }, [draftId, attempt]);

  return <div className="office-frame">
    {loading && <div className="office-message" role="status">Opening Office document…</div>}
    {error && <div className="office-message office-error" role="alert">{error} <button onClick={() => {
      setLoading(true); setError(""); setAttempt(value => value + 1);
    }}>Retry</button></div>}
    <div className="office-host" ref={host} />
  </div>;
}

function DraftRoute({ id }: { id: string }) {
  const [draft, setDraft] = useState<Draft>();
  const [error, setError] = useState("");
  useEffect(() => {
    const controller = new AbortController();
    void request<Draft>(`/api/matter-drafts/${id}`, controller.signal).then(setDraft).catch((failure: unknown) => {
      if (!controller.signal.aborted) setError(failure instanceof Error ? failure.message : "Could not load draft.");
    });
    return () => controller.abort();
  }, [id]);
  if (error) return <div className="office-message" role="alert">{error} <Link to="/matters">Back to Matters</Link></div>;
  if (!draft) return <div className="office-message" role="status">Loading draft…</div>;
  if (!draft.officeEnabled && !draft.officeDocumentId) return <LegacyEditor />;
  return <main className="office-draft">
    <header className="office-header">
      <Link to={`/matters/${draft.matterId}`}>← Back to Matter</Link>
      <h1>{draft.title}</h1>
      <span>{draft.draftType} · {draft.status}</span>
      <span>Office document</span>
    </header>
    {draft.officeEnabled ? <OfficeFrame draftId={id} />
      : <div className="office-message" role="alert">ONLYOFFICE is disabled. Enable it to open this Office document.</div>}
  </main>;
}

export function OnlyOfficeDraftEditorPage() {
  const { id = "" } = useParams();
  return <DraftRoute key={id} id={id} />;
}
