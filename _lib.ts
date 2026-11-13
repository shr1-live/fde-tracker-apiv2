// Server-side helpers for Vercel functions. Secrets come from Vercel env vars and never reach the browser.
import { createHmac, timingSafeEqual } from "node:crypto";

export const ADMIN_EMAIL = (process.env.ADMIN_EMAIL || "shivanshu.rai@fiftyfivetech.io").toLowerCase();
export const SB_URL = (process.env.SUPABASE_URL || process.env.VITE_SUPABASE_URL || "").replace(/\/$/, "");
export const SB_SERVICE = process.env.SUPABASE_SERVICE_ROLE_KEY || "";
const SECRET = process.env.APPROVAL_SECRET || "";
export const configured = () => !!(SB_URL && SB_SERVICE && SECRET);

export const sign = (email: string, action: string, exp: number) =>
  createHmac("sha256", SECRET).update(`${email}|${action}|${exp}`).digest("hex");

export function verify(email: string, action: string, exp: number, sig: string) {
  if (!SECRET || !email || !(exp > Date.now()) || !sig) return false;
  const a = Buffer.from(sign(email, action, exp));
  const b = Buffer.from(sig);
  return a.length === b.length && timingSafeEqual(a, b);
}

export const rest = (path: string, init: RequestInit = {}) =>
  fetch(`${SB_URL}/rest/v1/${path}`, {
    ...init,
    headers: { apikey: SB_SERVICE, Authorization: `Bearer ${SB_SERVICE}`, "Content-Type": "application/json", Prefer: "return=representation", ...(init.headers || {}) },
  });

const ENT: Record<string, string> = { "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" };
export const esc = (s: string) => s.replace(/[&<>"']/g, (c) => ENT[c]);
