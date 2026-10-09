import React, { useEffect, useState, useCallback, useMemo, useRef } from "react";
import { createPortal } from "react-dom";
import {
  FileText,
  Scale,
  Search,
  CreditCard,
  Archive,
  Shield,
  Check,
  ChevronDown,
  Pencil,
  KeyRound,
  UserX,
  UserCheck,
  Plus,
  X,
  ExternalLink,
} from "lucide-react";
import type {
  OfficeAuthority,
  OfficeModule,
  LandAccessLevel,
  HelperAccessLevel,
  OfficeAccountDetail,
  OfficeAccountOptions,
} from "./officeV3Types";
import "./admin.css";

const ALL_MODULES: { code: OfficeModule; label: string; description: string }[] = [
  {
    code: "DakMatters",
    label: "Dak & Matters",
    description: "Work on Dak/files, noting, drafts and outward action.",
  },
  {
    code: "Court",
    label: "Court",
    description: "Manage Court cases, orders, hearings and documents.",
  },
  {
    code: "Rti",
    label: "RTI",
    description: "Handle RTI applications and official responses.",
  },
  {
    code: "Accounts",
    label: "Accounts",
    description: "Handle Accounts and compensation-support work.",
  },
  {
    code: "RecordRoom",
    label: "Record Room",
    description: "Handle physical record/file custody.",
  },
];

interface ConfirmationDialogState {
  title: string;
  employeeName: string;
  summaryItems?: { label: string; value: string; isAddition?: boolean; isRemoval?: boolean }[];
  message?: string;
  warning?: string;
  confirmLabel: string;
  confirmTone?: "primary" | "danger" | "warning";
  onConfirm: () => Promise<void> | void;
}

// ─────────────────────────────────────────────────────────────────────────────
// SEARCHABLE FLOATING COMBOBOX / POPOVER FOR CIVIL DESIGNATIONS
// ─────────────────────────────────────────────────────────────────────────────

interface DesignationComboboxProps {
  value: string;
  onChange: (id: string) => void;
  options: { id: string; name: string }[];
  isCustomMode: boolean;
  onToggleCustomMode: (custom: boolean) => void;
  customValue: string;
  onCustomChange: (val: string) => void;
  placeholder?: string;
}

const DesignationCombobox: React.FC<DesignationComboboxProps> = ({
  value,
  onChange,
  options,
  isCustomMode,
  onToggleCustomMode,
  customValue,
  onCustomChange,
  placeholder,
}) => {
  const [isOpen, setIsOpen] = useState(false);
  const [searchQuery, setSearchQuery] = useState("");
  const [highlightIndex, setHighlightIndex] = useState(0);
  const triggerRef = useRef<HTMLButtonElement>(null);
  const popoverRef = useRef<HTMLDivElement>(null);
  const searchInputRef = useRef<HTMLInputElement>(null);
  const [popoverStyle, setPopoverStyle] = useState<React.CSSProperties>({});

  const selectedOption = useMemo(() => {
    return options.find((d) => d.id === value);
  }, [options, value]);

  const filteredOptions = useMemo(() => {
    const q = searchQuery.trim().toLowerCase();
    if (!q) return options;
    return options.filter((d) => d.name.toLowerCase().includes(q));
  }, [options, searchQuery]);

  const updatePosition = useCallback(() => {
    if (triggerRef.current) {
      const rect = triggerRef.current.getBoundingClientRect();
      const popoverWidth = Math.max(rect.width, 320);
      let top = rect.bottom + 4;
      let left = rect.left;
      if (left + popoverWidth > window.innerWidth - 16) {
        left = window.innerWidth - popoverWidth - 16;
      }
      if (top + 260 > window.innerHeight && rect.top > 270) {
        top = rect.top - 264;
      }
      setPopoverStyle({
        top: `${top}px`,
        left: `${left}px`,
        width: `${popoverWidth}px`,
      });
    }
  }, []);

  const handleOpen = () => {
    updatePosition();
    setIsOpen(true);
    setSearchQuery("");
    setHighlightIndex(0);
  };

  useEffect(() => {
    if (isOpen) {
      updatePosition();
      const timer = setTimeout(() => {
        searchInputRef.current?.focus();
      }, 50);

      const handleClickOutside = (e: MouseEvent) => {
        if (
          popoverRef.current &&
          !popoverRef.current.contains(e.target as Node) &&
          triggerRef.current &&
          !triggerRef.current.contains(e.target as Node)
        ) {
          setIsOpen(false);
        }
      };

      const handleScrollOrResize = () => {
        updatePosition();
      };

      document.addEventListener("mousedown", handleClickOutside);
      window.addEventListener("resize", handleScrollOrResize);
      window.addEventListener("scroll", handleScrollOrResize, true);

      return () => {
        clearTimeout(timer);
        document.removeEventListener("mousedown", handleClickOutside);
        window.removeEventListener("resize", handleScrollOrResize);
        window.removeEventListener("scroll", handleScrollOrResize, true);
      };
    }
  }, [isOpen, updatePosition]);

  const handleKeyDown = (e: React.KeyboardEvent) => {
    if (e.key === "Escape") {
      e.preventDefault();
      setIsOpen(false);
      triggerRef.current?.focus();
      return;
    }
    if (e.key === "ArrowDown") {
      e.preventDefault();
      setHighlightIndex((prev) => (filteredOptions.length > 0 ? (prev + 1) % filteredOptions.length : 0));
      return;
    }
    if (e.key === "ArrowUp") {
      e.preventDefault();
      setHighlightIndex((prev) => (filteredOptions.length > 0 ? (prev - 1 + filteredOptions.length) % filteredOptions.length : 0));
      return;
    }
    if (e.key === "Enter") {
      e.preventDefault();
      if (filteredOptions.length > 0 && filteredOptions[highlightIndex]) {
        onChange(filteredOptions[highlightIndex].id);
        setIsOpen(false);
        triggerRef.current?.focus();
      }
    }
  };

  return (
    <div className="rbac-form-group">
      <label>Civil Designation *</label>

      {/* Accessible & Backward-compatible hidden select */}
      <select
        value={isCustomMode ? "__OTHER__" : value}
        onChange={(e) => {
          if (e.target.value === "__OTHER__") {
            onToggleCustomMode(true);
            onChange("__OTHER__");
          } else {
            onToggleCustomMode(false);
            onChange(e.target.value);
          }
        }}
        style={{ display: "none" }}
        tabIndex={-1}
        aria-hidden="true"
      >
        <option value="">-- Select Civil Designation --</option>
        {options.map((d) => (
          <option key={d.id} value={d.id}>
            {d.name}
          </option>
        ))}
        <option value="__OTHER__">Other / Other Designation</option>
      </select>

      {!isCustomMode ? (
        <div className="rbac-combobox-wrapper">
          <button
            ref={triggerRef}
            type="button"
            className={`rbac-combobox-trigger ${!selectedOption ? "placeholder" : ""} ${isOpen ? "open" : ""}`}
            onClick={() => (isOpen ? setIsOpen(false) : handleOpen())}
            aria-haspopup="listbox"
            aria-expanded={isOpen}
          >
            <span>{selectedOption ? selectedOption.name : placeholder || "-- Select Civil Designation --"}</span>
            <ChevronDown size={16} style={{ color: "#64748B", transform: isOpen ? "rotate(180deg)" : "none", transition: "transform 150ms ease" }} />
          </button>

          <button
            type="button"
            className="rbac-designation-switch-btn"
            onClick={() => {
              onToggleCustomMode(true);
              onChange("__OTHER__");
              onCustomChange("");
            }}
          >
            Can't find the designation?  + Enter another designation
          </button>

          {isOpen &&
            createPortal(
              <div
                ref={popoverRef}
                className="rbac-combobox-popover"
                style={popoverStyle}
                onKeyDown={handleKeyDown}
                role="listbox"
              >
                <div className="rbac-combobox-search-box">
                  <Search size={16} className="rbac-combobox-search-icon" />
                  <input
                    ref={searchInputRef}
                    type="text"
                    className="rbac-combobox-search-input"
                    placeholder="Search designation, e.g. Patwari, Tehsildar..."
                    value={searchQuery}
                    onChange={(e) => {
                      setSearchQuery(e.target.value);
                      setHighlightIndex(0);
                    }}
                  />
                </div>

                <div
                  className="rbac-combobox-other-btn"
                  onClick={() => {
                    onToggleCustomMode(true);
                    onChange("__OTHER__");
                    onCustomChange("");
                    setIsOpen(false);
                  }}
                  role="button"
                  tabIndex={0}
                >
                  <Plus size={16} /> + Enter Other Designation
                </div>

                <ul className="rbac-combobox-list">
                  {filteredOptions.length === 0 ? (
                    <li className="rbac-combobox-empty">
                      No matching designations found.
                    </li>
                  ) : (
                    filteredOptions.map((d, idx) => {
                      const isSelected = value === d.id;
                      const isHighlighted = highlightIndex === idx;
                      return (
                        <li
                          key={d.id}
                          className={`rbac-combobox-item ${isSelected ? "selected" : ""} ${isHighlighted ? "active" : ""}`}
                          onClick={() => {
                            onChange(d.id);
                            setIsOpen(false);
                            triggerRef.current?.focus();
                          }}
                          onMouseEnter={() => setHighlightIndex(idx)}
                          role="option"
                          aria-selected={isSelected}
                        >
                          <span>{d.name}</span>
                          {isSelected && <Check size={16} color="#2563EB" />}
                        </li>
                      );
                    })
                  )}
                </ul>
              </div>,
              document.body
            )}
        </div>
      ) : (
        <div style={{ display: "flex", flexDirection: "column", gap: "6px" }}>
          <input
            type="text"
            required
            placeholder="e.g. Kanungo, Reader to ADM, Legal Assistant"
            value={customValue}
            onChange={(e) => onCustomChange(e.target.value)}
            className="rbac-input rbac-custom-input"
            autoFocus
          />
          <button
            type="button"
            className="rbac-designation-switch-btn"
            onClick={() => {
              onToggleCustomMode(false);
              onCustomChange("");
              onChange("");
            }}
          >
            ← Use standard designation
          </button>
        </div>
      )}
    </div>
  );
};

const getInitials = (name: string): string => {
  if (!name) return "OF";
  const parts = name.trim().split(/\s+/);
  if (parts.length === 1) return parts[0].slice(0, 2).toUpperCase();
  return (parts[0][0] + parts[parts.length - 1][0]).toUpperCase();
};

export const UsersAdmin: React.FC = () => {
  const [accounts, setAccounts] = useState<OfficeAccountDetail[]>([]);
  const [options, setOptions] = useState<OfficeAccountOptions | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  // Filters & Search
  const [searchQuery, setSearchQuery] = useState("");
  const [filterAuthority, setFilterAuthority] = useState<string>("ALL");
  const [filterStatus, setFilterStatus] = useState<string>("ALL");

  // Modals & Drawers
  const [showCreateModal, setShowCreateModal] = useState(false);
  const [showTechAdminModal, setShowTechAdminModal] = useState(false);
  const [selectedDrawerOfficer, setSelectedDrawerOfficer] = useState<OfficeAccountDetail | null>(null);
  const [editingOfficer, setEditingOfficer] = useState<OfficeAccountDetail | null>(null);
  const [inspectingOfficer, setInspectingOfficer] = useState<OfficeAccountDetail | null>(null);
  const [confirmationDialog, setConfirmationDialog] = useState<ConfirmationDialogState | null>(null);

  // Form states - Create Officer Modal (Default: ALL UNSELECTED)
  const [newUsername, setNewUsername] = useState("");
  const [newFullName, setNewFullName] = useState("");
  const [newDesignationId, setNewDesignationId] = useState("");
  const [isCustomDesignationMode, setIsCustomDesignationMode] = useState(false);
  const [newCustomDesignation, setNewCustomDesignation] = useState("");
  const [newIsSupervisor, setNewIsSupervisor] = useState(false);
  const [newModules, setNewModules] = useState<OfficeModule[]>([]);
  const [newCanRegisterInwardDak, setNewCanRegisterInwardDak] = useState(false);
  const [newLandAccess, setNewLandAccess] = useState<LandAccessLevel>("None");
  const [newPrimaryDeskId, setNewPrimaryDeskId] = useState("");
  const [newAdditionalDeskIds, setNewAdditionalDeskIds] = useState<string[]>([]);
  const [createFormError, setCreateFormError] = useState<string | null>(null);

  // Form states - Create Technical System Admin Modal
  const [techUsername, setTechUsername] = useState("");
  const [techFullName, setTechFullName] = useState("");
  const [techDesignationId, setTechDesignationId] = useState("");
  const [techFormError, setTechFormError] = useState<string | null>(null);

  // Form states - Edit Officer Drawer/Modal
  const [editFullName, setEditFullName] = useState("");
  const [editDesignationId, setEditDesignationId] = useState("");
  const [isEditCustomMode, setIsEditCustomMode] = useState(false);
  const [editCustomDesignation, setEditCustomDesignation] = useState("");
  const [editIsSupervisor, setEditIsSupervisor] = useState(false);
  const [editModules, setEditModules] = useState<OfficeModule[]>([]);
  const [editCanRegisterInwardDak, setEditCanRegisterInwardDak] = useState(false);
  const [editLandAccess, setEditLandAccess] = useState<LandAccessLevel>("None");
  const [editPrimaryDeskId, setEditPrimaryDeskId] = useState("");
  const [editAdditionalDeskIds, setEditAdditionalDeskIds] = useState<string[]>([]);
  const [editDrawerError, setEditDrawerError] = useState<string | null>(null);
  const [editDrawerLoading, setEditDrawerLoading] = useState(false);

  // Attached helper sub-editing in Edit Modal
  const [showAddHelperForOfficer, setShowAddHelperForOfficer] = useState(false);
  const [helperUsername, setHelperUsername] = useState("");
  const [helperFullName, setHelperFullName] = useState("");
  const [helperDesignationChoice, setHelperDesignationChoice] = useState<"DEO" | "OTHER">("DEO");
  const [helperCustomDesignation, setHelperCustomDesignation] = useState("");
  const [helperDeskId, setHelperDeskId] = useState("");
  const [helperAccess, setHelperAccess] = useState<HelperAccessLevel>("ReadOnly");
  const [helperFormError, setHelperFormError] = useState<string | null>(null);

  const [editingAttachedHelper, setEditingAttachedHelper] = useState<OfficeAccountDetail | null>(null);
  const [editingHelperAccess, setEditingHelperAccess] = useState<HelperAccessLevel>("ReadOnly");
  const [editingHelperDeskId, setEditingHelperDeskId] = useState("");

  // One-Time Credential Modal
  const [showCredentialModal, setShowCredentialModal] = useState(false);
  const [credentialData, setCredentialData] = useState<{
    username: string;
    temporaryCredential: string;
    expiresAt?: string | null;
  } | null>(null);
  const [credentialAcknowledged, setCredentialAcknowledged] = useState(false);
  const [copiedNotice, setCopiedNotice] = useState(false);

  const [actionLoading, setActionLoading] = useState(false);
  const [actionMessage, setActionMessage] = useState<string | null>(null);

  const loadData = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const [accountsRes, optionsRes] = await Promise.all([
        fetch("/api/office/accounts", { credentials: "include" }),
        fetch("/api/office/accounts/options", { credentials: "include" }),
      ]);

      if (accountsRes.status === 403 || optionsRes.status === 403) {
        setError("You do not have administrative authority to manage office accounts.");
        setLoading(false);
        return;
      }

      if (!accountsRes.ok || !optionsRes.ok) {
        throw new Error("Unable to load accounts directory.");
      }

      const accData = (await accountsRes.json()) as OfficeAccountDetail[];
      const optData = (await optionsRes.json()) as OfficeAccountOptions;

      setAccounts(accData);
      setOptions(optData);
    } catch (err: any) {
      setError(err.message || "Failed to load office accounts.");
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    void loadData();
  }, [loadData]);

  // Derived: Is the selected designation in Create Form canonical ADM?
  const isSelectedDesignationAdm = useMemo(() => {
    if (isCustomDesignationMode || !newDesignationId) return false;
    const d = options?.designations.find((x) => x.id === newDesignationId);
    return Boolean(d && (d.code === "ADM" || d.name.toLowerCase().includes("additional district magistrate")));
  }, [isCustomDesignationMode, newDesignationId, options?.designations]);

  // Derived: Is the designation in Edit Form ADM?
  const isEditDesignationAdm = useMemo(() => {
    if (isEditCustomMode || !editDesignationId) return false;
    const d = options?.designations.find((x) => x.id === editDesignationId);
    return Boolean(d && (d.code === "ADM" || d.name.toLowerCase().includes("additional district magistrate")));
  }, [isEditCustomMode, editDesignationId, options?.designations]);

  const openCreateOfficerModal = () => {
    setNewUsername("");
    setNewFullName("");
    setNewDesignationId("");
    setIsCustomDesignationMode(false);
    setNewCustomDesignation("");
    setNewIsSupervisor(false);
    setNewModules([]);
    setNewCanRegisterInwardDak(false);
    setNewLandAccess("None");
    setNewPrimaryDeskId("");
    setNewAdditionalDeskIds([]);
    setCreateFormError(null);
    setShowCreateModal(true);
  };

  // 1. CREATE OFFICER: Validation & Review -> Open Confirmation Dialog
  const handleCreateOfficerSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    setCreateFormError(null);

    const username = newUsername.trim();
    const fullName = newFullName.trim();

    if (!username) {
      setCreateFormError("Official Username is required.");
      return;
    }
    if (!fullName) {
      setCreateFormError("Full Name is required.");
      return;
    }

    if (isCustomDesignationMode && !newCustomDesignation.trim()) {
      setCreateFormError("Please enter a custom designation title.");
      return;
    }
    if (!isCustomDesignationMode && !newDesignationId) {
      setCreateFormError("Please select a civil designation or switch to custom designation.");
      return;
    }

    const designationName = isCustomDesignationMode
      ? `${newCustomDesignation.trim()} (Custom)`
      : options?.designations.find((d) => d.id === newDesignationId)?.name || "Unassigned";

    let authority: OfficeAuthority = "STANDARD_OFFICER";
    let authorityLabel = "Officer / Staff";
    if (newIsSupervisor) {
      authority = "OFFICE_SUPERVISOR";
      authorityLabel = "Office Supervisor";
    } else if (isSelectedDesignationAdm && options?.authority === "SYSTEM_ADMIN") {
      authority = "OFFICE_ADMIN";
      authorityLabel = "Office Administrator";
    }

    const primaryDesk = options?.desks.find((d) => d.id === newPrimaryDeskId);
    const addlDesks = options?.desks.filter((d) => newAdditionalDeskIds.includes(d.id)) || [];
    const deskSummary = primaryDesk
      ? addlDesks.length > 0
        ? `${primaryDesk.name} (+${addlDesks.length} more)`
        : primaryDesk.name
      : "No Specific Seat";

    const isOther = isCustomDesignationMode || newDesignationId === "__OTHER__";
    const allDeskIds = [newPrimaryDeskId, ...newAdditionalDeskIds].filter(Boolean);
    const payload = {
      username,
      account: {
        fullName,
        designationId: isOther ? null : newDesignationId || null,
        customDesignation: isOther ? newCustomDesignation.trim() : null,
        authority,
        modules: newIsSupervisor ? [] : newModules,
        canRegisterInwardDak: newIsSupervisor ? true : newCanRegisterInwardDak,
        landAccess: newIsSupervisor ? ("ViewWrite" as LandAccessLevel) : newLandAccess,
        deskIds: Array.from(new Set(allDeskIds)),
      },
    };

    const modulesSummary = newIsSupervisor || authority === "OFFICE_ADMIN"
      ? "Full Office Access"
      : newModules.length > 0
      ? newModules.map((m) => ALL_MODULES.find((x) => x.code === m)?.label || m).join(", ")
      : "None";

    const summaryItems = [
      { label: "Civil Designation", value: designationName },
      { label: "Authority Level", value: authorityLabel },
      { label: "Work Access", value: modulesSummary },
      {
        label: "Inward Dak Registry",
        value: newIsSupervisor || newCanRegisterInwardDak ? "Can Register Inward Dak" : "No Registry Rights",
      },
      {
        label: "Land Records",
        value: newIsSupervisor ? "View + Write" : newLandAccess === "ViewWrite" ? "View + Write" : newLandAccess === "ViewOnly" ? "View Only" : "None",
      },
      { label: "Assigned Seat / Desk", value: deskSummary },
    ];

    setConfirmationDialog({
      title: "Confirm New Officer Account",
      employeeName: `${fullName} (${username})`,
      summaryItems,
      warning: authority === "OFFICE_ADMIN"
        ? "NOTICE: Selecting Additional District Magistrate (ADM) designates this account as an Office Administrator with full office administrative control."
        : undefined,
      confirmLabel: "Confirm & Create Account",
      confirmTone: "primary",
      onConfirm: async () => {
        setActionLoading(true);
        try {
          const res = await fetch("/api/office/accounts", {
            method: "POST",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify(payload),
            credentials: "include",
          });

          if (!res.ok) {
            const err = await res.json().catch(() => ({}));
            throw new Error(err.message || `Account creation failed (HTTP ${res.status})`);
          }

          const created = await res.json();
          setShowCreateModal(false);
          setConfirmationDialog(null);
          setCredentialData({
            username: created.account?.username || username,
            temporaryCredential: created.temporaryCredential,
            expiresAt: created.credentialExpiresAt,
          });
          setCredentialAcknowledged(false);
          setShowCredentialModal(true);
          void loadData();
        } catch (err: any) {
          setCreateFormError(err.message || "Failed to create account.");
          setConfirmationDialog(null);
        } finally {
          setActionLoading(false);
        }
      },
    });
  };

  // 2. CREATE SYSTEM ADMIN: Technical security account
  const handleCreateTechAdminSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    setTechFormError(null);

    const username = techUsername.trim();
    const fullName = techFullName.trim();

    if (!username) {
      setTechFormError("Official Username is required.");
      return;
    }
    if (!fullName) {
      setTechFormError("Full Name is required.");
      return;
    }

    const payload = {
      username,
      account: {
        fullName,
        designationId: techDesignationId || null,
        customDesignation: null,
        authority: "SYSTEM_ADMIN" as OfficeAuthority,
        modules: [],
        canRegisterInwardDak: true,
        landAccess: "ViewWrite" as LandAccessLevel,
        deskIds: [],
      },
    };

    setConfirmationDialog({
      title: "Confirm Technical System Administrator",
      employeeName: `${fullName} (${username})`,
      message: "Create a technical System Administrator with platform security authority?",
      summaryItems: [
        { label: "Authority", value: "System Administrator (Full System Access)" },
        { label: "Designation", value: techDesignationId ? options?.designations.find((d) => d.id === techDesignationId)?.name || "Technical" : "No civil designation" },
        { label: "Desk", value: "Not required / No desk" },
      ],
      confirmLabel: "Confirm & Create Admin",
      confirmTone: "primary",
      onConfirm: async () => {
        setActionLoading(true);
        try {
          const res = await fetch("/api/office/accounts", {
            method: "POST",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify(payload),
            credentials: "include",
          });

          if (!res.ok) {
            const err = await res.json().catch(() => ({}));
            throw new Error(err.message || `System Admin creation failed (HTTP ${res.status})`);
          }

          const created = await res.json();
          setShowTechAdminModal(false);
          setConfirmationDialog(null);
          setCredentialData({
            username: created.account?.username || username,
            temporaryCredential: created.temporaryCredential,
            expiresAt: created.credentialExpiresAt,
          });
          setCredentialAcknowledged(false);
          setShowCredentialModal(true);
          void loadData();
        } catch (err: any) {
          setTechFormError(err.message || "Failed to create System Administrator.");
          setConfirmationDialog(null);
        } finally {
          setActionLoading(false);
        }
      },
    });
  };

  const openEditDrawer = async (officer: OfficeAccountDetail) => {
    setEditDrawerLoading(true);
    setEditDrawerError(null);
    setShowAddHelperForOfficer(false);
    setEditingAttachedHelper(null);
    try {
      const res = await fetch(`/api/office/accounts/${officer.id}`, { credentials: "include" });
      if (!res.ok) throw new Error("Could not reload officer details.");
      const fresh = (await res.json()) as OfficeAccountDetail;
      setEditingOfficer(fresh);
      setEditFullName(fresh.fullName || "");
      if (fresh.customDesignation) {
        setIsEditCustomMode(true);
        setEditDesignationId("");
        setEditCustomDesignation(fresh.customDesignation);
      } else {
        setIsEditCustomMode(false);
        setEditDesignationId(fresh.designationId || "");
        setEditCustomDesignation("");
      }
      setEditIsSupervisor(fresh.authority === "OFFICE_SUPERVISOR");
      setEditModules(fresh.modules || []);
      setEditCanRegisterInwardDak(fresh.canRegisterInwardDak);
      setEditLandAccess(fresh.landAccess || "None");
      setEditPrimaryDeskId(fresh.deskIds[0] || "");
      setEditAdditionalDeskIds(fresh.deskIds.slice(1) || []);
    } catch (err: any) {
      alert(err.message || "Failed to load officer details.");
    } finally {
      setEditDrawerLoading(false);
    }
  };

  // 3. EDIT OFFICER: Compute Diff & Review -> Confirmation Dialog
  const handleEditOfficerReview = (e: React.FormEvent) => {
    e.preventDefault();
    if (!editingOfficer) return;
    setEditDrawerError(null);

    const fullName = editFullName.trim();
    if (!fullName) {
      setEditDrawerError("Full Name is required.");
      return;
    }

    if (isEditCustomMode && !editCustomDesignation.trim()) {
      setEditDrawerError("Please enter a custom designation title.");
      return;
    }

    let authority: OfficeAuthority = editingOfficer.authority;
    if (editingOfficer.authority === "STANDARD_OFFICER" || editingOfficer.authority === "OFFICE_SUPERVISOR") {
      authority = editIsSupervisor ? "OFFICE_SUPERVISOR" : "STANDARD_OFFICER";
    }

    const isOther = isEditCustomMode || editDesignationId === "__OTHER__";
    const allEditDeskIds = [editPrimaryDeskId, ...editAdditionalDeskIds].filter(Boolean);
    const payload = {
      account: {
        fullName,
        designationId: isOther ? null : editDesignationId || null,
        customDesignation: isOther ? editCustomDesignation.trim() : null,
        authority,
        modules: editIsSupervisor || authority === "OFFICE_ADMIN" || authority === "SYSTEM_ADMIN" ? [] : editModules,
        canRegisterInwardDak: editIsSupervisor ? true : editCanRegisterInwardDak,
        landAccess: editIsSupervisor ? ("ViewWrite" as LandAccessLevel) : editLandAccess,
        deskIds: Array.from(new Set(allEditDeskIds)),
      },
      expectedRevision: editingOfficer.revision,
    };

    // Calculate Diff for Confirmation Dialog
    const diffItems: { label: string; value: string; isAddition?: boolean; isRemoval?: boolean }[] = [];

    // Full name diff
    if (fullName !== editingOfficer.fullName) {
      diffItems.push({ label: "Name", value: `${editingOfficer.fullName} → ${fullName}` });
    }

    // Designation diff
    const oldDesig = editingOfficer.effectiveDesignation || "Unassigned";
    const newDesig = isEditCustomMode
      ? `${editCustomDesignation.trim()} (Custom)`
      : options?.designations.find((d) => d.id === editDesignationId)?.name || "Unassigned";
    if (oldDesig !== newDesig) {
      diffItems.push({ label: "Designation", value: `${oldDesig} → ${newDesig}` });
    }

    // Authority diff
    if (authority !== editingOfficer.authority) {
      diffItems.push({ label: "Authority", value: `${getAuthorityBadgeLabel(editingOfficer.authority)} → ${getAuthorityBadgeLabel(authority)}` });
    }

    // Modules added / removed
    if (!editIsSupervisor && editingOfficer.authority !== "OFFICE_ADMIN" && editingOfficer.authority !== "SYSTEM_ADMIN") {
      const addedMods = editModules.filter((m) => !editingOfficer.modules.includes(m));
      const removedMods = editingOfficer.modules.filter((m) => !editModules.includes(m));
      for (const m of addedMods) {
        const modName = ALL_MODULES.find((x) => x.code === m)?.label || m;
        diffItems.push({ label: "Access Added", value: `+ ${modName}`, isAddition: true });
      }
      for (const m of removedMods) {
        const modName = ALL_MODULES.find((x) => x.code === m)?.label || m;
        diffItems.push({ label: "Access Removed", value: `- ${modName}`, isRemoval: true });
      }

      // Inward Dak diff
      if (editCanRegisterInwardDak !== editingOfficer.canRegisterInwardDak) {
        if (editCanRegisterInwardDak) {
          diffItems.push({ label: "Inward Dak", value: "+ Granted Inward Dak Registry", isAddition: true });
        } else {
          diffItems.push({ label: "Inward Dak", value: "- Revoked Inward Dak Registry", isRemoval: true });
        }
      }

      // Land Records diff
      if (editLandAccess !== editingOfficer.landAccess) {
        diffItems.push({ label: "Land Records", value: `${editingOfficer.landAccess} → ${editLandAccess}` });
      }
    }

    // Desk diff
    const oldPrimaryDesk = options?.desks.find((d) => editingOfficer.deskIds[0] === d.id)?.name || "None";
    const newPrimaryDesk = options?.desks.find((d) => editPrimaryDeskId === d.id)?.name || "None";
    if (oldPrimaryDesk !== newPrimaryDesk) {
      diffItems.push({ label: "Seat / Desk", value: `${oldPrimaryDesk} → ${newPrimaryDesk}` });
    }

    // Additional desks diff
    const oldAddl = editingOfficer.deskIds.slice(1);
    const addedDesks = editAdditionalDeskIds.filter((id) => !oldAddl.includes(id));
    const removedDesks = oldAddl.filter((id) => !editAdditionalDeskIds.includes(id));
    for (const dId of addedDesks) {
      const dName = options?.desks.find((d) => d.id === dId)?.name || dId;
      diffItems.push({ label: "Seat Added", value: `+ ${dName}`, isAddition: true });
    }
    for (const dId of removedDesks) {
      const dName = options?.desks.find((d) => d.id === dId)?.name || dId;
      diffItems.push({ label: "Seat Removed", value: `- ${dName}`, isRemoval: true });
    }

    if (diffItems.length === 0) {
      diffItems.push({ label: "Notice", value: "No operational access changes detected." });
    }

    setConfirmationDialog({
      title: "Confirm Access Changes",
      employeeName: editingOfficer.fullName,
      summaryItems: diffItems,
      warning: "Saving changes will invalidate current active sessions for this employee.",
      confirmLabel: "Confirm Changes",
      confirmTone: "primary",
      onConfirm: async () => {
        setActionLoading(true);
        try {
          const res = await fetch(`/api/office/accounts/${editingOfficer.id}`, {
            method: "PUT",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify(payload),
            credentials: "include",
          });

          if (res.status === 409) {
            setEditDrawerError("This account changed elsewhere. Latest details have been reloaded.");
            setConfirmationDialog(null);
            await loadData();
            const refetch = await fetch(`/api/office/accounts/${editingOfficer.id}`, { credentials: "include" });
            if (refetch.ok) setEditingOfficer(await refetch.json());
            return;
          }

          if (!res.ok) {
            const err = await res.json().catch(() => ({}));
            throw new Error(err.message || `Failed to update officer (HTTP ${res.status})`);
          }

          setEditingOfficer(null);
          setConfirmationDialog(null);
          setActionMessage("Account updated successfully. Existing login sessions for this employee were ended.");
          setTimeout(() => setActionMessage(null), 5000);
          void loadData();
        } catch (err: any) {
          setEditDrawerError(err.message || "Failed to update account.");
          setConfirmationDialog(null);
        } finally {
          setActionLoading(false);
        }
      },
    });
  };

  // 4. ADD HELPER SUB-FORM WITH CONFIRMATION
  const handleAddHelperSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!editingOfficer) return;
    setHelperFormError(null);

    const username = helperUsername.trim();
    const fullName = helperFullName.trim();

    if (!username) {
      setHelperFormError("Official Username is required.");
      return;
    }
    if (!fullName) {
      setHelperFormError("Full Name is required.");
      return;
    }

    if (helperDesignationChoice === "OTHER" && !helperCustomDesignation.trim()) {
      setHelperFormError("Please enter custom designation title.");
      return;
    }

    const deoId = options?.designations.find((d) => d.code === "DEO" || d.name.toLowerCase().includes("data entry"))?.id;
    const payload = {
      username,
      helper: {
        fullName,
        designationId: helperDesignationChoice === "DEO" ? deoId : null,
        customDesignation: helperDesignationChoice === "OTHER" ? helperCustomDesignation.trim() : null,
        deskId: helperDeskId || null,
        access: helperAccess,
      },
    };

    setConfirmationDialog({
      title: "Confirm Attached Assistant",
      employeeName: `${fullName} (${username})`,
      message: `Attach this assistant under supervising officer ${editingOfficer.fullName}?`,
      summaryItems: [
        { label: "Designation", value: helperDesignationChoice === "DEO" ? "DEO" : `${helperCustomDesignation.trim()} (Custom)` },
        { label: "Access Level", value: helperAccess === "ReadWrite" ? "Read + Write" : helperAccess === "ReadOnly" ? "Read Only" : "None" },
        { label: "Supervisor", value: editingOfficer.fullName },
      ],
      confirmLabel: "Confirm & Attach Assistant",
      confirmTone: "primary",
      onConfirm: async () => {
        setActionLoading(true);
        try {
          const res = await fetch(`/api/office/accounts/${editingOfficer.id}/helpers`, {
            method: "POST",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify(payload),
            credentials: "include",
          });

          if (!res.ok) {
            const err = await res.json().catch(() => ({}));
            throw new Error(err.message || `Failed to add helper (HTTP ${res.status})`);
          }

          const created = await res.json();
          setShowAddHelperForOfficer(false);
          setConfirmationDialog(null);
          setCredentialData({
            username: created.account?.username || username,
            temporaryCredential: created.temporaryCredential,
            expiresAt: created.credentialExpiresAt,
          });
          setCredentialAcknowledged(false);
          setShowCredentialModal(true);
          void loadData();
        } catch (err: any) {
          setHelperFormError(err.message || "Failed to attach helper.");
          setConfirmationDialog(null);
        } finally {
          setActionLoading(false);
        }
      },
    });
  };

  // 4b. EDIT ATTACHED HELPER ACCESS WITH CONFIRMATION
  const openEditHelperAccess = (h: OfficeAccountDetail) => {
    setEditingAttachedHelper(h);
    const codes = h.helperPermissionCodes || [];
    const isWrite = codes.some((c) => !c.endsWith(".View"));
    setEditingHelperAccess(codes.length === 0 ? "None" : isWrite ? "ReadWrite" : "ReadOnly");
    setEditingHelperDeskId(h.deskIds[0] || "");
  };

  const handleSaveAttachedHelperAccess = (e: React.FormEvent) => {
    e.preventDefault();
    if (!editingAttachedHelper || !editingOfficer) return;

    const oldCodes = editingAttachedHelper.helperPermissionCodes || [];
    const oldIsWrite = oldCodes.some((c) => !c.endsWith(".View"));
    const oldAccessLabel = oldCodes.length === 0 ? "None" : oldIsWrite ? "Read + Write" : "Read Only";
    const newAccessLabel = editingHelperAccess === "ReadWrite" ? "Read + Write" : editingHelperAccess === "ReadOnly" ? "Read Only" : "None";

    const diffItems = [
      { label: "Helper Access", value: `${oldAccessLabel} → ${newAccessLabel}` },
      { label: "Supervising Officer", value: editingOfficer.fullName },
    ];

    setConfirmationDialog({
      title: "Confirm Helper Permission Changes",
      employeeName: `${editingAttachedHelper.fullName} (${editingAttachedHelper.username})`,
      summaryItems: diffItems,
      warning: "Saving changes will invalidate current active sessions for this assistant.",
      confirmLabel: "Confirm Helper Changes",
      confirmTone: "primary",
      onConfirm: async () => {
        setActionLoading(true);
        try {
          const payload = {
            account: {
              fullName: editingAttachedHelper.fullName,
              designationId: editingAttachedHelper.designationId,
              customDesignation: editingAttachedHelper.customDesignation,
              authority: "HELPER" as OfficeAuthority,
              modules: [],
              canRegisterInwardDak: false,
              landAccess: "None" as LandAccessLevel,
              deskIds: editingHelperDeskId ? [editingHelperDeskId] : [],
              helperAccess: editingHelperAccess,
            },
            expectedRevision: editingAttachedHelper.revision,
          };

          const res = await fetch(`/api/office/accounts/${editingAttachedHelper.id}`, {
            method: "PUT",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify(payload),
            credentials: "include",
          });

          if (!res.ok) {
            const err = await res.json().catch(() => ({}));
            throw new Error(err.message || `Failed to update helper (HTTP ${res.status})`);
          }

          setEditingAttachedHelper(null);
          setConfirmationDialog(null);
          setActionMessage(`Assistant ${editingAttachedHelper.username} access updated.`);
          setTimeout(() => setActionMessage(null), 5000);
          void loadData();
        } catch (err: any) {
          alert(err.message || "Failed to update assistant access.");
          setConfirmationDialog(null);
        } finally {
          setActionLoading(false);
        }
      },
    });
  };

  // 5. RESET CREDENTIAL WITH CONFIRMATION
  const handleResetCredentialClick = (officer: OfficeAccountDetail) => {
    setConfirmationDialog({
      title: "Confirm Password Reset",
      employeeName: `${officer.fullName} (${officer.username})`,
      message: `Reset password for ${officer.fullName}? This will invalidate the employee's current sessions and issue a new temporary credential.`,
      warning: "The officer will be required to set a permanent password upon first login with the new credential.",
      confirmLabel: "Confirm Password Reset",
      confirmTone: "warning",
      onConfirm: async () => {
        setActionLoading(true);
        try {
          const res = await fetch(`/api/office/accounts/${officer.id}/reset-credential`, {
            method: "POST",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify({ expectedRevision: officer.revision }),
            credentials: "include",
          });

          if (res.status === 409) {
            alert("This account was updated elsewhere. Reloading latest records.");
            setConfirmationDialog(null);
            await loadData();
            return;
          }

          if (!res.ok) {
            const err = await res.json().catch(() => ({}));
            throw new Error(err.message || `Reset failed (HTTP ${res.status})`);
          }

          const data = await res.json();
          setConfirmationDialog(null);
          setCredentialData({
            username: officer.username,
            temporaryCredential: data.temporaryCredential,
            expiresAt: data.credentialExpiresAt,
          });
          setCredentialAcknowledged(false);
          setShowCredentialModal(true);
          void loadData();
        } catch (err: any) {
          alert(err.message || "Credential reset failed.");
          setConfirmationDialog(null);
        } finally {
          setActionLoading(false);
        }
      },
    });
  };

  // 6. TOGGLE STATUS (DISABLE / ENABLE) WITH CONFIRMATION
  const handleToggleStatusClick = (officer: OfficeAccountDetail) => {
    const isDeactivating = officer.isActive;
    const title = isDeactivating ? "Confirm Account Deactivation" : "Confirm Account Activation";
    const message = isDeactivating
      ? `Disable ${officer.fullName}? The employee will immediately lose access to the LAC Platform.`
      : `Enable ${officer.fullName}? The employee will regain access to the LAC Platform.`;
    const confirmLabel = isDeactivating ? "Confirm Deactivation" : "Confirm Activation";
    const confirmTone = isDeactivating ? "danger" : "primary";

    setConfirmationDialog({
      title,
      employeeName: `${officer.fullName} (${officer.username})`,
      message,
      warning: isDeactivating ? "All active sessions will be terminated immediately." : undefined,
      confirmLabel,
      confirmTone,
      onConfirm: async () => {
        setActionLoading(true);
        try {
          const res = await fetch(`/api/office/accounts/${officer.id}/toggle-status`, {
            method: "POST",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify({ expectedRevision: officer.revision }),
            credentials: "include",
          });

          if (res.status === 409) {
            alert("This account was updated elsewhere. Reloading latest records.");
            setConfirmationDialog(null);
            await loadData();
            return;
          }

          if (!res.ok) {
            const err = await res.json().catch(() => ({}));
            throw new Error(err.message || `Status update failed (HTTP ${res.status})`);
          }

          setConfirmationDialog(null);
          setActionMessage(`Officer ${officer.username} is now ${officer.isActive ? "inactive" : "active"}.`);
          setTimeout(() => setActionMessage(null), 4000);
          void loadData();
        } catch (err: any) {
          alert(err.message || "Failed to update officer status.");
          setConfirmationDialog(null);
        } finally {
          setActionLoading(false);
        }
      },
    });
  };

  const toggleModuleSelection = (mod: OfficeModule, currentList: OfficeModule[], setter: (m: OfficeModule[]) => void) => {
    if (currentList.includes(mod)) {
      setter(currentList.filter((m) => m !== mod));
    } else {
      setter([...currentList, mod]);
    }
  };

  const copyCredentialToClipboard = () => {
    if (!credentialData?.temporaryCredential) return;
    navigator.clipboard.writeText(credentialData.temporaryCredential);
    setCopiedNotice(true);
    setTimeout(() => setCopiedNotice(false), 2500);
  };

  const closeCredentialModal = () => {
    setShowCredentialModal(false);
    setCredentialData(null);
    setCredentialAcknowledged(false);
  };

  // Concise Work Access Summary for Directory Table
  const getWorkAccessSummary = (account: OfficeAccountDetail): string => {
    if (account.authority === "SYSTEM_ADMIN") {
      return "Full System Access";
    }
    if (account.authority === "OFFICE_ADMIN" || account.authority === "OFFICE_SUPERVISOR") {
      return "Full Office Access";
    }
    if (account.authority === "HELPER") {
      const hasWrite = account.helperPermissionCodes?.some((c) => !c.endsWith(".View"));
      return hasWrite ? "Assistant · Read + Write" : "Assistant · Read Only";
    }

    const parts: string[] = [];
    if (account.modules.length > 0) {
      const moduleNames = account.modules.map((m) => {
        const match = ALL_MODULES.find((x) => x.code === m);
        return match ? match.label : m;
      });
      if (moduleNames.length <= 2) {
        parts.push(moduleNames.join(" · "));
      } else {
        parts.push(`${moduleNames[0]} · ${moduleNames[1]} (+${moduleNames.length - 2})`);
      }
    }

    if (account.canRegisterInwardDak) {
      parts.push("Inward Registry");
    }

    if (account.landAccess === "ViewWrite") {
      parts.push("Land View + Write");
    } else if (account.landAccess === "ViewOnly") {
      parts.push("Land View");
    }

    const assignedDesks = options?.desks.filter((d) => account.deskIds.includes(d.id)) || [];
    if (assignedDesks.length > 0) {
      const deskText = assignedDesks.length > 1
        ? `${assignedDesks[0].name} (+${assignedDesks.length - 1} more)`
        : assignedDesks[0].name;
      parts.push(`Desk: ${deskText}`);
    }

    if (parts.length === 0) {
      return "No Assigned Work";
    }

    return parts.join(" · ");
  };

  // Filtered accounts
  const filteredAccounts = useMemo(() => {
    const q = searchQuery.trim().toLowerCase();
    return accounts.filter((acc) => {
      if (filterAuthority !== "ALL" && acc.authority !== filterAuthority) return false;
      if (filterStatus === "ACTIVE" && !acc.isActive) return false;
      if (filterStatus === "INACTIVE" && acc.isActive) return false;
      if (!q) return true;
      return (
        acc.username.toLowerCase().includes(q) ||
        (acc.fullName && acc.fullName.toLowerCase().includes(q)) ||
        (acc.effectiveDesignation && acc.effectiveDesignation.toLowerCase().includes(q))
      );
    });
  }, [accounts, searchQuery, filterAuthority, filterStatus]);

  // Attached helpers map
  const attachedHelpersMap = useMemo(() => {
    const map = new Map<string, OfficeAccountDetail[]>();
    for (const acc of accounts) {
      if (acc.supervisingOfficerId) {
        const list = map.get(acc.supervisingOfficerId) || [];
        list.push(acc);
        map.set(acc.supervisingOfficerId, list);
      }
    }
    return map;
  }, [accounts]);

  const getAuthorityBadgeLabel = (auth: OfficeAuthority) => {
    switch (auth) {
      case "SYSTEM_ADMIN": return "System Administrator";
      case "OFFICE_ADMIN": return "Office Administrator";
      case "OFFICE_SUPERVISOR": return "Office Supervisor";
      case "STANDARD_OFFICER": return "Officer / Staff";
      case "HELPER": return "Helper";
      default: return auth;
    }
  };

  const getAuthorityBadgeTone = (auth: OfficeAuthority) => {
    switch (auth) {
      case "SYSTEM_ADMIN": return "rbac-badge-purple";
      case "OFFICE_ADMIN": return "rbac-badge-indigo";
      case "OFFICE_SUPERVISOR": return "rbac-badge-blue";
      case "STANDARD_OFFICER": return "rbac-badge-teal";
      case "HELPER": return "rbac-badge-slate";
      default: return "rbac-badge-outline";
    }
  };

  return (
    <div className="rbac-admin-root">
      {/* Top Header */}
      <div className="rbac-header-row">
        <div className="rbac-header-title">
          <h1>Officers &amp; Staff Directory</h1>
          <p>Government office staff directory, operational seat assignments, and work modules.</p>
        </div>

        <div className="rbac-actions-group">
          <button className="rbac-btn-primary" onClick={openCreateOfficerModal} disabled={loading}>
            <Plus size={16} /> + Create Officer / Staff
          </button>

          {options?.authority === "SYSTEM_ADMIN" && (
            <button
              className="rbac-btn-outline"
              onClick={() => {
                setTechUsername("");
                setTechFullName("");
                setTechDesignationId("");
                setTechFormError(null);
                setShowTechAdminModal(true);
              }}
            >
              <Shield size={16} /> + Create System Admin
            </button>
          )}
        </div>
      </div>

      {/* Error Banner */}
      {error && (
        <div style={{ background: "#fef2f2", border: "1px solid #fecaca", padding: "12px 18px", borderRadius: "10px", color: "#991b1b", fontSize: "13.5px" }}>
          ⚠️ {error}
        </div>
      )}

      {/* Action Notification Banner */}
      {actionMessage && (
        <div style={{ background: "#f0fdf4", border: "1px solid #bbf7d0", padding: "12px 18px", borderRadius: "10px", color: "#166534", fontSize: "13.5px" }}>
          ✓ {actionMessage}
        </div>
      )}

      {/* Filter / Search Bar */}
      <div className="rbac-filter-bar">
        <div className="rbac-search-box">
          <span className="rbac-search-icon">
            <Search size={16} />
          </span>
          <input
            type="text"
            placeholder="Search by officer name, username, or designation…"
            value={searchQuery}
            onChange={(e) => setSearchQuery(e.target.value)}
          />
        </div>

        <select
          className="rbac-filter-select"
          value={filterAuthority}
          onChange={(e) => setFilterAuthority(e.target.value)}
        >
          <option value="ALL">All Authorities</option>
          <option value="OFFICE_ADMIN">Office Administrator</option>
          <option value="OFFICE_SUPERVISOR">Office Supervisor</option>
          <option value="STANDARD_OFFICER">Ordinary Officers</option>
          <option value="HELPER">Helpers &amp; Assistants</option>
          <option value="SYSTEM_ADMIN">Technical Administrators</option>
        </select>

        <select
          className="rbac-filter-select"
          value={filterStatus}
          onChange={(e) => setFilterStatus(e.target.value)}
        >
          <option value="ALL">All Statuses</option>
          <option value="ACTIVE">Active</option>
          <option value="INACTIVE">Inactive</option>
        </select>
      </div>

      {/* Directory Table */}
      {loading ? (
        <div style={{ padding: "48px", textAlign: "center", color: "#64748b", background: "#ffffff", borderRadius: "12px", border: "1px solid #e2e8f0" }}>
          Loading official directory…
        </div>
      ) : filteredAccounts.length === 0 ? (
        <div style={{ background: "#ffffff", border: "1px solid #e2e8f0", borderRadius: "12px", padding: "48px 24px", textAlign: "center" }}>
          <h3 style={{ margin: "0 0 8px 0", color: "#1e293b", fontSize: "18px" }}>No Officers Found</h3>
          <p style={{ margin: 0, color: "#64748b", fontSize: "14px" }}>
            No employee records matched the selected filters.
          </p>
        </div>
      ) : (
        <div className="rbac-table-container">
          <table className="rbac-table">
            <thead>
              <tr>
                <th style={{ width: "24%" }}>Employee</th>
                <th style={{ width: "17%" }}>Designation</th>
                <th style={{ width: "16%" }}>Authority</th>
                <th style={{ width: "22%" }}>Work Access</th>
                <th style={{ width: "9%" }}>Status</th>
                <th style={{ width: "12%", textAlign: "right" }}>Details</th>
              </tr>
            </thead>
            <tbody>
              {filteredAccounts.map((u) => {
                const attachedHelpers = attachedHelpersMap.get(u.id) || [];
                const isTechAdmin = u.authority === "SYSTEM_ADMIN" && !u.designationId && !u.customDesignation;
                const workSummary = getWorkAccessSummary(u);

                return (
                  <tr key={u.id}>
                    {/* 1. Employee */}
                    <td>
                      <div style={{ display: "flex", alignItems: "center", gap: "12px" }}>
                        <div className="rbac-avatar">
                          {getInitials(u.fullName || u.username)}
                        </div>
                        <div>
                          <div
                            style={{ fontWeight: 600, color: "#0f172a", cursor: "pointer", fontSize: "14px" }}
                            onClick={() => setSelectedDrawerOfficer(u)}
                          >
                            {u.fullName}
                          </div>
                          <div style={{ fontSize: "12px", color: "#64748b", fontFamily: "monospace" }}>
                            {u.username}
                          </div>
                          {attachedHelpers.length > 0 && (
                            <div style={{ fontSize: "11px", color: "#0369a1", marginTop: "2px", fontWeight: 500 }}>
                              {attachedHelpers.length} attached helper{attachedHelpers.length > 1 ? "s" : ""}
                            </div>
                          )}
                          {u.supervisingOfficerId && (
                            <div style={{ fontSize: "11px", color: "#854d0e", background: "#fef9c3", padding: "1px 6px", borderRadius: "4px", display: "inline-block", marginTop: "3px", fontWeight: 500 }}>
                              Attached Helper
                            </div>
                          )}
                        </div>
                      </div>
                    </td>

                    {/* 2. Designation */}
                    <td>
                      {isTechAdmin ? (
                        <div>
                          <span className="rbac-badge rbac-badge-purple">Technical Account</span>
                          <div className="subtext" style={{ fontSize: "11px", color: "#64748b", marginTop: "2px" }}>
                            No civil designation
                          </div>
                        </div>
                      ) : u.effectiveDesignation ? (
                        <span style={{ fontSize: "13.5px", color: "#1e293b", fontWeight: 500 }}>
                          {u.effectiveDesignation}
                        </span>
                      ) : (
                        <span className="subtext" style={{ fontSize: "12px", color: "#94a3b8" }}>
                          Unassigned
                        </span>
                      )}
                    </td>

                    {/* 3. Authority */}
                    <td>
                      <span className={`rbac-badge ${getAuthorityBadgeTone(u.authority)}`}>
                        {getAuthorityBadgeLabel(u.authority)}
                      </span>
                    </td>

                    {/* 4. Work Access */}
                    <td>
                      <span style={{ fontSize: "13px", color: "#334155", fontWeight: 500 }}>
                        {workSummary}
                      </span>
                    </td>

                    {/* 5. Status */}
                    <td>
                      <span className={`rbac-status-pill ${u.isActive ? "active" : "inactive"}`}>
                        <span className={`rbac-status-dot ${u.isActive ? "active" : "inactive"}`} />
                        {u.isActive ? "Active" : "Inactive"}
                      </span>
                    </td>

                    {/* 6. Details - Single Prominent Action */}
                    <td style={{ textAlign: "right" }}>
                      <button
                        className="rbac-btn-sm rbac-btn-outline rbac-btn-view-details"
                        onClick={() => setSelectedDrawerOfficer(u)}
                        title="View Details"
                      >
                        View Details
                      </button>
                    </td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        </div>
      )}

      {/* =========================================================================
          OFFICER DETAILS DRAWER
          ========================================================================= */}
      {selectedDrawerOfficer && (
        <div className="rbac-drawer-backdrop" onClick={() => setSelectedDrawerOfficer(null)}>
          <div className="rbac-drawer-shell" onClick={(e) => e.stopPropagation()}>
            <div className="rbac-drawer-header">
              <div style={{ display: "flex", alignItems: "center", gap: "12px" }}>
                <div className="rbac-avatar" style={{ width: "42px", height: "42px", fontSize: "15px" }}>
                  {getInitials(selectedDrawerOfficer.fullName || selectedDrawerOfficer.username)}
                </div>
                <div>
                  <h3>{selectedDrawerOfficer.fullName}</h3>
                  <p>{selectedDrawerOfficer.username}</p>
                </div>
              </div>
              <button
                type="button"
                className="rbac-modal-close-btn"
                onClick={() => setSelectedDrawerOfficer(null)}
                aria-label="Close"
              >
                &times;
              </button>
            </div>

            <div className="rbac-drawer-body">
              {/* Section 1: Employee */}
              <div className="rbac-detail-card">
                <div className="rbac-detail-card-title">1. Employee Identity</div>
                <div className="rbac-detail-grid">
                  <span className="rbac-detail-label">Full Name:</span>
                  <span className="rbac-detail-val">{selectedDrawerOfficer.fullName}</span>

                  <span className="rbac-detail-label">Username:</span>
                  <span className="rbac-detail-val" style={{ fontFamily: "monospace" }}>{selectedDrawerOfficer.username}</span>

                  <span className="rbac-detail-label">Designation:</span>
                  <span className="rbac-detail-val">
                    {selectedDrawerOfficer.authority === "SYSTEM_ADMIN" && !selectedDrawerOfficer.designationId && !selectedDrawerOfficer.customDesignation
                      ? "Technical Account (No civil designation)"
                      : selectedDrawerOfficer.effectiveDesignation || "Unassigned"}
                  </span>

                  <span className="rbac-detail-label">Status:</span>
                  <span className="rbac-detail-val" style={{ color: selectedDrawerOfficer.isActive ? "#166534" : "#94a3b8" }}>
                    {selectedDrawerOfficer.isActive ? "Active Account" : "Inactive / Disabled"}
                  </span>
                </div>
              </div>

              {/* Section 2: Authority */}
              <div className="rbac-detail-card">
                <div className="rbac-detail-card-title">2. Authority Level</div>
                <div style={{ display: "flex", alignItems: "center", gap: "10px" }}>
                  <span className={`rbac-badge ${getAuthorityBadgeTone(selectedDrawerOfficer.authority)}`} style={{ fontSize: "12px", padding: "4px 10px" }}>
                    {getAuthorityBadgeLabel(selectedDrawerOfficer.authority)}
                  </span>
                </div>
                <div style={{ fontSize: "12px", color: "#64748b", marginTop: "4px" }}>
                  {selectedDrawerOfficer.authority === "SYSTEM_ADMIN" && "Full administrative control over security, technical accounts, and system configuration."}
                  {selectedDrawerOfficer.authority === "OFFICE_ADMIN" && "Executive authority with full operational office management and supervisor delegation."}
                  {selectedDrawerOfficer.authority === "OFFICE_SUPERVISOR" && "Supervisor with operational access to all office modules and oversight over officers and helpers."}
                  {selectedDrawerOfficer.authority === "STANDARD_OFFICER" && "Official employee authorized for assigned work modules, designated desk, and land records."}
                  {selectedDrawerOfficer.authority === "HELPER" && "Data Entry Operator / Assistant working under the direction of an officer."}
                </div>
              </div>

              {/* Section 3: Work Access */}
              <div className="rbac-detail-card">
                <div className="rbac-detail-card-title">3. Work Access &amp; Permissions</div>
                <div className="rbac-detail-grid">
                  <span className="rbac-detail-label">Work Modules:</span>
                  <span className="rbac-detail-val">
                    {selectedDrawerOfficer.authority === "SYSTEM_ADMIN"
                      ? "Full System Access"
                      : selectedDrawerOfficer.authority === "OFFICE_ADMIN" || selectedDrawerOfficer.authority === "OFFICE_SUPERVISOR"
                      ? "Full Office Access (All Modules)"
                      : selectedDrawerOfficer.authority === "HELPER"
                      ? "Assistant bounded by Supervising Officer"
                      : selectedDrawerOfficer.modules.length > 0
                      ? selectedDrawerOfficer.modules.map((m) => ALL_MODULES.find((x) => x.code === m)?.label || m).join(", ")
                      : "None"}
                  </span>

                  <span className="rbac-detail-label">Inward Dak:</span>
                  <span className="rbac-detail-val">
                    {selectedDrawerOfficer.canRegisterInwardDak ? "Can Register Inward Dak Receipts" : "No Registration Rights"}
                  </span>

                  <span className="rbac-detail-label">Land Records:</span>
                  <span className="rbac-detail-val">
                    {selectedDrawerOfficer.landAccess === "ViewWrite" ? "View + Write" : selectedDrawerOfficer.landAccess === "ViewOnly" ? "View Only" : "None"}
                  </span>
                </div>
              </div>

              {/* Section 4: Desks */}
              <div className="rbac-detail-card">
                <div className="rbac-detail-card-title">4. Operational Seats &amp; Desks</div>
                {selectedDrawerOfficer.deskIds.length > 0 ? (
                  <div style={{ display: "flex", flexWrap: "wrap", gap: "6px" }}>
                    {selectedDrawerOfficer.deskIds.map((deskId, idx) => {
                      const desk = options?.desks.find((d) => d.id === deskId);
                      return (
                        <span key={deskId} className={`desk-chip ${idx === 0 ? "primary" : ""}`}>
                          {desk ? `${desk.name} (${desk.code})` : deskId} {idx === 0 ? "· Primary" : ""}
                        </span>
                      );
                    })}
                  </div>
                ) : (
                  <div style={{ fontSize: "13px", color: "#94a3b8" }}>
                    No dealing seat or desk assigned.
                  </div>
                )}
              </div>

              {/* Section 5: Staff Relationship */}
              <div className="rbac-detail-card">
                <div className="rbac-detail-card-title">5. Staff Relationship</div>
                {selectedDrawerOfficer.supervisingOfficerId ? (
                  <div className="rbac-detail-grid">
                    <span className="rbac-detail-label">Supervised By:</span>
                    <span className="rbac-detail-val">
                      {accounts.find((a) => a.id === selectedDrawerOfficer.supervisingOfficerId)?.fullName || "Supervising Officer"}
                    </span>
                  </div>
                ) : attachedHelpersMap.get(selectedDrawerOfficer.id)?.length ? (
                  <div>
                    <div style={{ fontSize: "12px", color: "#64748b", marginBottom: "6px" }}>
                      Attached Assistants ({attachedHelpersMap.get(selectedDrawerOfficer.id)!.length}):
                    </div>
                    <div style={{ display: "flex", flexDirection: "column", gap: "6px" }}>
                      {attachedHelpersMap.get(selectedDrawerOfficer.id)!.map((h) => (
                        <div key={h.id} style={{ display: "flex", justifyContent: "space-between", alignItems: "center", padding: "6px 10px", background: "#f8fafc", borderRadius: "6px", border: "1px solid #e2e8f0", fontSize: "12.5px" }}>
                          <span><strong>{h.fullName}</strong> ({h.username})</span>
                          <span style={{ color: "#64748b" }}>{h.effectiveDesignation || "DEO"}</span>
                        </div>
                      ))}
                    </div>
                  </div>
                ) : (
                  <div style={{ fontSize: "13px", color: "#94a3b8" }}>
                    Independent official post. No attached assistants.
                  </div>
                )}
              </div>

              {/* Section 6: Action Buttons */}
              <div style={{ marginTop: "4px", display: "flex", flexDirection: "column", gap: "10px" }}>
                <div style={{ fontSize: "12px", fontWeight: 700, textTransform: "uppercase", color: "#475569", letterSpacing: "0.04em" }}>
                  Account Actions
                </div>
                <div style={{ display: "flex", flexWrap: "wrap", gap: "10px" }}>
                  <button
                    type="button"
                    className="rbac-btn-sm rbac-btn-primary"
                    onClick={() => {
                      const target = selectedDrawerOfficer;
                      setSelectedDrawerOfficer(null);
                      void openEditDrawer(target);
                    }}
                  >
                    <Pencil size={14} /> Edit Account
                  </button>

                  <button
                    type="button"
                    className="rbac-btn-sm rbac-btn-outline"
                    onClick={() => handleResetCredentialClick(selectedDrawerOfficer)}
                  >
                    <KeyRound size={14} /> Reset Password
                  </button>

                  <button
                    type="button"
                    className={`rbac-btn-sm ${selectedDrawerOfficer.isActive ? "rbac-btn-danger" : "rbac-btn-outline"}`}
                    onClick={() => handleToggleStatusClick(selectedDrawerOfficer)}
                  >
                    {selectedDrawerOfficer.isActive ? (
                      <><UserX size={14} /> Disable Account</>
                    ) : (
                      <><UserCheck size={14} /> Enable Account</>
                    )}
                  </button>

                  {options?.authority === "SYSTEM_ADMIN" && (
                    <button
                      type="button"
                      className="rbac-btn-sm rbac-btn-outline"
                      onClick={() => setInspectingOfficer(selectedDrawerOfficer)}
                    >
                      <ExternalLink size={14} /> Access Inspector
                    </button>
                  )}
                </div>
              </div>
            </div>

            <div className="rbac-drawer-footer">
              <button
                type="button"
                className="rbac-btn-outline"
                onClick={() => setSelectedDrawerOfficer(null)}
              >
                Close
              </button>
            </div>
          </div>
        </div>
      )}

      {/* =========================================================================
          CREATE OFFICER / STAFF MODAL (1366x768 Optimized)
          ========================================================================= */}
      {showCreateModal && (
        <div className="rbac-modal-backdrop" onClick={() => setShowCreateModal(false)}>
          <div className="rbac-modal-shell" onClick={(e) => e.stopPropagation()}>
            <div className="rbac-modal-header">
              <div>
                <h2>Create Officer / Staff</h2>
                <p>Create an official account and choose the work access required for this employee.</p>
              </div>
              <button
                type="button"
                className="rbac-modal-close-btn"
                onClick={() => setShowCreateModal(false)}
                aria-label="Close"
              >
                &times;
              </button>
            </div>

            <form onSubmit={handleCreateOfficerSubmit} style={{ display: "flex", flexDirection: "column", flex: 1, minHeight: 0 }}>
              <div className="rbac-modal-body">
                {createFormError && (
                  <div style={{ padding: "10px 14px", borderRadius: "8px", background: "#fef2f2", color: "#991b1b", fontSize: "13px", border: "1px solid #fecaca" }}>
                    ⚠️ {createFormError}
                  </div>
                )}

                {/* SECTION 1 — Employee */}
                <div className="rbac-card-section">
                  <div className="rbac-section-title">Section 1 — Employee Identity</div>
                  <div className="rbac-grid-2col">
                    {/* Left: Username & Full Name */}
                    <div style={{ display: "flex", flexDirection: "column", gap: "12px" }}>
                      <div className="rbac-form-group">
                        <label>Official Username *</label>
                        <input
                          type="text"
                          required
                          placeholder="e.g. rajesh.sharma"
                          value={newUsername}
                          onChange={(e) => setNewUsername(e.target.value)}
                          autoFocus
                          className="rbac-input"
                        />
                      </div>
                      <div className="rbac-form-group">
                        <label>Full Name *</label>
                        <input
                          type="text"
                          required
                          placeholder="e.g. Rajesh Sharma"
                          value={newFullName}
                          onChange={(e) => setNewFullName(e.target.value)}
                          className="rbac-input"
                        />
                      </div>
                    </div>

                    {/* Right: Civil Designation with Custom Mode */}
                    <div style={{ display: "flex", flexDirection: "column", gap: "6px" }}>
                      <DesignationCombobox
                        value={newDesignationId}
                        onChange={(val) => {
                          setNewDesignationId(val);
                          if (val !== "__OTHER__") {
                            setIsCustomDesignationMode(false);
                          }
                        }}
                        options={options?.designations || []}
                        isCustomMode={isCustomDesignationMode}
                        onToggleCustomMode={(custom) => setIsCustomDesignationMode(custom)}
                        customValue={newCustomDesignation}
                        onCustomChange={(val) => setNewCustomDesignation(val)}
                        placeholder="-- Select Civil Designation --"
                      />

                      {isSelectedDesignationAdm && options?.authority === "SYSTEM_ADMIN" && (
                        <div className="rbac-adm-notice">
                          <Shield size={20} style={{ color: "#4f46e5", flexShrink: 0, marginTop: "2px" }} />
                          <div>
                            <strong>Office Administrator — Full Office Access</strong>
                            <p>Assigning ADM configures this account as the Office Administrator with full operational office authority.</p>
                          </div>
                        </div>
                      )}
                    </div>
                  </div>
                </div>

                {/* SECTION 2 — Authority */}
                {options?.canAssignOfficeSupervisor && !isSelectedDesignationAdm && (
                  <div
                    className={`rbac-card-section ${newIsSupervisor ? "highlight" : ""}`}
                    style={{
                      cursor: "pointer",
                      borderColor: newIsSupervisor ? "#2563eb" : "#e2e8f0",
                      background: newIsSupervisor ? "#eff6ff" : "#ffffff",
                      transition: "all 180ms ease",
                    }}
                    onClick={() => setNewIsSupervisor(!newIsSupervisor)}
                  >
                    <div style={{ display: "flex", alignItems: "flex-start", gap: "12px" }}>
                      <div
                        style={{
                          width: "36px",
                          height: "36px",
                          borderRadius: "8px",
                          background: newIsSupervisor ? "#dbeafe" : "#f1f5f9",
                          color: newIsSupervisor ? "#1d4ed8" : "#475569",
                          display: "flex",
                          alignItems: "center",
                          justifyContent: "center",
                          flexShrink: 0,
                        }}
                      >
                        <Shield size={20} />
                      </div>
                      <div style={{ flex: 1 }}>
                        <div style={{ display: "flex", alignItems: "center", justifyContent: "space-between" }}>
                          <strong style={{ fontSize: "14px", color: "#0f172a" }}>Office Supervisor</strong>
                          <input
                            type="checkbox"
                            checked={newIsSupervisor}
                            onChange={(e) => {
                              e.stopPropagation();
                              setNewIsSupervisor(e.target.checked);
                            }}
                            style={{ width: "16px", height: "16px", accentColor: "#2563eb", cursor: "pointer" }}
                          />
                        </div>
                        <div style={{ fontSize: "12.5px", color: "#64748b", marginTop: "2px" }}>
                          Supervisor receives full operational access and can oversee case matters, staff and helpers.
                        </div>
                        {newIsSupervisor && (
                          <div style={{ marginTop: "8px", padding: "8px 12px", borderRadius: "8px", background: "#f0fdf4", border: "1px solid #bbf7d0", color: "#166534", fontSize: "12.5px" }}>
                            ✓ <strong>Full operational office access will be assigned automatically.</strong> Module, Land, and Inward Dak permissions are pre-configured.
                          </div>
                        )}
                      </div>
                    </div>
                  </div>
                )}

                {/* Ordinary Officer Configurations (Sections 3 & 4) */}
                {!newIsSupervisor && !isSelectedDesignationAdm && (
                  <>
                    {/* SECTION 3 — Work Access */}
                    <div className="rbac-card-section">
                      <div className="rbac-section-title">Section 3 — Work Access Modules</div>
                      <div className="rbac-module-cards-grid">
                        {ALL_MODULES.map((mod) => {
                          const isSelected = newModules.includes(mod.code);
                          const IconComponent =
                            mod.code === "DakMatters" ? FileText :
                            mod.code === "Court" ? Scale :
                            mod.code === "Rti" ? Search :
                            mod.code === "Accounts" ? CreditCard : Archive;

                          return (
                            <div
                              key={mod.code}
                              className={`rbac-module-card ${isSelected ? "selected" : ""}`}
                              onClick={() => toggleModuleSelection(mod.code, newModules, setNewModules)}
                              role="checkbox"
                              aria-checked={isSelected}
                              tabIndex={0}
                              onKeyDown={(e) => {
                                if (e.key === " " || e.key === "Enter") {
                                  e.preventDefault();
                                  toggleModuleSelection(mod.code, newModules, setNewModules);
                                }
                              }}
                            >
                              <div className="rbac-module-card-header">
                                <div className="rbac-module-icon-wrap">
                                  <IconComponent size={20} />
                                </div>
                                {isSelected && (
                                  <div className="rbac-module-check">
                                    <Check size={13} strokeWidth={3} />
                                  </div>
                                )}
                              </div>
                              <div className="rbac-module-card-text">
                                <span className="rbac-module-card-title">{mod.label}</span>
                                <span className="rbac-module-card-desc">{mod.description}</span>
                              </div>
                              <input
                                type="checkbox"
                                checked={isSelected}
                                onChange={() => {}}
                                style={{ display: "none" }}
                              />
                            </div>
                          );
                        })}
                      </div>
                    </div>

                    {/* SECTION 4 — Additional Access */}
                    <div className="rbac-card-section">
                      <div className="rbac-section-title">Section 4 — Additional Work Capabilities</div>
                      <div className="rbac-grid-2col">
                        {/* Left: Inward Dak */}
                        <div
                          style={{
                            display: "flex",
                            alignItems: "flex-start",
                            gap: "10px",
                            padding: "10px 14px",
                            border: "1.5px solid",
                            borderColor: newCanRegisterInwardDak ? "#0284c7" : "#e2e8f0",
                            borderRadius: "10px",
                            background: newCanRegisterInwardDak ? "#f0f9ff" : "#ffffff",
                            cursor: "pointer",
                            transition: "all 180ms ease",
                          }}
                          onClick={() => setNewCanRegisterInwardDak(!newCanRegisterInwardDak)}
                        >
                          <input
                            type="checkbox"
                            checked={newCanRegisterInwardDak}
                            onChange={(e) => {
                              e.stopPropagation();
                              setNewCanRegisterInwardDak(e.target.checked);
                            }}
                            style={{ marginTop: "2px", width: "16px", height: "16px", accentColor: "#0284c7", cursor: "pointer" }}
                          />
                          <div>
                            <strong style={{ fontSize: "13px", color: "#0f172a" }}>Can Register Inward Dak</strong>
                            <div style={{ fontSize: "11.5px", color: "#64748b", marginTop: "2px" }}>
                              Register new incoming Dak receipts and assign first marking.
                            </div>
                          </div>
                        </div>

                        {/* Right: Land Records */}
                        <div style={{ display: "flex", flexDirection: "column", gap: "6px" }}>
                          <div style={{ fontSize: "12.5px", fontWeight: 600, color: "#334155" }}>
                            Land Records Access
                          </div>
                          <div className="rbac-segmented-control" role="radiogroup" aria-label="Land Records Access">
                            <button
                              type="button"
                              className={`rbac-segmented-btn ${newLandAccess === "None" ? "active" : ""}`}
                              onClick={() => setNewLandAccess("None")}
                            >
                              <input type="radio" name="createLandAccessRadio" value="None" checked={newLandAccess === "None"} onChange={() => {}} style={{ display: "none" }} />
                              None
                            </button>
                            <button
                              type="button"
                              className={`rbac-segmented-btn ${newLandAccess === "ViewOnly" ? "active" : ""}`}
                              onClick={() => setNewLandAccess("ViewOnly")}
                            >
                              <input type="radio" name="createLandAccessRadio" value="ViewOnly" checked={newLandAccess === "ViewOnly"} onChange={() => {}} style={{ display: "none" }} />
                              View Only
                            </button>
                            <button
                              type="button"
                              className={`rbac-segmented-btn ${newLandAccess === "ViewWrite" ? "active" : ""}`}
                              onClick={() => setNewLandAccess("ViewWrite")}
                            >
                              <input type="radio" name="createLandAccessRadio" value="ViewWrite" checked={newLandAccess === "ViewWrite"} onChange={() => {}} style={{ display: "none" }} />
                              View + Write
                            </button>
                          </div>
                        </div>
                      </div>
                    </div>
                  </>
                )}

                {/* SECTION 5 — Desk / Seat */}
                <div className="rbac-card-section highlight">
                  <div className="rbac-section-title">Section 5 — Operational Seat &amp; Desks</div>
                  <div className="rbac-grid-2col">
                    <div className="rbac-form-group">
                      <label>Primary Dealing Seat</label>
                      <select
                        value={newPrimaryDeskId}
                        onChange={(e) => {
                          const val = e.target.value;
                          setNewPrimaryDeskId(val);
                          if (val && newAdditionalDeskIds.includes(val)) {
                            setNewAdditionalDeskIds(newAdditionalDeskIds.filter((id) => id !== val));
                          }
                        }}
                        className="rbac-input"
                      >
                        <option value="">-- No Specific Seat Assigned --</option>
                        {options?.desks.map((d) => (
                          <option key={d.id} value={d.id}>
                            {d.name} ({d.code})
                          </option>
                        ))}
                      </select>
                    </div>

                    <div className="rbac-form-group">
                      <label>Additional Active Desks</label>
                      <select
                        value=""
                        onChange={(e) => {
                          const val = e.target.value;
                          if (val && !newAdditionalDeskIds.includes(val) && val !== newPrimaryDeskId) {
                            setNewAdditionalDeskIds([...newAdditionalDeskIds, val]);
                          }
                        }}
                        className="rbac-input"
                      >
                        <option value="">+ Add another desk…</option>
                        {options?.desks
                          ?.filter((d) => d.id !== newPrimaryDeskId && !newAdditionalDeskIds.includes(d.id))
                          .map((d) => (
                            <option key={d.id} value={d.id}>
                              {d.name} ({d.code})
                            </option>
                          ))}
                      </select>
                    </div>
                  </div>

                  {newAdditionalDeskIds.length > 0 && (
                    <div style={{ display: "flex", flexWrap: "wrap", gap: "8px", marginTop: "4px" }}>
                      {newAdditionalDeskIds.map((deskId) => {
                        const desk = options?.desks.find((d) => d.id === deskId);
                        return (
                          <span
                            key={deskId}
                            className="rbac-badge rbac-badge-outline"
                            style={{ display: "inline-flex", alignItems: "center", gap: "8px", fontSize: "12px", padding: "4px 10px" }}
                          >
                            <span>{desk?.name || deskId}</span>
                            <button
                              type="button"
                              onClick={() => setNewAdditionalDeskIds(newAdditionalDeskIds.filter((id) => id !== deskId))}
                              style={{ background: "none", border: "none", cursor: "pointer", color: "#64748b", fontWeight: "bold", padding: 0, display: "flex", alignItems: "center" }}
                              title="Remove desk"
                            >
                              <X size={14} />
                            </button>
                          </span>
                        );
                      })}
                    </div>
                  )}
                </div>
              </div>

              {/* Sticky Footer */}
              <div className="rbac-modal-footer">
                <button
                  type="button"
                  className="rbac-btn-outline"
                  onClick={() => setShowCreateModal(false)}
                  disabled={actionLoading}
                >
                  Cancel
                </button>
                <button
                  type="submit"
                  className="rbac-btn-primary"
                  disabled={actionLoading}
                >
                  Review &amp; Create
                </button>
              </div>
            </form>
          </div>
        </div>
      )}

      {/* =========================================================================
          CREATE TECHNICAL SYSTEM ADMINISTRATOR MODAL
          ========================================================================= */}
      {showTechAdminModal && (
        <div className="rbac-modal-backdrop" onClick={() => setShowTechAdminModal(false)}>
          <div className="rbac-modal-shell" style={{ maxWidth: "540px" }} onClick={(e) => e.stopPropagation()}>
            <div className="rbac-modal-header">
              <div>
                <h2>Create System Administrator</h2>
                <p>Platform administrative identity with technical security authority.</p>
              </div>
              <button
                type="button"
                className="rbac-modal-close-btn"
                onClick={() => setShowTechAdminModal(false)}
                aria-label="Close"
              >
                &times;
              </button>
            </div>

            <form onSubmit={handleCreateTechAdminSubmit} style={{ display: "flex", flexDirection: "column", flex: 1, minHeight: 0 }}>
              <div className="rbac-modal-body">
                {techFormError && (
                  <div style={{ padding: "10px 14px", borderRadius: "8px", background: "#fef2f2", color: "#991b1b", fontSize: "13px", border: "1px solid #fecaca" }}>
                    ⚠️ {techFormError}
                  </div>
                )}

                <div className="rbac-form-group">
                  <label>Official Username *</label>
                  <input
                    type="text"
                    required
                    placeholder="e.g. sysadmin.main"
                    value={techUsername}
                    onChange={(e) => setTechUsername(e.target.value)}
                    autoFocus
                    className="rbac-input"
                  />
                </div>

                <div className="rbac-form-group">
                  <label>Full Name *</label>
                  <input
                    type="text"
                    required
                    placeholder="e.g. Platform Administrator"
                    value={techFullName}
                    onChange={(e) => setTechFullName(e.target.value)}
                    className="rbac-input"
                  />
                </div>

                <div className="rbac-form-group">
                  <label>Civil Designation (Optional)</label>
                  <select
                    value={techDesignationId}
                    onChange={(e) => setTechDesignationId(e.target.value)}
                    className="rbac-input"
                  >
                    <option value="">-- No Official Designation --</option>
                    {options?.designations.map((d) => (
                      <option key={d.id} value={d.id}>
                        {d.name}
                      </option>
                    ))}
                  </select>
                </div>

                <div style={{ padding: "10px 14px", borderRadius: "8px", background: "#f8fafc", border: "1px solid #cbd5e1", fontSize: "12.5px", color: "#475569" }}>
                  ℹ️ System Administrator is a technical security role and does not require a civil designation.
                </div>
              </div>

              <div className="rbac-modal-footer">
                <button
                  type="button"
                  className="rbac-btn-outline"
                  onClick={() => setShowTechAdminModal(false)}
                  disabled={actionLoading}
                >
                  Cancel
                </button>
                <button
                  type="submit"
                  className="rbac-btn-primary"
                  disabled={actionLoading}
                >
                  Review &amp; Create
                </button>
              </div>
            </form>
          </div>
        </div>
      )}

      {/* =========================================================================
          EDIT OFFICER MODAL (1366x768 Optimized)
          ========================================================================= */}
      {editingOfficer && (
        <div className="rbac-modal-backdrop" onClick={() => setEditingOfficer(null)}>
          <div className="rbac-modal-shell" onClick={(e) => e.stopPropagation()}>
            <div className="rbac-modal-header">
              <div>
                <h2>Edit Account — {editingOfficer.username}</h2>
                <p>Update designation, operational access, seat, or attached assistants.</p>
              </div>
              <button
                type="button"
                className="rbac-modal-close-btn"
                onClick={() => setEditingOfficer(null)}
                aria-label="Close"
              >
                &times;
              </button>
            </div>

            <form onSubmit={handleEditOfficerReview} style={{ display: "flex", flexDirection: "column", flex: 1, minHeight: 0 }}>
              <div className="rbac-modal-body">
                {editDrawerLoading && (
                  <div style={{ padding: "10px 14px", borderRadius: "8px", background: "#f8fafc", color: "#475569", fontSize: "13px" }}>
                    Loading latest account details…
                  </div>
                )}
                {editDrawerError && (
                  <div style={{ padding: "10px 14px", borderRadius: "8px", background: "#fef2f2", color: "#991b1b", fontSize: "13px", border: "1px solid #fecaca" }}>
                    ⚠️ {editDrawerError}
                  </div>
                )}

                {/* Section 1: Employee */}
                <div className="rbac-card-section">
                  <div className="rbac-section-title">Section 1 — Employee Identity</div>
                  <div className="rbac-grid-2col">
                    <div className="rbac-form-group">
                      <label>Full Name *</label>
                      <input
                        type="text"
                        required
                        value={editFullName}
                        onChange={(e) => setEditFullName(e.target.value)}
                        className="rbac-input"
                      />
                    </div>

                    <div style={{ display: "flex", flexDirection: "column", gap: "6px" }}>
                      <DesignationCombobox
                        value={editDesignationId}
                        onChange={(val) => {
                          setEditDesignationId(val);
                          if (val !== "__OTHER__") {
                            setIsEditCustomMode(false);
                          }
                        }}
                        options={options?.designations || []}
                        isCustomMode={isEditCustomMode}
                        onToggleCustomMode={(custom) => setIsEditCustomMode(custom)}
                        customValue={editCustomDesignation}
                        onCustomChange={(val) => setEditCustomDesignation(val)}
                        placeholder="-- No Official Designation --"
                      />

                      {isEditDesignationAdm && editingOfficer.authority !== "OFFICE_ADMIN" && editingOfficer.authority !== "SYSTEM_ADMIN" && (
                        <div style={{ fontSize: "11.5px", color: "#64748b", background: "#f8fafc", padding: "8px 12px", borderRadius: "8px", border: "1px solid #e2e8f0" }}>
                          ℹ️ Note: Changing an existing account's designation to ADM does not automatically promote it to Office Admin authority.
                        </div>
                      )}
                    </div>
                  </div>
                </div>

                {/* Section 2: Supervisor Toggle */}
                {options?.canAssignOfficeSupervisor && editingOfficer.authority !== "OFFICE_ADMIN" && editingOfficer.authority !== "SYSTEM_ADMIN" && (
                  <div
                    className={`rbac-card-section ${editIsSupervisor ? "highlight" : ""}`}
                    style={{
                      cursor: "pointer",
                      borderColor: editIsSupervisor ? "#2563eb" : "#e2e8f0",
                      background: editIsSupervisor ? "#eff6ff" : "#ffffff",
                      transition: "all 180ms ease",
                    }}
                    onClick={() => setEditIsSupervisor(!editIsSupervisor)}
                  >
                    <div style={{ display: "flex", alignItems: "flex-start", gap: "12px" }}>
                      <div
                        style={{
                          width: "36px",
                          height: "36px",
                          borderRadius: "8px",
                          background: editIsSupervisor ? "#dbeafe" : "#f1f5f9",
                          color: editIsSupervisor ? "#1d4ed8" : "#475569",
                          display: "flex",
                          alignItems: "center",
                          justifyContent: "center",
                          flexShrink: 0,
                        }}
                      >
                        <Shield size={20} />
                      </div>
                      <div style={{ flex: 1 }}>
                        <div style={{ display: "flex", alignItems: "center", justifyContent: "space-between" }}>
                          <strong style={{ fontSize: "14px", color: "#0f172a" }}>Office Supervisor</strong>
                          <input
                            type="checkbox"
                            checked={editIsSupervisor}
                            onChange={(e) => {
                              e.stopPropagation();
                              setEditIsSupervisor(e.target.checked);
                            }}
                            style={{ width: "16px", height: "16px", accentColor: "#2563eb", cursor: "pointer" }}
                          />
                        </div>
                        <div style={{ fontSize: "12.5px", color: "#64748b", marginTop: "2px" }}>
                          Supervisor receives full operational access and can oversee case matters, staff and helpers.
                        </div>
                        {editIsSupervisor && (
                          <div style={{ marginTop: "8px", padding: "8px 12px", borderRadius: "8px", background: "#f0fdf4", border: "1px solid #bbf7d0", color: "#166534", fontSize: "12.5px" }}>
                            ✓ <strong>Full operational office access will be assigned automatically.</strong> Module, Land, and Inward Dak permissions are pre-configured.
                          </div>
                        )}
                      </div>
                    </div>
                  </div>
                )}

                {/* Sections 3 & 4: Work Access & Additional when not supervisor or admin */}
                {!editIsSupervisor && editingOfficer.authority !== "OFFICE_ADMIN" && editingOfficer.authority !== "SYSTEM_ADMIN" && (
                  <>
                    <div className="rbac-card-section">
                      <div className="rbac-section-title">Section 3 — Work Access Modules</div>
                      <div className="rbac-module-cards-grid">
                        {ALL_MODULES.map((mod) => {
                          const isSelected = editModules.includes(mod.code);
                          const IconComponent =
                            mod.code === "DakMatters" ? FileText :
                            mod.code === "Court" ? Scale :
                            mod.code === "Rti" ? Search :
                            mod.code === "Accounts" ? CreditCard : Archive;

                          return (
                            <div
                              key={mod.code}
                              className={`rbac-module-card ${isSelected ? "selected" : ""}`}
                              onClick={() => toggleModuleSelection(mod.code, editModules, setEditModules)}
                              role="checkbox"
                              aria-checked={isSelected}
                              tabIndex={0}
                              onKeyDown={(e) => {
                                if (e.key === " " || e.key === "Enter") {
                                  e.preventDefault();
                                  toggleModuleSelection(mod.code, editModules, setEditModules);
                                }
                              }}
                            >
                              <div className="rbac-module-card-header">
                                <div className="rbac-module-icon-wrap">
                                  <IconComponent size={20} />
                                </div>
                                {isSelected && (
                                  <div className="rbac-module-check">
                                    <Check size={13} strokeWidth={3} />
                                  </div>
                                )}
                              </div>
                              <div className="rbac-module-card-text">
                                <span className="rbac-module-card-title">{mod.label}</span>
                                <span className="rbac-module-card-desc">{mod.description}</span>
                              </div>
                              <input
                                type="checkbox"
                                checked={isSelected}
                                onChange={() => {}}
                                style={{ display: "none" }}
                              />
                            </div>
                          );
                        })}
                      </div>
                    </div>

                    <div className="rbac-card-section">
                      <div className="rbac-section-title">Section 4 — Additional Capabilities</div>
                      <div className="rbac-grid-2col">
                        <div
                          style={{
                            display: "flex",
                            alignItems: "flex-start",
                            gap: "10px",
                            padding: "10px 14px",
                            border: "1.5px solid",
                            borderColor: editCanRegisterInwardDak ? "#0284c7" : "#e2e8f0",
                            borderRadius: "10px",
                            background: editCanRegisterInwardDak ? "#f0f9ff" : "#ffffff",
                            cursor: "pointer",
                            transition: "all 180ms ease",
                          }}
                          onClick={() => setEditCanRegisterInwardDak(!editCanRegisterInwardDak)}
                        >
                          <input
                            type="checkbox"
                            checked={editCanRegisterInwardDak}
                            onChange={(e) => {
                              e.stopPropagation();
                              setEditCanRegisterInwardDak(e.target.checked);
                            }}
                            style={{ marginTop: "2px", width: "16px", height: "16px", accentColor: "#0284c7", cursor: "pointer" }}
                          />
                          <div>
                            <strong style={{ fontSize: "13px", color: "#0f172a" }}>Can Register Inward Dak</strong>
                            <div style={{ fontSize: "11.5px", color: "#64748b", marginTop: "2px" }}>
                              Register new incoming Dak receipts and assign first marking.
                            </div>
                          </div>
                        </div>

                        <div style={{ display: "flex", flexDirection: "column", gap: "6px" }}>
                          <div style={{ fontSize: "12.5px", fontWeight: 600, color: "#334155" }}>
                            Land Records Access
                          </div>
                          <div className="rbac-segmented-control" role="radiogroup" aria-label="Land Records Access">
                            <button
                              type="button"
                              className={`rbac-segmented-btn ${editLandAccess === "None" ? "active" : ""}`}
                              onClick={() => setEditLandAccess("None")}
                            >
                              <input type="radio" name="editLandRadio" value="None" checked={editLandAccess === "None"} onChange={() => {}} style={{ display: "none" }} />
                              None
                            </button>
                            <button
                              type="button"
                              className={`rbac-segmented-btn ${editLandAccess === "ViewOnly" ? "active" : ""}`}
                              onClick={() => setEditLandAccess("ViewOnly")}
                            >
                              <input type="radio" name="editLandRadio" value="ViewOnly" checked={editLandAccess === "ViewOnly"} onChange={() => {}} style={{ display: "none" }} />
                              View Only
                            </button>
                            <button
                              type="button"
                              className={`rbac-segmented-btn ${editLandAccess === "ViewWrite" ? "active" : ""}`}
                              onClick={() => setEditLandAccess("ViewWrite")}
                            >
                              <input type="radio" name="editLandRadio" value="ViewWrite" checked={editLandAccess === "ViewWrite"} onChange={() => {}} style={{ display: "none" }} />
                              View + Write
                            </button>
                          </div>
                        </div>
                      </div>
                    </div>
                  </>
                )}

                {/* Section 5: Desks */}
                <div className="rbac-card-section highlight">
                  <div className="rbac-section-title">Section 5 — Operational Seat &amp; Desks</div>
                  <div className="rbac-grid-2col">
                    <div className="rbac-form-group">
                      <label>Primary Dealing Seat</label>
                      <select
                        value={editPrimaryDeskId}
                        onChange={(e) => {
                          const val = e.target.value;
                          setEditPrimaryDeskId(val);
                          if (val && editAdditionalDeskIds.includes(val)) {
                            setEditAdditionalDeskIds(editAdditionalDeskIds.filter((id) => id !== val));
                          }
                        }}
                        className="rbac-input"
                      >
                        <option value="">-- No Specific Seat Assigned --</option>
                        {options?.desks.map((d) => (
                          <option key={d.id} value={d.id}>
                            {d.name} ({d.code})
                          </option>
                        ))}
                      </select>
                    </div>

                    <div className="rbac-form-group">
                      <label>Additional Active Desks</label>
                      <select
                        value=""
                        onChange={(e) => {
                          const val = e.target.value;
                          if (val && !editAdditionalDeskIds.includes(val) && val !== editPrimaryDeskId) {
                            setEditAdditionalDeskIds([...editAdditionalDeskIds, val]);
                          }
                        }}
                        className="rbac-input"
                      >
                        <option value="">+ Add another desk…</option>
                        {options?.desks
                          ?.filter((d) => d.id !== editPrimaryDeskId && !editAdditionalDeskIds.includes(d.id))
                          .map((d) => (
                            <option key={d.id} value={d.id}>
                              {d.name} ({d.code})
                            </option>
                          ))}
                      </select>
                    </div>
                  </div>

                  {editAdditionalDeskIds.length > 0 && (
                    <div style={{ display: "flex", flexWrap: "wrap", gap: "8px", marginTop: "4px" }}>
                      {editAdditionalDeskIds.map((deskId) => {
                        const desk = options?.desks.find((d) => d.id === deskId);
                        return (
                          <span
                            key={deskId}
                            className="rbac-badge rbac-badge-outline"
                            style={{ display: "inline-flex", alignItems: "center", gap: "8px", fontSize: "12px", padding: "4px 10px" }}
                          >
                            <span>{desk?.name || deskId}</span>
                            <button
                              type="button"
                              onClick={() => setEditAdditionalDeskIds(editAdditionalDeskIds.filter((id) => id !== deskId))}
                              style={{ background: "none", border: "none", cursor: "pointer", color: "#64748b", fontWeight: "bold", padding: 0, display: "flex", alignItems: "center" }}
                              title="Remove desk"
                            >
                              <X size={14} />
                            </button>
                          </span>
                        );
                      })}
                    </div>
                  )}
                </div>

                {/* Section 6: Attached Helpers Sub-Section */}
                <div className="rbac-card-section">
                  <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center" }}>
                    <div className="rbac-section-title">Section 6 — Attached Assistants</div>
                    <button
                      type="button"
                      className="rbac-btn-sm rbac-btn-outline"
                      onClick={() => {
                        setHelperUsername("");
                        setHelperFullName("");
                        setHelperDesignationChoice("DEO");
                        setHelperCustomDesignation("");
                        setHelperDeskId(editingOfficer.deskIds[0] || "");
                        setHelperAccess("ReadOnly");
                        setHelperFormError(null);
                        setShowAddHelperForOfficer(true);
                      }}
                    >
                      <Plus size={14} /> Add Assistant
                    </button>
                  </div>

                  {attachedHelpersMap.get(editingOfficer.id)?.length ? (
                    <div style={{ display: "flex", flexDirection: "column", gap: "8px", marginTop: "4px" }}>
                      {attachedHelpersMap.get(editingOfficer.id)!.map((h) => (
                        <div key={h.id} style={{ display: "flex", justifyContent: "space-between", alignItems: "center", padding: "10px 14px", background: "#f8fafc", borderRadius: "8px", border: "1px solid #e2e8f0", fontSize: "13px" }}>
                          <div>
                            <strong>{h.fullName}</strong> <span style={{ color: "#64748b", fontFamily: "monospace", fontSize: "12px" }}>({h.username})</span>
                            <div style={{ fontSize: "11.5px", color: "#64748b", marginTop: "2px" }}>
                              {h.effectiveDesignation || "DEO"} · {h.isActive ? "Active" : "Inactive"} · {h.helperPermissionCodes?.some((c) => !c.endsWith(".View")) ? "Read + Write" : "Read Only"}
                            </div>
                          </div>
                          <div style={{ display: "flex", gap: "6px" }}>
                            <button
                              type="button"
                              className="rbac-btn-sm rbac-btn-outline"
                              onClick={() => openEditHelperAccess(h)}
                            >
                              Edit Access
                            </button>
                            <button type="button" className="rbac-btn-sm rbac-btn-outline" onClick={() => handleResetCredentialClick(h)}>
                              Reset
                            </button>
                            <button type="button" className={`rbac-btn-sm ${h.isActive ? "rbac-btn-danger" : "rbac-btn-outline"}`} onClick={() => handleToggleStatusClick(h)}>
                              {h.isActive ? "Deactivate" : "Activate"}
                            </button>
                          </div>
                        </div>
                      ))}
                    </div>
                  ) : (
                    <div style={{ fontSize: "12.5px", color: "#94a3b8", fontStyle: "italic" }}>
                      No assistants currently attached to this officer.
                    </div>
                  )}

                  {/* Edit Attached Helper Sub-Form */}
                  {editingAttachedHelper && (
                    <div style={{ marginTop: "10px", padding: "14px", background: "#eff6ff", borderRadius: "10px", border: "1.5px solid #bfdbfe" }}>
                      <div style={{ fontWeight: 700, fontSize: "13.5px", marginBottom: "8px", color: "#1e40af" }}>
                        Edit Assistant Access: {editingAttachedHelper.fullName} ({editingAttachedHelper.username})
                      </div>
                      <div style={{ display: "flex", flexDirection: "column", gap: "10px" }}>
                        <div>
                          <label style={{ fontSize: "12px", fontWeight: 600, color: "#334155", display: "block", marginBottom: "4px" }}>
                            Access Level
                          </label>
                          <div className="rbac-segmented-control" role="radiogroup">
                            <button
                              type="button"
                              className={`rbac-segmented-btn ${editingHelperAccess === "None" ? "active" : ""}`}
                              onClick={() => setEditingHelperAccess("None")}
                            >
                              None
                            </button>
                            <button
                              type="button"
                              className={`rbac-segmented-btn ${editingHelperAccess === "ReadOnly" ? "active" : ""}`}
                              onClick={() => setEditingHelperAccess("ReadOnly")}
                            >
                              Read Only
                            </button>
                            <button
                              type="button"
                              className={`rbac-segmented-btn ${editingHelperAccess === "ReadWrite" ? "active" : ""}`}
                              onClick={() => setEditingHelperAccess("ReadWrite")}
                            >
                              Read + Write
                            </button>
                          </div>
                        </div>

                        <div className="rbac-form-group">
                          <label>Desk / Seat</label>
                          <select
                            value={editingHelperDeskId}
                            onChange={(e) => setEditingHelperDeskId(e.target.value)}
                            className="rbac-input"
                          >
                            <option value="">-- No Specific Seat --</option>
                            {options?.desks.map((d) => (
                              <option key={d.id} value={d.id}>
                                {d.name} ({d.code})
                              </option>
                            ))}
                          </select>
                        </div>

                        <div style={{ display: "flex", justifyContent: "flex-end", gap: "8px", marginTop: "4px" }}>
                          <button
                            type="button"
                            className="rbac-btn-sm rbac-btn-outline"
                            onClick={() => setEditingAttachedHelper(null)}
                          >
                            Cancel
                          </button>
                          <button
                            type="button"
                            className="rbac-btn-sm rbac-btn-primary"
                            onClick={handleSaveAttachedHelperAccess}
                          >
                            Save Assistant Access
                          </button>
                        </div>
                      </div>
                    </div>
                  )}

                  {/* Add Helper Sub-Form */}
                  {showAddHelperForOfficer && (
                    <div style={{ marginTop: "10px", padding: "14px", background: "#f8fafc", borderRadius: "10px", border: "1px solid #cbd5e1" }}>
                      <div style={{ fontWeight: 700, fontSize: "13.5px", marginBottom: "8px", color: "#0f172a" }}>
                        New Attached Assistant
                      </div>

                      {helperFormError && (
                        <div style={{ color: "#991b1b", fontSize: "12px", marginBottom: "8px", background: "#fef2f2", padding: "8px 12px", borderRadius: "6px" }}>
                          ⚠️ {helperFormError}
                        </div>
                      )}

                      <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: "10px", marginBottom: "8px" }}>
                        <div className="rbac-form-group">
                          <label>Username *</label>
                          <input
                            type="text"
                            required
                            placeholder="e.g. deo.officer"
                            value={helperUsername}
                            onChange={(e) => setHelperUsername(e.target.value)}
                            className="rbac-input"
                          />
                        </div>
                        <div className="rbac-form-group">
                          <label>Full Name *</label>
                          <input
                            type="text"
                            required
                            placeholder="e.g. Assistant Name"
                            value={helperFullName}
                            onChange={(e) => setHelperFullName(e.target.value)}
                            className="rbac-input"
                          />
                        </div>
                      </div>

                      <div style={{ display: "flex", gap: "14px", alignItems: "center", marginBottom: "10px" }}>
                        <label style={{ display: "flex", alignItems: "center", gap: "6px", fontSize: "13px", cursor: "pointer" }}>
                          <input
                            type="radio"
                            name="seniorHelperDesignation"
                            checked={helperDesignationChoice === "DEO"}
                            onChange={() => setHelperDesignationChoice("DEO")}
                            style={{ accentColor: "#2563eb" }}
                          />
                          DEO
                        </label>
                        <label style={{ display: "flex", alignItems: "center", gap: "6px", fontSize: "13px", cursor: "pointer" }}>
                          <input
                            type="radio"
                            name="seniorHelperDesignation"
                            checked={helperDesignationChoice === "OTHER"}
                            onChange={() => setHelperDesignationChoice("OTHER")}
                            style={{ accentColor: "#2563eb" }}
                          />
                          Custom Title
                        </label>
                        {helperDesignationChoice === "OTHER" && (
                          <input
                            type="text"
                            placeholder="e.g. Assistant Reader"
                            value={helperCustomDesignation}
                            onChange={(e) => setHelperCustomDesignation(e.target.value)}
                            className="rbac-input"
                            style={{ height: "36px", padding: "0 10px", flex: 1 }}
                          />
                        )}
                      </div>

                      <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: "10px", marginBottom: "10px" }}>
                        <div className="rbac-form-group">
                          <label>Personal Seat / Desk</label>
                          <select
                            value={helperDeskId}
                            onChange={(e) => setHelperDeskId(e.target.value)}
                            className="rbac-input"
                          >
                            <option value="">-- No Specific Seat --</option>
                            {options?.desks.map((d) => (
                              <option key={d.id} value={d.id}>
                                {d.name} ({d.code})
                              </option>
                            ))}
                          </select>
                        </div>

                        <div>
                          <label style={{ fontSize: "13px", fontWeight: 600, color: "#334155", display: "block", marginBottom: "6px" }}>
                            Helper Access Level
                          </label>
                          <div style={{ display: "flex", gap: "10px", alignItems: "center", height: "42px" }}>
                            <label style={{ display: "flex", alignItems: "center", gap: "4px", fontSize: "12px", cursor: "pointer" }}>
                              <input
                                type="radio"
                                name="seniorHelperAccessRadio"
                                value="None" checked={helperAccess === "None"}
                                onChange={() => setHelperAccess("None")}
                                style={{ accentColor: "#2563eb" }}
                              />
                              None
                            </label>
                            <label style={{ display: "flex", alignItems: "center", gap: "4px", fontSize: "12px", cursor: "pointer" }}>
                              <input
                                type="radio"
                                name="seniorHelperAccessRadio"
                                value="ReadOnly" checked={helperAccess === "ReadOnly"}
                                onChange={() => setHelperAccess("ReadOnly")}
                                style={{ accentColor: "#2563eb" }}
                              />
                              Read Only
                            </label>
                            <label style={{ display: "flex", alignItems: "center", gap: "4px", fontSize: "12px", cursor: "pointer" }}>
                              <input
                                type="radio"
                                name="seniorHelperAccessRadio"
                                value="ReadWrite" checked={helperAccess === "ReadWrite"}
                                onChange={() => setHelperAccess("ReadWrite")}
                                style={{ accentColor: "#2563eb" }}
                              />
                              Read + Write
                            </label>
                          </div>
                        </div>
                      </div>

                      <div style={{ display: "flex", justifyContent: "flex-end", gap: "8px" }}>
                        <button
                          type="button"
                          className="rbac-btn-sm rbac-btn-outline"
                          onClick={() => setShowAddHelperForOfficer(false)}
                        >
                          Cancel
                        </button>
                        <button
                          type="button"
                          className="rbac-btn-sm rbac-btn-primary"
                          onClick={handleAddHelperSubmit}
                        >
                          Save Assistant
                        </button>
                      </div>
                    </div>
                  )}
                </div>
              </div>

              <div className="rbac-modal-footer">
                <button
                  type="button"
                  className="rbac-btn-outline"
                  onClick={() => setEditingOfficer(null)}
                  disabled={actionLoading}
                >
                  Cancel
                </button>
                <button
                  type="submit"
                  className="rbac-btn-primary"
                  disabled={actionLoading}
                >
                  Review Changes
                </button>
              </div>
            </form>
          </div>
        </div>
      )}

      {/* =========================================================================
          CONFIRMATION DIALOG (Mandatory Safety Before Any Server Mutation)
          ========================================================================= */}
      {confirmationDialog && (
        <div className="rbac-modal-backdrop" style={{ zIndex: 1200 }}>
          <div className="rbac-confirm-shell" onClick={(e) => e.stopPropagation()}>
            <div className="rbac-confirm-header">
              <h3>{confirmationDialog.title}</h3>
              <button
                type="button"
                className="rbac-modal-close-btn"
                onClick={() => setConfirmationDialog(null)}
                aria-label="Cancel"
              >
                &times;
              </button>
            </div>

            <div className="rbac-confirm-body">
              <div>
                <span style={{ color: "#64748b", fontSize: "12px", textTransform: "uppercase", fontWeight: 700 }}>Employee</span>
                <div style={{ fontSize: "15px", fontWeight: 700, color: "#0f172a" }}>
                  {confirmationDialog.employeeName}
                </div>
              </div>

              {confirmationDialog.message && (
                <div style={{ lineHeight: 1.45 }}>
                  {confirmationDialog.message}
                </div>
              )}

              {confirmationDialog.summaryItems && confirmationDialog.summaryItems.length > 0 && (
                <div className="rbac-confirm-diff-list">
                  {confirmationDialog.summaryItems.map((item, idx) => (
                    <div key={idx} className="rbac-diff-row">
                      <span style={{ width: "140px", color: "#64748b", fontWeight: 600, flexShrink: 0 }}>
                        {item.label}:
                      </span>
                      <span className={item.isAddition ? "rbac-diff-add" : item.isRemoval ? "rbac-diff-remove" : ""} style={{ flex: 1 }}>
                        {item.value}
                      </span>
                    </div>
                  ))}
                </div>
              )}

              {confirmationDialog.warning && (
                <div style={{ padding: "10px 14px", borderRadius: "8px", background: "#fffbeb", border: "1px solid #fef3c7", color: "#92400e", fontSize: "12.5px" }}>
                  ⚠️ {confirmationDialog.warning}
                </div>
              )}
            </div>

            <div className="rbac-confirm-footer">
              <button
                type="button"
                className="rbac-btn-outline"
                onClick={() => setConfirmationDialog(null)}
                disabled={actionLoading}
              >
                Cancel
              </button>
              <button
                type="button"
                className={`rbac-btn-primary ${confirmationDialog.confirmTone === "danger" ? "rbac-btn-danger" : ""}`}
                style={confirmationDialog.confirmTone === "warning" ? { background: "#d97706", borderColor: "#b45309" } : {}}
                onClick={() => void confirmationDialog.onConfirm()}
                disabled={actionLoading}
              >
                {actionLoading ? "Processing…" : confirmationDialog.confirmLabel}
              </button>
            </div>
          </div>
        </div>
      )}

      {/* =========================================================================
          TECHNICAL ACCESS INSPECTOR MODAL (SYSTEM_ADMIN ONLY)
          ========================================================================= */}
      {inspectingOfficer && (
        <div className="rbac-modal-backdrop" onClick={() => setInspectingOfficer(null)}>
          <div className="rbac-modal-shell" style={{ maxWidth: "560px" }} onClick={(e) => e.stopPropagation()}>
            <div className="rbac-modal-header">
              <div>
                <h2>Technical Access Inspector</h2>
                <p>{inspectingOfficer.fullName} ({inspectingOfficer.username})</p>
              </div>
              <button className="rbac-modal-close-btn" onClick={() => setInspectingOfficer(null)}>&times;</button>
            </div>
            <div className="rbac-modal-body" style={{ display: "flex", flexDirection: "column", gap: "12px" }}>
              <div style={{ display: "grid", gridTemplateColumns: "140px 1fr", gap: "8px", fontSize: "13px" }}>
                <span style={{ color: "#64748b" }}>Civil Designation:</span>
                <span style={{ fontWeight: 600, color: "#0f172a" }}>
                  {inspectingOfficer.authority === "SYSTEM_ADMIN" && !inspectingOfficer.designationId && !inspectingOfficer.customDesignation
                    ? "No civil designation (Technical System Administrator)"
                    : inspectingOfficer.effectiveDesignation || "Unassigned"}
                </span>

                <span style={{ color: "#64748b" }}>Authority:</span>
                <span>
                  <span className={`rbac-badge ${getAuthorityBadgeTone(inspectingOfficer.authority)}`}>
                    {getAuthorityBadgeLabel(inspectingOfficer.authority)}
                  </span>
                </span>

                <span style={{ color: "#64748b" }}>Status:</span>
                <span style={{ color: inspectingOfficer.isActive ? "#166534" : "#94a3b8", fontWeight: 600 }}>
                  {inspectingOfficer.isActive ? "Active" : "Inactive"}
                </span>

                <span style={{ color: "#64748b" }}>Land Records:</span>
                <span>{inspectingOfficer.landAccess}</span>

                <span style={{ color: "#64748b" }}>Inward Dak:</span>
                <span>{inspectingOfficer.canRegisterInwardDak ? "Can Register Inward Dak" : "No Registry Rights"}</span>

                <span style={{ color: "#64748b" }}>Work Modules:</span>
                <span>
                  {inspectingOfficer.modules.length > 0
                    ? inspectingOfficer.modules.join(", ")
                    : inspectingOfficer.authority === "OFFICE_SUPERVISOR" || inspectingOfficer.authority === "OFFICE_ADMIN" || inspectingOfficer.authority === "SYSTEM_ADMIN"
                    ? "Full Office Access"
                    : "None"}
                </span>

                <span style={{ color: "#64748b" }}>Assigned Desks:</span>
                <span>
                  {inspectingOfficer.deskIds.length > 0
                    ? options?.desks.filter(d => inspectingOfficer.deskIds.includes(d.id)).map(d => `${d.name} (${d.code})`).join(", ") || "Assigned Desks"
                    : "None"}
                </span>
              </div>
            </div>
            <div className="rbac-modal-footer">
              <button className="rbac-btn-outline" onClick={() => setInspectingOfficer(null)}>
                Close
              </button>
            </div>
          </div>
        </div>
      )}

      {/* =========================================================================
          ONE-TIME TEMPORARY CREDENTIAL MODAL
          ========================================================================= */}
      {showCredentialModal && credentialData && (
        <div className="rbac-modal-backdrop">
          <div className="rbac-modal-shell" style={{ maxWidth: "480px" }}>
            <div className="rbac-modal-header">
              <div>
                <h2>One-Time Temporary Credential</h2>
                <p>Share securely with the employee. This password will not be shown again.</p>
              </div>
            </div>

            <div className="rbac-modal-body" style={{ display: "flex", flexDirection: "column", gap: "16px" }}>
              <div style={{ background: "#f8fafc", padding: "16px", borderRadius: "10px", border: "1px solid #e2e8f0" }}>
                <div style={{ fontSize: "12px", color: "#64748b", textTransform: "uppercase", fontWeight: 600, letterSpacing: "0.05em" }}>
                  Official Username
                </div>
                <div style={{ fontSize: "16px", fontWeight: 700, color: "#0f172a", marginTop: "4px" }}>
                  {credentialData.username}
                </div>

                <div style={{ fontSize: "12px", color: "#64748b", textTransform: "uppercase", fontWeight: 600, letterSpacing: "0.05em", marginTop: "16px" }}>
                  Temporary Password
                </div>
                <div style={{ display: "flex", alignItems: "center", gap: "10px", marginTop: "6px" }}>
                  <code style={{ fontSize: "18px", fontWeight: 700, color: "#1e3a8a", background: "#ffffff", padding: "8px 12px", borderRadius: "8px", border: "1px solid #cbd5e1", flex: 1, letterSpacing: "0.05em" }}>
                    {credentialData.temporaryCredential}
                  </code>
                  <button type="button" className="rbac-btn-outline" onClick={copyCredentialToClipboard} style={{ padding: "8px 14px" }}>
                    {copiedNotice ? "Copied! ✓" : "Copy"}
                  </button>
                </div>

                <div style={{ marginTop: "16px", fontSize: "12.5px", color: "#b45309", background: "#fffbeb", padding: "10px 14px", borderRadius: "8px", border: "1px solid #fef3c7" }}>
                  ⏱ <strong>Valid for 24 hours.</strong> The officer must replace it with a personal password on first login.
                </div>
              </div>

              <label style={{ display: "flex", alignItems: "flex-start", gap: "10px", fontSize: "13px", color: "#334155", cursor: "pointer", background: "#f1f5f9", padding: "12px 14px", borderRadius: "8px" }}>
                <input
                  type="checkbox"
                  checked={credentialAcknowledged}
                  onChange={(e) => setCredentialAcknowledged(e.target.checked)}
                  style={{ marginTop: "2px", accentColor: "#2563eb" }}
                />
                <span>
                  I have copied or securely communicated this temporary credential to the employee. I acknowledge that it cannot be retrieved again.
                </span>
              </label>
            </div>

            <div className="rbac-modal-footer">
              <button
                type="button"
                className="rbac-btn-primary"
                disabled={!credentialAcknowledged}
                onClick={closeCredentialModal}
              >
                Close &amp; Finish
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
};
