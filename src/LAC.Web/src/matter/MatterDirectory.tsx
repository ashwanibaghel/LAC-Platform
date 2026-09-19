import React, { useState, useEffect, useCallback } from "react";
import { Link, useNavigate } from "react-router-dom";
import { useAuth } from "../auth/AuthProvider";
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
  const [villages, setVillages] = useState<VillageOption[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  // New Matter Modal State
  const [showCreateModal, setShowCreateModal] = useState(false);
  const [createTitle, setCreateTitle] = useState("");
  const [createVillageId, setCreateVillageId] = useState("");
  const [createWorkstreamId, setCreateWorkstreamId] = useState("");
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
          if (data.workstreams.length > 0 && !createWorkstreamId) {
            setCreateWorkstreamId(data.workstreams[0].id);
          }
        }
      })
      .catch(() => {});

    fetch("/api/villages", { credentials: "include" })
      .then((res) => (res.ok ? res.json() : null))
      .then((data) => {
        if (Array.isArray(data)) {
          setVillages(data);
          if (data.length > 0 && !createVillageId) {
            setCreateVillageId(data[0].id);
          }
        }
      })
      .catch(() => {});
  }, [createVillageId, createWorkstreamId]);

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

  const handleCreateSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!createTitle.trim()) {
      setCreateError("Matter title is required.");
      return;
    }
    if (!createVillageId) {
      setCreateError("Please select an official Village.");
      return;
    }
    if (!createWorkstreamId) {
      setCreateError("Workstream is mandatory for all new matters.");
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
      navigate(`/matters/${created.id}`);
    } catch (err: any) {
      setCreateError(err.message || "Could not create matter.");
    } finally {
      setCreating(false);
    }
  };

  const totalPages = Math.ceil(totalCount / pageSize);

  return (
    <div className="section">
      <div className="matter-directory-header">
        <div>
          <h2>Matters Directory</h2>
          <span className="hint">Secure workspace for legal, compensation, and administrative case files.</span>
        </div>
        {(hasPermission("Matter.Create") || hasPermission("Matter.Edit")) && (
          <button className="primary" onClick={() => setShowCreateModal(true)}>
            + New Matter
          </button>
        )}
      </div>

      {/* Filters Bar */}
      <form onSubmit={handleSearchSubmit} className="matter-filters-bar">
        <input
          type="text"
          className="matter-search-input"
          placeholder="Search by title, reference no, khasra, or village..."
          value={q}
          onChange={(e) => setQ(e.target.value)}
        />
        <button type="submit">Search</button>

        <select
          className="matter-filter-select"
          value={workstreamFilter}
          onChange={(e) => {
            setWorkstreamFilter(e.target.value);
            setPage(1);
          }}
        >
          <option value="">All Workstreams</option>
          <option value="UNCLASSIFIED">[Unclassified]</option>
          {workstreams.map((w) => (
            <option key={w.id} value={w.id}>
              {w.name} ({w.code})
            </option>
          ))}
        </select>

        <select
          className="matter-filter-select"
          value={status}
          onChange={(e) => {
            setStatus(e.target.value);
            setPage(1);
          }}
        >
          <option value="all">All Statuses</option>
          <option value="Open">Open</option>
          <option value="Archived">Archived</option>
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
        >
          <option value="updatedat:desc">Recently Updated</option>
          <option value="createdat:desc">Newest First</option>
          <option value="title:asc">Title (A-Z)</option>
          <option value="referencenumber:asc">Reference No</option>
          <option value="village:asc">Village (A-Z)</option>
        </select>
      </form>

      {error && <div className="state error">{error}</div>}

      {loading ? (
        <div className="state loading">Loading matters...</div>
      ) : items.length === 0 ? (
        <div className="state empty">
          <strong>No matters found.</strong>
          <span>Try adjusting your search criteria or workstream filter.</span>
        </div>
      ) : (
        <>
          <table className="data-table">
            <thead>
              <tr>
                <th>Title</th>
                <th>Reference No</th>
                <th>Type</th>
                <th>Workstream</th>
                <th>Status</th>
                <th>Village</th>
                <th>Documents</th>
                <th>Updated</th>
              </tr>
            </thead>
            <tbody>
              {items.map((m) => (
                <tr key={m.id}>
                  <td>
                    <Link to={`/matters/${m.id}`}>
                      <strong>{m.title}</strong>
                    </Link>
                  </td>
                  <td>{m.referenceNumber || "—"}</td>
                  <td>{m.matterType}</td>
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
                  <td>{m.villageName}</td>
                  <td>{m.documentCount}</td>
                  <td>{new Date(m.updatedAt).toLocaleDateString()}</td>
                </tr>
              ))}
            </tbody>
          </table>

          {/* Pagination */}
          {totalPages > 1 && (
            <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center", marginTop: 16 }}>
              <span>
                Showing {items.length} of {totalCount} matters (Page {page} of {totalPages})
              </span>
              <div style={{ display: "flex", gap: 8 }}>
                <button disabled={page <= 1} onClick={() => setPage((p) => Math.max(1, p - 1))}>
                  Previous
                </button>
                <button disabled={page >= totalPages} onClick={() => setPage((p) => p + 1)}>
                  Next
                </button>
              </div>
            </div>
          )}
        </>
      )}

      {/* Create Matter Modal */}
      {showCreateModal && (
        <div className="matter-modal-overlay">
          <div className="matter-modal-card">
            <h3 className="matter-modal-title">Create Official Matter</h3>
            {createError && <p style={{ color: "#b91c1c", marginBottom: 12 }}>{createError}</p>}
            <form onSubmit={handleCreateSubmit}>
              <div className="field-grid">
                <label>
                  Village *
                  <select value={createVillageId} onChange={(e) => setCreateVillageId(e.target.value)} required>
                    {villages.map((v) => (
                      <option key={v.id} value={v.id}>
                        {v.name}
                      </option>
                    ))}
                  </select>
                </label>

                <label>
                  Workstream *
                  <select value={createWorkstreamId} onChange={(e) => setCreateWorkstreamId(e.target.value)} required>
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
                    <option>Court Case</option>
                    <option>Compensation</option>
                    <option>Land Acquisition</option>
                    <option>Demarcation</option>
                    <option>Possession</option>
                    <option>General</option>
                    <option>Other</option>
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
                  Remarks
                  <textarea
                    value={createRemarks}
                    onChange={(e) => setCreateRemarks(e.target.value)}
                    placeholder="Administrative notes or context..."
                    rows={3}
                  />
                </label>
              </div>

              <div className="matter-modal-actions">
                <button type="button" className="quiet-button" onClick={() => setShowCreateModal(false)}>
                  Cancel
                </button>
                <button type="submit" disabled={creating || !createTitle.trim()}>
                  {creating ? "Creating..." : "Create Matter"}
                </button>
              </div>
            </form>
          </div>
        </div>
      )}
    </div>
  );
};
