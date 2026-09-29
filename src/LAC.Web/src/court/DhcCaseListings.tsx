import React, { useEffect, useState } from "react";
import type { DhcObservationDto } from "./types";

export const DhcCaseListings: React.FC<{ caseId: string }> = ({ caseId }) => {
  const [items, setItems] = useState<DhcObservationDto[]>([]);
  useEffect(() => {
    const controller = new AbortController();
    void fetch(`/api/court-cases/${caseId}/dhc-listings`, { credentials: "include", signal: controller.signal })
      .then(response => response.ok ? response.json() as Promise<DhcObservationDto[]> : [])
      .then(setItems).catch(() => {});
    return () => controller.abort();
  }, [caseId]);
  return (
    <section className="court-card" style={{ padding: 16, marginTop: 16 }}>
      <h3>Delhi High Court date updates</h3>
      <p>These are dates published in the official cause list. They do not mean a hearing took place.</p>
      {items.length === 0 ? <p>No official Delhi High Court dates have been found for this case.</p> :
        <ul>{items.map(item => <li key={item.id} style={{ marginBottom: 12 }}>
          <strong>{item.listingDate}</strong> · {item.status === "NeedsReview" ? "Needs attention" : "Official date recorded"}
          <details><summary>Source details</summary><div>{item.sourceTitle} · page {item.pageNumber}</div>
            <div>{item.rawMatchedText}</div>
            {item.conflictReason && <div>{item.conflictReason}</div>}
            <a href={item.sourceUrl} target="_blank" rel="noreferrer">Official source</a>
            {item.documentId && <> · <a href={`/api/court-cases/dhc-sync/documents/${item.documentId}/content`} target="_blank" rel="noreferrer">Saved PDF</a></>}
          </details>
        </li>)}</ul>}
    </section>
  );
};
