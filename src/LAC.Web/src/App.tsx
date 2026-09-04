import { Fragment, useEffect, useState } from "react";
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
        if (error.name !== "AbortError" && cached === undefined)
          setState({ loading: false, error: error.message });
      });
    return () => controller.abort();
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
function EntityLink({ to, children }: { to: string; children: ReactNode }) {
  return (
    <Link className="entity-link" to={to}>
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
      <PageHeader eyebrow="Acquisition awards" title="Awards" />
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
  const [rectangle, setRectangle] = useState("");
  const overview = useApi<any>(`/awards/${id}/workspace?r=${refresh}`);
  const workspace = useApi<Page<any>>(
    path(`/awards/${id}/khasras`, { page, pageSize: 25, r: refresh }),
  );
  const notifications = useApi<any[]>(`/awards/${id}/notifications?r=${refresh}`);
  const possession = useApi<any[]>(`/awards/${id}/possession-events?r=${refresh}`);
  const courtCases = useApi<any[]>(`/awards/${id}/court-cases?r=${refresh}`);
  const claims = useApi<Page<any>>(path(`/awards/${id}/claims`, { page: 0, pageSize: 25, r: refresh }));
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
        actions={<div className="khasra-actions"><button onClick={() => setAdding(true)}>+ Add / Link Khasra</button>{a?.villages?.[0] && <a className="secondary-button" href={`/api/villages/${a.villages[0].id}/khasras/import-template`}>Download Template</a>}<button className="secondary-button" onClick={() => setRelated(true)}>+ Add Related Record</button><ExportMenu baseUrl={`/api/awards/${id}/export`} query="" /></div>}
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
      <section className="detail-grid">
        <InfoSection
          title="Award identity"
          rows={[
            [
              "Village(s)",
              a.villages.map((v: any) => (
                <EntityLink key={v.id} to={route.village(v.id)}>
                  {v.name}
                </EntityLink>
              )),
            ],
            [
              "Project / agency",
              a.project?.name || a.project?.requiringAgency || "—",
            ],
            ["Remarks", a.remarks || "—"],
          ]}
        />
        <InfoSection
          title="Record data"
          rows={[
            ["Core details", "Available"],
            ["Khasras", a.khasrasData],
            ["Notifications", a.notificationsData],
            ["Possession", a.possessionData],
            ["Litigation", a.litigationData],
            ["Claims", a.claimsData],
          ]}
        />
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
          <div className="inline-control"><select value={rectangle} onChange={e => setRectangle(e.target.value)}><option value="">All rectangles</option>{rectangles.map(value => <option key={value} value={value}>{value === "Other" ? "Other / Rectangle Not Identified" : `Rectangle ${value}`}</option>)}</select><span>{a.khasraCount} linked</span></div>
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
    </>
  );
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
  const [recorded, setRecorded] = useState("");
  const [awarded, setAwarded] = useState("");
  const [message, setMessage] = useState("");
  const [preview, setPreview] = useState<any>();
  const save = async () => {
    try {
      await post(`/awards/${award.id}/khasras`, {
        villageId,
        khasraNumber: number,
        qualifier: qualifier || null,
        recordedTotalAreaBigha: recorded === "" ? null : Number(recorded),
        recordedTotalAreaBiswa: null,
        recordedTotalAreaBiswansi: null,
        awardedAreaBigha: awarded === "" ? null : Number(awarded),
        awardedAreaBiswa: null,
        awardedAreaBiswansi: null,
        relationshipStatus: "Recorded",
        remarks: null,
      });
      onSaved();
    } catch (e) {
      setMessage(e instanceof Error ? e.message : "Could not link Khasra.");
    }
  };
  return (
    <section className="workspace-panel">
      <div className="panel-title">
        <h3>Add / Link Khasra</h3>
        <button onClick={onClose}>Close</button>
      </div>
      <p>
        Existing canonical Khasra is reused. A missing one is added to the
        selected Village master and marked for review.
      </p>
      <label className="secondary-button file-button">Preview Award Excel<input type="file" accept=".xlsx" onChange={async e => { const file = e.target.files?.[0]; if (!file) return; try { setPreview(await upload(`/awards/${award.id}/khasras/import-preview?villageId=${villageId}`, file)); } catch (error) { setMessage(error instanceof Error ? error.message : "Could not preview workbook."); } }} /></label>
      {preview && <div className="metric-row"><span>{preview.totalRows} source rows</span><span>{preview.validRows} ready</span><span>{preview.invalidRows} blocked</span><span>{preview.newKhasras} new khasras</span><span>{preview.existingKhasras} existing khasras</span></div>}
      {preview && <p className="hint">Preview only: no workbook row is imported from this panel until the Award import commit endpoint is explicitly approved and completed.</p>}
      <div className="field-grid">
        <label>
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
          <input value={number} onChange={(e) => setNumber(e.target.value)} />
        </label>
        <label>
          Qualifier
          <input
            value={qualifier}
            onChange={(e) => setQualifier(e.target.value)}
            placeholder="min"
          />
        </label>
        <label>
          Award recorded Bigha
          <input
            value={recorded}
            onChange={(e) => setRecorded(e.target.value)}
            inputMode="decimal"
          />
        </label>
        <label>
          Area awarded Bigha
          <input
            value={awarded}
            onChange={(e) => setAwarded(e.target.value)}
            inputMode="decimal"
          />
        </label>
      </div>
      <div className="form-footer">
        <button onClick={save} disabled={!villageId || !number.trim()}>
          Save link
        </button>
      </div>
      {message && <p className="form-message">{message}</p>}
    </section>
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
  return <section className="workspace-panel"><div className="panel-title"><h3>Add Related Record</h3><button onClick={onClose}>Close</button></div><p>These records are linked to this Award without inferring legal status. Khasra-dependent records may only use existing Award Khasras.</p><div className="field-grid"><label>Record type<select value={kind} onChange={e => { setKind(e.target.value); setReference(""); setDetail(""); setAmountValue(""); }}><option value="notification">Notification</option><option value="possession">Possession Event</option><option value="court">Court Case</option><option value="claim">Claim</option><option value="land-class">Land Classification</option><option value="valuation">Valuation Rule</option><option value="compensation">Compensation Rule</option><option value="area-issue">Area Issue</option><option value="supplementary">Supplementary Matter</option></select></label>{kind === "notification" ? <label>Canonical Notification<select value={reference} onChange={e => setReference(e.target.value)}><option value="">Select Notification</option>{notifications.data?.items.map(n => <option key={n.id} value={n.id}>{n.notificationNumber} · Section {n.sectionType}</option>)}</select></label> : <label>{kind === "court" ? "Case number" : kind === "land-class" ? "Classification code" : kind === "claim" ? "Claim reference" : "Type / reference"}<input value={reference} onChange={e => setReference(e.target.value)} /></label>}<label>{kind === "court" ? "Court name" : "Description / status"}<input value={detail} onChange={e => setDetail(e.target.value)} /></label>{["claim", "valuation", "compensation", "area-issue"].includes(kind) && <label>{kind === "compensation" ? "Rate percent" : "Amount / difference"}<input value={amountValue} onChange={e => setAmountValue(e.target.value)} inputMode="decimal" /></label>}</div>{needsKhasras && <fieldset className="award-khasra-picker"><legend>Affected Award Khasras</legend>{khasras.length ? khasras.map(k => <label key={k.khasraId}><input type="checkbox" checked={selectedKhasras.includes(k.khasraId)} onChange={() => toggle(k.khasraId)} /> {k.displayNumber} · {k.villageName}</label>) : <span className="hint">Link a Khasra before recording this type of related record.</span>}</fieldset>}<div className="form-footer"><span className="hint">Only populated record types appear in the Award overview.</span><button onClick={save} disabled={(needsKhasras && !selectedKhasras.length) || (kind === "court" && (!reference.trim() || !detail.trim()))}>Save related record</button></div>{message && <p className="form-message">{message}</p>}</section>;
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
            ["Project", n.project.name],
            ["Requiring agency", n.project.requiringAgency || "—"],
            ["Remarks", n.remarks || "—"],
          ]}
        />
      </section>
      <section className="section">
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
