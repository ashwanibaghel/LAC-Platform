import React from "react";
import { Link } from "react-router-dom";
import { IconLand, IconSearch, IconAward, IconChevronRight } from "../components/Icons";

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
      .then((res) => (res.ok ? res.json() : null))
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
    <div className="land-records-page">
      <nav aria-label="Breadcrumb" className="breadcrumbs">
        <Link to="/">Home</Link>
        <i>/</i>
        <span>Land Records</span>
      </nav>

      <div className="page-header">
        <div>
          <p className="eyebrow">Administrative Hierarchy</p>
          <h1>{district.name} District</h1>
          <p>
            Explore land acquisition records by administrative sub-divisions, villages, and canonical khasra records.
          </p>
        </div>

        <div className="page-actions" style={{ display: "flex", gap: "10px" }}>
          <Link to="/villages" className="secondary-button">
            <IconLand size={16} /> All Villages
          </Link>
          <Link to="/imports/lr" className="secondary-button">
            <IconSearch size={16} /> LR Registers
          </Link>
        </div>
      </div>

      <div className="summary-strip" style={{ marginBottom: "28px" }}>
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

      <section className="section">
        <div className="section-heading">
          <div>
            <h2>Sub-divisions in {district.name}</h2>
            <span>Select a sub-division to view its constituent villages and land records.</span>
          </div>
          <span>{district.subDivisions?.length || 0} available</span>
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
