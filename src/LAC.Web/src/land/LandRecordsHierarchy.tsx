import React from "react";
import { Link } from "react-router-dom";
import { IconLand, IconAward, IconChevronRight, IconFileText } from "../components/Icons";
import "./land.css";

const api = "/api";

function LoadingState({ label = "Loading land records…" }: { label?: string }) {
  return (
    <div className="state loading" role="status">
      {label}
    </div>
  );
}

function ErrorState({ message }: { message: string }) {
  return (
    <div className="state error" role="alert">
      <strong>Unable to load land records.</strong>
      <span>{message}</span>
    </div>
  );
}

function EmptyState({ title, detail }: { title: string; detail: string }) {
  return (
    <div className="state empty">
      <strong>{title}</strong>
      <span>{detail}</span>
    </div>
  );
}

export const LandRecordsHierarchy: React.FC = () => {
  const [loading, setLoading] = React.useState(true);
  const [error, setError] = React.useState<string | null>(null);
  const [district, setDistrict] = React.useState<any>(null);

  React.useEffect(() => {
    let active = true;
    fetch(`${api}/home`, { credentials: "include" })
      .then((res) => {
        if (!res.ok) {
          if (res.status === 403) throw new Error("Access denied: You do not have permission to view land records.");
          throw new Error("Failed to load land records.");
        }
        return res.json();
      })
      .then((data) => {
        if (active) {
          setDistrict(data);
          setLoading(false);
        }
      })
      .catch((err) => {
        if (active) {
          setError(err?.message || "Failed to load land records");
          setLoading(false);
        }
      });
    return () => {
      active = false;
    };
  }, []);

  if (loading) return <LoadingState />;
  if (error) return <ErrorState message={error} />;
  if (!district)
    return (
      <EmptyState
        title="No administrative hierarchy available"
        detail="District land records will appear here once configured."
      />
    );

  return (
    <div className="land-records-page" style={{ paddingBottom: "16px" }}>
      <nav aria-label="Breadcrumb" className="breadcrumbs" style={{ marginBottom: "8px" }}>
        <Link to="/">Home</Link>
        <i>/</i>
        <span>Land Records</span>
      </nav>

      <div className="page-header" style={{ marginBottom: "16px" }}>
        <div>
          <p className="eyebrow" style={{ margin: "0 0 2px 0" }}>Administrative Workspace</p>
          <h1 style={{ margin: "0 0 4px 0" }}>{district.name} Land Records</h1>
          <p style={{ margin: 0, fontSize: "14px", color: "#64748b" }}>
            Explore land acquisition records, village directories, awards, and canonical khasra records.
          </p>
        </div>
      </div>

      {/* Compact Entry Destinations Grid */}
      <div
        className="land-entry-grid"
        style={{
          display: "grid",
          gridTemplateColumns: "repeat(auto-fit, minmax(240px, 1fr))",
          gap: "12px",
          marginBottom: "16px",
        }}
      >
        <Link to="/villages" className="lac-card" style={{ textDecoration: "none", color: "inherit", padding: "14px 16px" }}>
          <div style={{ display: "flex", alignItems: "center", gap: "10px", marginBottom: "6px" }}>
            <div
              style={{
                width: "32px",
                height: "32px",
                borderRadius: "6px",
                background: "rgba(37, 99, 235, 0.1)",
                color: "#2563eb",
                display: "flex",
                alignItems: "center",
                justifyContent: "center",
                flexShrink: 0,
              }}
            >
              <IconLand size={18} />
            </div>
            <div>
              <h3 style={{ margin: 0, fontSize: "15px", fontWeight: 700 }}>Village Directory</h3>
              <span style={{ fontSize: "12px", color: "#64748b" }}>All villages & khasras</span>
            </div>
          </div>
          <p style={{ margin: 0, fontSize: "12px", color: "#64748b", lineHeight: "1.4" }}>
            Access canonical village workspaces, core document matrix, and khasra entry.
          </p>
        </Link>

        <Link to="/awards" className="lac-card" style={{ textDecoration: "none", color: "inherit", padding: "14px 16px" }}>
          <div style={{ display: "flex", alignItems: "center", gap: "10px", marginBottom: "6px" }}>
            <div
              style={{
                width: "32px",
                height: "32px",
                borderRadius: "6px",
                background: "rgba(16, 185, 129, 0.1)",
                color: "#10b981",
                display: "flex",
                alignItems: "center",
                justifyContent: "center",
                flexShrink: 0,
              }}
            >
              <IconAward size={18} />
            </div>
            <div>
              <h3 style={{ margin: 0, fontSize: "15px", fontWeight: 700 }}>Awards Directory</h3>
              <span style={{ fontSize: "12px", color: "#64748b" }}>Acquisition awards</span>
            </div>
          </div>
          <p style={{ margin: 0, fontSize: "12px", color: "#64748b", lineHeight: "1.4" }}>
            Inspect land acquisition awards, section declarations, and linked khasras.
          </p>
        </Link>

        <Link to="/imports/lr" className="lac-card" style={{ textDecoration: "none", color: "inherit", padding: "14px 16px" }}>
          <div style={{ display: "flex", alignItems: "center", gap: "10px", marginBottom: "6px" }}>
            <div
              style={{
                width: "32px",
                height: "32px",
                borderRadius: "6px",
                background: "rgba(245, 158, 11, 0.1)",
                color: "#f59e0b",
                display: "flex",
                alignItems: "center",
                justifyContent: "center",
                flexShrink: 0,
              }}
            >
              <IconFileText size={18} />
            </div>
            <div>
              <h3 style={{ margin: 0, fontSize: "15px", fontWeight: 700 }}>LR Registers</h3>
              <span style={{ fontSize: "12px", color: "#64748b" }}>Import & review</span>
            </div>
          </div>
          <p style={{ margin: 0, fontSize: "12px", color: "#64748b", lineHeight: "1.4" }}>
            Review land record entry queues and unverified OCR extractions.
          </p>
        </Link>
      </div>

      {/* Compact Jurisdiction Summary Strip */}
      <div className="summary-strip" style={{ marginBottom: "16px" }}>
        <div className="metric">
          <strong>{district.name}</strong>
          <span>District Jurisdiction</span>
        </div>
        <div className="metric">
          <strong>{district.subDivisions?.length || 0}</strong>
          <span>Sub-divisions</span>
        </div>
        <div className="metric">
          <strong>
            {district.subDivisions?.reduce(
              (acc: number, s: any) => acc + (s.villageCount || 0),
              0
            ) || 0}
          </strong>
          <span>Total Villages</span>
        </div>
      </div>

      {/* Sub-divisions Table */}
      <section className="section">
        <div className="section-heading" style={{ marginBottom: "12px" }}>
          <div>
            <h2 style={{ fontSize: "16px", margin: 0 }}>Sub-divisions in {district.name}</h2>
            <span style={{ fontSize: "13px", color: "#64748b" }}>Select a sub-division to view constituent villages and land records.</span>
          </div>
          <span style={{ fontSize: "13px" }}>{district.subDivisions?.length || 0} available</span>
        </div>

        <div className="table-wrap">
          <table>
            <thead>
              <tr>
                <th scope="col">Sub-division</th>
                <th scope="col">Villages</th>
                <th scope="col">Action</th>
              </tr>
            </thead>
            <tbody>
              {district.subDivisions?.map((subdivision: any) => (
                <tr key={subdivision.id}>
                  <td>
                    <Link
                      to={`/subdivisions/${subdivision.id}`}
                      className="entity-link"
                      style={{ fontWeight: 650 }}
                    >
                      {subdivision.name}
                    </Link>
                  </td>
                  <td>
                    <span style={{ fontWeight: 600 }}>{subdivision.villageCount}</span> villages
                  </td>
                  <td>
                    <Link
                      className="text-action"
                      to={`/subdivisions/${subdivision.id}`}
                      style={{ display: "inline-flex", alignItems: "center", gap: "4px" }}
                    >
                      <span>Explore</span>
                      <IconChevronRight size={14} />
                    </Link>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </section>
    </div>
  );
};
