import type { DhcSyncRunDto } from "./types";

type DailyOutcome = { title: string; lines: string[]; tone: "success" | "attention" | "neutral" | "error" };

export const dailyCauseListOutcome = (
  run: DhcSyncRunDto | null,
  watchedCount: number | null,
  syncInProgress: boolean,
): DailyOutcome => {
  if (syncInProgress || run?.status === "Running")
    return { title: "Checking cause lists…", lines: ["Checking Delhi High Court publications…"], tone: "neutral" };
  if (run?.status === "Failed")
    return { title: "Cause-list check was interrupted", lines: ["The next automatic cycle will try again."], tone: "error" };
  if (run?.status !== "Completed")
    return { title: "Automatic cause-list checks are ready", lines: ["Updates will appear here after a completed check."], tone: "neutral" };

  const noChanges = run.observationsCreated === 0 && run.reviewCount === 0;
  const lines = [
    ...(watchedCount === null ? [] : [`${watchedCount} Delhi High Court cases are being monitored.`]),
    `${run.sourceDocumentsDiscovered} current cause-list publications found.`,
    run.sourceDocumentsProcessed === 0
      ? "No publication needed reprocessing."
      : `${run.sourceDocumentsProcessed} publications checked in this cycle.`,
  ];
  if (run.observationsCreated > 0) {
    lines.push(`${run.observationsCreated} new listing ${run.observationsCreated === 1 ? "entry" : "entries"} found.`);
    lines.push(`${run.observationsAccepted} official listing ${run.observationsAccepted === 1 ? "entry" : "entries"} confirmed.`);
  }
  if (run.reviewCount > 0)
    lines.push(`${run.reviewCount} new issue${run.reviewCount === 1 ? "" : "s"} from this check ${run.reviewCount === 1 ? "needs" : "need"} attention.`);

  return {
    title: noChanges ? "✓ No new DHC updates found" : run.observationsCreated > 0
      ? "New official cause-list evidence found" : "Cause-list check complete",
    lines,
    tone: run.reviewCount > 0 ? "attention" : "success",
  };
};
