import React, { useEffect, useState } from "react";
import "./court-intelligence.css";

type Source = { orderDate: string | null; page: number; evidence: string; officialUrl: string; evidenceParts?: { page: number; evidence: string }[] };
type Fact = { category: string; field: string; value: string; page: number; evidence: string; scope: string; evidenceParts?: { page: number; evidence: string }[] };
type Proposition = { id?: string; role: string; attribution: string; scope: string; text: string; source: Source };
type Order = { orderDate: string | null; officialUrl: string; status: string; facts: Fact[]; summaryFacts?: Fact[]; nextHearingDate?: string | null; court?: string | null; digest?: Proposition[]; presentationKind?: "Routine" | "Substantive" | "Unverified"; officeActionCount?: number };
type Action = { id: string; type?: string; text: string; actor: string; deadlineText: string | null; dueDate: string | null; source: Source };
type CaptionEntry = { text: string; source: Source };
type Intelligence = { caseNumber?: string; status: string; processingComplete: boolean; currentPosition: { text: string; source: Source; attribution?: string; role?: string; scope?: string }[]; beforeNextHearing: Action[]; latestOrder: Order | null; finalOrder?: Order | null; latestMeaningfulOrder?: Order | null; chronologyWarnings?: string[]; orders: Order[]; caption?: Record<string, CaptionEntry | string>; lacCaptionAppearances?: CaptionEntry[]; factualChronology?: { dates: string[]; text: string; role: string; source: Source }[]; sourceCoverage?: { basis: string; knownSources: number; checkedSources: number; gaps: { orderDate: string | null; officialUrl: string; reason: string }[] } };
type Answer = { answer: string; claims: { text: string; attribution: string; source: Source }[]; insufficientEvidence: boolean; reason?: string };
type RegisteredIntelligence = Intelligence & { caseId: string; knownOrderCount?: number; unprocessedOrderCount?: number; unusableKnownOrderCount?: number; refreshState?: { caseId: string; status: string; checked?: number; total?: number; needsReview?: number; message?: string } };

const officialLink = (url: string) => {
  try {
    const parsed = new URL(url);
    return parsed.protocol === "https:" &&
      parsed.hostname === "delhihighcourt.nic.in" &&
      !parsed.username &&
      !parsed.password &&
      !parsed.search &&
      !parsed.hash &&
      (!parsed.port || parsed.port === "443") &&
      ["/app/showlogo/", "/app/showFileJudgment/", "/app/case_number_pdf/", "/app/downloadOrderbByDate/"].some(prefix => parsed.pathname.startsWith(prefix))
      ? url
      : null;
  } catch {
    return null;
  }
};

const registeredPayload = (value: unknown, caseId: string): value is RegisteredIntelligence =>
  !!value &&
  typeof value === "object" &&
  "caseId" in value &&
  value.caseId === caseId &&
  "orders" in value &&
  Array.isArray(value.orders) &&
  "currentPosition" in value &&
  Array.isArray(value.currentPosition) &&
  "beforeNextHearing" in value &&
  Array.isArray(value.beforeNextHearing);

const officerText = (text: string) => text.replace(/^\s*(?:\d+[.)]|\(\d+\)|\([a-z]\))\s+/i, "");
const safeOrderFacts = (order: Order) => order.summaryFacts ?? (order.status === "Validated" ? order.facts : []);
const currentFacts = (order: Order) =>
  safeOrderFacts(order).filter(fact => fact.scope === "Current" && ["COURT_DIRECTION", "COURT_FINDING", "PROCEDURAL_EVENT"].includes(fact.category));

const roleLabels: Record<string, string> = {
  PETITIONER_SUBMISSION: "Petitioner submission",
  LAC_OR_RESPONDENT_SUBMISSION: "LAC/respondent submission",
  OTHER_PARTY_SUBMISSION: "Other party submission",
  COURT_DIRECTION: "Court direction",
  COURT_OBSERVATION: "Court observation",
  COURT_FINDING: "Court finding",
  CASE_CONTEXT: "Case context",
  ISSUE_BEFORE_COURT: "Issue before Court",
  PROCEDURAL_EVENT: "Procedural event",
  DISPOSITION: "Disposition",
  RECORDED_COMPLIANCE: "Recorded compliance",
  NEXT_HEARING: "Next date",
  COMPENSATION_FACT: "Compensation record",
  POSSESSION_FACT: "Possession record",
  REFERENCE_FACT: "Reference record",
  AWARD_FACT: "Award record",
  KHASRA_FACT: "Khasra record",
  DOCUMENT_OR_FILING_FACT: "Filing record",
  DEADLINE: "Recorded period"
};

const roleLabel = (role: string) => roleLabels[role] ?? "Recorded factual reference";

const roleClass = (role?: string) => {
  if (!role) return "";
  const r = role.toUpperCase();
  if (r.includes("FINDING")) return "role-court-finding";
  if (r.includes("DIRECTION")) return "role-court-direction";
  if (r.includes("OBSERVATION")) return "role-court-observation";
  if (r.includes("PETITIONER")) return "role-petitioner-submission";
  if (r.includes("LAC") || r.includes("RESPONDENT")) return "role-lac-or-respondent-submission";
  if (r.includes("COMPLIANCE")) return "role-recorded-compliance";
  if (r.includes("COMPENSATION")) return "role-compensation-fact";
  if (r.includes("POSSESSION")) return "role-possession-fact";
  if (r.includes("REFERENCE") || r.includes("AWARD")) return "role-award-fact";
  return "role-procedural-event";
};

const orderDigest = (order: Order): Proposition[] =>
  order.digest ?? safeOrderFacts(order)
    .filter(fact => !["Uncertain", "Quoted"].includes(fact.scope))
    .slice(0, 6)
    .map(fact => ({
      role: fact.category,
      attribution: roleLabel(fact.category),
      scope: fact.scope,
      text: fact.value,
      source: { orderDate: order.orderDate, page: fact.page, evidence: fact.evidence, officialUrl: order.officialUrl }
    }));

const timelineLabel = (order: Order) => {
  const digest = orderDigest(order);
  if (!digest.length) return "Source check needed";
  if (digest.some(entry => entry.role === "COURT_DIRECTION")) return "Court direction recorded";
  if (digest.some(entry => entry.role === "COURT_OBSERVATION")) return "Court observation recorded";
  if (digest.some(entry => entry.role.includes("SUBMISSION"))) return "Party positions considered";
  return "Case development recorded";
};

const confirmedNextHearing = (order: Order | null) =>
  !!order?.nextHearingDate &&
  /^\d{4}-\d{2}-\d{2}$/.test(order.nextHearingDate) &&
  safeOrderFacts(order).some(
    fact => fact.field === "nextHearing" && fact.scope === "Current" && ["COURT_DIRECTION", "PROCEDURAL_EVENT", "NEXT_HEARING"].includes(fact.category)
  );

const shownDate = (value: string | null) => {
  if (!value) return "Date not confirmed";
  const date = new Date(`${value}T00:00:00Z`);
  return Number.isNaN(date.getTime())
    ? "Date needs verification"
    : new Intl.DateTimeFormat("en-GB", { day: "numeric", month: "short", year: "numeric", timeZone: "UTC" }).format(date);
};

const officeToday = () => {
  const parts = new Intl.DateTimeFormat("en-CA", { timeZone: "Asia/Kolkata", year: "numeric", month: "2-digit", day: "2-digit" }).formatToParts(new Date());
  return ["year", "month", "day"].map(type => parts.find(part => part.type === type)!.value).join("-");
};

const upcomingHearing = (order: Order | null, today = officeToday()) =>
  confirmedNextHearing(order) && order!.nextHearingDate! >= today;

const actionPeriod = (action: Action, today = officeToday()) => {
  if (!action.dueDate) return `${action.deadlineText ?? "No deadline stated"} · completion not confirmed`;
  if (/\bpreferably\b/i.test(action.deadlineText ?? ""))
    return `Preferred period ${action.dueDate < today ? "ended" : "ends"} ${shownDate(action.dueDate)} · completion not confirmed`;
  return `${action.deadlineText ?? "No deadline stated"} · Due ${shownDate(action.dueDate)} · completion not confirmed`;
};

const Evidence: React.FC<{ source: Source }> = ({ source }) => (
  <details className="court-intelligence-evidence">
    <summary>View evidence</summary>
    <div className="court-evidence-body">
      <p className="court-evidence-meta">{shownDate(source.orderDate)} · Page {source.page}</p>
      {source.evidenceParts?.length ? (
        source.evidenceParts.map((part, index) => (
          <div key={index} className="court-evidence-part">
            <small>Page {part.page}</small>
            <blockquote>{part.evidence}</blockquote>
          </div>
        ))
      ) : (
        <blockquote>{source.evidence}</blockquote>
      )}
      {officialLink(source.officialUrl) && (
        <a href={officialLink(source.officialUrl)!} target="_blank" rel="noreferrer" className="court-official-order-link">
          Open official order ↗
        </a>
      )}
    </div>
  </details>
);

const actionTitle = (action: Action) =>
  /status report/i.test(action.text)
    ? "Status report"
    : /affidavit/i.test(action.text)
    ? "Status affidavit"
    : action.type === "Reference"
    ? "Court reference"
    : action.type === "Compensation"
    ? "Compensation action"
    : "Outstanding LAC action";

const ActionRow: React.FC<{ action: Action }> = ({ action }) => (
  <div className="court-intelligence-action">
    <div className="court-action-topline">
      <h5>{actionTitle(action)}</h5>
      <small className="court-action-deadline">{actionPeriod(action)}</small>
    </div>
    <p>{officerText(action.text)}</p>
    <Evidence source={action.source} />
  </div>
);

const OrderSummary: React.FC<{ order: Order }> = ({ order }) => {
  const digest = orderDigest(order);
  return (
    <>
      {!digest.length ? (
        <p className="court-intelligence-muted">This order needs source verification before its contents can be summarized.</p>
      ) : (
        <>
          <p className="court-intelligence-label">What happened in this order</p>
          <ul className="court-intelligence-order-points">
            {digest.slice(0, 6).map((entry, index) => (
              <li key={entry.id ?? index} className="court-order-point-item">
                <small className={`court-role-pill ${roleClass(entry.role)}`}>
                  {roleLabel(entry.role)}
                  {entry.scope === "Historical" ? " · historical reference" : ""}
                </small>
                <p>{officerText(entry.text)}</p>
                <Evidence source={entry.source} />
              </li>
            ))}
          </ul>
          {order.officeActionCount === 0 && (
            <small className="court-order-no-action">No direct LAC action was identified in this order.</small>
          )}
        </>
      )}
      {confirmedNextHearing(order) && (
        <p className="court-intelligence-hearing">
          Source-listed next date <strong>{shownDate(order.nextHearingDate!)}</strong>
        </p>
      )}
      {officialLink(order.officialUrl) && (
        <a href={officialLink(order.officialUrl)!} target="_blank" rel="noreferrer" className="court-official-order-link">
          Open official order ↗
        </a>
      )}
    </>
  );
};

const FullOrderFacts: React.FC<{ order: Order }> = ({ order }) => (
  <details className="court-full-order-facts">
    <summary>Full verified order brief</summary>
    <div className="court-facts-list">
      {safeOrderFacts(order)
        .filter(fact => !["Quoted", "Uncertain"].includes(fact.scope))
        .map((fact, index) => (
          <div key={index} className="court-fact-row">
            <small className={`court-role-pill ${roleClass(fact.category)}`}>
              {roleLabel(fact.category)}
              {fact.scope === "Historical" ? " · historical reference" : ""}
            </small>
            <p>{officerText(fact.value)}</p>
            <Evidence
              source={{
                orderDate: order.orderDate,
                page: fact.page,
                evidence: fact.evidence,
                evidenceParts: fact.evidenceParts,
                officialUrl: order.officialUrl
              }}
            />
          </div>
        ))}
    </div>
  </details>
);

export const CourtIntelligence: React.FC<{ caseId: string; showMatterHeader?: boolean }> = ({ caseId, showMatterHeader = false }) => {
  const [storedData, setData] = useState<RegisteredIntelligence | null>(null);
  const [unavailable, setUnavailable] = useState(false);
  const [question, setQuestion] = useState("");
  const [answer, setAnswer] = useState<Answer | null>(null);
  const [asking, setAsking] = useState(false);
  const [askError, setAskError] = useState("");
  const [historyOpen, setHistoryOpen] = useState(false);
  const [refreshing, setRefreshing] = useState(false);
  const [refreshError, setRefreshError] = useState("");

  const activeCase = React.useRef(caseId);
  activeCase.current = caseId;
  const epoch = React.useRef(0);
  const questionAbort = React.useRef<AbortController | null>(null);
  const refreshAbort = React.useRef<AbortController | null>(null);

  const data = storedData?.caseId === caseId ? storedData : null;

  useEffect(() => {
    const controller = new AbortController();
    const generation = ++epoch.current;
    setData(null);
    setUnavailable(false);
    setQuestion("");
    setAnswer(null);
    setAskError("");
    setHistoryOpen(false);
    setAsking(false);
    setRefreshing(false);
    setRefreshError("");

    fetch(`/api/court-cases/${caseId}/intelligence`, { signal: controller.signal, cache: "no-store" })
      .then(async response => {
        if (response.status === 204) return null;
        if (!response.ok) throw new Error("Unavailable");
        const result: unknown = await response.json();
        if (!registeredPayload(result, caseId)) throw new Error("Invalid case intelligence");
        return result;
      })
      .then(result => {
        if (!controller.signal.aborted && epoch.current === generation && activeCase.current === caseId) {
          setData(result);
          setHistoryOpen((result?.orders.length ?? 0) > 1);
        }
      })
      .catch(() => {
        if (!controller.signal.aborted && epoch.current === generation) setUnavailable(true);
      });

    return () => {
      controller.abort();
      questionAbort.current?.abort();
      refreshAbort.current?.abort();
    };
  }, [caseId]);

  const running = data?.refreshState?.status === "Running";

  useEffect(() => {
    if (!running) return;
    const controller = new AbortController();
    const generation = epoch.current;
    let inFlight = false;
    const timer = setInterval(async () => {
      if (inFlight) return;
      inFlight = true;
      try {
        const response = await fetch(`/api/court-cases/${caseId}/intelligence`, { signal: controller.signal, cache: "no-store" });
        if (!response.ok) return;
        const result: unknown = await response.json();
        if (!controller.signal.aborted && generation === epoch.current && activeCase.current === caseId && registeredPayload(result, caseId)) {
          setData(result);
        }
      } catch {
        /* Existing structured brief stays readable during transient polling failure. */
      } finally {
        inFlight = false;
      }
    }, 5000);

    return () => {
      clearInterval(timer);
      controller.abort();
    };
  }, [caseId, running]);

  const ask = async (event: React.FormEvent) => {
    event.preventDefault();
    if (asking || !question.trim()) return;
    const requestedCase = caseId;
    const generation = epoch.current;
    const controller = new AbortController();
    questionAbort.current = controller;
    setAsking(true);
    setAnswer(null);
    setAskError("");

    try {
      const response = await fetch(`/api/court-cases/${caseId}/intelligence/ask`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ question }),
        signal: controller.signal,
        cache: "no-store"
      });
      if (!response.ok) throw new Error("Unavailable");
      const result = (await response.json()) as Answer & { caseId: string };
      if (result.caseId !== requestedCase || !Array.isArray(result.claims)) throw new Error("Wrong case response");
      if (!controller.signal.aborted && generation === epoch.current && activeCase.current === requestedCase) {
        setAnswer(result);
      }
    } catch {
      if (!controller.signal.aborted && generation === epoch.current && activeCase.current === requestedCase) {
        setAskError("Question answering is temporarily unavailable. Case intelligence remains available.");
      }
    } finally {
      if (generation === epoch.current && activeCase.current === requestedCase) setAsking(false);
    }
  };

  const refresh = async () => {
    if (refreshing || running) return;
    const generation = epoch.current;
    const requestedCase = caseId;
    const controller = new AbortController();
    refreshAbort.current = controller;
    setRefreshing(true);
    setRefreshError("");

    try {
      const response = await fetch(`/api/court-cases/${caseId}/intelligence/refresh`, {
        method: "POST",
        signal: controller.signal,
        cache: "no-store"
      });
      if (response.status !== 202) throw new Error("Unavailable");
      const state = await response.json();
      if (state.caseId !== requestedCase) throw new Error("Wrong case");
      if (!controller.signal.aborted && generation === epoch.current && activeCase.current === requestedCase) {
        setData(previous => (previous ? { ...previous, refreshState: state } : previous));
      }
    } catch {
      if (!controller.signal.aborted && generation === epoch.current && activeCase.current === requestedCase) {
        setRefreshError("Local intelligence processing is temporarily unavailable or busy. Your Court records and verified evidence remain available.");
      }
    } finally {
      if (generation === epoch.current && activeCase.current === requestedCase) setRefreshing(false);
    }
  };

  const reviewCount = data?.orders.filter(order => order.status !== "Validated").length ?? 0;
  const verifiedFacts = data?.orders.flatMap(safeOrderFacts) ?? [];
  const suggestions = data
    ? [
        ...(data.latestOrder && currentFacts(data.latestOrder).some(fact => fact.category === "COURT_DIRECTION")
          ? [{ label: "Latest direction", question: "What did the latest order direct?" }]
          : []),
        ...(data.beforeNextHearing.length ? [{ label: "What is pending from LAC?", question: "What is still pending from LAC?" }] : []),
        ...(verifiedFacts.some(fact => fact.field === "compensation")
          ? [{ label: "Compensation position", question: "What has happened regarding compensation?" }]
          : []),
        ...(verifiedFacts.some(fact => fact.field === "possession")
          ? [{ label: "Possession position", question: "What is the possession position?" }]
          : []),
        ...(data.orders.some(order => order.orderDate?.startsWith("2025") && safeOrderFacts(order).length)
          ? [{ label: "What happened in 2025?", question: "What happened in this case during 2025?" }]
          : []),
        ...(confirmedNextHearing(data.latestOrder) && data.beforeNextHearing.length
          ? [{ label: "Next hearing preparation", question: "What does LAC need to do before the next hearing?" }]
          : [])
      ]
    : [];

  return (
    <section className="court-intelligence" aria-label="Court Intelligence">
      <header className="court-intelligence-header">
        <div className="court-intelligence-title-lockup">
          <span className="court-intelligence-emblem" aria-hidden="true">🏛</span>
          <div>
            <h3>Court Intelligence</h3>
            <p className="court-intelligence-header-desc">Synthesized from official Court orders & proceedings</p>
          </div>
        </div>
        <span>Evidence-backed office brief</span>
      </header>

      {!data ? (
        <div className="court-intelligence-empty-card">
          <p className="court-intelligence-empty">
            {unavailable
              ? "Court intelligence is temporarily unavailable. Your Court records remain available."
              : "No reviewed intelligence is available yet. Court records and official order links remain unchanged."}
          </p>
        </div>
      ) : (
        <>
          {data.knownOrderCount !== undefined && (
            <div role="status" className="court-intelligence-status-bar">
              <div className="court-intelligence-status-message">
                {data.knownOrderCount === 0 ? (
                  <p>No official order is indexed for this registered matter. Current position is not inferred.</p>
                ) : (
                  <>
                    {!!data.unprocessedOrderCount && (
                      <p className="court-intelligence-status-pending">
                        <span className="court-status-pill pending">Pending</span>
                        Intelligence not processed yet for {data.unprocessedOrderCount} known order{data.unprocessedOrderCount === 1 ? "" : "s"}. Only verified evidence is shown below.
                      </p>
                    )}
                    {!!data.unusableKnownOrderCount && (
                      <p className="court-intelligence-status-review">
                        <span className="court-status-pill review">Needs Review</span>
                        {data.unusableKnownOrderCount} known source{data.unusableKnownOrderCount === 1 ? " needs" : "s need"} case/date/source verification before processing.
                      </p>
                    )}
                    {!data.unprocessedOrderCount && !data.unusableKnownOrderCount && (
                      <p className="court-intelligence-status-synced">
                        <span className="court-status-pill verified">Verified Index</span>
                        {data.knownOrderCount} official Court {data.knownOrderCount === 1 ? "order" : "orders"} indexed &amp; verified.
                      </p>
                    )}
                    {data.orders.length > 0 && (
                      <button
                        type="button"
                        className="court-intelligence-btn-action"
                        onClick={refresh}
                        disabled={refreshing || running}
                      >
                        {refreshing || running
                          ? "Processing intelligence…"
                          : data.unprocessedOrderCount
                          ? "Process known orders"
                          : "Refresh intelligence"}
                      </button>
                    )}
                  </>
                )}
                {running && (
                  <p className="court-intelligence-status-running">
                    <span className="court-intelligence-spinner" aria-hidden="true" />
                    Checking this matter's known official orders… {data.refreshState?.checked ?? 0} checked. Existing verified evidence stays available.
                  </p>
                )}
                {["Failed", "Interrupted"].includes(data.refreshState?.status ?? "") && (
                  <p className="court-intelligence-status-alert">
                    Intelligence processing did not finish. Existing verified evidence is unchanged; you can retry explicitly.
                  </p>
                )}
                {data.refreshState?.status === "CompletedWithReview" && (
                  <p className="court-intelligence-status-review">
                    <span className="court-status-pill review">Review Required</span>
                    Intelligence checked · {data.refreshState.needsReview ?? 0} sources need review. Unverified material contributes no facts.
                  </p>
                )}
                {refreshError && <p className="court-intelligence-status-alert">{refreshError}</p>}
              </div>
            </div>
          )}

          {showMatterHeader && (
            <div className="court-intelligence-matter">
              <h2>{data.caseNumber ?? "Court matter"}</h2>
              <div className="court-matter-badges">
                {data.latestOrder?.court && <span className="court-meta-tag">{data.latestOrder.court}</span>}
                {confirmedNextHearing(data.latestOrder) && (
                  <span className={`court-meta-tag ${upcomingHearing(data.latestOrder) ? "tag-hearing-upcoming" : "tag-hearing-past"}`}>
                    {upcomingHearing(data.latestOrder) ? "Next source-listed hearing" : "Last source-listed date"}:{" "}
                    {shownDate(data.latestOrder!.nextHearingDate!)}
                  </span>
                )}
              </div>
              {confirmedNextHearing(data.latestOrder) && !upcomingHearing(data.latestOrder) && (
                <p className="court-intelligence-muted">
                  That date has passed. A subsequent order or new hearing date is not established by the processed sources.
                </p>
              )}
            </div>
          )}

          {(reviewCount > 0 || !data.processingComplete) && (
            <details className="court-intelligence-verification">
              <summary>
                Some source material still needs verification.
                {reviewCount > 0 ? ` · ${reviewCount} order${reviewCount === 1 ? "" : "s"}` : ""}
              </summary>
              <p>Actions and summaries shown here use only verified evidence.</p>
            </details>
          )}

          {data.caption && (
            <article className="court-intelligence-position court-intelligence-caption-card">
              <h4>{typeof data.caption.title === "string" ? data.caption.title : "Source-confirmed case context"}</h4>
              <dl>
                {[
                  ["bench", "Bench"],
                  ["petitionerAdvocates", "Advocates for petitioner"],
                  ["respondentAdvocates", "Advocates for respondents / LAC"]
                ].map(([key, label]) => {
                  const entry = data.caption?.[key];
                  return entry && typeof entry !== "string" ? (
                    <div key={key} className="court-caption-row">
                      <dt>{label}</dt>
                      <dd>
                        {entry.text}
                        <Evidence source={entry.source} />
                      </dd>
                    </div>
                  ) : null;
                })}
              </dl>
            </article>
          )}

          {data.finalOrder && (
            <article className="court-intelligence-latest court-final-outcome-card">
              <div className="court-intelligence-region-heading">
                <h4>Final judicial outcome</h4>
                <time>{shownDate(data.finalOrder.orderDate)}</time>
              </div>
              <OrderSummary order={data.finalOrder} />
              <FullOrderFacts order={data.finalOrder} />
              <p className="court-intelligence-muted">
                Source judicial outcome only. The office register is not changed automatically; surviving actions remain below.
              </p>
            </article>
          )}

          {!!data.chronologyWarnings?.length && (
            <div className="court-intelligence-warning-banner">
              <p className="court-intelligence-muted">
                The supplied order history has a continuity gap. An earlier judicial outcome must not be assumed to establish the present case status.
              </p>
            </div>
          )}

          {!data.finalOrder && data.latestMeaningfulOrder && data.latestMeaningfulOrder.orderDate !== data.latestOrder?.orderDate && (
            <details className="court-intelligence-history court-substantive-history">
              <summary>Earlier substantive development · {shownDate(data.latestMeaningfulOrder.orderDate)}</summary>
              <div className="court-history-inner">
                <OrderSummary order={data.latestMeaningfulOrder} />
                <FullOrderFacts order={data.latestMeaningfulOrder} />
              </div>
            </details>
          )}

          {!!data.lacCaptionAppearances?.length && (
            <details className="court-intelligence-history court-counsel-appearances">
              <summary>LAC counsel appearances in processed orders</summary>
              <div className="court-counsel-list">
                {data.lacCaptionAppearances.map((entry, index) => (
                  <div className="court-intelligence-history-row" key={index}>
                    <small>{shownDate(entry.source.orderDate)} · Exact respondent caption; other respondents may also be named</small>
                    <p>{entry.text}</p>
                    <Evidence source={entry.source} />
                  </div>
                ))}
              </div>
            </details>
          )}

          <article
            className={`court-intelligence-attention${
              data.beforeNextHearing.length ? "" : " court-intelligence-attention-empty"
            }`}
          >
            <div className="court-intelligence-region-heading">
              <h4>{data.beforeNextHearing.length ? "Needs your attention" : "Office action check"}</h4>
              {data.beforeNextHearing.length > 0 && (
                <span>{upcomingHearing(data.latestOrder) ? "Before next hearing" : "Outstanding LAC action"}</span>
              )}
            </div>
            {data.beforeNextHearing.length ? (
              <>
                <div className="court-action-card-group">
                  {data.beforeNextHearing.slice(0, 3).map(action => (
                    <ActionRow key={action.id} action={action} />
                  ))}
                </div>
                {data.beforeNextHearing.length > 3 && (
                  <details className="court-intelligence-all-actions">
                    <summary>View all actions ({data.beforeNextHearing.length})</summary>
                    <div className="court-action-card-group">
                      {data.beforeNextHearing.slice(3).map(action => (
                        <ActionRow key={action.id} action={action} />
                      ))}
                    </div>
                  </details>
                )}
              </>
            ) : (
              <p>
                {data.unprocessedOrderCount && !verifiedFacts.length
                  ? "Known orders have not been processed yet; no office obligation is inferred."
                  : "No direct LAC action was identified in the processed orders. This does not establish that all office duties are complete."}
              </p>
            )}
          </article>

          <article className="court-intelligence-position">
            <h4>Current position</h4>
            {data.currentPosition.length ? (
              <>
                <div className="court-position-stream">
                  {data.currentPosition.slice(0, 3).map((line, index) => (
                    <div key={index} className="court-position-item">
                      <p>
                        {line.role && (
                          <small className={`court-role-pill ${roleClass(line.role)}`}>
                            {roleLabel(line.role)}
                            {line.scope === "Historical" ? " · historical reference" : ""} ·{" "}
                          </small>
                        )}
                        {officerText(line.text)}
                      </p>
                      <Evidence source={line.source} />
                    </div>
                  ))}
                </div>
                {data.currentPosition.length > 3 && (
                  <details className="court-position-more">
                    <summary>More case context</summary>
                    <div className="court-position-stream">
                      {data.currentPosition.slice(3, 6).map((line, index) => (
                        <div key={index} className="court-position-item">
                          <small className={`court-role-pill ${roleClass(line.role)}`}>
                            {line.role ? roleLabel(line.role) : line.attribution}
                          </small>
                          <p>{officerText(line.text)}</p>
                          <Evidence source={line.source} />
                        </div>
                      ))}
                    </div>
                  </details>
                )}
              </>
            ) : (
              <p className="court-intelligence-muted">Current position is not yet confirmed from the available orders.</p>
            )}
          </article>

          <div className="court-intelligence-working-grid">
            <article className="court-intelligence-latest">
              <div className="court-intelligence-region-heading">
                <h4>Latest order</h4>
                {data.latestOrder && <time>{shownDate(data.latestOrder.orderDate)}</time>}
              </div>
              {data.latestOrder ? (
                <OrderSummary order={data.latestOrder} />
              ) : (
                <p className="court-intelligence-muted">No processed order is available yet.</p>
              )}
            </article>

            <article className="court-intelligence-ask">
              <h4>Ask Court Intelligence</h4>
              <p className="court-intelligence-muted">English, हिन्दी or Hinglish · this matter only</p>
              <form onSubmit={ask} className="court-intelligence-ask-form">
                <label className="court-intelligence-question-label" htmlFor={`case-question-${caseId}`}>
                  Your question
                </label>
                <div className="court-intelligence-question-row">
                  <input
                    id={`case-question-${caseId}`}
                    value={question}
                    maxLength={600}
                    onChange={event => setQuestion(event.target.value)}
                    placeholder="Ask anything about this matter…"
                  />
                  <button type="submit" disabled={asking || !question.trim()}>
                    {asking ? "Checking…" : "Ask"}
                  </button>
                </div>
              </form>

              {!!suggestions.length && (
                <div className="court-intelligence-suggestions">
                  {suggestions.map(suggestion => (
                    <button
                      key={suggestion.label}
                      type="button"
                      onClick={() => setQuestion(suggestion.question)}
                    >
                      {suggestion.label}
                    </button>
                  ))}
                </div>
              )}

              {askError && <p role="status" className="court-intelligence-ask-error">{askError}</p>}

              {answer && (
                <div className="court-intelligence-answer" aria-live="polite">
                  {answer.claims.length ? (
                    answer.claims.map((claim, index) => (
                      <div key={index} className="court-answer-claim">
                        <small className="court-claim-meta">
                          {claim.attribution} · {shownDate(claim.source.orderDate)} · Page {claim.source.page}
                        </small>
                        <p>{officerText(claim.text)}</p>
                        <Evidence source={claim.source} />
                      </div>
                    ))
                  ) : (
                    <>
                      <p className="court-answer-unconfirmed">
                        {answer.reason === "OrderUnavailable"
                          ? "I could not find an official order for that listed date."
                          : "I could not confirm this from the orders processed for this matter."}
                      </p>
                      {reviewCount > 0 && (
                        <small className="court-answer-review-warning">
                          {reviewCount} order{reviewCount === 1 ? "" : "s"} still need{reviewCount === 1 ? "s" : ""} source verification.
                        </small>
                      )}
                      <div className="court-intelligence-answer-actions">
                        <button type="button" onClick={() => setHistoryOpen(true)}>
                          Review available order history
                        </button>
                        {data.latestOrder && officialLink(data.latestOrder.officialUrl) && (
                          <a href={data.latestOrder.officialUrl} target="_blank" rel="noreferrer">
                            Open latest official order ↗
                          </a>
                        )}
                      </div>
                    </>
                  )}
                </div>
              )}
            </article>
          </div>

          <details
            className="court-intelligence-history"
            open={historyOpen}
            onToggle={event => setHistoryOpen(event.currentTarget.open)}
          >
            <summary>
              <span>Order history</span>
              <small>
                {data.orders.length} orders
                {data.orders.length > 0
                  ? ` · ${shownDate(data.orders[0].orderDate)} – ${shownDate(data.orders[data.orders.length - 1].orderDate)}`
                  : ""}
              </small>
              <span>View timeline</span>
            </summary>

            {data.sourceCoverage && (
              <p className="court-intelligence-muted court-coverage-caption">
                {data.sourceCoverage.checkedSources} of {data.sourceCoverage.knownSources} supplied sources have verified facts. This is not a claim that every Court order is available.
              </p>
            )}

            <div className="court-timeline-container">
              {data.orders.map((order, index) => (
                <details className="court-intelligence-history-row" key={`${order.officialUrl}-${index}`}>
                  <summary>
                    <time>{shownDate(order.orderDate)}</time>
                    <span className="court-timeline-summary-cell">
                      {orderDigest(order).length ? (
                        orderDigest(order)
                          .slice(0, order.presentationKind === "Routine" ? 1 : 2)
                          .map((entry, number) => (
                            <span className="court-intelligence-timeline-fact" key={number}>
                              <small className={`court-role-pill ${roleClass(entry.role)}`}>
                                {roleLabel(entry.role)} ·{" "}
                              </small>
                              {officerText(entry.text)}
                            </span>
                          ))
                      ) : (
                        timelineLabel(order)
                      )}
                      {confirmedNextHearing(order) && (
                        <small className="court-timeline-nextdate">Next date: {shownDate(order.nextHearingDate!)}</small>
                      )}
                    </span>
                    <small className={`court-status-pill ${order.status === "Validated" ? "verified" : "review"}`}>
                      {order.status === "Validated" ? "Verified" : "Review"}
                    </small>
                  </summary>
                  <div className="court-history-expanded">
                    <OrderSummary order={order} />
                  </div>
                </details>
              ))}
            </div>

            {!!data.sourceCoverage?.gaps.length && (
              <details className="court-gaps-disclosure">
                <summary>Chronology gaps ({data.sourceCoverage.gaps.length})</summary>
                <div className="court-gaps-content">
                  {data.sourceCoverage.gaps.map((gap, index) => (
                    <p key={index}>
                      {shownDate(gap.orderDate)} · {gap.reason}
                      {officialLink(gap.officialUrl) && (
                        <>
                          {" "}·{" "}
                          <a href={gap.officialUrl} target="_blank" rel="noreferrer">
                            Check source ↗
                          </a>
                        </>
                      )}
                    </p>
                  ))}
                </div>
              </details>
            )}
          </details>

          {!!data.factualChronology?.length && (
            <details className="court-intelligence-history court-factual-chronology-section">
              <summary>Factual dates mentioned in orders · not Court hearing dates</summary>
              <div className="court-factual-items">
                {data.factualChronology.map((entry, index) => (
                  <div className="court-intelligence-history-row" key={index}>
                    <small>
                      Mentioned date{entry.dates.length === 1 ? "" : "s"}: {entry.dates.map(shownDate).join(" · ")} ·{" "}
                      {roleLabel(entry.role)}
                    </small>
                    <p>{officerText(entry.text)}</p>
                    <Evidence source={entry.source} />
                  </div>
                ))}
              </div>
            </details>
          )}
        </>
      )}
    </section>
  );
};
