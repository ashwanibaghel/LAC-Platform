import React, { useState } from "react";
import { useAuth } from "./AuthProvider";
import { PasswordInput } from "./PasswordInput";

export const LoginPage: React.FC = () => {
  const { login } = useAuth();
  const [username, setUsername] = useState("");
  const [password, setPassword] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!username.trim() || !password) {
      setError("Please enter both username and password.");
      return;
    }

    try {
      setError(null);
      setLoading(true);
      await login(username.trim(), password);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Authentication failed. Please check credentials.");
    } finally {
      setLoading(false);
    }
  };

  return (
    <div className="login-container">
      <div className="login-card">
        <div className="login-header">
          <div className="login-emblem">LAC</div>
          <h1>LAC Platform</h1>
          <p className="login-subheading">Land Acquisition Collector & Revenue Records Portal</p>
        </div>

        {error && (
          <div className="login-error-alert" role="alert">
            <span>⚠️</span> {error}
          </div>
        )}

        <form onSubmit={handleSubmit} className="login-form">
          <div className="form-group">
            <label htmlFor="username">Username / Official ID</label>
            <input
              id="username"
              type="text"
              value={username}
              onChange={(e) => setUsername(e.target.value)}
              placeholder="e.g. admin"
              autoComplete="username"
              disabled={loading}
              autoFocus
              required
            />
          </div>

          <div className="form-group">
            <label htmlFor="password">Password</label>
            <PasswordInput
              id="password"
              value={password}
              onChange={(e) => setPassword(e.target.value)}
              placeholder="••••••••"
              autoComplete="current-password"
              disabled={loading}
              required
            />
          </div>

          <button type="submit" className="login-button" disabled={loading}>
            {loading ? "Verifying Credentials..." : "Sign In to Office"}
          </button>
        </form>

        <div className="login-footer">
          <small>Government of NCT of Delhi · Revenue Department</small>
          <small className="security-notice">
            Authorized Personnel Only. All access and activity is logged and audited under statutory records rules.
          </small>
        </div>
      </div>
    </div>
  );
};
