#!/usr/bin/env node
// Claude Code PreToolUse hook (Write | Edit | MultiEdit | NotebookEdit): refuses to write text that looks like a secret.
// No dependencies. Reads the tool call as JSON on stdin; on a match prints a "deny" decision with the reason, so
// the model is told why and can put the value in Render/Vercel settings instead. Exit 0 with no output = allowed.
//
// Public values stay allowed: the Supabase *publishable* key (sb_publishable_...), the API and Supabase URLs.
// web/.env.production may hold those, but never a service_role key.

const RULES = [
  { name: "private key block", re: /-----BEGIN (?:RSA |EC |DSA |OPENSSH |PGP |ENCRYPTED )?PRIVATE KEY(?: BLOCK)?-----/ },
  { name: "AWS access key id", re: /\b(?:AKIA|ASIA)[0-9A-Z]{16}\b/ },
  { name: "Google API key", re: /\bAIza[0-9A-Za-z_-]{35}\b/ },
  { name: "Supabase secret key", re: /\bsb_secret_[A-Za-z0-9_-]{10,}/ },
  { name: "OpenAI/Anthropic-style secret key", re: /\bsk-(?:ant-|proj-|live-|test-)?[A-Za-z0-9_-]{20,}/ },
  { name: "Stripe live key", re: /\b(?:sk|rk)_live_[A-Za-z0-9]{16,}/ },
  { name: "GitHub token", re: /\b(?:ghp|gho|ghs|ghu|github_pat)_[A-Za-z0-9_]{20,}/ },
  { name: "Slack token", re: /\bxox[abprs]-[A-Za-z0-9-]{10,}/ },
  // scheme://user:password@host, e.g. postgres://postgres:hunter2@db.x.supabase.co — placeholders like <password>
  // and throwaway local databases (@localhost, @127.0.0.1) are fine
  { name: "connection string with a password", re: /\b(?:postgres(?:ql)?|mysql|mongodb(?:\+srv)?|redis|rediss|amqp|mssql):\/\/[^\s:/@'"]+:(?![<{$*]|(?:password|pass|pw|changeme|secret)@)[^\s@'"<>{}]{3,}@(?!localhost\b|127\.0\.0\.1\b|\[::1\])/i },
  // ADO.NET style: Host=...;Password=...  (placeholders and env references are fine)
  { name: "connection string with a password", re: /;\s*(?:Password|Pwd)\s*=\s*(?![<{$%*]|;|"|\s|your|changeme|password\b)[^;\s"']{3,}/i },
];

/** A JWT whose payload says role=service_role (the Supabase key that bypasses row-level security). */
function serviceRoleJwt(text) {
  for (const m of text.matchAll(/\beyJ[A-Za-z0-9_-]{8,}\.(eyJ[A-Za-z0-9_-]{8,})\.[A-Za-z0-9_-]{8,}/g)) {
    try {
      const payload = JSON.parse(Buffer.from(m[1], "base64url").toString("utf8"));
      if (payload && payload.role === "service_role") return true;
    } catch { /* not a JWT */ }
  }
  return false;
}

/** Obvious test fixtures, e.g. "AIzaSyOWNKEY_0123456789abcdef..." or "sk-not-a-gemini-key-...": not real keys. */
const FAKE = /0123456789|1234567890|not-a-|example|dummy|fake/i;

function findings(text, filePath) {
  const hits = [];
  for (const r of RULES) {
    const all = new RegExp(r.re.source, r.re.flags + "g");
    if ([...text.matchAll(all)].some((m) => !FAKE.test(m[0]))) hits.push(r.name);
  }
  if (serviceRoleJwt(text)) hits.push("Supabase service_role JWT");
  // .env files (web/.env.production is committed and public): a service_role key is never given a value there.
  // A commented-out name with no value, as in .env.example, is fine.
  if (/(^|[\\/])\.env(\.[\w-]+)?$/i.test(filePath) && /^[ \t]*[^#\s][^=\n]*service[_-]?role[^=\n]*=[ \t]*\S/im.test(text)) hits.push("service_role key in an env file");
  return [...new Set(hits)];
}

/** Every piece of text the tool would write. */
function textsOf(input = {}) {
  const out = [];
  for (const k of ["content", "new_string", "new_source"]) if (typeof input[k] === "string") out.push(input[k]);
  if (Array.isArray(input.edits)) for (const e of input.edits) if (typeof e?.new_string === "string") out.push(e.new_string);
  return out;
}

let raw = "";
process.stdin.setEncoding("utf8");
process.stdin.on("data", (c) => (raw += c));
process.stdin.on("end", () => {
  let call;
  try { call = JSON.parse(raw || "{}"); } catch { process.exit(0); } // not our input: let the normal checks decide
  const input = call.tool_input ?? {};
  const filePath = String(input.file_path ?? input.notebook_path ?? "");
  const hits = findings(textsOf(input).join("\n"), filePath);
  if (hits.length === 0) process.exit(0);

  const reason =
    `Blocked by .claude/hooks/block-secrets.mjs: the text for ${filePath || "this file"} looks like it contains a secret (${hits.join(", ")}). ` +
    "Secrets never go in code, docs or committed env files. Put the value in Render or Vercel environment settings (the owner pastes it), " +
    "and reference it by its setting NAME in code. Public values (API URL, Supabase URL, sb_publishable_ key) are fine. " +
    "If this is a false positive, use an obvious placeholder such as <password>.";
  process.stdout.write(JSON.stringify({
    hookSpecificOutput: { hookEventName: "PreToolUse", permissionDecision: "deny", permissionDecisionReason: reason },
  }));
  process.exit(0);
});
