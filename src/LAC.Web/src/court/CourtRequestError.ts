/** Show safe API validation messages, never developer exception/HTML bodies. */
export const responseError = async (response: Response, fallback: string): Promise<string> => {
  if (response.status === 401) return "Your session has expired. Please sign in again.";
  if (response.status >= 500) return fallback;
  try {
    const problem = await response.json() as { detail?: unknown; error?: unknown };
    const message = problem.detail ?? problem.error;
    return typeof message === "string" && message.trim() ? message : fallback;
  } catch { return fallback; }
};
