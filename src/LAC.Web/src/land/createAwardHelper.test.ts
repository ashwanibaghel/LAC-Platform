import { createAwardWithOptionalPdf } from "./createAwardHelper";

// Simple test suite executed via node/tsx/tsc
async function runTests() {
  let passed = 0;
  let failed = 0;

  function assert(condition: boolean, name: string) {
    if (condition) {
      passed++;
      console.log(`✓ PASS: ${name}`);
    } else {
      failed++;
      console.error(`✗ FAIL: ${name}`);
    }
  }

  // Case 1: Award creation fails (500)
  {
    const mockFetch = (async (url: string) => {
      if (url.includes("/awards")) {
        return {
          ok: false,
          json: async () => ({ detail: "Award number already exists" }),
        } as any;
      }
      return { ok: true } as any;
    }) as typeof fetch;

    const res = await createAwardWithOptionalPdf({
      api: "/api",
      villageId: "v-1",
      awardData: { awardNumber: "A1" },
      initialFile: null,
      fetchFn: mockFetch,
    });

    assert(res.awardCreated === false, "Award creation failure returns awardCreated: false");
    assert(res.pdfUploaded === false, "Award creation failure returns pdfUploaded: false");
    assert(res.error === "Award number already exists", "Award creation failure returns correct error message");
  }

  // Case 2: Award creation succeeds, no initial file
  {
    const mockFetch = (async (url: string) => {
      if (url.endsWith("/awards")) {
        return {
          ok: true,
          json: async () => ({ id: "award-123" }),
        } as any;
      }
      return { ok: true } as any;
    }) as typeof fetch;

    const res = await createAwardWithOptionalPdf({
      api: "/api",
      villageId: "v-1",
      awardData: { awardNumber: "A1" },
      initialFile: null,
      fetchFn: mockFetch,
    });

    assert(res.awardCreated === true, "Award creation success returns awardCreated: true");
    assert(res.awardId === "award-123", "Award creation success returns awardId");
    assert(res.pdfUploaded === false, "No initial file returns pdfUploaded: false");
  }

  // Case 3: Award creation succeeds, PDF upload fails (partial success)
  {
    const mockFetch = (async (url: string) => {
      if (url.endsWith("/awards")) {
        return {
          ok: true,
          json: async () => ({ id: "award-123" }),
        } as any;
      }
      if (url.includes("/core-documents")) {
        return {
          ok: false,
          json: async () => ({ detail: "Storage quota exceeded" }),
        } as any;
      }
      return { ok: true } as any;
    }) as typeof fetch;

    const mockFile = new File(["dummy content"], "award.pdf", { type: "application/pdf" });

    const res = await createAwardWithOptionalPdf({
      api: "/api",
      villageId: "v-1",
      awardData: { awardNumber: "A1" },
      initialFile: mockFile,
      fetchFn: mockFetch,
    });

    assert(res.awardCreated === true, "Partial success returns awardCreated: true");
    assert(res.awardId === "award-123", "Partial success retains created awardId");
    assert(res.pdfUploaded === false, "Partial success returns pdfUploaded: false");
    assert(
      res.message !== undefined && res.message.includes("Award created successfully, but the PDF could not be attached"),
      "Partial success returns retry-safe non-destructive notice message"
    );
  }

  // Case 4: Award creation succeeds and PDF upload succeeds
  {
    const mockFetch = (async (url: string) => {
      if (url.endsWith("/awards")) {
        return {
          ok: true,
          json: async () => ({ id: "award-123" }),
        } as any;
      }
      if (url.includes("/core-documents")) {
        return {
          ok: true,
          json: async () => ({ id: "doc-456" }),
        } as any;
      }
      return { ok: true } as any;
    }) as typeof fetch;

    const mockFile = new File(["dummy content"], "award.pdf", { type: "application/pdf" });

    const res = await createAwardWithOptionalPdf({
      api: "/api",
      villageId: "v-1",
      awardData: { awardNumber: "A1" },
      initialFile: mockFile,
      fetchFn: mockFetch,
    });

    assert(res.awardCreated === true, "Full success returns awardCreated: true");
    assert(res.pdfUploaded === true, "Full success returns pdfUploaded: true");
  }

  if (failed > 0) {
    process.exit(1);
  }
}

if (typeof process !== "undefined" && process.env.NODE_ENV !== "production") {
  runTests().catch((e) => {
    console.error(e);
    process.exit(1);
  });
}
