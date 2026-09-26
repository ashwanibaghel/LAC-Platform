import React, { createContext, useContext, useEffect, useState, useCallback } from "react";
import type { CurrentUser, AuthContextType } from "./types";

const AuthContext = createContext<AuthContextType | undefined>(undefined);

export const AuthProvider: React.FC<{ children: React.ReactNode }> = ({ children }) => {
  const [user, setUser] = useState<CurrentUser | null>(null);
  const [loading, setLoading] = useState(true);

  const refreshUser = useCallback(async () => {
    try {
      const response = await fetch("/api/auth/me", { credentials: "include" });
      if (response.ok) {
        const data = (await response.json()) as CurrentUser;
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
    void refreshUser();
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
    } catch {
      throw new Error("Cannot reach the sign-in server. Your password was not checked; please retry when the API is running.");
    }

    if (!response.ok) {
      if (response.status === 502 || response.status === 503 || response.status === 504) {
        throw new Error("Sign-in server is unavailable. Your password was not checked; please retry when the API is running.");
      }
      if (response.status !== 401 && response.status !== 400) {
        throw new Error(`Sign-in failed because of a server error (HTTP ${response.status}). Please retry.`);
      }
      const err = (await response.json().catch(() => null)) as { message?: string } | null;
      throw new Error(err?.message || "Invalid username or password");
    }

    const data = (await response.json()) as CurrentUser;
    setUser(data);
  };

  const logout = async () => {
    try {
      await fetch("/api/auth/logout", {
        method: "POST",
        credentials: "include",
      });
    } finally {
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
