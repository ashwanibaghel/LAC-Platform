import React, { useState, useEffect, ReactNode } from "react";
import { Link } from "react-router-dom";
import { ExportMenu } from "../components/ExportMenu";
import { IconPlus, IconSearch, IconChevronRight } from "../components/Icons";

const api = "/api";

type Page<T> = {
  items: T[];
  page: number;
  pageSize: number;
  totalCount: number;
};

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

function date(value?: string | null) {
  if (!value) return "—";
  const raw = String(value);
  const parsed = new Date(/^\d{4}-\d{2}-\d{2}$/.test(raw) ? raw + "T00:00:00" : raw);
  if (Number.isNaN(parsed.getTime())) return "—";
  return new Intl.DateTimeFormat("en-GB", { day: "2-digit", month: "short", year: "numeric" }).format(parsed);
}

export const VillageKhasrasTab: React.FC<{ villageId?: string; id?: string }> = ({ villageId, id }) => {
  const targetId = villageId || id || "";
  const [page, setPage] = useState(0);
  const [query, setQuery] = useState("");
  const [refresh, setRefresh] = useState(0);
  const [panel, setPanel] = useState(false);
  const [quickId, setQuickId] = useState("");
  const [importFile, setImportFile] = useState<File | null>(null);
  const [edit, setEdit] = useState<any>(null);

  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [data, setData] = useState<Page<any> | null>(null);

  useEffect(() => {
    if (!targetId) return;
    let active = true;
    setLoading(true);
    setError(null);

    const searchParams = new URLSearchParams({
      page: page.toString(),
      pageSize: "25",
      r: refresh.toString(),
    });
    if (query) searchParams.set("q", query);

    fetch(`${api}/villages/${targetId}/khasras?${searchParams.toString()}`, { credentials: "include" })
      .then(async (res) => {
        if (!res.ok) throw new Error("Could not load Khasras.");
        return res.json() as Promise<Page<any>>;
      })
      .then((resData) => {
        if (active) {
          setData(resData);
          setLoading(false);
        }
      })
      .catch((err) => {
        if (active) {
          setError(err?.message || "Failed to load Khasras.");
          setLoading(false);
        }
      });

    return () => {
      active = false;
    };
  }, [targetId, page, query, refresh]);

  const changed = () => {
    setRefresh((v) => v + 1);
    setPanel(false);
    setEdit(null);
    setImportFile(null);
  };

  const openAdd = () => {
    setEdit(null);
    setPanel(true);
  };

  const lastPage = data ? Math.max(0, Math.ceil(data.totalCount / data.pageSize) - 1) : 0;

  return (
    <section className="section khasra-workspace flex flex-col gap-4">
      <div className="section-heading khasra-heading">
        <div>
          <h2 className="text-base font-bold text-slate-900 m-0">Khasras</h2>
          <span className="text-xs text-slate-500">Canonical land parcel records for this village.</span>
        </div>
        <div className="khasra-actions">
          <button className="home-action-btn home-action-btn-primary" onClick={openAdd}>
            <IconPlus size={14} />
            <span>Add Khasra</span>
          </button>
          <label className="secondary-button file-button" style={{ cursor: "pointer" }}>
            Upload Excel
            <input
              type="file"
              accept=".xlsx"
              onChange={(e) => e.target.files?.[0] && setImportFile(e.target.files[0])}
            />
          </label>
          <a
            className="secondary-button"
            href={`${api}/villages/${targetId}/khasras/import-template`}
            download
          >
            Download Template
          </a>
          <ExportMenu baseUrl={`${api}/villages/${targetId}/khasras/export`} query={query} />
          <div className="lac-header-search-box" style={{ width: "220px" }}>
            <IconSearch size={14} className="lac-search-icon" />
            <input
              value={query}
              onChange={(e) => {
                setPage(0);
                setQuery(e.target.value);
              }}
              placeholder="Search Khasras…"
            />
          </div>
        </div>
      </div>

      {importFile && (
        <KhasraImportModal
          id={targetId}
          file={importFile}
          onClose={() => setImportFile(null)}
          onSaved={changed}
        />
      )}

      {panel && (
        <KhasraEntryPanelModal
          id={targetId}
          edit={edit}
          onClose={() => {
            setPanel(false);
            setEdit(null);
          }}
          onSaved={changed}
        />
      )}

      {loading ? (
        <div className="state loading">Loading khasra records…</div>
      ) : error ? (
        <div className="state error"><strong>Unable to load khasras.</strong><span>{error}</span></div>
      ) : !data?.items.length ? (
        <div className="state empty">
          <strong>{query ? "No khasras match" : "No khasras added yet."}</strong>
          <span>
            {query
              ? "Adjust the search query or add a new Khasra."
              : "Add a Khasra manually or upload the approved Excel template."}
          </span>
        </div>
      ) : (
        <>
          <RectangleKhasraGroups
            items={data.items}
            onQuickView={setQuickId}
            onEdit={(k) => {
              setEdit(k);
              setPanel(true);
            }}
          />

          {data.totalCount > data.pageSize && (
            <div className="pagination">
              <span>
                Showing {data.page * data.pageSize + 1}–
                {Math.min((data.page + 1) * data.pageSize, data.totalCount)} of {data.totalCount} khasras
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
        </>
      )}

      {quickId && <KhasraQuickViewDrawer id={quickId} onClose={() => setQuickId("")} />}
    </section>
  );
};

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
    !a ? 1 : !b ? -1 : Number(a) - Number(b) || a.localeCompare(b, undefined, { numeric: true })
  );
  const headers = [
    "Khasra No.",
    "Bigha",
    "Biswa",
    "Biswansi",
    "Recorded Owner Summary",
    "Acquisition Status",
    "Linked Award(s)",
    "Actions",
  ];

  return (
    <div className="rectangle-groups">
      {sorted.map(([rectangle, khasras]) => (
        <section className="rectangle-group" key={rectangle || "other"}>
          <h3>{rectangle ? `Rectangle ${rectangle}` : "Other / Rectangle Not Identified"}</h3>
          <div className="table-wrap">
            <table>
              <thead>
                <tr>
                  {headers.map((h) => (
                    <th key={h}>{h}</th>
                  ))}
                </tr>
              </thead>
              <tbody>
                {khasras
                  .sort((a, b) =>
                    a.displayNumber.localeCompare(b.displayNumber, undefined, { numeric: true })
                  )
                  .map((k) => (
                    <tr key={k.id}>
                      <td>
                        <button className="link-button font-bold" onClick={() => onQuickView(k.id)}>
                          {k.displayNumber}
                        </button>
                      </td>
                      <td>{k.areaBigha ?? "—"}</td>
                      <td>{k.areaBiswa ?? "—"}</td>
                      <td>{k.areaBiswansi ?? "—"}</td>
                      <td>{k.ownerSummary || "—"}</td>
                      <td>
                        <span className={`status ${k.acquisitionStatus !== "Not recorded" ? "success" : ""}`}>
                          {k.acquisitionStatus}
                        </span>
                      </td>
                      <td>
                        {k.awards?.length
                          ? k.awards.map((a: any) => (
                              <Link key={a.id} to={`/awards/${a.id}`} className="entity-link mr-2">
                                {a.awardNumber}
                              </Link>
                            ))
                          : "—"}
                      </td>
                      <td>
                        <button className="icon-action text-xs font-semibold" onClick={() => onEdit(k)}>
                          Edit
                        </button>
                      </td>
                    </tr>
                  ))}
              </tbody>
            </table>
          </div>
        </section>
      ))}
    </div>
  );
}

function KhasraEntryPanelModal({
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
      : [blankKhasra()]
  );
  const [message, setMessage] = useState("");
  const [busy, setBusy] = useState(false);

  const update = (i: number, key: keyof KhasraRow, value: string) =>
    setRows((all) => all.map((r, index) => (index === i ? { ...r, [key]: value } : r)));

  const save = async () => {
    try {
      const nonEmpty = rows.filter((r) => r.khasraNumber.trim());
      if (!nonEmpty.length) {
        setMessage("Enter at least one Khasra Number.");
        return;
      }
      setBusy(true);
      if (edit) {
        const res = await fetch(`${api}/khasras/${edit.id}`, {
          method: "PUT",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify(toKhasraPayload(nonEmpty[0])),
          credentials: "include",
        });
        if (!res.ok) throw new Error("Could not update Khasra.");
      } else {
        const res = await fetch(`${api}/villages/${id}/khasras/batch`, {
          method: "POST",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify({ rows: nonEmpty.map(toKhasraPayload) }),
          credentials: "include",
        });
        if (!res.ok) throw new Error("Could not batch save Khasras.");
      }
      onSaved();
    } catch (e) {
      setMessage(e instanceof Error ? e.message : "Could not save khasras.");
    } finally {
      setBusy(false);
    }
  };

  return (
    <div className="lac-modal-backdrop" onClick={onClose}>
      <div className="lac-modal-card" style={{ width: "min(720px, 95vw)" }} onClick={(e) => e.stopPropagation()}>
        <div className="lac-modal-header">
          <h3>{edit ? "Edit Khasra" : "Add Khasras"}</h3>
          <button className="lac-modal-close" onClick={onClose}>&times;</button>
        </div>

        <div className="khasra-grid-wrap">
          <table className="khasra-grid">
            <thead>
              <tr>
                <th>Khasra Number</th>
                <th>Bigha</th>
                <th>Biswa</th>
                <th>Biswansi</th>
                <th>Award No.</th>
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
                      placeholder="e.g. 12/4"
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
                      placeholder="Reuse Award No."
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
                      <button className="quiet-button text-red-600 text-xs" onClick={() => setRows((all) => all.filter((_, index) => index !== i))}>
                        Remove
                      </button>
                    )}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>

        {message && <div className="form-message">{message}</div>}

        <div className="lac-modal-footer">
          {!edit && (
            <button className="secondary-button" onClick={() => setRows((all) => [...all, blankKhasra()])}>
              + Add Row
            </button>
          )}
          <button className="secondary-button" onClick={onClose}>Cancel</button>
          <button disabled={busy} onClick={save}>
            {busy ? "Saving…" : edit ? "Save changes" : "Save All Rows"}
          </button>
        </div>
      </div>
    </div>
  );
}

function KhasraImportModal({
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
  const [busy, setBusy] = useState(false);

  useEffect(() => {
    const form = new FormData();
    form.append("file", file);
    fetch(`${api}/villages/${id}/khasras/import-preview`, { method: "POST", body: form, credentials: "include" })
      .then(async (r) => {
        if (!r.ok) throw new Error("Could not preview workbook.");
        return r.json();
      })
      .then(setPreview)
      .catch((e) => setMessage(e instanceof Error ? e.message : "Could not preview workbook."));
  }, [file, id]);

  const save = async () => {
    try {
      setBusy(true);
      const res = await fetch(`${api}/villages/${id}/khasras/import`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ rows: preview.importableRows }),
        credentials: "include",
      });
      if (!res.ok) throw new Error("Could not import rows.");
      const saved = await res.json();
      setMessage(
        `${saved.createdKhasras} created, ${saved.reusedKhasras} reused, ${saved.createdAwards} award(s) created, ${saved.createdAwardLinks} new link(s), ${saved.failedRows} skipped.`
      );
      onSaved();
    } catch (e) {
      setMessage(e instanceof Error ? e.message : "Could not import rows.");
    } finally {
      setBusy(false);
    }
  };

  return (
    <div className="lac-modal-backdrop" onClick={onClose}>
      <div className="lac-modal-card" style={{ width: "min(840px, 95vw)" }} onClick={(e) => e.stopPropagation()}>
        <div className="lac-modal-header">
          <h3>Excel Import Preview</h3>
          <button className="lac-modal-close" onClick={onClose}>&times;</button>
        </div>

        {!preview && !message && <div className="state loading">Parsing Excel workbook…</div>}

        {preview && (
          <>
            <div className="metric-row">
              <span>{preview.totalRows} total</span>
              <span>{preview.validRows} ready</span>
              <span>{preview.invalidRows} blocked</span>
              <span>{preview.newKhasras} new khasras</span>
              <span>{preview.existingKhasras} existing</span>
              <span>{preview.newAwards} new awards</span>
            </div>

            <div className="table-wrap" style={{ maxHeight: "300px", overflowY: "auto" }}>
              <table>
                <thead>
                  <tr>
                    <th>Row</th>
                    <th>Khasra No.</th>
                    <th>Bigha</th>
                    <th>Biswa</th>
                    <th>Biswansi</th>
                    <th>Award No.</th>
                    <th>Result</th>
                  </tr>
                </thead>
                <tbody>
                  {preview.rows?.map((row: any) => (
                    <tr key={row.rowNumber}>
                      <td>{row.rowNumber}</td>
                      <td>{row.row?.khasraNumber || row.khasraNumber || "—"}</td>
                      <td>{row.row?.bigha ?? "—"}</td>
                      <td>{row.row?.biswa ?? "—"}</td>
                      <td>{row.row?.biswansi ?? "—"}</td>
                      <td>{row.row?.awardNumber || "—"}</td>
                      <td>
                        <span className={`status ${row.result === "READY" ? "success" : "warning"}`}>
                          {row.result}
                        </span>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </>
        )}

        {message && <div className="form-message">{message}</div>}

        <div className="lac-modal-footer">
          <button className="secondary-button" onClick={onClose}>Cancel</button>
          {preview?.importableRows?.length > 0 && (
            <button disabled={busy} onClick={save}>
              {busy ? "Importing…" : "Import All Valid Rows"}
            </button>
          )}
        </div>
      </div>
    </div>
  );
}

function KhasraQuickViewDrawer({ id, onClose }: { id: string; onClose: () => void }) {
  const [k, setK] = useState<any>(null);
  const [ownership, setOwnership] = useState<any>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let active = true;
    setLoading(true);

    Promise.all([
      fetch(`${api}/khasras/${id}`, { credentials: "include" }).then((r) => r.ok ? r.json() : null),
      fetch(`${api}/khasras/${id}/ownership`, { credentials: "include" }).then((r) => r.ok ? r.json() : null)
    ])
      .then(([kData, oData]) => {
        if (active) {
          setK(kData);
          setOwnership(oData);
          setLoading(false);
        }
      })
      .catch((e) => {
        if (active) {
          setError(e?.message || "Could not load Khasra.");
          setLoading(false);
        }
      });

    return () => {
      active = false;
    };
  }, [id]);

  return (
    <aside className="khasra-drawer">
      <div className="khasra-drawer-header">
        <h3>Khasra Record Drawer</h3>
        <button className="lac-modal-close" onClick={onClose}>&times;</button>
      </div>

      {loading && <div className="state loading">Loading parcel details…</div>}
      {error && <div className="state error"><strong>Error</strong><span>{error}</span></div>}

      {!loading && k && (
        <>
          <div>
            <h2 className="text-xl font-extrabold text-slate-900 m-0">{k.displayNumber}</h2>
            <span className="text-xs text-slate-500">
              {k.village?.name} &middot; {k.village?.subDivision?.name}
            </span>
          </div>

          <div className="info-section">
            <h4 className="text-xs font-bold uppercase text-slate-500 mb-2">Canonical Area</h4>
            <dl>
              <div><dt>Bigha</dt><dd>{k.areaBigha ?? "—"}</dd></div>
              <div><dt>Biswa</dt><dd>{k.areaBiswa ?? "—"}</dd></div>
              <div><dt>Biswansi</dt><dd>{k.areaBiswansi ?? "—"}</dd></div>
            </dl>
          </div>

          <section>
            <h4 className="text-xs font-bold uppercase text-slate-500 mb-2">Recorded Owners</h4>
            {ownership?.isAmbiguous ? (
              <p className="text-xs text-slate-500">Ambiguous historical record — not guessed.</p>
            ) : ownership?.owners?.length ? (
              ownership.owners.map((o: any) => (
                <p key={o.partyId} className="text-xs m-0 py-1">
                  <Link to={`/parties/${o.partyId}`} className="entity-link">
                    {o.displayName}
                  </Link>
                </p>
              ))
            ) : (
              <p className="text-xs text-slate-500">Not recorded.</p>
            )}
          </section>

          <section>
            <h4 className="text-xs font-bold uppercase text-slate-500 mb-2">Linked Awards</h4>
            {k.awards?.length ? (
              k.awards.map((a: any) => (
                <p key={a.id} className="text-xs m-0 py-1">
                  <Link to={`/awards/${a.id}`} className="entity-link">{a.awardNumber}</Link> &middot;{" "}
                  <span>{a.acquisitionStatus || "Status not recorded"}</span>
                </p>
              ))
            ) : (
              <p className="text-xs text-slate-500">Not linked to any Award.</p>
            )}
          </section>

          <div className="mt-auto pt-4 border-t border-slate-200">
            <Link to={`/khasras/${id}`} className="text-action text-sm font-bold flex items-center gap-1">
              <span>Open Full Khasra Record</span>
              <IconChevronRight size={14} />
            </Link>
          </div>
        </>
      )}
    </aside>
  );
}
