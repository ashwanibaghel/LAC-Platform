export const sessionExpiredEvent = "lac:session-expired";

// All existing API clients share the browser fetch boundary. A rotated cookie expires
// the session once; subsequent resource requests never reach the server until sign-in.
export function createSessionFetch(original: typeof fetch, expire: () => void, origin: string) {
  let authenticated = false;
  let expired = false;
  let generation = 0;
  const wrapped: typeof fetch = async (input, init) => {
    const url = new URL(typeof input === "string" ? input : input instanceof URL ? input.href : input.url, origin);
    const resource = url.origin === origin && url.pathname.startsWith("/api/")
      && !["/api/auth/login", "/api/auth/logout"].includes(url.pathname);
    if (resource && expired) return new Response(null, { status: 401 });
    const started = generation;
    const response = await original(input, init);
    // Do not repopulate caches or state with a response from a previous login.
    if (resource && started !== generation) return new Response(null, { status: 401 });
    if (resource && response.status === 401 && authenticated && !expired) {
      expired = true;
      authenticated = false;
      generation++;
      expire();
    }
    return response;
  };
  return {
    fetch: wrapped,
    authenticated() { generation++; authenticated = true; expired = false; },
    loggedOut() { generation++; authenticated = false; expired = false; },
  };
}

let session: ReturnType<typeof createSessionFetch> | undefined;
export function installSessionFetch() {
  if (session) return;
  session = createSessionFetch(window.fetch.bind(window), () => window.dispatchEvent(new Event(sessionExpiredEvent)), window.location.origin);
  window.fetch = session.fetch;
}
export const sessionAuthenticated = () => { session?.authenticated(); window.dispatchEvent(new Event("lac:session-changed")); };
export const sessionLoggedOut = () => { session?.loggedOut(); window.dispatchEvent(new Event("lac:session-changed")); };
