import React, { createContext, useContext, useEffect, useState, useCallback } from "react";
import type { CurrentUser, AuthContextType } from "./types";
import { sessionAuthenticated, sessionExpiredEvent, sessionLoggedOut } from "./sessionFetch";

const AuthContext = createContext<AuthContextType | undefined>(undefined);

export const AuthProvider: React.FC<{ children: React.ReactNode }> = ({ children }) => {
  const [user, setUser] = useState<CurrentUser | null>(null);
  const [loading, setLoading] = useState(true);

  const refreshUser = useCallback(async () => {
    try {
      const response = await fetch("/api/auth/me", { credentials: "include" });
      if (response.ok) {
        const data = (await response.json()) as CurrentUser;
        sessionAuthenticated();
        setUser(data);
      } else {
        setUser(null);
      }
    } catch {
      setUser(null);
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    const expired = () => {
      try { sessionStorage.setItem("lac:session-expired", "1"); } catch { /* Optional notice. */ }
      setUser(null); setLoading(false);
    };
    window.addEventListener(sessionExpiredEvent, expired);
    void refreshUser();
    return () => window.removeEventListener(sessionExpiredEvent, expired);
  }, [refreshUser]);

  const login = async (username: string, password: string) => {
    let response: Response;
    try {
      response = await fetch("/api/auth/login", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ username, password }),
        credentials: "include",
      });
    } catch { throw new Error("LAC server is not available right now. Please try again."); }

    if (response.status === 401) throw new Error("Username or password is incorrect.");
    if (!response.ok) throw new Error("LAC server is not available right now. Please try again.");

    const data = (await response.json()) as CurrentUser;
    sessionAuthenticated();
    try { sessionStorage.removeItem("lac:session-expired"); } catch { /* Optional notice. */ }
    setUser(data);
  };

  const logout = async () => {
    try {
      await fetch("/api/auth/logout", {
        method: "POST",
        credentials: "include",
      });
    } finally {
      sessionLoggedOut();
      setUser(null);
    }
  };

  const hasPermission = useCallback((permissionCode: string) => {
    if (!user) return false;
    return user.permissions.some((p) => p.code === permissionCode);
  }, [user]);

  return (
    <AuthContext.Provider value={{ user, loading, login, logout, hasPermission, refreshUser }}>
      {children}
    </AuthContext.Provider>
  );
};

export const useAuth = (): AuthContextType => {
  const context = useContext(AuthContext);
  if (!context) {
    throw new Error("useAuth must be used within an AuthProvider");
  }
  return context;
};
