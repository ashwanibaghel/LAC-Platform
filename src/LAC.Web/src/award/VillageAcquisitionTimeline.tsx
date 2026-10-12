import { useEffect, useState } from "react";
import { Link } from "react-router-dom";
import { landRecordResponse } from "./LandRecordApiError";

type TimelineEvent = { eventId: string; title: string; legalDate?: string; sourcePath: string; needsReview: boolean;
  warnings: string[]; documents: { documentId: string; contentPath: string }[] };
export function VillageAcquisitionTimeline({ villageId }: { villageId: string }) {
  const [events, setEvents] = useState<TimelineEvent[]>();
  const [error, setError] = useState("");
  useEffect(() => {
    let current = true;
    fetch(`/api/villages/${villageId}/acquisition-timeline`, { credentials: "include" })
      .then(r => landRecordResponse<{ events: TimelineEvent[] }>(r)).then(data => { if (current) setEvents(data.events); })
      .catch(e => { if (current) setError(e.message); });
    return () => { current = false; };
  }, [villageId]);
  return <section className="section" aria-label="Village acquisition timeline"><h2>Acquisition timeline</h2>
    <p>Legal event dates from linked records and source evidence.</p>
    {error ? <p role="alert">{error}</p> : !events ? <p>Loading timeline…</p> : !events.length ? <p>No linked legal events.</p> :
      <ol>{events.map(event => <li key={event.eventId}><strong>{event.legalDate || "Date needs review"}</strong> ·
        {event.sourcePath.startsWith("/api/") ? <a href={event.sourcePath}>{event.title}</a> : <Link to={event.sourcePath}>{event.title}</Link>}
        {event.documents.map((doc, index) => <span key={doc.documentId}> · <a href={doc.contentPath} target="_blank" rel="noreferrer">Source PDF {index + 1}</a></span>)}
        {event.needsReview && <p>{event.warnings.join(" ")}</p>}
      </li>)}</ol>}
  </section>;
}
