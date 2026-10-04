import React, { useEffect, useMemo, useState } from "react";
import { Link } from "react-router-dom";
import { IconLand, IconAward, IconArrowRight } from "../components/Icons";
import "./land.css";

const api = "/api";

interface SubDivisionItem {
  id: string;
  name: string;
  villageCount: number;
}

interface DistrictData {
  id: string;
  name: string;
  subDivisions: SubDivisionItem[];
}

function LoadingState({ label = "Loading administrative hierarchy…" }: { label?: string }) {
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
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [district, setDistrict] = useState<DistrictData | null>(null);
  const [awardTotalCount, setAwardTotalCount] = useState<number | null>(null);

  useEffect(() => {
    let active = true;

    // 1. Fetch District hierarchy & Sub-Divisions
    const fetchDistrict = fetch(`${api}/home`, { credentials: "include" })
      .then((res) => (res.ok ? res.json() : null))
      .then((data) => {
        if (active && data) {
          setDistrict(data);
        }
      })
      .catch((err) => {
        if (active) setError(err?.message || "Failed to load land records");
      });

    // 2. Fetch Awards count from /api/awards
    const fetchAwardsCount = fetch(`${api}/awards?page=0&pageSize=1`, { credentials: "include" })
      .then((res) => (res.ok ? res.json() : null))
      .then((data) => {
        if (active && data && typeof data.totalCount === "number") {
          setAwardTotalCount(data.totalCount);
        }
      })
      .catch(() => {});

    Promise.allSettled([fetchDistrict, fetchAwardsCount]).finally(() => {
      if (active) setLoading(false);
    });

    return () => {
      active = false;
    };
  }, []);

  const totalVillages = useMemo(() => {
    return (
      district?.subDivisions?.reduce(
        (acc: number, s: SubDivisionItem) => acc + (s.villageCount || 0),
        0
      ) || 0
    );
  }, [district]);

  // Order sub-divisions by village count descending (Matiala: 38, Najafgarh: 21, Bijwasan: 13, Dwarka: 4)
  const sortedSubDivisions = useMemo(() => {
    if (!district?.subDivisions) return [];
    return [...district.subDivisions].sort(
      (a, b) => (b.villageCount || 0) - (a.villageCount || 0)
    );
  }, [district]);

  if (loading) return <LoadingState />;
  if (error) return <ErrorState message={error} />;
  if (!district)
    return (
      <EmptyState
        title="No administrative hierarchy available"
        detail="District land records will appear here once configured."
      />
    );

  const subDivisionsCount = district.subDivisions?.length || 0;

  return (
    <div className="land-records-workspace">
      {/* Breadcrumb Navigation */}
      <nav aria-label="Breadcrumb" className="breadcrumbs">
        <Link to="/">Home</Link>
        <i>/</i>
        <span>Land Records</span>
      </nav>

      {/* Simplified, Calm Header */}
      <div className="land-overview-header">
        <div className="land-overview-titles">
          <h1 className="land-overview-title">Land Records</h1>
          <p className="land-overview-subtitle">{district.name}</p>
        </div>

        <div className="land-overview-stats">
          <div className="land-stat-pill">
            <span className="land-stat-val">{subDivisionsCount}</span>
            <span className="land-stat-lbl">Sub-Divisions</span>
          </div>
          <div className="land-stat-pill">
            <span className="land-stat-val">{totalVillages}</span>
            <span className="land-stat-lbl">Villages</span>
          </div>
          {awardTotalCount !== null && (
            <div className="land-stat-pill">
              <span className="land-stat-val">{awardTotalCount}</span>
              <span className="land-stat-lbl">Awards</span>
            </div>
          )}
        </div>
      </div>

      {/* Quick Jump Strip */}
      <div className="land-overview-toolbar">
        <div className="land-overview-nav-links">
          <Link to="/villages" className="secondary-button land-toolbar-link">
            <IconLand size={14} />
            <span>Villages Directory</span>
          </Link>
          <Link to="/awards" className="secondary-button land-toolbar-link">
            <IconAward size={14} />
            <span>Awards Register</span>
          </Link>
        </div>
      </div>

      {/* 4 Premium Cards Grid */}
      <section className="subdivision-grid-section" aria-label="Administrative sub-divisions">
        <div className="subdivision-cards-grid">
          {sortedSubDivisions.map((subdivision) => (
            <Link
              key={subdivision.id}
              to={`/subdivisions/${subdivision.id}`}
              className="subdivision-card"
            >
              <div className="subdivision-card-header">
                <div className="subdivision-card-main">
                  <h2 className="subdivision-card-name">{subdivision.name}</h2>
                  <span className="subdivision-card-meta">Sub-Division</span>
                </div>
                <span className="subdivision-card-count">
                  {subdivision.villageCount} {subdivision.villageCount === 1 ? "village" : "villages"}
                </span>
              </div>

              <div className="subdivision-card-footer">
                <span className="subdivision-card-action">
                  <span>Explore</span>
                  <IconArrowRight size={14} />
                </span>
              </div>
            </Link>
          ))}
        </div>
      </section>
    </div>
  );
};
