// POST: a signed-in user whose access is pending asks us to email the admin a one-tap approval link.
// The caller's Supabase JWT is verified server-side; the email is taken from the token, never from the request body.
import { ADMIN_EMAIL, SB_URL, SB_SERVICE, configured, rest, sign, esc } from "./_lib.js";

// Best-effort per-IP throttle (per warm instance); the hard limit is "one email per request" via notified_at below.
const hits = new Map<string, number[]>();
const throttled = (ip: string) => {
  const now = Date.now(); const list = (hits.get(ip) || []).filter((t) => now - t < 60_000);
  list.push(now); hits.set(ip, list);
  if (hits.size > 5000) hits.clear();
  return list.length > 5;
};

export default async function handler(req: any, res: any) {
  res.setHeader("Cache-Control", "no-store");
  const ip = String(req.headers["x-forwarded-for"] || "").split(",")[0].trim() || "unknown";
  if (throttled(ip)) return res.status(429).json({ error: "slow down" });
  if (req.method !== "POST") return res.status(405).json({ error: "POST only" });
  if (!configured()) return res.status(200).json({ sent: false, reason: "not configured" });
  const token = String(req.headers.authorization || "").replace(/^Bearer /i, "");
  if (!token) return res.status(401).json({ error: "unauthorised" });

  const u = await fetch(`${SB_URL}/auth/v1/user`, { headers: { apikey: SB_SERVICE, Authorization: `Bearer ${token}` } });
  if (!u.ok) return res.status(401).json({ error: "unauthorised" });
  const email = String((await u.json()).email || "").toLowerCase();
  if (!email.endsWith("@fiftyfivetech.io")) return res.status(403).json({ error: "forbidden" });

  const r = await rest(`access_requests?email=eq.${encodeURIComponent(email)}&select=status,notified_at`);
  const row = (await r.json())?.[0];
  if (!row || row.status !== "pending" || row.notified_at) return res.status(200).json({ sent: false });

  const key = process.env.RESEND_API_KEY;
  if (!key) return res.status(200).json({ sent: false, reason: "no email provider; admin sees it in the app" });

  const site = (process.env.SITE_URL || `https://${req.headers.host}`).replace(/\/$/, "");
  const exp = Date.now() + 7 * 24 * 3600 * 1000;
  const link = (a: string) => `${site}/api/approve?email=${encodeURIComponent(email)}&action=${a}&exp=${exp}&sig=${sign(email, a, exp)}`;
  const mail = await fetch("https://api.resend.com/emails", {
    method: "POST",
    headers: { Authorization: `Bearer ${key}`, "Content-Type": "application/json" },
    body: JSON.stringify({
      from: process.env.NOTIFY_FROM || "FDE Tracker <onboarding@resend.dev>",
      to: [ADMIN_EMAIL],
      subject: `Access request: ${email}`,
      html: `<p><b>${esc(email)}</b> asked for access to the FDE Learning Tracker.</p>
<p><a href="${link("approve")}" style="background:#2B5BA9;color:#fff;padding:10px 18px;border-radius:8px;text-decoration:none;font-weight:600">Approve in one tap</a>
&nbsp; <a href="${link("reject")}" style="color:#B3261E">Reject</a></p>
<p style="color:#666;font-size:13px">These links are made only for you and expire in 7 days. You can also decide in the app under Admin.</p>`,
    }),
  });
  if (mail.ok) await rest(`access_requests?email=eq.${encodeURIComponent(email)}`, { method: "PATCH", body: JSON.stringify({ notified_at: new Date().toISOString() }) });
  return res.status(200).json({ sent: mail.ok });
}
