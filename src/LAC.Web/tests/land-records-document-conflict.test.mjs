import { chromium } from "playwright";
import assert from "node:assert/strict";
import test from "node:test";

const BASE_URL = process.env.VITE_TEST_URL || "http://localhost:5173";

test("Land Records UI — Document Conflict Experience Acceptance Test", async (t) => {
  const browser = await chromium.launch({
    headless: true,
  });
  const context = await browser.newContext({
    viewport: { width: 1366, height: 768 },
  });
  const page = await context.newPage();

  // Synthetic user with Award editing & upload permissions
  const testUser = {
    id: "00000000-0000-0000-0000-000000000001",
    username: "revenue_officer",
    displayName: "Revenue Officer",
    roles: ["Officer"],
    permissions: [
      { code: "Award.View", scope: "All" },
      { code: "Award.Edit", scope: "All" },
      { code: "Award.CoreDocument.Upload", scope: "All" },
    ],
    workstreams: [],
    desks: [],
  };

  const villageData = {
    id: "vil-south-01",
    name: "Village X",
    subDivision: {
      id: "sub-1",
      name: "Mehrauli",
      district: { id: "dist-1", name: "South" },
    },
    totalKhasras: 261,
    linkedAwards: 2,
  };

  // Award 42 is first so it defaults as selected in dropdown; Award 01 is second
  const coreRecordsData = [
    {
      id: "award-42",
      awardNumber: "42/2010-11",
      awardDate: "2010-11-20",
      awardType: "Regular",
      roles: [
        { role: "Award", count: 0, available: false, documents: [] },
        { role: "NM", count: 0, available: false, documents: [] },
        { role: "StatementA", count: 0, available: false, documents: [] },
        { role: "PossessionProceeding", count: 0, available: false, documents: [] },
      ],
    },
    {
      id: "award-01",
      awardNumber: "01/2007-08",
      awardDate: "2007-08-15",
      awardType: "Regular",
      roles: [
        {
          role: "Award",
          count: 1,
          available: true,
          documents: [
            {
              documentId: "doc-existing-01",
              role: "Award",
              coreDocumentRole: "Award",
              originalFileName: "Existing_Award_01.pdf",
              uploadedAt: "2026-09-01T10:00:00Z",
              status: "Active",
              viewRoute: "/api/documents/doc-existing-01/content",
            },
          ],
        },
        { role: "NM", count: 0, available: false, documents: [] },
        { role: "StatementA", count: 0, available: false, documents: [] },
        { role: "PossessionProceeding", count: 0, available: false, documents: [] },
      ],
    },
  ];

  let nextBatchResponse = null;

  await page.route("**/api/**", async (route) => {
    const url = new URL(route.request().url());
    const pathname = url.pathname;

    if (pathname === "/api/auth/me") {
      return route.fulfill({ status: 200, json: testUser });
    }

    if (pathname === "/api/villages/vil-south-01") {
      return route.fulfill({ status: 200, json: villageData });
    }

    if (pathname.includes("/api/villages/vil-south-01/core-records")) {
      return route.fulfill({ status: 200, json: coreRecordsData });
    }

    if (pathname.endsWith("/core-document-intake")) {
      if (nextBatchResponse) {
        const resp = nextBatchResponse;
        nextBatchResponse = null;
        return route.fulfill(resp);
      }
      return route.fulfill({
        status: 200,
        json: {
          batchId: "batch-1",
          items: [
            {
              fileName: "test_upload.pdf",
              intake: {
                intakeId: "intake-test-1",
                fileName: "test_upload.pdf",
                detectedRole: "Award",
                detectedAwardNumber: "42/2010-11",
                confidence: 0.88,
                matchState: "MatchedExistingAward",
                matchedAwardId: "award-42",
                matchedAwardNumber: "42/2010-11",
                status: "PendingReview",
              },
            },
          ],
        },
      });
    }

    if (pathname.includes("/core-document-intakes/") && pathname.endsWith("/confirm")) {
      return route.fulfill({
        status: 409,
        contentType: "application/problem+json",
        body: JSON.stringify({
          type: "https://tools.ietf.org/html/rfc9110#section-15.5.10",
          title: "Document Ownership Conflict",
          status: 409,
          detail: "This PDF is already attached to Award 01/2007-08, Village X.",
        }),
      });
    }

    return route.continue();
  });

  try {
    await t.test("Navigates to Village Core Records workspace", async () => {
      await page.goto(`${BASE_URL}/villages/vil-south-01?section=core&resetCache=1`, {
        waitUntil: "networkidle",
      });
      await page.waitForSelector(".village-core-workspace", { timeout: 10000 });
      const title = await page.textContent(".village-core-title");
      assert.ok(title?.includes("Award Core Records"), `Expected Award Core Records title, got: ${title}`);
      const awards = await page.$$(".village-award-card");
      assert.equal(awards.length, 2, "Should show 2 awards");
    });

    await t.test("Scenario 1: Upload batch detects 409 document ownership conflict", async () => {
      // Mock batch upload returning RFC 9110 / 409 ProblemDetails
      nextBatchResponse = {
        status: 409,
        contentType: "application/problem+json",
        body: JSON.stringify({
          type: "https://tools.ietf.org/html/rfc9110#section-15.5.10",
          title: "Document Ownership Conflict",
          status: 409,
          detail: "This PDF is already attached to Award 01/2007-08, Village X.",
        }),
      };

      // Click "+ Upload Core Documents"
      await page.click("button:has-text('Upload Core Documents')");
      await page.waitForSelector(".modal-card", { timeout: 5000 });

      // Upload file that collides with Award 01/2007-08
      const fileInput = await page.$('.modal-card input[type="file"]');
      assert.ok(fileInput, "File input must be present");
      await fileInput.setInputFiles({
        name: "Conflicting_Document.pdf",
        mimeType: "application/pdf",
        buffer: Buffer.from("%PDF-1.4 synthetic test pdf"),
      });

      // Click Analyse button
      await page.click(".modal-card button:has-text('Analyse')");

      // Verify the review card displays the clear conflict message
      await page.waitForSelector(".doc-conflict-card-banner", { timeout: 5000 });
      const bannerText = await page.textContent(".doc-conflict-card-banner");
      assert.ok(
        bannerText?.includes("This PDF is already attached to Award 01/2007-08, Village X."),
        `Expected clear explanation in banner, got: ${bannerText}`
      );

      // Verify raw JSON 409 is NEVER exposed in the UI
      const bodyText = await page.textContent(".modal-card");
      assert.ok(!bodyText?.includes("rfc9110"), "Raw RFC 9110 URL must not leak in UI");
      assert.ok(!bodyText?.includes('"status": 409'), "Raw JSON status must not leak in UI");
      assert.ok(!bodyText?.includes('"type":'), "Raw JSON fields must not leak in UI");

      // Verify "Low confidence" is NOT displayed as the explanation for ownership conflict
      assert.ok(
        !bodyText?.includes("Low confidence"),
        "Ownership conflict must never display 'Low confidence'"
      );

      // Verify status badge shows Ownership Conflict
      const badge = await page.textContent(".intake-badge-conflict");
      assert.equal(badge?.trim(), "Ownership Conflict", "Badge must state Ownership Conflict");

      // Click "Resolve Conflict & Options" to open DocumentConflictModal
      await page.click("button:has-text('Resolve Conflict & Options')");
      await page.waitForSelector(".doc-conflict-modal", { timeout: 5000 });

      // Verify Document Conflict Modal header and explanation
      const modalHeader = await page.textContent(".doc-conflict-modal .village-modal-title");
      assert.equal(modalHeader?.trim(), "Document Ownership Conflict");

      const conflictBanner = await page.textContent(".doc-conflict-banner-title");
      assert.ok(
        conflictBanner?.includes("This PDF is already attached to Award 01/2007-08, Village X."),
        "Modal must state conflicting award and village"
      );

      // Verify 4 Authorized Actions are available
      const btnViewExisting = await page.$("button:has-text('View Existing Award')");
      const btnCancelUpload = await page.$("button:has-text('Cancel Upload')");
      const btnReassign = await page.$("button:has-text('Request Correct Link / Reassign')");
      const btnMultiAward = await page.$("button:has-text('Link to Multiple Awards')");

      assert.ok(btnViewExisting, "View Existing Award action must be present");
      assert.ok(btnCancelUpload, "Cancel Upload action must be present");
      assert.ok(btnReassign, "Request Correct Link / Reassign action must be present");
      assert.ok(btnMultiAward, "Link to Multiple Awards action must be present");

      // Check Multi-Award policy note
      await btnMultiAward.click();
      await page.waitForSelector(".doc-conflict-policy-note", { timeout: 3000 });
      const policyNote = await page.textContent(".doc-conflict-policy-note");
      assert.ok(
        policyNote?.includes("strictly enforce single-Award exclusivity"),
        "Must explain backend policy restriction on single-award exclusivity"
      );

      // Check Reassignment Request with justification requirement and audit logging
      await btnReassign.click();
      await page.waitForSelector(".doc-conflict-reassign-panel", { timeout: 3000 });

      const submitBtn = await page.$("button:has-text('Submit Reassignment Request')");
      const isDisabledInitially = await submitBtn?.isDisabled();
      assert.ok(isDisabledInitially, "Submit Reassignment must be disabled without reason entry");

      // Fill in statutory justification reason
      await page.fill(
        ".doc-conflict-reason-input",
        "Assigned erroneously during batch scanning. Revenue record verified for Award 42/2010-11."
      );
      const isEnabledAfterText = !(await submitBtn?.isDisabled());
      assert.ok(isEnabledAfterText, "Submit Reassignment must be enabled after entering reason");

      // Submit reassignment request
      await submitBtn?.click();
      await page.waitForSelector(".doc-conflict-audit-badge", { timeout: 3000 });
      const auditText = await page.textContent(".doc-conflict-reassign-success");
      assert.ok(
        auditText?.includes("Audit Logged · Reason Recorded"),
        "Must display audit trail confirmation badge"
      );
      assert.ok(
        auditText?.includes("No client-side bypass was permitted"),
        "Must explicitly safeguard against client-side bypass"
      );

      // Test "View Existing Award" action
      await page.click("button:has-text('View Existing Award')");

      // Conflict modal closes and Award 01/2007-08 card is highlighted in the village workspace
      await page.waitForSelector("#award-award-01.village-award-card-highlighted", {
        timeout: 5000,
      });
      const isHighlighted = await page.isVisible("#award-award-01.village-award-card-highlighted");
      assert.ok(isHighlighted, "Award 01/2007-08 should be highlighted on View Existing Award");
    });

    await t.test("Scenario 2: Same-award upload shows 'Already uploaded' with Open button", async () => {
      // Open Smart Core Upload Modal
      await page.click("button:has-text('Upload Core Documents')");
      await page.waitForSelector(".modal-card", { timeout: 5000 });

      // Upload file with name identical to existing document in Award 01/2007-08
      const fileInput = await page.$('.modal-card input[type="file"]');
      await fileInput.setInputFiles({
        name: "Existing_Award_01.pdf",
        mimeType: "application/pdf",
        buffer: Buffer.from("%PDF-1.4 test duplicate"),
      });

      nextBatchResponse = {
        status: 200,
        json: {
          batchId: "batch-duplicate-check",
          items: [
            {
              fileName: "Existing_Award_01.pdf",
              intake: {
                intakeId: "intake-dup-01",
                fileName: "Existing_Award_01.pdf",
                detectedRole: "Award",
                detectedAwardNumber: "01/2007-08",
                matchedAwardId: "award-01",
                matchedAwardNumber: "01/2007-08",
                confidence: 0.95,
                matchState: "MatchedExistingAward",
                status: "Confirmed",
                confirmedAwardId: "award-01",
                confirmedRole: "Award",
                isDuplicate: true,
                viewRoute: "/api/documents/doc-existing-01/content",
              },
            },
          ],
        },
      };

      await page.click(".modal-card button:has-text('Analyse')");

      // Verify "Already uploaded" card is rendered
      await page.waitForSelector(".doc-already-uploaded-card", { timeout: 5000 });
      const cardText = await page.textContent(".doc-already-uploaded-card");
      assert.ok(cardText?.includes("Already uploaded"), "Must show 'Already uploaded' badge");
      assert.ok(
        cardText?.includes("Award 01/2007-08"),
        "Must specify that it is already attached to Award 01/2007-08"
      );

      const openDocBtn = await page.$("button:has-text('Open Existing Document')");
      assert.ok(openDocBtn, "Must show 'Open Existing Document' action");

      // Close modal
      await page.click(".village-modal-close");
      await page.waitForSelector(".modal-backdrop", { state: "detached", timeout: 5000 });
    });

    await t.test("Scenario 3: Quick Attach handles conflict and Cancel Upload cleanly", async () => {
      // Find quick attach button on Award 42/2010-11 for Award slot
      const award42AttachBtn = await page.$(
        "#award-award-42 .village-doc-slot:has-text('Award PDF') .village-doc-attach-btn"
      );
      assert.ok(award42AttachBtn, "Quick attach button for Award 42 must be available");
      await award42AttachBtn.click();

      await page.waitForSelector(".modal-card:has-text('Attach Award PDF')", { timeout: 5000 });

      // Select file that triggers 409 conflict
      nextBatchResponse = {
        status: 409,
        contentType: "application/problem+json",
        body: JSON.stringify({
          type: "https://tools.ietf.org/html/rfc9110#section-15.5.10",
          title: "Document Ownership Conflict",
          status: 409,
          detail: "This PDF is already attached to Award 01/2007-08, Village X.",
        }),
      };

      const fileInput = await page.$('.modal-card input[type="file"]');
      await fileInput.setInputFiles({
        name: "Conflicting_Quick_Doc.pdf",
        mimeType: "application/pdf",
        buffer: Buffer.from("%PDF-1.4 quick conflict"),
      });

      await page.click(".modal-card button:has-text('Analyse & Attach')");

      // Verify Document Conflict Modal opens directly
      await page.waitForSelector(".doc-conflict-modal", { timeout: 5000 });
      const explanation = await page.textContent(".doc-conflict-banner-title");
      assert.ok(
        explanation?.includes("This PDF is already attached to Award 01/2007-08, Village X."),
        "Quick attach conflict must explain existing award ownership"
      );

      // Click "Cancel Upload"
      await page.click("button:has-text('Cancel Upload')");

      // Verify modal is dismissed cleanly without residual state
      await page.waitForSelector(".doc-conflict-modal", { state: "detached", timeout: 3000 });
      const modalExists = await page.isVisible(".doc-conflict-modal");
      assert.equal(modalExists, false, "Modal must be dismissed after Cancel Upload");
    });
  } finally {
    await browser.close();
  }
});
