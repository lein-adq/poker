import { createContext, useContext, useEffect, useState, type ReactNode } from "react";
import { whoAmI, logout as kratosLogout, type KratosSession } from "../lib/kratos";

interface AuthState {
  session: KratosSession | null;
  loading: boolean;
  refresh: () => Promise<void>;
  logout: () => Promise<void>;
}

const AuthContext = createContext<AuthState | null>(null);

export function AuthProvider({ children }: { children: ReactNode }) {
  const [session, setSession] = useState<KratosSession | null>(null);
  const [loading, setLoading] = useState(true);

  const refresh = async () => {
    setSession(await whoAmI());
  };

  useEffect(() => {
    refresh().finally(() => setLoading(false));
  }, []);

  const logout = async () => {
    await kratosLogout();
    setSession(null);
  };

  return <AuthContext.Provider value={{ session, loading, refresh, logout }}>{children}</AuthContext.Provider>;
}

export function useAuth(): AuthState {
  const ctx = useContext(AuthContext);
  if (!ctx) throw new Error("useAuth must be used within AuthProvider");
  return ctx;
}
