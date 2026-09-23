import React, { useState, useEffect, useCallback } from "react";
import { Link, useNavigate } from "react-router-dom";
import { useAuth } from "../auth/AuthProvider";
import {
  IconSearch,
  IconPlus,
  IconClose,
  IconBuilding,
  IconMatters,
  IconFilter
} from "../components/Icons";
import "./matter.css";

interface WorkstreamOption {
  id: string;
  name: string;
  code: string;
}

interface VillageOption {
  id: string;
  name: string;
}

interface MatterListItem {
  id: string;
  villageId: string;
  villageName: string;
  workstreamId: string | null;
  workstreamName: string | null;
  workstreamCode: string | null;
  title: string;
  matterType: string;
  status: string;
  referenceNumber: string | null;
  remarks: string | null;
  khasraReferenceText: string | null;
  revision: number;
  createdAt: string;
  updatedAt: string;
  documentCount: number;
  draftCount: number;
}

interface MatterListResponse {
  items: MatterListItem[];
  totalCount: number;
  page: number;
  pageSize: number;
}

export const MatterDirectory: React.FC = () => {
  const navigate = useNavigate();
  const { hasPermission } = useAuth();

  const [items, setItems] = useState<MatterListItem[]>([]);
  const [totalCount, setTotalCount] = useState(0);
  const [page, setPage] = useState(1);
  const [pageSize] = useState(20);
  const [q, setQ] = useState("");
  const [searchTerm, setSearchTerm] = useState("");
  const [status, setStatus] = useState<string>("all");
  const [workstreamFilter, setWorkstreamFilter] = useState<string>("");
  const [sortBy, setSortBy] = useState<string>("updatedat");
  const [sortDesc, setSortDesc] = useState<boolean>(true);
  const [workstreams, setWorkstreams] = useState<WorkstreamOption[]>([]);
  const [matterTypes, setMatterTypes] = useState<string[]>([]);
  const [villages, setVillages] = useState<VillageOption[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  // New Matter Modal State
  const [showCreateModal, setShowCreateModal] = useState(false);
  const [createTitle, setCreateTitle] = useState("");
  const [createVillageId, setCreateVillageId] = useState(""); // Intentional explicit selection
  const [createWorkstreamId, setCreateWorkstreamId] = useState(""); // Intentional explicit selection
  const [createMatterType, setCreateMatterType] = useState("Court Case");
  const [createRefNo, setCreateRefNo] = useState("");
  const [createKhasraRef, setCreateKhasraRef] = useState("");
  const [createRemarks, setCreateRemarks] = useState("");
  const [createError, setCreateError] = useState<string | null>(null);
  const [creating, setCreating] = useState(false);

  // Fetch Lookups
  useEffect(() => {
    fetch("/api/matters/context", { credentials: "include" })
      .then((res) => (res.ok ? res.json() : null))
      .then((data) => {
        if (data?.workstreams) {
          setWorkstreams(data.workstreams);
        }
        if (Array.isArray(data?.matterTypes) && data.matterTypes.length > 0) {
          setMatterTypes(data.matterTypes);
          if (data.matterTypes.includes("Court Case")) {
            setCreateMatterType("Court Case");
          } else {
            setCreateMatterType(data.matterTypes[0]);
          }
        }
      })
      .catch(() => {});

    fetch("/api/villages", { credentials: "include" })
      .then((res) => (res.ok ? res.json() : null))
      .then((data) => {
        if (Array.isArray(data)) {
          setVillages(data);
        }
      })
      .catch(() => {});
  }, []);

  const loadMatters = useCallback(async () => {
    try {
      setLoading(true);
      setError(null);
      const params = new URLSearchParams();
      params.append("page", page.toString());
      params.append("pageSize", pageSize.toString());
      if (searchTerm.trim()) params.append("q", searchTerm.trim());
      if (status && status !== "all") params.append("status", status);
      if (workstreamFilter) {
        if (workstreamFilter === "UNCLASSIFIED") {
          params.append("workstream", "UNCLASSIFIED");
        } else {
          params.append("workstreamId", workstreamFilter);
        }
      }
      params.append("sortBy", sortBy);
      params.append("sortDesc", sortDesc.toString());

      const res = await fetch(`/api/matters?${params.toString()}`, { credentials: "include" });
      if (!res.ok) {
        if (res.status === 403) throw new Error("Access denied: You do not have permission to view matters.");
        throw new Error("Failed to load matters.");
      }
      const data: MatterListResponse = await res.json();
      setItems(data.items);
      setTotalCount(data.totalCount);
    } catch (err: any) {
      setError(err.message || "An error occurred while loading matters.");
      setItems([]);
      setTotalCount(0);
    } finally {
      setLoading(false);
    }
  }, [page, pageSize, searchTerm, status, workstreamFilter, sortBy, sortDesc]);

  useEffect(() => {
    void loadMatters();
  }, [loadMatters]);

  const handleSearchSubmit = (e: React.FormEvent) => {
    e.preventDefault();
    setPage(1);
    setSearchTerm(q);
  };

  const handleClearFilters = () => {
    setQ("");
    setSearchTerm("");
    setStatus("all");
    setWorkstreamFilter("");
    setSortBy("updatedat");
    setSortDesc(true);
    setPage(1);
  };

  const handleCreateSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!createVillageId) {
      setCreateError("Please explicitly select an official Village.");
      return;
    }
    if (!createWorkstreamId) {
      setCreateError("Please explicitly select a Workstream.");
      return;
    }
    if (!createTitle.trim()) {
      setCreateError("Matter title is required.");
      return;
    }

    try {
      setCreating(true);
      setCreateError(null);
      const res = await fetch("/api/matters", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        credentials: "include",
        body: JSON.stringify({
          villageId: createVillageId,
          workstreamId: createWorkstreamId,
          title: createTitle.trim(),
          matterType: createMatterType,
          referenceNumber: createRefNo.trim() || null,
          remarks: createRemarks.trim() || null,
          khasraReferenceText: createKhasraRef.trim() || null
        })
      });

      if (!res.ok) {
        const errorData = await res.json().catch(() => null);
        throw new Error(errorData?.message || errorData?.title || "Failed to create matter.");
      }

      const created = await res.json();
      setShowCreateModal(false);
      setCreateTitle("");
      setCreateVillageId("");
      setCreateWorkstreamId("");
      setCreateRefNo("");
      setCreateKhasraRef("");
      setCreateRemarks("");
      navigate(`/matters/${created.id}`);
    } catch (err: any) {
      setCreateError(err.message || "Could not create matter.");
    } finally {
      setCreating(false);
    }
  };

  const totalPages = Math.ceil(totalCount / pageSize);
  const startCount = totalCount === 0 ? 0 : (page - 1) * pageSize + 1;
  const endCount = Math.min(page * pageSize, totalCount);
  const hasActiveFilters = Boolean(searchTerm.trim() || status !== "all" || workstreamFilter);

  // Operational-readiness fix: Matter.Create only required for creation
  const canCreate = hasPermission("Matter.Create");

  return (
    <div className="matter-directory-container">
      {/* Header Bar */}
      <div className="matter-directory-header">
        <div>
          <h2>Matters</h2>
          <span className="hint">Official legal, compensation, and administrative case directory.</span>
        </div>
        {canCreate && (
          <button
            type="button"
            className="btn-action-primary"
            onClick={() => {
              setCreateError(null);
              setShowCreateModal(true);
            }}
          >
            <IconPlus size={16} />
            + New Matter
          </button>
        )}
      </div>

      {/* Command & Filters Bar */}
      <form onSubmit={handleSearchSubmit} className="matter-filters-bar">
        <div className="matter-search-wrap">
          <IconSearch size={16} className="matter-search-icon" />
          <input
            type="text"
            className="matter-search-input"
            placeholder="Search by title, reference no, khasra, or village..."
            value={q}
            onChange={(e) => setQ(e.target.value)}
          />
        </div>

        <select
          className="matter-filter-select"
          value={workstreamFilter}
          onChange={(e) => {
            setWorkstreamFilter(e.target.value);
            setPage(1);
          }}
          aria-label="Filter by workstream"
        >
          <option value="">All Workstreams</option>
          <option value="UNCLASSIFIED">[Unclassified]</option>
          {workstreams.map((w) => (
            <option key={w.id} value={w.id}>
              {w.name} ({w.code})
            </option>
          ))}
        </select>

        {/* TODO: Re-introduce "Archived" status filter when backend list/read surfaces support browsing archived records (currently backend queries query RecordStatus.Active only). */}
        <select
          className="matter-filter-select"
          value={status}
          onChange={(e) => {
            setStatus(e.target.value);
            setPage(1);
          }}
          aria-label="Filter by status"
        >
          <option value="all">All Statuses</option>
          <option value="Open">Open</option>
        </select>

        <select
          className="matter-filter-select"
          value={`${sortBy}:${sortDesc ? "desc" : "asc"}`}
          onChange={(e) => {
            const [sb, sd] = e.target.value.split(":");
            setSortBy(sb);
            setSortDesc(sd === "desc");
            setPage(1);
          }}
          aria-label="Sort options"
        >
          <option value="updatedat:desc">Recently Updated</option>
          <option value="createdat:desc">Newest First</option>
          <option value="title:asc">Title (A-Z)</option>
          <option value="referencenumber:asc">Reference No</option>
          <option value="village:asc">Village (A-Z)</option>
        </select>

        <button type="submit" className="matter-filter-btn">
          <IconFilter size={14} />
          Filter
        </button>

        {hasActiveFilters && (
          <button
            type="button"
            className="btn-action-secondary"
            onClick={handleClearFilters}
            style={{ padding: "6px 10px", fontSize: "12px" }}
          >
            Clear
          </button>
        )}
      </form>

      {error && <div className="state error">{error}</div>}

      {loading ? (
        <div className="state loading">Loading matters directory...</div>
      ) : items.length === 0 ? (
        <div className="state empty">
          <IconMatters size={32} style={{ color: "#94a3b8", marginBottom: 8 }} />
          <strong>No matters found</strong>
          <span>Adjust your search keywords or workstream filters.</span>
        </div>
      ) : (
        <>
          {/* Dense Operational Table */}
          <table className="matter-directory-table">
            <thead>
              <tr>
                <th style={{ width: "30%" }}>Matter Title</th>
                <th style={{ width: "14%" }}>Reference No</th>
                <th style={{ width: "11%" }}>Type</th>
                <th style={{ width: "13%" }}>Village</th>
                <th style={{ width: "14%" }}>Workstream</th>
                <th style={{ width: "6%" }}>Status</th>
                <th style={{ width: "12%" }}>Work</th>
              </tr>
            </thead>
            <tbody>
              {items.map((m) => (
                <tr
                  key={m.id}
                  className="matter-table-row"
                  onClick={() => navigate(`/matters/${m.id}`)}
                  tabIndex={0}
                  onKeyDown={(e) => {
                    if (e.key === "Enter") navigate(`/matters/${m.id}`);
                  }}
                >
                  <td>
                    <div className="matter-identity-cell">
                      <Link
                        to={`/matters/${m.id}`}
                        className="matter-identity-title"
                        onClick={(e) => e.stopPropagation()}
                      >
                        {m.title}
                      </Link>
                      {m.khasraReferenceText && (
                        <span className="matter-identity-sub" title={m.khasraReferenceText}>
                          Khasra: {m.khasraReferenceText}
                        </span>
                      )}
                    </div>
                  </td>
                  <td>
                    <span style={{ fontWeight: 500, fontFamily: "monospace", fontSize: "12px" }}>
                      {m.referenceNumber || "—"}
                    </span>
                  </td>
                  <td>
                    <span style={{ fontSize: "12px", color: "#475569" }}>{m.matterType}</span>
                  </td>
                  <td>
                    <span style={{ fontWeight: 500, color: "#334155" }}>{m.villageName}</span>
                  </td>
                  <td>
                    {m.workstreamName ? (
                      <span className="matter-badge-workstream" title={`Code: ${m.workstreamCode}`}>
                        {m.workstreamName}
                      </span>
                    ) : (
                      <span className="matter-badge-unclassified" title="Legacy unclassified matter">
                        [Unclassified]
                      </span>
                    )}
                  </td>
                  <td>
                    <span className={m.status === "Archived" ? "matter-badge-archived" : "matter-badge-open"}>
                      {m.status}
                    </span>
                  </td>
                  <td>
                    <span style={{ fontSize: "12px", fontWeight: 600, color: "#475569" }}>
                      {m.documentCount} docs · {m.draftCount} drafts
                    </span>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>

          {/* Compact Pagination */}
          <div className="matter-pagination-bar">
            <span>
              Showing <strong>{startCount}–{endCount}</strong> of <strong>{totalCount}</strong> matters
            </span>
            <div className="matter-pagination-actions">
              <button
                type="button"
                className="matter-pagination-btn"
                disabled={page <= 1}
                onClick={() => setPage((p) => Math.max(1, p - 1))}
              >
                Previous
              </button>
              <button
                type="button"
                className="matter-pagination-btn"
                disabled={page >= totalPages}
                onClick={() => setPage((p) => p + 1)}
              >
                Next
              </button>
            </div>
          </div>
        </>
      )}

      {/* New Matter Modal */}
      {showCreateModal && (
        <div className="matter-modal-overlay" onClick={() => setShowCreateModal(false)}>
          <div className="matter-modal-card" onClick={(e) => e.stopPropagation()}>
            <div className="matter-modal-header">
              <div>
                <h3 className="matter-modal-title">Create Official Matter</h3>
                <span className="hint">Register a new case file into official village & workstream context.</span>
              </div>
              <button
                type="button"
                style={{ border: "none", background: "transparent", cursor: "pointer", color: "#64748b" }}
                onClick={() => setShowCreateModal(false)}
                title="Close dialog"
              >
                <IconClose size={18} />
              </button>
            </div>

            {createError && (
              <div
                style={{
                  background: "#fef2f2",
                  border: "1px solid #fecaca",
                  color: "#b91c1c",
                  padding: "8px 12px",
                  borderRadius: "6px",
                  marginBottom: 14,
                  fontSize: "13px"
                }}
              >
                {createError}
              </div>
            )}

            <form onSubmit={handleCreateSubmit}>
              <div className="field-grid">
                <label>
                  Village *
                  <select
                    value={createVillageId}
                    onChange={(e) => setCreateVillageId(e.target.value)}
                    required
                  >
                    <option value="" disabled>
                      Select Official Village...
                    </option>
                    {villages.map((v) => (
                      <option key={v.id} value={v.id}>
                        {v.name}
                      </option>
                    ))}
                  </select>
                </label>

                <label>
                  Workstream *
                  <select
                    value={createWorkstreamId}
                    onChange={(e) => setCreateWorkstreamId(e.target.value)}
                    required
                  >
                    <option value="" disabled>
                      Select Workstream...
                    </option>
                    {workstreams.map((w) => (
                      <option key={w.id} value={w.id}>
                        {w.name} ({w.code})
                      </option>
                    ))}
                  </select>
                </label>

                <label style={{ gridColumn: "span 2" }}>
                  Matter Title *
                  <input
                    type="text"
                    value={createTitle}
                    onChange={(e) => setCreateTitle(e.target.value)}
                    placeholder="e.g. WP (C) 1042/2026 Ram Lal vs Union of India"
                    required
                  />
                </label>

                <label>
                  Matter Type *
                  <select value={createMatterType} onChange={(e) => setCreateMatterType(e.target.value)}>
                    {(matterTypes.length > 0 ? matterTypes : ["Court Case", "Compensation", "Land Acquisition", "General", "Other"]).map((mt) => (
                      <option key={mt} value={mt}>
                        {mt}
                      </option>
                    ))}
                  </select>
                </label>

                <label>
                  Reference Number
                  <input
                    type="text"
                    value={createRefNo}
                    onChange={(e) => setCreateRefNo(e.target.value)}
                    placeholder="e.g. LAC/SEC/2026/89"
                  />
                </label>

                <label style={{ gridColumn: "span 2" }}>
                  Optional Khasra Reference
                  <input
                    type="text"
                    value={createKhasraRef}
                    onChange={(e) => setCreateKhasraRef(e.target.value)}
                    placeholder="e.g. Khasra No. 12/4, 12/5 min"
                  />
                </label>

                <label style={{ gridColumn: "span 2" }}>
                  Remarks / Administrative Notes
                  <textarea
                    value={createRemarks}
                    onChange={(e) => setCreateRemarks(e.target.value)}
                    placeholder="Administrative background or context notes..."
                    rows={3}
                  />
                </label>
              </div>

              <div className="matter-modal-actions">
                <button
                  type="button"
                  className="btn-action-secondary"
                  onClick={() => setShowCreateModal(false)}
                >
                  Cancel
                </button>
                <button
                  type="submit"
                  className="btn-action-primary"
                  disabled={creating || !createTitle.trim() || !createVillageId || !createWorkstreamId}
                >
                  {creating ? "Creating Matter..." : "Create Matter"}
                </button>
              </div>
            </form>
          </div>
        </div>
      )}
    </div>
  );
};
