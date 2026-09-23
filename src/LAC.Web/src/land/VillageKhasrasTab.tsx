import React, { useState, useEffect, useRef } from "react";
import { Link } from "react-router-dom";
import { useAuth } from "../auth/AuthProvider";
import { ExportMenu } from "../components/ExportMenu";
import { IconPlus, IconSearch, IconClose } from "../components/Icons";
import "./land.css";

const api = "/api";

type Page<T> = {
  items: T[];
  page: number;
  pageSize: number;
  totalCount: number;
};

export interface KhasraWorkspaceRow {
  khasraNumber: string;
  bigha?: number | null;
  biswa?: number | null;
  biswansi?: number | null;
  awardNumber?: string | null;
  awardDate?: string | null;
  rectangleNumber?: string | null;
  qualifier?: string | null;
}

export interface KhasraImportProblem {
  rowNumber: number;
  khasraNumber?: string | null;
  message: string;
}

export interface KhasraImportRowPreview {
  rowNumber: number;
  khasraNumber?: string | null;
  khasraStatus: string;
  areaStatus: string;
  awardStatus: string;
  awardLinkStatus: string;
  result: string;
  message?: string | null;
  canImport: boolean;
  row?: KhasraWorkspaceRow | null;
}

export interface KhasraImportPreview {
  totalRows: number;
  validRows: number;
  invalidRows: number;
  newKhasras: number;
  existingKhasras: number;
  newAwards: number;
  existingAwards: number;
  ambiguousAwards: number;
  newAwardLinks: number;
  existingAwardLinks: number;
  skippedRows: number;
  problems: KhasraImportProblem[];
  importableRows: KhasraWorkspaceRow[];
  rows: KhasraImportRowPreview[];
}

export interface NotificationLinkItem {
  id: string;
  notificationNumber?: string | null;
  sectionType?: string | null;
  notificationDate?: string | null;
  area?: number | null;
  areaUnit?: string | null;
}

export interface AwardLinkItem {
  id: string;
  awardNumber: string;
  acquisitionStatus?: string | null;
  acquiredArea?: number | null;
  areaUnit?: string | null;
}

export interface LrEntryItem {
  id: string;
  villageLrId: string;
  rawKhasraText?: string | null;
  rawAreaText?: string | null;
  rawRemarks?: string | null;
  verificationStatus: string;
}

export interface RecordedOwner {
  partyId: string;
  displayName: string;
  rawShareText?: string | null;
  numerator?: number | null;
  denominator?: number | null;
}

export interface RecordedOwnershipResult {
  found: boolean;
  isAmbiguous: boolean;
  message?: string | null;
  khatauniRecordId?: string | null;
  khataId?: string | null;
  owners: RecordedOwner[];
}

export interface KhasraDetail {
  id: string;
  displayNumber: string;
  normalizedNumber: string;
  rectangleNumber?: string | null;
  killaNumber?: string | null;
  subdivisionNumber?: string | null;
  totalArea?: number | null;
  areaUnit?: string | null;
  areaBigha?: number | null;
  areaBiswa?: number | null;
  areaBiswansi?: number | null;
  remarks?: string | null;
  village?: {
    id: string;
    name: string;
    subDivision?: {
      id: string;
      name: string;
    };
  };
  notifications?: NotificationLinkItem[];
  awards?: AwardLinkItem[];
  lrEntries?: LrEntryItem[];
}

export interface VillageKhasrasTabProps {
  villageId: string;
}

export const VillageKhasrasTab: React.FC<VillageKhasrasTabProps> = ({ villageId }) => {
  const { hasPermission } = useAuth();
  const canEditKhasra = hasPermission("Khasra.Edit");

  const [page, setPage] = useState(0);
  const [query, setQuery] = useState("");
  const [refresh, setRefresh] = useState(0);
  const [panel, setPanel] = useState(false);
  const [quickId, setQuickId] = useState("");
  const [edit, setEdit] = useState<any>(null);

  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [data, setData] = useState<Page<any> | null>(null);

  useEffect(() => {
    if (!villageId) return;
    let active = true;
    setLoading(true);
    setError(null);

    const searchParams = new URLSearchParams({
      page: page.toString(),
      pageSize: "25",
      r: refresh.toString(),
    });
    if (query) searchParams.set("q", query);

    fetch(`${api}/villages/${villageId}/khasras?${searchParams.toString()}`, { credentials: "include" })
      .then(async (res) => {
        if (!res.ok) {
          if (res.status === 403) throw new Error("Access denied: You do not have permission to view Khasras.");
          throw new Error("Could not load Khasras.");
        }
        return res.json() as Promise<Page<any>>;
      })
      .then((resData) => {
        if (active) {
          setData(resData);
          setLoading(false);
        }
      })
      .catch((err: any) => {
        if (active) {
          setError(err?.message || "Failed to load Khasras.");
          setLoading(false);
        }
      });

    return () => {
      active = false;
    };
  }, [villageId, page, query, refresh]);

  const changed = () => {
    setRefresh((v) => v + 1);
  };

  const openAddModal = () => {
    setEdit(null);
    setPanel(true);
  };

  const openEditModal = (khasra: any, e: React.MouseEvent) => {
    e.stopPropagation();
    setEdit(khasra);
    setPanel(true);
  };

  const handleRowClick = (khasraId: string) => {
    setQuickId(khasraId);
  };

  // Group items by rectangle for clean visual grouping
  const items = data?.items || [];
  const grouped = items.reduce<Record<string, any[]>>((acc, item) => {
    const rect = item.rectangleNumber || "Unassigned";
    if (!acc[rect]) acc[rect] = [];
    acc[rect].push(item);
    return acc;
  }, {});

  return (
    <div className="village-khasras-tab">
      <div className="section-heading" style={{ marginBottom: "16px" }}>
        <div>
          <h2>Canonical Khasras Directory</h2>
          <span>Master land parcel directory for this village, organized by rectangle records.</span>
        </div>
        <div style={{ display: "flex", gap: "10px", alignItems: "center" }}>
          <ExportMenu villageId={villageId} />
          {canEditKhasra && (
            <button className="primary-button" onClick={openAddModal}>
              <IconPlus size={15} /> Add Khasra
            </button>
          )}
        </div>
      </div>

      {/* Filter / Search Bar */}
      <div className="khasras-filter-bar">
        <div className="search-input-wrap">
          <IconSearch size={15} className="search-icon" />
          <input
            type="text"
            className="form-input search-input"
            placeholder="Search Khasra number…"
            value={query}
            onChange={(e) => {
              setQuery(e.target.value);
              setPage(0);
            }}
          />
        </div>
        <span className="v-count-badge">{data?.totalCount ?? 0} Total Khasras</span>
      </div>

      {loading ? (
        <div className="state loading">Loading khasras directory…</div>
      ) : error ? (
        <div className="state error">{error}</div>
      ) : items.length === 0 ? (
        <div className="state empty" style={{ padding: "24px", textAlign: "center" }}>
          <strong>No khasra records found</strong>
          <p style={{ margin: "4px 0 0", color: "#64748b", fontSize: "13px" }}>
            {query ? `No results matching "${query}".` : "No khasra parcels registered for this village yet."}
          </p>
        </div>
      ) : (
        <div className="khasra-grouped-container">
          {Object.entries(grouped).map(([rectName, rectItems]) => (
            <div key={rectName} className="rect-group-card">
              <div className="rect-group-header">
                <span className="rect-title">Rectangle {rectName}</span>
                <span className="rect-count">{rectItems.length} parcel(s)</span>
              </div>
              <div className="table-wrap">
                <table>
                  <thead>
                    <tr>
                      <th scope="col" style={{ width: "25%" }}>Khasra Number</th>
                      <th scope="col" style={{ width: "40%" }}>Canonical Area</th>
                      <th scope="col" style={{ width: "25%" }}>Linked Award</th>
                      <th scope="col" style={{ width: "10%", textAlign: "right" }}></th>
                    </tr>
                  </thead>
                  <tbody>
                    {rectItems.map((k) => (
                      <tr
                        key={k.id}
                        tabIndex={0}
                        role="button"
                        aria-label={`Inspect Khasra ${k.displayNumber || k.normalizedNumber || k.id}`}
                        className="khasra-row-interactive"
                        onClick={() => handleRowClick(k.id)}
                        onKeyDown={(e) => {
                          if (e.key === "Enter" || e.key === " ") {
                            e.preventDefault();
                            handleRowClick(k.id);
                          }
                        }}
                        title="Click or press Enter/Space to inspect Khasra details"
                      >
                        <td>
                          <span className="khasra-num-highlight">
                            {k.displayNumber || k.normalizedNumber || k.id}
                          </span>
                        </td>
                        <td>
                          <span style={{ fontSize: "13px", color: "#1e293b", fontWeight: 600 }}>
                            {k.areaBigha != null
                              ? `${k.areaBigha} Bigha ${k.areaBiswa ?? 0} Biswa ${k.areaBiswansi ?? 0} Biswansi`
                              : k.totalArea != null
                              ? `${k.totalArea} ${k.areaUnit || ""}`
                              : "—"}
                          </span>
                        </td>
                        <td>
                          {k.awards?.length > 0 ? (
                            <span className="khasra-award-pill">
                              Award #{k.awards[0].awardNumber}
                            </span>
                          ) : (
                            <span style={{ fontSize: "12px", color: "#94a3b8" }}>Unlinked</span>
                          )}
                        </td>
                        <td style={{ textAlign: "right" }} onClick={(e) => e.stopPropagation()}>
                          {canEditKhasra && (
                            <button
                              type="button"
                              className="khasra-edit-btn"
                              onClick={(e) => openEditModal(k, e)}
                              title="Edit Khasra"
                            >
                              Edit
                            </button>
                          )}
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            </div>
          ))}

          {/* Pagination Controls */}
          {data && data.totalCount > 25 && (
            <div className="pagination-bar" style={{ display: "flex", justifyContent: "space-between", alignItems: "center", marginTop: "16px" }}>
              <button
                type="button"
                className="secondary-button"
                disabled={page === 0}
                onClick={() => setPage((p) => p - 1)}
              >
                Previous Page
              </button>
              <span style={{ fontSize: "13px", color: "#64748b" }}>
                Page {page + 1} of {Math.ceil(data.totalCount / 25)}
              </span>
              <button
                type="button"
                className="secondary-button"
                disabled={(page + 1) * 25 >= data.totalCount}
                onClick={() => setPage((p) => p + 1)}
              >
                Next Page
              </button>
            </div>
          )}
        </div>
      )}

      {/* Add / Edit Modal */}
      {panel && (
        <KhasraEditModal
          villageId={villageId}
          khasra={edit}
          onClose={() => setPanel(false)}
          onSuccess={() => {
            setPanel(false);
            changed();
          }}
        />
      )}

      {/* Right-Side Inspector Drawer */}
      {quickId && (
        <KhasraRightInspectorDrawer
          id={quickId}
          villageId={villageId}
          onClose={() => setQuickId("")}
        />
      )}
    </div>
  );
};

/* =========================================================
   Add / Edit Modal
   ========================================================= */
interface KhasraEditModalProps {
  villageId: string;
  khasra?: any;
  onClose: () => void;
  onSuccess: () => void;
}

const KhasraEditModal: React.FC<KhasraEditModalProps> = ({ villageId, khasra, onClose, onSuccess }) => {
  const isEditing = Boolean(khasra);
  const [displayNumber, setDisplayNumber] = useState(khasra?.displayNumber || khasra?.normalizedNumber || "");
  const [rectangleNumber, setRectangleNumber] = useState(khasra?.rectangleNumber || "");
  const [killaNumber, setKillaNumber] = useState(khasra?.killaNumber || "");
  const [subdivisionNumber, setSubdivisionNumber] = useState(khasra?.subdivisionNumber || "");
  const [bigha, setBigha] = useState(khasra?.areaBigha?.toString() || "");
  const [biswa, setBiswa] = useState(khasra?.areaBiswa?.toString() || "");
  const [biswansi, setBiswansi] = useState(khasra?.areaBiswansi?.toString() || "");
  const [remarks, setRemarks] = useState(khasra?.remarks || "");

  const [busy, setBusy] = useState(false);
  const [err, setErr] = useState<string | null>(null);

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!displayNumber.trim()) {
      setErr("Khasra number is required.");
      return;
    }

    setBusy(true);
    setErr(null);

    try {
      const payload = {
        displayNumber: displayNumber.trim(),
        rectangleNumber: rectangleNumber.trim() || null,
        killaNumber: killaNumber.trim() || null,
        subdivisionNumber: subdivisionNumber.trim() || null,
        areaBigha: bigha ? parseInt(bigha, 10) : null,
        areaBiswa: biswa ? parseInt(biswa, 10) : null,
        areaBiswansi: biswansi ? parseInt(biswansi, 10) : null,
        remarks: remarks.trim() || null,
      };

      const url = isEditing ? `${api}/khasras/${khasra.id}` : `${api}/villages/${villageId}/khasras`;
      const method = isEditing ? "PUT" : "POST";

      const res = await fetch(url, {
        method,
        headers: { "Content-Type": "application/json" },
        credentials: "include",
        body: JSON.stringify(payload),
      });

      if (!res.ok) {
        const errJson = await res.json().catch(() => ({}));
        throw new Error(errJson.detail || errJson.message || "Failed to save Khasra.");
      }

      onSuccess();
    } catch (e: any) {
      setErr(e?.message || "Failed to save Khasra.");
    } finally {
      setBusy(false);
    }
  };

  return (
    <div className="modal-overlay" onClick={onClose}>
      <div className="modal-container" onClick={(e) => e.stopPropagation()} style={{ maxWidth: "520px" }}>
        <div className="modal-header">
          <h3>{isEditing ? `Edit Khasra #${displayNumber}` : "Add Khasra Parcel"}</h3>
          <button className="icon-button" onClick={onClose}>
            <IconClose size={18} />
          </button>
        </div>

        <form onSubmit={handleSubmit}>
          <div className="modal-body" style={{ display: "flex", flexDirection: "column", gap: "14px" }}>
            {err && <div className="state error">{err}</div>}

            <div className="form-group">
              <label className="form-label required">Khasra Number</label>
              <input
                type="text"
                className="form-input"
                placeholder="e.g. 12//14"
                value={displayNumber}
                onChange={(e) => setDisplayNumber(e.target.value)}
                required
              />
            </div>

            <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr 1fr", gap: "12px" }}>
              <div className="form-group">
                <label className="form-label">Rectangle</label>
                <input
                  type="text"
                  className="form-input"
                  placeholder="e.g. 12"
                  value={rectangleNumber}
                  onChange={(e) => setRectangleNumber(e.target.value)}
                />
              </div>
              <div className="form-group">
                <label className="form-label">Killa</label>
                <input
                  type="text"
                  className="form-input"
                  placeholder="e.g. 14"
                  value={killaNumber}
                  onChange={(e) => setKillaNumber(e.target.value)}
                />
              </div>
              <div className="form-group">
                <label className="form-label">Sub-division</label>
                <input
                  type="text"
                  className="form-input"
                  placeholder="e.g. 1"
                  value={subdivisionNumber}
                  onChange={(e) => setSubdivisionNumber(e.target.value)}
                />
              </div>
            </div>

            <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr 1fr", gap: "12px" }}>
              <div className="form-group">
                <label className="form-label">Bigha</label>
                <input
                  type="number"
                  min="0"
                  className="form-input"
                  value={bigha}
                  onChange={(e) => setBigha(e.target.value)}
                />
              </div>
              <div className="form-group">
                <label className="form-label">Biswa</label>
                <input
                  type="number"
                  min="0"
                  className="form-input"
                  value={biswa}
                  onChange={(e) => setBiswa(e.target.value)}
                />
              </div>
              <div className="form-group">
                <label className="form-label">Biswansi</label>
                <input
                  type="number"
                  min="0"
                  className="form-input"
                  value={biswansi}
                  onChange={(e) => setBiswansi(e.target.value)}
                />
              </div>
            </div>

            <div className="form-group">
              <label className="form-label">Remarks</label>
              <textarea
                className="form-input"
                rows={2}
                placeholder="Optional parcel remarks…"
                value={remarks}
                onChange={(e) => setRemarks(e.target.value)}
              />
            </div>
          </div>

          <div className="modal-footer">
            <button type="button" className="secondary-button" onClick={onClose}>
              Cancel
            </button>
            <button type="submit" className="primary-button" disabled={busy || !displayNumber.trim()}>
              {busy ? "Saving…" : isEditing ? "Update Khasra" : "Create Khasra"}
            </button>
          </div>
        </form>
      </div>
    </div>
  );
};

/* =========================================================
   Right-Side Inspector Drawer with Accessibility & Keyboard Focus Trap
   ========================================================= */
interface KhasraRightInspectorDrawerProps {
  id: string;
  villageId: string;
  onClose: () => void;
}

const KhasraRightInspectorDrawer: React.FC<KhasraRightInspectorDrawerProps> = ({ id, villageId, onClose }) => {
  const { hasPermission } = useAuth();
  const canViewAward = hasPermission("Award.View");
  const canViewLr = hasPermission("LR.View");

  const [loading, setLoading] = useState(true);
  const [detailError, setDetailError] = useState<string | null>(null);
  const [detail, setDetail] = useState<KhasraDetail | null>(null);
  const [ownership, setOwnership] = useState<{ forbidden?: boolean; error?: boolean; data?: RecordedOwnershipResult }>({});

  const previousActiveElement = useRef<HTMLElement | null>(null);
  const closeBtnRef = useRef<HTMLButtonElement | null>(null);
  const drawerRef = useRef<HTMLDivElement | null>(null);

  useEffect(() => {
    previousActiveElement.current = document.activeElement as HTMLElement;

    const timer = setTimeout(() => {
      closeBtnRef.current?.focus();
    }, 50);

    const handleKeyDown = (e: KeyboardEvent) => {
      if (e.key === "Escape") {
        e.preventDefault();
        onClose();
      }
      if (e.key === "Tab" && drawerRef.current) {
        const focusables = drawerRef.current.querySelectorAll<HTMLElement>(
          'a[href], button:not([disabled]), textarea:not([disabled]), input:not([disabled]), select:not([disabled]), [tabindex]:not([tabindex="-1"])'
        );
        if (focusables.length > 0) {
          const first = focusables[0];
          const last = focusables[focusables.length - 1];
          if (e.shiftKey && document.activeElement === first) {
            e.preventDefault();
            last.focus();
          } else if (!e.shiftKey && document.activeElement === last) {
            e.preventDefault();
            first.focus();
          }
        }
      }
    };

    document.addEventListener("keydown", handleKeyDown);

    return () => {
      clearTimeout(timer);
      document.removeEventListener("keydown", handleKeyDown);
      if (previousActiveElement.current && typeof previousActiveElement.current.focus === "function") {
        previousActiveElement.current.focus();
      }
    };
  }, [onClose]);

  useEffect(() => {
    let active = true;
    setLoading(true);
    setDetailError(null);

    Promise.all([
      fetch(`${api}/khasras/${id}`, { credentials: "include" }).then(async (r) => {
        if (!r.ok) throw new Error("Could not load Khasra details.");
        return r.json() as Promise<KhasraDetail>;
      }),
      fetch(`${api}/khasras/${id}/ownership`, { credentials: "include" }).then(async (r) => {
        if (r.status === 403) return { forbidden: true };
        if (!r.ok) return { error: true };
        return r.json() as Promise<RecordedOwnershipResult>;
      }),
    ])
      .then(([d, o]) => {
        if (active) {
          setDetail(d);
          if (o && "forbidden" in o && o.forbidden) {
            setOwnership({ forbidden: true });
          } else if (o && "error" in o && o.error) {
            setOwnership({ error: true });
          } else {
            setOwnership({ data: o as RecordedOwnershipResult });
          }
          setLoading(false);
        }
      })
      .catch((e: any) => {
        if (active) {
          setDetailError(e?.message || "Khasra record unavailable.");
          setLoading(false);
        }
      });

    return () => {
      active = false;
    };
  }, [id]);

  return (
    <div className="khasra-drawer-backdrop" onClick={onClose}>
      <div
        ref={drawerRef}
        className="khasra-drawer-panel"
        role="dialog"
        aria-modal="true"
        aria-labelledby="drawer-title-khasra"
        onClick={(e) => e.stopPropagation()}
      >
        <div className="khasra-drawer-header">
          <div>
            <span className="v-eyebrow">PARCEL INSPECTOR</span>
            <h3 id="drawer-title-khasra" style={{ margin: "2px 0 0", fontSize: "18px", fontWeight: 800 }}>
              Khasra #{detail?.displayNumber || id}
            </h3>
          </div>
          <button ref={closeBtnRef} className="icon-button" onClick={onClose} title="Close Inspector (Esc)">
            <IconClose size={18} />
          </button>
        </div>

        {loading ? (
          <div className="state loading" style={{ margin: "24px 0" }}>Loading parcel details…</div>
        ) : detailError || !detail ? (
          <div className="state error" style={{ margin: "24px 0" }}>{detailError || "Khasra record unavailable."}</div>
        ) : (
          <div className="khasra-drawer-body">
            {/* Village & Rectangle Context */}
            <div className="drawer-section-card">
              <span className="drawer-card-lbl">LOCATION CONTEXT</span>
              <div className="drawer-card-val">
                {detail.village?.name || "Village Record"}
              </div>
              <span className="drawer-card-sub">
                Sub-division: {detail.village?.subDivision?.name || "—"} • Rectangle: {detail.rectangleNumber || "—"}
              </span>
            </div>

            {/* Canonical Master Area */}
            <div className="drawer-section-card">
              <span className="drawer-card-lbl">CANONICAL MASTER AREA</span>
              <div className="drawer-card-val-lg">
                {detail.areaBigha != null
                  ? `${detail.areaBigha} Bigha ${detail.areaBiswa ?? 0} Biswa ${detail.areaBiswansi ?? 0} Biswansi`
                  : detail.totalArea != null
                  ? `${detail.totalArea} ${detail.areaUnit || ""}`
                  : "Not recorded"}
              </div>
            </div>

            {/* Recorded Owners */}
            <div className="drawer-section">
              <h4 className="drawer-section-title">Recorded Owners</h4>
              {ownership.forbidden ? (
                <p className="drawer-text-muted">Ownership details not available with current access</p>
              ) : ownership.error ? (
                <p className="drawer-text-muted">Ownership status unavailable</p>
              ) : ownership.data?.isAmbiguous ? (
                <p className="drawer-text-warn">
                  {ownership.data.message || "Recorded ownership is ambiguous and pending verification."}
                </p>
              ) : !ownership.data?.found ? (
                <p className="drawer-text-muted">
                  {ownership.data?.message || "No verified recorded ownership is available for this context."}
                </p>
              ) : ownership.data.owners?.length > 0 ? (
                <div className="drawer-owners-list">
                  {ownership.data.owners.map((owner, idx) => {
                    const shareStr = owner.rawShareText
                      ? owner.rawShareText
                      : owner.numerator != null && owner.denominator != null
                      ? `${owner.numerator}/${owner.denominator}`
                      : null;

                    return (
                      <div key={idx} className="drawer-owner-item">
                        <span className="owner-name">{owner.displayName}</span>
                        {shareStr && <span className="owner-share">Share: {shareStr}</span>}
                      </div>
                    );
                  })}
                </div>
              ) : (
                <p className="drawer-text-muted">
                  {ownership.data?.message || "No verified recorded ownership is available for this context."}
                </p>
              )}
            </div>

            {/* Linked Acquisition Awards */}
            <div className="drawer-section">
              <h4 className="drawer-section-title">Linked Acquisition Awards</h4>
              {detail.awards && detail.awards.length > 0 ? (
                <div className="drawer-links-list">
                  {detail.awards.map((a) => (
                    <div key={a.id} className="drawer-link-item">
                      {canViewAward ? (
                        <Link to={`/awards/${a.id}`} className="entity-link" style={{ fontWeight: 650 }}>
                          Award #{a.awardNumber} {a.acquisitionStatus ? `(${a.acquisitionStatus})` : ""}
                        </Link>
                      ) : (
                        <span style={{ fontWeight: 650 }}>
                          Award #{a.awardNumber} {a.acquisitionStatus ? `(${a.acquisitionStatus})` : ""}
                        </span>
                      )}
                    </div>
                  ))}
                </div>
              ) : (
                <p className="drawer-text-muted">No linked awards</p>
              )}
            </div>

            {/* Relevant Notifications */}
            <div className="drawer-section">
              <h4 className="drawer-section-title">Relevant Notifications</h4>
              {detail.notifications && detail.notifications.length > 0 ? (
                <div className="drawer-links-list">
                  {detail.notifications.map((n) => (
                    <div key={n.id} className="drawer-link-item">
                      {canViewAward ? (
                        <Link to={`/notifications/${n.id}`} className="entity-link" style={{ fontWeight: 650 }}>
                          {n.sectionType || "Notification"} #{n.notificationNumber || n.id}
                        </Link>
                      ) : (
                        <span style={{ fontWeight: 650 }}>
                          {n.sectionType || "Notification"} #{n.notificationNumber || n.id}
                        </span>
                      )}
                    </div>
                  ))}
                </div>
              ) : (
                <p className="drawer-text-muted">No linked notifications</p>
              )}
            </div>

            {/* LR Source Entries */}
            <div className="drawer-section">
              <h4 className="drawer-section-title">LR Source Entries</h4>
              {detail.lrEntries && detail.lrEntries.length > 0 ? (
                <div className="drawer-links-list">
                  {detail.lrEntries.map((e) => (
                    <div key={e.id} className="drawer-link-item" style={{ justifyContent: "space-between" }}>
                      {canViewLr ? (
                        <Link to={`/villages/${detail.village?.id || villageId}/lr/${e.villageLrId}`} className="entity-link">
                          Source Entry ({e.rawKhasraText || "Khasra"})
                        </Link>
                      ) : (
                        <span>Source Entry ({e.rawKhasraText || "Khasra"})</span>
                      )}
                      <span className="status-badge status-draft">{e.verificationStatus}</span>
                    </div>
                  ))}
                </div>
              ) : (
                <p className="drawer-text-muted">No linked LR entries</p>
              )}
            </div>
          </div>
        )}

        <div className="khasra-drawer-footer">
          <Link to={`/khasras/${id}`} className="primary-button" style={{ textDecoration: "none" }}>
            Open Full Khasra Record →
          </Link>
          <button type="button" className="secondary-button" onClick={onClose}>
            Close Inspector
          </button>
        </div>
      </div>
    </div>
  );
};
