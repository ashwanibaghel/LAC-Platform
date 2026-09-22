import React, { useState, useEffect } from "react";
import { useParams, useSearchParams, Link } from "react-router-dom";
import { useAuth } from "../auth/AuthProvider";
import { IconLand, IconAward, IconFileText, IconShield } from "../components/Icons";
import { VillageOverviewTab } from "./VillageOverviewTab";
import { VillageCoreRecordsTab } from "./VillageCoreRecordsTab";
import { VillageKhasrasTab } from "./VillageKhasrasTab";
import { VillageLandRecordsTab } from "./VillageLandRecordsTab";
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
  const canViewLr = hasPermission("LR.View");
  const canViewMatters = hasPermission("Matter.View") || hasPermission("Matter.Create");

  let currentTab = searchParams.get("tab") || "overview";

  // If active tab is hidden due to permissions, fall back to overview
  if (currentTab === "khasras" && !canViewKhasras) currentTab = "overview";
  if (currentTab === "lr" && !canViewLr) currentTab = "overview";
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

  return (
    <div className="land-records-page village-workspace-page">
      {/* Breadcrumbs */}
      <nav aria-label="Breadcrumb" className="breadcrumbs">
        <Link to="/">Home</Link>
        <i>/</i>
        <Link to="/land-records">Land Records</Link>
        <i>/</i>
        <Link to="/villages">Villages</Link>
        <i>/</i>
        <span>{village.name}</span>
      </nav>

      {/* Header Strip */}
      <div className="land-header-strip">
        <div className="land-title-area">
          <p className="eyebrow">
            Sub-division: {village.subDivision?.name || "—"} • District:{" "}
            {village.subDivision?.district?.name || "—"}
          </p>
          <h1>{village.name} Workspace</h1>
          <p>
            Official village land acquisition records, core document matrix, canonical khasras, and matter tracking.
          </p>
        </div>

        {/* Metric Pills */}
        <div style={{ display: "flex", gap: "12px", alignItems: "center" }}>
          <div className="metric" style={{ background: "#f8fafc", padding: "8px 14px", borderRadius: "8px" }}>
            <strong>{village.totalKhasras ?? 0}</strong>
            <span>Khasras</span>
          </div>
          <div className="metric" style={{ background: "#f8fafc", padding: "8px 14px", borderRadius: "8px" }}>
            <strong>{village.linkedAwards ?? 0}</strong>
            <span>Awards</span>
          </div>
          <div className="metric" style={{ background: "#f8fafc", padding: "8px 14px", borderRadius: "8px" }}>
            <strong>{village.documentCount ?? 0}</strong>
            <span>Documents</span>
          </div>
        </div>
      </div>

      {/* Horizontal Tabs Navigation */}
      <div className="land-tabs-nav" style={{ marginBottom: "24px" }}>
        <button
          className={`land-tab-button ${currentTab === "overview" ? "active" : ""}`}
          onClick={() => handleTabChange("overview")}
        >
          <IconShield size={16} /> Overview
        </button>
        <button
          className={`land-tab-button ${currentTab === "core-records" ? "active" : ""}`}
          onClick={() => handleTabChange("core-records")}
        >
          <IconFileText size={16} /> Core Records
        </button>
        {canViewKhasras && (
          <button
            className={`land-tab-button ${currentTab === "khasras" ? "active" : ""}`}
            onClick={() => handleTabChange("khasras")}
          >
            <IconLand size={16} /> Khasras
          </button>
        )}
        {canViewLr && (
          <button
            className={`land-tab-button ${currentTab === "lr" ? "active" : ""}`}
            onClick={() => handleTabChange("lr")}
          >
            <IconLand size={16} /> LR & Ownership
          </button>
        )}
        {canViewMatters && (
          <button
            className={`land-tab-button ${currentTab === "matters" ? "active" : ""}`}
            onClick={() => handleTabChange("matters")}
          >
            <IconAward size={16} /> Matters
          </button>
        )}
      </div>

      {/* Active Tab Panel */}
      <div className="land-tab-content">
        {currentTab === "overview" && <VillageOverviewTab villageId={id} />}
        {currentTab === "core-records" && <VillageCoreRecordsTab villageId={id} />}
        {currentTab === "khasras" && canViewKhasras && <VillageKhasrasTab villageId={id} />}
        {currentTab === "lr" && canViewLr && <VillageLandRecordsTab villageId={id} />}
        {currentTab === "matters" && canViewMatters && <VillageMattersTab villageId={id} />}
      </div>
    </div>
  );
};
