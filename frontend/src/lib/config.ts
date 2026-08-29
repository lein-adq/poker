// The frontend talks to two origins:
//  - VITE_API_URL: Ory Oathkeeper's proxy port, which injects X-User-Id/X-User-Email onto every
//    /api/** and /hubs/** request after validating the Kratos session cookie.
//  - VITE_KRATOS_URL: Kratos's public API, used directly for the registration/login/logout flows.
export const API_URL = import.meta.env.VITE_API_URL ?? "http://localhost:4455";
export const KRATOS_URL = import.meta.env.VITE_KRATOS_URL ?? "http://localhost:4433";
