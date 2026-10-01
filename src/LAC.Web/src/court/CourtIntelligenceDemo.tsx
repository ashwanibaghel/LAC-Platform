// Explicit development-only public-order demo; not a route in the production app.
import { useState } from "react";
import { createRoot } from "react-dom/client";
import { CourtIntelligence } from "./CourtIntelligence";
const cases = ["W.P.(C) 1784/2026", "W.P.(C) 7689/2000", "CO.PET. 39/2009", "W.P.(C) 8611/2019", "W.P.(C) 7363/2015"];
function Demo() {
  const [selected, setSelected] = useState(0);
  return <main style={{ maxWidth: 1160, margin: "32px auto", padding: "0 24px" }}>
    <p style={{ fontSize: 12, color: "#526e88" }}>LAC PLATFORM · LOCAL PUBLIC-ORDER DEMO</p>
    <h1 style={{ color: "#183751" }}>Court Matter Intelligence</h1>
    <p style={{ color: "#61768c" }}>Real official order evidence · Isolated intelligence files · Not the Court register</p>
    <label htmlFor="demo-matter">Matter </label><select id="demo-matter" value={selected} onChange={event => setSelected(Number(event.target.value))} style={{ padding: 10, borderRadius: 8, border: "1px solid #ccdbe8" }}>{cases.map((value, index) => <option key={value} value={index}>{value}</option>)}</select>
    <CourtIntelligence key={selected} caseId={`a1000000-0000-4000-8000-00000000000${selected+1}`} />
  </main>;
}
if (import.meta.env.DEV) createRoot(document.getElementById("root")!).render(<Demo />);
