import React, { useState, useEffect } from "react";
import { Link, useNavigate } from "react-router-dom";
import { IconLand, IconSearch, IconChevronRight } from "../components/Icons";
import "./land.css";

const api = "/api";

interface VillageItem {
  id: string;
  name: string;
  khasraCount: number;
  subDivisionName?: string;
  districtName?: string;
}

interface PageData {
  items: VillageItem[];
  page: number;
  pageSize: number;
  totalCount: number;
}

export const VillageDirectory: React.FC = () => {
  const navigate = useNavigate();
  const [page, setPage] = useState(0);
  const [query, setQuery] = useState("");
  const [delayedQuery, setDelayedQuery] = useState("");
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [data, setData] = useState<PageData | null>(null);

  // Debounce search query
  useEffect(() => {
    const timer = setTimeout(() => {
      setDelayedQuery(query.trim());
      setPage(0);
    }, 250);
    return () => clearTimeout(timer);
  }, [query]);

  // Fetch villages
  useEffect(() => {
    let active = true;
    setLoading(true);
    setError(null);

    const params = new URLSearchParams({
      page: page.toString(),
      pageSize: "25",
    });
    if (delayedQuery) params.set("q", delayedQuery);

    fetch(`${api}/villages?${params.toString()}`, { credentials: "include" })
      .then(async (res) => {
        if (!res.ok) throw new Error("Failed to load village directory.");
        return res.json() as Promise<PageData>;
      })
      .then((resData) => {
        if (active) {
          setData(resData);
          setLoading(false);
        }
      })
      .catch((err) => {
        if (active) {
          setError(err?.message || "Could not fetch villages.");
          setLoading(false);
        }
      });

    return () => {
      active = false;
    };
  }, [page, delayedQuery]);

  const lastPage = data ? Math.max(0, Math.ceil(data.totalCount / data.pageSize) - 1) : 0;

  return (
    <div className="land-records-page">
      {/* Header */}
      <div className="land-header-strip">
        <div className="land-title-area">
          <h1>Village Directory</h1>
          <p>Canonical village directory and land acquisition record entry points.</p>
        </div>

        <div className="lac-header-search-box" style={{ width: "260px" }}>
          <IconSearch size={15} className="lac-search-icon" />
          <input
            value={query}
            onChange={(e) => setQuery(e.target.value)}
            placeholder="Search village name…"
          />
        </div>
      </div>

      {/* Directory Table */}
      <div className="table-wrap">
        <table>
          <thead>
            <tr>
              <th scope="col">Village Name</th>
              <th scope="col">Khasras Count</th>
              <th scope="col">Action</th>
            </tr>
          </thead>
          <tbody>
            {loading && (
              <tr>
                <td colSpan={3} className="text-center py-6 text-slate-500">
                  Loading village records…
                </td>
              </tr>
            )}

            {!loading && error && (
              <tr>
                <td colSpan={3} className="text-center py-6 text-red-600">
                  {error}
                </td>
              </tr>
            )}

            {!loading && !error && data?.items.length === 0 && (
              <tr>
                <td colSpan={3} className="text-center py-6 text-slate-500">
                  {delayedQuery ? "No villages match your search query." : "No villages available."}
                </td>
              </tr>
            )}

            {!loading &&
              !error &&
              data?.items.map((village) => (
                <tr
                  key={village.id}
                  onClick={() => navigate(`/villages/${village.id}`)}
                  style={{ cursor: "pointer" }}
                >
                  <td>
                    <Link
                      to={`/villages/${village.id}`}
                      className="entity-link"
                      style={{ fontWeight: 700 }}
                      onClick={(e) => e.stopPropagation()}
                    >
                      {village.name}
                    </Link>
                  </td>
                  <td>
                    <span style={{ fontWeight: 600 }}>{village.khasraCount}</span> khasras
                  </td>
                  <td>
                    <Link
                      to={`/villages/${village.id}`}
                      className="text-action"
                      style={{ display: "inline-flex", alignItems: "center", gap: "4px" }}
                      onClick={(e) => e.stopPropagation()}
                    >
                      <span>Open Workspace</span>
                      <IconChevronRight size={14} />
                    </Link>
                  </td>
                </tr>
              ))}
          </tbody>
        </table>
      </div>

      {/* Pagination */}
      {data && data.totalCount > data.pageSize && (
        <div className="pagination">
          <span>
            Showing {data.page * data.pageSize + 1}–
            {Math.min((data.page + 1) * data.pageSize, data.totalCount)} of {data.totalCount} villages
          </span>
          <div>
            <button disabled={page === 0} onClick={() => setPage(page - 1)}>
              Previous
            </button>
            <button disabled={page >= lastPage} onClick={() => setPage(page + 1)}>
              Next
            </button>
          </div>
        </div>
      )}
    </div>
  );
};
