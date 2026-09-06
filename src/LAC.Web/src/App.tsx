import { Fragment, useEffect, useRef, useState } from "react";
import type { FormEvent, ReactNode } from "react";
import {
  BrowserRouter,
  Link,
  NavLink,
  Route,
  Routes,
  useNavigate,
  useParams,
  useSearchParams,
} from "react-router-dom";
import { ExportMenu } from "./components/ExportMenu";
import "./index.css";
import "./sidebar.css";
import "./verification.css";

const api = "/api";
type Page<T> = {
  items: T[];
  page: number;
  pageSize: number;
  totalCount: number;
};
type State<T> = { loading: boolean; data?: T; error?: string };

type CachedApiResponse = { expiresAt: number; data: unknown };
const apiCache = new Map<string, CachedApiResponse>();
const apiCachePrefix = "lac-platform:api-cache:v1:";
const apiCacheTtlMs = 5 * 60 * 1000;
const apiRequestTimeoutMs = 8_000;

// Explicit local recovery clears only this application's cached API responses and job pointer.
if (new URLSearchParams(window.location.search).get("resetCache") === "1") {
  try {
    for (const key of Object.keys(sessionStorage)) {
      if (key.startsWith("lac-platform:api-cache:") || key.startsWith("lac.awardPdfJob.")) sessionStorage.removeItem(key);
    }
    const url = new URL(window.location.href);
    url.searchParams.delete("resetCache");
    window.history.replaceState(null, "", url);
  } catch { /* Recovery remains optional when browser storage is unavailable. */ }
}

function readCachedResponse<T>(requestPath?: string): T | undefined {
  if (!requestPath) return undefined;
  const memory = apiCache.get(requestPath);
  if (memory && memory.expiresAt > Date.now()) return memory.data as T;
  if (memory) apiCache.delete(requestPath);
  try {
    const stored = sessionStorage.getItem(apiCachePrefix + requestPath);
    if (!stored) return undefined;
    const parsed = JSON.parse(stored) as CachedApiResponse;
    if (parsed.expiresAt <= Date.now()) {
      sessionStorage.removeItem(apiCachePrefix + requestPath);
      return undefined;
    }
    apiCache.set(requestPath, parsed);
    return parsed.data as T;
  } catch {
    return undefined;
  }
}

function cacheResponse<T>(requestPath: string, data: T) {
  const entry: CachedApiResponse = {
    expiresAt: Date.now() + apiCacheTtlMs,
    data,
  };
  apiCache.set(requestPath, entry);
  try {
    sessionStorage.setItem(apiCachePrefix + requestPath, JSON.stringify(entry));
  } catch {
    /* Cache is optional when browser storage is unavailable. */
  }
}

function clearApiCache() {
  apiCache.clear();
  try {
    for (let index = sessionStorage.length - 1; index >= 0; index--) {
      const key = sessionStorage.key(index);
      if (key?.startsWith(apiCachePrefix)) sessionStorage.removeItem(key);
    }
  } catch {
    /* Cache invalidation is best-effort only. */
  }
}

function useApi<T>(path?: string): State<T> {
  const [state, setState] = useState<State<T>>(() => {
    const cached = readCachedResponse<T>(path);
    return cached === undefined
      ? { loading: Boolean(path) }
      : { loading: false, data: cached };
  });
  useEffect(() => {
    if (!path) return;
    const controller = new AbortController();
    let timedOut = false;
    const timeout = window.setTimeout(() => { timedOut = true; controller.abort(); }, apiRequestTimeoutMs);
    const cached = readCachedResponse<T>(path);
    if (cached !== undefined) setState({ loading: false, data: cached });
    else setState({ loading: true });
    fetch(api + path, { signal: controller.signal })
      .then(async (response) => {
        if (!response.ok)
          throw new Error(
            (await response.json().catch(() => null))?.title ||
              `Request failed (${response.status})`,
          );
        return response.json() as Promise<T>;
      })
      .then((data) => {
        cacheResponse(path, data);
        setState({ loading: false, data });
      })
      .catch((error) => {
        if (cached === undefined) {
          setState({ loading: false, error: timedOut ? "The data service is taking too long to respond. Please try again." : error.message });
        }
      })
      .finally(() => {
        window.clearTimeout(timeout);
      });
    return () => { window.clearTimeout(timeout); controller.abort(); };
  }, [path]);
  return state;
}

async function post<T>(path: string, body: unknown): Promise<T> {
  const response = await fetch(api + path, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(body),
  });
  if (!response.ok)
    throw new Error(
      (await response.json().catch(() => null))?.title ||
        "Could not save the record.",
    );
  const data =
    response.status === 204 ? ({} as T) : ((await response.json()) as T);
  clearApiCache();
  return data;
}
async function put<T>(path: string, body: unknown): Promise<T> {
  const response = await fetch(api + path, {
    method: "PUT",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(body),
  });
  if (!response.ok)
    throw new Error(
      (await response.json().catch(() => null))?.detail ||
        "Could not update the LR row.",
    );
  const data =
    response.status === 204 ? ({} as T) : ((await response.json()) as T);
  clearApiCache();
  return data;
}
async function upload<T>(path: string, file: File): Promise<T> {
  const form = new FormData();
  form.append("file", file);
  const response = await fetch(api + path, { method: "POST", body: form });
  if (!response.ok)
    throw new Error(
      (await response.json().catch(() => null))?.detail ||
        "Could not read the workbook.",
    );
  const data = (await response.json()) as T;
  clearApiCache();
  return data;
}
async function uploadAwardPdf<T>(targetAwardId: string | undefined, selectedVillageId: string | undefined, file: File): Promise<T> {
  const form = new FormData(); form.append("file", file);
  const query = new URLSearchParams(); if (targetAwardId) query.set("targetAwardId", targetAwardId); if (selectedVillageId) query.set("selectedVillageId", selectedVillageId);
  const response = await fetch(`${api}/award-pdf-extractions${query.size ? `?${query}` : ""}`, { method: "POST", body: form });
  if (!response.ok) throw new Error((await response.json().catch(() => null))?.detail || "Could not queue the Award PDF.");
  clearApiCache(); return response.json() as Promise<T>;
}

const path = (
  base: string,
  params: Record<string, string | number | undefined>,
) => {
  const search = new URLSearchParams();
  Object.entries(params).forEach(([key, value]) => {
    if (value !== undefined && value !== "") search.set(key, String(value));
  });
  return `${base}${search.size ? `?${search}` : ""}`;
};
const date = (value?: string | null) =>
  value
    ? new Intl.DateTimeFormat("en-GB", {
        day: "2-digit",
        month: "short",
        year: "numeric",
      }).format(new Date(`${value}T00:00:00`))
    : "—";
const amount = (value?: number | null, unit?: string | null) =>
  value == null ? "—" : `${value} ${unit || ""}`.trim();
const route = {
  village: (id: string) => `/villages/${id}`,
  khasra: (id: string) => `/khasras/${id}`,
  award: (id: string) => `/awards/${id}`,
  notification: (id: string) => `/notifications/${id}`,
  subdivision: (id: string) => `/subdivisions/${id}`,
  district: (id: string) => `/districts/${id}`,
};

function LoadingState({ label = "Loading records…" }: { label?: string }) {
  return (
    <div className="state loading" role="status">
      {label}
    </div>
  );
}
function ErrorState({ message }: { message: string }) {
  return (
    <div className="state error" role="alert">
      <strong>Unable to load this view.</strong>
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
function StatusBadge({
  children,
  tone,
}: {
  children: ReactNode;
  tone?: string;
}) {
  return <span className={`status ${tone || "neutral"}`}>{children}</span>;
}
function EntityLink({ to, children, className = "" }: { to: string; children: ReactNode; className?: string }) {
  return (
    <Link className={`entity-link ${className}`} to={to}>
      {children}
    </Link>
  );
}
function Breadcrumbs({ items }: { items: { label: string; to?: string }[] }) {
  return (
    <nav aria-label="Breadcrumb" className="breadcrumbs">
      {items.map((item, index) => (
        <span key={`${item.label}-${index}`}>
          {item.to ? (
            <Link to={item.to}>{item.label}</Link>
          ) : (
            <span>{item.label}</span>
          )}
          {index < items.length - 1 && <i>/</i>}
        </span>
      ))}
    </nav>
  );
}
function PageHeader({
  eyebrow,
  title,
  actions,
  children,
}: {
  eyebrow?: string;
  title: string;
  actions?: ReactNode;
  children?: ReactNode;
}) {
  return (
    <div className="page-header">
      <div>
        {eyebrow && <p className="eyebrow">{eyebrow}</p>}
        <h1>{title}</h1>
        {children}
      </div>
      {actions && <div className="page-actions">{actions}</div>}
    </div>
  );
}
function DataTable({
  headers,
  children,
}: {
  headers: string[];
  children: ReactNode;
}) {
  return (
    <div className="table-wrap">
      <table>
        <thead>
          <tr>
            {headers.map((header, index) => (
              <th key={`${header}-${index}`} scope="col">
                {header}
              </th>
            ))}
          </tr>
        </thead>
        <tbody>{children}</tbody>
      </table>
    </div>
  );
}
function SearchInput({
  value,
  onChange,
  placeholder = "Search",
}: {
  value: string;
  onChange: (value: string) => void;
  placeholder?: string;
}) {
  return (
    <label className="search-input">
      <span className="sr-only">{placeholder}</span>
      <input
        value={value}
        onChange={(event) => onChange(event.target.value)}
        placeholder={placeholder}
      />
    </label>
  );
}
function Pagination({
  page,
  pageSize,
  totalCount,
  onChange,
}: Page<unknown> & { onChange: (page: number) => void }) {
  const last = Math.max(0, Math.ceil(totalCount / pageSize) - 1);
  if (totalCount <= pageSize) return null;
  return (
    <div className="pagination">
      <span>
        Showing {page * pageSize + 1}–
        {Math.min((page + 1) * pageSize, totalCount)} of {totalCount}
      </span>
      <div>
        <button disabled={page === 0} onClick={() => onChange(page - 1)}>
          Previous
        </button>
        <button disabled={page >= last} onClick={() => onChange(page + 1)}>
          Next
        </button>
      </div>
    </div>
  );
}

function GlobalSearch() {
  const [term, setTerm] = useState("");
  const [delayed, setDelayed] = useState("");
  const navigate = useNavigate();
  useEffect(() => {
    const timer = window.setTimeout(() => setDelayed(term.trim()), 250);
    return () => window.clearTimeout(timer);
  }, [term]);
  const results = useApi<any[]>(
    delayed.length >= 2 ? path("/search", { q: delayed }) : undefined,
  );
  const choose = (target: string) => {
    setTerm("");
    navigate(target);
  };
  return (
    <div className="global-search">
      <SearchInput
        value={term}
        onChange={setTerm}
        placeholder="Search village, khasra, or award"
      />
      {term.length >= 2 && (
        <div className="search-results" role="listbox">
          {results.loading && <LoadingState label="Searching…" />}
          {results.error && <ErrorState message={results.error} />}
          {results.data?.length === 0 && (
            <EmptyState
              title="No matching records"
              detail="Try a village name, khasra number, or award reference."
            />
          )}
          {results.data?.map((result) => (
            <button
              key={`${result.type}-${result.id}`}
              role="option"
              onClick={() => choose(result.route)}
            >
              <StatusBadge>{result.type}</StatusBadge>
              <span>
                <b>{result.label}</b>
                {result.context && <small>{result.context}</small>}
              </span>
            </button>
          ))}
        </div>
      )}
    </div>
  );
}

function Shell({ children }: { children: ReactNode }) {
  const [collapsed, setCollapsed] = useState(false);
  const links = [
    ["Home", "/", "⌂"],
    ["Awards", "/awards", "⌑"],
    ["Search", "/search", "⌕"],
  ];
  return (
    <div className={`app-shell ${collapsed ? "sidebar-collapsed" : ""}`}>
      <aside className="sidebar">
        <div className="sidebar-brand-row">
          <Link className="brand" to="/">
            <span>LAC</span>
            <strong>LAC Platform</strong>
          </Link>
          <button
            className="sidebar-toggle"
            onClick={() => setCollapsed((value) => !value)}
            aria-label={collapsed ? "Expand navigation" : "Collapse navigation"}
            aria-expanded={!collapsed}
          >
            ☰
          </button>
        </div>
        <nav aria-label="Primary navigation">
          {links.map(([label, to, icon]) => (
            <NavLink
              key={to}
              to={to}
              end={to === "/"}
              aria-label={label}
              title={collapsed ? label : undefined}
            >
              <i aria-hidden="true">{icon}</i>
              <span>{label}</span>
            </NavLink>
          ))}
        </nav>
      </aside>
      <div className="workspace">
        <header className="topbar">
          <div className="product-title">
            LAC Platform<small>Land Acquisition Cell</small>
          </div>
          <GlobalSearch />
          <div className="environment-badge">Development</div>
        </header>
        <main>{children}</main>
      </div>
    </div>
  );
}

function Home() {
  const result = useApi<any>("/home");
  if (result.loading) return <LoadingState />;
  if (result.error) return <ErrorState message={result.error} />;
  if (!result.data)
    return (
      <EmptyState
        title="No administrative hierarchy yet"
        detail="District records will appear here once available."
      />
    );
  const district = result.data;
  return (
    <>
      <Breadcrumbs items={[{ label: "Home" }]} />
      <PageHeader eyebrow="Land acquisition records" title={district.name}>
        <p>
          Begin with the administrative hierarchy, then follow connected
          land-records to their canonical detail pages.
        </p>
      </PageHeader>
      <section className="section">
        <div className="section-heading">
          <h2>Sub-divisions</h2>
          <span>{district.subDivisions.length} available</span>
        </div>
        <DataTable headers={["Sub-division", "Villages", ""]}>
          {district.subDivisions.map((subdivision: any) => (
            <tr key={subdivision.id}>
              <td>
                <EntityLink to={route.subdivision(subdivision.id)}>
                  {subdivision.name}
                </EntityLink>
              </td>
              <td>{subdivision.villageCount}</td>
              <td>
                <Link
                  className="text-action"
                  to={route.subdivision(subdivision.id)}
                >
                  Open
                </Link>
              </td>
            </tr>
          ))}
        </DataTable>
      </section>
    </>
  );
}

function District() {
  const { id = "" } = useParams();
  const result = useApi<any>(`/districts/${id}`);
  if (result.loading) return <LoadingState />;
  if (result.error) return <ErrorState message={result.error} />;
  if (!result.data) return null;
  const district = result.data;
  return (
    <>
      <Breadcrumbs
        items={[{ label: "Home", to: "/" }, { label: district.name }]}
      />
      <PageHeader eyebrow="District" title={district.name} />
      <DataTable headers={["Sub-division", "Villages", ""]}>
        {district.subDivisions.map((subdivision: any) => (
          <tr key={subdivision.id}>
            <td>
              <EntityLink to={route.subdivision(subdivision.id)}>
                {subdivision.name}
              </EntityLink>
            </td>
            <td>{subdivision.villageCount}</td>
            <td>
              <Link
                className="text-action"
                to={route.subdivision(subdivision.id)}
              >
                Open
              </Link>
            </td>
          </tr>
        ))}
      </DataTable>
    </>
  );
}

function Subdivision() {
  const { id = "" } = useParams();
  const [page, setPage] = useState(0);
  const [query, setQuery] = useState("");
  const result = useApi<any>(
    path(`/subdivisions/${id}`, { page, pageSize: 25, q: query }),
  );
  if (result.loading) return <LoadingState />;
  if (result.error) return <ErrorState message={result.error} />;
  if (!result.data) return null;
  const subdivision = result.data;
  return (
    <>
      <Breadcrumbs
        items={[
          { label: "Home", to: "/" },
          {
            label: subdivision.district.name,
            to: route.district(subdivision.district.id),
          },
          { label: subdivision.name },
        ]}
      />
      <PageHeader eyebrow={subdivision.district.name} title={subdivision.name}>
        <p>
          {subdivision.villageCount} village
          {subdivision.villageCount === 1 ? "" : "s"} in this sub-division.
        </p>
      </PageHeader>
      <section className="section">
        <div className="section-heading">
          <h2>Villages</h2>
          <SearchInput
            value={query}
            onChange={(value) => {
              setPage(0);
              setQuery(value);
            }}
            placeholder="Filter villages"
          />
        </div>
        {subdivision.villages.items.length ? (
          <>
            <DataTable headers={["Village", "Khasras", ""]}>
              {subdivision.villages.items.map((village: any) => (
                <tr key={village.id}>
                  <td>
                    <EntityLink to={route.village(village.id)}>
                      {village.name}
                    </EntityLink>
                  </td>
                  <td>{village.khasraCount}</td>
                  <td>
                    <Link
                      className="text-action"
                      to={route.village(village.id)}
                    >
                      Open
                    </Link>
                  </td>
                </tr>
              ))}
            </DataTable>
            <Pagination {...subdivision.villages} onChange={setPage} />
          </>
        ) : (
          <EmptyState
            title="No villages match"
            detail="Adjust the filter to see villages in this sub-division."
          />
        )}
      </section>
    </>
  );
}

function Villages() {
  const [page, setPage] = useState(0);
  const [query, setQuery] = useState("");
  const result = useApi<Page<any>>(
    path("/villages", { page, pageSize: 25, q: query }),
  );
  return (
    <>
      <Breadcrumbs items={[{ label: "Villages" }]} />
      <PageHeader eyebrow="Directory" title="Villages" />
      <section className="section">
        <div className="section-heading">
          <h2>Land-record villages</h2>
          <SearchInput
            value={query}
            onChange={(value) => {
              setPage(0);
              setQuery(value);
            }}
            placeholder="Search villages"
          />
        </div>
        {result.loading ? (
          <LoadingState />
        ) : result.error ? (
          <ErrorState message={result.error} />
        ) : !result.data?.items.length ? (
          <EmptyState
            title="No villages match"
            detail="Try a different village name."
          />
        ) : (
          <>
            <DataTable headers={["Village", "Khasras", ""]}>
              {result.data.items.map((village) => (
                <tr key={village.id}>
                  <td>
                    <EntityLink to={route.village(village.id)}>
                      {village.name}
                    </EntityLink>
                  </td>
                  <td>{village.khasraCount}</td>
                  <td>
                    <Link
                      className="text-action"
                      to={route.village(village.id)}
                    >
                      Open
                    </Link>
                  </td>
                </tr>
              ))}
            </DataTable>
            <Pagination {...result.data} onChange={setPage} />
          </>
        )}
      </section>
    </>
  );
}

function Village() {
  const { id = "" } = useParams();
  const village = useApi<any>(`/villages/${id}`);
  if (village.loading) return <LoadingState />;
  if (village.error) return <ErrorState message={village.error} />;
  if (!village.data) return null;
  const data = village.data;
  return (
    <>
      <Breadcrumbs
        items={[
          { label: "Home", to: "/" },
          {
            label: data.subDivision.district.name,
            to: route.district(data.subDivision.district.id),
          },
          {
            label: data.subDivision.name,
            to: route.subdivision(data.subDivision.id),
          },
          { label: data.name },
        ]}
      />
      <PageHeader
        eyebrow={`${data.subDivision.district.name} · ${data.subDivision.name}`}
        title={data.name}
      >
        <p>Village Khasra workspace</p>
      </PageHeader>
      <div className="summary-strip">
        <Metric label="Khasras" value={data.totalKhasras} />
        {data.linkedAwards > 0 && (
          <Metric label="Linked awards" value={data.linkedAwards} />
        )}
      </div>
      <VillageKhasras id={id} />
    </>
  );
}
function Metric({ label, value }: { label: string; value: ReactNode }) {
  return (
    <div className="metric">
      <strong>{value}</strong>
      <span>{label}</span>
    </div>
  );
}
type KhasraRow = {
  khasraNumber: string;
  bigha: string;
  biswa: string;
  biswansi: string;
  awardNumber: string;
  awardDate: string;
};
const blankKhasra = (): KhasraRow => ({
  khasraNumber: "",
  bigha: "",
  biswa: "",
  biswansi: "",
  awardNumber: "",
  awardDate: "",
});
const toKhasraPayload = (row: KhasraRow) => ({
  khasraNumber: row.khasraNumber,
  bigha: row.bigha === "" ? null : Number(row.bigha),
  biswa: row.biswa === "" ? null : Number(row.biswa),
  biswansi: row.biswansi === "" ? null : Number(row.biswansi),
  awardNumber: row.awardNumber || null,
  awardDate: row.awardDate || null,
});
function VillageKhasras({ id }: { id: string }) {
  const [page, setPage] = useState(0);
  const [query, setQuery] = useState("");
  const [refresh, setRefresh] = useState(0);
  const [panel, setPanel] = useState(false);
  const [quickId, setQuickId] = useState("");
  const [importFile, setImportFile] = useState<File | null>(null);
  const [edit, setEdit] = useState<any>(null);
  const result = useApi<Page<any>>(
    path(`/villages/${id}/khasras`, {
      page,
      pageSize: 25,
      q: query,
      r: refresh,
    }),
  );
  const changed = () => {
    setRefresh((value) => value + 1);
    setPanel(false);
    setEdit(null);
    setImportFile(null);
  };
  const openAdd = () => {
    setEdit(null);
    setPanel(true);
  };
  return (
    <section className="section khasra-workspace">
      <div className="section-heading khasra-heading">
        <div>
          <h2>Khasras</h2>
          <span>Canonical parcel records for this village.</span>
        </div>
        <div className="khasra-actions">
          <button onClick={openAdd}>+ Add Khasra</button>
          <label className="secondary-button file-button">
            Upload Excel
            <input
              type="file"
              accept=".xlsx"
              onChange={(event) =>
                event.target.files?.[0] && setImportFile(event.target.files[0])
              }
            />
          </label>
          <a
            className="secondary-button"
            href={`${api}/villages/${id}/khasras/import-template`}
          >
            Download Template
          </a>
          <ExportMenu
            baseUrl={`${api}/villages/${id}/khasras/export`}
            query={query}
          />
          <SearchInput
            value={query}
            onChange={(value) => {
              setPage(0);
              setQuery(value);
            }}
            placeholder="Search Khasras"
          />
        </div>
      </div>
      {importFile && (
        <KhasraImport
          id={id}
          file={importFile}
          onClose={() => setImportFile(null)}
          onSaved={changed}
        />
      )}
      {panel && (
        <KhasraEntryPanel
          id={id}
          edit={edit}
          onClose={() => {
            setPanel(false);
            setEdit(null);
          }}
          onSaved={changed}
        />
      )}
      {result.loading ? (
        <LoadingState />
      ) : result.error ? (
        <ErrorState message={result.error} />
      ) : !result.data?.items.length ? (
        <EmptyState
          title={query ? "No khasras match" : "No khasras added yet."}
          detail={
            query
              ? "Adjust the search or add a new Khasra."
              : "Add a Khasra manually or upload the approved Excel template."
          }
        />
      ) : (
        <>
          <RectangleKhasraGroups
            items={result.data.items}
            onQuickView={setQuickId}
            onEdit={(k) => {
              setEdit(k);
              setPanel(true);
            }}
          />
          <Pagination {...result.data} onChange={setPage} />
        </>
      )}
      {quickId && (
        <KhasraQuickView id={quickId} onClose={() => setQuickId("")} />
      )}
    </section>
  );
}
function RectangleKhasraGroups({
  items,
  onQuickView,
  onEdit,
}: {
  items: any[];
  onQuickView: (id: string) => void;
  onEdit: (item: any) => void;
}) {
  const groups = new Map<string, any[]>();
  items.forEach((item) => {
    const key = item.rectangleNumber?.trim() || "";
    groups.set(key, [...(groups.get(key) || []), item]);
  });
  const sorted = [...groups.entries()].sort(([a], [b]) =>
    !a
      ? 1
      : !b
        ? -1
        : Number(a) - Number(b) ||
          a.localeCompare(b, undefined, { numeric: true }),
  );
  const headers = [
    "Khasra no.",
    "Bigha",
    "Biswa",
    "Biswansi",
    "Recorded owner summary",
    "Acquisition status",
    "Linked award(s)",
    "Actions",
  ];
  return (
    <div className="rectangle-groups">
      {sorted.map(([rectangle, khasras]) => (
        <section className="rectangle-group" key={rectangle || "other"}>
          <h3>
            {rectangle
              ? `Rectangle ${rectangle}`
              : "Other / Rectangle Not Identified"}
          </h3>
          <DataTable headers={headers}>
            {khasras
              .sort((a, b) =>
                a.displayNumber.localeCompare(b.displayNumber, undefined, {
                  numeric: true,
                }),
              )
              .map((k) => (
                <tr key={k.id}>
                  <td>
                    <button
                      className="link-button"
                      onClick={() => onQuickView(k.id)}
                    >
                      {k.displayNumber}
                    </button>
                  </td>
                  <td>{k.areaBigha ?? "—"}</td>
                  <td>{k.areaBiswa ?? "—"}</td>
                  <td>{k.areaBiswansi ?? "—"}</td>
                  <td>{k.ownerSummary}</td>
                  <td>
                    <StatusBadge
                      tone={
                        k.acquisitionStatus !== "Not recorded"
                          ? "success"
                          : undefined
                      }
                    >
                      {k.acquisitionStatus}
                    </StatusBadge>
                  </td>
                  <td>
                    {k.awards.length
                      ? k.awards.map((a: any) => (
                          <EntityLink key={a.id} to={route.award(a.id)}>
                            {a.awardNumber}
                          </EntityLink>
                        ))
                      : "—"}
                  </td>
                  <td>
                    <button
                      className="icon-action"
                      aria-label={`Edit Khasra ${k.displayNumber}`}
                      onClick={() => onEdit(k)}
                    >
                      Edit
                    </button>
                  </td>
                </tr>
              ))}
          </DataTable>
        </section>
      ))}
    </div>
  );
}
function KhasraEntryPanel({
  id,
  edit,
  onClose,
  onSaved,
}: {
  id: string;
  edit: any;
  onClose: () => void;
  onSaved: () => void;
}) {
  const [rows, setRows] = useState<KhasraRow[]>(
    edit
      ? [
          {
            khasraNumber: edit.displayNumber,
            bigha: edit.areaBigha ?? "",
            biswa: edit.areaBiswa ?? "",
            biswansi: edit.areaBiswansi ?? "",
            awardNumber: edit.awards?.[0]?.awardNumber ?? "",
            awardDate: "",
          },
        ]
      : [blankKhasra()],
  );
  const [message, setMessage] = useState("");
  const update = (i: number, key: keyof KhasraRow, value: string) =>
    setRows((all) =>
      all.map((r, index) => (index === i ? { ...r, [key]: value } : r)),
    );
  const save = async () => {
    try {
      const nonEmpty = rows.filter((r) => r.khasraNumber.trim());
      if (!nonEmpty.length) {
        setMessage("Enter at least one Khasra Number.");
        return;
      }
      if (edit) await put(`/khasras/${edit.id}`, toKhasraPayload(nonEmpty[0]));
      else
        await post(`/villages/${id}/khasras/batch`, {
          rows: nonEmpty.map(toKhasraPayload),
        });
      onSaved();
    } catch (e) {
      setMessage(e instanceof Error ? e.message : "Could not save khasras.");
    }
  };
  return (
    <div className="workspace-panel">
      <div className="panel-title">
        <h3>{edit ? "Edit Khasra" : "Add Khasras"}</h3>
        <button onClick={onClose}>Close</button>
      </div>
      <p>
        Enter one or many canonical parcels. An Award number reuses the
        canonical Award when it already exists.
      </p>
      <div className="khasra-grid-wrap">
        <table className="khasra-grid">
          <thead>
            <tr>
              <th>Khasra Number</th>
              <th>Bigha</th>
              <th>Biswa</th>
              <th>Biswansi</th>
              <th>Award</th>
              <th>Award Date</th>
              <th />
            </tr>
          </thead>
          <tbody>
            {rows.map((row, i) => (
              <tr key={i}>
                <td>
                  <input
                    autoFocus={i === 0}
                    value={row.khasraNumber}
                    onChange={(e) => update(i, "khasraNumber", e.target.value)}
                  />
                </td>
                <td>
                  <input
                    inputMode="decimal"
                    value={row.bigha}
                    onChange={(e) => update(i, "bigha", e.target.value)}
                  />
                </td>
                <td>
                  <input
                    inputMode="numeric"
                    value={row.biswa}
                    onChange={(e) => update(i, "biswa", e.target.value)}
                  />
                </td>
                <td>
                  <input
                    inputMode="numeric"
                    value={row.biswansi}
                    onChange={(e) => update(i, "biswansi", e.target.value)}
                  />
                </td>
                <td>
                  <input
                    value={row.awardNumber}
                    onChange={(e) => update(i, "awardNumber", e.target.value)}
                    placeholder="Search/reuse award no."
                  />
                </td>
                <td>
                  <input
                    type="date"
                    value={row.awardDate}
                    onChange={(e) => update(i, "awardDate", e.target.value)}
                  />
                </td>
                <td>
                  {!edit && rows.length > 1 && (
                    <button
                      onClick={() =>
                        setRows((all) => all.filter((_, index) => index !== i))
                      }
                    >
                      Remove
                    </button>
                  )}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
      <div className="form-footer">
        <button
          className="secondary-button"
          onClick={() => setRows((all) => [...all, blankKhasra()])}
        >
          Add Row
        </button>
        <button onClick={save}>
          {edit ? "Save changes" : "Add All / Save All"}
        </button>
      </div>
      {message && <p className="form-message">{message}</p>}
    </div>
  );
}
function KhasraImport({
  id,
  file,
  onClose,
  onSaved,
}: {
  id: string;
  file: File;
  onClose: () => void;
  onSaved: () => void;
}) {
  const [preview, setPreview] = useState<any>();
  const [message, setMessage] = useState("");
  useEffect(() => {
    void upload(`/villages/${id}/khasras/import-preview`, file)
      .then(setPreview)
      .catch((e) =>
        setMessage(
          e instanceof Error ? e.message : "Could not preview workbook.",
        ),
      );
  }, [file, id]);
  const save = async () => {
    try {
      const saved: any = await post(`/villages/${id}/khasras/import`, {
        rows: preview.importableRows,
      });
      setMessage(
        `${saved.createdKhasras} created, ${saved.reusedKhasras} reused, ${saved.createdAwards} award(s) created, ${saved.createdAwardLinks} new link(s), ${saved.failedRows} skipped.`,
      );
      onSaved();
    } catch (e) {
      setMessage(e instanceof Error ? e.message : "Could not import rows.");
    }
  };
  return (
    <div className="workspace-panel">
      <div className="panel-title">
        <h3>Excel import preview</h3>
        <button onClick={onClose}>Close</button>
      </div>
      <p>
        Only the first sheet, <strong>All Occurrences</strong>, is read. Other
        sheets and unrelated columns are ignored.
      </p>
      {!preview && !message && <LoadingState />}
      {preview && (
        <>
          <div className="metric-row">
            <span>{preview.totalRows} rows</span>
            <span>{preview.validRows} ready</span>
            <span>{preview.invalidRows} blocked</span>
            <span>{preview.newKhasras} new khasras</span>
            <span>{preview.existingKhasras} existing khasras</span>
            <span>{preview.newAwards} canonical awards</span>
            <span>{preview.newAwardLinks} Khasra links</span>
          </div>
          <div className="table-wrap import-preview-table">
            <table>
              <thead>
                <tr>
                  {[
                    "Sr. No.",
                    "Khasra No.",
                    "Qualifier",
                    "Bigha",
                    "Biswa",
                    "Biswansi",
                    "Award No.",
                    "Award Date",
                    "Khasra Status",
                    "Link Status",
                    "Result",
                  ].map((header) => (
                    <th key={header}>{header}</th>
                  ))}
                </tr>
              </thead>
              <tbody>
                {preview.rows.map((row: any) => (
                  <tr key={row.rowNumber}>
                    <td>{row.rowNumber}</td>
                    <td>
                      {row.row?.khasraNumber || row.khasraNumber || "—"}
                      <small className="subtext">{row.message || ""}</small>
                    </td>
                    <td>{row.row?.qualifier || "—"}</td>
                    <td>{row.row?.bigha ?? "—"}</td>
                    <td>{row.row?.biswa ?? "—"}</td>
                    <td>{row.row?.biswansi ?? "—"}</td>
                    <td>{row.row?.awardNumber || "—"}</td>
                    <td>
                      {row.row?.awardDate ? date(row.row.awardDate) : "—"}
                    </td>
                    <td>{row.khasraStatus}</td>
                    <td>{row.awardLinkStatus}</td>
                    <td>
                      <StatusBadge
                        tone={row.result === "READY" ? "success" : "warning"}
                      >
                        {row.result}
                      </StatusBadge>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
          {preview.importableRows.length > 0 && (
            <button onClick={save}>Import All Valid Rows</button>
          )}
        </>
      )}
      {message && <p className="form-message">{message}</p>}
    </div>
  );
}
function KhasraQuickView({ id, onClose }: { id: string; onClose: () => void }) {
  const k = useApi<any>(`/khasras/${id}`);
  const ownership = useApi<any>(`/khasras/${id}/ownership`);
  if (k.loading || ownership.loading)
    return (
      <aside className="quick-view">
        <LoadingState />
      </aside>
    );
  if (!k.data)
    return (
      <aside className="quick-view">
        <ErrorState message={k.error || "Could not load Khasra."} />
      </aside>
    );
  const x = k.data;
  return (
    <aside className="quick-view">
      <div className="panel-title">
        <h3>Khasra quick view</h3>
        <button onClick={onClose}>Close</button>
      </div>
      <h2>{x.displayNumber}</h2>
      <p>
        {x.village.name} · {x.village.subDivision.name}
      </p>
      <InfoSection
        title="Area"
        rows={[
          ["Bigha", x.areaBigha ?? "—"],
          ["Biswa", x.areaBiswa ?? "—"],
          ["Biswansi", x.areaBiswansi ?? "—"],
        ]}
      />
      <section>
        <h3>Recorded owners</h3>
        {ownership.data?.isAmbiguous ? (
          <p>Ambiguous historical record — not guessed.</p>
        ) : ownership.data?.owners?.length ? (
          ownership.data.owners.map((o: any) => (
            <p key={o.partyId}>
              <EntityLink to={`/parties/${o.partyId}`}>
                {o.displayName}
              </EntityLink>
            </p>
          ))
        ) : (
          <p>Not recorded.</p>
        )}
      </section>
      <section>
        <h3>Linked awards</h3>
        {x.awards.length ? (
          x.awards.map((a: any) => (
            <p key={a.id}>
              <EntityLink to={route.award(a.id)}>{a.awardNumber}</EntityLink> ·{" "}
              {a.acquisitionStatus || "Status not recorded"}
            </p>
          ))
        ) : (
          <p>Not linked.</p>
        )}
      </section>
      <section>
        <h3>Relevant notifications</h3>
        {x.notifications.length ? (
          x.notifications.map((n: any) => (
            <p key={n.id}>{n.notificationNumber}</p>
          ))
        ) : (
          <p>Not recorded.</p>
        )}
      </section>
      <Link className="text-action" to={route.khasra(id)}>
        Open Full Record
      </Link>
    </aside>
  );
}
function VillageAwards({ id }: { id: string }) {
  const result = useApi<Page<any>>(
    path(`/villages/${id}/awards`, { page: 0, pageSize: 25 }),
  );
  return (
    <TabTable
      result={result}
      headers={["Award", "Date", "Status"]}
      rows={(award) => (
        <tr key={award.id}>
          <td>
            <EntityLink to={route.award(award.id)}>
              {award.awardNumber}
            </EntityLink>
          </td>
          <td>{date(award.awardDate)}</td>
          <td>
            <StatusBadge
              tone={award.status === "Published" ? "success" : undefined}
            >
              {award.status}
            </StatusBadge>
          </td>
        </tr>
      )}
      empty="No awards are linked to this village."
    />
  );
}
function VillageNotifications({ id }: { id: string }) {
  const result = useApi<any[]>(`/villages/${id}/notifications`);
  return (
    <TabTable
      result={result}
      headers={["Notification", "Section", "Date"]}
      rows={(notification) => (
        <tr key={notification.id}>
          <td>
            <EntityLink to={route.notification(notification.id)}>
              {notification.notificationNumber}
            </EntityLink>
          </td>
          <td>Section {notification.sectionType}</td>
          <td>{date(notification.notificationDate)}</td>
        </tr>
      )}
      empty="No notifications are linked to this village."
    />
  );
}
function VillageKhatauni({ id }: { id: string }) {
  const result = useApi<any[]>(`/villages/${id}/khatauni`);
  return (
    <TabTable
      result={result}
      headers={[
        "Reference",
        "Record year",
        "As of",
        "Status",
        "Khatas",
        "Recorded khasras",
      ]}
      rows={(record) => (
        <tr key={record.id}>
          <td>
            <EntityLink to={`/khatauni/${record.id}`}>
              {record.referenceNumber || "Unreferenced revenue record"}
            </EntityLink>
          </td>
          <td>{record.recordYearText || "—"}</td>
          <td>{date(record.asOfDate)}</td>
          <td>
            <StatusBadge>{record.verificationStatus}</StatusBadge>
          </td>
          <td>{record.khataCount}</td>
          <td>{record.recordedKhasraCount}</td>
        </tr>
      )}
      empty="No Khatauni revenue records are linked to this village."
    />
  );
}
function VillageLrs({ id }: { id: string }) {
  const result = useApi<any[]>(`/villages/${id}/lrs`);
  const progress = useApi<any>(`/villages/${id}/lr-progress`);
  return (
    <>
      {progress.data && (
        <div className="metric-row">
          <span>{progress.data.totalRows} total</span>
          <span>{progress.data.draft} draft</span>
          <span>{progress.data.needsReview} review</span>
          <span>{progress.data.verified} verified</span>
          <span>{progress.data.committed} committed</span>
        </div>
      )}
      <TabTable
        result={result}
        headers={["Register reference", "Entries"]}
        rows={(lr) => (
          <tr key={lr.id}>
            <td>
              <EntityLink to={`/villages/${id}/lr/${lr.id}`}>
                {lr.registerReference || "Unreferenced register"}
              </EntityLink>
            </td>
            <td>{lr.entryCount}</td>
          </tr>
        )}
        empty="No LR register is linked to this village."
      />
    </>
  );
}
function VillageDocuments({ id }: { id: string }) {
  const result = useApi<any[]>(`/villages/${id}/documents`);
  return (
    <TabTable
      result={result}
      headers={["Document", "Type", "Uploaded"]}
      rows={(doc) => (
        <tr key={doc.id}>
          <td>{doc.originalFileName}</td>
          <td>{doc.documentType}</td>
          <td>{date(doc.uploadedAt?.slice(0, 10))}</td>
        </tr>
      )}
      empty="No documents are linked to this village."
    />
  );
}
function TabTable({
  result,
  headers,
  rows,
  empty,
}: {
  result: State<any[] | Page<any>>;
  headers: string[];
  rows: (item: any) => ReactNode;
  empty: string;
}) {
  if (result.loading) return <LoadingState />;
  if (result.error) return <ErrorState message={result.error} />;
  const values = Array.isArray(result.data) ? result.data : result.data?.items;
  return (
    <section className="section">
      {values?.length ? (
        <DataTable headers={headers}>{values.map(rows)}</DataTable>
      ) : (
        <EmptyState title="Nothing to display" detail={empty} />
      )}
    </section>
  );
}

function Khasra() {
  const { id = "" } = useParams();
  const result = useApi<any>(`/khasras/${id}`);
  if (result.loading) return <LoadingState />;
  if (result.error) return <ErrorState message={result.error} />;
  if (!result.data) return null;
  const k = result.data;
  const district = k.village.subDivision.district;
  return (
    <>
      <Breadcrumbs
        items={[
          { label: "Home", to: "/" },
          { label: district.name, to: route.district(district.id) },
          {
            label: k.village.subDivision.name,
            to: route.subdivision(k.village.subDivision.id),
          },
          { label: k.village.name, to: route.village(k.village.id) },
          { label: k.displayNumber },
        ]}
      />
      <PageHeader
        eyebrow="Canonical land parcel record"
        title={`Khasra ${k.displayNumber}`}
      >
        <p>
          {k.village.name} · {k.village.subDivision.name} · {district.name}
        </p>
      </PageHeader>
      <section className="detail-grid">
        <InfoSection
          title="Land identity"
          rows={[
            ["Display number", k.displayNumber],
            ["Rectangle", k.rectangleNumber || "—"],
            ["Killa", k.killaNumber || "—"],
            ["Subdivision", k.subdivisionNumber || "—"],
            ["Total area", amount(k.totalArea, k.areaUnit)],
            ["Remarks", k.remarks || "—"],
          ]}
        />
        <InfoSection
          title="Current acquisition summary"
          rows={[
            ["Awards linked", k.awards.length],
            ["Notifications linked", k.notifications.length],
            ["Status", k.awards[0]?.acquisitionStatus || "Not linked"],
          ]}
        />
      </section>
      <section className="section">
        <h2>Acquisition history</h2>
        {k.notifications.length || k.awards.length ? (
          <DataTable headers={["Type", "Reference", "Area", "Status"]}>
            {k.notifications.map((n: any) => (
              <tr key={`n-${n.id}`}>
                <td>Notification · Section {n.sectionType}</td>
                <td>
                  <EntityLink to={route.notification(n.id)}>
                    {n.notificationNumber}
                  </EntityLink>
                </td>
                <td>{amount(n.area, n.areaUnit)}</td>
                <td>
                  <StatusBadge>Linked</StatusBadge>
                </td>
              </tr>
            ))}
            {k.awards.map((a: any) => (
              <tr key={`a-${a.id}`}>
                <td>Award</td>
                <td>
                  <EntityLink to={route.award(a.id)}>
                    {a.awardNumber}
                  </EntityLink>
                </td>
                <td>{amount(a.acquiredArea, a.areaUnit)}</td>
                <td>
                  <StatusBadge tone="success">
                    {a.acquisitionStatus || "Linked"}
                  </StatusBadge>
                </td>
              </tr>
            ))}
          </DataTable>
        ) : (
          <EmptyState
            title="No acquisition links"
            detail="No notifications or awards are connected to this khasra."
          />
        )}
      </section>
      <section className="section">
        <PermanentSourceLines endpoint={`/khasras/${id}/evidence`} />
        <h2>Source / LR information</h2>
        {k.lrEntries.length ? (
          <DataTable
            headers={[
              "Raw khasra text",
              "Raw area",
              "Verification",
              "Register",
            ]}
          >
            {k.lrEntries.map((entry: any) => (
              <tr key={entry.id}>
                <td>{entry.rawKhasraText}</td>
                <td>{entry.rawAreaText || "—"}</td>
                <td>
                  <StatusBadge>{entry.verificationStatus}</StatusBadge>
                </td>
                <td>
                  <EntityLink to={`/imports/lr?register=${entry.villageLrId}`}>
                    Open LR
                  </EntityLink>
                </td>
              </tr>
            ))}
          </DataTable>
        ) : (
          <EmptyState
            title="No LR source row"
            detail="No historical LR entry is connected to this khasra."
          />
        )}
      </section>
      <FutureSections
        names={["Ownership", "Compensation", "Possession", "Court cases"]}
      />
    </>
  );
}
function InfoSection({
  title,
  rows,
}: {
  title: string;
  rows: [string, ReactNode][];
}) {
  return (
    <section className="info-section">
      <h2>{title}</h2>
      <dl>
        {rows.map(([label, value]) => (
          <div key={label}>
            <dt>{label}</dt>
            <dd>{value}</dd>
          </div>
        ))}
      </dl>
    </section>
  );
}
function FutureSections({ names }: { names: string[] }) {
  return (
    <section className="future">
      <h2>Future record areas</h2>
      {names.map((name) => (
        <div key={name}>
          <strong>{name}</strong>
          <span>Not yet available in Phase 1.</span>
        </div>
      ))}
    </section>
  );
}

function Awards() {
  const [page, setPage] = useState(0);
  const [query, setQuery] = useState("");
  const result = useApi<Page<any>>(
    path("/awards", { page, pageSize: 25, q: query }),
  );
  return (
    <>
      <Breadcrumbs items={[{ label: "Awards" }]} />
      <PageHeader eyebrow="Acquisition awards" title="Awards" actions={<EntityLink to="/awards/import-pdf" className="secondary-button">Import Award PDF</EntityLink>} />
      <section className="section">
        <div className="section-heading">
          <h2>Award register</h2>
          <SearchInput
            value={query}
            onChange={(value) => {
              setPage(0);
              setQuery(value);
            }}
            placeholder="Search award number"
          />
        </div>
        {result.loading ? (
          <LoadingState />
        ) : result.error ? (
          <ErrorState message={result.error} />
        ) : !result.data?.items.length ? (
          <EmptyState
            title="No awards match"
            detail="Adjust the award number search."
          />
        ) : (
          <>
            <DataTable
              headers={[
                "Award number",
                "Award date",
                "Village",
                "Status",
                "Linked Khasras",
                "Project / agency",
              ]}
            >
              {result.data.items.map((award) => (
                <tr key={award.id}>
                  <td>
                    <EntityLink to={route.award(award.id)}>
                      {award.awardNumber}
                    </EntityLink>
                  </td>
                  <td>{date(award.awardDate)}</td>
                  <td>{award.villageNames || "—"}</td>
                  <td>
                    <StatusBadge
                      tone={
                        award.status === "Published" ? "success" : undefined
                      }
                    >
                      {award.status}
                    </StatusBadge>
                  </td>
                  <td>{award.linkedKhasraCount}</td>
                  <td>
                    {award.projectName ? (
                      <>
                        {award.projectName}
                        <small className="subtext">
                          {award.requiringAgency || "—"}
                        </small>
                      </>
                    ) : (
                      "—"
                    )}
                  </td>
                </tr>
              ))}
            </DataTable>
            <Pagination {...result.data} onChange={setPage} />
          </>
        )}
      </section>
    </>
  );
}

function Award() {
  const { id = "" } = useParams();
  const [page, setPage] = useState(0);
  const [refresh, setRefresh] = useState(0);
  const [adding, setAdding] = useState(false);
  const [related, setRelated] = useState(false);
  const [pdfImport, setPdfImport] = useState(false);
  const [rectangle, setRectangle] = useState("");
  const overview = useApi<any>(`/awards/${id}/workspace?r=${refresh}`);
  const workspace = useApi<Page<any>>(
    path(`/awards/${id}/khasras`, { page, pageSize: 25, r: refresh }),
  );
  const notifications = useApi<any[]>(overview.data?.notificationCount > 0 ? `/awards/${id}/notifications?r=${refresh}` : undefined);
  const possession = useApi<any[]>(overview.data?.possessionEventCount > 0 ? `/awards/${id}/possession-events?r=${refresh}` : undefined);
  const courtCases = useApi<any[]>(overview.data?.courtCaseCount > 0 ? `/awards/${id}/court-cases?r=${refresh}` : undefined);
  const claims = useApi<Page<any>>(overview.data?.claimCount > 0 ? path(`/awards/${id}/claims`, { page: 0, pageSize: 25, r: refresh }) : undefined);
  if (overview.loading || workspace.loading) return <LoadingState />;
  if (overview.error || workspace.error)
    return <ErrorState message={overview.error || workspace.error || ""} />;
  if (!overview.data || !workspace.data) return null;
  const a = overview.data;
  const rows = workspace.data.items;
  const rectangles = [...new Set(rows.map((k: any) => k.rectangleNumber || "Other"))].sort((x, y) => Number(x) - Number(y) || x.localeCompare(y));
  const visibleRows = rows.filter((k: any) => !rectangle || (k.rectangleNumber || "Other") === rectangle).sort((x: any, y: any) => Number(x.rectangleNumber ?? Number.MAX_SAFE_INTEGER) - Number(y.rectangleNumber ?? Number.MAX_SAFE_INTEGER) || x.displayNumber.localeCompare(y.displayNumber, undefined, { numeric: true }));
  const areas = (b: any, w: any, s: any) =>
    b == null && w == null && s == null
      ? "—"
      : `${b ?? "—"}-${w ?? "—"}-${s ?? "—"}`;
  return (
    <>
      <Breadcrumbs
        items={[
          { label: "Home", to: "/" },
          { label: "Awards", to: "/awards" },
          { label: a.awardNumber },
        ]}
      />
      <PageHeader
        eyebrow="Award workspace"
        title={a.awardNumber}
        actions={<div className="award-actions"><button onClick={() => setAdding(true)}>+ Add / Link Khasra</button><button className="secondary-button" onClick={() => setPdfImport(true)}>Import / Review Data ▾</button><button className="secondary-button" onClick={() => setRelated(true)}>+ Add Related Record ▾</button><ExportMenu baseUrl={`/api/awards/${id}/export`} query="" /></div>}
      >
        <p>
          {[
            date(a.awardDate),
            ...a.villages.map((v: any) => v.name),
            a.awardType,
            a.purpose,
            a.actRegime,
          ]
            .filter(Boolean)
            .join(" · ")}
        </p>
      </PageHeader>
      <section className="award-summary">
        <div className="award-facts"><strong>Village{a.villages.length === 1 ? "" : "s"}</strong><span>{a.villages.map((v: any, i: number) => <Fragment key={v.id}>{i > 0 && ", "}<EntityLink to={route.village(v.id)}>{v.name}</EntityLink></Fragment>)}</span>{a.project?.name && <><strong>Project</strong><span>{a.project.name}</span></>}{a.purpose && <><strong>Purpose</strong><span>{a.purpose}</span></>}{a.actRegime && <><strong>Act / nature</strong><span>{a.actRegime}</span></>}</div>
        <details className="completeness"><summary>Record completeness <b>{[a.khasrasData, a.notificationsData, a.possessionData, a.litigationData, a.claimsData].filter((x: string) => x !== "Not Added").length} of 5 data areas available</b></summary><div className="metric-row"><span>Khasras: {a.khasrasData}</span><span>Notifications: {a.notificationsData}</span><span>Possession: {a.possessionData}</span><span>Litigation: {a.litigationData}</span><span>Claims: {a.claimsData}</span></div></details>
      </section>
      <section className="section">
        <div className="section-heading">
          <div>
            <h2>Khasras</h2>
            <span>
              Canonical village parcels; Award areas never overwrite village
              master area.
            </span>
          </div>
          <div className="award-khasra-toolbar"><span>{a.khasraCount} linked</span><input aria-label="Search Khasras" placeholder="Search Khasra" /><select value={rectangle} onChange={e => setRectangle(e.target.value)}><option value="">All rectangles</option>{rectangles.map(value => <option key={value} value={value}>{value === "Other" ? "Other / Rectangle Not Identified" : `Rectangle ${value}`}</option>)}</select><select aria-label="Review state"><option>All review states</option><option>Needs review</option><option>Reviewed</option></select></div>
        </div>
        {rows.length ? (
          <>
            <DataTable
              headers={[
                "Khasra no.",
                "Village",
                "Canonical area",
                "Award recorded area",
                "Area awarded",
                "Master review",
                "Actions",
              ]}
            >
              {visibleRows.map((k: any, index: number) => <Fragment key={k.awardKhasraId}>
                {(index === 0 || (visibleRows[index - 1].rectangleNumber || "Other") !== (k.rectangleNumber || "Other")) && <tr key={`rectangle-${k.rectangleNumber || "Other"}`} className="rectangle-row"><td colSpan={7}>{k.rectangleNumber ? `RECTANGLE ${k.rectangleNumber}` : "OTHER / RECTANGLE NOT IDENTIFIED"}</td></tr>}
                <tr key={k.awardKhasraId}>
                  <td>
                    <EntityLink to={route.khasra(k.khasraId)}>
                      {k.displayNumber}
                    </EntityLink>
                  </td>
                  <td>{k.villageName}</td>
                  <td>
                    {areas(
                      k.canonicalAreaBigha,
                      k.canonicalAreaBiswa,
                      k.canonicalAreaBiswansi,
                    )}
                  </td>
                  <td>
                    {areas(
                      k.recordedTotalAreaBigha,
                      k.recordedTotalAreaBiswa,
                      k.recordedTotalAreaBiswansi,
                    )}
                  </td>
                  <td>
                    {areas(
                      k.awardedAreaBigha,
                      k.awardedAreaBiswa,
                      k.awardedAreaBiswansi,
                    )}
                  </td>
                  <td>
                    {k.reviewFlagId ? (
                      <button className="link-button" onClick={async () => { await post(`/khasra-review-flags/${k.reviewFlagId}/resolve`, { resolvedBy: "Award workspace" }); setRefresh(x => x + 1) }}>
                        <StatusBadge tone="warning">From Award · Review</StatusBadge>
                      </button>
                    ) : (
                      "—"
                    )}
                  </td>
                  <td>
                    <EntityLink to={route.khasra(k.khasraId)}>Open</EntityLink>
                  </td>
                </tr></Fragment>)}
            </DataTable>
            <Pagination {...workspace.data} onChange={setPage} />
          </>
        ) : (
          <EmptyState
            title="No Khasras linked to this Award."
            detail="Link an existing Village Khasra or add a missing one; it will remain canonical Village master data."
          />
        )}
      </section>
      {a.notificationCount > 0 && (
        <section className="section">
          <h2>Notifications</h2>
          <DataTable headers={["Notification", "Section", "Date"]}>{notifications.data?.map(n => <tr key={n.id}><td><EntityLink to={route.notification(n.id)}>{n.notificationNumber}</EntityLink></td><td>Section {n.sectionType}</td><td>{date(n.notificationDate)}</td></tr>)}</DataTable>
        </section>
      )}
      {a.possessionEventCount > 0 && (
        <section className="section">
          <h2>Possession</h2>
          <p>This does not imply possession of the whole Award.</p><DataTable headers={["Date", "Event", "Status", "Affected Khasras"]}>{possession.data?.map(item => <tr key={item.id}><td>{date(item.possessionDate)}</td><td>{item.eventType || "—"}</td><td>{item.status || "—"}</td><td>{item.khasraCount}</td></tr>)}</DataTable>
        </section>
      )}
      {a.courtCaseCount > 0 && (
        <section className="section">
          <h2>Court cases</h2>
          <p>No legal effect is inferred.</p><DataTable headers={["Case", "Court", "Status", "Affected Khasras"]}>{courtCases.data?.map(item => <tr key={item.id}><td>{item.caseNumber}</td><td>{item.courtName}</td><td>{item.status || "—"}</td><td>{item.khasraCount}</td></tr>)}</DataTable>
        </section>
      )}
      {a.claimCount > 0 && <section className="section"><h2>Claims</h2><DataTable headers={["Reference", "Date", "Claimant", "Claimed amount", "Affected Khasras", "Status"]}>{claims.data?.items.map(item => <tr key={item.id}><td>{item.claimReference || "—"}</td><td>{date(item.claimDate)}</td><td>{item.claimantName || "—"}</td><td>{item.claimedAmount ?? "—"}</td><td>{item.khasraCount}</td><td>{item.status || "—"}</td></tr>)}</DataTable></section>}
      {adding && (
        <AwardKhasraPanel
          award={a}
          onClose={() => setAdding(false)}
          onSaved={() => {
            setAdding(false);
            setRefresh((x) => x + 1);
          }}
        />
      )}
      {related && <AwardRelatedPanel award={a} khasras={rows} onClose={() => setRelated(false)} onSaved={() => { setRelated(false); setRefresh(x => x + 1); }} />}
      <AwardDocumentsSection key={`${id}-${refresh}`} awardId={id} />
      <PermanentSourceLines endpoint={`/awards/${id}/evidence`} />
      {pdfImport && <AwardPdfImportPanel award={a} onClose={() => {setPdfImport(false);setRefresh(v=>v+1);}} />}
    </>
  );
}
function DocumentPdfViewer({documentId,initialPage=1}:{documentId:string;initialPage?:number}) {
  const src=`${api}/documents/${documentId}/content#page=${initialPage}&view=FitH&navpanes=0`;
  return <iframe className="document-pdf-viewer" src={src} title={`Original PDF, page ${initialPage}`} />;
}
function AwardDocumentsSection({awardId}:{awardId:string}) {
  const [refresh,setRefresh]=useState(0);const [message,setMessage]=useState("");const [preview,setPreview]=useState<any>();const [localJobs,setLocalJobs]=useState<Record<string,any>>({});const viewerRef=useRef<HTMLElement>(null);
  const result=useApi<any[]>(`/awards/${awardId}/documents?r=${refresh}`);
  const analyze=async(doc:any)=>{try{setMessage("");const villageId=doc.villages?.length===1?doc.villages[0].villageId:undefined;const started:any=await post(`/awards/${awardId}/documents/${doc.id}/analyze`,{villageId});if(started?.jobId)setLocalJobs(x=>({...x,[doc.id]:{id:started.jobId,status:"Queued",processedPages:0,totalPages:null,currentStage:"Waiting to analyze"}}));}catch(e){setMessage(e instanceof Error?e.message:"Could not start analysis.");}};
  const reanalyze=async(doc:any)=>{try{setMessage("");const started:any=await post(`/award-pdf-extractions/${doc.job.id}/reanalyze`,{});setLocalJobs(x=>({...x,[doc.id]:{...doc.job,id:started?.id||doc.job.id,status:"Analyzing",processedPages:0,currentStage:"Re-analyzing saved page evidence",startedAt:new Date().toISOString()}}));}catch(e){setMessage(e instanceof Error?e.message:"Could not re-analyze saved pages.");}};
  if(result.error)return <section className="section"><h2>Documents</h2><p role="alert">{result.error}</p></section>;
  if(!result.data?.length)return null;
  return <section className="award-documents"><div className="section-heading"><div><p className="section-eyebrow">Award record</p><h2>Documents</h2></div><span className="hint">Analysis runs in the background</span></div>{result.data.map(doc=>{const j=localJobs[doc.id]||doc.job;const processing=j&&["Queued","Extracting","Analyzing","BuildingCandidates"].includes(j.status);const state=!j?"Not analyzed":processing?`${j.currentStage||"Analyzing"}…`:j.status==="Failed"?"Analysis needs attention":j.reviewed?"Reviewed":j.ingestionSessionId?`Review ready · ${j.attention} need attention`:"Stored";return <div className="award-document-row" key={doc.id}><div className="document-icon">PDF</div><div className="document-main"><strong>{doc.originalFileName}</strong><span>{state}</span>{processing&&<DocumentAnalysisProgress jobId={j.id} paused={Boolean(preview)} onComplete={()=>{setLocalJobs(x=>{const y={...x};delete y[doc.id];return y;});setRefresh(x=>x+1);}}/>}</div><div className="document-actions"><button className="quiet-button" onClick={()=>setPreview(doc)}>View PDF</button>{!j&&<button onClick={()=>analyze(doc)}>Analyze data</button>}{j?.ingestionSessionId&&<Link className="primary-link" to={`/awards/${awardId}/ingestion/${j.ingestionSessionId}`}>Review data</Link>}{j&&!processing&&<button className="quiet-button" onClick={()=>reanalyze(doc)}>Re-analyze</button>}</div></div>;})}{message&&<p className="form-message">{message}</p>}{preview&&<aside ref={viewerRef} className="document-viewer-modal" role="dialog" aria-modal="true" aria-label="Award document"><header><strong>{preview.originalFileName}</strong><div className="document-viewer-actions"><button className="quiet-button" onClick={()=>viewerRef.current?.requestFullscreen?.()}>Full screen</button><button className="quiet-button" onClick={()=>setPreview(undefined)}>Close</button></div></header><DocumentPdfViewer documentId={preview.id}/></aside>}</section>;
}

function DocumentAnalysisProgress({jobId,paused,onComplete}:{jobId:string;paused:boolean;onComplete:()=>void}) {
  const [tick,setTick]=useState(0);const samples=useRef<Array<{pages:number;at:number}>>([]);const status=useApi<any>(`/award-pdf-extractions/${jobId}?r=${tick}`);const job=status.data;
  useEffect(()=>{if(paused||!job||!["Queued","Extracting","Analyzing","BuildingCandidates"].includes(job.status))return;const timer=window.setInterval(()=>setTick(x=>x+1),5000);return()=>window.clearInterval(timer);},[paused,job?.status]);
  useEffect(()=>{if(job&&!["Queued","Extracting","Analyzing","BuildingCandidates"].includes(job.status))onComplete();},[job?.status]);
  const total=Number(job?.totalPages||0);const processed=Math.min(Number(job?.processedPages||0),total||Number.MAX_SAFE_INTEGER);const percent=total?Math.round(processed/total*100):0;
  useEffect(()=>{if(!job||processed<0)return;const now=Date.now();const last=samples.current.at(-1);if(!last||last.pages!==processed){samples.current=[...samples.current.filter(x=>now-x.at<120000),{pages:processed,at:now}].slice(-6);}},[processed,job?.id]);
  const windowSamples=samples.current;const first=windowSamples[0];const last=windowSamples.at(-1);const pageRate=first&&last&&last.pages>first.pages&&last.at>first.at?(last.pages-first.pages)/((last.at-first.at)/1000):null;const remaining=pageRate&&total>processed?(total-processed)/pageRate:null;const eta=pageRate===null?"Measuring pace…":remaining!==null?(remaining<60?`About ${Math.max(1,Math.round(remaining))} sec left`:`About ${Math.ceil(remaining/60)} min left`):"Almost done";
  return <div className="document-progress" aria-label={`${percent}% complete`}><div className="document-progress-track"><span style={{width:total?`${Math.max(2,percent)}%`:"35%"}} /></div><div className="document-progress-meta"><span>{total?`${percent}% complete`:`${job?.currentStage||"Preparing…"}`}</span><span>{eta}</span></div></div>;
}

function PermanentSourceLines({endpoint}:{endpoint:string}) {
  const [page,setPage]=useState(0);const result=useApi<Page<any>>(path(endpoint,{page}));
  if(!result.data?.items.length)return null;
  const groups = new Map<string,any[]>();
  result.data.items.forEach(e=>{const key=`${e.documentId}:${e.pageNumber}:${e.verifiedAt}`;groups.set(key,[...(groups.get(key)||[]),e]);});
  const factLabel=(name:string)=>({recordedAreaBigha:"Total Area Recorded in Award — Bigha",recordedAreaBiswa:"Total Area Recorded in Award — Biswa",recordedAreaBiswansi:"Total Area Recorded in Award — Biswansi",awardedAreaBigha:"Area Awarded — Bigha",awardedAreaBiswa:"Area Awarded — Biswa",awardedAreaBiswansi:"Area Awarded — Biswansi"} as Record<string,string>)[name]||name.replace(/([A-Z])/g," $1");
  return <section className="section"><h2>Verified sources</h2>{Array.from(groups).map(([key,values])=>{const e=values[0];return <details key={key} className="source-note"><summary>{e.documentName} · Page {e.pageNumber} · Verified {date(e.verifiedAt)} · <a href={e.sourceUrl} target="_blank" rel="noreferrer">View source</a></summary><p>Verified by {e.verifiedBy}</p><dl>{values.map(f=><div key={f.id}><dt>{factLabel(f.factName)}</dt><dd>{String(JSON.parse(f.confirmedValueJson))}</dd></div>)}</dl></details>;})}<Pagination {...result.data} onChange={setPage}/></section>;
}

function AwardPdfImportPanel({ award, onClose }: { award?: any; onClose?: () => void }) {
  const [file,setFile]=useState<File>();const [message,setMessage]=useState("");const [busy,setBusy]=useState(false);
  const submit=async()=>{try{if(!file)throw new Error("Choose a PDF file.");setBusy(true);setMessage("");await uploadAwardPdf(award?.id,award?.villages?.length===1?award.villages[0].id:undefined,file);if(onClose)onClose();else setMessage("PDF stored. Choose an Award workspace to analyze it later.");}catch(error){setMessage(error instanceof Error?error.message:"Could not store the PDF.");}finally{setBusy(false);}};
  const body=<section className="workspace-panel"><div className="panel-title"><h3>Upload Award PDF</h3>{onClose&&<button className="quiet-button" onClick={onClose}>Close</button>}</div><p>The PDF is stored under this Award immediately. You can view it now and choose analysis later.</p><div className="field-grid"><label>Award<input readOnly value={award?.awardNumber||"No Award selected"} /></label><label className="span-two">PDF file<input type="file" accept="application/pdf,.pdf" onChange={e=>setFile(e.target.files?.[0])}/></label></div><div className="form-footer"><span className="hint">Uploading does not start OCR or add any records.</span><button disabled={busy||!file} onClick={submit}>{busy?"Uploading…":"Upload PDF"}</button></div>{message&&<p className="form-message">{message}</p>}</section>;
  return onClose?<aside className="workflow-drawer" aria-label="Upload Award PDF">{body}</aside>:<><Breadcrumbs items={[{label:"Awards",to:"/awards"},{label:"Upload Award PDF"}]}/><PageHeader eyebrow="Award document" title="Upload Award PDF"><p>Store the document first. Analysis is optional and can be started later.</p></PageHeader>{body}</>;
}

function AreaInputGroup({ label, value, onChange, readOnly = false }: { label: string; value: { bigha: string; biswa: string; biswansi: string }; onChange?: (value: { bigha: string; biswa: string; biswansi: string }) => void; readOnly?: boolean }) {
  const field = (key: "bigha" | "biswa" | "biswansi", caption: string, integer = false) => <label>{caption}<input value={value[key]} readOnly={readOnly} inputMode="decimal" onChange={e => { if (!readOnly) onChange?.({ ...value, [key]: integer ? e.target.value.replace(/[^0-9]/g, "") : e.target.value }); }} /></label>;
  return <fieldset className="area-fieldset"><legend>{label}</legend><div className="area-inputs">{field("bigha", "Bigha")}{field("biswa", "Biswa", true)}{field("biswansi", "Biswansi", true)}</div></fieldset>;
}

function AwardKhasraPanel({
  award,
  onClose,
  onSaved,
}: {
  award: any;
  onClose: () => void;
  onSaved: () => void;
}) {
  const [villageId, setVillageId] = useState(award.villages[0]?.id || "");
  const [number, setNumber] = useState("");
  const [qualifier, setQualifier] = useState("");
  const [canonical, setCanonical] = useState({ bigha: "", biswa: "", biswansi: "" });
  const [recorded, setRecorded] = useState({ bigha: "", biswa: "", biswansi: "" });
  const [awarded, setAwarded] = useState({ bigha: "", biswa: "", biswansi: "" });
  const [message, setMessage] = useState("");
  const matchPath = villageId && number.trim() ? path(`/awards/${award.id}/khasras/match`, { villageId, khasraNumber: number.trim(), qualifier: qualifier.trim() || undefined }) : undefined;
  const match = useApi<any>(matchPath);
  const numberOrNull = (value: string) => value.trim() === "" ? null : Number(value);
  const integerOrNull = (value: string) => value.trim() === "" ? null : Number(value);
  const save = async () => {
    try {
      await post(`/awards/${award.id}/khasras`, {
        villageId,
        khasraNumber: number,
        qualifier: qualifier || null,
        recordedTotalAreaBigha: numberOrNull(recorded.bigha),
        recordedTotalAreaBiswa: integerOrNull(recorded.biswa),
        recordedTotalAreaBiswansi: integerOrNull(recorded.biswansi),
        awardedAreaBigha: numberOrNull(awarded.bigha),
        awardedAreaBiswa: integerOrNull(awarded.biswa),
        awardedAreaBiswansi: integerOrNull(awarded.biswansi),
        relationshipStatus: "Recorded",
        remarks: null,
        canonicalAreaBigha: match.data?.isExisting ? null : numberOrNull(canonical.bigha),
        canonicalAreaBiswa: match.data?.isExisting ? null : integerOrNull(canonical.biswa),
        canonicalAreaBiswansi: match.data?.isExisting ? null : integerOrNull(canonical.biswansi),
      });
      onSaved();
    } catch (e) {
      setMessage(e instanceof Error ? e.message : "Could not link Khasra.");
    }
  };
  return (
    <aside className="workflow-drawer" aria-label="Add or link Khasra"><section className="workspace-panel">
      <div className="panel-title">
        <h3>Add / Link Khasra</h3>
        <button onClick={onClose}>Close</button>
      </div>
      <p>Link an existing Village Khasra, or add a missing Khasra for master review.</p>
      <div className="field-grid">
        <label className="span-two">
          Village
          <select
            value={villageId}
            onChange={(e) => setVillageId(e.target.value)}
          >
            {award.villages.map((v: any) => (
              <option key={v.id} value={v.id}>
                {v.name}
              </option>
            ))}
          </select>
        </label>
        <label>
          Khasra Number
          <input value={number} onChange={(e) => setNumber(e.target.value)} placeholder="e.g. 22//2/1" />
        </label>
        <label>
          Qualifier
          <input
            value={qualifier}
            onChange={(e) => setQualifier(e.target.value)}
            placeholder="min"
          />
        </label>
      </div>
      {number.trim() && (match.loading ? <p className="form-message">Searching Village Khasra…</p> : match.error ? <p className="form-message">Could not check this Khasra yet. Please correct the number or try again.</p> : match.data?.isExisting ? <><p className="form-message">✓ Existing Khasra found in {award.villages.find((v: any) => v.id === villageId)?.name}. This master record will be linked to the Award.</p><AreaInputGroup label="Village Master Area" readOnly value={{ bigha: String(match.data.canonicalAreaBigha ?? ""), biswa: String(match.data.canonicalAreaBiswa ?? ""), biswansi: String(match.data.canonicalAreaBiswansi ?? "") }} /></> : <><p className="form-message">New Khasra. This Khasra will be added to {award.villages.find((v: any) => v.id === villageId)?.name} and marked for master review.</p><AreaInputGroup label="Village Master Area (optional)" value={canonical} onChange={setCanonical} /></>)}
      <AreaInputGroup label="Total Area Recorded in Award" value={recorded} onChange={setRecorded} />
      <AreaInputGroup label="Area Awarded in this Award" value={awarded} onChange={setAwarded} />
      <div className="form-footer">
        <button className="secondary-button" onClick={onClose}>Cancel</button>
        <button onClick={save} disabled={!villageId || !number.trim() || match.loading || Boolean(match.error)}>
          Add to Award
        </button>
      </div>
      {message && <p className="form-message">{message}</p>}
    </section></aside>
  );
}

function AwardRelatedPanel({ award, khasras, onClose, onSaved }: { award: any; khasras: any[]; onClose: () => void; onSaved: () => void }) {
  const [kind, setKind] = useState("notification");
  const [reference, setReference] = useState("");
  const [detail, setDetail] = useState("");
  const [amountValue, setAmountValue] = useState("");
  const [selectedKhasras, setSelectedKhasras] = useState<string[]>([]);
  const [message, setMessage] = useState("");
  const notifications = useApi<Page<any>>(path("/notifications", { page: 0, pageSize: 100 }));
  const toggle = (value: string) => setSelectedKhasras(values => values.includes(value) ? values.filter(x => x !== value) : [...values, value]);
  const save = async () => {
    try {
      const khasraIds = selectedKhasras;
      if (kind === "notification") {
        if (!reference) throw new Error("Select a canonical Notification.");
        await post(`/awards/${award.id}/notifications/${reference}`, {});
      } else if (kind === "possession") await post(`/awards/${award.id}/possession-events`, { possessionDate: null, eventType: reference || null, status: detail || null, remarks: null, khasraIds });
      else if (kind === "court") await post(`/awards/${award.id}/court-cases`, { caseNumber: reference, courtName: detail, caseType: null, filedDate: null, currentStatus: null, remarks: null, khasraIds });
      else if (kind === "claim") await post(`/awards/${award.id}/claims`, { claimReference: reference || null, claimDate: null, claimText: detail || null, claimedRateAmount: amountValue === "" ? null : Number(amountValue), claimedRateUnit: null, claimedAmount: null, status: "Received", remarks: null, khasraIds });
      else if (kind === "land-class") await post(`/awards/${award.id}/land-classes`, { code: reference, description: detail || null });
      else if (kind === "valuation") await post(`/awards/${award.id}/valuation-rules`, { awardLandClassId: null, ruleType: reference || "Other", rateAmount: amountValue === "" ? null : Number(amountValue), rateUnit: null, referenceDate: null, legalSection: null, description: detail || null });
      else if (kind === "compensation") await post(`/awards/${award.id}/compensation-rules`, { ruleType: reference || "Other", ratePercent: amountValue === "" ? null : Number(amountValue), rateAmount: null, legalSection: null, basisDescription: detail || null, startEvent: null, endEvent: null, remarks: null });
      else if (kind === "area-issue") await post(`/awards/${award.id}/area-issues`, { khasraId: khasraIds[0] || null, issueType: reference || "Other", notificationAreaBigha: null, fieldBookAreaBigha: null, differenceBigha: amountValue === "" ? null : Number(amountValue), status: "Open", corrigendumReference: null, corrigendumDate: null, remarks: detail || null });
      else await post(`/awards/${award.id}/supplementary-matters`, { matterType: reference || "Other", status: "Pending", description: detail || null, supplementaryAwardId: null });
      onSaved();
    } catch (e) { setMessage(e instanceof Error ? e.message : "Could not save the related record."); }
  };
  const needsKhasras = ["possession", "court", "claim", "area-issue"].includes(kind);
  const labels: Record<string, string> = { notification: "Notification", possession: "Possession Event", court: "Court Case", claim: "Claim", "land-class": "Land Classification", valuation: "Valuation Rule", compensation: "Compensation Rule", "area-issue": "Area Issue / Corrigendum", supplementary: "Supplementary Matter" };
  return <aside className="workflow-drawer" aria-label="Add related Award record"><section className="workspace-panel"><div className="panel-title"><h3>Add Related Record</h3><button onClick={onClose}>Close</button></div><p>Choose a record type, then complete only the fields relevant to that record. Court cases do not infer any legal restraint.</p><div className="related-menu">{Object.entries(labels).map(([value, label]) => <button key={value} className={kind === value ? "active" : ""} onClick={() => { setKind(value); setReference(""); setDetail(""); setAmountValue(""); }}>{label}</button>)}</div><div className="field-grid">{kind === "notification" ? <label>Canonical Notification<select value={reference} onChange={e => setReference(e.target.value)}><option value="">Select Notification</option>{notifications.data?.items.map(n => <option key={n.id} value={n.id}>{n.notificationNumber} · Section {n.sectionType}</option>)}</select></label> : <label>{kind === "court" ? "Case number" : kind === "land-class" ? "Classification code" : kind === "claim" ? "Claim reference" : kind === "possession" ? "Event type" : "Rule / matter type"}<input value={reference} onChange={e => setReference(e.target.value)} /></label>}<label>{kind === "court" ? "Court name" : kind === "claim" ? "Claim details" : kind === "possession" ? "Event status" : "Details"}<input value={detail} onChange={e => setDetail(e.target.value)} /></label>{["claim", "valuation", "compensation", "area-issue"].includes(kind) && <label>{kind === "compensation" ? "Rate percent" : kind === "valuation" ? "Rate amount" : "Amount / difference"}<input value={amountValue} onChange={e => setAmountValue(e.target.value)} inputMode="decimal" /></label>}</div>{needsKhasras && <fieldset className="award-khasra-picker"><legend>Affected Award Khasras</legend>{khasras.length ? khasras.map(k => <label key={k.khasraId}><input type="checkbox" checked={selectedKhasras.includes(k.khasraId)} onChange={() => toggle(k.khasraId)} /> {k.displayNumber} · {k.villageName}</label>) : <span className="hint">Link a Khasra before recording this type of related record.</span>}</fieldset>}<div className="form-footer"><span className="hint">Only populated record types appear in the Award overview.</span><button onClick={save} disabled={(needsKhasras && !selectedKhasras.length) || (kind === "court" && (!reference.trim() || !detail.trim()))}>Save {labels[kind]}</button></div>{message && <p className="form-message">{message}</p>}</section></aside>;
}

function AwardIngestion() {
  const { id = "" } = useParams(); const navigate = useNavigate(); const award = useApi<any>(`/awards/${id}/workspace`); const linkedVillages = useApi<any[]>(`/awards/${id}/ingestion-villages`); const [villageId, setVillageId] = useState(""); const [number, setNumber] = useState(""); const [qualifier, setQualifier] = useState(""); const [canonicalArea, setCanonicalArea] = useState(""); const [recordedArea, setRecordedArea] = useState(""); const [awardedArea, setAwardedArea] = useState(""); const [message, setMessage] = useState("");
  useEffect(() => { if (!villageId && linkedVillages.data?.length) setVillageId(linkedVillages.data[0].villageId); }, [linkedVillages.data, villageId]);
  if (award.loading || linkedVillages.loading) return <LoadingState />; if (award.error || linkedVillages.error || !award.data || !linkedVillages.data) return <ErrorState message={award.error || linkedVillages.error || "Award could not be loaded."} />; const a = award.data;
  const create = async () => { try { if (!villageId || !number.trim()) throw new Error("Select a directly linked Award Village and enter a Khasra number."); const payload = { khasraNumber: number, qualifier: qualifier || null, canonicalAreaBigha: canonicalArea === "" ? null : Number(canonicalArea), canonicalAreaBiswa: null, canonicalAreaBiswansi: null, recordedAreaBigha: recordedArea === "" ? null : Number(recordedArea), recordedAreaBiswa: null, recordedAreaBiswansi: null, awardedAreaBigha: awardedArea === "" ? null : Number(awardedArea), awardedAreaBiswa: null, awardedAreaBiswansi: null }; const result: any = await post("/award-ingestion-sessions", { sourceType: "Manual", targetAwardId: id, selectedVillageId: villageId, sourceDocumentId: null, createdBy: null, remarks: null, candidates: [{ candidateType: "AwardKhasra", payloadJson: JSON.stringify(payload) }] }); navigate(`/awards/${id}/ingestion/${result.id}`); } catch (e) { setMessage(e instanceof Error ? e.message : "Could not create ingestion preview."); } };
  return <><Breadcrumbs items={[{ label: "Awards", to: "/awards" }, { label: a.awardNumber, to: route.award(id) }, { label: "Import Award Data" }]} /><PageHeader eyebrow="Award data" title="Import Award Data"><p>Review incoming Award information before adding it to official records.</p></PageHeader><section className="import-intro"><h2>Review before adding</h2><p>Nothing will be added to official records until you review and confirm it. Start with a Khasra record below; bulk Excel upload will use the same review process.</p>{linkedVillages.data.length === 0 ? <p className="form-message">This Award does not yet have an Award Village. Add the Village to the Award before importing Khasras.</p> : <><div className="field-grid"><label>Award Village<select value={villageId} onChange={e => setVillageId(e.target.value)}>{linkedVillages.data.map(v => <option key={v.villageId} value={v.villageId}>{v.name}</option>)}</select></label><label>Khasra number<input value={number} onChange={e => setNumber(e.target.value)} placeholder="e.g. 2//22/1" /></label><label>Qualifier<input value={qualifier} onChange={e => setQualifier(e.target.value)} placeholder="min" /></label><label>Village master Bigha<input value={canonicalArea} onChange={e => setCanonicalArea(e.target.value)} inputMode="decimal" /></label><label>Award recorded Bigha<input value={recordedArea} onChange={e => setRecordedArea(e.target.value)} inputMode="decimal" /></label><label>Area awarded Bigha<input value={awardedArea} onChange={e => setAwardedArea(e.target.value)} inputMode="decimal" /></label></div><div className="form-footer"><span className="hint">Village master area and Award area remain separate.</span><button onClick={create}>Review Incoming Record</button></div></>}{message && <p className="form-message">{message}</p>}</section></>;
}
function AwardIngestionReview() {
  const { id = "", sessionId = "" } = useParams();
  const [refresh, setRefresh] = useState(0);
  const [bucket, setBucket] = useState("attention");
  const [type, setType] = useState("");
  const [page, setPage] = useState(0);
  const [sourcePage, setSourcePage] = useState("");
  const [reviewer, setReviewer] = useState(()=>sessionStorage.getItem("lac.reviewOfficer")||"");
  const [message, setMessage] = useState("");
  const [active, setActive] = useState<any>();
  const [confirmAction, setConfirmAction] = useState<"exact" | "commit">();
  const [busy, setBusy] = useState(false);
  const summary = useApi<any>(`/award-ingestion-sessions/${sessionId}/overview?r=${refresh}`);
  const records = useApi<Page<any>>(path(`/award-ingestion-sessions/${sessionId}/candidates`, {page,pageSize:25,bucket,type:type || undefined,sourcePage:sourcePage || undefined,r:refresh}));
  if (summary.error) return <div className="review-workspace"><Breadcrumbs items={[{label:"Awards",to:"/awards"},...(id?[{label:"Award",to:route.award(id)}]:[]),{label:"Document review"}]} /><section className="workspace-panel stale-review-panel"><h2>This document review is no longer available</h2><p>The saved review session could not be found. This usually happens when an older analysis was re-analyzed or its temporary review was cleared. No canonical Award data was deleted by this page.</p><Link className="primary-link" to={id?"/awards/"+id+"/ingestion":"/awards"}>{id?"Return to Award document review":"Return to Awards"}</Link></section></div>;
  if (!summary.data) return <LoadingState />;
  const s = summary.data;
  const groups: any[] = s.sections;
  const count = (predicate: (g:any)=>boolean) => groups.filter(predicate).reduce((sum,g)=>sum+g.count,0);
  const exact = count(g=>g.safeToConfirm && !g.verified && g.status==="Ready");
  const verified = count(g=>g.verified && g.status==="Ready");
  const committed = count(g=>g.status==="Committed");
  const skipped = count(g=>g.status==="Skipped" || g.status==="Rejected");
  const conflicts = count(g=>["Conflict","Ambiguous","DuplicateInBatch"].includes(g.status));
  const unreadable = count(g=>g.status==="Invalid");
  const attention = count(g=>!g.safeToConfirm && !g.verified && !["Conflict","Ambiguous","DuplicateInBatch","Invalid","Committed","Skipped","Rejected"].includes(g.status));
  const pending = exact + attention + conflicts + unreadable;
  const exactSelection = sourcePage ? s.pages.find((p:any)=>String(p.page)===sourcePage)?.exact || 0 : exact;
  const changed = () => {setPage(0);setRefresh(v=>v+1);};
  const runConfirm = async () => {
    if (!reviewer.trim()) {setMessage("Enter the verifying officer's name.");return;}
    setBusy(true);setMessage("");
    try {
      await post(`/award-ingestion-sessions/${sessionId}/${confirmAction==="exact"?"confirm-exact":"commit-verified"}`,{verifiedBy:reviewer.trim(),expectedCount:confirmAction==="exact"?exactSelection:verified,sourcePage:sourcePage?Number(sourcePage):null});
      setConfirmAction(undefined);setBucket(confirmAction==="exact"?"verified":"attention");changed();
    } catch(e) {setMessage(e instanceof Error?e.message:"Confirmation failed.");} finally {setBusy(false);}
  };
  const payload = (c:any) => {try{return JSON.parse(c.payloadJson);}catch{return {};}};
  const display = (c:any) => {const p=payload(c);return p.khasraNumber ? `${p.khasraNumber}${p.qualifier?` ${p.qualifier}`:""}` : p.notificationNumber || p.awardNumber || p.villageName || p.caseNumber || p.claimReference || p.possessionDate || p.category || p.ruleType || p.matterType || "Source finding";};
  const reason = (c:any) => {try {const e=JSON.parse(c.sourceLocatorJson||"{}");return (e.Warnings||e.warnings||[]).join(" ") || e.Reason || e.reason || "Check source and confirm.";}catch{return "Check source and confirm.";}};
  const firstReviewable = records.data?.items.find(c=>!c.verifiedAt) || records.data?.items[0];
  return <div className="review-workspace">
    <Breadcrumbs items={[{label:"Awards",to:"/awards"},...(s.targetAwardId?[{label:"Award",to:route.award(s.targetAwardId)}]:[]),{label:"Document review"}]} />
    <PageHeader eyebrow="Document verification" title={s.documentName||"Award document review"}><p>{s.awardNumber?`Award ${s.awardNumber}`:"Award context pending"}{s.villageName?` · ${s.villageName}`:""} · {s.totalPages?`${s.processedPages||0} of ${s.totalPages} pages processed`:"Stored source document"}</p></PageHeader>
    {!s.targetAwardId && <ReviewContextPicker sessionId={sessionId} reviewer={reviewer} onReviewer={setReviewer} onSaved={changed}/>}
    {s.targetAwardId && <div className="review-context-line"><span>Review linked to <Link to={route.award(s.targetAwardId)}>Award workspace</Link></span><span>Original PDF retained · analysis status: {s.analysisStatus||"Review ready"}</span></div>}
    <div className="verification-summary">
      {[["exact","Exact matches",exact],["attention","Need attention",attention],["conflict","Conflicts",conflicts],["unreadable","Could not read",unreadable],["verified","Human verified",verified],["committed","Committed",committed]].map(([value,label,total])=><button key={String(value)} aria-pressed={bucket===value} onClick={()=>{setBucket(String(value));setPage(0);}}><b>{total}</b>{label}</button>)}
    </div>
    <section className="verification-status-note" aria-label="Review progress"><div><strong>{pending}</strong><span>still needs a decision</span></div><div><strong>{verified}</strong><span>verified and waiting for commit</span></div><div><strong>{committed}</strong><span>already committed to official records</span></div>{skipped>0&&<div><strong>{skipped}</strong><span>skipped / set aside</span></div>}<p>Verification is saved immediately and this page can be left safely. Commit only the human-verified records; committed items remain auditable here and are not re-created.</p></section>
    <div className="verification-toolbar">
      <label>Section<select value={type} onChange={e=>{setType(e.target.value);setPage(0);}}><option value="">All sections</option>{Array.from(new Set(groups.map(g=>g.candidateType))).map(t=><option key={String(t)} value={String(t)}>{reviewSectionName(String(t))} · {count(g=>g.candidateType===t)}</option>)}</select></label>
      <label>Source page<select value={sourcePage} onChange={e=>{setSourcePage(e.target.value);setPage(0);}}><option value="">All pages</option>{s.pages.filter((p:any)=>p.page).map((p:any)=><option key={p.page} value={p.page}>Page {p.page} · {p.count} Khasras · {p.exact} exact</option>)}</select></label>
      <label>Verifying officer<input value={reviewer} onChange={e=>{setReviewer(e.target.value);sessionStorage.setItem("lac.reviewOfficer",e.target.value);}} placeholder="Your name" autoComplete="name" /></label>
      {firstReviewable&&<button className="quiet-button" onClick={()=>setActive(firstReviewable)}>Open review workbench</button>}
      {exactSelection>0&&<button disabled={!s.targetAwardId || busy} onClick={()=>setConfirmAction("exact")}>Confirm {exactSelection} exact matches</button>}
      {verified>0&&<button disabled={!s.targetAwardId || busy} onClick={()=>setConfirmAction("commit")}>Commit {verified} verified records</button>}
    </div>
    {confirmAction && <section className="workspace-panel" role="alertdialog" aria-label="Confirm reviewed group"><h3>{confirmAction==="exact"?"Confirm exact Khasra matches":"Commit human-verified records"}</h3><p>{confirmAction==="exact"?`${exactSelection} existing Khasras will be confirmed for linking. 0 new Khasras. 0 uncertain rows or conflicts included. This step verifies only; commit remains separate.`:`${verified} human-verified records will be committed. Their document, page, confirmed values and your verification will be preserved permanently.`}</p><button disabled={busy} onClick={runConfirm}>Confirm</button> <button disabled={busy} onClick={()=>setConfirmAction(undefined)}>Cancel</button></section>}
    {message && <p className="form-message" role="alert">{message}</p>}
    {records.error ? <ErrorState message={records.error}/> : records.data ? <section className="section"><h2>{bucket==="attention"?"Items needing your attention":bucket==="exact"?"Exact matches":bucket==="verified"?"Human-verified records":bucket==="committed"?"Committed records":"Review items"}</h2>
      <DataTable headers={["Section","Detected value","Source page","Review","Action"]}>{records.data.items.map(c=><tr key={c.id}><td>{reviewSectionName(c.candidateType)}</td><td>{display(c)}</td><td>{c.sourcePage || (()=>{try{const source=JSON.parse(c.sourceLocatorJson||"{}");return source.Page||source.page||"Not identified";}catch{return "Not identified";}})()}</td><td>{c.verifiedAt?`Verified by ${c.verifiedBy}`:c.safeToConfirm?"Exact match":c.status==="Conflict"?"Conflict":c.status==="Invalid"?"Could not read":"Needs attention"}</td><td><button className="link-button" onClick={()=>setActive(c)}>{c.verifiedAt?"View source":"Review / correct"}</button></td></tr>)}</DataTable>
      {records.data.items.length===0 && <p>No items in this group.</p>}
      <Pagination {...records.data} onChange={setPage}/>
      {active && <FactVerificationDrawer key={active.id} candidate={active} documentId={s.sourceDocumentId} awardId={s.targetAwardId || id} reviewer={reviewer} reason={reason(active)} onClose={()=>setActive(undefined)} onSaved={()=>{const next=records.data?.items[records.data.items.findIndex(c=>c.id===active.id)+1];setActive(next);changed();}}/>}
    </section>:<LoadingState/>}
  </div>;
}

function ReviewContextPicker({sessionId,reviewer,onReviewer,onSaved}:{sessionId:string;reviewer:string;onReviewer:(value:string)=>void;onSaved:()=>void}) {
  const [query,setQuery]=useState("");const [awardId,setAwardId]=useState("");const [villageId,setVillageId]=useState("");const [message,setMessage]=useState("");const [busy,setBusy]=useState(false);
  const awards=useApi<Page<any>>(path("/awards",{page:0,pageSize:25,q:query}));
  const award=useApi<any>(awardId?`/awards/${awardId}/workspace`:undefined);
  const selectedVillage=villageId||(award.data?.villages?.length===1?award.data.villages[0].id:"");
  const save=async(e:FormEvent)=>{e.preventDefault();setBusy(true);setMessage("");try{await post(`/award-ingestion-sessions/${sessionId}/context`,{awardId,villageId:selectedVillage,verifiedBy:reviewer});sessionStorage.setItem("lac.reviewOfficer",reviewer);onSaved();}catch(error){setMessage(error instanceof Error?error.message:"Could not link the review.");}finally{setBusy(false);}};
  return <form className="review-context-card" onSubmit={save}><div><span className="review-step">01 · Set up this review</span><h2>Which Award does this document belong to?</h2><p>Select once to enable matching and confirmation. Your uploaded PDF and extracted pages will be reused.</p></div><div className="verification-toolbar"><label>Find Award<input placeholder="Search Award number" value={query} onChange={e=>setQuery(e.target.value)}/></label><label>Award<select required value={awardId} onChange={e=>{setAwardId(e.target.value);setVillageId("");}}><option value="">Choose Award</option>{awards.data?.items.map(a=><option key={a.id} value={a.id}>{a.awardNumber}</option>)}</select></label><label>Official Village<select required value={selectedVillage} onChange={e=>setVillageId(e.target.value)}><option value="">Choose Village</option>{award.data?.villages?.map((v:any)=><option key={v.id} value={v.id}>{v.name}</option>)}</select></label><label>Your name<input required value={reviewer} onChange={e=>onReviewer(e.target.value)} placeholder="Verifying officer"/></label><button type="submit" disabled={busy||!awardId||!selectedVillage||!reviewer.trim()}>{busy?"Linking…":"Use this Award →"}</button></div>{message&&<p role="alert">{message}</p>}{awards.error&&<p role="alert">{awards.error}</p>}</form>;
}

function reviewSectionName(type:string) {return ({AwardKhasra:"Khasras",AwardCore:"Award details",AwardVillage:"Village",Notification:"Notifications",PossessionEvent:"Possession",CourtCase:"Court cases",Claim:"Claims",AwardLandClass:"Land classification",AwardValuationRule:"Valuation",AwardCompensationRule:"Compensation",AwardAreaIssue:"Area issues",AwardSupplementaryMatter:"Supplementary matters",UnmappedAwardFinding:"Other / narrative evidence"} as Record<string,string>)[type] || type;}

function FactVerificationDrawer({candidate,documentId,awardId,reviewer,reason,onClose,onSaved}:{candidate:any;documentId:string;awardId:string;reviewer:string;reason:string;onClose:()=>void;onSaved:()=>void}) {
  const [value,setValue]=useState<any>(()=>{try{return JSON.parse(candidate.payloadJson);}catch{return {};}});
  const [message,setMessage]=useState("");const [busy,setBusy]=useState(false);const [officer,setOfficer]=useState(reviewer||sessionStorage.getItem("lac.reviewOfficer")||"");const context=useApi<any>(awardId?`/awards/${awardId}/workspace`:undefined);
  let locator:any={};try{locator=JSON.parse(candidate.sourceLocatorJson||"{}");}catch{/* No guessed source. */}
  const page=candidate.sourcePage || locator.Page || locator.page;
  const sourceTerm=(candidate.rawSourceText||"").trim().slice(0,120);
  const source=page&&documentId?`${api}/documents/${documentId}/content#page=${page}&view=FitH&navpanes=0${sourceTerm?`&search=${encodeURIComponent(sourceTerm)}`:""}`:undefined;
  const supported=["AwardCore","AwardVillage","AwardKhasra","Notification","PossessionEvent","CourtCase","Claim","AwardLandClass","AwardValuationRule","AwardCompensationRule","AwardAreaIssue","AwardSupplementaryMatter"].includes(candidate.candidateType);
  useEffect(()=>{const previous=document.body.style.overflow;document.body.style.overflow="hidden";return()=>{document.body.style.overflow=previous;};},[]);
  useEffect(()=>{const close=(event:KeyboardEvent)=>{if(event.key==="Escape")onClose();};window.addEventListener("keydown",close);return()=>window.removeEventListener("keydown",close);},[onClose]);
  const field=(key:string,label:string,kind:"text"|"number"|"date"="text")=><label key={key}>{label}<input type={kind} step={kind==="number"?"any":undefined} value={value[key]??""} onChange={e=>setValue({...value,[key]:e.target.value===""?null:kind==="number"?Number(e.target.value):e.target.value})}/></label>;
  const area=(prefix:string,label:string)=><fieldset><legend>{label}</legend><div className="field-grid">{field(prefix+"Bigha","Bigha","number")}{field(prefix+"Biswa","Biswa","number")}{field(prefix+"Biswansi","Biswansi","number")}</div></fieldset>;
  const save=async(action:string)=>{setBusy(true);setMessage("");try{await post(`/award-ingestion-candidates/${candidate.id}/verify`,{verifiedBy:officer,correctedPayloadJson:JSON.stringify(value),action});onSaved();}catch(e){setMessage(e instanceof Error?e.message:"Could not verify.");}finally{setBusy(false);}};
  const guide=candidate.candidateType==="AwardVillage"?"Compare the detected Village name with the PDF on the right, choose the official master spelling, enter your name, then confirm. This only verifies the Village link; it does not commit the PDF automatically.":candidate.candidateType==="AwardKhasra"?"Read the Khasra and both Award area columns from the PDF. Keep qualifiers such as min exactly as shown. Confirm only when the fields and source page agree.":candidate.candidateType==="AwardCore"?"Confirm the Award number and date against the PDF. Correct only what is visibly supported; unclear digits must stay for review.":"Compare the extracted value with the source page, correct only clear values, then confirm or skip it.";
  return <aside className="evidence-workbench" role="dialog" aria-modal="true" aria-label="Verify source fact"><form className="evidence-editor" onSubmit={e=>{e.preventDefault();if(supported&&!busy&&awardId&&officer.trim()&&!candidate.verifiedAt)void save("Confirm");}}>
    <div className="panel-title"><div><span className="review-step">Review source fact</span><h2>{reviewSectionName(candidate.candidateType)}</h2></div><button type="button" onClick={onClose}>← Back to review</button></div><div className="review-guide"><strong>What to do here</strong><p>{guide}</p></div><p className="review-reason"><strong>Why it needs review:</strong> {reason}</p>{!awardId&&<p className="review-blocker">Choose the Award on the review page first to enable confirmation.</p>}{(locator.PossibleCanonicalMatch||locator.possibleCanonicalMatch)&&<p>Possible master: {locator.PossibleCanonicalMatch||locator.possibleCanonicalMatch}. Enter that identifier yourself to link it; no digit is changed automatically.</p>}
    {candidate.candidateType==="AwardCore"?<><div className="field-grid">{field("awardNumber","Award number")}{field("awardDate","Award date","date")}{field("awardType","Nature / type")}</div><label>Purpose / nature of acquisition<textarea value={value.purpose||""} onChange={e=>setValue({...value,purpose:e.target.value||null})}/></label><p className="hint">Enter only what is visible in the PDF. An unreadable award number is never guessed.</p></>:candidate.candidateType==="AwardVillage"?<><label>Official Village<select value={value.villageName||""} onChange={e=>setValue({...value,villageName:e.target.value,exactCanonicalVillageName:e.target.value})}><option value="">Select the official spelling</option>{context.data?.villages?.map((v:any)=><option value={v.name} key={v.id}>{v.name}</option>)}</select></label><p className="hint">Detected: {JSON.parse(candidate.payloadJson).villageName}. Confirm the official Village linked to this Award.</p></>:candidate.candidateType==="AwardKhasra"?<><div className="field-grid">{field("khasraNumber","Khasra number")}{field("qualifier","Qualifier")}</div>{area("recordedArea","Total Area Recorded in Award")}{area("awardedArea","Area Awarded in this Award")}<p className="hint">Village Master Area is separate and will not be edited here. A missing Khasra will be flagged for master review.</p></>:candidate.candidateType==="Notification"?<div className="field-grid">{field("sectionType","Section")}{field("notificationNumber","Notification number")}{field("notificationDate","Notification date","date")}</div>:candidate.candidateType==="PossessionEvent"?<><div className="field-grid">{field("possessionDate","Possession date","date")}{field("eventType","Event description")}{field("status","Recorded possession status")}</div><p>No affected parcels are inferred. Link the specific Khasras in the Award possession workflow; this does not change Award status.</p></>:candidate.candidateType==="CourtCase"?<><div className="field-grid">{field("caseNumber","Case number")}{field("courtName","Court")}{field("caseType","Case type")}</div><p>This confirms the case reference only, not a stay or affected parcels.</p></>:candidate.candidateType==="Claim"?<><div className="field-grid">{field("claimReference","Claim reference")}{field("claimDate","Claim date","date")}</div><label>Claim text<textarea value={value.claimText||""} onChange={e=>setValue({...value,claimText:e.target.value})}/></label><p>Names do not identify or merge Parties automatically.</p></>:candidate.candidateType==="AwardLandClass"?<div className="field-grid">{field("code","Class code")}{field("description","Description")}</div>:candidate.candidateType==="AwardValuationRule"?<div className="field-grid">{field("ruleType","Valuation rule")}{field("rateAmount","Rate amount","number")}{field("rateUnit","Rate unit")}{field("legalSection","Legal section")}</div>:candidate.candidateType==="AwardCompensationRule"?<div className="field-grid">{field("ruleType","Compensation rule")}{field("ratePercent","Rate percent","number")}{field("rateAmount","Amount","number")}{field("legalSection","Legal section")}</div>:candidate.candidateType==="AwardAreaIssue"?<div className="field-grid">{field("issueType","Issue")}{field("notificationAreaBigha","Notification area (Bigha)","number")}{field("fieldBookAreaBigha","Field-book area (Bigha)","number")}{field("differenceBigha","Difference (Bigha)","number")}</div>:candidate.candidateType==="AwardSupplementaryMatter"?<div className="field-grid">{field("matterType","Matter")}{field("description","Description")}</div>:<p>{value.summary||"This is source evidence, not a structured record ready for confirmation."}</p>}
    <section className="source-spotlight"><div><span>Source match</span><strong>Page {page||"not identified"}</strong></div><mark>{candidate.rawSourceText||value.extractedText||"No readable text was returned; inspect the page manually."}</mark><small>The PDF opens on this page automatically. If the PDF contains searchable text, the browser will also try to find this line.</small></section><details open><summary>Original extracted text</summary><p style={{whiteSpace:"pre-wrap",overflowWrap:"anywhere"}}>{candidate.rawSourceText||value.extractedText||"No readable text."}</p></details>
    {message&&<p role="alert" className="form-message">{message}</p>}
    <div className="form-footer"><label className="review-officer">Verifying officer<input value={officer} onChange={e=>{setOfficer(e.target.value);sessionStorage.setItem("lac.reviewOfficer",e.target.value);}} placeholder="Your name"/></label>{!candidate.verifiedAt&&<><span className="review-action-hint">Confirm saves this review decision; it does not silently alter unrelated records.</span>{supported&&<button type="submit" disabled={busy||!awardId||!officer.trim()}>{busy?"Saving…":"Confirm & next →"}</button>}{candidate.candidateType==="AwardKhasra"&&<button type="button" disabled={busy||!awardId||!officer.trim()} onClick={()=>save("LinkExisting")}>Link existing</button>}<button type="button" disabled={busy||!officer.trim()} onClick={()=>save("Skip")}>Skip</button></>}</div>
  </form><section className="evidence-viewer"><header><strong>Source · Page {page||"not identified"}</strong>{source&&<a href={source} target="_blank" rel="noreferrer">Open full PDF</a>}</header>{source?<iframe key={source} src={source} title={`Original PDF, initially page ${page}`}/>:<p>No valid document page is available. Confirmation is blocked.</p>}</section></aside>;
}

function Notifications() {
  const [page, setPage] = useState(0);
  const [query, setQuery] = useState("");
  const result = useApi<Page<any>>(
    path("/notifications", { page, pageSize: 25, q: query }),
  );
  return (
    <>
      <Breadcrumbs items={[{ label: "Notifications" }]} />
      <PageHeader eyebrow="Acquisition notifications" title="Notifications" />
      <section className="section">
        <div className="section-heading">
          <h2>Notification register</h2>
          <SearchInput
            value={query}
            onChange={(value) => {
              setPage(0);
              setQuery(value);
            }}
            placeholder="Search notification number"
          />
        </div>
        {result.loading ? (
          <LoadingState />
        ) : result.error ? (
          <ErrorState message={result.error} />
        ) : !result.data?.items.length ? (
          <EmptyState
            title="No notifications match"
            detail="Adjust the notification number search."
          />
        ) : (
          <>
            <DataTable headers={["Notification", "Section", "Date"]}>
              {result.data.items.map((notification) => (
                <tr key={notification.id}>
                  <td>
                    <EntityLink to={route.notification(notification.id)}>
                      {notification.notificationNumber}
                    </EntityLink>
                  </td>
                  <td>Section {notification.sectionType}</td>
                  <td>{date(notification.notificationDate)}</td>
                </tr>
              ))}
            </DataTable>
            <Pagination {...result.data} onChange={setPage} />
          </>
        )}
      </section>
    </>
  );
}
function Notification() {
  const { id = "" } = useParams();
  const result = useApi<any>(`/notifications/${id}`);
  if (result.loading) return <LoadingState />;
  if (result.error) return <ErrorState message={result.error} />;
  if (!result.data) return null;
  const n = result.data;
  return (
    <>
      <Breadcrumbs
        items={[
          { label: "Notifications", to: "/notifications" },
          { label: n.notificationNumber },
        ]}
      />
      <PageHeader
        eyebrow={`Section ${n.sectionType} notification`}
        title={n.notificationNumber}
      >
        <p>{date(n.notificationDate)}</p>
      </PageHeader>
      <section className="detail-grid">
        <InfoSection
          title="Notification information"
          rows={[
            ["Section type", n.sectionType],
            ["Gazette details", n.gazetteDetails || "—"],
            ["Project", n.project?.name || "—"],
            ["Requiring agency", n.project?.requiringAgency || "—"],
            ["Remarks", n.remarks || "—"],
          ]}
        />
      </section>
      <section className="section">
        <PermanentSourceLines endpoint={`/notifications/${id}/evidence`} />
        <h2>Linked khasras</h2>
        {n.khasras.length ? (
          <DataTable headers={["Khasra no.", "Village", "Notified area"]}>
            {n.khasras.map((k: any) => (
              <tr key={k.id}>
                <td>
                  <EntityLink to={route.khasra(k.id)}>
                    {k.displayNumber}
                  </EntityLink>
                </td>
                <td>{k.villageName}</td>
                <td>{amount(k.notifiedArea, k.areaUnit)}</td>
              </tr>
            ))}
          </DataTable>
        ) : (
          <EmptyState
            title="No linked khasras"
            detail="This notification has no parcel links."
          />
        )}
      </section>
    </>
  );
}

function Documents() {
  const result = useApi<Page<any>>(
    path("/documents", { page: 0, pageSize: 25 }),
  );
  return (
    <>
      <Breadcrumbs items={[{ label: "Documents" }]} />
      <PageHeader eyebrow="Document metadata" title="Documents">
        <p>
          Physical document metadata is canonical; related records surface it in
          their detail views.
        </p>
      </PageHeader>
      {result.loading ? (
        <LoadingState />
      ) : result.error ? (
        <ErrorState message={result.error} />
      ) : result.data?.items.length ? (
        <DocumentTable documents={result.data.items} />
      ) : (
        <EmptyState
          title="No documents in dummy data"
          detail="Documents will appear here when they are linked to official records."
        />
      )}
    </>
  );
}
function DocumentTable({ documents }: { documents: any[] }) {
  return (
    <DataTable headers={["File name", "Type", "Uploaded", "Status"]}>
      {documents.map((doc) => (
        <tr key={doc.id}>
          <td>{doc.originalFileName}</td>
          <td>{doc.documentType}</td>
          <td>{date(doc.uploadedAt?.slice(0, 10))}</td>
          <td>
            <StatusBadge>{doc.status}</StatusBadge>
          </td>
        </tr>
      ))}
    </DataTable>
  );
}

function SearchPage() {
  const [params] = useSearchParams();
  const initial = params.get("q") || "";
  const [query, setQuery] = useState(initial);
  const results = useApi<any[]>(
    query.trim().length >= 2 ? path("/search", { q: query.trim() }) : undefined,
  );
  return (
    <>
      <Breadcrumbs items={[{ label: "Search" }]} />
      <PageHeader eyebrow="Cross-record lookup" title="Search records">
        <p>
          Khasra results always include their village context because parcel
          numbers are not globally unique.
        </p>
      </PageHeader>
      <SearchInput
        value={query}
        onChange={setQuery}
        placeholder="Search village, khasra, or award"
      />
      {query.trim().length < 2 ? (
        <EmptyState
          title="Start a search"
          detail="Enter at least two characters."
        />
      ) : results.loading ? (
        <LoadingState label="Searching records…" />
      ) : results.error ? (
        <ErrorState message={results.error} />
      ) : !results.data?.length ? (
        <EmptyState
          title="No results"
          detail="No village, khasra, or award matched the search."
        />
      ) : (
        <section className="search-page-results">
          {results.data.map((result) => (
            <Link key={`${result.type}-${result.id}`} to={result.route}>
              <StatusBadge>{result.type}</StatusBadge>
              <span>
                <strong>{result.label}</strong>
                <small>{result.context || "No additional context"}</small>
              </span>
            </Link>
          ))}
        </section>
      )}
    </>
  );
}

function LrImport() {
  const [districtId, setDistrictId] = useState("");
  const [subdivisionId, setSubdivisionId] = useState("");
  const [villageId, setVillageId] = useState("");
  const [registerId, setRegisterId] = useState("");
  const [rawKhasra, setRawKhasra] = useState("");
  const [rawArea, setRawArea] = useState("");
  const [remarks, setRemarks] = useState("");
  const [awardQuery, setAwardQuery] = useState("");
  const [notificationQuery, setNotificationQuery] = useState("");
  const [selectedKhasra, setSelectedKhasra] = useState("");
  const [selectedAward, setSelectedAward] = useState("");
  const [section4, setSection4] = useState("");
  const [section6, setSection6] = useState("");
  const [message, setMessage] = useState("");
  const [saving, setSaving] = useState(false);
  const districts = useApi<any[]>("/districts");
  const district = useApi<any>(
    districtId ? `/districts/${districtId}` : undefined,
  );
  const subdivision = useApi<any>(
    subdivisionId
      ? path(`/subdivisions/${subdivisionId}`, { page: 0, pageSize: 100 })
      : undefined,
  );
  const registers = useApi<any[]>(
    villageId ? `/villages/${villageId}/lrs` : undefined,
  );
  const khasras = useApi<Page<any>>(
    villageId && rawKhasra
      ? path(`/villages/${villageId}/khasras`, {
          page: 0,
          pageSize: 10,
          q: rawKhasra,
        })
      : undefined,
  );
  const awards = useApi<Page<any>>(
    awardQuery
      ? path("/awards", { page: 0, pageSize: 10, q: awardQuery })
      : undefined,
  );
  const notifications = useApi<Page<any>>(
    notificationQuery
      ? path("/notifications", { page: 0, pageSize: 10, q: notificationQuery })
      : undefined,
  );
  const submit = async (event: FormEvent) => {
    event.preventDefault();
    if (!registerId) {
      setMessage("Select or create an LR register first.");
      return;
    }
    setSaving(true);
    setMessage("");
    try {
      await post(`/village-lrs/${registerId}/entries`, {
        rowNumber: null,
        rawKhasraText: rawKhasra,
        rawAreaText: rawArea || null,
        rawRemarks: remarks || null,
        khasraId: selectedKhasra || null,
        awardId: selectedAward || null,
        section4NotificationId: section4 || null,
        section6NotificationId: section6 || null,
      });
      setMessage("Draft LR row saved. Raw transcription was preserved.");
      setRawKhasra("");
      setRawArea("");
      setRemarks("");
      setSelectedKhasra("");
    } catch (error) {
      setMessage(
        error instanceof Error ? error.message : "Could not save the LR row.",
      );
    } finally {
      setSaving(false);
    }
  };
  const createRegister = async () => {
    if (!villageId) return;
    try {
      const created: any = await post("/village-lrs", {
        villageId,
        registerReference: null,
        remarks: null,
      });
      setRegisterId(created.id);
      setMessage("New LR register created for the selected village.");
    } catch (error) {
      setMessage(
        error instanceof Error
          ? error.message
          : "Could not create the LR register.",
      );
    }
  };
  return (
    <>
      <Breadcrumbs items={[{ label: "Import" }, { label: "LR entry" }]} />
      <PageHeader
        eyebrow="Historical land records"
        title="Village LR data entry"
      >
        <p>
          Keep handwritten text intact, link only records you can verify, and
          never silently merge uncertain values.
        </p>
      </PageHeader>
      <form className="lr-form" onSubmit={submit}>
        <fieldset>
          <legend>1. Select location</legend>
          <label>
            District
            <select
              value={districtId}
              onChange={(event) => {
                setDistrictId(event.target.value);
                setSubdivisionId("");
                setVillageId("");
                setRegisterId("");
              }}
            >
              <option value="">Select district</option>
              {districts.data?.map((d) => (
                <option key={d.id} value={d.id}>
                  {d.name}
                </option>
              ))}
            </select>
          </label>
          <label>
            Sub-division
            <select
              disabled={!districtId}
              value={subdivisionId}
              onChange={(event) => {
                setSubdivisionId(event.target.value);
                setVillageId("");
                setRegisterId("");
              }}
            >
              <option value="">Select sub-division</option>
              {district.data?.subDivisions.map((s: any) => (
                <option key={s.id} value={s.id}>
                  {s.name}
                </option>
              ))}
            </select>
          </label>
          <label>
            Village
            <select
              disabled={!subdivisionId}
              value={villageId}
              onChange={(event) => {
                setVillageId(event.target.value);
                setRegisterId("");
              }}
            >
              <option value="">Select village</option>
              {subdivision.data?.villages.items.map((v: any) => (
                <option key={v.id} value={v.id}>
                  {v.name}
                </option>
              ))}
            </select>
          </label>
        </fieldset>
        <fieldset>
          <legend>2. Select or create LR register</legend>
          <div className="inline-control">
            <select
              disabled={!villageId}
              value={registerId}
              onChange={(event) => setRegisterId(event.target.value)}
            >
              <option value="">Select existing register</option>
              {registers.data?.map((register) => (
                <option key={register.id} value={register.id}>
                  {register.registerReference || "Unreferenced register"} (
                  {register.entryCount} rows)
                </option>
              ))}
            </select>
            <button
              type="button"
              disabled={!villageId}
              onClick={createRegister}
            >
              Create register
            </button>
          </div>
        </fieldset>
        <fieldset disabled={!registerId}>
          <legend>3. Enter a source row</legend>
          <div className="field-grid">
            <label>
              Khasra transcription
              <input
                value={rawKhasra}
                onChange={(event) => {
                  setRawKhasra(event.target.value);
                  setSelectedKhasra("");
                }}
                placeholder="e.g. 22//2 min"
                required
              />
              {khasras.data?.items.length ? (
                <select
                  value={selectedKhasra}
                  onChange={(event) => setSelectedKhasra(event.target.value)}
                >
                  <option value="">Do not link until verified</option>
                  {khasras.data.items.map((k) => (
                    <option key={k.id} value={k.id}>
                      {k.displayNumber} — existing record
                    </option>
                  ))}
                </select>
              ) : rawKhasra ? (
                <small className="hint">
                  No existing khasra suggestion. It will remain unlinked; do not
                  merge uncertain records.
                </small>
              ) : null}
            </label>
            <label>
              Area transcription
              <input
                value={rawArea}
                onChange={(event) => setRawArea(event.target.value)}
                placeholder="e.g. 2 bigha"
              />
            </label>
            <label>
              Award reference
              <input
                value={awardQuery}
                onChange={(event) => {
                  setAwardQuery(event.target.value);
                  setSelectedAward("");
                }}
                placeholder="Search existing award"
              />
              {awards.data?.items.length ? (
                <select
                  value={selectedAward}
                  onChange={(event) => setSelectedAward(event.target.value)}
                >
                  <option value="">Do not link until verified</option>
                  {awards.data.items.map((a) => (
                    <option key={a.id} value={a.id}>
                      {a.awardNumber}
                    </option>
                  ))}
                </select>
              ) : null}
            </label>
            <label>
              Notification reference
              <input
                value={notificationQuery}
                onChange={(event) => {
                  setNotificationQuery(event.target.value);
                  setSection4("");
                  setSection6("");
                }}
                placeholder="Search existing notification"
              />
              {notifications.data?.items.length ? (
                <select
                  onChange={(event) =>
                    event.target.value &&
                    (event.target.selectedOptions[0].dataset.section === "4"
                      ? setSection4(event.target.value)
                      : setSection6(event.target.value))
                  }
                >
                  <option value="">Link by section after verification</option>
                  {notifications.data.items.map((n) => (
                    <option
                      key={n.id}
                      value={n.id}
                      data-section={n.sectionType}
                    >
                      {n.notificationNumber} — Section {n.sectionType}
                    </option>
                  ))}
                </select>
              ) : null}
            </label>
            <label className="span-two">
              Remarks
              <textarea
                value={remarks}
                onChange={(event) => setRemarks(event.target.value)}
                placeholder="Preserve any relevant source note."
              />
            </label>
          </div>
          <div className="form-footer">
            <span className="hint">
              Raw transcription is stored exactly as entered. Linked records are
              optional and reviewed separately.
            </span>
            <button type="submit" disabled={saving}>
              {saving ? "Saving…" : "Save draft row"}
            </button>
          </div>
        </fieldset>
        {message && (
          <p className="form-message" role="status">
            {message}
          </p>
        )}
      </form>
    </>
  );
}

function Khatauni() {
  const { id = "" } = useParams();
  const result = useApi<any>(`/khatauni/${id}`);
  if (result.loading) return <LoadingState />;
  if (result.error) return <ErrorState message={result.error} />;
  if (!result.data) return null;
  const r = result.data;
  return (
    <>
      <Breadcrumbs
        items={[
          { label: r.villageName, to: route.village(r.villageId) },
          { label: "Khatauni" },
        ]}
      />
      <PageHeader
        eyebrow="Revenue Record · not legal title"
        title={r.referenceNumber || "Khatauni record"}
      >
        <p>
          {r.recordYearText || "Unknown year"} · As of {date(r.asOfDate)} ·{" "}
          {r.verificationStatus}
        </p>
      </PageHeader>
      <div className="metric-grid">
        <Metric label="Khatas" value={r.totalKhatas} />
        <Metric label="Linked khasras" value={r.totalLinkedKhasras} />
        <Metric label="Recorded parties" value={r.totalRecordedParties} />
      </div>
      <section className="section">
        <h2>Khatas</h2>
        <DataTable
          headers={[
            "Khata no.",
            "Khasras",
            "Recorded owners",
            "Share validation",
            "Status",
          ]}
        >
          {r.khatas.map((k: any) => (
            <tr key={k.id}>
              <td>
                <EntityLink to={`/khatas/${k.id}`}>{k.khataNumber}</EntityLink>
              </td>
              <td>{k.khasraCount}</td>
              <td>{k.ownerCount}</td>
              <td>{k.shareValidation}</td>
              <td>
                <StatusBadge>
                  {k.isVerified ? "Verified" : "Needs review"}
                </StatusBadge>
              </td>
            </tr>
          ))}
        </DataTable>
      </section>
    </>
  );
}
function Khata() {
  const { id = "" } = useParams();
  const result = useApi<any>(`/khatas/${id}`);
  if (result.loading) return <LoadingState />;
  if (result.error) return <ErrorState message={result.error} />;
  if (!result.data) return null;
  const k = result.data;
  return (
    <>
      <Breadcrumbs
        items={[
          { label: k.villageName, to: route.village(k.villageId) },
          {
            label: k.khatauniReference || "Khatauni",
            to: `/khatauni/${k.khatauniRecordId}`,
          },
          { label: k.khataNumber },
        ]}
      />
      <PageHeader
        eyebrow="Recorded ownership · revenue record"
        title={`Khata ${k.khataNumber}`}
      />
      <section className="section">
        <h2>Khasras</h2>
        <DataTable headers={["Khasra no.", "Recorded area", "Raw area"]}>
          {k.khasras.map((item: any) => (
            <tr key={item.khasraId}>
              <td>
                <EntityLink to={route.khasra(item.khasraId)}>
                  {item.displayNumber}
                </EntityLink>
              </td>
              <td>{amount(item.recordedArea, item.areaUnit)}</td>
              <td>{item.rawAreaText || "—"}</td>
            </tr>
          ))}
        </DataTable>
      </section>
      <section className="section">
        <h2>Recorded owners</h2>
        <p>{k.shareValidation}</p>
        <DataTable
          headers={["Party", "Raw share", "Structured share", "Verification"]}
        >
          {k.owners.map((owner: any) => (
            <tr key={owner.id}>
              <td>
                <EntityLink to={`/parties/${owner.partyId}`}>
                  {owner.displayName}
                </EntityLink>
              </td>
              <td>{owner.rawShareText || "—"}</td>
              <td>
                {owner.shareNumerator == null
                  ? "—"
                  : `${owner.shareNumerator}/${owner.shareDenominator}`}
              </td>
              <td>
                <StatusBadge>{owner.verificationStatus}</StatusBadge>
              </td>
            </tr>
          ))}
        </DataTable>
      </section>
    </>
  );
}
function Party() {
  const { id = "" } = useParams();
  const result = useApi<any>(`/parties/${id}`);
  if (result.loading) return <LoadingState />;
  if (result.error) return <ErrorState message={result.error} />;
  if (!result.data) return null;
  const p = result.data;
  return (
    <>
      <Breadcrumbs items={[{ label: "Recorded party" }]} />
      <PageHeader eyebrow={p.partyType} title={p.displayName}>
        <p>{p.fatherOrSpouseName || "No father/spouse field recorded"}</p>
      </PageHeader>
      <InfoSection
        title="Revenue record party details"
        rows={[
          ["Address", p.addressText || "—"],
          ["Remarks", p.remarks || "—"],
        ]}
      />
      <section className="section">
        <h2>Recorded land holdings</h2>
        {p.holdings.length ? (
          <DataTable
            headers={["Village", "Khatauni", "Khata", "Khasras", "Share"]}
          >
            {p.holdings.map((h: any, i: number) => (
              <tr key={i}>
                <td>
                  <EntityLink to={route.village(h.villageId)}>
                    {h.villageName}
                  </EntityLink>
                </td>
                <td>
                  <EntityLink to={`/khatauni/${h.khatauniRecordId}`}>
                    {h.khatauniReference || "Revenue record"}
                  </EntityLink>
                </td>
                <td>
                  <EntityLink to={`/khatas/${h.khataId}`}>
                    {h.khataNumber}
                  </EntityLink>
                </td>
                <td>
                  {h.khasras.map((k: any) => (
                    <EntityLink key={k.id} to={route.khasra(k.id)}>
                      {k.displayNumber}
                    </EntityLink>
                  ))}
                </td>
                <td>
                  {h.rawShareText ||
                    (h.shareNumerator == null
                      ? "—"
                      : `${h.shareNumerator}/${h.shareDenominator}`)}
                </td>
              </tr>
            ))}
          </DataTable>
        ) : (
          <EmptyState
            title="No recorded holdings"
            detail="This party has not yet been linked to a Khata."
          />
        )}
      </section>
      <FutureSections
        names={["Awards", "Claims", "Compensation", "Litigation"]}
      />
    </>
  );
}
function App() {
  return (
    <BrowserRouter>
      <Shell>
        <Routes>
          <Route path="/" element={<Home />} />
          <Route path="/districts/:id" element={<District />} />
          <Route path="/subdivisions/:id" element={<Subdivision />} />
          <Route path="/villages" element={<Villages />} />
          <Route path="/villages/:id" element={<Village />} />
          <Route
            path="/villages/:villageId/lr/:lrId"
            element={<LrRegister />}
          />
          <Route path="/khasras/:id" element={<Khasra />} />
          <Route path="/khatauni/:id" element={<Khatauni />} />
          <Route path="/khatas/:id" element={<Khata />} />
          <Route path="/parties/:id" element={<Party />} />
          <Route path="/awards" element={<Awards />} />
          <Route path="/awards/import-pdf" element={<AwardPdfImportPanel />} />
          <Route path="/award-ingestion-sessions/:sessionId/review" element={<AwardIngestionReview />} />
          <Route path="/awards/:id/ingestion" element={<AwardIngestion />} />
          <Route path="/awards/:id/ingestion/:sessionId" element={<AwardIngestionReview />} />
          <Route path="/awards/:id" element={<Award />} />
          <Route path="/notifications" element={<Notifications />} />
          <Route path="/notifications/:id" element={<Notification />} />
          <Route path="/documents" element={<Documents />} />
          <Route path="/search" element={<SearchPage />} />
          <Route path="/imports/lr" element={<LrWorkspace />} />
          <Route path="/imports/lr/review" element={<LrReview />} />
          <Route path="*" element={<SearchPage />} />
        </Routes>
      </Shell>
    </BrowserRouter>
  );
}
void LrImport;
type LrDraft = {
  rowNumber: string;
  rawKhasraText: string;
  khasraId: string;
  rawAreaText: string;
  parsedArea: string;
  areaUnit: string;
  section4NotificationId: string;
  section6NotificationId: string;
  awardId: string;
  rawRemarks: string;
  verificationStatus: string;
};
const blankLrRow = (rowNumber = ""): LrDraft => ({
  rowNumber,
  rawKhasraText: "",
  khasraId: "",
  rawAreaText: "",
  parsedArea: "",
  areaUnit: "",
  section4NotificationId: "",
  section6NotificationId: "",
  awardId: "",
  rawRemarks: "",
  verificationStatus: "Draft",
});
function LrWorkspace() {
  const [params, setParams] = useSearchParams();
  const [districtId, setDistrictId] = useState("");
  const [subdivisionId, setSubdivisionId] = useState("");
  const [villageId, setVillageId] = useState("");
  const [registerId, setRegisterId] = useState(params.get("register") || "");
  const [rows, setRows] = useState<LrDraft[]>([
    blankLrRow("1"),
    blankLrRow("2"),
    blankLrRow("3"),
  ]);
  const [message, setMessage] = useState("");
  const [saving, setSaving] = useState(false);
  const districts = useApi<any[]>("/districts");
  const district = useApi<any>(
    districtId ? `/districts/${districtId}` : undefined,
  );
  const subdivision = useApi<any>(
    subdivisionId
      ? path(`/subdivisions/${subdivisionId}`, { page: 0, pageSize: 100 })
      : undefined,
  );
  const registers = useApi<any[]>(
    villageId ? `/villages/${villageId}/lrs` : undefined,
  );
  const khasras = useApi<Page<any>>(
    villageId
      ? path(`/villages/${villageId}/khasras`, { page: 0, pageSize: 100 })
      : undefined,
  );
  const awards = useApi<Page<any>>(path("/awards", { page: 0, pageSize: 100 }));
  const notifications = useApi<Page<any>>(
    path("/notifications", { page: 0, pageSize: 100 }),
  );
  const selected = useApi<any>(
    registerId ? `/village-lrs/${registerId}` : undefined,
  );
  useEffect(() => {
    if (selected.data && !villageId) setVillageId(selected.data.villageId);
  }, [selected.data, villageId]);
  useEffect(() => {
    if (villageId && registers.data?.length && !registerId)
      setRegisterId(registers.data[0].id);
  }, [villageId, registers.data, registerId]);
  const update = (index: number, key: keyof LrDraft, value: string) =>
    setRows((current) =>
      current.map((row, i) => (i === index ? { ...row, [key]: value } : row)),
    );
  const copyPrevious = (index: number) => {
    if (index) {
      const previous = rows[index - 1];
      setRows((current) =>
        current.map((row, i) =>
          i === index
            ? {
                ...row,
                awardId: previous.awardId,
                section4NotificationId: previous.section4NotificationId,
                section6NotificationId: previous.section6NotificationId,
                areaUnit: previous.areaUnit,
              }
            : row,
        ),
      );
    }
  };
  const addRows = (count: number) =>
    setRows((current) => [
      ...current,
      ...Array.from({ length: count }, (_, i) =>
        blankLrRow(String(current.length + i + 1)),
      ),
    ]);
  const createRegister = async () => {
    if (!villageId) return;
    try {
      const created: any = await post("/village-lrs", {
        villageId,
        registerReference: null,
        remarks: null,
      });
      setRegisterId(created.id);
      setParams({ register: created.id });
      setMessage("New LR register created; location context is now fixed.");
    } catch (error) {
      setMessage(
        error instanceof Error
          ? error.message
          : "Could not create LR register.",
      );
    }
  };
  const save = async (event: FormEvent) => {
    event.preventDefault();
    if (!registerId) {
      setMessage("Select or create an LR register first.");
      return;
    }
    const populated = rows.filter((row) => row.rawKhasraText.trim());
    if (!populated.length) {
      setMessage("Enter at least one raw Khasra transcription.");
      return;
    }
    setSaving(true);
    try {
      const saved: any[] = await post(
        `/village-lrs/${registerId}/entries/batch`,
        {
          rows: populated.map((row) => ({
            ...row,
            rowNumber: row.rowNumber ? Number(row.rowNumber) : null,
            parsedArea: row.parsedArea ? Number(row.parsedArea) : null,
            khasraId: row.khasraId || null,
            awardId: row.awardId || null,
            section4NotificationId: row.section4NotificationId || null,
            section6NotificationId: row.section6NotificationId || null,
          })),
        },
      );
      const warning = saved.find(
        (row) => row.possibleDuplicate,
      )?.duplicateWarning;
      setMessage(
        `${saved.length} draft row(s) saved.${warning ? ` ${warning}` : ""}`,
      );
      setRows([
        blankLrRow(String(populated.length + 1)),
        blankLrRow(String(populated.length + 2)),
        blankLrRow(String(populated.length + 3)),
      ]);
    } catch (error) {
      setMessage(
        error instanceof Error
          ? error.message
          : "Could not save. Typed rows remain on screen for retry.",
      );
    } finally {
      setSaving(false);
    }
  };
  return (
    <>
      <Breadcrumbs items={[{ label: "Import" }, { label: "LR entry" }]} />
      <PageHeader
        eyebrow="Historical source migration"
        title="Village LR data entry"
        actions={
          <Link className="text-action" to="/imports/lr/review">
            Verification queue
          </Link>
        }
      >
        <p>
          Raw source and structured interpretation stay separate. Only an
          explicit Commit updates canonical relationships.
        </p>
      </PageHeader>
      <form className="lr-form lr-grid-form" onSubmit={save}>
        <fieldset>
          <legend>1. Fixed register context</legend>
          <div className="field-grid">
            <label>
              District
              <select
                value={districtId}
                onChange={(e) => {
                  setDistrictId(e.target.value);
                  setSubdivisionId("");
                  setVillageId("");
                  setRegisterId("");
                }}
              >
                <option value="">Select district</option>
                {districts.data?.map((d) => (
                  <option key={d.id} value={d.id}>
                    {d.name}
                  </option>
                ))}
              </select>
            </label>
            <label>
              Sub-division
              <select
                disabled={!districtId}
                value={subdivisionId}
                onChange={(e) => {
                  setSubdivisionId(e.target.value);
                  setVillageId("");
                  setRegisterId("");
                }}
              >
                <option value="">Select sub-division</option>
                {district.data?.subDivisions.map((s: any) => (
                  <option key={s.id} value={s.id}>
                    {s.name}
                  </option>
                ))}
              </select>
            </label>
            <label>
              Village
              <select
                disabled={!subdivisionId && !villageId}
                value={villageId}
                onChange={(e) => {
                  setVillageId(e.target.value);
                  setRegisterId("");
                }}
              >
                <option value="">Select village</option>
                {subdivision.data?.villages.items.map((v: any) => (
                  <option key={v.id} value={v.id}>
                    {v.name}
                  </option>
                ))}
              </select>
            </label>
            <label>
              LR register
              <div className="inline-control">
                <select
                  disabled={!villageId}
                  value={registerId}
                  onChange={(e) => {
                    setRegisterId(e.target.value);
                    setParams({ register: e.target.value });
                  }}
                >
                  <option value="">Select register</option>
                  {registers.data?.map((row) => (
                    <option key={row.id} value={row.id}>
                      {row.registerReference || "Unreferenced register"} (
                      {row.entryCount} rows)
                    </option>
                  ))}
                </select>
                <button
                  type="button"
                  disabled={!villageId}
                  onClick={createRegister}
                >
                  Create
                </button>
              </div>
            </label>
          </div>
          {selected.data && (
            <p className="context-strip">
              Entering <strong>{selected.data.villageName}</strong> ·{" "}
              {selected.data.registerReference || "Unreferenced register"} ·{" "}
              {selected.data.totalRows} saved rows.{" "}
              <Link
                to={`/villages/${selected.data.villageId}/lr/${registerId}`}
              >
                Open register
              </Link>
            </p>
          )}
        </fieldset>
        <fieldset disabled={!registerId}>
          <legend>2. Multi-row draft entry</legend>
          <p className="hint">
            Copy previous reuses Award, Section 4, Section 6 and area unit.
            Parsed area remains source interpretation until explicitly mapped
            during Commit.
          </p>
          <div className="lr-grid-wrap">
            <table className="lr-grid">
              <thead>
                <tr>
                  <th>Row</th>
                  <th>Raw Khasra</th>
                  <th>Structured link</th>
                  <th>Raw area</th>
                  <th>Parsed / unit</th>
                  <th>Sec 4</th>
                  <th>Sec 6</th>
                  <th>Award</th>
                  <th>Status</th>
                  <th>Remarks</th>
                  <th />
                </tr>
              </thead>
              <tbody>
                {rows.map((row, index) => (
                  <tr key={index}>
                    <td>
                      <input
                        value={row.rowNumber}
                        onChange={(e) =>
                          update(index, "rowNumber", e.target.value)
                        }
                        inputMode="numeric"
                      />
                    </td>
                    <td>
                      <input
                        value={row.rawKhasraText}
                        onChange={(e) =>
                          update(index, "rawKhasraText", e.target.value)
                        }
                        placeholder="22//2 min"
                      />
                    </td>
                    <td>
                      <select
                        value={row.khasraId}
                        onChange={(e) =>
                          update(index, "khasraId", e.target.value)
                        }
                      >
                        <option value="">Unlinked—review</option>
                        {khasras.data?.items.map((k) => (
                          <option key={k.id} value={k.id}>
                            {k.displayNumber}
                          </option>
                        ))}
                      </select>
                    </td>
                    <td>
                      <input
                        value={row.rawAreaText}
                        onChange={(e) =>
                          update(index, "rawAreaText", e.target.value)
                        }
                        placeholder="2 bigha"
                      />
                    </td>
                    <td>
                      <div className="split-input">
                        <input
                          value={row.parsedArea}
                          onChange={(e) =>
                            update(index, "parsedArea", e.target.value)
                          }
                          inputMode="decimal"
                          placeholder="2"
                        />
                        <input
                          value={row.areaUnit}
                          onChange={(e) =>
                            update(index, "areaUnit", e.target.value)
                          }
                          placeholder="Bigha"
                        />
                      </div>
                    </td>
                    <td>
                      <select
                        value={row.section4NotificationId}
                        onChange={(e) =>
                          update(
                            index,
                            "section4NotificationId",
                            e.target.value,
                          )
                        }
                      >
                        <option value="">—</option>
                        {notifications.data?.items
                          .filter((n) => n.sectionType === "4")
                          .map((n) => (
                            <option key={n.id} value={n.id}>
                              {n.notificationNumber}
                            </option>
                          ))}
                      </select>
                    </td>
                    <td>
                      <select
                        value={row.section6NotificationId}
                        onChange={(e) =>
                          update(
                            index,
                            "section6NotificationId",
                            e.target.value,
                          )
                        }
                      >
                        <option value="">—</option>
                        {notifications.data?.items
                          .filter((n) => n.sectionType === "6")
                          .map((n) => (
                            <option key={n.id} value={n.id}>
                              {n.notificationNumber}
                            </option>
                          ))}
                      </select>
                    </td>
                    <td>
                      <select
                        value={row.awardId}
                        onChange={(e) =>
                          update(index, "awardId", e.target.value)
                        }
                      >
                        <option value="">—</option>
                        {awards.data?.items.map((a) => (
                          <option key={a.id} value={a.id}>
                            {a.awardNumber}
                          </option>
                        ))}
                      </select>
                    </td>
                    <td>
                      <select
                        value={row.verificationStatus}
                        onChange={(e) =>
                          update(index, "verificationStatus", e.target.value)
                        }
                      >
                        <option>Draft</option>
                        <option>NeedsReview</option>
                        <option>Verified</option>
                      </select>
                    </td>
                    <td>
                      <input
                        value={row.rawRemarks}
                        onChange={(e) =>
                          update(index, "rawRemarks", e.target.value)
                        }
                      />
                    </td>
                    <td>
                      <button
                        type="button"
                        className="copy-down"
                        disabled={!index}
                        onClick={() => copyPrevious(index)}
                      >
                        Copy previous
                      </button>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
          <div className="form-footer">
            <div>
              <button
                type="button"
                className="secondary-button"
                onClick={() => addRows(1)}
              >
                Add row
              </button>
              <button
                type="button"
                className="secondary-button"
                onClick={() => addRows(10)}
              >
                Add 10 rows
              </button>
            </div>
            <button type="submit" disabled={saving}>
              {saving ? "Saving…" : "Save draft rows"}
            </button>
          </div>
        </fieldset>
        {message && (
          <p className="form-message" role="status">
            {message}
          </p>
        )}
      </form>
    </>
  );
}
function LrRegister() {
  const { villageId = "", lrId = "" } = useParams();
  const [page, setPage] = useState(0);
  const [refresh, setRefresh] = useState(0);
  const [message, setMessage] = useState("");
  const detail = useApi<any>(`/village-lrs/${lrId}?r=${refresh}`);
  const entries = useApi<Page<any>>(
    path(`/village-lrs/${lrId}/entries`, { page, pageSize: 25, r: refresh }),
  );
  const setStatus = async (row: any) => {
    try {
      await put(`/lr-entries/${row.id}`, {
        expectedRevision: row.revision,
        row: {
          ...row,
          verificationStatus: row.khasraId ? "Verified" : "NeedsReview",
        },
      });
      setRefresh((x) => x + 1);
      setMessage("Row status saved.");
    } catch (error) {
      setMessage(
        error instanceof Error ? error.message : "Could not update row.",
      );
    }
  };
  const commit = async (row: any) => {
    try {
      await post(`/lr-entries/${row.id}/commit`, {
        expectedRevision: row.revision,
        applyParsedAreaToAcquisitionLinks: false,
      });
      setRefresh((x) => x + 1);
      setMessage(
        "Committed. LR parsed area was not applied to acquisition area.",
      );
    } catch (error) {
      setMessage(
        error instanceof Error ? error.message : "Could not commit row.",
      );
    }
  };
  if (detail.loading || entries.loading) return <LoadingState />;
  if (detail.error || entries.error)
    return <ErrorState message={detail.error || entries.error || ""} />;
  if (!detail.data || !entries.data) return null;
  const lr = detail.data;
  return (
    <>
      <Breadcrumbs
        items={[
          { label: "Village", to: route.village(villageId) },
          { label: "LR register" },
        ]}
      />
      <PageHeader
        eyebrow={lr.villageName}
        title={lr.registerReference || "Village LR register"}
        actions={
          <Link className="text-action" to={`/imports/lr?register=${lrId}`}>
            Enter rows
          </Link>
        }
      />
      <div className="metric-grid">
        <Metric label="Rows" value={lr.totalRows} />
        <Metric label="Draft" value={lr.draftCount} />
        <Metric label="Needs review" value={lr.needsReviewCount} />
        <Metric
          label="Verified / committed"
          value={`${lr.verifiedCount} / ${lr.committedCount}`}
        />
      </div>
      <section className="section">
        <DataTable
          headers={[
            "Row",
            "Raw Khasra",
            "Linked Khasra",
            "Area",
            "Award",
            "Sec 4 / 6",
            "Status",
            "Action",
          ]}
        >
          {entries.data.items.map((row) => (
            <tr key={row.id}>
              <td>{row.rowNumber || "—"}</td>
              <td>{row.rawKhasraText}</td>
              <td>
                {row.khasraId ? (
                  <EntityLink to={route.khasra(row.khasraId)}>
                    {row.khasraDisplayNumber}
                  </EntityLink>
                ) : (
                  "Unresolved"
                )}
              </td>
              <td>
                {row.rawAreaText || "—"}
                <small className="subtext">
                  {amount(row.parsedArea, row.areaUnit)}
                </small>
              </td>
              <td>
                {row.awardId ? (
                  <EntityLink to={route.award(row.awardId)}>
                    {row.awardNumber}
                  </EntityLink>
                ) : (
                  "—"
                )}
              </td>
              <td>
                {row.section4Number || "—"} / {row.section6Number || "—"}
              </td>
              <td>
                <StatusBadge
                  tone={
                    row.verificationStatus === "Committed"
                      ? "success"
                      : row.verificationStatus === "NeedsReview"
                        ? "warning"
                        : undefined
                  }
                >
                  {row.verificationStatus}
                </StatusBadge>
              </td>
              <td>
                <div className="row-actions">
                  {row.verificationStatus !== "Committed" && (
                    <button onClick={() => setStatus(row)}>
                      {row.khasraId ? "Verify" : "Needs review"}
                    </button>
                  )}
                  {row.verificationStatus === "Verified" && (
                    <button
                      className="commit-button"
                      onClick={() => commit(row)}
                    >
                      Commit
                    </button>
                  )}
                </div>
              </td>
            </tr>
          ))}
        </DataTable>
        <Pagination {...entries.data} onChange={setPage} />
      </section>
      {message && (
        <p className="form-message" role="status">
          {message}
        </p>
      )}
    </>
  );
}
function LrReview() {
  const [page, setPage] = useState(0);
  const [status, setStatus] = useState("NeedsReview");
  const result = useApi<Page<any>>(
    path("/lr-review", { page, pageSize: 25, status }),
  );
  return (
    <>
      <Breadcrumbs
        items={[
          { label: "Import", to: "/imports/lr" },
          { label: "Verification queue" },
        ]}
      />
      <PageHeader eyebrow="Human review" title="LR verification queue" />
      <section className="section">
        <div className="section-heading">
          <h2>Rows requiring attention</h2>
          <label className="inline-filter">
            Status
            <select
              value={status}
              onChange={(e) => {
                setStatus(e.target.value);
                setPage(0);
              }}
            >
              <option>NeedsReview</option>
              <option>Draft</option>
              <option>Verified</option>
            </select>
          </label>
        </div>
        {result.loading ? (
          <LoadingState />
        ) : result.error ? (
          <ErrorState message={result.error} />
        ) : !result.data?.items.length ? (
          <EmptyState
            title="Queue is clear"
            detail="No rows match this status."
          />
        ) : (
          <>
            <DataTable
              headers={[
                "Village / LR",
                "Row",
                "Raw Khasra",
                "Structured Khasra",
                "Award",
                "Status",
              ]}
            >
              {result.data.items.map((row) => (
                <tr key={row.id}>
                  <td>
                    {row.villageName}
                    <small className="subtext">
                      {row.registerReference || "Unreferenced register"}
                    </small>
                  </td>
                  <td>{row.rowNumber || "—"}</td>
                  <td>{row.rawKhasraText}</td>
                  <td>
                    {row.khasraId ? (
                      <EntityLink to={route.khasra(row.khasraId)}>
                        {row.khasraDisplayNumber}
                      </EntityLink>
                    ) : (
                      "Unresolved"
                    )}
                  </td>
                  <td>
                    {row.awardId ? (
                      <EntityLink to={route.award(row.awardId)}>
                        {row.awardNumber}
                      </EntityLink>
                    ) : (
                      "—"
                    )}
                  </td>
                  <td>
                    <Link
                      className="text-action"
                      to={`/villages/${row.villageId || ""}/lr/${row.villageLrId}`}
                    >
                      {row.verificationStatus}
                    </Link>
                  </td>
                </tr>
              ))}
            </DataTable>
            <Pagination {...result.data} onChange={setPage} />
          </>
        )}
      </section>
    </>
  );
}
// Kept as internal compatibility views while they are intentionally absent from the Village workspace.
void [
  VillageAwards,
  VillageNotifications,
  VillageKhatauni,
  VillageLrs,
  VillageDocuments,
];

export default App;
