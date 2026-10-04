import React, { useEffect, useState } from "react";
import "./court-intelligence.css";

type Source = { orderDate: string | null; page: number; evidence: string; officialUrl: string; evidenceParts?: { page: number; evidence: string }[] };
type Fact = { category: string; field: string; value: string; page: number; evidence: string; scope: string; evidenceParts?: { page: number; evidence: string }[] };
type Proposition = { id?: string; role: string; attribution: string; scope: string; text: string; source: Source };
type Order = { orderDate: string | null; officialUrl: string; corrigendumUrl?: string | null; uploadDate?: string | null; sourceKind?: string | null; status: string; facts: Fact[]; summaryFacts?: Fact[]; nextHearingDate?: string | null; court?: string | null; digest?: Proposition[]; presentationKind?: "Routine" | "Substantive" | "Unverified"; officeActionCount?: number; failureMessage?: string; refreshFailure?: string; coverage?: { allSelectedChunksProcessed?: boolean } };
type Action = { id: string; type?: string; text: string; actor: string; deadlineText: string | null; dueDate: string | null; source: Source };
type CaptionEntry = { text: string; source: Source };
type Intelligence = { caseNumber?: string; status: string; processingComplete: boolean; currentPosition: { id?: string; text: string; source: Source; attribution?: string; role?: string; scope?: string }[]; beforeNextHearing: Action[]; latestOrder: Order | null; finalOrder?: Order | null; latestMeaningfulOrder?: Order | null; chronologyWarnings?: string[]; orders: Order[]; caption?: Record<string, CaptionEntry | string>; lacCaptionAppearances?: CaptionEntry[]; factualChronology?: { dates: string[]; text: string; role: string; source: Source }[]; sourceCoverage?: { basis: string; knownSources: number; checkedSources: number; gaps: { orderDate: string | null; officialUrl: string; reason: string }[] } };
type SourceDiagnostic = { orderDate: string | null; rawOrderDate: string | null; officialUrl: string | null; sourceObservationId: string; sourceState: string; reasonCode: string; officerMessage: string; aiState: string; usableFactCount: number; reviewRequired: boolean; sourceLabel: string; aiLabel: string; technical: Record<string, unknown> };
type PipelineSummary = { officialOrdersFound: number; usableAiBriefs: number; blockedBeforeAi: number; processedButReviewRequired: number; pendingProcessing: number; extractionIncomplete: number; usableBriefsWithReview: number };
type Answer = { answer: string; actionConclusion?: string; coverageNote?: string; mode?: "CourtGrounded" | "GeneralLocal"; claims: { text: string; attribution: string; source: Source }[]; insufficientEvidence: boolean; reason?: string };
type ProgressSummary = { officialSources: number; usableBriefs: number; blockedSources: number; pendingSources: number; latestBriefReady: boolean; latestOrderDate: string | null; processingCurrentOrderDate: string | null; processingChecked: number; processingTotal: number; backgroundProcessing: boolean; coverageComplete: boolean };
type RegisteredIntelligence = Intelligence & { progressSummary?: ProgressSummary; sourceDiagnostics?: SourceDiagnostic[]; pipelineSummary?: PipelineSummary; caseId: string; courtName?: string; officeStatus?: string; officeNdoh?: string | null; officialStatus?: { rawStatus: string | null; observedAt: string; listingDate: string | null }; historySync?: { runId: string; status: string; phase: string; completedAt: string | null; failureMessage?: string }; caseBrief?: Record<string, Proposition[]>; sourceReviewOrders?: { orderDate: string | null; officialUrl: string; corrigendumUrl?: string | null; reason: string }[]; knownOrderCount?: number; unprocessedOrderCount?: number; unusableKnownOrderCount?: number; refreshState?: { caseId: string; status: string; startedAt?: string; checked?: number; total?: number; needsReview?: number; message?: string } };

const IconCourt: React.FC<{ size?: number; className?: string }> = ({ size = 16, className = "" }) => (
  <svg
    width={size}
    height={size}
    viewBox="0 0 24 24"
    fill="none"
    stroke="currentColor"
    strokeWidth="2"
    strokeLinecap="round"
    strokeLinejoin="round"
    className={className}
    aria-hidden="true"
  >
    <path d="M3 21h18" />
    <path d="M6 18v-7" />
    <path d="M10 18v-7" />
    <path d="M14 18v-7" />
    <path d="M18 18v-7" />
    <polygon points="12 2 2 7 22 7 12 2" />
  </svg>
);

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
  if (r.includes("OTHER_PARTY")) return "role-other-party-submission";
  if (r.includes("LAC") || r.includes("RESPONDENT")) return "role-lac-or-respondent-submission";
  if (r.includes("COMPLIANCE")) return "role-recorded-compliance";
  if (r.includes("COMPENSATION")) return "role-compensation-fact";
  return "role-procedural-event";
};

const timelineLabel = (order: Order) => {
  if (order.status === "Unprocessed") return "Discovered official order · queued for AI processing";
  if (order.status === "Processing") return "Reading official order…";
  if (order.status === "NeedsSourceReview") return "Official source requires review before processing";
  if (order.status === "NeedsReview") return "Individually usable summary facts";
  if (order.failureMessage) return "Official order PDF requires review";
  return "Verified order facts";
};

const orderDigest = (order: Order): Proposition[] =>
  order.digest ?? safeOrderFacts(order).filter(fact => fact.scope === "Current").map(fact => ({
    id: undefined,
    role: fact.category,
    attribution: fact.category === "COURT_DIRECTION" ? "Direction" : fact.category === "COURT_FINDING" ? "Finding" : "Observation",
    scope: fact.scope,
    text: fact.value,
    source: {
      orderDate: order.orderDate,
      officialUrl: order.officialUrl,
      page: fact.page,
      evidence: fact.evidence,
      evidenceParts: fact.evidenceParts
    }
  }));

const confirmedNextHearing = (order: Order | null) =>
  !!order &&
  typeof order.nextHearingDate === "string" &&
  safeOrderFacts(order).some(fact =>
    fact.field === "nextHearing" &&
    fact.scope === "Current" &&
    fact.category !== "PETITIONER_SUBMISSION"
  );

const officeToday = () => {
  const now = new Date();
  return `${now.getFullYear()}-${String(now.getMonth() + 1).padStart(2, "0")}-${String(now.getDate()).padStart(2, "0")}`;
};

const upcomingHearing = (order: Order | null, today = officeToday()) =>
  confirmedNextHearing(order) && order!.nextHearingDate! >= today;

const isActionOverdue = (action: Action, today = officeToday()) => !!action.dueDate && action.dueDate < today;

const actionPeriod = (action: Action, today = officeToday()) => {
  if (!action.dueDate) return `${action.deadlineText ?? "No deadline stated"} · completion not confirmed`;
  if (/\bpreferably\b/i.test(action.deadlineText ?? ""))
    return `${action.dueDate < today ? "Preferred period ended" : "Preferred period ends"} ${shownDate(action.dueDate)} · completion not confirmed`;
  return `${action.dueDate < today ? "Overdue since" : "Due"} ${shownDate(action.dueDate)} · completion not confirmed`;
};

const shownDate = (iso?: string | null) => {
  if (!iso) return "Date not stated";
  const [year, month, day] = iso.split("-");
  if (!year || !month || !day) return iso;
  const months = ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];
  return `${Number(day)} ${months[Number(month) - 1] ?? month} ${year}`;
};

const Evidence: React.FC<{ source: Source }> = ({ source }) => {
  const parts = source.evidenceParts?.length
    ? source.evidenceParts
    : [{ page: source.page, evidence: source.evidence }];

  return (
    <details className="court-intelligence-evidence">
      <summary>View evidence</summary>
      <div className="court-evidence-body">
        <p className="court-evidence-meta">
          {shownDate(source.orderDate)}
          {officialLink(source.officialUrl) && (
            <>
              {" "}·{" "}
              <a href={officialLink(source.officialUrl)!} target="_blank" rel="noreferrer">
                Official source ↗
              </a>
            </>
          )}
        </p>
        {parts.map((part, index) => (
          <div key={index} className="court-evidence-part">
            <small>Page {part.page}</small>
            <blockquote>{part.evidence}</blockquote>
          </div>
        ))}
      </div>
    </details>
  );
};

const actionTitle = (action: Action) =>
  /compliance/i.test(action.text)
    ? "Compliance action"
    : /status\s+report/i.test(action.text)
    ? "Status report"
    : /affidavit/i.test(action.text)
    ? "Status affidavit"
    : action.type === "Reference"
    ? "Court reference"
    : action.type === "Compensation"
    ? "Compensation action"
    : "Outstanding LAC action";

const ActionRow: React.FC<{ action: Action }> = ({ action }) => {
  const overdue = isActionOverdue(action);
  return (
    <div className={`court-intelligence-action ${overdue ? "action-overdue" : "action-pending"}`}>
      <div className="court-action-topline">
        <h5>{actionTitle(action)}</h5>
        <small className={`court-action-deadline ${overdue ? "overdue" : "pending"}`}>
          {actionPeriod(action)}
        </small>
      </div>
      <p>{officerText(action.text)}</p>
      <Evidence source={action.source} />
    </div>
  );
};

const OrderSummary: React.FC<{ order: Order }> = ({ order }) => {
  const digest = orderDigest(order);
  return (
    <div className="court-order-summary-card">
      {!digest.length ? (
        <p className="court-intelligence-muted">This order needs source verification before its contents can be summarized.</p>
      ) : (
        <>
          <p className="court-intelligence-label">What happened in this order</p>
          <ul className="court-intelligence-order-points">
            {digest.slice(0, 4).map((entry, index) => (
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
          {order.officeActionCount === 0 && order.status === "Validated" && !order.failureMessage && !order.refreshFailure && order.coverage?.allSelectedChunksProcessed !== false && (
            <small className="court-order-no-action">No direct LAC action was identified in this order.</small>
          )}
        </>
      )}
      <div className="court-order-summary-footer">
        {confirmedNextHearing(order) && (
          <span className="court-intelligence-hearing">
            Next source-listed date <strong>{shownDate(order.nextHearingDate!)}</strong>
          </span>
        )}
        <div className="court-order-actions-row">
          {officialLink(order.officialUrl) && (
            <a href={officialLink(order.officialUrl)!} target="_blank" rel="noreferrer" className="court-official-order-link">
              Open official order ↗
            </a>
          )}
          <FullOrderFacts order={order} />
        </div>
      </div>
    </div>
  );
};

const FullOrderFacts: React.FC<{ order: Order }> = ({ order }) => (
  <details className="court-full-order-facts">
    <summary>View full brief</summary>
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

type HistoryRun = { id: string; status: string; phase: string; captchaChallenges: number; failureMessage?: string; items: { courtCaseId: string; failureMessage?: string }[] };
const DhcHistoryWizard: React.FC<{ caseId: string; existingRun?: string; startError?: string; onClose: () => void; onUpdated: () => void }> = ({ caseId, existingRun, startError, onClose, onUpdated }) => {
  const [runId, setRunId] = useState(existingRun ?? "");
  const [run, setRun] = useState<HistoryRun | null>(null);
  const [challenge, setChallenge] = useState<{ kind: string; officialText: string | null; operation: string } | null>(null);
  const [typed, setTyped] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const [imageVersion, setImageVersion] = useState(0);
  const challengeKey = React.useRef("");
  const alive = React.useRef(true);
  const base = "/api/court-cases/dhc-assisted/runs";
  const request = async (url: string, body?: object) => {
    const response = await fetch(url, { method: "POST", cache: "no-store", credentials: "include",
      ...(body ? { headers: { "Content-Type": "application/json" }, body: JSON.stringify(body) } : {}) });
    if (!response.ok) {
      const err = await response.json().catch(() => null);
      throw new Error(err?.detail ?? err?.error ?? "Official verification step failed.");
    }
    return response.json();
  };
  useEffect(() => {
    alive.current = true;
    setRunId(existingRun ?? "");
    return () => { alive.current = false; };
  }, [caseId, existingRun]);
  useEffect(() => {
    if (!runId) return;
    const controller = new AbortController();
    let loading = false;
    const load = async () => {
      if (loading || controller.signal.aborted) return;
      loading = true;
      try {
        const response = await fetch(`${base}/${runId}`, { cache: "no-store", signal: controller.signal });
        if (!response.ok) throw new Error("Official sync progress is temporarily unavailable.");
        const next = await response.json() as HistoryRun;
        if (next.id !== runId || next.items.length !== 1 || next.items[0].courtCaseId !== caseId) throw new Error("Case verification mismatch.");
        if (controller.signal.aborted) return;
        setRun(next);
        if (["WaitingForCaptcha", "PausedForCaptcha"].includes(next.status)) {
          const key = `${runId}:${next.captchaChallenges}`;
          if (challengeKey.current !== key) {
            const result = await fetch(`${base}/${runId}/captcha`, { cache: "no-store", signal: controller.signal });
            if (!result.ok) throw new Error("Official CAPTCHA is temporarily unavailable.");
            const value = await result.json();
            if (!controller.signal.aborted) { setChallenge(value); setTyped(""); challengeKey.current = key; }
          }
        } else { setChallenge(null); setTyped(""); }
        if (next.status === "ReadyForOrders") {
          await request(`${base}/${runId}/orders`);
          onUpdated();
        }
        if (next.status === "Completed") {
          onUpdated();
          onClose();
        }
      } catch (err) { if (!controller.signal.aborted) setError((err as Error).message); }
      finally { loading = false; }
    };
    void load();
    const timer = window.setInterval(() => { void load(); }, 3000);
    return () => { controller.abort(); window.clearInterval(timer); };
  }, [caseId, runId]);
  const act = async (action: "captcha" | "captcha/refresh" | "resume" | "orders") => {
    const humanAnswer = typed;
    setTyped(""); setBusy(true); setError("");
    try {
      const result = await request(`${base}/${runId}/${action}`, action === "captcha" ? { answer: humanAnswer } : undefined);
      if (!alive.current) return;
      if (action === "captcha" && !result?.accepted) setError("Official CAPTCHA was not accepted. Enter the new challenge.");
      challengeKey.current = "";
      setChallenge(null); setImageVersion(value => value + 1); onUpdated();
    } catch (err) { if (alive.current) setError((err as Error).message); }
    finally { if (alive.current) setBusy(false); }
  };
  const complete = run?.status === "Completed";
  const interrupted = ["Failed", "Interrupted"].includes(run?.status ?? "");
  return (
    <div className="dhc-history-backdrop">
      <section className="dhc-history-dialog" role="dialog" aria-modal="true" aria-labelledby="dhc-history-title">
        <header>
          <h3 id="dhc-history-title">Sync Full DHC History</h3>
          <button type="button" onClick={onClose} aria-label="Close history sync">✕</button>
        </header>
        <p role="status">
          {complete ? "Official lookup completed. Discovered orders are queued for local AI processing." :
           interrupted ? "Official lookup could not be completed. Resume verification to continue." :
           challenge ? "Official CAPTCHA verification required" :
           run?.phase === "OrderLookup" ? "Discovering order history…" : "Checking official status…"}
        </p>
        <p className="court-intelligence-muted">Official status and every returned order are saved for this case. Processing continues while you work in the case.</p>
        {challenge && (
          <form onSubmit={event => { event.preventDefault(); if (typed.trim() && !busy) void act("captcha"); }}>
            <p>{challenge.operation} · enter the official CAPTCHA yourself</p>
            {challenge.kind === "Image" ? <img alt="Official DHC CAPTCHA" src={`${base}/${runId}/captcha/image?v=${imageVersion}`} /> : <div className="dhc-human-challenge">{challenge.officialText}</div>}
            <label htmlFor="dhc-history-answer">Official CAPTCHA answer</label>
            <input id="dhc-history-answer" autoComplete="off" value={typed} maxLength={64} onChange={event => setTyped(event.target.value)} />
            <div className="dhc-history-actions">
              <button disabled={busy || !typed.trim()} type="submit">Verify and continue</button>
              <button disabled={busy} type="button" onClick={() => void act("captcha/refresh")}>New challenge</button>
            </div>
          </form>
        )}
        {(error || startError) && <p role="alert" className="court-status-alert">{error || startError}</p>}
        {run?.items[0]?.failureMessage && <details><summary>Verification details</summary><p>{run.items[0].failureMessage}</p></details>}
        {interrupted && <button type="button" disabled={busy} onClick={() => void act("resume")}>Resume official verification</button>}
        {complete && <button type="button" onClick={onClose}>View history and processing progress</button>}
      </section>
    </div>
  );
};

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
  const [syncOpen, setSyncOpen] = useState(false);
  const [reload, setReload] = useState(0);
  const [syncRun, setSyncRun] = useState<{ caseId: string; runId: string } | null>(null);
  const [syncError, setSyncError] = useState("");
  const [startingSync, setStartingSync] = useState(false);
  const [conversation, setConversation] = useState<{ question: string; answer: Answer }[]>([]);

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
    setAsking(false);
    setAskError("");
    setHistoryOpen(false);
    setRefreshing(false);
    setRefreshError("");
    setConversation([]);

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
          setHistoryOpen((result?.orders.length ?? 0) > (result?.historySync ? 0 : 1));
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

  const running = data?.refreshState?.status === "Running" || data?.progressSummary?.backgroundProcessing === true;
  const syncRunning = data?.historySync && ["Submitted", "Queued", "Running", "WaitingForCaptcha", "PausedForCaptcha"].includes(data.historySync.status);
  const awaitingAi = data?.historySync?.status === "Completed" && data.orders.length > 0 &&
    (!data.refreshState || (Date.parse(data.historySync.completedAt ?? "") > Date.parse(data.refreshState.startedAt ?? "1970-01-01")));

  useEffect(() => {
    if (!running && !syncRunning && !reload && !awaitingAi) return;
    const controller = new AbortController();
    const generation = epoch.current;
    let inFlight = false;
    const timer = setInterval(async () => {
      if (inFlight) return;
      inFlight = true;
      try {
        const response = await fetch(`/api/court-cases/${caseId}/intelligence`, { signal: controller.signal, cache: "no-store", credentials: "include" });
        if (!response.ok) return;
        const result: unknown = await response.json();
        if (!controller.signal.aborted && generation === epoch.current && activeCase.current === caseId && registeredPayload(result, caseId)) {
          setData(result);
        }
      } catch {
        /* Existing structured brief stays readable during transient polling failure. */
      } finally {
        inFlight = false;
        if (!controller.signal.aborted) setReload(0);
      }
    }, 5000);

    return () => {
      clearInterval(timer);
      controller.abort();
    };
  }, [caseId, running, syncRunning, awaitingAi, reload, data?.historySync?.status]);

  const ask = async (event: React.FormEvent) => {
    event.preventDefault();
    if (asking || !question.trim()) return;
    const requestedCase = caseId;
    const currentQuestion = question.trim();
    const generation = epoch.current;
    const controller = new AbortController();
    questionAbort.current = controller;
    setAsking(true);
    setAskError("");

    try {
      const response = await fetch(`/api/court-cases/${caseId}/intelligence/ask`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        credentials: "include",
        body: JSON.stringify({ question: currentQuestion, history: conversation.filter(turn => turn.answer.mode === "GeneralLocal").slice(-4).map(turn => ({ question: turn.question, answer: turn.answer.answer })) }),
        signal: controller.signal,
        cache: "no-store"
      });
      if (!response.ok) throw new Error("Unavailable");
      const result = (await response.json()) as Answer & { caseId: string };
      if (result.caseId !== requestedCase || !Array.isArray(result.claims)) throw new Error("Wrong case response");
      if (!controller.signal.aborted && generation === epoch.current && activeCase.current === requestedCase) {
        setAnswer(result);
        setConversation(previous => [...previous, { question: currentQuestion, answer: result }].slice(-10));
        setQuestion("");
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
        credentials: "include",
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

  const startHistory = async () => {
    if (startingSync || running) return;
    const generation = epoch.current;
    const requestedCase = caseId;
    setSyncError(""); setSyncOpen(true);
    if (data?.historySync && (syncRunning || ["Interrupted", "Failed"].includes(data.historySync.status))) {
      setSyncRun({ caseId, runId: data.historySync.runId }); return;
    }
    const controller = new AbortController();
    refreshAbort.current = controller;
    setStartingSync(true); setSyncRun(null);
    try {
      const response = await fetch(`/api/court-cases/${caseId}/dhc-history`, { method: "POST", cache: "no-store", credentials: "include", signal: controller.signal });
      const result = await response.json().catch(() => null);
      if (!response.ok) throw new Error(result?.detail ?? result?.error ?? "Official history sync is temporarily unavailable.");
      if (result.caseId !== requestedCase || typeof result.runId !== "string") throw new Error("Case verification mismatch.");
      if (!controller.signal.aborted && generation === epoch.current && activeCase.current === requestedCase) {
        setSyncRun({ caseId: requestedCase, runId: result.runId }); setReload(value => value + 1);
      }
    } catch (err) {
      if (!controller.signal.aborted && generation === epoch.current && activeCase.current === requestedCase) setSyncError((err as Error).message);
    } finally {
      if (generation === epoch.current && activeCase.current === requestedCase) setStartingSync(false);
    }
  };

  const pendingCount = data?.orders.filter(order => ["Unprocessed", "Processing"].includes(order.status)).length ?? 0;
  const pipeline = data?.pipelineSummary ?? {
    officialOrdersFound: data?.knownOrderCount ?? data?.orders.length ?? 0,
    usableAiBriefs: data?.orders.filter(order => safeOrderFacts(order).length > 0).length ?? 0,
    blockedBeforeAi: (data?.orders.filter(order => order.status === "NeedsSourceReview").length ?? 0) + (data?.unusableKnownOrderCount ?? 0),
    processedButReviewRequired: data?.orders.filter(order => order.status === "NeedsReview" && order.coverage?.allSelectedChunksProcessed === true && !order.failureMessage).length ?? 0,
    pendingProcessing: pendingCount,
    extractionIncomplete: data?.orders.filter(order => order.status === "NeedsReview" && (!order.coverage?.allSelectedChunksProcessed || !!order.failureMessage)).length ?? 0,
    usableBriefsWithReview: data?.orders.filter(order => safeOrderFacts(order).length > 0 && (order.status !== "Validated" || order.refreshFailure)).length ?? 0
  };

  const progress = data?.progressSummary ?? {
    officialSources: pipeline.officialOrdersFound,
    usableBriefs: pipeline.usableAiBriefs,
    blockedSources: pipeline.blockedBeforeAi,
    pendingSources: pipeline.pendingProcessing,
    latestBriefReady: !!(data?.latestOrder && safeOrderFacts(data.latestOrder).length > 0),
    latestOrderDate: data?.latestOrder?.orderDate ?? null,
    processingCurrentOrderDate: null,
    processingChecked: data?.refreshState?.checked ?? pipeline.usableAiBriefs,
    processingTotal: data?.refreshState?.total ?? pipeline.officialOrdersFound,
    backgroundProcessing: running,
    coverageComplete: data?.processingComplete ?? true
  };

  const latestVerified = data?.sourceDiagnostics ? [...data.orders].reverse().find(order =>
    data.sourceDiagnostics!.some(source => source.officialUrl === order.officialUrl && source.orderDate === order.orderDate && source.usableFactCount > 0)) ?? null : data?.latestOrder ?? null;

  const primaryNotice = refreshError || (pipeline.blockedBeforeAi > 0
    ? pipeline.officialOrdersFound === 1 ? data?.sourceDiagnostics?.[0]?.officerMessage ?? "This official source is not yet verified for AI. Review its source details below."
      : `${pipeline.blockedBeforeAi} official sources are withheld pending source verification. Their facts are not used; see each source's reason below.`
    : pipeline.extractionIncomplete > 0 ? "AI extraction is incomplete for some sources. Partial facts are withheld; see source details below."
    : pipeline.processedButReviewRequired > 0 ? "Some AI briefs require review. Only individually verified facts are used; see source details below."
    : pipeline.pendingProcessing > 0 ? "Some official sources are waiting for AI processing. Completed briefs remain available." : null);

  const principalOutcome = data?.latestOrder && safeOrderFacts(data.latestOrder).find(fact => fact.category === "DISPOSITION" && fact.scope === "Current" && /\b(?:petition|appeal|suit)\b.{0,100}\b(?:allowed|dismissed|disposed)\b/i.test(fact.value));
  const positionLines = principalOutcome && data?.latestOrder ? [{ text: principalOutcome.value, role: "DISPOSITION", scope: "Current", source: { orderDate: data.latestOrder.orderDate, officialUrl: data.latestOrder.officialUrl, page: principalOutcome.page, evidence: principalOutcome.evidence, evidenceParts: principalOutcome.evidenceParts } },
    ...data.currentPosition.filter(line => line.text !== principalOutcome.value && !(line.role === "DISPOSITION" && line.scope !== "Historical" && line.source.orderDate === data.latestOrder?.orderDate))] : data?.currentPosition ?? [];
  const verifiedFacts = data?.orders.flatMap(safeOrderFacts) ?? [];
  const actionCheckIncomplete = !!data && (!data.processingComplete || data.orders.length === 0 ||
    data.orders.some(order => order.status !== "Validated" || !!order.failureMessage || !!order.refreshFailure || order.coverage?.allSelectedChunksProcessed === false) ||
    !!data.unusableKnownOrderCount);
  const anyOverdueAction = data ? data.beforeNextHearing.some(a => isActionOverdue(a)) : false;

  const suggestions = data
    ? [
        { label: "Explain this case simply", question: "Explain this case simply in plain officer language." },
        ...(data.beforeNextHearing.length ? [{ label: "What is pending from LAC?", question: "What is still pending from LAC?" }] : [{ label: "What is pending from LAC?", question: "What is pending from LAC?" }]),
        ...(data.latestOrder && currentFacts(data.latestOrder).some(fact => fact.category === "COURT_DIRECTION")
          ? [{ label: "Latest direction", question: "What did the latest order direct?" }]
          : [{ label: "Latest direction", question: "What did the latest order direct?" }]),
        ...(verifiedFacts.some(fact => fact.field === "compensation")
          ? [{ label: "Compensation position", question: "What has happened regarding compensation?" }]
          : [{ label: "Compensation position", question: "What is the compensation position?" }]),
        { label: "Last 3 orders", question: "Summarize the key events in the last three orders." },
        ...(confirmedNextHearing(data.latestOrder) && data.beforeNextHearing.length
          ? [{ label: "Next hearing preparation", question: "What does LAC need to do before the next hearing?" }]
          : [])
      ]
    : [];

  const healthToneClass = anyOverdueAction
    ? "tone-danger"
    : pipeline.blockedBeforeAi > 0
    ? "tone-warning"
    : "tone-healthy";

  return (
    <section className="court-intelligence" aria-label="Court Intelligence">
      {/* 1. Header Lockup */}
      <header className="court-intelligence-header">
        <div className="court-intelligence-title-lockup">
          <span className="court-intelligence-emblem">
            <IconCourt size={16} />
          </span>
          <div>
            <h3>Court Intelligence</h3>
            <p className="court-intelligence-header-desc">Synthesized from available verified Court orders</p>
          </div>
        </div>
        <span>Evidence-backed office brief</span>
      </header>

      <div className="court-history-toolbar">
        <dl className="court-history-metrics">
          <div><dt>Official DHC status</dt><dd>{data?.officialStatus?.rawStatus ?? "Not checked"}</dd>{data?.officialStatus?.listingDate && <small>Official listing: {shownDate(data.officialStatus.listingDate)}</small>}</div>
          <div><dt>Office register</dt><dd>{data?.officeStatus ?? "See case record"}</dd>{data?.officeNdoh && <small>Office NDOH: {shownDate(data.officeNdoh)}</small>}</div>
          <div><dt>Latest verified order</dt><dd>{latestVerified ? shownDate(latestVerified.orderDate) : pipeline.officialOrdersFound > 0 ? "Awaiting source verification" : "No verified order yet"}</dd></div>
          <div><dt>Last synced</dt><dd>{data?.historySync?.completedAt ? new Date(data.historySync.completedAt).toLocaleString("en-IN", { timeZone: "Asia/Kolkata" }) : "Not yet"}</dd></div>
        </dl>
        <button className="court-intelligence-btn-action" type="button" disabled={running || startingSync} onClick={startHistory}>{running || startingSync ? "Syncing…" : "Sync Full DHC History"}</button>
      </div>
      {syncOpen && (
        <DhcHistoryWizard
          key={caseId}
          caseId={caseId}
          existingRun={syncRun?.caseId === caseId ? syncRun.runId : undefined}
          startError={syncError}
          onClose={() => { setSyncOpen(false); setReload(value => value + 1); }}
          onUpdated={() => setReload(value => value + 1)}
        />
      )}

      {!data ? (
        <div className="court-intelligence-empty-card">
          <p className="court-intelligence-empty">
            {unavailable
              ? "Court intelligence is temporarily unavailable. Your Court records remain available."
              : "DHC history has not been checked yet. Court records and official order links remain unchanged."}
          </p>
        </div>
      ) : (
        <>
          <div className={`court-case-health ${healthToneClass}`} role="status">
            <strong>{pipeline.officialOrdersFound ? `${pipeline.officialOrdersFound} official ${pipeline.officialOrdersFound === 1 ? "source found" : "orders"} · ${pipeline.usableAiBriefs} usable AI ${pipeline.usableAiBriefs === 1 ? "brief" : "briefs"}${pipeline.blockedBeforeAi ? ` · ${pipeline.blockedBeforeAi} source-blocked` : ""}${pipeline.pendingProcessing ? ` · ${pipeline.pendingProcessing} awaiting AI` : ""}${pipeline.extractionIncomplete ? ` · ${pipeline.extractionIncomplete} extraction incomplete` : ""}` : data.historySync?.status === "Completed" ? "Official DHC search returned no order PDF." : "DHC history has not been checked yet."}</strong>
            {pipeline.usableBriefsWithReview > 0 && <small>{pipeline.usableBriefsWithReview} of the {pipeline.usableAiBriefs} usable briefs also require review; this is not an additional source.</small>}
            {primaryNotice && <p className="court-primary-notice">{primaryNotice}</p>}
            {data.progressSummary?.latestBriefReady && <small>{data.progressSummary.backgroundProcessing ? "Fast brief ready" : "Latest brief ready"} · {shownDate(data.progressSummary.latestOrderDate)}</small>}
            {(running || syncRunning) && <p>{syncRunning ? data.historySync?.status.includes("Captcha") ? "CAPTCHA required · continue verification" : "Checking official DHC history…" : data.progressSummary ? `Background history · ${data.progressSummary.processingChecked} / ${data.progressSummary.processingTotal} sources checked${data.progressSummary.processingCurrentOrderDate ? ` · Processing ${shownDate(data.progressSummary.processingCurrentOrderDate)}` : ""}` : `AI processing · ${data.refreshState?.checked ?? 0} of ${data.refreshState?.total ?? data.orders.length} sources checked`}</p>}
            {data.orders.length > 0 && <details className="court-processing-controls"><summary>Processing controls</summary><button type="button" disabled={refreshing || running} onClick={refresh}>{refreshing || running ? "Processing intelligence…" : data.unprocessedOrderCount ? "Process known orders" : "Refresh intelligence"}</button></details>}
          </div>

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

          {/* 3. Executive Workspace Grid Above The Fold (1366x768) */}
          <div className="court-intelligence-executive-grid">
            {/* Left Column: Current Position & What LAC Needs To Do */}
            <div className="court-grid-left">
              {/* CURRENT POSITION */}
              <article className="court-card court-card-position">
                <div className="court-card-header">
                  <h4>Current position</h4>
                  {positionLines.length > 3 && (
                    <span className="court-card-meta">{positionLines.length} points</span>
                  )}
                </div>

                {positionLines.length ? (
                  <>
                    <ul className="court-position-bullets">
                      {positionLines.slice(0, 3).map((line, index) => (
                        <li key={index} className="court-position-bullet-item">
                          <div className="court-bullet-content">
                            <p>
                              {line.role && (
                                <span className={`court-role-pill ${roleClass(line.role)}`}>
                                  {roleLabel(line.role)}
                                  {line.scope === "Historical" ? " · historical reference" : ""}
                                </span>
                              )}
                              {officerText(line.text)}
                            </p>
                          </div>
                          <Evidence source={line.source} />
                        </li>
                      ))}
                    </ul>
                    {positionLines.length > 3 && (
                      <details className="court-position-more">
                        <summary>More case context ({positionLines.length - 3})</summary>
                        <ul className="court-position-bullets">
                          {positionLines.slice(3, 6).map((line, index) => (
                            <li key={index} className="court-position-bullet-item">
                              <div className="court-bullet-content">
                                <p>
                                  {line.role && (
                                    <span className={`court-role-pill ${roleClass(line.role)}`}>
                                      {roleLabel(line.role)}
                                      {line.scope === "Historical" ? " · historical reference" : ""}
                                    </span>
                                  )}
                                  {officerText(line.text)}
                                </p>
                              </div>
                              <Evidence source={line.source} />
                            </li>
                          ))}
                        </ul>
                      </details>
                    )}
                  </>
                ) : (
                  <p className="court-intelligence-muted">Current position is not yet confirmed from the available orders.</p>
                )}
              </article>

              {/* WHAT LAC NEEDS TO DO */}
              <article
                className={`court-card court-card-attention${
                  data.beforeNextHearing.length
                    ? anyOverdueAction
                      ? " attention-has-overdue"
                      : " attention-pending-future"
                    : " court-intelligence-attention-empty"
                }`}
              >
                <div className="court-intelligence-region-heading">
                  <h4>What LAC needs to do</h4>
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
                  <div className="court-no-action-box">
                    <p>
                      {pipeline.usableAiBriefs > 0
                        ? "No verified LAC-specific mandatory action is established in the currently processed evidence."
                        : "No usable Court evidence is currently available for this matter. Office actions cannot be checked until a source is processed and verified."}
                      {pipeline.usableAiBriefs > 0 && (pipeline.blockedBeforeAi > 0
                        ? ` Some discovered orders are still under source review, so no conclusion is drawn from those sources. ${pipeline.blockedBeforeAi} older source${pipeline.blockedBeforeAi === 1 ? " remains" : "s remain"} under review.`
                        : pipeline.pendingProcessing > 0
                        ? ` Some discovered orders are still awaiting AI processing, so no conclusion is drawn from those sources. ${pipeline.pendingProcessing} older source${pipeline.pendingProcessing === 1 ? " remains" : "s remain"} under review.`
                        : actionCheckIncomplete
                        ? " The evidence retains review conditions; this does not establish that all office duties are complete."
                        : " This does not establish that all office duties are complete.")}
                    </p>
                  </div>
                )}

                {"conditionalDirections" in data && Array.isArray(data.conditionalDirections) && data.conditionalDirections.length > 0 && (
                  <div className="court-conditional-block">
                    <h5>Conditional Court directions</h5>
                    <p className="court-intelligence-muted">These depend on the stated conditions; they are not unconditional tasks or payment deadlines.</p>
                    {data.conditionalDirections.map((entry: { text: string; actor: string; conditionText?: string; modality: string; source: Source }, index: number) => (
                      <div className="court-position-item" key={index}>
                        <small>{entry.actor} · {entry.modality}{entry.conditionText ? ` · ${entry.conditionText}` : ""}</small>
                        <p>{officerText(entry.text)}</p>
                        <Evidence source={entry.source} />
                      </div>
                    ))}
                  </div>
                )}
              </article>
            </div>

            {/* Right Column: Ask Court AI (Interactive Chat Experience) */}
            <div className="court-grid-right">
              <article className="court-card court-card-ask">
                <div className="court-card-header">
                  <div className="court-ask-header-title">
                    <h4>Ask Court AI</h4>
                    <span className="court-ai-badge">Assistant</span>
                  </div>
                  <p className="court-intelligence-muted">English, हिन्दी or Hinglish · this matter only</p>
                </div>

                {conversation.length > 1 && (
                  <details className="court-chat-history">
                    <summary>Earlier messages in this case ({conversation.length - 1})</summary>
                    {conversation.slice(0, -1).map((turn, index) => (
                      <div key={index} className="court-chat-turn">
                        <strong>{turn.question}</strong>
                        <p>{turn.answer.answer}</p>
                        {turn.answer.claims.map((claim, n) => (
                          <Evidence key={n} source={claim.source} />
                        ))}
                      </div>
                    ))}
                  </details>
                )}

                {answer && (
                  <div className="court-intelligence-answer" aria-live="polite">
                    {answer.actionConclusion && <p className="court-action-conclusion">{answer.actionConclusion}</p>}
                    {answer.coverageNote && <p className="court-answer-coverage">{answer.coverageNote}</p>}
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
                    ) : answer.actionConclusion ? null : answer.mode === "GeneralLocal" ? (
                      <p className="court-local-chat-answer">{answer.answer}<small>General local chat</small></p>
                    ) : (
                      <>
                        <p className="court-answer-unconfirmed">
                          {answer.reason === "OrderUnavailable"
                            ? "I could not find an official order for that listed date."
                            : answer.reason === "OrderProcessing" || answer.reason === "OrderNotVerified" ? answer.answer.split("\n")[0]
                            : answer.reason === "HistoryTooLong" ? "This history is too long for one reply. Please ask for a narrower date range or review the complete order history."
                            : "I could not confirm this from the orders processed for this matter."}
                        </p>
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

                  {asking && (
                    <div className="court-chat-typing">
                      <span className="court-typing-dot" />
                      <span className="court-typing-dot" />
                      <span className="court-typing-dot" />
                      <span>Synthesizing from Court orders…</span>
                    </div>
                  )}


                {/* Suggestions Chips */}
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

                {/* Input Form */}
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
                      placeholder="Ask about this case, or chat normally…"
                    />
                    <button type="submit" disabled={asking || !question.trim()}>
                      {asking ? "Checking…" : "Ask"}
                    </button>
                  </div>
                </form>

                {askError && <p role="status" className="court-intelligence-ask-error">{askError}</p>}
                {!progress.coverageComplete && (
                  <small className="court-partial-coverage-note">
                    Background history is still being processed · answers reflect currently verified evidence.
                  </small>
                )}
              </article>
            </div>
          </div>

          {/* 4. Latest Verified Order Card */}
          <article className="court-card court-card-latest">
            <div className="court-intelligence-region-heading">
              <h4>Latest verified order</h4>
              {latestVerified && <time>{shownDate(latestVerified.orderDate)}</time>}
            </div>
            {latestVerified ? (
              <OrderSummary order={latestVerified} />
            ) : (
              <p className="court-intelligence-muted">No verified AI brief is available yet. Review each official source state in the history below.</p>
            )}
          </article>

          {/* 5. Complete Order History */}
          <details
            className="court-intelligence-history"
            open={historyOpen}
            onToggle={event => setHistoryOpen(event.currentTarget.open)}
          >
            <summary>
              <span>Complete order history</span>
              <small>
                {data.knownOrderCount ?? data.orders.length} order{(data.knownOrderCount ?? data.orders.length) === 1 ? "" : "s"}
                {data.orders.length > 0
                  ? ` · ${shownDate(data.orders[0].orderDate)} – ${shownDate(data.orders[data.orders.length - 1].orderDate)}`
                  : ""}
              </small>
              <span>View timeline</span>
            </summary>

            <p className="court-intelligence-muted court-coverage-caption">
              Every discovered source is listed below with its verification, AI state and usable fact count. Completeness beyond these returned sources is not established.
            </p>

            <div className="court-timeline-container">
              {data.sourceDiagnostics?.map(source => {
                const order = data.orders.find(order => order.officialUrl === source.officialUrl && order.orderDate === source.orderDate);
                return (
                  <details className="court-source-pipeline" key={`${source.sourceObservationId}-${source.officialUrl}`}>
                    <summary>
                      <time>{source.orderDate ? shownDate(source.orderDate) : source.rawOrderDate || "Date unverified"}</time>
                      <span className={`court-status-pill ${source.reviewRequired ? "review" : "verified"}`}>
                        {source.sourceLabel} · {source.aiLabel}
                      </span>
                      <strong>{source.usableFactCount} usable facts</strong>
                      {source.officialUrl && officialLink(source.officialUrl) && (
                        <a href={source.officialUrl} target="_blank" rel="noreferrer" onClick={event => event.stopPropagation()}>
                          Open official PDF ↗
                        </a>
                      )}
                      <span className="court-source-reason">{source.officerMessage}</span>
                    </summary>
                    <dl className="court-source-stages">
                      <div><dt>Source verification</dt><dd>{source.sourceLabel}</dd></div>
                      <div><dt>AI processing</dt><dd>{source.aiLabel}</dd></div>
                      <div><dt>Usable facts</dt><dd>{source.usableFactCount}</dd></div>
                    </dl>
                    {order && source.usableFactCount > 0 && (
                      <div className="court-history-expanded">
                        <OrderSummary order={order} />
                        <FullOrderFacts order={order} />
                      </div>
                    )}
                    <details className="court-source-technical">
                      <summary>Technical source details</summary>
                      <p>Reason code: {source.reasonCode}</p>
                      <p>Observation: {source.sourceObservationId}</p>
                      <p>Raw official date: {source.rawOrderDate ?? "Not supplied"}</p>
                      <pre>{JSON.stringify(source.technical, null, 2)}</pre>
                    </details>
                  </details>
                );
              })}

              {!data.sourceDiagnostics && data.orders.map((order, index) => (
                <details className="court-intelligence-history-row" key={`${order.officialUrl}-${index}`}>
                  <summary>
                    <time>{shownDate(order.orderDate)}{order.sourceKind === "Corrigendum" && <small> · Corrigendum</small>}</time>
                    <span className="court-timeline-summary-cell">
                      {orderDigest(order).length ? (
                        orderDigest(order)
                          .slice(0, order.presentationKind === "Routine" ? 1 : 2)
                          .map((entry, number) => (
                            <span className="court-intelligence-timeline-fact" key={number}>
                              <small className={`court-role-pill ${roleClass(entry.role)}`}>
                                {roleLabel(entry.role)}
                              </small>{" "}
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
                      {order.status === "Validated" ? "Processed" : order.status === "Unprocessed" ? "Not Processed" : order.status === "Processing" ? "Processing" : "Needs Review"}
                    </small>
                    {officialLink(order.officialUrl) && (
                      <a href={order.officialUrl} target="_blank" rel="noreferrer" onClick={event => event.stopPropagation()}>
                        Open official PDF ↗
                      </a>
                    )}
                    {order.corrigendumUrl && officialLink(order.corrigendumUrl) && (
                      <a href={order.corrigendumUrl} target="_blank" rel="noreferrer" onClick={event => event.stopPropagation()}>
                        Open official corrigendum ↗
                      </a>
                    )}
                  </summary>
                  <div className="court-history-expanded">
                    <OrderSummary order={order} />
                    <FullOrderFacts order={order} />
                  </div>
                </details>
              ))}

              {!data.sourceDiagnostics && data.sourceReviewOrders?.map((order, index) => (
                <div className="court-intelligence-history-row" key={`review-${index}`}>
                  <p>Order date needs review · <span className="court-status-pill review">Needs Review</span> · {order.reason}</p>
                  {officialLink(order.officialUrl) && <a href={order.officialUrl} target="_blank" rel="noreferrer">Open official PDF ↗</a>}
                  {order.corrigendumUrl && officialLink(order.corrigendumUrl) && <a href={order.corrigendumUrl} target="_blank" rel="noreferrer">Open official corrigendum ↗</a>}
                </div>
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

          {/* 6. Case Details & Evidence Accordion (Secondary Info) */}
          <details className="court-secondary-details-accordion">
            <summary>
              <span>Case details & evidence</span>
              <small>Bench, advocates, counsel appearances, and chronological records</small>
            </summary>
            <div className="court-secondary-details-content">
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

              {data.caseBrief && (
                <details className="court-intelligence-history">
                  <summary>Whole-case facts and party positions</summary>
                  {Object.entries(data.caseBrief).filter(([, entries]) => entries.length > 0).map(([role, entries]) => (
                    <details key={role}>
                      <summary>{roleLabel(role)} · {entries.length}</summary>
                      {entries.map((entry, index) => (
                        <div className="court-position-item" key={index}>
                          <small>{shownDate(entry.source.orderDate)}{entry.scope === "Historical" ? " · historical reference" : ""}</small>
                          <p>{officerText(entry.text)}</p>
                          <Evidence source={entry.source} />
                        </div>
                      ))}
                    </details>
                  ))}
                </details>
              )}

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
            </div>
          </details>
        </>
      )}
    </section>
  );
};
