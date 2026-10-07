import { chromium } from "playwright";
import http from "node:http";
import fs from "node:fs";
import path from "node:path";

const PORT = 5199;
const DIST_DIR = path.join(process.cwd(), "dist");

function startStaticServer() {
  return new Promise((resolve) => {
    const server = http.createServer((req, res) => {
      // API requests must NEVER return HTML index.html
      if (req.url.startsWith("/api/")) {
        res.writeHead(404, { "Content-Type": "application/json" });
        res.end(JSON.stringify({ error: `Unhandled API endpoint: ${req.url}` }));
        return;
      }

      let filePath = path.join(DIST_DIR, req.url === "/" ? "index.html" : req.url);
      if (!fs.existsSync(filePath) || fs.statSync(filePath).isDirectory()) {
        filePath = path.join(DIST_DIR, "index.html");
      }
      const ext = path.extname(filePath);
      const mimeTypes = {
        ".html": "text/html",
        ".js": "text/javascript",
        ".css": "text/css",
        ".json": "application/json",
        ".png": "image/png",
        ".jpg": "image/jpeg",
        ".svg": "image/svg+xml"
      };
      const contentType = mimeTypes[ext] || "application/octet-stream";
      res.writeHead(200, { "Content-Type": contentType });
      fs.createReadStream(filePath).pipe(res);
    });
    server.listen(PORT, () => {
      console.log(`Static server running on http://127.0.0.1:${PORT}`);
      resolve(server);
    });
  });
}

async function main() {
  const server = await startStaticServer();

  const browser = await chromium.launch({
    executablePath: "C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe",
    headless: true,
  });

  const context = await browser.newContext({
    viewport: { width: 1366, height: 768 },
    deviceScaleFactor: 1,
  });

  const page = await context.newPage();
  page.on("console", (msg) => {
    if (msg.type() === "error") console.log(`[PAGE LOG ERROR]:`, msg.text());
  });
  page.on("pageerror", (err) => {
    console.error(`[PAGE EXCEPTION]:`, err.message, err.stack);
  });

  // 1. Current Auth User (Ashwani Baghel)
  await page.route("**/api/auth/me", (route) => {
    route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify({
        id: "user-1",
        username: "ashwanibaghel826@gmail.com",
        displayName: "Ashwani Baghel",
        roles: ["Administrator", "IntakeOfficer"],
        permissions: [
          { code: "Dak.View", scope: "Global" },
          { code: "Dak.Register", scope: "Global" },
          { code: "Dak.Edit", scope: "Global" },
          { code: "Dak.Move", scope: "Global" },
          { code: "Dak.Receive", scope: "Global" },
          { code: "Dak.PullBack", scope: "Global" },
          { code: "Dak.Dispose", scope: "Global" },
          { code: "Dak.Cancel", scope: "Global" },
          { code: "Dak.Reopen", scope: "Global" },
          { code: "Court.View", scope: "Global" },
          { code: "Court.Create", scope: "Global" },
          { code: "Matter.View", scope: "Global" },
          { code: "Matter.Create", scope: "Global" },
          { code: "Land.View", scope: "Global" },
          { code: "Award.View", scope: "Global" },
          { code: "WorkItem.View", scope: "Global" },
          { code: "WorkItem.Create", scope: "Global" },
          { code: "Schedule.View", scope: "Global" },
          { code: "Audit.View", scope: "Global" },
          { code: "Users.Manage", scope: "Global" },
          { code: "Access.Manage", scope: "Global" }
        ],
        designation: { id: "d-1", name: "Naib Tehsildar (LA)" },
        workstreams: [],
        desks: [{ id: "desk-1", code: "NT_LAC", name: "Naib Tehsildar Desk" }]
      }),
    });
  });

  // 2. Home Dashboard Mocks
  await page.route("**/api/dak/my-desk*", (route) => {
    route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify({ items: [], summary: { total: 14 } }),
    });
  });

  await page.route("**/api/work-items/my-work*", (route) => {
    route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify({ items: [], summary: { total: 8 } }),
    });
  });

  await page.route("**/api/attention-items*", (route) => {
    route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify({ items: [], totalCount: 5 }),
    });
  });

  await page.route("**/api/court-cases/imports/urgent-reviews*", (route) => {
    route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify({
        items: [],
        summary: { urgentReviews: 3, officeToday: "2026-10-06" }
      }),
    });
  });

  // 3. Court Cases Mocks
  const courtCaseList = {
    items: [
      {
        id: "case-101",
        caseNumber: "W.P.(C) 4589/2026",
        courtName: "High Court of Delhi",
        petitionerName: "Ramesh Chandra & Ors",
        respondentName: "Land Acquisition Collector & Anr",
        caseType: "Writ Petition",
        status: "Pending",
        nextHearingDate: "2026-10-24",
        villageName: "Galibpur",
        khasraNumber: "45//2",
        awardNumber: "12/2008-09"
      },
      {
        id: "case-102",
        caseNumber: "LA.APP. 112/2025",
        courtName: "High Court of Delhi",
        petitionerName: "Union of India",
        respondentName: "Smt. Shanti Devi",
        caseType: "Land Acquisition Appeal",
        status: "Hearing",
        nextHearingDate: "2026-11-02",
        villageName: "Alipur",
        khasraNumber: "12//4",
        awardNumber: "05/2007-08"
      }
    ],
    totalCount: 2,
    page: 0,
    pageSize: 25
  };

  await page.route("**/api/court-cases?*", (route) => {
    route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify(courtCaseList),
    });
  });

  await page.route("**/api/court-cases/case-101", (route) => {
    route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify({
        id: "case-101",
        caseNumber: "W.P.(C) 4589/2026",
        courtName: "High Court of Delhi",
        caseTitle: "Ramesh Chandra & Ors vs Land Acquisition Collector & Anr",
        caseType: "Writ Petition",
        filedDate: "2026-03-12",
        currentStatus: "Pending",
        disposedDate: null,
        remarks: "Petition seeking enhanced compensation under Section 64/28A for acquisition of Khasra 45/2 Galibpur.",
        revision: 1,
        responsibleOfficeDeskId: "desk-1",
        responsibleOfficeDeskName: "Naib Tehsildar Desk",
        assignedUserId: "user-1",
        assignedUserDisplayName: "Ashwani Baghel (NT)",
        authoritativeNextDate: "2026-10-24",
        activeScheduleNextDate: "2026-10-24",
        isProjectedToCalendar: true,
        activeScheduledEventId: null,
        nextHearingDate: "2026-10-24",
        lastHearingDate: "2026-08-14",
        lastOrderType: "Notice",
        restraintNature: null,
        lastSummary: "Notice issued to LAC Delhi. Counter affidavit to be filed within 4 weeks.",
        awards: [
          { awardId: "a-456", awardNumber: "12/2008-09", projectName: "Undeveloped Land Acquisition", awardDate: "2008-11-15", villageNames: ["Galibpur"] }
        ],
        khasras: [
          { khasraId: "k-789", villageId: "v-101", villageName: "Galibpur", normalizedNumber: "45//2", qualifier: null, recordedArea: 4.8, areaUnit: "Bigha" }
        ],
        matters: [
          { matterId: "m-101", title: "Section 28A Compensation Enhancement - Village Galibpur", referenceNumber: "MAT/2026/0088", status: "Active", workstreamName: "Land Acquisition", draftsCount: 1, workItemsCount: 2 }
        ],
        parties: [
          { id: "party-1", courtCaseId: "case-101", displayName: "Ramesh Chandra & Ors", role: "Petitioner", sequence: 1 },
          { id: "party-2", courtCaseId: "case-101", displayName: "Land Acquisition Collector & Anr", role: "Respondent", sequence: 2 }
        ],
        representatives: [
          { id: "rep-1", courtCaseId: "case-101", displayName: "Adv. Rajesh Kumar", representativeType: "Standing Counsel", representsRole: "Respondent" }
        ],
        awardsCount: 1,
        khasrasCount: 1,
        mattersCount: 1,
        partiesCount: 2,
        documentsCount: 0,
        proceedingsCount: 1,
        eventsCount: 1,
        capabilities: {
          canEdit: true,
          canAssign: true,
          canManageProceedings: true,
          canManageDocuments: true,
          canPromoteToCalendar: true,
          canLinkAward: true,
          canLinkKhasra: true,
          canLinkMatter: true
        },
        operationalNdoh: "2026-10-24",
        operationalNdohSource: "Listing"
      }),
    });
  });

  // 4. Matter Mocks (Comprehensive to eliminate any JSON parse errors)
  const matterList = {
    items: [
      {
        id: "m-101",
        matterNumber: "MAT/2026/0088",
        title: "Section 28A Compensation Enhancement - Village Galibpur",
        category: "Land Acquisition Enhancement",
        status: "Active",
        villageName: "Galibpur",
        awardNumber: "12/2008-09",
        assignedOfficer: "Ashwani Baghel (NT)",
        createdAt: "2026-09-01"
      },
      {
        id: "m-102",
        matterNumber: "MAT/2026/0045",
        title: "DHC High Court Writ Defense - W.P.(C) 4589/2026",
        category: "Court Litigation Defense",
        status: "Under Review",
        villageName: "Galibpur",
        awardNumber: "12/2008-09",
        assignedOfficer: "Legal Cell",
        createdAt: "2026-08-16"
      }
    ],
    totalCount: 2,
    page: 0,
    pageSize: 25
  };

  await page.route("**/api/matters?*", (route) => {
    route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify(matterList),
    });
  });

  await page.route("**/api/matters/context*", (route) => {
    route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify({
        workstreams: [
          { id: "ws-1", name: "Land Acquisition", code: "LA" },
          { id: "ws-2", name: "Court Litigation", code: "LIT" }
        ]
      })
    });
  });

  await page.route("**/api/matters/m-101/context*", (route) => {
    route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify({
        matter: {
          id: "m-101",
          villageId: "v-101",
          villageName: "Galibpur",
          workstreamId: "ws-1",
          workstreamName: "Land Acquisition",
          workstreamCode: "LA",
          title: "Section 28A Compensation Enhancement - Village Galibpur",
          matterType: "Compensation",
          status: "Active",
          referenceNumber: "MAT/2026/0088",
          remarks: "Matter file tracking Section 28A applications submitted by landowners of Village Galibpur.",
          khasraReferenceText: "45//2",
          revision: 1,
          createdAt: "2026-09-01T10:00:00Z",
          updatedAt: "2026-10-06T10:00:00Z"
        },
        village: {
          villageId: "v-101",
          name: "Galibpur"
        },
        awards: [
          { awardId: "a-456", awardNumber: "12/2008-09", awardDate: "2008-11-15", isPrimary: true }
        ],
        khasras: [
          { khasraId: "k-789", villageId: "v-101", displayNumber: "45//2" }
        ],
        courtCases: [
          { courtCaseId: "case-101", caseNumber: "W.P.(C) 4589/2026", caseTitle: "Ramesh Chandra vs LAC", courtName: "High Court of Delhi", currentStatus: "Pending", operationalNdoh: "2026-10-24" }
        ],
        courtContextState: "Associated",
        daks: [
          { dakId: "dak-101", diaryNumber: "DAK/2026/00142", subject: "Representation regarding enhancement of compensation for Khasra 45/2 Galibpur", status: "InProcess", receivedDate: "2026-10-06" }
        ],
        workItems: [
          { workItemId: "wi-1", title: "Verify Revenue Khatauni Extract", status: "Pending", priority: "Urgent", dueAt: "2026-10-20" }
        ],
        outwards: [],
        outwardCount: 0,
        documentCount: 1,
        draftCount: 0
      })
    });
  });

  await page.route("**/api/matters/m-101/documents*", (route) => {
    route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify([
        {
          id: "md-1",
          documentId: "doc-999",
          documentRole: "Application",
          displayName: "Landowner Representation Section 28A",
          originalFileName: "Scanned_Representation_DAK_00142.pdf",
          mimeType: "application/pdf",
          fileSize: 1048576,
          uploadedAt: "2026-10-06T10:15:00Z"
        }
      ])
    });
  });

  await page.route("**/api/matters/m-101/events*", (route) => {
    route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify([
        {
          id: "ev-1",
          sequenceNumber: 1,
          action: "MatterCreated",
          actionAt: "2026-09-01T10:00:00Z",
          actionByDisplayNameSnapshot: "Ashwani Baghel (NT)"
        },
        {
          id: "ev-2",
          sequenceNumber: 2,
          action: "DakLinked",
          actionAt: "2026-10-06T10:35:00Z",
          actionByDisplayNameSnapshot: "Ashwani Baghel (NT)"
        }
      ])
    });
  });

  await page.route("**/api/matters/m-101", (route) => {
    route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify({
        id: "m-101",
        matterNumber: "MAT/2026/0088",
        title: "Section 28A Compensation Enhancement - Village Galibpur",
        category: "Land Acquisition Enhancement",
        status: "Active",
        villageName: "Galibpur",
        awardNumber: "12/2008-09",
        assignedOfficer: "Ashwani Baghel (NT)",
        description: "Matter file tracking Section 28A applications submitted by landowners of Village Galibpur following High Court judgment.",
        createdAt: "2026-09-01",
        notes: [],
        documents: [],
        linkedCourtCases: [
          { id: "case-101", caseNumber: "W.P.(C) 4589/2026", courtName: "High Court of Delhi" }
        ]
      }),
    });
  });

  await page.route("**/api/outward?matterId=*", (route) => {
    route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify({ items: [] }) });
  });

  // 5. Land / Village Mocks (Comprehensive to eliminate any JSON parse errors)
  const villageList = {
    items: [
      { id: "v-101", name: "Galibpur", subDivisionName: "Najafgarh", districtName: "South-West", khasraCount: 142, awardCount: 4 },
      { id: "v-102", name: "Alipur", subDivisionName: "Alipur", districtName: "North", khasraCount: 310, awardCount: 8 },
      { id: "v-103", name: "Kanjhawala", subDivisionName: "Kanjhawala", districtName: "North-West", khasraCount: 225, awardCount: 6 }
    ],
    totalCount: 3,
    page: 0,
    pageSize: 25
  };

  await page.route("**/api/villages?*", (route) => {
    route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify(villageList),
    });
  });

  await page.route("**/api/villages/v-101/overview*", (route) => {
    route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify({
        village: {
          id: "v-101",
          name: "Galibpur",
          subDivision: {
            id: "sd-1",
            name: "Najafgarh",
            district: { id: "d-1", name: "South-West" }
          },
          totalKhasras: 142,
          linkedAwards: 4,
          documentCount: 12,
          lrAvailable: true
        },
        official: {
          khasraCount: 142,
          awardCount: 4,
          notificationCount: 2,
          possessionEventCount: 1,
          courtCaseCount: 3,
          valuationRuleCount: 0,
          compensationRuleCount: 0,
          claimCount: 0
        },
        awards: [
          {
            id: "a-456",
            awardNumber: "12/2008-09",
            awardDate: "2008-11-15",
            awardType: "Section 11",
            status: "Published",
            khasraCount: 142,
            documentCount: 3
          }
        ],
        notifications: [
          {
            id: "notif-1",
            notificationNumber: "F.15(12)/2007/L&B/LA",
            sectionType: "4",
            notificationDate: "2007-04-12"
          }
        ],
        pendingReview: [],
        sources: [
          { sourceType: "Award Document", status: "Loaded", detail: "Award 12/2008-09 PDF verified" },
          { sourceType: "Revenue Khatauni", status: "Loaded", detail: "1980-81 Jamabandi available" }
        ]
      })
    });
  });

  await page.route("**/api/villages/v-101/matters*", (route) => {
    route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify(matterList.items)
    });
  });

  await page.route("**/api/villages/v-101/core-records*", (route) => {
    route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify([
        {
          id: "a-456",
          awardNumber: "12/2008-09",
          awardDate: "2008-11-15",
          awardType: "Section 11",
          roles: []
        }
      ])
    });
  });

  await page.route("**/api/villages/v-101/khasras*", (route) => {
    route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify({
        items: [
          {
            id: "k-789",
            displayNumber: "45//2",
            areaBigha: 4,
            areaBiswa: 16,
            areaBiswansi: 0,
            ownerSummary: "Ramesh Chandra & Ors",
            acquisitionStatus: "Acquired",
            awards: [{ id: "a-456", awardNumber: "12/2008-09" }]
          }
        ],
        totalCount: 142,
        page: 0,
        pageSize: 25
      })
    });
  });

  await page.route("**/api/villages/v-101", (route) => {
    route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify({
        id: "v-101",
        name: "Galibpur",
        subDivision: { id: "sd-1", name: "Najafgarh", district: { id: "d-1", name: "South-West" } },
        totalKhasras: 142,
        linkedAwards: 4,
        awards: [
          { id: "a-456", awardNumber: "12/2008-09", awardDate: "2008-11-15", totalLandAcquired: "125 Bigha 4 Biswa" }
        ],
        khasras: { items: [], totalCount: 142, page: 0, pageSize: 25 }
      }),
    });
  });

  // 6. Common Dak Lookups Mocks
  await page.route("**/api/dak/lookups/registration", (route) => {
    route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify({
        categories: [
          { id: "cat-1", code: "LAND_REF", name: "Land Acquisition Reference", defaultPriority: "Routine", isActive: true },
          { id: "cat-2", code: "COURT_NOTICE", name: "High Court Summon / Notice", defaultPriority: "Urgent", isActive: true },
          { id: "cat-3", code: "REP_LANDOWNER", name: "Landowner Representation", defaultPriority: "Routine", isActive: true }
        ],
        workstreams: [
          { id: "ws-1", code: "LA", name: "Land Acquisition", isActive: true },
          { id: "ws-2", code: "LIT", name: "Court Litigation", isActive: true },
          { id: "ws-3", code: "PAY", name: "Disbursement & Award", isActive: true }
        ]
      }),
    });
  });

  await page.route("**/api/dak/lookups/directory", (route) => {
    route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify({
        desks: [
          { id: "desk-1", code: "NT_LAC", name: "Naib Tehsildar Desk", isActive: true },
          { id: "desk-2", code: "LA_SECTION", name: "Land Acquisition Branch", isActive: true },
          { id: "desk-3", code: "LEGAL_CELL", name: "Legal & Court Cell", isActive: true }
        ]
      }),
    });
  });

  await page.route("**/api/dak?*", (route) => {
    route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify({
        items: [
          {
            id: "dak-101",
            diaryNumber: "DAK/2026/00142",
            receivedDate: "2026-10-06",
            subject: "Representation regarding enhancement of compensation for Khasra 45/2 Galibpur",
            senderName: "Ramesh Chandra & Ors",
            senderDepartment: "Landowners Association",
            inwardMode: "Physical / By Hand",
            priority: "Urgent",
            dueDate: "2026-10-20",
            status: "InProcess",
            categoryName: "Landowner Representation",
            workstreamName: "Land Acquisition",
            assignedDeskName: "Naib Tehsildar Desk",
            assignedUserDisplayName: "Ashwani Baghel (NT)",
            hasDocument: true,
            revision: 2,
            createdAt: "2026-10-06T10:15:00Z",
            recordStatus: "Active"
          },
          {
            id: "dak-102",
            diaryNumber: "DAK/2026/00141",
            receivedDate: "2026-10-06",
            subject: "Notice in W.P.(C) 4589/2026 High Court of Delhi vs LAC Delhi",
            senderName: "Registrar, High Court of Delhi",
            senderDepartment: "Judicial Branch",
            inwardMode: "Court Summon / Special Messenger",
            priority: "Immediate",
            dueDate: "2026-10-12",
            status: "InProcess",
            categoryName: "High Court Summon / Notice",
            workstreamName: "Court Litigation",
            assignedDeskName: "Legal & Court Cell",
            assignedUserDisplayName: "Sunil Sharma",
            hasDocument: true,
            revision: 3,
            createdAt: "2026-10-06T09:30:00Z",
            recordStatus: "Active"
          }
        ],
        totalCount: 2,
        page: 0,
        pageSize: 25
      }),
    });
  });

  await page.route("**/api/dak/delivery-queue*", (route) => {
    route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify({
        items: [
          {
            id: "dak-101",
            diaryNumber: "DAK/2026/00142",
            status: "InProcess",
            routingState: "InTransit",
            physicalState: "CustodyConfirmed",
            revision: 2
          },
          {
            id: "dak-102",
            diaryNumber: "DAK/2026/00141",
            status: "InProcess",
            routingState: "WithHolder",
            physicalState: "AtRecordedLocation",
            revision: 3
          }
        ],
        totalCount: 2,
        page: 0,
        pageSize: 25
      })
    });
  });

  await page.route("**/api/dak/*/timeline", (route) => {
    route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify([
        {
          id: "mov-1",
          sequenceNumber: 1,
          action: "Registered",
          actionByUserId: "user-intake",
          actionByDisplayName: "Intake Operator",
          actionAt: "2026-10-06T10:15:00Z",
          remarks: "Physical application received at reception counter with 1 enclosure."
        },
        {
          id: "mov-2",
          sequenceNumber: 2,
          action: "Marked",
          fromDeskName: "Central Inward Intake",
          toDeskId: "desk-1",
          toDeskCode: "NT_LAC",
          toDeskName: "Naib Tehsildar Desk",
          toUserId: "user-1",
          toUserDisplayName: "Ashwani Baghel (NT)",
          actionByUserId: "user-dispatch",
          actionByDisplayName: "Central Dispatch Inward Officer",
          actionAt: "2026-10-06T10:30:00Z",
          instructions: "Please examine village award and Khatauni records for Khasra 45/2 and put up report.",
          remarks: "Urgent representation flagged for priority scrutiny."
        }
      ])
    });
  });

  await page.route("**/api/dak/*/physical-original", (route) => {
    route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify({
        hasPhysicalOriginal: true,
        deskId: "desk-1",
        userId: "user-1",
        locationNote: "Almirah #2, Rack B, File #142",
        provenanceNote: "Physical stamped original verified and stored in central dispatch safe.",
        updatedAt: "2026-10-06T10:20:00Z",
        updatedByUserId: "user-1",
        revision: 2
      }),
    });
  });

  await page.route("**/api/dak/*/movement-targets", (route) => {
    route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify({
        desks: [
          {
            id: "desk-1",
            code: "NT_LAC",
            name: "Naib Tehsildar Desk",
            isActive: true,
            members: [
              { userId: "user-1", displayName: "Ashwani Baghel", designation: "Naib Tehsildar", isPrimary: true }
            ]
          },
          {
            id: "desk-2",
            code: "LA_SECTION",
            name: "Land Acquisition Branch",
            isActive: true,
            members: [
              { userId: "user-2", displayName: "Rajesh Verma", designation: "Section Officer (LA)", isPrimary: true },
              { userId: "user-3", displayName: "Deepak Mehra", designation: "Kanungo (LA)", isPrimary: false }
            ]
          },
          {
            id: "desk-3",
            code: "LEGAL_CELL",
            name: "Legal & Court Cell",
            isActive: true,
            members: [
              { userId: "user-4", displayName: "Sunil Sharma", designation: "Legal Assistant", isPrimary: true }
            ]
          }
        ]
      }),
    });
  });

  await page.route("**/api/outward?dakId=*", (route) => {
    route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify({ items: [] }) });
  });

  const artifactDir = "C:\\Users\\ashwa\\.gemini\\antigravity\\brain\\46d891e0-f3bc-4271-97ab-04c71a748d70";
  const localDir = path.join(process.cwd(), "screenshots");
  if (!fs.existsSync(localDir)) fs.mkdirSync(localDir, { recursive: true });

  const capture = async (num, name, urlPath, waitForSel) => {
    console.log(`\n${num}. Capturing ${name} at 1366 × 768 (${urlPath})...`);
    await page.goto(`http://127.0.0.1:${PORT}${urlPath}`, { waitUntil: "networkidle" });
    if (waitForSel) await page.waitForSelector(waitForSel, { timeout: 6000 }).catch(() => {});
    await page.waitForTimeout(400);

    const artPath = path.join(artifactDir, `${name}.png`);
    await page.screenshot({ path: artPath });
    fs.copyFileSync(artPath, path.join(localDir, `${name}.png`));
    console.log(`Saved: ${artPath}`);
  };

  // ─── PART A: CLEAN PRESERVATION RUNTIME SCREENSHOTS ───
  // 1. Home dashboard
  await capture("1", "home_dashboard_1366x768", "/", ".home-container");

  // 2. Court directory landing
  await capture("2", "court_directory_landing_1366x768", "/court-cases", ".court-directory-page, .court-container, .table-wrap");

  // 3. Court workspace
  await capture("3", "court_workspace_1366x768", "/court-cases/case-101", ".court-workspace-container");

  // 4. Matter directory
  await capture("4", "matter_directory_1366x768", "/matters", ".matter-directory-page, .matter-list, .table-wrap");

  // 5. Matter workspace (CLEAN: No JSON syntax errors)
  await capture("5", "matter_workspace_1366x768", "/matters/m-101", ".matter-workspace-shell");

  // 6. Land Records directory
  await capture("6", "land_records_directory_1366x768", "/villages", ".land-records-page, .villages-directory, .table-wrap");

  // 7. Village Workspace Galibpur (CLEAN: No JSON syntax errors)
  await capture("7", "land_village_workspace_1366x768", "/villages/v-101", ".village-overview-container, .summary-strip");

  // ─── PART B: DAK SCREENSHOTS ───
  // 8. Dak Quick Intake
  await page.goto(`http://127.0.0.1:${PORT}/dak/register`, { waitUntil: "networkidle" });
  await page.waitForSelector(".quick-intake-form");
  await page.fill("input[placeholder='e.g. DAK/2026/00142']", "DAK/2026/00142");
  await page.fill("input[placeholder='Sender Name / Entity']", "Ramesh Chandra & Ors");
  await page.fill("input[placeholder='e.g. F.1(23)/2025/L&B']", "REP/GAL/2026/99");
  await page.fill("input[placeholder='Subject or title of incoming correspondence...']", "Representation regarding enhancement of compensation for Khasra 45/2 Galibpur");
  await page.waitForTimeout(300);
  const shot8Art = path.join(artifactDir, "dak_quick_intake_1366x768.png");
  await page.screenshot({ path: shot8Art });
  fs.copyFileSync(shot8Art, path.join(localDir, "dak_quick_intake_1366x768.png"));
  console.log(`8. Saved Dak Quick Intake: ${shot8Art}`);

  // 9. Dak Directory
  await capture("9", "dak_directory_1366x768", "/dak", ".compact-dak-table");

  // 9b. Dak Delivery Queue: Incoming Dispatches
  console.log("\n9b. Capturing dak_delivery_queue_incoming_1366x768 at 1366 × 768...");
  await page.click("button:has-text('Incoming Dispatches')");
  await page.waitForSelector(".compact-dak-table");
  await page.waitForTimeout(300);
  const shot9bArt = path.join(artifactDir, "dak_delivery_queue_incoming_1366x768.png");
  await page.screenshot({ path: shot9bArt });
  fs.copyFileSync(shot9bArt, path.join(localDir, "dak_delivery_queue_incoming_1366x768.png"));
  console.log(`9b. Saved Delivery Queue (Incoming): ${shot9bArt}`);

  // 10. Dak State 1: Active With Holder (Accepted at Desk)
  console.log("\n10. Capturing dak_active_with_holder_1366x768 at 1366 × 768...");
  await page.route("**/api/dak/dak-101", (route) => {
    route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify({
        id: "dak-101",
        diaryNumber: "DAK/2026/00142",
        receivedDate: "2026-10-06",
        subject: "Representation regarding enhancement of compensation for Khasra 45/2 Galibpur",
        senderName: "Ramesh Chandra & Ors",
        senderDesignation: "Authorized Representative",
        senderDepartment: "Landowners Association",
        senderAddress: "H.No. 42, Village Galibpur, New Delhi - 110073",
        senderReferenceNumber: "REP/GAL/2026/99",
        senderLetterDate: "2026-10-04",
        inwardMode: "Physical / By Hand",
        priority: "Urgent",
        dueDate: "2026-10-20",
        status: "InProcess",
        categoryId: "cat-3",
        categoryName: "Landowner Representation",
        workstreamId: "ws-1",
        workstreamName: "Land Acquisition",
        revision: 2,
        mainDocumentId: "doc-999",
        mainDocumentFileName: "Scanned_Representation_DAK_00142.pdf",
        currentAssignment: {
          id: "asg-1",
          officeDeskId: "desk-1",
          deskCode: "NT_LAC",
          deskName: "Naib Tehsildar Desk",
          assignedUserId: "user-1",
          assignedUserDisplayName: "Ashwani Baghel",
          assignedByDisplayName: "Central Dispatch Inward Officer",
          assignedAt: "2026-10-06T10:30:00Z",
          instructions: "Please examine village award and Khatauni records for Khasra 45/2 and put up report.",
          isActive: true,
          isDeskActive: true,
          isUserEligible: true,
          needsAttention: false,
          isReceived: true // Active With Holder!
        },
        attachments: [
          { id: "att-1", documentId: "doc-888", originalFileName: "Annexure_A_Khasra_Extract.pdf", title: "Annexure A - Khasra Extract", attachmentType: "Annexure", sequenceOrder: 1, createdAt: "2026-10-06T10:16:00Z" }
        ],
        villageLinks: [{ linkId: "vl-1", entityId: "v-101", canOpen: true, displayName: "Galibpur", entityType: "Village" }],
        awardLinks: [{ linkId: "al-1", entityId: "a-456", canOpen: true, displayName: "12/2008-09", entityType: "Award" }],
        matterLinks: [{ linkId: "ml-1", entityId: "m-101", canOpen: true, displayName: "MAT/2026/0088", entityType: "Matter" }],
        khasraLinks: [{ linkId: "kl-1", entityId: "k-789", canOpen: true, displayName: "45//2", entityType: "Khasra" }],
        createdAt: "2026-10-06T10:15:00Z",
        recordStatus: "Active"
      })
    });
  });
  await page.goto(`http://127.0.0.1:${PORT}/dak/dak-101`, { waitUntil: "networkidle" });
  await page.waitForSelector(".dak-workspace-actions-bar");
  await page.waitForTimeout(400);
  const shot10Art = path.join(artifactDir, "dak_active_with_holder_1366x768.png");
  await page.screenshot({ path: shot10Art });
  fs.copyFileSync(shot10Art, path.join(localDir, "dak_active_with_holder_1366x768.png"));
  console.log(`10. Saved Active With Holder: ${shot10Art}`);

  // 11. Dak Send / Mark Modal (From Active With Holder state)
  console.log("\n11. Capturing dak_send_mark_modal_1366x768 at 1366 × 768...");
  await page.click("button:has-text('Send / Mark')");
  await page.waitForSelector(".modal-card");
  await page.waitForTimeout(400);
  const shot11Art = path.join(artifactDir, "dak_send_mark_modal_1366x768.png");
  await page.screenshot({ path: shot11Art });
  fs.copyFileSync(shot11Art, path.join(localDir, "dak_send_mark_modal_1366x768.png"));
  console.log(`11. Saved Send/Mark Modal: ${shot11Art}`);
  await page.click(".modal-card button:has-text('Cancel')");
  await page.waitForTimeout(300);

  // 12. Dak State 2: In Transit as SENDER (Ashwani Baghel sent it to Rajesh Verma)
  console.log("\n12. Capturing dak_in_transit_sender_1366x768 at 1366 × 768...");
  await page.route("**/api/dak/dak-101", (route) => {
    route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify({
        id: "dak-101",
        diaryNumber: "DAK/2026/00142",
        receivedDate: "2026-10-06",
        subject: "Representation regarding enhancement of compensation for Khasra 45/2 Galibpur",
        senderName: "Ramesh Chandra & Ors",
        senderDepartment: "Landowners Association",
        inwardMode: "Physical / By Hand",
        priority: "Urgent",
        dueDate: "2026-10-20",
        status: "InProcess",
        categoryId: "cat-3",
        categoryName: "Landowner Representation",
        workstreamId: "ws-1",
        workstreamName: "Land Acquisition",
        revision: 3,
        currentAssignment: {
          id: "asg-2",
          officeDeskId: "desk-2",
          deskCode: "LA_SECTION",
          deskName: "Land Acquisition Branch",
          assignedUserId: "user-2",
          assignedUserDisplayName: "Rajesh Verma (SO)",
          assignedByUserId: "user-1",
          assignedByDisplayName: "Ashwani Baghel", // Current user is sender!
          assignedAt: "2026-10-06T11:00:00Z",
          instructions: "Forwarded for verification of Land Acquisition payment voucher.",
          isActive: true,
          isDeskActive: true,
          isUserEligible: true,
          needsAttention: false,
          isReceived: false // In Transit!
        },
        attachments: [],
        villageLinks: [{ linkId: "vl-1", entityId: "v-101", canOpen: true, displayName: "Galibpur", entityType: "Village" }],
        awardLinks: [{ linkId: "al-1", entityId: "a-456", canOpen: true, displayName: "12/2008-09", entityType: "Award" }],
        matterLinks: [{ linkId: "ml-1", entityId: "m-101", canOpen: true, displayName: "MAT/2026/0088", entityType: "Matter" }],
        khasraLinks: [{ linkId: "kl-1", entityId: "k-789", canOpen: true, displayName: "45//2", entityType: "Khasra" }],
        createdAt: "2026-10-06T10:15:00Z",
        recordStatus: "Active"
      })
    });
  });
  await page.goto(`http://127.0.0.1:${PORT}/dak/dak-101`, { waitUntil: "networkidle" });
  await page.waitForSelector(".dak-workspace-actions-bar");
  await page.waitForTimeout(400);
  const shot12Art = path.join(artifactDir, "dak_in_transit_sender_1366x768.png");
  await page.screenshot({ path: shot12Art });
  fs.copyFileSync(shot12Art, path.join(localDir, "dak_in_transit_sender_1366x768.png"));
  console.log(`12. Saved In Transit as Sender: ${shot12Art}`);

  // 13. Dak State 3: In Transit as RECIPIENT (Central Dispatch sent it to Ashwani Baghel)
  console.log("\n13. Capturing dak_in_transit_recipient_1366x768 at 1366 × 768...");
  await page.route("**/api/dak/dak-101", (route) => {
    route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify({
        id: "dak-101",
        diaryNumber: "DAK/2026/00142",
        receivedDate: "2026-10-06",
        subject: "Representation regarding enhancement of compensation for Khasra 45/2 Galibpur",
        senderName: "Ramesh Chandra & Ors",
        senderDepartment: "Landowners Association",
        inwardMode: "Physical / By Hand",
        priority: "Urgent",
        dueDate: "2026-10-20",
        status: "InProcess",
        categoryId: "cat-3",
        categoryName: "Landowner Representation",
        workstreamId: "ws-1",
        workstreamName: "Land Acquisition",
        revision: 2,
        currentAssignment: {
          id: "asg-1",
          officeDeskId: "desk-1",
          deskCode: "NT_LAC",
          deskName: "Naib Tehsildar Desk",
          assignedUserId: "user-1",
          assignedUserDisplayName: "Ashwani Baghel", // Current user is recipient!
          assignedByUserId: "user-dispatch",
          assignedByDisplayName: "Central Dispatch Inward Officer",
          assignedAt: "2026-10-06T10:30:00Z",
          instructions: "Please examine village award and Khatauni records for Khasra 45/2 and put up report.",
          isActive: true,
          isDeskActive: true,
          isUserEligible: true,
          needsAttention: false,
          isReceived: false // In Transit!
        },
        attachments: [],
        villageLinks: [{ linkId: "vl-1", entityId: "v-101", canOpen: true, displayName: "Galibpur", entityType: "Village" }],
        awardLinks: [{ linkId: "al-1", entityId: "a-456", canOpen: true, displayName: "12/2008-09", entityType: "Award" }],
        matterLinks: [{ linkId: "ml-1", entityId: "m-101", canOpen: true, displayName: "MAT/2026/0088", entityType: "Matter" }],
        khasraLinks: [{ linkId: "kl-1", entityId: "k-789", canOpen: true, displayName: "45//2", entityType: "Khasra" }],
        createdAt: "2026-10-06T10:15:00Z",
        recordStatus: "Active"
      })
    });
  });
  await page.goto(`http://127.0.0.1:${PORT}/dak/dak-101`, { waitUntil: "networkidle" });
  await page.waitForSelector(".dak-workspace-actions-bar");
  await page.waitForTimeout(400);
  const shot13Art = path.join(artifactDir, "dak_in_transit_recipient_1366x768.png");
  await page.screenshot({ path: shot13Art });
  fs.copyFileSync(shot13Art, path.join(localDir, "dak_in_transit_recipient_1366x768.png"));
  console.log(`13. Saved In Transit as Recipient: ${shot13Art}`);

  // 14. Dak State 4: Resolved Terminal State
  console.log("\n14. Capturing dak_resolved_state_1366x768 at 1366 × 768...");
  await page.route("**/api/dak/dak-101", (route) => {
    route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify({
        id: "dak-101",
        diaryNumber: "DAK/2026/00142",
        receivedDate: "2026-10-06",
        subject: "Representation regarding enhancement of compensation for Khasra 45/2 Galibpur",
        senderName: "Ramesh Chandra & Ors",
        senderDepartment: "Landowners Association",
        inwardMode: "Physical / By Hand",
        priority: "Urgent",
        dueDate: "2026-10-20",
        status: "Disposed",
        categoryId: "cat-3",
        categoryName: "Landowner Representation",
        workstreamId: "ws-1",
        workstreamName: "Land Acquisition",
        revision: 4,
        currentAssignment: {
          id: "asg-1",
          officeDeskId: "desk-1",
          deskCode: "NT_LAC",
          deskName: "Naib Tehsildar Desk",
          assignedUserId: "user-1",
          assignedUserDisplayName: "Ashwani Baghel",
          assignedByDisplayName: "Central Dispatch",
          assignedAt: "2026-10-06T10:30:00Z",
          instructions: "Examined. Matter settled per award terms and section report placed in file.",
          isActive: true,
          isDeskActive: true,
          isUserEligible: true,
          needsAttention: false,
          isReceived: true
        },
        attachments: [],
        villageLinks: [{ linkId: "vl-1", entityId: "v-101", canOpen: true, displayName: "Galibpur", entityType: "Village" }],
        awardLinks: [{ linkId: "al-1", entityId: "a-456", canOpen: true, displayName: "12/2008-09", entityType: "Award" }],
        matterLinks: [{ linkId: "ml-1", entityId: "m-101", canOpen: true, displayName: "MAT/2026/0088", entityType: "Matter" }],
        khasraLinks: [{ linkId: "kl-1", entityId: "k-789", canOpen: true, displayName: "45//2", entityType: "Khasra" }],
        createdAt: "2026-10-06T10:15:00Z",
        updatedAt: "2026-10-06T14:30:00Z",
        resolution: {
          resolvedAt: "2026-10-06T14:30:00Z",
          resolvedByUserId: "user-1",
          resolvedByDisplayName: "Ashwani Baghel (NT)",
          remarks: "Matter settled per award terms and Section 28A scrutiny report placed in physical case file."
        },
        recordStatus: "Active"
      })
    });
  });
  await page.goto(`http://127.0.0.1:${PORT}/dak/dak-101`, { waitUntil: "networkidle" });
  await page.waitForSelector(".terminal-actions-bar");
  await page.waitForTimeout(400);
  const shot14Art = path.join(artifactDir, "dak_resolved_state_1366x768.png");
  await page.screenshot({ path: shot14Art });
  fs.copyFileSync(shot14Art, path.join(localDir, "dak_resolved_state_1366x768.png"));
  console.log(`14. Saved Resolved State: ${shot14Art}`);

  // 15. Dak State 5: Return Pending (Physical Paper Recovery Required)
  console.log("\n15. Capturing dak_return_pending_1366x768 at 1366 × 768...");
  await page.route("**/api/dak/dak-101", (route) => {
    route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify({
        id: "dak-101",
        diaryNumber: "DAK/2026/00142",
        receivedDate: "2026-10-06",
        subject: "Representation regarding enhancement of compensation for Khasra 45/2 Galibpur",
        senderName: "Ramesh Chandra & Ors",
        senderDepartment: "Landowners Association",
        inwardMode: "Physical / By Hand",
        priority: "Urgent",
        dueDate: "2026-10-20",
        status: "InProcess",
        categoryId: "cat-3",
        categoryName: "Landowner Representation",
        workstreamId: "ws-1",
        workstreamName: "Land Acquisition",
        revision: 4,
        routingState: "WithHolder",
        physicalState: "ReturnPending",
        pendingTransfer: {
          id: "tr-99",
          senderUserId: "user-1",
          toDeskId: "desk-2",
          toUserId: "user-2",
          toUserDisplayName: "Rajesh Verma (SO)",
          destinationKind: "Officer",
          purpose: "Transfer",
          state: "PulledBack",
          includesPhysicalOriginal: true,
          sentAt: "2026-10-06T11:00:00Z",
          pulledBackAt: "2026-10-06T11:20:00Z",
          pullBackReason: "Dispatched to wrong section officer by mistake."
        },
        currentAssignment: {
          id: "asg-1",
          officeDeskId: "desk-1",
          deskCode: "NT_LAC",
          deskName: "Naib Tehsildar Desk",
          assignedUserId: "user-1",
          assignedUserDisplayName: "Ashwani Baghel",
          assignedByUserId: "user-1",
          assignedByDisplayName: "Ashwani Baghel",
          assignedAt: "2026-10-06T10:30:00Z",
          instructions: "Transfer pulled back. Awaiting confirmation of physical file return.",
          isActive: true,
          isDeskActive: true,
          isUserEligible: true,
          needsAttention: false,
          isReceived: true
        },
        attachments: [],
        villageLinks: [{ linkId: "vl-1", entityId: "v-101", canOpen: true, displayName: "Galibpur", entityType: "Village" }],
        awardLinks: [{ linkId: "al-1", entityId: "a-456", canOpen: true, displayName: "12/2008-09", entityType: "Award" }],
        matterLinks: [{ linkId: "ml-1", entityId: "m-101", canOpen: true, displayName: "MAT/2026/0088", entityType: "Matter" }],
        khasraLinks: [{ linkId: "kl-1", entityId: "k-789", canOpen: true, displayName: "45//2", entityType: "Khasra" }],
        createdAt: "2026-10-06T10:15:00Z",
        recordStatus: "Active"
      })
    });
  });
  await page.goto(`http://127.0.0.1:${PORT}/dak/dak-101`, { waitUntil: "networkidle" });
  await page.waitForSelector(".dak-workspace-actions-bar");
  await page.waitForTimeout(400);
  const shot15Art = path.join(artifactDir, "dak_return_pending_1366x768.png");
  await page.screenshot({ path: shot15Art });
  fs.copyFileSync(shot15Art, path.join(localDir, "dak_return_pending_1366x768.png"));
  console.log(`15. Saved Return Pending State: ${shot15Art}`);

  await browser.close();
  server.close();
  console.log("\nALL PRESERVATION AND DAK SCREENSHOTS CAPTURED SUCCESSFULLY AT EXACTLY 1366 × 768!");
}

main().catch((err) => {
  console.error("Screenshot script failed:", err);
  process.exit(1);
});
