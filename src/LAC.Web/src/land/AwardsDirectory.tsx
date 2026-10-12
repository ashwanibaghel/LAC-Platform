import React, { useEffect, useMemo, useState } from "react";
import { Link } from "react-router-dom";
import {
  IconLand,
  IconSearch,
  IconChevronRight,
} from "../components/Icons";
import { formatTitleCase } from "./VillagesDirectory";
import "./land.css";

const api = "/api";

interface AwardItem {
  id: string;
  awardNumber: string;
  awardDate?: string | null;
  awardType?: string | null;
  status: string;
  actRegime?: string | null;
  projectName?: string | null;
  requiringAgency?: string | null;
  villageNames?: string | null;
  linkedKhasraCount: number;
}

interface VillageAwardGroup {
  villageKey: string;
  villageName: string;
  subDivisionName?: string;
  isMultiVillage?: boolean;
  awards: AwardItem[];
}

interface SubDivisionOption {
  id: string;
  name: string;
  villageCount: number;
}

function formatDate(iso?: string | null): string {
  if (!iso) return "—";
  try {
    const d = new Date(iso);
    if (isNaN(d.getTime())) return iso;
    return d.toLocaleDateString("en-GB", {
      day: "2-digit",
      month: "short",
      year: "numeric",
    });
  } catch {
    return iso;
  }
}

function getSubDivisionToneClass(name?: string): string {
  if (!name) return "unassigned";
  const lower = name.toLowerCase();
  if (lower.includes("bijwasan")) return "bijwasan";
  if (lower.includes("dwarka")) return "dwarka";
  if (lower.includes("matiala")) return "matiala";
  if (lower.includes("najafgarh")) return "najafgarh";
  return "unassigned";
}

function getAwardStatusTone(status?: string): string {
  if (!status) return "neutral";
  const lower = status.toLowerCase();
  if (lower === "published") return "success";
  if (lower === "draft") return "neutral";
  if (lower === "archived") return "muted";
  return "warning";
}

export const AwardsDirectory: React.FC = () => {
  const [searchTerm, setSearchTerm] = useState("");
  const [selectedSubDivision, setSelectedSubDivision] = useState<string>("ALL");
  const [expandedVillages, setExpandedVillages] = useState<Set<string>>(new Set());

  const [subDivisions, setSubDivisions] = useState<SubDivisionOption[]>([]);
  const [villageSubDivMap, setVillageSubDivMap] = useState<Record<string, string>>({});

  const [rawAwards, setRawAwards] = useState<AwardItem[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  // 1. Initial Load: Fetch Sub-Divisions mapping & all Awards
  useEffect(() => {
    let active = true;

    fetch(`${api}/home`, { credentials: "include" })
      .then((res) => (res.ok ? res.json() : null))
      .then((homeData) => {
        if (!active || !homeData?.subDivisions) return;
        const sds: SubDivisionOption[] = homeData.subDivisions;
        setSubDivisions(sds);

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
                mapping[v.name.trim().toLowerCase()] = item.sdName;
                mapping[v.name.trim().toUpperCase()] = item.sdName;
                mapping[v.name.trim()] = item.sdName;
              }
            }
          }
          setVillageSubDivMap(mapping);
        });
      })
      .catch(() => {});

    fetch(`${api}/awards?page=0&pageSize=100`, { credentials: "include" })
      .then((res) => (res.ok ? res.json() : null))
      .then((data) => {
        if (!active) return;
        if (data?.items) {
          setRawAwards(data.items);
          setError(null);
        }
        setLoading(false);
      })
      .catch((err) => {
        if (active) {
          setError(err?.message || "Failed to load awards register");
          setLoading(false);
        }
      });

    return () => {
      active = false;
    };
  }, []);

  // 2. Group Awards by Village (Safe, non-inflated grouping)
  const { villageGroups, multiVillageGroup, unlinkedAwards } = useMemo(() => {
    const groupsMap: Record<string, VillageAwardGroup> = {};
    const unlinked: AwardItem[] = [];
    const multiVillageList: AwardItem[] = [];

    for (const award of rawAwards) {
      const vRaw = award.villageNames?.trim();
      if (!vRaw || vRaw === "—") {
        unlinked.push(award);
        continue;
      }

      const vList = vRaw.split(",").map((s) => s.trim()).filter(Boolean);
      if (vList.length === 0) {
        unlinked.push(award);
        continue;
      }

      if (vList.length > 1) {
        multiVillageList.push(award);
        continue;
      }

      const singleName = vList[0];
      const matchKey = singleName.toLowerCase();
      const resolvedSubDiv = villageSubDivMap[matchKey] || villageSubDivMap[singleName.toUpperCase()];

      if (!resolvedSubDiv) {
        unlinked.push(award);
        continue;
      }

      if (!groupsMap[matchKey]) {
        groupsMap[matchKey] = {
          villageKey: matchKey,
          villageName: formatTitleCase(singleName),
          subDivisionName: resolvedSubDiv,
          awards: [],
        };
      }
      groupsMap[matchKey].awards.push(award);
    }

    const sortedGroups = Object.values(groupsMap).map((grp) => {
      const subDiv = grp.subDivisionName || villageSubDivMap[grp.villageKey];
      const sortedAwards = [...grp.awards].sort((a, b) => {
        if (a.awardDate && b.awardDate) return b.awardDate.localeCompare(a.awardDate);
        if (a.awardDate) return -1;
        if (b.awardDate) return 1;
        return a.awardNumber.localeCompare(b.awardNumber);
      });
      return {
        ...grp,
        subDivisionName: subDiv,
        awards: sortedAwards,
      };
    });

    sortedGroups.sort((a, b) => a.villageName.localeCompare(b.villageName));

    const multiGroup: VillageAwardGroup | null = multiVillageList.length > 0 ? {
      villageKey: "__multi_village__",
      villageName: "Multiple villages",
      subDivisionName: "Multi-Jurisdiction",
      isMultiVillage: true,
      awards: [...multiVillageList].sort((a, b) => {
        if (a.awardDate && b.awardDate) return b.awardDate.localeCompare(a.awardDate);
        if (a.awardDate) return -1;
        if (b.awardDate) return 1;
        return a.awardNumber.localeCompare(b.awardNumber);
      }),
    } : null;

    unlinked.sort((a, b) => {
      if (a.awardDate && b.awardDate) return b.awardDate.localeCompare(a.awardDate);
      if (a.awardDate) return -1;
      if (b.awardDate) return 1;
      return a.awardNumber.localeCompare(b.awardNumber);
    });

    return {
      villageGroups: sortedGroups,
      multiVillageGroup: multiGroup,
      unlinkedAwards: unlinked,
    };
  }, [rawAwards, villageSubDivMap]);

  // Default expansion: expand the first village group once loaded
  useEffect(() => {
    if (villageGroups.length > 0 && expandedVillages.size === 0) {
      setExpandedVillages(new Set([villageGroups[0].villageKey]));
    }
  }, [villageGroups]);

  // Toggle single village accordion
  const toggleVillage = (vKey: string) => {
    setExpandedVillages((prev) => {
      const next = new Set(prev);
      if (next.has(vKey)) next.delete(vKey);
      else next.add(vKey);
      return next;
    });
  };

  // Filter groups according to active search and selected sub-division
  const filteredVillageGroups = useMemo(() => {
    const q = searchTerm.trim().toLowerCase();
    return villageGroups.filter((group) => {
      if (selectedSubDivision !== "ALL") {
        if (selectedSubDivision === "MULTI") return false;
        if (group.subDivisionName !== selectedSubDivision) return false;
      }
      if (!q) return true;
      if (group.villageName.toLowerCase().includes(q)) return true;
      return group.awards.some(
        (a) =>
          a.awardNumber.toLowerCase().includes(q) ||
          (a.projectName && a.projectName.toLowerCase().includes(q)) ||
          (a.requiringAgency && a.requiringAgency.toLowerCase().includes(q))
      );
    });
  }, [villageGroups, searchTerm, selectedSubDivision]);

  // Expand / Collapse all
  const expandAll = () => {
    const all = new Set<string>();
    filteredVillageGroups.forEach((g) => all.add(g.villageKey));
    if (multiVillageGroup) all.add(multiVillageGroup.villageKey);
    if (unlinkedAwards.length > 0) all.add("__unlinked__");
    setExpandedVillages(all);
  };

  const collapseAll = () => {
    setExpandedVillages(new Set());
  };

  return (
    <div className="land-records-workspace">
      {/* Breadcrumb Hierarchy */}
      <nav aria-label="Breadcrumb" className="breadcrumbs">
        <Link to="/">Home</Link>
        <i>/</i>
        <Link to="/land-records">Land Records</Link>
        <i>/</i>
        <span>Awards</span>
      </nav>

      {/* Header Strip */}
      <div className="awards-header-strip">
        <div>
          <h1 className="land-overview-title">Awards</h1>
          <p className="land-overview-subtitle">South West Delhi</p>
        </div>

        <div className="land-overview-nav-links">
          <Link to="/land-records" className="secondary-button land-toolbar-link">
            <IconLand size={14} /> Overview
          </Link>
          <Link to="/villages" className="secondary-button land-toolbar-link">
            <IconLand size={14} /> Villages
          </Link>
        </div>
      </div>

      {/* Filter and Search Bar */}
      <div className="awards-filter-toolbar">
        {/* Segmented Sub-Division Filter */}
        <div className="subdivision-segmented-filter" role="tablist" aria-label="Filter awards by Sub-Division">
          <button
            type="button"
            className={`subdivision-filter-btn ${selectedSubDivision === "ALL" ? "active" : ""}`}
            onClick={() => setSelectedSubDivision("ALL")}
          >
            <span>All</span>
            <span className="subdivision-filter-count">{rawAwards.length}</span>
          </button>

          {subDivisions.map((sd) => {
            const count = villageGroups
              .filter((g) => g.subDivisionName === sd.name)
              .reduce((acc, g) => acc + g.awards.length, 0);
            return (
              <button
                key={sd.id}
                type="button"
                className={`subdivision-filter-btn ${selectedSubDivision === sd.name ? "active" : ""}`}
                onClick={() => setSelectedSubDivision(sd.name)}
              >
                <span>{sd.name}</span>
                <span className="subdivision-filter-count">{count}</span>
              </button>
            );
          })}

          {multiVillageGroup && (
            <button
              type="button"
              className={`subdivision-filter-btn ${selectedSubDivision === "MULTI" ? "active" : ""}`}
              onClick={() => setSelectedSubDivision("MULTI")}
            >
              <span>Multiple villages</span>
              <span className="subdivision-filter-count">{multiVillageGroup.awards.length}</span>
            </button>
          )}
        </div>

        {/* Search Input */}
        <div className="villages-search-box">
          <IconSearch size={15} className="lac-search-icon" />
          <input
            type="text"
            value={searchTerm}
            onChange={(e) => setSearchTerm(e.target.value)}
            placeholder="Filter awards or villages…"
            aria-label="Filter awards or villages"
          />
          {searchTerm && (
            <button
              type="button"
              onClick={() => setSearchTerm("")}
              className="search-clear-btn"
              title="Clear filter"
            >
              ✕
            </button>
          )}
        </div>

        {/* Expand / Collapse Controls */}
        <div className="awards-accordion-controls">
          <button type="button" onClick={expandAll} className="quiet-action-btn">
            Expand All
          </button>
          <span>·</span>
          <button type="button" onClick={collapseAll} className="quiet-action-btn">
            Collapse All
          </button>
        </div>
      </div>

      {/* Main Accordion List */}
      {loading ? (
        <div className="state loading" role="status">
          Loading awards…
        </div>
      ) : error ? (
        <div className="state error" role="alert">
          <strong>Error loading awards:</strong> {error}
        </div>
      ) : rawAwards.length === 0 ? (
        <div className="state empty">
          <strong>No awards registered</strong>
          <span>No awards are currently recorded in the system.</span>
        </div>
      ) : (
        <div className="awards-accordions-container">
          {/* 1. Single Village Groups */}
          {selectedSubDivision !== "MULTI" &&
            filteredVillageGroups.map((group) => {
              const isExpanded = expandedVillages.has(group.villageKey);
              return (
                <div key={group.villageKey} className="award-village-accordion">
                  {/* Header: ▼ Village Name | Sub-Division | N Awards */}
                  <button
                    type="button"
                    className="award-accordion-header"
                    onClick={() => toggleVillage(group.villageKey)}
                    aria-expanded={isExpanded}
                  >
                    <div className="award-accordion-title-area">
                      <span className={`award-accordion-chevron ${isExpanded ? "open" : ""}`}>
                        ▼
                      </span>
                      <h2 className="award-accordion-village">{group.villageName}</h2>
                      {group.subDivisionName && (
                        <span className={`subdivision-badge ${getSubDivisionToneClass(group.subDivisionName)}`}>
                          {group.subDivisionName}
                        </span>
                      )}
                    </div>

                    <div className="award-accordion-meta">
                      <span className="award-group-count">
                        {group.awards.length} {group.awards.length === 1 ? "Award" : "Awards"}
                      </span>
                    </div>
                  </button>

                  {/* Group Table: Award No. | Date | Status | Linked Khasras | Project / Agency | Open */}
                  {isExpanded && (
                    <div className="award-accordion-content">
                      <table className="award-dense-table">
                        <thead>
                          <tr>
                            <th scope="col" style={{ width: "20%" }}>Award No.</th>
                            <th scope="col" style={{ width: "16%" }}>Date</th>
                            <th scope="col" style={{ width: "14%" }}>Status</th>
                            <th scope="col" style={{ width: "16%" }}>Linked Khasras</th>
                            <th scope="col" style={{ width: "24%" }}>Project / Agency</th>
                            <th scope="col" style={{ width: "10%", textAlign: "right" }}>Action</th>
                          </tr>
                        </thead>
                        <tbody>
                          {group.awards.map((award) => (
                            <tr key={award.id}>
                              <td>
                                <Link to={`/awards/${award.id}`} className="award-link-bold">
                                  {award.awardNumber}
                                </Link>
                              </td>
                              <td>{formatDate(award.awardDate)}</td>
                              <td>
                                <span className={`status ${getAwardStatusTone(award.status)}`}>
                                  {award.status || "Draft"}
                                </span>
                              </td>
                              <td>
                                <span className="count-badge-khasra">
                                  {award.linkedKhasraCount > 0 ? award.linkedKhasraCount : "—"}
                                </span>
                              </td>
                              <td>
                                <div className="award-project-cell">
                                  <span className="award-project-name">{award.projectName || "—"}</span>
                                  {award.requiringAgency && (
                                    <span className="award-requiring-agency">{award.requiringAgency}</span>
                                  )}
                                </div>
                              </td>
                              <td style={{ textAlign: "right" }}>
                                <Link to={`/awards/${award.id}`} className="village-action-btn">
                                  <span>Open</span>
                                  <IconChevronRight size={13} />
                                </Link>
                              </td>
                            </tr>
                          ))}
                        </tbody>
                      </table>
                    </div>
                  )}
                </div>
              );
            })}

          {/* 2. Multi-Village Awards */}
          {(selectedSubDivision === "ALL" || selectedSubDivision === "MULTI") &&
            multiVillageGroup && (
              <div className="award-village-accordion multi-village-accordion">
                <button
                  type="button"
                  className="award-accordion-header"
                  onClick={() => toggleVillage(multiVillageGroup.villageKey)}
                  aria-expanded={expandedVillages.has(multiVillageGroup.villageKey)}
                >
                  <div className="award-accordion-title-area">
                    <span
                      className={`award-accordion-chevron ${
                        expandedVillages.has(multiVillageGroup.villageKey) ? "open" : ""
                      }`}
                    >
                      ▼
                    </span>
                    <h2 className="award-accordion-village">Multiple villages</h2>
                    <span className="subdivision-badge unassigned">Shared</span>
                  </div>

                  <div className="award-accordion-meta">
                    <span className="award-group-count">
                      {multiVillageGroup.awards.length}{" "}
                      {multiVillageGroup.awards.length === 1 ? "Award" : "Awards"}
                    </span>
                  </div>
                </button>

                {expandedVillages.has(multiVillageGroup.villageKey) && (
                  <div className="award-accordion-content">
                    <p className="award-group-note">
                      This Award covers more than one revenue village.
                    </p>
                    <table className="award-dense-table">
                      <thead>
                        <tr>
                          <th scope="col" style={{ width: "20%" }}>Award No.</th>
                          <th scope="col" style={{ width: "16%" }}>Date</th>
                          <th scope="col" style={{ width: "14%" }}>Status</th>
                          <th scope="col" style={{ width: "16%" }}>Linked Khasras</th>
                          <th scope="col" style={{ width: "24%" }}>Project / Agency</th>
                          <th scope="col" style={{ width: "10%", textAlign: "right" }}>Action</th>
                        </tr>
                      </thead>
                      <tbody>
                        {multiVillageGroup.awards.map((award) => (
                          <tr key={award.id}>
                            <td>
                              <Link to={`/awards/${award.id}`} className="award-link-bold">
                                {award.awardNumber}
                              </Link>
                              {award.villageNames && (
                                <div className="award-multi-village-tags">
                                  {award.villageNames.split(",").map((v, i) => (
                                    <span key={i} className="multi-village-chip">
                                      {formatTitleCase(v.trim())}
                                    </span>
                                  ))}
                                </div>
                              )}
                            </td>
                            <td>{formatDate(award.awardDate)}</td>
                            <td>
                              <span className={`status ${getAwardStatusTone(award.status)}`}>
                                {award.status || "Draft"}
                              </span>
                            </td>
                            <td>
                              <span className="count-badge-khasra">
                                {award.linkedKhasraCount > 0 ? award.linkedKhasraCount : "—"}
                              </span>
                            </td>
                            <td>
                              <div className="award-project-cell">
                                <span className="award-project-name">{award.projectName || "—"}</span>
                                {award.requiringAgency && (
                                  <span className="award-requiring-agency">{award.requiringAgency}</span>
                                )}
                              </div>
                            </td>
                            <td style={{ textAlign: "right" }}>
                              <Link to={`/awards/${award.id}`} className="village-action-btn">
                                <span>Open</span>
                                <IconChevronRight size={13} />
                              </Link>
                            </td>
                          </tr>
                        ))}
                      </tbody>
                    </table>
                  </div>
                )}
              </div>
            )}

          {/* 3. Unlinked Awards */}
          {(selectedSubDivision === "ALL" || selectedSubDivision === "UNLINKED") &&
            unlinkedAwards.length > 0 && (
              <div className="award-village-accordion unlinked-accordion">
                <button
                  type="button"
                  className="award-accordion-header"
                  onClick={() => toggleVillage("__unlinked__")}
                  aria-expanded={expandedVillages.has("__unlinked__")}
                >
                  <div className="award-accordion-title-area">
                    <span
                      className={`award-accordion-chevron ${
                        expandedVillages.has("__unlinked__") ? "open" : ""
                      }`}
                    >
                      ▼
                    </span>
                    <h2 className="award-accordion-village">Village link required</h2>
                    <span className="status warning">Action required</span>
                  </div>

                  <div className="award-accordion-meta">
                    <span className="award-group-count">
                      {unlinkedAwards.length} {unlinkedAwards.length === 1 ? "Award" : "Awards"}
                    </span>
                  </div>
                </button>

                {expandedVillages.has("__unlinked__") && (
                  <div className="award-accordion-content">
                    <table className="award-dense-table">
                      <thead>
                        <tr>
                          <th scope="col" style={{ width: "22%" }}>Award No.</th>
                          <th scope="col" style={{ width: "16%" }}>Date</th>
                          <th scope="col" style={{ width: "14%" }}>Status</th>
                          <th scope="col" style={{ width: "16%" }}>Linked Khasras</th>
                          <th scope="col" style={{ width: "20%" }}>Project / Agency</th>
                          <th scope="col" style={{ width: "12%", textAlign: "right" }}>Action</th>
                        </tr>
                      </thead>
                      <tbody>
                        {unlinkedAwards.map((award) => (
                          <tr key={award.id}>
                            <td>
                              <Link to={`/awards/${award.id}`} className="award-link-bold">
                                {award.awardNumber}
                              </Link>
                            </td>
                            <td>{formatDate(award.awardDate)}</td>
                            <td>
                              <span className={`status ${getAwardStatusTone(award.status)}`}>
                                {award.status || "Draft"}
                              </span>
                            </td>
                            <td>
                              <span className="count-badge-khasra">
                                {award.linkedKhasraCount > 0 ? award.linkedKhasraCount : "—"}
                              </span>
                            </td>
                            <td>
                              <div className="award-project-cell">
                                <span className="award-project-name">{award.projectName || "—"}</span>
                                {award.requiringAgency && (
                                  <span className="award-requiring-agency">{award.requiringAgency}</span>
                                )}
                              </div>
                            </td>
                            <td style={{ textAlign: "right" }}>
                              <Link to={`/awards/${award.id}`} className="village-action-btn">
                                <span>Link village</span>
                                <IconChevronRight size={13} />
                              </Link>
                            </td>
                          </tr>
                        ))}
                      </tbody>
                    </table>
                  </div>
                )}
              </div>
            )}
        </div>
      )}
    </div>
  );
};
