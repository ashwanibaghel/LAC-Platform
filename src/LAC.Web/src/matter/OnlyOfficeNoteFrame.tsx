import React, { useEffect, useRef, useState } from "react";
import { notingOfficeApi } from "./notingOfficeApi";
import type { OfficeSessionConfigResponse } from "./notingTypes";

type OfficeInstance = { destroyEditor: () => void };

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
  onDocumentReady?: () => void;
  onDocumentStateChange?: (isDirty: boolean) => void;
  onError?: (errorMessage: string) => void;
}

export const OnlyOfficeNoteFrame: React.FC<OnlyOfficeNoteFrameProps> = ({
  sessionId,
  isFrozen = false,
  onDocumentReady,
  onDocumentStateChange,
  onError
}) => {
  const hostRef = useRef<HTMLDivElement | null>(null);
  const editorRef = useRef<OfficeInstance | null>(null);

  const [loading, setLoading] = useState<boolean>(true);
  const [error, setError] = useState<string | null>(null);
  const [attempt, setAttempt] = useState<number>(0);

  useEffect(() => {
    let aborted = false;
    const container = hostRef.current;
    if (!container || !sessionId) return;

    setLoading(true);
    setError(null);

    let readyTimeout: number | undefined;

    const initEditor = async () => {
      try {
        const configData: OfficeSessionConfigResponse = await notingOfficeApi.getSessionConfig(sessionId);
        if (aborted) return;

        const docsApi = await loadOfficeScript(configData.scriptUrl);
        if (aborted) return;

        const placeholder = document.createElement("div");
        placeholder.id = `office-note-host-${sessionId}-${Date.now()}`;
        container.replaceChildren(placeholder);

        readyTimeout = window.setTimeout(() => {
          if (!aborted) {
            setLoading(false);
            setError("Document Server did not open the note within timeout. Ensure Document Server is running.");
            onError?.("Document Server timeout");
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
                onDocumentReady?.();
              }
            },
            onDocumentStateChange: (event: any) => {
              // event.data indicates document modification state
              onDocumentStateChange?.(Boolean(event?.data));
            },
            onError: (event: any) => {
              clearTimeout(readyTimeout);
              const errMsg = typeof event?.data === "string" ? event.data : "ONLYOFFICE editor reported an error";
              if (!aborted) {
                setLoading(false);
                setError(errMsg);
                onError?.(errMsg);
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
          onError?.(msg);
        }
      }
    };

    void initEditor();

    return () => {
      aborted = true;
      clearTimeout(readyTimeout);
      try {
        editorRef.current?.destroyEditor();
      } catch {}
      editorRef.current = null;
      if (container) {
        container.replaceChildren();
      }
    };
  }, [sessionId, attempt]);

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

      {isFrozen && (
        <div className="onlyoffice-note-frozen-banner">
          🔒 Note lease frozen for canonical preview & official submission.
        </div>
      )}

      <div className="onlyoffice-host-container" ref={hostRef} />
    </div>
  );
};
