// Persisted item states do not prove how many order searches finished: a
// status-phase review can survive a completed order lookup. Report only the
// durable run state, never an inferred per-item count.
export const orderLinkCheckState = (status: string): string => {
  switch (status) {
    case "WaitingForCaptcha": return "Waiting for verification code";
    case "PausedForCaptcha": return "Verification code needed to continue";
    case "Running": return "Checking official order links…";
    case "Interrupted":
    case "Failed": return "Order-link check interrupted";
    case "Completed": return "Order-link check complete";
    default: return "Order-link check status unavailable";
  }
};
