import { useEffect, useState } from "react";

type PdfState = "checking" | "ready" | "unavailable";

export function AwardPdfViewer({ documentId, page }: { documentId?: string; page?: number }) {
  const [checked, setChecked] = useState<{ url: string; retry: number; state: PdfState }>({ url: "", retry: 0, state: "checking" });
  const [retry, setRetry] = useState(0);
  const url = documentId ? `/api/documents/${documentId}/content` : "";
  const pdf = page && url ? `${url}#page=${page}&view=FitH&navpanes=0` : "";
  const state = checked.url === url && checked.retry === retry ? checked.state : "checking";

  useEffect(() => {
    if (!url) return;
    let live = true;
    // A failed PDF request can leave Chrome's embedded viewer as a blank black pane.
    // Check the same authenticated endpoint before handing it to the native viewer.
    fetch(url, { credentials: "include", headers: { Range: "bytes=0-15" } })
      .then(async response => {
        if (!response.ok || !response.headers.get("content-type")?.toLowerCase().includes("application/pdf")) throw new Error("PDF unavailable");
        const signature = new TextDecoder().decode(await response.arrayBuffer());
        if (!signature.startsWith("%PDF-")) throw new Error("Invalid PDF response");
        if (live) setChecked({ url, retry, state: "ready" });
      })
      .catch(() => { if (live) setChecked({ url, retry, state: "unavailable" }); });
    return () => { live = false; };
  }, [url, retry]);

  if (!pdf) return <div className="award-wb-pdf-state">No stored source page. Verification requires source evidence.</div>;
  if (state !== "ready") return <div className="award-wb-pdf-state" role="status">
    <strong>{state === "checking" ? "Loading original PDF…" : "PDF could not be displayed here."}</strong>
    {state === "unavailable" && <div><button onClick={() => setRetry(value => value + 1)}>Retry</button><a href={pdf} target="_blank" rel="noreferrer">Open PDF ↗</a></div>}
  </div>;
  return <iframe key={page} src={pdf} title={`Original Award PDF page ${page}`} onError={() => setChecked({ url, retry, state: "unavailable" })} />;
}
