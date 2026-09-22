import React from "react";
import type { CourtCaseDetailDto } from "./types";

interface CourtOverviewTabProps {
  courtCase: CourtCaseDetailDto;
}

export const CourtOverviewTab: React.FC<CourtOverviewTabProps> = ({ courtCase }) => {
  const nextDate = courtCase.authoritativeNextDate || courtCase.activeScheduleNextDate || courtCase.nextHearingDate;
  const summary = courtCase.lastSummary || courtCase.latestProceedingSummary;

  return (
    <div className="court-overview-tab">
      {nextDate && (
        <div className="court-ndoh-alert-card">
          <div>
            <div style={{ fontSize: "12px", color: "#6d28d9", fontWeight: 600, textTransform: "uppercase" }}>
              Next Date of Hearing (NDOH)
            </div>
            <div style={{ fontSize: "20px", fontWeight: 700, color: "#4c1d95" }}>
              {nextDate}
            </div>
          </div>
          <div style={{ display: "flex", gap: "8px", alignItems: "center" }}>
            <span className="court-badge court-badge-ndoh">Authoritative Proceeding</span>
            {courtCase.isProjectedToCalendar ? (
              <span className="court-badge" style={{ background: "#dcfce7", color: "#166534" }}>● On Calendar</span>
            ) : (
              <span className="court-badge" style={{ background: "#f1f5f9", color: "#64748b" }}>○ Not on Calendar</span>
            )}
          </div>
        </div>
      )}

      {summary && (
        <div style={{ background: "#f8fafc", border: "1px solid #e2e8f0", borderRadius: "6px", padding: "16px", marginTop: "16px" }}>
          <div style={{ fontSize: "12px", fontWeight: 600, color: "#64748b", textTransform: "uppercase", marginBottom: "6px" }}>
            Latest Order / Proceeding Summary
          </div>
          <div style={{ fontSize: "14px", color: "#1e293b", lineHeight: 1.5 }}>
            {summary}
          </div>
        </div>
      )}

      <div className="court-overview-cards" style={{ marginTop: "20px" }}>
        <div className="court-card">
          <div className="court-card-title">Status</div>
          <div className="court-card-value">
            {courtCase.currentStatus ? (
              <span
                className={`court-badge ${
                  courtCase.currentStatus.toLowerCase() === "disposed"
                    ? "court-badge-status-disposed"
                    : courtCase.currentStatus.toLowerCase() === "stay"
                    ? "court-badge-status-stay"
                    : "court-badge-status-pending"
                }`}
              >
                {courtCase.currentStatus}
              </span>
            ) : (
              <span style={{ color: "#94a3b8", fontSize: "14px" }}>—</span>
            )}
          </div>
        </div>

        <div className="court-card">
          <div className="court-card-title">Proceedings</div>
          <div className="court-card-value">{courtCase.proceedingsCount ?? courtCase.proceedingCount ?? 0}</div>
        </div>

        <div className="court-card">
          <div className="court-card-title">Documents</div>
          <div className="court-card-value">{courtCase.documentsCount ?? courtCase.documentCount ?? 0}</div>
        </div>

        <div className="court-card">
          <div className="court-card-title">Linked Awards</div>
          <div className="court-card-value">
            {courtCase.awardsCount !== null && courtCase.awardsCount !== undefined ? courtCase.awardsCount : "—"}
          </div>
        </div>

        <div className="court-card">
          <div className="court-card-title">Linked Matters</div>
          <div className="court-card-value">
            {courtCase.mattersCount !== null && courtCase.mattersCount !== undefined ? courtCase.mattersCount : "—"}
          </div>
        </div>
      </div>

      <div style={{ background: "#fff", border: "1px solid #e2e8f0", borderRadius: "8px", padding: "20px", marginTop: "20px" }}>
        <h4 style={{ margin: "0 0 16px 0", fontSize: "16px", fontWeight: 600, color: "#1e293b" }}>
          Case Information
        </h4>
        <div style={{ display: "grid", gridTemplateColumns: "repeat(auto-fit, minmax(240px, 1fr))", gap: "16px" }}>
          <div>
            <div style={{ fontSize: "12px", color: "#64748b", fontWeight: 600 }}>COURT</div>
            <div style={{ fontSize: "14px", color: "#0f172a", marginTop: "4px" }}>{courtCase.courtName}</div>
          </div>
          <div>
            <div style={{ fontSize: "12px", color: "#64748b", fontWeight: 600 }}>CASE TYPE</div>
            <div style={{ fontSize: "14px", color: "#0f172a", marginTop: "4px" }}>{courtCase.caseType || "—"}</div>
          </div>
          <div>
            <div style={{ fontSize: "12px", color: "#64748b", fontWeight: 600 }}>FILED DATE</div>
            <div style={{ fontSize: "14px", color: "#0f172a", marginTop: "4px" }}>{courtCase.filedDate || "—"}</div>
          </div>
          <div>
            <div style={{ fontSize: "12px", color: "#64748b", fontWeight: 600 }}>DISPOSED DATE</div>
            <div style={{ fontSize: "14px", color: "#0f172a", marginTop: "4px" }}>{courtCase.disposedDate || "—"}</div>
          </div>
          <div>
            <div style={{ fontSize: "12px", color: "#64748b", fontWeight: 600 }}>RESPONSIBLE DESK</div>
            <div style={{ fontSize: "14px", color: "#0f172a", marginTop: "4px" }}>{courtCase.responsibleOfficeDeskName || "Unassigned Desk"}</div>
          </div>
          <div>
            <div style={{ fontSize: "12px", color: "#64748b", fontWeight: 600 }}>ASSIGNED OFFICER</div>
            <div style={{ fontSize: "14px", color: "#0f172a", marginTop: "4px" }}>{courtCase.assignedUserDisplayName || "Unassigned"}</div>
          </div>
        </div>

        {courtCase.remarks && (
          <div style={{ marginTop: "20px", paddingTop: "16px", borderTop: "1px solid #f1f5f9" }}>
            <div style={{ fontSize: "12px", color: "#64748b", fontWeight: 600, marginBottom: "4px" }}>REMARKS</div>
            <div style={{ fontSize: "14px", color: "#334155", lineHeight: 1.5 }}>{courtCase.remarks}</div>
          </div>
        )}
      </div>
    </div>
  );
};
