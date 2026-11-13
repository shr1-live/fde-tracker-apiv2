// GET shows a confirm page (so mail scanners that prefetch links cannot approve anyone); POST applies the decision.
// Both need a valid HMAC signature minted only for this email and action, so no login is needed and nobody can forge one.
import { configured, verify, rest, esc } from "./_lib.js";

const page = (body: string) => `<!doctype html><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>FDE Tracker access</title>
<body style="font-family:system-ui,sans-serif;background:#0E141B;color:#E7ECF2;display:grid;place-items:center;min-height:100vh;margin:0"><main style="max-width:420px;padding:28px;background:#17202A;border:1px solid #2B3744;border-radius:16px;text-align:center">${body}</main>`;
const btn = "font:inherit;font-weight:600;padding:12px 22px;border-radius:10px;border:0;cursor:pointer;color:#10161D;background:#E5BC3E";

export default async function handler(req: any, res: any) {
  res.setHeader("Cache-Control", "no-store");
  res.setHeader("Content-Type", "text/html; charset=utf-8");
  res.setHeader("X-Robots-Tag", "noindex");
  const src = req.method === "POST" ? req.body || {} : req.query || {};
  const email = String(src.email || "").toLowerCase();
  const action = String(src.action || "");
  const exp = Number(src.exp);
  const sig = String(src.sig || "");
  if (!configured() || !["approve", "reject"].includes(action) || !verify(email, action, exp, sig)) {
    return res.status(400).send(page("<h2>Link invalid or expired</h2><p>Open the app and use the Admin page instead.</p>"));
  }
  if (req.method !== "POST") {
    return res.status(200).send(page(`<h2>${action === "approve" ? "Approve" : "Reject"} access?</h2><p><b>${esc(email)}</b></p>
<form method="post"><input type="hidden" name="email" value="${esc(email)}"><input type="hidden" name="action" value="${action}"><input type="hidden" name="exp" value="${exp}"><input type="hidden" name="sig" value="${esc(sig)}">
<button style="${btn}">Yes, ${action}</button></form>`));
  }
  const r = await rest(`access_requests?email=eq.${encodeURIComponent(email)}`, {
    method: "PATCH",
    body: JSON.stringify({ status: action === "approve" ? "approved" : "rejected", decided_at: new Date().toISOString() }),
  });
  const ok = r.ok && (await r.json()).length > 0;
  return res.status(ok ? 200 : 404).send(page(ok ? `<h2>Done</h2><p>${esc(email)} is now <b>${action === "approve" ? "approved" : "rejected"}</b>.</p>` : "<h2>No such request</h2>"));
}
