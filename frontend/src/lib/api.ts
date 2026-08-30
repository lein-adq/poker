import { API_URL } from "./config";

async function request<T>(path: string, init?: RequestInit): Promise<T> {
  const res = await fetch(`${API_URL}${path}`, {
    ...init,
    credentials: "include",
    headers: { "Content-Type": "application/json", ...init?.headers },
  });
  if (!res.ok) {
    const body = await res.json().catch(() => ({ error: res.statusText }));
    throw new Error(body.error ?? `Request failed (${res.status})`);
  }
  if (res.status === 204) return undefined as T;
  
  const text = await res.text();
  if (!text) return undefined as T;
  return JSON.parse(text);
}

export interface WalletBalance {
  balance: number;
  playChips: number;
}

export interface TableSummary {
  id: string;
  name: string;
  seatedPlayerCount: number;
  maxSeats: number;
  minBuyIn: number;
  maxBuyIn: number;
  isPrivate: boolean;
  status: "WaitingForPlayers" | "Playing";
  waitlistCount: number;
}

export const api = {
  getWallet: () => request<WalletBalance>("/api/wallet/"),
  claimWelcomeGift: () => request<{ granted: number }>("/api/wallet/welcome-gift/claim", { method: "POST" }),
  listTables: () => request<TableSummary[]>("/api/tables/"),
  createTable: (input: {
    name: string;
    minBuyIn: number;
    maxBuyIn: number;
    smallBlind: number;
    bigBlind: number;
    isPrivate: boolean;
    useRealBankroll: boolean;
  }) => request<TableSummary>("/api/tables/", { method: "POST", body: JSON.stringify(input) }),
  requestRebuy: (tableId: string, additionalChips: number) =>
    request<void>(`/api/tables/${tableId}/rebuy`, { method: "POST", body: JSON.stringify({ additionalChips }) }),
  getProfile: () => request<{ id: string; displayName: string | null; role: string }>("/api/profile/"),
  updateProfile: (displayName: string) => 
    request<void>("/api/profile/display-name", { method: "PUT", body: JSON.stringify({ name: displayName }) }),
};
