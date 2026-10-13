import React, { useEffect, useRef, useState } from "react";
import { notingOfficeApi } from "./notingOfficeApi";
import type { OfficeSessionConfigResponse } from "./notingTypes";
import { NativeSynchronization } from "./nativeSynchronization";

type OfficeInstance = { destroyEditor: () => void; downloadAs: (format: string) => void };

const loadedScripts = new Map<string, Promise<any>>();

function loadOfficeScript(url: string): Promise<any> {
  const existing = loadedScripts.get(url);
  if (existing) return existing;

  const promise = new Promise<any>((resolve, reject) => {
    // If DocsAPI is already available globally, resolve immediately
    if ((window as any).DocsAPI) {
      resolve((window as any).DocsAPI);
      return;
    }

    const script = document.createElement("script");
    const fail = () => {
      clearTimeout(timer);
      script.remove();
      loadedScripts.delete(url);
      reject(new Error("Document Server script unreachable. Please check Document Server connection on port 8082."));
    };

    const timer = window.setTimeout(fail, 25000);
    script.src = url;
    script.async = true;
    script.onerror = fail;
    script.onload = () => {
      clearTimeout(timer);
      if ((window as any).DocsAPI) {
        resolve((window as any).DocsAPI);
      } else {
        fail();
      }
    };
    document.head.appendChild(script);
  });

  loadedScripts.set(url, promise);
  return promise;
}

interface OnlyOfficeNoteFrameProps {
  sessionId: string;
  isFrozen?: boolean;
  isInputBlocked?: boolean;
  onDocumentReady?: () => void;
  onDocumentStateChange?: (isDirty: boolean) => void;
  onError?: (errorMessage: string) => void;
  onNativeBridgeReady?: (bridge: NativeSynchronization | null) => void;
}

export const OnlyOfficeNoteFrame: React.FC<OnlyOfficeNoteFrameProps> = ({
  sessionId,
  isFrozen = false,
  isInputBlocked = false,
  onDocumentReady,
  onDocumentStateChange,
  onError,
  onNativeBridgeReady
}) => {
  const hostRef = useRef<HTMLDivElement | null>(null);
  const editorRef = useRef<OfficeInstance | null>(null);
  const callbacks = useRef({ onDocumentReady, onDocumentStateChange, onError, onNativeBridgeReady });
  callbacks.current = { onDocumentReady, onDocumentStateChange, onError, onNativeBridgeReady };

  const [loading, setLoading] = useState<boolean>(true);
  const [error, setError] = useState<string | null>(null);
  const [attempt, setAttempt] = useState<number>(0);

  useEffect(() => {
    let aborted = false;
    const container = hostRef.current;
    if (!container || !sessionId) return;

    if (isFrozen) {
      setLoading(false);
      return;
    }

    setLoading(true);
    setError(null);

    let readyTimeout: number | undefined;
    let nativeBridge: NativeSynchronization | undefined;

    const initEditor = async () => {
      try {
        const configData: OfficeSessionConfigResponse = await notingOfficeApi.getSessionConfig(sessionId);
        if (aborted) return;

        const docsApi = await loadOfficeScript(configData.scriptUrl);
        if (aborted) return;

        const placeholder = document.createElement("div");
        placeholder.id = `office-note-host-${sessionId}-${Date.now()}`;
        container.replaceChildren(placeholder);
        nativeBridge = new NativeSynchronization(new URL(configData.scriptUrl).origin,
          () => { container.inert = true; }, () => editorRef.current!.downloadAs("docx"));

        readyTimeout = window.setTimeout(() => {
          if (!aborted) {
            setLoading(false);
            setError("Document Server did not open the note within timeout. Ensure Document Server is running.");
            nativeBridge?.fail(new Error("Document Server timeout"));
            callbacks.current.onError?.("Document Server timeout");
          }
        }, 45000);

        editorRef.current = new docsApi.DocEditor(placeholder.id, {
          ...configData.config,
          width: "100%",
          height: "100%",
          events: {
            onDocumentReady: () => {
              clearTimeout(readyTimeout);
              if (!aborted) {
                setLoading(false);
                setError(null);
                callbacks.current.onDocumentReady?.();
                callbacks.current.onNativeBridgeReady?.(nativeBridge!);
              }
            },
            onDocumentStateChange: (event: any) => {
              // event.data indicates document modification state
              if (!aborted) {
                nativeBridge?.documentState(Boolean(event?.data));
                callbacks.current.onDocumentStateChange?.(Boolean(event?.data));
              }
            },
            onDownloadAs: (event: any) => {
              if (!aborted && typeof event?.data?.url === "string") nativeBridge?.exported(event.data.url);
            },
            onError: (event: any) => {
              clearTimeout(readyTimeout);
              const errMsg = typeof event?.data === "string" ? event.data : "ONLYOFFICE editor reported an error";
              if (!aborted) {
                setLoading(false);
                setError(errMsg);
                nativeBridge?.fail(new Error(errMsg));
                callbacks.current.onError?.(errMsg);
              }
            }
          }
        });
      } catch (err: any) {
        if (!aborted) {
          clearTimeout(readyTimeout);
          setLoading(false);
          const msg = err.message || "Failed to initialize ONLYOFFICE Note Editor";
          setError(msg);
          nativeBridge?.fail(new Error(msg));
          callbacks.current.onError?.(msg);
        }
      }
    };

    void initEditor();

    return () => {
      aborted = true;
      nativeBridge?.close();
      callbacks.current.onNativeBridgeReady?.(null);
      clearTimeout(readyTimeout);
      try {
        editorRef.current?.destroyEditor();
      } catch {}
      editorRef.current = null;
      if (container) {
        container.replaceChildren();
      }
    };
  }, [sessionId, attempt, isFrozen]);

  return (
    <div className="onlyoffice-note-frame-root">
      {loading && (
        <div className="onlyoffice-note-loading-overlay">
          <div className="noting-spinner" />
          <div className="loading-text">Loading Green Legal Note Editor…</div>
          <div className="loading-subtext">Initializing actor's private working note</div>
        </div>
      )}

      {error && (
        <div className="onlyoffice-note-error-overlay">
          <div className="error-icon">⚠️</div>
          <h4>ONLYOFFICE Editor Offline or Unavailable</h4>
          <p>{error}</p>
          <div className="error-actions">
            <button
              type="button"
              className="noting-btn noting-btn-primary"
              onClick={() => {
                setError(null);
                setLoading(true);
                setAttempt((c) => c + 1);
              }}
            >
              Retry Connection
            </button>
          </div>
        </div>
      )}

      {(isInputBlocked || isFrozen) && (
        <div
          className={`onlyoffice-note-quiesce-overlay ${isFrozen ? "frozen" : "quiescing"}`}
          style={{
            position: "absolute",
            top: 0,
            left: 0,
            right: 0,
            bottom: 0,
            zIndex: 10,
            background: isFrozen ? "rgba(248, 250, 252, 0.88)" : "rgba(255, 255, 255, 0.75)",
            backdropFilter: "blur(2px)",
            display: "flex",
            flexDirection: "column",
            alignItems: "center",
            justifyContent: "center",
            cursor: "not-allowed",
            textAlign: "center",
            padding: "20px"
          }}
        >
          {isFrozen ? (
            <div style={{ background: "#ffffff", border: "1px solid #cbd5e1", borderRadius: "8px", padding: "16px 24px", maxWidth: "420px", boxShadow: "0 4px 12px rgba(0,0,0,0.08)" }}>
              <span style={{ fontSize: "28px", display: "block", marginBottom: "8px" }}>🔒</span>
              <strong style={{ fontSize: "14px", color: "#0f172a", display: "block", marginBottom: "4px" }}>Note Lease Frozen for Canonical Review</strong>
              <p style={{ fontSize: "12px", color: "#64748b", margin: 0 }}>Editor input is locked. Official note content is verified against the canonical preview.</p>
            </div>
          ) : (
            <div style={{ background: "#ffffff", border: "1px solid #cbd5e1", borderRadius: "8px", padding: "16px 24px", maxWidth: "420px", boxShadow: "0 4px 12px rgba(0,0,0,0.08)" }}>
              <div className="noting-spinner" style={{ margin: "0 auto 8px" }} />
              <strong style={{ fontSize: "14px", color: "#0f172a", display: "block", marginBottom: "4px" }}>Quiescing Editor Input…</strong>
              <p style={{ fontSize: "12px", color: "#64748b", margin: 0 }}>Capturing final committed keystrokes before review barrier.</p>
            </div>
          )}
        </div>
      )}

      <div className="onlyoffice-host-container" ref={hostRef} inert={isInputBlocked || isFrozen} />
    </div>
  );
};
