import { useEffect, useState } from "react";
import { Link } from "react-router-dom";

type ImportSource = {
  batchId: string; importRowId: string; sourceRowNumber: number;
  rawDirections?: string; rawBriefFacts?: string; rawLastOrderLink?: string;
  lastOrderLinkState?: string; rawStatus?: string; rawNdoh?: string;
  parsedNdoh?: string; committedProceedingId?: string; committedAt?: string;
};

export function CourtImportProvenance({ caseId }: { caseId: string }) {
  const [sources, setSources] = useState<ImportSource[]>([]);
  useEffect(() => {
    const controller = new AbortController();
    fetch("/api/court-cases/" + caseId + "/import-provenance", { credentials: "include", signal: controller.signal })
      .then(async r => r.ok ? await r.json() as ImportSource[] : [])
      .then(setSources).catch(() => {});
    return () => controller.abort();
  }, [caseId]);
  if (!sources.length) return null;
  return <section style={{ marginTop: 24 }}>
    <h3>Office-register import provenance</h3>
    <p>These are original source notes, not verified court orders or proceedings.</p>
    {sources.map(source => <div key={source.importRowId} style={{ border: "1px solid currentColor", padding: 12, marginBottom: 8 }}>
      <p><Link to={"/court-cases/imports/" + source.batchId}>Import batch</Link> · Excel row {source.sourceRowNumber} · Imported {source.committedAt || "—"}</p>
      <p>Directions: {source.rawDirections || "—"}</p>
      <p>Brief facts: {source.rawBriefFacts || "—"}</p>
      <p>Source status: {source.rawStatus || "—"} · Source NDOH: {source.rawNdoh || "—"} · Parsed NDOH: {source.parsedNdoh || "—"}</p>
      <p>Last-order URL: {source.lastOrderLinkState === "ValidHttpUrl" && source.rawLastOrderLink
        ? <a href={source.rawLastOrderLink} target="_blank" rel="noopener noreferrer">{source.rawLastOrderLink}</a>
        : source.rawLastOrderLink || "—"} ({source.lastOrderLinkState || "—"})</p>
    </div>)}
  </section>;
}
