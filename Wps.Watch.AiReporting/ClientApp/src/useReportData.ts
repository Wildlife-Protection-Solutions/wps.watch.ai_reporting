import { useEffect, useState } from "react";
import { mapRegionSiteTotals, runReport, type OrgRow } from "./api";

type Status = "loading" | "ready" | "error";

/**
 * Fetches a canned report and adapts it to the prototype's OrgRow[] shape.
 * Currently wired to the region-site-totals tracer report; other report ids fall
 * back to the same mapper (the column adapter is keyed on the backend's names).
 */
export function useReportData(reportId: string): {
  rows: OrgRow[];
  status: Status;
  error: string | null;
} {
  const [rows, setRows] = useState<OrgRow[]>([]);
  const [status, setStatus] = useState<Status>("loading");
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;
    setStatus("loading");
    setError(null);

    runReport(reportId)
      .then((resp) => {
        if (cancelled) return;
        setRows(mapRegionSiteTotals(resp));
        setStatus("ready");
      })
      .catch((e: unknown) => {
        if (cancelled) return;
        setError(e instanceof Error ? e.message : String(e));
        setStatus("error");
      });

    return () => {
      cancelled = true;
    };
  }, [reportId]);

  return { rows, status, error };
}
