// Typed client for the wpsWatch AI Reporting REST API (served same-origin from the
// ASP.NET app). The canned report rows come back with the backend's column names;
// `mapRegionSiteTotals` adapts them to the row shape the prototype components expect.

export interface ReportRunResponse {
  reportId: string;
  executedAtUtc: string;
  durationMs: number;
  columns: string[];
  rowCount: number;
  rows: Record<string, unknown>[];
  parametersApplied: Record<string, unknown>;
}

/** The row shape the ported report components consume (formerly window.ORG_ROWS). */
export interface OrgRow {
  id: number;
  region: string;
  org: string;
  histInv: number;
  decomm: number;
  currInv: number;
  undepl: number;
  deployed: number;
  sites: number;
  issues: number;
  online: number;
  offline: number;
  total: number;
  onlinePct: number;
}

export interface AskAnswer {
  count: number;
  window: string;
  filters: string[];
  columns: string[];
  rows: Record<string, unknown>[];
  sql: string;
  truncated: boolean;
  moreCount: number;
}

export interface AskResponseBody {
  text: string;
  answer: AskAnswer | null;
}

export interface AskError extends Error {
  code?: string;
  status?: number;
}

function num(v: unknown): number {
  if (typeof v === "number") return v;
  if (v == null) return 0;
  const n = Number(v);
  return Number.isFinite(n) ? n : 0;
}

/** Run a canned report by id. Body is the parameter map (empty for parameterless reports). */
export async function runReport(
  id: string,
  params: Record<string, unknown> = {},
): Promise<ReportRunResponse> {
  const res = await fetch(`/api/reports/${id}/run`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(params),
  });
  if (!res.ok) {
    throw new Error(`Report "${id}" failed (HTTP ${res.status}).`);
  }
  return res.json();
}

/** Adapt the backend's region-site-totals columns to the prototype's OrgRow shape. */
export function mapRegionSiteTotals(resp: ReportRunResponse): OrgRow[] {
  return resp.rows.map((r, i) => {
    const online = num(r.Online);
    const offline = num(r.Offline);
    const total = num(r.Total) || online + offline;
    return {
      id: num(r.OrganizationId) || i,
      region: (r.RegionName as string) || "Unknown",
      org: (r.OrganizationName as string) || "—",
      histInv: num(r.HistoricalInventory),
      decomm: num(r.Decommissioned),
      currInv: num(r.CurrentInventory),
      undepl: num(r.Undeployed),
      deployed: num(r.TotalDeployments),
      sites: num(r.SiteCount),
      issues: num(r.ActiveIssues),
      online,
      offline,
      total,
      onlinePct: total ? (online / total) * 100 : 0,
    };
  });
}

/** Ask the AI Reports chat a plain-English question. */
export async function ask(
  question: string,
  history?: { role: string; text: string }[],
): Promise<AskResponseBody> {
  const res = await fetch("/api/ask", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ question, history }),
  });
  if (!res.ok) {
    const body = await res.json().catch(() => ({}) as Record<string, unknown>);
    const err: AskError = new Error(
      (body.message as string) || `Ask failed (HTTP ${res.status}).`,
    );
    err.code = body.error as string | undefined;
    err.status = res.status;
    throw err;
  }
  return res.json();
}
