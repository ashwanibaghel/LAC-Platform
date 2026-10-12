import { chromium } from "playwright";
import assert from "node:assert/strict";
import test from "node:test";

const BASE_URL = process.env.VITE_TEST_URL || "http://localhost:5173";

test("Land Records UI — Document Conflict Experience [Mocked UI Contract]", async (t) => {
  const browser = await chromium.launch({
    headless: true,
  });
  const context = await browser.newContext({
    viewport: { width: 1366, height: 768 },
  });
  const page = await context.newPage();

  // Synthetic user with Award editing & upload permissions
  let currentUser = {
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
      return route.fulfill({ status: 200, json: currentUser });
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

    return route.continue();
  });

  const resetToWorkspace = async () => {
    await page.goto(`${BASE_URL}/villages/vil-south-01?section=core&resetCache=1`, {
      waitUntil: "networkidle",
    });
    await page.waitForSelector(".village-core-workspace", { timeout: 10000 });
  };

  try {
    await t.test(
      "[Mocked UI Contract] Scenario 1: Reassignment disabled without backend and no false 'Audit Logged'",
      async () => {
        await resetToWorkspace();

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

        await page.click("button:has-text('Upload Core Documents')");
        await page.waitForSelector(".intake-modal", { timeout: 5000 });

        const fileInput = await page.$('.intake-modal input[type="file"]');
        assert.ok(fileInput, "File input must be present");
        await fileInput.setInputFiles({
          name: "Conflicting_Doc_01.pdf",
          mimeType: "application/pdf",
          buffer: Buffer.from("%PDF-1.4 synthetic test pdf"),
        });

        await page.click(".intake-modal button:has-text('Analyse')");
        await page.waitForSelector(".doc-conflict-card-banner", { timeout: 5000 });

        const bannerText = await page.textContent(".doc-conflict-card-banner");
        assert.ok(
          bannerText?.includes("This PDF is already attached to Award 01/2007-08, Village X."),
          `Expected clear conflict explanation, got: ${bannerText}`
        );

        // Open Document Conflict Modal
        await page.click(".doc-conflict-card-actions button:has-text('Resolve Conflict & Options')");
        await page.waitForSelector(".doc-conflict-modal", { timeout: 5000 });

        // Open Reassign panel
        await page.click(".doc-conflict-modal button:has-text('Request Correct Link / Reassign')");
        await page.waitForSelector(".doc-conflict-reassign-panel", { timeout: 3000 });

        // Must display notice that reassignment requires administrative correction and is not yet available
        const noticeText = await page.textContent(".doc-conflict-unavailable-notice");
        assert.ok(
          noticeText?.includes("Reassignment requires administrative correction — not yet available"),
          `Expected administrative correction notice, got: ${noticeText}`
        );

        // Submit button must be disabled
        const submitBtn = await page.$(".doc-conflict-reassign-panel button:has-text('Submit Reassignment')");
        assert.ok(submitBtn, "Submit button should exist");
        const isDisabled = await submitBtn.isDisabled();
        assert.equal(isDisabled, true, "Submit Reassignment must be strictly disabled");

        // Reason textarea must be disabled
        const reasonInput = await page.$(".doc-conflict-reason-input");
        const isReasonDisabled = await reasonInput?.isDisabled();
        assert.equal(isReasonDisabled, true, "Reason textarea must be disabled");

        // NO false 'Audit Logged · Reason Recorded' claim
        const modalContent = await page.textContent(".doc-conflict-modal");
        assert.ok(!modalContent?.includes("Audit Logged"), "UI must NOT falsely display 'Audit Logged'");
        assert.ok(!modalContent?.includes("Reason Recorded"), "UI must NOT falsely display 'Reason Recorded'");

        // Test View Existing Award highlighting
        await page.click(".doc-conflict-modal button:has-text('View Existing Award')");
        await page.waitForSelector("#award-award-01.village-award-card-highlighted", { timeout: 5000 });
        const isHighlighted = await page.isVisible("#award-award-01.village-award-card-highlighted");
        assert.ok(isHighlighted, "Award 01/2007-08 card must be highlighted");
      }
    );

    await t.test(
      "[Mocked UI Contract] Scenario 2: Unknown conflicting Award identity shows 'Existing Award could not be identified' and disabled View button",
      async () => {
        await resetToWorkspace();

        nextBatchResponse = {
          status: 409,
          contentType: "application/problem+json",
          body: JSON.stringify({
            type: "https://tools.ietf.org/html/rfc9110#section-15.5.10",
            title: "Document Ownership Conflict",
            status: 409,
            detail: "This PDF is already linked to another Award.",
          }),
        };

        await page.click("button:has-text('Upload Core Documents')");
        await page.waitForSelector(".intake-modal", { timeout: 5000 });

        const fileInput = await page.$('.intake-modal input[type="file"]');
        await fileInput.setInputFiles({
          name: "Unknown_Owner_Doc.pdf",
          mimeType: "application/pdf",
          buffer: Buffer.from("%PDF-1.4 synthetic unknown"),
        });

        await page.click(".intake-modal button:has-text('Analyse')");
        await page.waitForSelector(".doc-conflict-card-banner", { timeout: 5000 });

        const bannerText = await page.textContent(".doc-conflict-card-banner");
        assert.ok(
          bannerText?.includes("Existing Award could not be identified"),
          `Expected 'Existing Award could not be identified', got: ${bannerText}`
        );

        // Open Document Conflict Modal
        await page.click(".doc-conflict-card-actions button:has-text('Resolve Conflict & Options')");
        await page.waitForSelector(".doc-conflict-modal", { timeout: 5000 });

        // Conflict details must state identity could not be identified without guessing
        const detailsText = await page.textContent(".doc-conflict-details");
        assert.ok(
          detailsText?.includes("Existing Award could not be identified"),
          "Ownership cell must state identity could not be identified"
        );
        assert.ok(!detailsText?.includes("01/2007-08"), "Must not invent 01/2007-08 as fallback");

        // View Existing Award button must be disabled with ID unavailable note
        const viewBtn = await page.$(".doc-conflict-modal button:has-text('View Existing Award')");
        assert.ok(viewBtn, "View Existing Award button must exist");
        const isViewDisabled = await viewBtn.isDisabled();
        assert.equal(isViewDisabled, true, "View Existing Award must be disabled when ID is unknown");
        const viewBtnText = await viewBtn.textContent();
        assert.ok(viewBtnText?.includes("ID unavailable"), "Button must indicate ID unavailable");

        // Cancel Upload in conflict modal dismisses cleanly
        await page.click(".doc-conflict-modal button:has-text('Cancel Upload')");
        await page.waitForSelector(".doc-conflict-modal", { state: "detached", timeout: 3000 });
        await page.click(".intake-modal .village-modal-close");
        await page.waitForSelector(".modal-backdrop", { state: "detached", timeout: 3000 });
      }
    );

    await t.test(
      "[Mocked UI Contract] Scenario 3: Same filename with different PDF hashes is not falsely flagged as duplicate",
      async () => {
        await resetToWorkspace();

        // Filename matches an existing document, but server does NOT mark it as duplicate
        nextBatchResponse = {
          status: 200,
          json: {
            batchId: "batch-diff-hash",
            items: [
              {
                fileName: "Existing_Award_01.pdf",
                intake: {
                  intakeId: "intake-new-file",
                  fileName: "Existing_Award_01.pdf",
                  detectedRole: "Award",
                  detectedAwardNumber: "42/2010-11",
                  matchedAwardId: "award-42",
                  matchedAwardNumber: "42/2010-11",
                  confidence: 0.9,
                  matchState: "MatchedExistingAward",
                  status: "PendingReview",
                  isDuplicate: false,
                },
              },
            ],
          },
        };

        await page.click("button:has-text('Upload Core Documents')");
        await page.waitForSelector(".intake-modal", { timeout: 5000 });

        const fileInput = await page.$('.intake-modal input[type="file"]');
        await fileInput.setInputFiles({
          name: "Existing_Award_01.pdf",
          mimeType: "application/pdf",
          buffer: Buffer.from("%PDF-1.4 different content different hash"),
        });

        await page.click(".intake-modal button:has-text('Analyse')");
        await page.waitForSelector(".intake-review-list", { timeout: 5000 });

        // Must NOT show 'Already uploaded' card
        const isAlreadyUploaded = await page.isVisible(".doc-already-uploaded-card");
        assert.equal(
          isAlreadyUploaded,
          false,
          "Must NOT flag as duplicate based merely on identical filename"
        );

        // Must show normal review and confirmation UI
        const confirmBtn = await page.$("button:has-text('Confirm & Link')");
        assert.ok(confirmBtn, "Must show standard officer confirmation action");

        await page.click(".intake-modal .village-modal-close");
        await page.waitForSelector(".modal-backdrop", { state: "detached", timeout: 3000 });
      }
    );

    await t.test(
      "[Mocked UI Contract] Scenario 4: Same-Award duplicate with verified server ownership (isDuplicate: true)",
      async () => {
        await resetToWorkspace();

        nextBatchResponse = {
          status: 200,
          json: {
            batchId: "batch-verified-dup",
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

        await page.click("button:has-text('Upload Core Documents')");
        await page.waitForSelector(".intake-modal", { timeout: 5000 });

        const fileInput = await page.$('.intake-modal input[type="file"]');
        await fileInput.setInputFiles({
          name: "Existing_Award_01.pdf",
          mimeType: "application/pdf",
          buffer: Buffer.from("%PDF-1.4 duplicate"),
        });

        await page.click(".intake-modal button:has-text('Analyse')");
        await page.waitForSelector(".doc-already-uploaded-card", { timeout: 5000 });

        const cardText = await page.textContent(".doc-already-uploaded-card");
        assert.ok(cardText?.includes("Already uploaded"), "Must show 'Already uploaded' badge");
        assert.ok(cardText?.includes("Award 01/2007-08"), "Must specify Award 01/2007-08");

        const openDocBtn = await page.$(".doc-already-uploaded-card button:has-text('Open Existing Document')");
        assert.ok(openDocBtn, "Must show 'Open Existing Document' action");

        await page.click(".intake-modal .village-modal-close");
        await page.waitForSelector(".modal-backdrop", { state: "detached", timeout: 3000 });
      }
    );

    await t.test(
      "[Mocked UI Contract] Scenario 5: 409 non-ownership conflicts (RoleConflict, AlreadyConfirmedDifferentDecision, StagedOrConcurrentIntakeConflict)",
      async () => {
        // 5a. RoleConflict
        await resetToWorkspace();
        nextBatchResponse = {
          status: 409,
          contentType: "application/problem+json",
          body: JSON.stringify({
            type: "https://tools.ietf.org/html/rfc9110#section-15.5.10",
            title: "Document Role Conflict",
            status: 409,
            conflictKind: "RoleConflict",
            conflictingAwardNumber: "42/2010-11",
            conflictingRole: "Award",
            detail: "A document has already fulfilled the Award role for Award 42/2010-11.",
          }),
        };

        await page.click("button:has-text('Upload Core Documents')");
        await page.waitForSelector(".intake-modal", { timeout: 5000 });

        let fileInput = await page.$('.intake-modal input[type="file"]');
        await fileInput.setInputFiles({
          name: "Duplicate_Role_Doc.pdf",
          mimeType: "application/pdf",
          buffer: Buffer.from("%PDF-1.4 test role conflict"),
        });

        await page.click(".intake-modal button:has-text('Analyse')");
        await page.waitForSelector(".intake-badge-conflict", { timeout: 5000 });

        const roleBadge = await page.textContent(".intake-badge-conflict");
        assert.equal(roleBadge?.trim(), "Role Conflict", "Badge must show 'Role Conflict'");

        // Open modal and check title
        await page.click(".doc-conflict-card-actions button:has-text('Resolve Conflict & Options')");
        await page.waitForSelector(".doc-conflict-modal", { timeout: 5000 });
        const modalTitle = await page.textContent(".doc-conflict-modal .village-modal-title");
        assert.equal(modalTitle?.trim(), "Document Role Conflict");
        await page.click(".doc-conflict-modal .village-modal-close");
        await page.waitForSelector(".doc-conflict-modal", { state: "detached", timeout: 3000 });
        await page.click(".intake-modal .village-modal-close");
        await page.waitForSelector(".modal-backdrop", { state: "detached", timeout: 3000 });

        // 5b. AlreadyConfirmedDifferentDecision
        await resetToWorkspace();
        nextBatchResponse = {
          status: 409,
          contentType: "application/problem+json",
          body: JSON.stringify({
            status: 409,
            conflictKind: "AlreadyConfirmedDifferentDecision",
            detail: "This document was already confirmed under a different role or decision.",
          }),
        };

        await page.click("button:has-text('Upload Core Documents')");
        await page.waitForSelector(".intake-modal", { timeout: 5000 });
        fileInput = await page.$('.intake-modal input[type="file"]');
        await fileInput.setInputFiles({
          name: "State_Conflict_Doc.pdf",
          mimeType: "application/pdf",
          buffer: Buffer.from("%PDF-1.4 state conflict"),
        });
        await page.click(".intake-modal button:has-text('Analyse')");
        await page.waitForSelector(".intake-badge-conflict", { timeout: 5000 });
        const stateBadge = await page.textContent(".intake-badge-conflict");
        assert.equal(stateBadge?.trim(), "State Conflict", "Badge must show 'State Conflict'");

        await page.click(".doc-conflict-card-actions button:has-text('Resolve Conflict & Options')");
        await page.waitForSelector(".doc-conflict-modal", { timeout: 5000 });
        const stateTitle = await page.textContent(".doc-conflict-modal .village-modal-title");
        assert.equal(stateTitle?.trim(), "Document State Conflict");
        await page.click(".doc-conflict-modal .village-modal-close");
        await page.waitForSelector(".doc-conflict-modal", { state: "detached", timeout: 3000 });
        await page.click(".intake-modal .village-modal-close");
        await page.waitForSelector(".modal-backdrop", { state: "detached", timeout: 3000 });

        // 5c. StagedOrConcurrentIntakeConflict
        await resetToWorkspace();
        nextBatchResponse = {
          status: 409,
          contentType: "application/problem+json",
          body: JSON.stringify({
            status: 409,
            conflictKind: "StagedOrConcurrentIntakeConflict",
            detail: "A concurrent upload or staged intake is already in progress.",
          }),
        };

        await page.click("button:has-text('Upload Core Documents')");
        await page.waitForSelector(".intake-modal", { timeout: 5000 });
        fileInput = await page.$('.intake-modal input[type="file"]');
        await fileInput.setInputFiles({
          name: "Concurrent_Doc.pdf",
          mimeType: "application/pdf",
          buffer: Buffer.from("%PDF-1.4 concurrent conflict"),
        });
        await page.click(".intake-modal button:has-text('Analyse')");
        await page.waitForSelector(".doc-conflict-card-banner", { timeout: 5000 });

        await page.click(".doc-conflict-card-actions button:has-text('Resolve Conflict & Options')");
        await page.waitForSelector(".doc-conflict-modal", { timeout: 5000 });
        const concTitle = await page.textContent(".doc-conflict-modal .village-modal-title");
        assert.equal(concTitle?.trim(), "Concurrent Intake Conflict");
        await page.click(".doc-conflict-modal .village-modal-close");
        await page.waitForSelector(".doc-conflict-modal", { state: "detached", timeout: 3000 });
        await page.click(".intake-modal .village-modal-close");
        await page.waitForSelector(".modal-backdrop", { state: "detached", timeout: 3000 });
      }
    );

    await t.test(
      "[Mocked UI Contract] Scenario 6: Unauthorized correction attempt (Award.CoreDocument.Upload without Award.Edit)",
      async () => {
        // Change user permissions to lack Award.Edit
        currentUser = {
          id: "00000000-0000-0000-0000-000000000002",
          username: "uploader_only",
          displayName: "Upload Clerk",
          roles: ["Clerk"],
          permissions: [
            { code: "Award.View", scope: "All" },
            { code: "Award.CoreDocument.Upload", scope: "All" },
          ],
          workstreams: [],
          desks: [],
        };

        await resetToWorkspace();

        nextBatchResponse = {
          status: 409,
          contentType: "application/problem+json",
          body: JSON.stringify({
            status: 409,
            detail: "This PDF is already attached to Award 01/2007-08, Village X.",
          }),
        };

        await page.click("button:has-text('Upload Core Documents')");
        await page.waitForSelector(".intake-modal", { timeout: 5000 });

        const fileInput = await page.$('.intake-modal input[type="file"]');
        await fileInput.setInputFiles({
          name: "Unauthorized_Reassign.pdf",
          mimeType: "application/pdf",
          buffer: Buffer.from("%PDF-1.4 unauth"),
        });

        await page.click(".intake-modal button:has-text('Analyse')");
        await page.waitForSelector(".doc-conflict-card-banner", { timeout: 5000 });

        await page.click(".doc-conflict-card-actions button:has-text('Resolve Conflict & Options')");
        await page.waitForSelector(".doc-conflict-modal", { timeout: 5000 });

        // Open Reassign panel
        await page.click(".doc-conflict-modal button:has-text('Request Correct Link / Reassign')");
        await page.waitForSelector(".doc-conflict-perm-error", { timeout: 3000 });

        const permError = await page.textContent(".doc-conflict-perm-error");
        assert.ok(
          permError?.includes("Permission required: Award.Edit"),
          `Expected 'Permission required: Award.Edit', got: ${permError}`
        );

        // Reassignment reason input should NOT be present
        const hasReasonInput = await page.isVisible(".doc-conflict-reason-input");
        assert.equal(hasReasonInput, false, "Reason input must not be shown to unauthorized users");

        await page.click(".doc-conflict-modal .village-modal-close");
        await page.waitForSelector(".doc-conflict-modal", { state: "detached", timeout: 3000 });
        await page.click(".intake-modal .village-modal-close");
        await page.waitForSelector(".modal-backdrop", { state: "detached", timeout: 3000 });
      }
    );

    await t.test(
      "[Mocked UI Contract] Scenario 7: View Existing Award outside visible village cards navigates to award route",
      async () => {
        // Reset permissions back to full officer
        currentUser = {
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

        await resetToWorkspace();

        nextBatchResponse = {
          status: 409,
          contentType: "application/problem+json",
          body: JSON.stringify({
            status: 409,
            conflictingAwardId: "award-outside-99",
            conflictingAwardNumber: "99/2015-16",
            detail: "This PDF is already attached to Award 99/2015-16, Village Other.",
          }),
        };

        await page.click("button:has-text('Upload Core Documents')");
        await page.waitForSelector(".intake-modal", { timeout: 5000 });

        const fileInput = await page.$('.intake-modal input[type="file"]');
        await fileInput.setInputFiles({
          name: "External_Award_Doc.pdf",
          mimeType: "application/pdf",
          buffer: Buffer.from("%PDF-1.4 external"),
        });

        await page.click(".intake-modal button:has-text('Analyse')");
        await page.waitForSelector(".doc-conflict-card-banner", { timeout: 5000 });

        await page.click(".doc-conflict-card-actions button:has-text('Resolve Conflict & Options')");
        await page.waitForSelector(".doc-conflict-modal", { timeout: 5000 });

        // Click View Existing Award (for award-outside-99 which is not rendered in village records)
        await page.click(".doc-conflict-modal button:has-text('View Existing Award')");

        // Should navigate to /awards/award-outside-99
        await page.waitForURL("**/awards/award-outside-99", { timeout: 5000 });
        const currentUrl = page.url();
        assert.ok(
          currentUrl.includes("/awards/award-outside-99"),
          `Expected navigation to /awards/award-outside-99, got: ${currentUrl}`
        );
      }
    );
  } finally {
    await browser.close();
  }
});
