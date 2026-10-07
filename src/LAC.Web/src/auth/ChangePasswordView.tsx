import React, { useState } from "react";
import type { CurrentUser } from "./types";
import { PasswordInput } from "./PasswordInput";

interface ChangePasswordViewProps {
  user: CurrentUser;
  onSuccess: () => Promise<void> | void;
  onLogout: () => Promise<void> | void;
}

export const ChangePasswordView: React.FC<ChangePasswordViewProps> = ({ user, onSuccess, onLogout }) => {
  const [currentPassword, setCurrentPassword] = useState("");
  const [newPassword, setNewPassword] = useState("");
  const [confirmPassword, setConfirmPassword] = useState("");
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [success, setSuccess] = useState(false);

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    setError(null);

    if (!currentPassword) {
      setError("Please enter your current temporary password.");
      return;
    }
    if (newPassword.length < 12) {
      setError("New password must be at least 12 characters in length.");
      return;
    }
    if (newPassword === currentPassword) {
      setError("New password must differ from your current temporary password.");
      return;
    }
    if (newPassword !== confirmPassword) {
      setError("New password and confirmation do not match.");
      return;
    }

    try {
      setLoading(true);
      const response = await fetch("/api/auth/change-password", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        credentials: "include",
        body: JSON.stringify({
          currentPassword,
          newPassword,
        }),
      });

      if (response.status === 401) {
        throw new Error("Current temporary password is incorrect or session expired.");
      }
      if (!response.ok) {
        const errorData = await response.json().catch(() => null);
        throw new Error(errorData?.message || errorData?.detail || `Failed to update password (${response.status}).`);
      }

      setSuccess(true);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Failed to change password. Please verify your current password.");
    } finally {
      setLoading(false);
    }
  };

  if (success) {
    return (
      <div className="login-container">
        <div className="login-card" style={{ maxWidth: "480px" }}>
          <div className="login-header">
            <div className="login-emblem" style={{ background: "#16a34a" }}>✓</div>
            <h1>Password Updated</h1>
            <p className="login-subheading">Security session credential replacement confirmed</p>
          </div>

          <div style={{
            background: "#f0fdf4",
            border: "1px solid #bbf7d0",
            borderRadius: "8px",
            padding: "16px",
            margin: "20px 0",
            color: "#166534",
            fontSize: "14px",
            lineHeight: 1.5,
          }}>
            <strong>Mandatory credential update complete.</strong>
            <p style={{ margin: "8px 0 0 0" }}>
              All prior officer sessions have been invalidated server-side. Please log in with your new permanent password to access operational modules.
            </p>
          </div>

          <button
            type="button"
            className="login-button"
            onClick={() => void onSuccess()}
            style={{ width: "100%", padding: "12px" }}
          >
            Proceed to Login
          </button>
        </div>
      </div>
    );
  }

  return (
    <div className="login-container">
      <div className="login-card" style={{ maxWidth: "520px" }}>
        <div className="login-header">
          <div className="login-emblem" style={{ background: "#dc2626" }}>🔒</div>
          <h1>Mandatory Password Replacement</h1>
          <p className="login-subheading">Government of NCT of Delhi · Revenue Department</p>
        </div>

        <div style={{
          background: "#fffbeb",
          border: "1px solid #fde68a",
          borderLeft: "4px solid #f59e0b",
          borderRadius: "6px",
          padding: "12px 14px",
          margin: "16px 0",
          fontSize: "13px",
          color: "#92400e",
          lineHeight: 1.45,
        }}>
          <strong>Account Security Notice:</strong>
          <div>
            Account <strong>{user.displayName}</strong> (<code>{user.username}</code>) was issued with a temporary credential or administrative reset.
            Statutory records policy requires establishing a permanent password before granting operational access.
          </div>
        </div>

        {error && (
          <div className="login-error-alert" role="alert" style={{ marginBottom: "16px" }}>
            <span>⚠️</span> {error}
          </div>
        )}

        <form onSubmit={handleSubmit} className="login-form">
          <div className="form-group">
            <label htmlFor="currentPassword">Current Temporary Password</label>
            <PasswordInput
              id="currentPassword"
              value={currentPassword}
              onChange={(e) => setCurrentPassword(e.target.value)}
              placeholder="Enter temporary password"
              disabled={loading}
              required
              autoFocus
            />
          </div>

          <div className="form-group">
            <label htmlFor="newPassword">New Permanent Password</label>
            <PasswordInput
              id="newPassword"
              value={newPassword}
              onChange={(e) => setNewPassword(e.target.value)}
              placeholder="Minimum 12 characters"
              disabled={loading}
              required
            />
            <div style={{ fontSize: "11.5px", color: newPassword.length >= 12 ? "#16a34a" : "#64748b", marginTop: "4px" }}>
              {newPassword.length >= 12 ? "✓ Length requirement met (>= 12 chars)" : "• Must be at least 12 characters"}
            </div>
          </div>

          <div className="form-group">
            <label htmlFor="confirmPassword">Confirm New Password</label>
            <PasswordInput
              id="confirmPassword"
              value={confirmPassword}
              onChange={(e) => setConfirmPassword(e.target.value)}
              placeholder="Re-enter new password"
              disabled={loading}
              required
            />
            {confirmPassword && confirmPassword !== newPassword && (
              <div style={{ fontSize: "11.5px", color: "#dc2626", marginTop: "4px" }}>
                Passwords do not match
              </div>
            )}
          </div>

          <div style={{ display: "flex", gap: "10px", marginTop: "20px" }}>
            <button
              type="submit"
              className="login-button"
              disabled={loading || newPassword.length < 12 || newPassword !== confirmPassword}
              style={{ flex: 1 }}
            >
              {loading ? "Updating Credential…" : "Set Permanent Password"}
            </button>
            <button
              type="button"
              className="secondary-button"
              onClick={() => void onLogout()}
              disabled={loading}
              style={{ padding: "0 16px" }}
            >
              Sign Out
            </button>
          </div>
        </form>

        <div className="login-footer" style={{ marginTop: "24px" }}>
          <small>Government of NCT of Delhi · Revenue Department</small>
          <small className="security-notice">
            Setting a permanent password invalidates all other concurrent sessions immediately.
          </small>
        </div>
      </div>
    </div>
  );
};
