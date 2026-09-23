import React, { useState, useEffect } from "react";
import { useParams, useSearchParams, Link } from "react-router-dom";
import { useAuth } from "../auth/AuthProvider";
import { IconLand, IconAward, IconFileText, IconShield } from "../components/Icons";
import { VillageOverviewTab } from "./VillageOverviewTab";
import { VillageCoreRecordsTab } from "./VillageCoreRecordsTab";
import { VillageKhasrasTab } from "./VillageKhasrasTab";
import { VillageMattersTab } from "./VillageMattersTab";
import "./land.css";

const api = "/api";

interface SubDivisionRef {
  id: string;
  name: string;
  district?: {
    id: string;
    name: string;
  };
}

interface VillageData {
  id: string;
  name: string;
  subDivision?: SubDivisionRef;
  totalKhasras: number;
  linkedAwards: number;
  documentCount: number;
  lrAvailable: boolean;
}

export const VillageWorkspace: React.FC = () => {
  const { id } = useParams<{ id: string }>();
  const [searchParams, setSearchParams] = useSearchParams();
  const { hasPermission } = useAuth();

  const canViewKhasras = hasPermission("Khasra.View");
  const canViewMatters = hasPermission("Matter.View") || hasPermission("Matter.Create");

  let currentTab = searchParams.get("tab") || "overview";

  // If active tab is hidden due to permissions or legacy 'lr' tab, fall back to overview
  if (currentTab === "khasras" && !canViewKhasras) currentTab = "overview";
  if (currentTab === "lr") currentTab = "overview";
  if (currentTab === "matters" && !canViewMatters) currentTab = "overview";

  const [village, setVillage] = useState<VillageData | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (!id) return;
    let active = true;
    setLoading(true);
    setError(null);

    fetch(`${api}/villages/${id}`, { credentials: "include" })
      .then((res) => {
        if (!res.ok) {
          if (res.status === 403) throw new Error("Access denied: You do not have permission to view this village.");
          throw new Error("Failed to load village details.");
        }
        return res.json();
      })
      .then((data) => {
        if (active) {
          setVillage(data);
          setLoading(false);
        }
      })
      .catch((err: any) => {
        if (active) {
          setError(err?.message || "Could not fetch village workspace.");
          setLoading(false);
        }
      });

    return () => {
      active = false;
    };
  }, [id]);

  const handleTabChange = (tabKey: string) => {
    setSearchParams({ tab: tabKey });
  };

  if (!id) return <div className="state error">Invalid village identifier.</div>;
  if (loading) return <div className="state loading">Loading village workspace…</div>;
  if (error || !village) return <div className="state error">{error || "Village not found."}</div>;

  const locationSub = village.subDivision?.name
    ? `${village.subDivision.name}${village.subDivision.district?.name ? ` · ${village.subDivision.district.name}` : ""}`
    : "Sub-Division unassigned";

  return (
    <div className="land-records-page village-workspace-page">
      {/* Compact Record Identity & Segmented Navigation Workspace Header */}
      <div className="village-header-card">
        <div className="village-header-top">
          <Link to="/villages" className="village-back-btn" title="Return to Village Directory">
            <span style={{ fontSize: "14px", fontWeight: 700 }}>←</span>
            <span>Villages</span>
          </Link>
          <span className="v-head-divider">/</span>
          <div className="village-identity-inline">
            <h1 className="village-title">{village.name}</h1>
            <span className="village-sub-location">{locationSub}</span>
          </div>

          <div className="village-meta-strip">
            <span className="v-meta-badge">
              <strong>{village.totalKhasras ?? 0}</strong> Khasras
            </span>
            <span className="v-meta-dot">•</span>
            <span className="v-meta-badge">
              <strong>{village.linkedAwards ?? 0}</strong> Awards
            </span>
            <span className="v-meta-dot">•</span>
            <span className="v-meta-badge">
              <strong>{village.documentCount ?? 0}</strong> Documents
            </span>
          </div>
        </div>

        {/* Segmented Workspace Tabs */}
        <nav className="village-nav-segmented" aria-label="Village Workspace Navigation">
          <button
            type="button"
            className={`v-seg-tab ${currentTab === "overview" ? "active" : ""}`}
            onClick={() => handleTabChange("overview")}
          >
            <IconShield size={14} />
            <span>Overview</span>
          </button>

          <button
            type="button"
            className={`v-seg-tab ${currentTab === "core-records" ? "active" : ""}`}
            onClick={() => handleTabChange("core-records")}
          >
            <IconFileText size={14} />
            <span>Core Records</span>
          </button>

          {canViewKhasras && (
            <button
              type="button"
              className={`v-seg-tab ${currentTab === "khasras" ? "active" : ""}`}
              onClick={() => handleTabChange("khasras")}
            >
              <IconLand size={14} />
              <span>Khasras</span>
            </button>
          )}

          {canViewMatters && (
            <button
              type="button"
              className={`v-seg-tab ${currentTab === "matters" ? "active" : ""}`}
              onClick={() => handleTabChange("matters")}
            >
              <IconAward size={14} />
              <span>Matters</span>
            </button>
          )}
        </nav>
      </div>

      {/* Active Tab Panel Surface */}
      <div className="village-tab-panel">
        {currentTab === "overview" && <VillageOverviewTab villageId={id} />}
        {currentTab === "core-records" && <VillageCoreRecordsTab villageId={id} />}
        {currentTab === "khasras" && canViewKhasras && <VillageKhasrasTab villageId={id} />}
        {currentTab === "matters" && canViewMatters && <VillageMattersTab villageId={id} />}
      </div>
    </div>
  );
};
