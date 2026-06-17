// Helper functions and constants for Region & Site Totals.

export const REGIONS = ["Africa", "Asia", "Americas", "Oceania"];

// helper: format numbers with commas
export function fmt(n) { return n == null ? "—" : Number(n).toLocaleString("en-US"); }
export function fmtPct(n) { return n == null ? "—" : n.toFixed(2) + "%"; }

// aggregate totals
export function sum(arr, key) { return arr.reduce((acc, r) => acc + (r[key] || 0), 0); }
export function aggregate(rows) {
  const histInv = sum(rows, "histInv");
  const decomm = sum(rows, "decomm");
  const currInv = sum(rows, "currInv");
  const undepl = sum(rows, "undepl");
  const deployed = sum(rows, "deployed");
  const sites = sum(rows, "sites");
  const issues = sum(rows, "issues");
  const online = sum(rows, "online");
  const offline = sum(rows, "offline");
  const total = online + offline;
  const onlinePct = total ? (online / total) * 100 : 0;
  return { histInv, decomm, currInv, undepl, deployed, sites, issues, online, offline, total, onlinePct };
}

// region rollups
export function rollupByRegion(rows) {
  const groups = {};
  rows.forEach(r => { (groups[r.region] = groups[r.region] || []).push(r); });
  return Object.entries(groups).map(([region, list]) => ({
    region,
    children: list,
    ...aggregate(list),
  }));
}
