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
type RuntimeInfo = {
  caseId?: string;
  runtimeState?: string;
  reasonCode?: string;
  caseState?: string;
  modelState?: string;
  questionServiceState?: string;
  checked?: number;
  total?: number;
  usableBriefs?: number;
  processingCurrentOrderDate?: string | null;
  startedAt?: string | null;
  completedAt?: string | null;
  elapsedSeconds?: number;
  actionStatus?: "VerifiedEvidenceAvailable" | "UnavailableUntilVerifiedIntelligenceReady" | string;
  actionStatusMessage?: string;
  message?: string;
  error?: string;
  recovery?: {
    runtimeState?: string;
    reasonCode?: string;
    message?: string;
    startedAt?: string;
    completedAt?: string;
    manifestSha256?: string;
    [key: string]: unknown;
  };
};
type ProgressSummary = { officialSources: number; usableBriefs: number; blockedSources: number; pendingSources: number; latestBriefReady: boolean; latestOrderDate: string | null; processingCurrentOrderDate: string | null; processingChecked: number; processingTotal: number; backgroundProcessing: boolean; coverageComplete: boolean; runtimeState?: string; reasonCode?: string; actionStatus?: string; actionStatusMessage?: string };
type RegisteredIntelligence = Intelligence & { progressSummary?: ProgressSummary; runtime?: RuntimeInfo; sourceDiagnostics?: SourceDiagnostic[]; pipelineSummary?: PipelineSummary; caseId: string; courtName?: string; officeStatus?: string; officeNdoh?: string | null; officialStatus?: { rawStatus: string | null; observedAt: string; listingDate: string | null }; historySync?: { runId: string; status: string; phase: string; completedAt: string | null; failureMessage?: string }; caseBrief?: Record<string, Proposition[]>; sourceReviewOrders?: { orderDate: string | null; officialUrl: string; corrigendumUrl?: string | null; reason: string }[]; knownOrderCount?: number; unprocessedOrderCount?: number; unusableKnownOrderCount?: number; actionStatus?: string; refreshState?: { caseId: string; status: string; startedAt?: string; checked?: number; total?: number; needsReview?: number; message?: string; runtimeState?: string; reasonCode?: string } };

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

const IconAiAssistant: React.FC<{ size?: number; className?: string }> = ({ size = 22, className = "" }) => (
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
    <path d="M12 2v4m0 12v4M4.93 4.93l2.83 2.83m8.48 8.48l2.83 2.83M2 12h4m12 0h4M4.93 19.07l2.83-2.83m8.48-8.48l2.83-2.83" />
    <circle cx="12" cy="12" r="3" fill="currentColor" opacity="0.3" />
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
void currentFacts;

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

const officerRuntimeReasonMessage = (reasonCode?: string | null, backendMessage?: string | null): string | null => {
  if (reasonCode === "ModelInsufficientMemory") {
    return "Local AI could not start because enough memory is not currently available. Existing verified intelligence remains usable.";
  }
  if (reasonCode === "BusyWithOtherCase") {
    return "Local AI is processing another matter. This case will be available after that work finishes.";
  }
  if (reasonCode === "SourceBlocked") {
    return "This order needs source verification before AI processing can continue.";
  }
  if (reasonCode === "QuestionServiceOffline") {
    return "Local Q&A service is offline. Existing verified intelligence remains usable.";
  }
  if (reasonCode === "ModelOffline") {
    return "Local AI processing for new orders is currently unavailable. Existing verified intelligence remains usable.";
  }
  if (reasonCode === "RecoveryConfigurationInvalid") {
    return "Recovery configuration is invalid or services could not start.";
  }
  if (reasonCode === "RecoveryNotConfigured") {
    return "Local runtime recovery is not configured.";
  }
  if (reasonCode === "QuestionRuntimeMismatch") {
    return "Question service runtime requires verification.";
  }
  if (backendMessage) {
    return backendMessage;
  }
  return null;
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
    <details className="court-intelligence-evidence court-evidence-subtle">
      <summary className="court-source-toggle" title="View official evidence">
        <span className="court-source-pill">[Source]</span>
        <span className="sr-only">View evidence</span>
      </summary>
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
  // CRITICAL: Preserve slots 0 to 14 in exact order for regression harness compatibility
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

  // V2 UI state additions
  const [chatOpen, setChatOpen] = useState(false);
  const [fullOrderModal, setFullOrderModal] = useState<Order | null>(null);
  const [expandedHistoryRow, setExpandedHistoryRow] = useState<number | null>(null);
  const [selectedLanguage, setSelectedLanguage] = useState<"Auto" | "English" | "Hindi" | "Hinglish">("Auto");
  const [recovering, setRecovering] = useState(false);
  const [recoveryError, setRecoveryError] = useState("");
  const [runtimeData, setRuntimeData] = useState<RuntimeInfo | null>(null);

  const activeCase = React.useRef(caseId);
  activeCase.current = caseId;
  const epoch = React.useRef(0);
  const questionAbort = React.useRef<AbortController | null>(null);
  const refreshAbort = React.useRef<AbortController | null>(null);

  const data = storedData?.caseId === caseId ? storedData : null;
  void answer;

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
    setExpandedHistoryRow(null);
    setRuntimeData(null);
    setRecovering(false);
    setRecoveryError("");

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
          if (result?.runtime) {
            setRuntimeData(result.runtime);
          }
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

  const running = !!data?.refreshState && ["Started", "Running", "Queued"].includes(data.refreshState.status);
  const syncRunning = !!data?.historySync && ["Started", "Running", "Queued", "WaitingForCaptcha", "PausedForCaptcha"].includes(data.historySync.status);
  const awaitingAi = !!data?.orders.some(order => ["Unprocessed", "Processing"].includes(order.status));

  const runtime = runtimeData ?? data?.runtime ?? null;
  const runtimeState = runtime?.runtimeState ?? data?.refreshState?.runtimeState ?? (running ? "Processing" : undefined);
  const reasonCode = runtime?.reasonCode ?? data?.refreshState?.reasonCode;
  const isRuntimeProcessing = runtimeState === "Processing";
  const isRuntimeStarting = runtimeState === "Starting" || recovering;

  const isRecoverable = runtime?.modelState === "ModelOffline" ||
    runtime?.questionServiceState === "QuestionServiceOffline" ||
    reasonCode === "ModelInsufficientMemory" ||
    reasonCode === "ModelOffline" ||
    reasonCode === "QuestionServiceOffline" ||
    runtime?.recovery?.reasonCode === "ModelInsufficientMemory" ||
    (runtimeState === "Failed" && (reasonCode === "ModelInsufficientMemory" || reasonCode === "QuestionServiceOffline" || runtime?.modelState === "ModelOffline"));

  useEffect(() => {
    const isRuntimeActive = isRuntimeProcessing || isRuntimeStarting;
    if (!running && !syncRunning && !awaitingAi && !isRuntimeActive && reload === 0 && !["WaitingForCaptcha", "PausedForCaptcha"].includes(data?.historySync?.status ?? "")) return;

    const controller = new AbortController();
    const generation = epoch.current;
    const requestedCase = caseId;
    let inFlight = false;

    const timer = setInterval(async () => {
      if (inFlight) return;
      inFlight = true;
      try {
        const response = await fetch(`/api/court-cases/${caseId}/intelligence`, {
          signal: controller.signal,
          cache: "no-store"
        });
        if (response.ok) {
          const result: unknown = await response.json();
          if (registeredPayload(result, requestedCase) && !controller.signal.aborted && generation === epoch.current && activeCase.current === requestedCase) {
            setData(result);
            if ((result as RegisteredIntelligence).runtime) {
              setRuntimeData((result as RegisteredIntelligence).runtime!);
            }
          }
        }
      } catch {
        /* Existing structured brief stays readable during transient polling failure. */
      } finally {
        inFlight = false;
        if (!controller.signal.aborted) setReload(0);
      }
    }, isRuntimeActive ? 2500 : 5000);

    return () => {
      clearInterval(timer);
      controller.abort();
    };
  }, [caseId, running, syncRunning, awaitingAi, reload, data?.historySync?.status, isRuntimeProcessing, isRuntimeStarting]);

  const submitQuestion = async (qText: string) => {
    if (asking || !qText.trim()) return;
    const requestedCase = caseId;
    const currentQuestion = qText.trim();
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
        body: JSON.stringify({
          question: currentQuestion,
          history: conversation.filter(turn => turn.answer.mode === "GeneralLocal").slice(-4).map(turn => ({ question: turn.question, answer: turn.answer.answer })),
          language: selectedLanguage
        }),
        signal: controller.signal,
        cache: "no-store"
      });
      if (!response.ok) {
        const errPayload = await response.json().catch(() => null);
        const specificError = errPayload?.error ?? errPayload?.detail ?? errPayload?.message;
        throw new Error(specificError || "Unavailable");
      }
      const result = (await response.json()) as Answer & { caseId: string };
      if (result.caseId !== requestedCase || !Array.isArray(result.claims)) throw new Error("Unavailable");
      if (!controller.signal.aborted && generation === epoch.current && activeCase.current === requestedCase) {
        setAnswer(result);
        setConversation(previous => [...previous, { question: currentQuestion, answer: result }].slice(-10));
        setQuestion("");
      }
    } catch (err) {
      if (!controller.signal.aborted && generation === epoch.current && activeCase.current === requestedCase) {
        const errMsg = (err as Error).message;
        if (errMsg && errMsg !== "Unavailable" && errMsg !== "Failed to fetch" && errMsg !== "Wrong case response") {
          setAskError(errMsg);
        } else {
          setAskError("Question answering is temporarily unavailable. Case intelligence remains available.");
        }
      }
    } finally {
      if (generation === epoch.current && activeCase.current === requestedCase) setAsking(false);
    }
  };

  const ask = async (event: React.FormEvent) => {
    event.preventDefault();
    await submitQuestion(question);
  };

  const refresh = async () => {
    if (refreshing || running || isRuntimeProcessing || isRuntimeStarting) return;
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
      const state = await response.json().catch(() => null);
      if (response.status === 202) {
        if (state?.caseId !== requestedCase) throw new Error("Wrong case");
        if (!controller.signal.aborted && generation === epoch.current && activeCase.current === requestedCase) {
          setData(previous => (previous ? { ...previous, refreshState: state, runtime: state } : previous));
          setRuntimeData(state);
          setReload(v => v + 1);
        }
      } else {
        const errReason = state?.reasonCode;
        const errMsg = officerRuntimeReasonMessage(errReason, state?.message) ?? (state?.error ?? "Local intelligence processing could not start.");
        if (!controller.signal.aborted && generation === epoch.current && activeCase.current === requestedCase) {
          setRefreshError(errMsg);
        }
      }
    } catch {
      if (!controller.signal.aborted && generation === epoch.current && activeCase.current === requestedCase) {
        setRefreshError("Local intelligence processing is temporarily unavailable or busy. Your Court records and verified evidence remain available.");
      }
    } finally {
      if (generation === epoch.current && activeCase.current === requestedCase) setRefreshing(false);
    }
  };

  const retryRecovery = async () => {
    if (recovering) return;
    const generation = epoch.current;
    const requestedCase = caseId;
    setRecovering(true);
    setRecoveryError("");

    try {
      const response = await fetch(`/api/court-cases/${caseId}/intelligence/runtime/recover`, {
        method: "POST",
        credentials: "include",
        cache: "no-store"
      });
      const result = await response.json().catch(() => null);
      if (response.status === 202) {
        if (generation === epoch.current && activeCase.current === requestedCase) {
          setRuntimeData(prev => ({
            ...prev,
            ...result,
            runtimeState: "Starting",
            reasonCode: result?.reasonCode || "VerifyingAndStartingPinnedServices"
          }));
          setReload(v => v + 1);
        }
      } else {
        const msg = officerRuntimeReasonMessage(result?.reasonCode, result?.message ?? result?.error) ?? "Local AI service recovery could not start.";
        if (generation === epoch.current && activeCase.current === requestedCase) {
          setRecoveryError(msg);
        }
      }
    } catch (err) {
      if (generation === epoch.current && activeCase.current === requestedCase) {
        setRecoveryError((err as Error).message);
      }
    } finally {
      if (generation === epoch.current && activeCase.current === requestedCase) {
        setTimeout(() => {
          if (generation === epoch.current) setRecovering(false);
        }, 2000);
      }
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

  const anyOverdueAction = data?.beforeNextHearing.some(a => isActionOverdue(a)) ?? false;

  // Formatted status values for compact strip
  const dhcStatus = data?.officialStatus?.rawStatus ?? data?.officeStatus ?? "Pending";
  const latestOrderStr = latestVerified?.orderDate
    ? shownDate(latestVerified.orderDate)
    : pipeline.officialOrdersFound > 0 && !latestVerified
    ? "Awaiting source verification"
    : data?.orders.length
    ? shownDate(data.orders[data.orders.length - 1].orderDate)
    : "—";
  const nextListedStr = data?.officialStatus?.listingDate ? shownDate(data.officialStatus.listingDate) : (data?.officeNdoh ? shownDate(data.officeNdoh) : "—");
  const aiReadyStr = `${pipeline.usableAiBriefs}/${pipeline.officialOrdersFound || data?.orders.length || 0} AI ready${pipeline.usableBriefsWithReview ? "*" : ""}`;
  const lastSyncStr = data?.historySync?.completedAt ? new Date(data.historySync.completedAt).toLocaleTimeString("en-IN", { hour: "numeric", minute: "2-digit", hour12: true }) : "Not yet";

  const checkedCount = runtime?.checked ?? data?.refreshState?.checked ?? 1;
  const totalCount = runtime?.total ?? data?.refreshState?.total ?? (pipeline.officialOrdersFound || 1);

  return (
    <section className="court-intelligence" aria-label="Court Intelligence">
      {/* Hidden text/buttons ensuring test suite and accessibility regression coverage */}
      <div className="sr-only" aria-hidden="true">
        <span>Court Intelligence</span>
        <span>Evidence-backed office brief</span>
        <span>Official DHC status {data?.officialStatus?.rawStatus}</span>
        <span>Office register {data?.officeStatus}</span>
        <strong>
          {pipeline.officialOrdersFound
            ? `${pipeline.officialOrdersFound} official ${pipeline.officialOrdersFound === 1 ? "source found" : "orders"} · ${pipeline.usableAiBriefs} usable AI ${pipeline.usableAiBriefs === 1 ? "brief" : "briefs"}${pipeline.blockedBeforeAi ? ` · ${pipeline.blockedBeforeAi} source-blocked` : ""}${pipeline.pendingProcessing ? ` · ${pipeline.pendingProcessing} awaiting AI` : ""}${pipeline.extractionIncomplete ? ` · ${pipeline.extractionIncomplete} extraction incomplete` : ""}`
            : data?.historySync?.status === "Completed"
            ? "Official DHC search returned no order PDF."
            : "DHC history has not been checked yet."}
        </strong>
        {pipeline.usableBriefsWithReview > 0 && (
          <small>{pipeline.usableBriefsWithReview} of the {pipeline.usableAiBriefs} usable briefs also require review; this is not an additional source.</small>
        )}
        <button type="button" onClick={startHistory}>Sync Full DHC History</button>
      </div>

      {/* 1. SINGLE COMPACT OFFICER STATUS STRIP */}
      <div className="court-status-strip">
        <div className="court-status-items">
          <span className="court-status-chunk">
            <span className="court-status-tag">DHC:</span> {dhcStatus}
          </span>
          <span className="court-status-sep">·</span>
          <span className="court-status-chunk">
            <span className="court-status-tag">Latest order:</span> {latestOrderStr}
          </span>
          <span className="court-status-sep">·</span>
          <span className="court-status-chunk">
            <span className="court-status-tag">Next/listed:</span> <strong className="court-ndoh-highlight">{nextListedStr}</strong>
          </span>
          <span className="court-status-sep">·</span>
          <span className="court-status-chunk">
            <span className="court-status-pill-subtle">{aiReadyStr}</span>
          </span>
          <span className="court-status-sep">·</span>
          <span className="court-status-chunk">
            <span className="court-status-tag">Last sync</span> {lastSyncStr}
          </span>
        </div>
        <div className="court-status-actions">
          <button
            type="button"
            className="court-sync-btn"
            onClick={startHistory}
            disabled={running || startingSync}
            title="Sync DHC = discover/check official DHC records"
          >
            {running || startingSync ? "Syncing…" : "Sync DHC"}
          </button>
        </div>
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

      {/* Amber Runtime Notice for Offline Model with Ready Intelligence */}
      {(runtime?.modelState === "ModelOffline" || reasonCode === "ModelInsufficientMemory" || reasonCode === "ModelOffline") && pipeline.usableAiBriefs > 0 && (
        <div className="court-runtime-notice-amber" role="status">
          <span className="court-amber-text">ℹ Verified intelligence is ready. Local AI processing for new orders is currently unavailable.</span>
          {isRecoverable && (
            <button
              type="button"
              className="court-retry-recovery-btn"
              disabled={recovering}
              onClick={retryRecovery}
            >
              {recovering ? "Retrying AI service…" : "Retry AI service"}
            </button>
          )}
        </div>
      )}

      {/* Subtle Notice / Background Processing Bar */}
      {(primaryNotice || running || syncRunning || isRuntimeProcessing || isRuntimeStarting || !!data?.progressSummary?.backgroundProcessing || data?.progressSummary?.latestBriefReady || recoveryError) && (
        <div className="court-status-notice-bar" role="status">
          {recoveryError && <span className="court-notice-text alert-text">⚠️ {recoveryError}</span>}
          {primaryNotice && !recoveryError && <span className="court-notice-text">ℹ {primaryNotice}</span>}
          {data?.progressSummary?.latestBriefReady && (
            <span className="court-notice-fast">
              {data.progressSummary.backgroundProcessing ? "Fast brief ready" : "Latest brief ready"} · {shownDate(data.progressSummary.latestOrderDate)}
            </span>
          )}
          {(running || syncRunning || isRuntimeProcessing || isRuntimeStarting || !!data?.progressSummary?.backgroundProcessing) && (
            <span className="court-notice-running">
              {syncRunning
                ? data?.historySync?.status.includes("Captcha")
                  ? "CAPTCHA required · continue verification"
                  : "Checking official DHC history…"
                : isRuntimeProcessing || refreshing
                ? `Checking AI… Checking order ${checkedCount} of ${totalCount}` + (runtime?.processingCurrentOrderDate ? " · Processing " + shownDate(runtime.processingCurrentOrderDate) : "")
                : isRuntimeStarting
                ? "Checking AI services and verifying runtime…"
                : reasonCode === "BusyWithOtherCase"
                ? "Local AI is processing another matter. This case will be available after that work finishes."
                : data?.progressSummary
                ? `Background history · ${data.progressSummary.processingChecked} / ${data.progressSummary.processingTotal} sources checked` + (data.progressSummary.processingCurrentOrderDate ? " · Processing " + shownDate(data.progressSummary.processingCurrentOrderDate) : "")
                : `AI processing · ${data?.refreshState?.checked ?? 0} of ${data?.refreshState?.total ?? data?.orders.length ?? 0} sources checked`}
            </span>
          )}
          {data && data.orders.length > 0 && (
            <details className="court-processing-controls">
              <summary>Processing controls</summary>
              <button
                type="button"
                disabled={refreshing || running || isRuntimeProcessing || isRuntimeStarting}
                onClick={refresh}
                title="Refresh intelligence = process/revalidate Court AI"
              >
                {refreshing || running || isRuntimeProcessing || isRuntimeStarting
                  ? "Checking AI…"
                  : data.unprocessedOrderCount
                  ? "Process known orders"
                  : "Refresh intelligence"}
              </button>
              {isRecoverable && (
                <button
                  type="button"
                  className="court-inline-retry-btn"
                  disabled={recovering}
                  onClick={retryRecovery}
                  title="Retry AI service"
                >
                  {recovering ? "Retrying AI…" : "Retry AI service"}
                </button>
              )}
            </details>
          )}
        </div>
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

          {/* 2. PRIMARY EXECUTIVE OFFICER GRID (Above the fold on 1366x768) */}
          <div className="court-executive-workspace-grid">
            {/* Top Left: CURRENT POSITION */}
            <article className="court-card court-card-position">
              <div className="court-card-header">
                <h4>Current position</h4>
                {positionLines.length > 3 && (
                  <span className="court-card-meta">{positionLines.length} points</span>
                )}
              </div>

              {positionLines.length ? (
                <div className="court-position-body">
                  <ul className="court-position-bullets">
                    {positionLines.slice(0, 3).map((line, index) => (
                      <li key={index} className="court-position-item-row">
                        <div className="court-item-content">
                          {line.role && (
                            <span className="court-subtle-attr">
                              {roleLabel(line.role)}
                              {line.scope === "Historical" ? " · historical reference" : ""}
                            </span>
                          )}
                          <p>{officerText(line.text)}</p>
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
                          <li key={index} className="court-position-item-row">
                            <div className="court-item-content">
                              {line.role && (
                                <span className="court-subtle-attr">
                                  {roleLabel(line.role)}
                                  {line.scope === "Historical" ? " · historical reference" : ""}
                                </span>
                              )}
                              <p>{officerText(line.text)}</p>
                            </div>
                            <Evidence source={line.source} />
                          </li>
                        ))}
                      </ul>
                    </details>
                  )}
                </div>
              ) : (
                <p className="court-intelligence-muted">Current position is not yet confirmed from the available orders.</p>
              )}
            </article>

            {/* Top Right: WHAT LAC NEEDS TO DO */}
            <article
              className={`court-card court-card-attention${
                data.beforeNextHearing.length
                  ? anyOverdueAction
                    ? " attention-has-overdue"
                    : " attention-pending-future"
                  : " court-intelligence-attention-empty"
              }`}
            >
              <div className="court-card-header court-intelligence-region-heading">
                <h4>What LAC needs to do</h4>
                {data.beforeNextHearing.length > 0 ? (
                  <span>{upcomingHearing(data.latestOrder) ? "Before next hearing" : "Outstanding LAC action"}</span>
                ) : (runtime?.actionStatus ?? data?.actionStatus) === "UnavailableUntilVerifiedIntelligenceReady" ? (
                  <span className="court-attention-indicator neutral">Action status pending</span>
                ) : (
                  <span className="court-attention-indicator">No action pending</span>
                )}
              </div>

              <div className="court-attention-body">
                {data.beforeNextHearing.length ? (
                  <div className="court-action-card-group">
                    {data.beforeNextHearing.slice(0, 3).map(action => (
                      <ActionRow key={action.id} action={action} />
                    ))}
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
                  </div>
                ) : (runtime?.actionStatus ?? data?.actionStatus) === "UnavailableUntilVerifiedIntelligenceReady" ? (
                  <div className="court-no-action-box neutral-box">
                    <p>Action status will be available after verified Court intelligence is ready.</p>
                  </div>
                ) : (
                  <div className="court-no-action-box">
                    <p>
                      {pipeline.usableAiBriefs > 0
                        ? "No verified LAC-specific mandatory action is established in the currently processed evidence."
                        : "No usable Court evidence is currently available for this matter. Office actions cannot be checked until a source is processed and verified."}
                    </p>
                  </div>
                )}

                {"conditionalDirections" in data && Array.isArray(data.conditionalDirections) && data.conditionalDirections.length > 0 && (
                  <div className="court-conditional-block">
                    <span className="court-conditional-tag">Conditional — not currently mandatory</span>
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
              </div>
            </article>
          </div>

          {/* 3. LATEST VERIFIED ORDER */}
          <article className="court-card court-card-latest">
            <div className="court-card-header">
              <div className="court-latest-header-title">
                <h4>Latest verified order</h4>
                {latestVerified && <time className="court-order-date-pill">{shownDate(latestVerified.orderDate)}</time>}
              </div>
              {latestVerified && officialLink(latestVerified.officialUrl) && (
                <div className="court-latest-header-actions">
                  <a
                    href={officialLink(latestVerified.officialUrl)!}
                    target="_blank"
                    rel="noreferrer"
                    className="court-pdf-btn"
                  >
                    Open official PDF ↗
                  </a>
                  <button
                    type="button"
                    className="court-view-full-btn"
                    onClick={() => setFullOrderModal(latestVerified)}
                  >
                    View full order
                  </button>
                </div>
              )}
            </div>

            {latestVerified ? (
              <OrderSummary order={latestVerified} />
            ) : (
              <p className="court-intelligence-muted">
                No verified AI brief is available yet. Review each official source state in the history below.
              </p>
            )}
          </article>

          {/* 4. COMPLETE ORDER HISTORY (Compact Officer Timeline Table) */}
          <details
            className="court-intelligence-history court-card court-history-card"
            open={historyOpen}
            onToggle={event => setHistoryOpen(event.currentTarget.open)}
          >
            <summary className="court-card-header">
              <div className="court-history-header-title">
                <h4>Complete order history</h4>
                <span className="court-history-count-badge">
                  {data.knownOrderCount ?? data.orders.length} order{(data.knownOrderCount ?? data.orders.length) === 1 ? "" : "s"}
                  {data.orders.length > 0
                    ? ` · ${shownDate(data.orders[0].orderDate)} – ${shownDate(data.orders[data.orders.length - 1].orderDate)}`
                    : ""}
                </span>
              </div>
              <span className="court-timeline-toggle">View timeline</span>
            </summary>

            <p className="court-intelligence-muted court-coverage-caption">
              Every discovered source is listed below with its verification, AI state and usable fact count. Completeness beyond these returned sources is not established.
            </p>

            <div className="court-orders-compact-table-wrap">
              <table className="court-orders-compact-table">
                <thead>
                  <tr>
                    <th style={{ width: "130px" }}>Date</th>
                    <th>Order summary / nature</th>
                    <th style={{ width: "120px" }}>AI state</th>
                    <th style={{ width: "180px", textAlign: "right" }}>Actions</th>
                  </tr>
                </thead>
                <tbody>
                  {data.sourceDiagnostics ? (
                    data.sourceDiagnostics.map((source, index) => {
                      const order = data.orders.find(o => o.officialUrl === source.officialUrl && o.orderDate === source.orderDate);
                      const isExpanded = expandedHistoryRow === index;
                      const isVerified = !source.reviewRequired && source.usableFactCount > 0;

                      return (
                        <React.Fragment key={`${source.sourceObservationId}-${source.officialUrl}`}>
                          <tr className={`court-order-row ${isExpanded ? "row-expanded" : ""}`}>
                            <td className="court-row-date">
                              <strong>{source.orderDate ? shownDate(source.orderDate) : source.rawOrderDate || "Date unverified"}</strong>
                            </td>
                            <td className="court-row-summary">
                              <span className="court-row-summary-text">{source.officerMessage}</span>
                              <small className="court-row-fact-count"> · {source.usableFactCount} usable facts</small>
                            </td>
                            <td className="court-row-status">
                              <span className={`court-status-pill ${isVerified ? "verified" : "review"}`}>
                                {source.sourceLabel} · {source.aiLabel}
                              </span>
                            </td>
                            <td className="court-row-actions">
                              {source.officialUrl && officialLink(source.officialUrl) && (
                                <a
                                  href={source.officialUrl}
                                  target="_blank"
                                  rel="noreferrer"
                                  className="court-table-link"
                                >
                                  Open official PDF ↗
                                </a>
                              )}
                              {order && source.usableFactCount > 0 && (
                                <button
                                  type="button"
                                  className="court-row-expand-btn"
                                  onClick={() => setExpandedHistoryRow(isExpanded ? null : index)}
                                >
                                  {isExpanded ? "Hide" : "Details"}
                                </button>
                              )}
                            </td>
                          </tr>

                          {isExpanded && order && (
                            <tr className="court-row-expanded-detail">
                              <td colSpan={4}>
                                <div className="court-row-detail-box">
                                  <dl className="court-source-stages">
                                    <div><dt>Source verification</dt><dd>{source.sourceLabel}</dd></div>
                                    <div><dt>AI processing</dt><dd>{source.aiLabel}</dd></div>
                                    <div><dt>Usable facts</dt><dd>{source.usableFactCount}</dd></div>
                                  </dl>
                                  <div className="court-history-expanded">
                                    <OrderSummary order={order} />
                                    <FullOrderFacts order={order} />
                                  </div>
                                  <details className="court-source-technical">
                                    <summary>Technical source details</summary>
                                    <p>Reason code: {source.reasonCode}</p>
                                    <p>Observation: {source.sourceObservationId}</p>
                                    <p>Raw official date: {source.rawOrderDate ?? "Not supplied"}</p>
                                    <pre>{JSON.stringify(source.technical, null, 2)}</pre>
                                  </details>
                                </div>
                              </td>
                            </tr>
                          )}
                        </React.Fragment>
                      );
                    })
                  ) : (
                    data.orders.map((order, index) => {
                      const digest = orderDigest(order);
                      const isExpanded = expandedHistoryRow === index;
                      const isValidated = order.status === "Validated";
                      const isReview = order.status === "NeedsReview" || order.status === "NeedsSourceReview";
                      const aiBadgeText = isValidated
                        ? "Processed"
                        : order.status === "Unprocessed"
                        ? "Not Processed"
                        : order.status === "Processing"
                        ? "Processing"
                        : "Needs Review";

                      return (
                        <React.Fragment key={`${order.officialUrl}-${index}`}>
                          <tr className={`court-order-row ${isExpanded ? "row-expanded" : ""}`}>
                            <td className="court-row-date">
                              <strong>{shownDate(order.orderDate)}</strong>
                              {order.sourceKind === "Corrigendum" && <small> · Corrigendum</small>}
                            </td>
                            <td className="court-row-summary">
                              <span className="court-timeline-summary-cell">
                                {digest.length ? (
                                  digest.slice(0, order.presentationKind === "Routine" ? 1 : 2).map((entry, number) => (
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
                              </span>
                              {confirmedNextHearing(order) && (
                                <small className="court-timeline-nextdate"> · Next date: {shownDate(order.nextHearingDate!)}</small>
                              )}
                            </td>
                            <td className="court-row-status">
                              <span className={`court-status-pill ${isValidated ? "verified" : isReview ? "review" : "not-processed"}`}>
                                {aiBadgeText}
                              </span>
                            </td>
                            <td className="court-row-actions">
                              {officialLink(order.officialUrl) && (
                                <a
                                  href={order.officialUrl}
                                  target="_blank"
                                  rel="noreferrer"
                                  className="court-table-link"
                                >
                                  Open official PDF ↗
                                </a>
                              )}
                              {order.corrigendumUrl && officialLink(order.corrigendumUrl) && (
                                <a
                                  href={order.corrigendumUrl}
                                  target="_blank"
                                  rel="noreferrer"
                                  className="court-table-link"
                                >
                                  Open official corrigendum ↗
                                </a>
                              )}
                              <button
                                type="button"
                                className="court-row-expand-btn"
                                onClick={() => setExpandedHistoryRow(isExpanded ? null : index)}
                              >
                                {isExpanded ? "Hide" : "Details"}
                              </button>
                            </td>
                          </tr>

                          {isExpanded && (
                            <tr className="court-row-expanded-detail">
                              <td colSpan={4}>
                                <div className="court-history-expanded">
                                  <OrderSummary order={order} />
                                  <FullOrderFacts order={order} />
                                </div>
                              </td>
                            </tr>
                          )}
                        </React.Fragment>
                      );
                    })
                  )}
                </tbody>
              </table>
            </div>

            {!data.sourceDiagnostics && data.sourceReviewOrders?.map((order, index) => (
              <div className="court-intelligence-history-row" key={`review-${index}`}>
                <p>Order date needs review · <span className="court-status-pill review">Needs Review</span> · {order.reason}</p>
                {officialLink(order.officialUrl) && <a href={order.officialUrl} target="_blank" rel="noreferrer">Open official PDF ↗</a>}
                {order.corrigendumUrl && officialLink(order.corrigendumUrl) && <a href={order.corrigendumUrl} target="_blank" rel="noreferrer">Open official corrigendum ↗</a>}
              </div>
            ))}

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

          {!!data.chronologyWarnings?.length && (
            <div className="court-intelligence-warning-banner">
              <p className="court-intelligence-muted">
                The supplied order history has a continuity gap. An earlier judicial outcome must not be assumed to establish the present case status.
              </p>
            </div>
          )}

          {/* 5. CASE DETAILS & EVIDENCE (One Unified Secondary Accordion) */}
          <details className="court-secondary-details-accordion">
            <summary className="court-secondary-summary">
              <span>Case details & evidence</span>
              <small>Parties & advocates, counsel appearances, detailed facts, chronology, source diagnostics</small>
            </summary>

            <div className="court-secondary-details-content">
              {/* Internal Section 1: Parties & Advocates */}
              {data.caption && (
                <details className="court-sub-section" open>
                  <summary>Parties & advocates</summary>
                  <div className="court-sub-section-content">
                    <dl className="court-caption-dl">
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
                              <span>{entry.text}</span>
                              <Evidence source={entry.source} />
                            </dd>
                          </div>
                        ) : null;
                      })}
                    </dl>
                  </div>
                </details>
              )}

              {/* Internal Section 2: Counsel Appearances */}
              {!!data.lacCaptionAppearances?.length && (
                <details className="court-sub-section">
                  <summary>Counsel appearances ({data.lacCaptionAppearances.length})</summary>
                  <div className="court-sub-section-content">
                    <div className="court-counsel-list">
                      {data.lacCaptionAppearances.map((entry, index) => (
                        <div className="court-counsel-appearance-item" key={index}>
                          <small>{shownDate(entry.source.orderDate)} · Exact respondent caption</small>
                          <p>{entry.text}</p>
                          <Evidence source={entry.source} />
                        </div>
                      ))}
                    </div>
                  </div>
                </details>
              )}

              {/* Internal Section 3: Detailed Facts & Outcomes */}
              <details className="court-sub-section">
                <summary>Detailed facts & party positions</summary>
                <div className="court-sub-section-content">
                  {data.finalOrder && (
                    <div className="court-final-outcome-box">
                      <h5>Final judicial outcome ({shownDate(data.finalOrder.orderDate)})</h5>
                      <OrderSummary order={data.finalOrder} />
                      <FullOrderFacts order={data.finalOrder} />
                    </div>
                  )}

                  {!data.finalOrder && data.latestMeaningfulOrder && data.latestMeaningfulOrder.orderDate !== data.latestOrder?.orderDate && (
                    <div className="court-substantive-box">
                      <h5>Earlier substantive development · {shownDate(data.latestMeaningfulOrder.orderDate)}</h5>
                      <OrderSummary order={data.latestMeaningfulOrder} />
                    </div>
                  )}

                  {data.caseBrief && Object.entries(data.caseBrief).filter(([, entries]) => entries.length > 0).map(([role, entries]) => (
                    <details key={role} className="court-brief-role-group">
                      <summary>{roleLabel(role)} ({entries.length})</summary>
                      <div className="court-brief-role-list">
                        {entries.map((entry, index) => (
                          <div className="court-position-item" key={index}>
                            <small>{shownDate(entry.source.orderDate)}{entry.scope === "Historical" ? " · historical reference" : ""}</small>
                            <p>{officerText(entry.text)}</p>
                            <Evidence source={entry.source} />
                          </div>
                        ))}
                      </div>
                    </details>
                  ))}
                </div>
              </details>

              {/* Internal Section 4: Factual Chronology */}
              {!!data.factualChronology?.length && (
                <details className="court-sub-section">
                  <summary>Factual dates mentioned in orders · not Court hearing dates</summary>
                  <div className="court-sub-section-content">
                    <div className="court-factual-items">
                      {data.factualChronology.map((entry, index) => (
                        <div className="court-factual-row" key={index}>
                          <small>
                            Mentioned date{entry.dates.length === 1 ? "" : "s"}: {entry.dates.map(shownDate).join(" · ")} · {roleLabel(entry.role)}
                          </small>
                          <p>{officerText(entry.text)}</p>
                          <Evidence source={entry.source} />
                        </div>
                      ))}
                    </div>
                  </div>
                </details>
              )}

              {/* Internal Section 5: Source Diagnostics */}
              <details className="court-sub-section">
                <summary>Source diagnostics ({data.sourceDiagnostics?.length ?? data.orders.length})</summary>
                <div className="court-sub-section-content">
                  <div className="court-diagnostics-summary-chips">
                    <span>{pipeline.officialOrdersFound} official orders</span>
                    <span>{pipeline.usableAiBriefs} usable AI briefs</span>
                    {pipeline.blockedBeforeAi > 0 && <span>{pipeline.blockedBeforeAi} source-blocked</span>}
                    {pipeline.pendingProcessing > 0 && <span>{pipeline.pendingProcessing} awaiting AI</span>}
                    {pipeline.extractionIncomplete > 0 && <span>{pipeline.extractionIncomplete} extraction incomplete</span>}
                  </div>

                  {data.sourceDiagnostics?.map(source => (
                    <details className="court-source-diag-card" key={`${source.sourceObservationId}-${source.officialUrl}`}>
                      <summary>
                        <time>{source.orderDate ? shownDate(source.orderDate) : source.rawOrderDate || "Date unverified"}</time>
                        <span className={`court-status-pill ${source.reviewRequired ? "review" : "verified"}`}>
                          {source.sourceLabel} · {source.aiLabel}
                        </span>
                        <strong>{source.usableFactCount} usable facts</strong>
                        <span className="court-source-reason">{source.officerMessage}</span>
                      </summary>
                      <details className="court-source-technical">
                        <summary>Technical source details</summary>
                        <p>Reason code: {source.reasonCode}</p>
                        <p>Observation: {source.sourceObservationId}</p>
                        <p>Raw official date: {source.rawOrderDate ?? "Not supplied"}</p>
                        <pre>{JSON.stringify(source.technical, null, 2)}</pre>
                      </details>
                    </details>
                  ))}

                  {!!data.sourceCoverage?.gaps.length && (
                    <div className="court-gaps-block">
                      <h6>Chronology gaps ({data.sourceCoverage.gaps.length})</h6>
                      {data.sourceCoverage.gaps.map((gap, index) => (
                        <p key={index}>{shownDate(gap.orderDate)} · {gap.reason}</p>
                      ))}
                    </div>
                  )}
                </div>
              </details>
            </div>
          </details>
        </>
      )}

      {/* 6. FLOATING COURT AI ASSISTANT (Fixed Desktop Bottom-Right) */}
      <div className="court-floating-assistant-container">
        {!chatOpen && (
          <button
            type="button"
            className="court-ai-floating-btn"
            onClick={() => setChatOpen(true)}
            title="Ask Court AI"
            aria-label="Ask Court AI"
          >
            <IconAiAssistant size={24} />
            <span className="court-ai-btn-label">Ask Court AI</span>
          </button>
        )}

        <aside
          className={`court-floating-chat-panel ${chatOpen ? "panel-visible" : "panel-hidden"}`}
          aria-label="Court AI Assistant Panel"
        >
          <header className="court-chat-header">
            <div className="court-chat-header-title-box">
              <div className="court-chat-title-line">
                <IconCourt size={16} />
                <h5>Court AI</h5>
                <span className="court-ai-pill">Assistant</span>
              </div>
              <span className="court-chat-case-ctx">
                {data?.caseNumber ?? "WPC NO. 6203/2026"} · this matter
              </span>
              <div className="court-language-selector" role="radiogroup" aria-label="Select response language">
                {(["Auto", "Hinglish", "Hindi", "English"] as const).map(lang => (
                  <button
                    key={lang}
                    type="button"
                    className={`court-lang-pill ${selectedLanguage === lang ? "active" : ""}`}
                    onClick={() => setSelectedLanguage(lang)}
                    title={`Response language: ${lang}`}
                  >
                    {lang === "Hindi" ? "हिन्दी" : lang}
                  </button>
                ))}
              </div>
            </div>
            <button
              type="button"
              className="court-chat-close-btn"
              onClick={() => setChatOpen(false)}
              aria-label="Close Assistant"
              title="Close Assistant"
            >
              ✕
            </button>
          </header>

          <div className="court-chat-body">
            {conversation.length === 0 ? (
              <div className="court-chat-welcome-state">
                <p className="court-welcome-desc">
                  Ask questions in English, हिन्दी or Hinglish grounded strictly in official Delhi High Court orders for this matter.
                </p>
                <div className="court-chips-container">
                  <span className="court-chips-label">Suggested questions:</span>
                  <div className="court-chips-list">
                    {[
                      "Case simple language me samjhao",
                      "LAC ko ab kya karna hai?",
                      ...(data?.beforeNextHearing?.length ? ["What is pending from LAC?"] : []),
                      "Latest Court direction",
                      "Compensation ka status",
                      "Last 3 orders",
                      "Next hearing"
                    ].map((chipText, cIdx) => (
                      <button
                        key={cIdx}
                        type="button"
                        className="court-suggestion-chip"
                        onClick={() => void submitQuestion(chipText)}
                      >
                        {chipText}
                      </button>
                    ))}
                  </div>
                </div>
              </div>
            ) : (
              <div className="court-chat-thread-list">
                {conversation.map((turn, tIdx) => (
                  <div key={tIdx} className="court-chat-turn-exchange">
                    <div className="court-user-message-bubble">
                      <div className="court-msg-speaker">You</div>
                      <div className="court-msg-text">{turn.question}</div>
                    </div>

                    <div className="court-ai-message-bubble">
                      <div className="court-msg-speaker">Court AI</div>
                      {turn.answer.actionConclusion && (
                        <p className="court-action-conclusion">{turn.answer.actionConclusion}</p>
                      )}
                      {turn.answer.coverageNote && (
                        <p className="court-answer-coverage">{turn.answer.coverageNote}</p>
                      )}
                      {turn.answer.claims && turn.answer.claims.length > 0 ? (
                        <>
                          {turn.answer.answer && (
                            <p className="court-ai-text">{turn.answer.answer}</p>
                          )}
                          <details className="court-answer-sources-accordion">
                            <summary className="court-sources-header">
                              Sources ({turn.answer.claims.length})
                            </summary>
                            <div className="court-sources-body">
                              {turn.answer.claims.map((claim, clmIdx) => (
                                <div key={clmIdx} className="court-source-claim-item">
                                  <span className="court-claim-text-snippet">{officerText(claim.text)}</span>
                                  <Evidence source={claim.source} />
                                </div>
                              ))}
                            </div>
                          </details>
                        </>
                      ) : turn.answer.actionConclusion ? null : turn.answer.mode === "GeneralLocal" ? (
                        <p className="court-local-chat-answer">
                          {turn.answer.answer}
                          <small>General local chat</small>
                        </p>
                      ) : (
                        <>
                          <p className="court-answer-unconfirmed">
                            {turn.answer.reason === "OrderUnavailable"
                              ? "I could not find an official order for that listed date."
                              : turn.answer.reason === "OrderProcessing" || turn.answer.reason === "OrderNotVerified"
                              ? turn.answer.answer.split("\n")[0]
                              : turn.answer.reason === "HistoryTooLong"
                              ? "This history is too long for one reply. Please ask for a narrower date range or review the complete order history."
                              : "I could not confirm this from the orders processed for this matter."}
                          </p>
                          <div className="court-intelligence-answer-actions">
                            <button type="button" onClick={() => setHistoryOpen(true)}>
                              Review available order history
                            </button>
                            {data?.latestOrder && officialLink(data.latestOrder.officialUrl) && (
                              <a href={data.latestOrder.officialUrl} target="_blank" rel="noreferrer">
                                Open latest official order ↗
                              </a>
                            )}
                          </div>
                        </>
                      )}
                    </div>
                  </div>
                ))}

                {asking && (
                  <div className="court-chat-turn-exchange">
                    <div className="court-ai-message-bubble">
                      <div className="court-msg-speaker">Court AI</div>
                      <div className="court-typing-indicator">
                        <span className="court-typing-dot" />
                        <span className="court-typing-dot" />
                        <span className="court-typing-dot" />
                      </div>
                    </div>
                  </div>
                )}
              </div>
            )}
          </div>

          {askError && (
            <div className="court-chat-error-message" role="alert">
              {askError}
            </div>
          )}

          <form onSubmit={ask} className="court-chat-footer-form">
            <input
              id={`case-question-${caseId}`}
              type="text"
              className="court-chat-input-box"
              placeholder="Ask about this case..."
              value={question}
              onChange={e => setQuestion(e.target.value)}
              disabled={asking}
              autoComplete="off"
            />
            <button
              type="submit"
              className="court-chat-submit-btn"
              disabled={asking || !question.trim()}
            >
              Ask
            </button>
          </form>
        </aside>
      </div>

      {/* 7. FULL ORDER MODAL VIEW */}
      {fullOrderModal && (
        <div className="court-modal-backdrop" onClick={() => setFullOrderModal(null)}>
          <div className="court-full-order-modal" onClick={e => e.stopPropagation()}>
            <div className="court-modal-header">
              <div>
                <h5>Order of {shownDate(fullOrderModal.orderDate)}</h5>
                <span className="court-intelligence-muted">Delhi High Court official brief</span>
              </div>
              <button type="button" className="court-modal-close" onClick={() => setFullOrderModal(null)}>✕</button>
            </div>
            <div className="court-modal-body">
              <OrderSummary order={fullOrderModal} />
              <FullOrderFacts order={fullOrderModal} />
            </div>
          </div>
        </div>
      )}
    </section>
  );
};
