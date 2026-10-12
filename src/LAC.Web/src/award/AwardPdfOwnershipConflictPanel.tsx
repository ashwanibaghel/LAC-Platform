import { useEffect, useRef, useState } from "react";
import { Link } from "react-router-dom";
import { landRecordResponse, type PdfOwnershipConflict } from "./LandRecordApiError";
import type { AwardPdfLinkResolutionResult } from "./landRecordsStaffTypes";

export function AwardPdfOwnershipConflictPanel({ conflict, onResolved }: { conflict: PdfOwnershipConflict; onResolved: (result: AwardPdfLinkResolutionResult) => void }) {
  const [sourceId, setSourceId] = useState(conflict.existingLinks[0]?.awardId || "");
  const [action, setAction] = useState("RelinkIncorrectAttachment");
  const [target, setTarget] = useState<{ revision: number; villageIds: string[]; villages: { villageId: string; name: string }[] }>();
  const [villageId, setVillageId] = useState("");
  const [reason, setReason] = useState("");
  const [pages, setPages] = useState("");
  const [coverage, setCoverage] = useState("");
  const [confirmed, setConfirmed] = useState(false);
  const [busy, setBusy] = useState(false);
  const [message, setMessage] = useState("");
  const receipt = useRef<{ payload: string; id: string } | undefined>(undefined);
  const source = conflict.existingLinks.find(x => x.awardId === sourceId);
  useEffect(() => {
    let current = true;
    fetch(`/api/awards/${conflict.selectedAwardId}/staff-workflow`, { credentials: "include" })
      .then(r => landRecordResponse<{ revision: number; villageIds: string[]; villages: { villageId: string; name: string }[] }>(r))
      .then(data => { if (current) { setTarget(data); if (data.villageIds.length === 1) setVillageId(data.villageIds[0]); } })
      .catch(error => { if (current) setMessage(error.message); });
    return () => { current = false; };
  }, [conflict.selectedAwardId]);
  const resolve = async () => {
    if (!source || !target || !conflict.documentId) return;
    const sourcePages = pages.split(",").map(x => Number(x.trim()));
    if (action === "ShareSource" && (!pages.trim() || sourcePages.some(x => !Number.isInteger(x) || x < 1))) { setMessage("Enter the source page numbers, separated by commas."); return; }
    const input = { action, sourceAwardId: source.awardId, targetAwardId: conflict.selectedAwardId, villageId,
      expectedSourceAwardRevision: source.awardRevision, expectedTargetAwardRevision: target.revision,
      expectedSourceAttachmentRevision: source.attachmentRevision, expectedSha256Hash: conflict.sha256Hash,
      officerConfirmed: confirmed, reason, sourcePages: action === "ShareSource" ? sourcePages : null, coverageDescription: action === "ShareSource" ? coverage : null };
    const payload = JSON.stringify(input);
    if (receipt.current?.payload !== payload) receipt.current = { payload, id: crypto.randomUUID() };
    try {
      setBusy(true); setMessage("");
      const result = await landRecordResponse<AwardPdfLinkResolutionResult>(await fetch(`/api/documents/${source.documentId || conflict.documentId}/award-link-resolutions`, {
        method: "POST", credentials: "include", headers: { "Content-Type": "application/json" }, body: JSON.stringify({ ...input, mutationId: receipt.current.id }),
      }));
      onResolved(result);
    } catch (error) { setMessage(error instanceof Error ? error.message : "Link correction failed."); }
    finally { setBusy(false); }
  };
  return <section className="section" role="alert" aria-label="PDF ownership conflict">
    <h3>This PDF is already attached to another Award</h3>
    <p>Check the existing Award and the source PDF before choosing a correction.</p>
    {conflict.existingLinks.map(link => <p key={link.attachmentId}>
      <Link to={`/awards/${link.awardId}`}>Open Award {link.awardNumber}</Link> · {link.villages.map(v => v.name).join(", ")}
    </p>)}
    {conflict.contentPath && <a href={conflict.contentPath} target="_blank" rel="noreferrer">View {conflict.documentReference || "existing PDF"}</a>}
    <p>If you selected the wrong Award, open its existing Award above and continue there.</p>
    {conflict.hasRestrictedLinks && <p>Some links require an officer with access to those records.</p>}
    {source?.canCorrect && conflict.documentId && <details><summary>Officer link correction</summary>
      {conflict.existingLinks.length > 1 && <label>Existing Award<select value={sourceId} onChange={e => setSourceId(e.target.value)}>{conflict.existingLinks.map(x => <option key={x.awardId} value={x.awardId}>{x.awardNumber}</option>)}</select></label>}
      <label>Reason for resolution<select value={action} onChange={e => { setAction(e.target.value); setConfirmed(false); }}>
        <option value="RelinkIncorrectAttachment">The PDF was attached to the wrong Award</option>
        <option value="VoidDuplicateAndRelink">The previous Award is a duplicate entry</option>
        <option value="ShareSource">The source PDF covers both Awards</option>
      </select></label>
      <label>Target Village<select value={villageId} onChange={e => setVillageId(e.target.value)}><option value="">Select linked Village</option>{target?.villageIds.map(id => <option key={id} value={id}>{target.villages.find(v => v.villageId === id)?.name || source.villages.find(v => v.villageId === id)?.name || id}</option>)}</select></label>
      <label>Officer reason<textarea value={reason} onChange={e => setReason(e.target.value)} /></label>
      {action === "ShareSource" && <><label>Source pages covering both Awards<input value={pages} onChange={e => setPages(e.target.value)} placeholder="1, 2" /></label><label>Explain source coverage<textarea value={coverage} onChange={e => setCoverage(e.target.value)} /></label></>}
      <p>The original PDF, old links, verified evidence and extraction history will remain available in the audit record.</p>
      <label><input type="checkbox" checked={confirmed} onChange={e => setConfirmed(e.target.checked)} />I reviewed the source and confirm this resolution.</label>
      <button disabled={busy || !target || !confirmed || !reason.trim() || !villageId || action === "ShareSource" && !coverage.trim()} onClick={() => void resolve()}>{busy ? "Saving…" : "Confirm link resolution"}</button>
    </details>}
    {message && <p>{message}</p>}
  </section>;
}
