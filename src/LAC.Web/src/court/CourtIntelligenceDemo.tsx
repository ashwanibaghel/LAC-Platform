// Explicit development-only public-order demo; not a route in the production app.
import { useState } from "react";
import { createRoot } from "react-dom/client";
import { CourtIntelligence } from "./CourtIntelligence";
const cases = ["W.P.(C) 1784/2026", "W.P.(C) 7689/2000", "CO.PET. 39/2009", "W.P.(C) 8611/2019", "W.P.(C) 7363/2015", "W.P.(C) 8664/2021", "W.P.(C) 568/2024", "W.P.(C) 17415/2024", "W.P.(C) 2687/2018", "W.P.(C) 120/2026"];
const showcase = ["Section 18 reference direction", "Compensation dispute", "Possession and acquisition history", "Reference limitation", "Section 30/31 reference"];
function Demo() {
  const [selected, setSelected] = useState(5);
  return <main style={{ maxWidth: 1160, margin: "32px auto", padding: "0 24px" }}>
    <p style={{ fontSize: 12, color: "#526e88" }}>LAC PLATFORM · LOCAL PUBLIC-ORDER DEMO</p>
    <label htmlFor="demo-matter">Matter </label><select id="demo-matter" value={selected} onChange={event => setSelected(Number(event.target.value))} style={{ padding: 10, borderRadius: 8, border: "1px solid #ccdbe8" }}><optgroup label="Verified evidence showcase">{cases.slice(5).map((value,index)=><option key={value} value={index+5}>{value} · {showcase[index]}</option>)}</optgroup><optgroup label="Additional test matters">{cases.slice(0,5).map((value,index)=><option key={value} value={index}>{value}</option>)}</optgroup></select>
    <CourtIntelligence key={selected} showMatterHeader caseId={`a1000000-0000-4000-8000-${String(selected+1).padStart(12,"0")}`} />
  </main>;
}
if (import.meta.env.DEV) createRoot(document.getElementById("root")!).render(<Demo />);
