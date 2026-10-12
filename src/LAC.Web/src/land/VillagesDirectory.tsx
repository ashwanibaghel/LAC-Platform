import React, { useEffect, useMemo, useState } from "react";
import { Link } from "react-router-dom";
import { IconLand, IconSearch, IconChevronRight, IconAward } from "../components/Icons";
import "./land.css";

const api = "/api";

interface VillageItem {
  id: string;
  name: string;
  khasraCount: number;
}

interface PageResponse<T> {
  items: T[];
  page: number;
  pageSize: number;
  totalCount: number;
}

interface SubDivisionOption {
  id: string;
  name: string;
  villageCount: number;
}

// Convert uppercase/mixed case village names safely into Title Case
export function formatTitleCase(str: string): string {
  if (!str) return "";
  return str
    .toLowerCase()
    .split(/([\s/-]+)/)
    .map((part) => {
      if (/^[\s/-]+$/.test(part)) return part;
      return part.charAt(0).toUpperCase() + part.slice(1);
    })
    .join("");
}

// Sub-Division badge tone styling helper
function getSubDivisionToneClass(name?: string): string {
  if (!name) return "unassigned";
  const lower = name.toLowerCase();
  if (lower.includes("bijwasan")) return "bijwasan";
  if (lower.includes("dwarka")) return "dwarka";
  if (lower.includes("matiala")) return "matiala";
  if (lower.includes("najafgarh")) return "najafgarh";
  return "unassigned";
}

export const VillagesDirectory: React.FC = () => {
  const [page, setPage] = useState(0);
  const [searchTerm, setSearchTerm] = useState("");
  const [selectedSubDivisionId, setSelectedSubDivisionId] = useState<string>("ALL");

  const [subDivisions, setSubDivisions] = useState<SubDivisionOption[]>([]);
  const [villageSubDivMap, setVillageSubDivMap] = useState<Record<string, string>>({});
  const [villageAwardCountMap, setVillageAwardCountMap] = useState<Record<string, number>>({});
  const [awardsLoaded, setAwardsLoaded] = useState(false);

  const [villagesResult, setVillagesResult] = useState<PageResponse<VillageItem> | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  // 1. Initial Load: Fetch Sub-Divisions and mapping
  useEffect(() => {
    let active = true;

    // Fetch /api/home to discover the 4 official sub-divisions
    fetch(`${api}/home`, { credentials: "include" })
      .then((res) => (res.ok ? res.json() : null))
      .then((homeData) => {
        if (!active || !homeData?.subDivisions) return;
        const sds: SubDivisionOption[] = homeData.subDivisions;
        setSubDivisions(sds);

        // Fetch each sub-division's village list to establish exact village -> subdivision mapping
        Promise.all(
          sds.map((sd) =>
            fetch(`${api}/subdivisions/${sd.id}?page=0&pageSize=100`, { credentials: "include" })
              .then((r) => (r.ok ? r.json() : null))
              .then((sdDetail) => ({
                sdName: sd.name,
                villages: sdDetail?.villages?.items || [],
              }))
              .catch(() => ({ sdName: sd.name, villages: [] }))
          )
        ).then((results) => {
          if (!active) return;
          const mapping: Record<string, string> = {};
          for (const item of results) {
            for (const v of item.villages) {
              if (v.id) mapping[v.id] = item.sdName;
              if (v.name) {
                const trimmed = v.name.trim();
                mapping[trimmed.toLowerCase()] = item.sdName;
                mapping[trimmed.toUpperCase()] = item.sdName;
                mapping[trimmed] = item.sdName;
              }
            }
          }
          setVillageSubDivMap(mapping);
        });
      })
      .catch(() => {});

    // Fetch official Awards to derive real Award count per village
    fetch(`${api}/awards?page=0&pageSize=100`, { credentials: "include" })
      .then((res) => (res.ok ? res.json() : null))
      .then((awardData) => {
        if (!active || !awardData?.items) return;
        const counts: Record<string, number> = {};
        for (const award of awardData.items) {
          if (award.villageNames) {
            const vNames = award.villageNames.split(",").map((s: string) => s.trim().toLowerCase());
            for (const vName of vNames) {
              if (vName) {
                counts[vName] = (counts[vName] || 0) + 1;
              }
            }
          }
        }
        setVillageAwardCountMap(counts);
        setAwardsLoaded(true);
      })
      .catch(() => {
        if (active) setAwardsLoaded(true);
      });

    return () => {
      active = false;
    };
  }, []);

  // 2. Fetch Villages from /api/villages whenever filter, search or page changes
  useEffect(() => {
    let active = true;
    setLoading(true);

    const queryParams = new URLSearchParams();
    queryParams.set("page", page.toString());
    queryParams.set("pageSize", "25");
    if (searchTerm.trim()) {
      queryParams.set("q", searchTerm.trim());
    }
    if (selectedSubDivisionId && selectedSubDivisionId !== "ALL") {
      queryParams.set("subDivisionId", selectedSubDivisionId);
    }

    fetch(`${api}/villages?${queryParams.toString()}`, { credentials: "include" })
      .then((res) => (res.ok ? res.json() : null))
      .then((data) => {
        if (active && data) {
          setVillagesResult(data);
          setError(null);
          setLoading(false);
        }
      })
      .catch((err) => {
        if (active) {
          setError(err?.message || "Failed to load villages");
          setLoading(false);
        }
      });

    return () => {
      active = false;
    };
  }, [page, searchTerm, selectedSubDivisionId]);

  // Total villages across all sub-divisions for the "All" pill
  const totalDistrictVillages = useMemo(() => {
    return subDivisions.reduce((acc, s) => acc + (s.villageCount || 0), 0);
  }, [subDivisions]);

  return (
    <div className="land-records-workspace">
      {/* Breadcrumb Hierarchy */}
      <nav aria-label="Breadcrumb" className="breadcrumbs">
        <Link to="/">Home</Link>
        <i>/</i>
        <Link to="/land-records">Land Records</Link>
        <i>/</i>
        <span>Villages</span>
      </nav>

      {/* Header Strip */}
      <div className="villages-header-strip">
        <div>
          <h1 className="land-overview-title">Villages</h1>
          <p className="land-overview-subtitle">South West Delhi</p>
        </div>

        <div className="land-overview-nav-links">
          <Link to="/land-records" className="secondary-button land-toolbar-link">
            <IconLand size={14} /> Overview
          </Link>
          <Link to="/awards" className="secondary-button land-toolbar-link">
            <IconAward size={14} /> Awards
          </Link>
        </div>
      </div>

      {/* Filter & Search Toolbar */}
      <div className="villages-filter-toolbar">
        {/* Segmented Sub-Division Filter */}
        <div className="subdivision-segmented-filter" role="tablist" aria-label="Filter by Sub-Division">
          <button
            type="button"
            className={`subdivision-filter-btn ${selectedSubDivisionId === "ALL" ? "active" : ""}`}
            onClick={() => {
              setSelectedSubDivisionId("ALL");
              setPage(0);
            }}
          >
            <span>All</span>
            {totalDistrictVillages > 0 && (
              <span className="subdivision-filter-count">{totalDistrictVillages}</span>
            )}
          </button>

          {subDivisions.map((sd) => (
            <button
              key={sd.id}
              type="button"
              className={`subdivision-filter-btn ${selectedSubDivisionId === sd.id ? "active" : ""}`}
              onClick={() => {
                setSelectedSubDivisionId(sd.id);
                setPage(0);
              }}
            >
              <span>{sd.name}</span>
              <span className="subdivision-filter-count">{sd.villageCount}</span>
            </button>
          ))}
        </div>

        {/* Search Box */}
        <div className="villages-search-box">
          <IconSearch size={15} className="lac-search-icon" />
          <input
            type="text"
            value={searchTerm}
            onChange={(e) => {
              setSearchTerm(e.target.value);
              setPage(0);
            }}
            placeholder="Search village name…"
            aria-label="Search village name"
          />
          {searchTerm && (
            <button
              type="button"
              onClick={() => {
                setSearchTerm("");
                setPage(0);
              }}
              className="search-clear-btn"
              title="Clear search"
            >
              ✕
            </button>
          )}
        </div>
      </div>

      {/* Dense High-Clarity Table */}
      {loading ? (
        <div className="state loading" role="status">
          Loading villages…
        </div>
      ) : error ? (
        <div className="state error" role="alert">
          <strong>Error loading villages:</strong> {error}
        </div>
      ) : !villagesResult?.items?.length ? (
        <div className="state empty">
          <strong>No villages found</strong>
          <span>
            {searchTerm
              ? `No villages matched "${searchTerm}".`
              : "No villages registered under this administrative division."}
          </span>
        </div>
      ) : (
        <div className="village-dense-table-wrap">
          <table className="village-dense-table">
            <thead>
              <tr>
                <th scope="col" style={{ width: "32%" }}>Village</th>
                <th scope="col" style={{ width: "24%" }}>Sub-Division</th>
                <th scope="col" style={{ width: "16%" }}>Khasras</th>
                <th scope="col" style={{ width: "16%" }}>Awards</th>
                <th scope="col" style={{ width: "12%", textAlign: "right" }}>Action</th>
              </tr>
            </thead>
            <tbody>
              {villagesResult.items.map((village) => {
                const subDivName =
                  villageSubDivMap[village.id] ||
                  villageSubDivMap[village.name.trim().toLowerCase()] ||
                  (selectedSubDivisionId !== "ALL"
                    ? subDivisions.find((s) => s.id === selectedSubDivisionId)?.name
                    : undefined);

                const awardCount =
                  awardsLoaded
                    ? villageAwardCountMap[village.name.trim().toLowerCase()] ?? 0
                    : null;

                const formattedName = formatTitleCase(village.name);

                return (
                  <tr key={village.id}>
                    <td>
                      <div className="village-title-cell">
                        <Link to={`/villages/${village.id}`} className="village-link-title">
                          {formattedName}
                        </Link>
                      </div>
                    </td>

                    <td>
                      {subDivName ? (
                        <span className={`subdivision-badge ${getSubDivisionToneClass(subDivName)}`}>
                          {subDivName}
                        </span>
                      ) : (
                        <span className="subdivision-badge unassigned">South West Delhi</span>
                      )}
                    </td>

                    <td>
                      <span className="count-badge-khasra">
                        {village.khasraCount > 0 ? village.khasraCount.toLocaleString() : "—"}
                      </span>
                    </td>

                    <td>
                      {awardCount !== null ? (
                        awardCount > 0 ? (
                          <span className="count-badge-award">
                            {awardCount}
                          </span>
                        ) : (
                          <span className="text-muted">—</span>
                        )
                      ) : (
                        <span className="text-muted">…</span>
                      )}
                    </td>

                    <td style={{ textAlign: "right" }}>
                      <Link to={`/villages/${village.id}`} className="village-action-btn">
                        <span>Open</span>
                        <IconChevronRight size={13} />
                      </Link>
                    </td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        </div>
      )}

      {/* Pagination Strip */}
      {villagesResult && villagesResult.totalCount > villagesResult.pageSize && (
        <div className="pagination">
          <span>
            Showing <b>{page * villagesResult.pageSize + 1}</b> to{" "}
            <b>{Math.min((page + 1) * villagesResult.pageSize, villagesResult.totalCount)}</b> of{" "}
            <b>{villagesResult.totalCount}</b> villages
          </span>

          <div>
            <button
              type="button"
              disabled={page === 0}
              onClick={() => setPage((p) => Math.max(0, p - 1))}
            >
              Previous
            </button>
            <button
              type="button"
              disabled={(page + 1) * villagesResult.pageSize >= villagesResult.totalCount}
              onClick={() => setPage((p) => p + 1)}
            >
              Next
            </button>
          </div>
        </div>
      )}
    </div>
  );
};
