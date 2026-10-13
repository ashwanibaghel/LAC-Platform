export class NativeSynchronization {
  private dirty = false;
  private closed = false;
  private pending: { resolve: (url: string) => void; reject: (error: Error) => void; timer: ReturnType<typeof setTimeout>; url?: string } | null = null;

  private readonly origin: string;
  private readonly quiesce: () => void;
  private readonly requestExport: () => void;
  private readonly timeoutMs: number;
  constructor(origin: string, quiesce: () => void, requestExport: () => void, timeoutMs = 30000) {
    this.origin = origin; this.quiesce = quiesce; this.requestExport = requestExport; this.timeoutMs = timeoutMs;
  }

  // The result is native export evidence, NEVER a Saved state. The backend must
  // validate its envelope and compare canonical text AND formatting with callback
  // bytes before committing. Boolean state alone cannot prove local durability.
  quiesceAndExport(): Promise<string> {
    if (this.closed) return Promise.reject(new Error("Native editor session has ended."));
    if (this.pending) return Promise.reject(new Error("Native synchronization is already pending."));
    this.quiesce();
    return new Promise((resolve, reject) => {
      const timer = setTimeout(() => this.fail(new Error("Native synchronization timed out; no save success is implied.")), this.timeoutMs);
      this.pending = { resolve, reject, timer };
      try { this.requestExport(); } catch (error) { this.fail(error instanceof Error ? error : new Error("Native export failed.")); }
    });
  }

  documentState(dirty: boolean): void {
    if (this.closed) return;
    this.dirty = dirty;
    this.finishIfReady();
  }

  exported(url: string): void {
    if (!this.pending || this.closed) return;
    try {
      const parsed = new URL(url);
      if (parsed.origin !== this.origin || parsed.username || parsed.password || parsed.hash || url.length > 8192)
        throw new Error("Unexpected native export origin.");
      this.pending.url = url;
      this.finishIfReady();
    } catch (error) { this.fail(error instanceof Error ? error : new Error("Invalid native export.")); }
  }

  private finishIfReady(): void {
    const pending = this.pending;
    if (!pending?.url || this.dirty || this.closed) return;
    this.pending = null;
    clearTimeout(pending.timer);
    pending.resolve(pending.url);
  }

  fail(error: Error): void {
    const pending = this.pending; this.pending = null;
    if (pending) { clearTimeout(pending.timer); pending.reject(error); }
  }

  close(): void {
    this.closed = true;
    this.fail(new Error("Native editor session changed; capture is invalid."));
  }
}
