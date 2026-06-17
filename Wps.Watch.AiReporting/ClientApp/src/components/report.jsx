import React, { useMemo, useState as useStateR, useEffect as useEffectR, useRef } from 'react';
import ReactDOM from 'react-dom';
import { Icon } from './icons';
import { ColumnFilterMenu, applyColumnFilters, uniqueValuesWithCounts, formatFilterValue } from './autofilter';
import { fmt, fmtPct, aggregate, rollupByRegion, REGIONS } from '../data';

// Region & Site Totals report — the tracer-bullet Phase-1 page.
// All filters/sort animate but the underlying click-through is a happy path.

// ---------- KPI variants ----------

const Sparkline = ({ values, color = "var(--gold)", height = 24, width = 90 }) => {
  if (!values || !values.length) return null;
  const min = Math.min(...values), max = Math.max(...values);
  const rng = max - min || 1;
  const pts = values.map((v, i) => {
    const x = (i / (values.length - 1)) * width;
    const y = height - ((v - min) / rng) * (height - 4) - 2;
    return [x, y];
  });
  const d = pts.map(([x, y], i) => `${i === 0 ? "M" : "L"}${x.toFixed(1)} ${y.toFixed(1)}`).join(" ");
  const area = `${d} L${width} ${height} L0 ${height} Z`;
  return (
    <svg width={width} height={height} className="kpi__spark">
      <path d={area} fill={color} opacity="0.12"/>
      <path d={d} stroke={color} strokeWidth="1.5" fill="none" strokeLinecap="round" strokeLinejoin="round"/>
      <circle cx={pts[pts.length-1][0]} cy={pts[pts.length-1][1]} r="2.5" fill={color}/>
    </svg>
  );
};

const KPICards = ({ totals, rows }) => {
  return (
    <div className="kpis">
      <div className="kpi kpi--feature">
        <div className="kpi__label">
          <Icon name="alert" size={13}/>
          Active Issues
        </div>
        <div className="kpi__value">{fmt(totals.issues)}</div>
        <div className="kpi__sub">
          <span className="trend-up"><Icon name="trend-up" size={12} stroke={2}/> +18</span>
          <span>vs. yesterday — needs triage</span>
        </div>
        <Sparkline values={[420, 432, 445, 440, 458, 462, 456, 474]} color="var(--status-bad)"/>
      </div>
      <div className="kpi">
        <div className="kpi__label"><Icon name="wifi" size={13}/> Online</div>
        <div className="kpi__value">{fmt(totals.online)}<sup>{fmtPct(totals.onlinePct)}</sup></div>
        <div className="kpi__sub">
          <span className="trend-down"><Icon name="trend-down" size={12} stroke={2}/> −12</span>
          <span>of {fmt(totals.total)} deployed</span>
        </div>
        <Sparkline values={[2870, 2855, 2842, 2860, 2848, 2835, 2826, 2814]} color="var(--status-good)"/>
      </div>
      <div className="kpi">
        <div className="kpi__label"><Icon name="wifi-off" size={13}/> Offline</div>
        <div className="kpi__value">{fmt(totals.offline)}</div>
        <div className="kpi__sub">
          <span className="trend-up"><Icon name="trend-up" size={12} stroke={2}/> +12</span>
          <span>since 24h ago</span>
        </div>
        <Sparkline values={[1180, 1192, 1206, 1198, 1211, 1218, 1225, 1225]} color="var(--status-warn)"/>
      </div>
      <div className="kpi">
        <div className="kpi__label"><Icon name="camera" size={13}/> Total Deployed</div>
        <div className="kpi__value">{fmt(totals.deployed)}</div>
        <div className="kpi__sub">
          <span className="trend-flat">·</span>
          <span>{fmt(totals.undepl)} in stock</span>
        </div>
      </div>
      <div className="kpi">
        <div className="kpi__label"><Icon name="globe" size={13}/> Sites Active</div>
        <div className="kpi__value">{fmt(totals.sites)}</div>
        <div className="kpi__sub">
          <span>across <strong style={{color:"var(--ink-2)"}}>{Object.keys(rollupByRegion(rows).reduce((a,r) => (a[r.region]=1,a), {})).length}</strong> regions</span>
        </div>
      </div>
    </div>
  );
};

const TotalsStrip = ({ totals, rows }) => (
  <div className="totals-strip">
    <div className="totals-strip__cell totals-strip__cell--feature">
      <div className="totals-strip__label" style={{display:"flex", alignItems:"center", gap:6}}>
        <Icon name="alert" size={13}/> Active Issues
      </div>
      <div className="totals-strip__value" style={{color:"var(--status-bad)"}}>{fmt(totals.issues)}</div>
      <div className="totals-strip__sub">+18 since yesterday</div>
    </div>
    <div className="totals-strip__cell">
      <div className="totals-strip__label">Online</div>
      <div className="totals-strip__value">{fmt(totals.online)} <span style={{fontSize:13, color:"var(--ink-3)", fontWeight:600}}>· {fmtPct(totals.onlinePct)}</span></div>
      <div className="totals-strip__sub">of {fmt(totals.total)} deployed</div>
    </div>
    <div className="totals-strip__cell">
      <div className="totals-strip__label">Offline</div>
      <div className="totals-strip__value" style={{color:"var(--status-warn)"}}>{fmt(totals.offline)}</div>
      <div className="totals-strip__sub">+12 in last 24h</div>
    </div>
    <div className="totals-strip__cell">
      <div className="totals-strip__label">Deployed</div>
      <div className="totals-strip__value">{fmt(totals.deployed)}</div>
      <div className="totals-strip__sub">{fmt(totals.undepl)} in stock</div>
    </div>
    <div className="totals-strip__cell">
      <div className="totals-strip__label">Sites</div>
      <div className="totals-strip__value">{fmt(totals.sites)}</div>
      <div className="totals-strip__sub">{Object.keys(rollupByRegion(rows).reduce((a,r) => (a[r.region]=1,a), {})).length} regions</div>
    </div>
  </div>
);

// ---------- Top issues chart ----------

const TopIssuesStrip = ({ rows }) => {
  const top = [...rows].sort((a,b) => b.issues - a.issues).slice(0, 6);
  const max = Math.max(...top.map(r => r.issues), 1);
  return (
    <div className="chart-strip">
      <div className="chart-strip__head">
        <div>
          <div className="chart-strip__title">Top organizations by active issues</div>
          <div className="chart-strip__sub">Where to look first this morning · top 6 of {rows.length}</div>
        </div>
        <div className="chart-strip__tabs">
          <button className="chart-strip__tab chart-strip__tab--active">Issues</button>
          <button className="chart-strip__tab">Offline</button>
          <button className="chart-strip__tab">Online %</button>
        </div>
      </div>
      <div className="bars">
        {top.map((r) => (
          <React.Fragment key={r.id}>
            <div className="bars__name" title={r.org}>{r.org}</div>
            <div className="bars__bar-track">
              <div className="bars__bar-fill" style={{ width: `${(r.issues / max) * 100}%` }}></div>
            </div>
            <div className="bars__num">{fmt(r.issues)}</div>
          </React.Fragment>
        ))}
      </div>
    </div>
  );
};

// ---------- Filter UIs ----------

const FilterChips = ({ filters, setFilters, regionCounts, openModal, rows }) => {
  const allRegions = ["All", ...REGIONS];
  return (
    <div className="filterbar">
      <span className="filterbar__label">Region</span>
      <div className="filterbar__group">
        {allRegions.map((r) => {
          const active = filters.region === r;
          const count = r === "All" ? rollupByRegion(rows).reduce((a,x) => a+x.children.length, 0) : (regionCounts[r] || 0);
          return (
            <button key={r}
              className={"chip" + (active ? " chip--active" : "")}
              onClick={() => setFilters({ ...filters, region: r })}>
              {r}
              <span className="chip__count">{count}</span>
            </button>
          );
        })}
      </div>
      <span style={{width:1, height:20, background:"var(--line-1)", margin:"0 6px"}}></span>
      <div className="filterbar__group">
        <button className={"chip" + (filters.health === "issues" ? " chip--active" : "")}
                onClick={() => setFilters({...filters, health: filters.health === "issues" ? null : "issues" })}>
          <Icon name="alert" size={12}/> Has issues
        </button>
        <button className={"chip" + (filters.health === "offline" ? " chip--active" : "")}
                onClick={() => setFilters({...filters, health: filters.health === "offline" ? null : "offline" })}>
          <Icon name="wifi-off" size={12}/> Below 70% online
        </button>
        <button className={"chip" + (filters.health === "stale" ? " chip--active" : "")}
                onClick={() => setFilters({...filters, health: filters.health === "stale" ? null : "stale" })}>
          Stale {">"} 24h
        </button>
        <button className="chip chip--add" onClick={openModal}>
          <Icon name="filter" size={12}/> More filters
        </button>
      </div>
      <div className="filterbar__spacer"></div>
      <div className="search-input">
        <Icon name="search" size={14}/>
        <input
          placeholder="Search organization…"
          value={filters.search || ""}
          onChange={(e) => setFilters({...filters, search: e.target.value})}/>
      </div>
    </div>
  );
};

const FilterModal = ({ filters, setFilters, onClose }) => {
  const [draft, setDraft] = useStateR(filters);
  const tagBtn = (selected, label, key, value) => (
    <button className={"chip" + (selected ? " chip--active" : "")} style={{margin:"2px 4px 2px 0"}}
            onClick={() => setDraft({...draft, [key]: selected ? null : value})}>{label}</button>
  );
  return (
    <div className="modal-overlay" onClick={onClose}>
      <div className="modal" onClick={(e)=>e.stopPropagation()}>
        <div className="modal__head">
          <h3 className="modal__title">Filter Region & Site Totals</h3>
          <button className="btn btn--subtle btn--icon" onClick={onClose}><Icon name="x"/></button>
        </div>
        <div className="modal__body">
          <div className="modal__col">
            <h4>Region</h4>
            {REGIONS.map(r => tagBtn(draft.region === r, r, "region", r))}
            <h4 style={{marginTop: 18}}>Health</h4>
            {tagBtn(draft.health === "issues", "Has active issues", "health", "issues")}
            {tagBtn(draft.health === "offline", "< 70% online", "health", "offline")}
            {tagBtn(draft.health === "stale", "Stale > 24h", "health", "stale")}
          </div>
          <div className="modal__col">
            <h4>Date range</h4>
            <div style={{display:"flex", gap:6, marginBottom:8}}>
              <div className="filter-rail__date" style={{flex:1}}><strong>From</strong>Apr 14, 2026</div>
              <div className="filter-rail__date" style={{flex:1}}><strong>To</strong>May 14, 2026</div>
            </div>
            <div style={{display:"flex", flexWrap:"wrap", gap:4}}>
              {["Today", "7 days", "30 days", "90 days", "YTD"].map(p => (
                <button key={p} className="chip" style={{margin:0}}>{p}</button>
              ))}
            </div>
            <h4 style={{marginTop: 18}}>Minimum cameras deployed</h4>
            <input type="range" min="0" max="200" defaultValue="0" style={{width:"100%", accentColor:"var(--gold)"}}/>
          </div>
          <div className="modal__col">
            <h4>Tags</h4>
            <div style={{display:"flex", flexWrap:"wrap", gap:4}}>
              {["Rhino", "Elephant", "Anti-poaching", "Border", "K9", "Drone"].map(t => (
                <button key={t} className="chip" style={{margin:0}}>{t}</button>
              ))}
            </div>
            <h4 style={{marginTop: 18}}>Camera type</h4>
            <label className="filter-rail__opt"><input type="checkbox" defaultChecked/> Buckeye (Visible)</label>
            <label className="filter-rail__opt"><input type="checkbox" defaultChecked/> Buckeye (Thermal)</label>
            <label className="filter-rail__opt"><input type="checkbox"/> Reconyx</label>
            <label className="filter-rail__opt"><input type="checkbox"/> Bushnell</label>
          </div>
        </div>
        <div className="modal__foot">
          <button className="btn btn--subtle" onClick={() => setDraft({region:"All", health:null, search:""})}>Reset</button>
          <div className="spacer"></div>
          <button className="btn btn--ghost" onClick={onClose}>Cancel</button>
          <button className="btn btn--primary" onClick={() => { setFilters(draft); onClose(); }}>Apply filters</button>
        </div>
      </div>
    </div>
  );
};

const FilterRail = ({ filters, setFilters, regionCounts, rows }) => {
  const regionList = REGIONS;
  return (
    <aside className="filter-rail">
      <h3>Filters <button className="btn btn--subtle btn--sm" onClick={() => setFilters({region:"All", health:null, search:""})}>Clear</button></h3>

      <div className="filter-rail__section">
        <div className="filter-rail__section-title">Date range</div>
        <div className="filter-rail__date-range">
          <div className="filter-rail__date"><strong>From</strong>Apr 14, 2026</div>
          <div className="filter-rail__date"><strong>To</strong>May 14, 2026</div>
        </div>
        <div style={{display:"flex", flexWrap:"wrap", gap:4, marginTop:4}}>
          {["7d", "30d", "90d", "YTD"].map((p, i) => (
            <button key={p} className={"chip" + (i === 1 ? " chip--active" : "")} style={{margin:0, padding:"3px 8px", fontSize:11.5}}>{p}</button>
          ))}
        </div>
      </div>

      <div className="filter-rail__section">
        <div className="filter-rail__section-title">Region <span className="filter-rail__count">{regionList.length}</span></div>
        <label className="filter-rail__opt">
          <input type="radio" name="region" checked={filters.region === "All"} onChange={() => setFilters({...filters, region:"All"})}/> All regions
          <span className="filter-rail__opt-count">{rows.length}</span>
        </label>
        {regionList.map(r => (
          <label key={r} className="filter-rail__opt">
            <input type="radio" name="region" checked={filters.region === r} onChange={() => setFilters({...filters, region:r})}/> {r}
            <span className="filter-rail__opt-count">{regionCounts[r] || 0}</span>
          </label>
        ))}
      </div>

      <div className="filter-rail__section">
        <div className="filter-rail__section-title">Health</div>
        <label className="filter-rail__opt">
          <input type="checkbox" checked={filters.health === "issues"} onChange={() => setFilters({...filters, health: filters.health === "issues" ? null : "issues"})}/>
          Has active issues
        </label>
        <label className="filter-rail__opt">
          <input type="checkbox" checked={filters.health === "offline"} onChange={() => setFilters({...filters, health: filters.health === "offline" ? null : "offline"})}/>
          {"< 70% online"}
        </label>
        <label className="filter-rail__opt">
          <input type="checkbox"/> Stale {">"} 24h
        </label>
        <label className="filter-rail__opt">
          <input type="checkbox"/> Decommissioned {">"} 20%
        </label>
      </div>

      <div className="filter-rail__section">
        <div className="filter-rail__section-title">Camera type</div>
        <label className="filter-rail__opt"><input type="checkbox" defaultChecked/> Buckeye (Visible) <span className="filter-rail__opt-count">2,310</span></label>
        <label className="filter-rail__opt"><input type="checkbox" defaultChecked/> Buckeye (Thermal) <span className="filter-rail__opt-count">1,118</span></label>
        <label className="filter-rail__opt"><input type="checkbox"/> Reconyx <span className="filter-rail__opt-count">412</span></label>
        <label className="filter-rail__opt"><input type="checkbox"/> Bushnell <span className="filter-rail__opt-count">199</span></label>
      </div>

      <div className="filter-rail__section">
        <div className="filter-rail__section-title">Tags</div>
        <div style={{display:"flex", flexWrap:"wrap", gap:4}}>
          {["Rhino", "Elephant", "Anti-poaching", "Border", "K9"].map(t => (
            <button key={t} className="chip" style={{margin:0, padding:"3px 8px", fontSize:11.5}}>{t}</button>
          ))}
        </div>
      </div>
    </aside>
  );
};

// ---------- Active filter chips (under header) ----------

const ActiveFilterChips = ({ filters, setFilters, columnFilters, setColumnFilters, globalSearch, setGlobalSearch, resultCount }) => {
  const chips = [];
  if (filters.region && filters.region !== "All") {
    chips.push({ key: "region", label: "Region", value: filters.region, clear: () => setFilters({...filters, region: "All"}) });
  }
  if (filters.health) {
    const map = { issues: "Has issues", offline: "< 70% online", stale: "Stale > 24h" };
    chips.push({ key: "health", label: "Health", value: map[filters.health], clear: () => setFilters({...filters, health: null }) });
  }
  if (filters.search) {
    chips.push({ key: "search", label: "Search", value: `"${filters.search}"`, clear: () => setFilters({...filters, search: ""}) });
  }
  if (globalSearch) {
    chips.push({ key: "global", label: "Find", value: `"${globalSearch}"`, clear: () => setGlobalSearch && setGlobalSearch("") });
  }
  // Column filters (excel-style)
  if (columnFilters) {
    Object.entries(columnFilters).forEach(([key, set]) => {
      if (!set || set.size === 0) return;
      const col = COLUMNS.find(c => c.key === key);
      if (!col) return;
      let valueStr;
      if (set.size <= 2) {
        valueStr = [...set].map(v => formatFilterValue(col, v)).join(", ");
      } else {
        valueStr = `${set.size} values`;
      }
      chips.push({
        key: `col-${key}`,
        label: col.label,
        value: valueStr,
        clear: () => {
          const next = { ...columnFilters };
          delete next[key];
          setColumnFilters(next);
        },
      });
    });
  }
  if (chips.length === 0) {
    return (
      <div style={{ display:"flex", alignItems:"center", marginBottom: 12, fontSize:12.5, color:"var(--ink-3)" }}>
        <span>Showing <strong style={{color:"var(--ink-1)"}}>{resultCount}</strong> {resultCount === 1 ? "organization" : "organizations"} · no filters applied. Use any column's funnel icon to filter by value.</span>
      </div>
    );
  }
  return (
    <div style={{ display:"flex", gap:6, alignItems:"center", flexWrap:"wrap", marginBottom: 12 }}>
      <span style={{fontSize:11.5, color:"var(--ink-3)", fontWeight:600, letterSpacing:0.3, textTransform:"uppercase"}}>Active filters</span>
      {chips.map(c => (
        <span key={c.key} className="chip chip--filter">
          <span className="chip__key">{c.label}:</span> {c.value}
          <button className="chip__remove" onClick={c.clear} title="Remove"><Icon name="x" size={12}/></button>
        </span>
      ))}
      <button className="btn btn--subtle btn--sm" onClick={() => {
        setFilters({region:"All", health:null, search:""});
        setColumnFilters && setColumnFilters({});
        setGlobalSearch && setGlobalSearch("");
      }}>Clear all</button>
      <span style={{marginLeft:"auto", fontSize:12.5, color:"var(--ink-3)"}}>
        Showing <strong style={{color:"var(--ink-1)"}}>{resultCount}</strong> {resultCount === 1 ? "organization" : "organizations"}
      </span>
    </div>
  );
};

// ---------- Data table ----------

const COLUMNS = [
  { key: "org", label: "Organization", num: false, w: 240 },
  { key: "histInv", label: "Hist Inv", num: true, w: 70, hint: "Historical inventory" },
  { key: "decomm", label: "Decomm", num: true, w: 70 },
  { key: "currInv", label: "Curr Inv", num: true, w: 70 },
  { key: "undepl", label: "In Stock", num: true, w: 70, hint: "Undeployed / in stock" },
  { key: "deployed", label: "Deployed", num: true, w: 80 },
  { key: "sites", label: "Sites", num: true, w: 64 },
  { key: "issues", label: "Issues", num: true, w: 80, hint: "Active issues open" },
  { key: "online", label: "Online", num: true, w: 72 },
  { key: "offline", label: "Offline", num: true, w: 72 },
  { key: "total", label: "Total", num: true, w: 64 },
  { key: "onlinePct", label: "Online %", num: true, w: 110 },
];

const SortHeader = ({ col, sort, setSort, onOpenFilter, isFiltered, menuOpenForKey, allRowsForColumn, columnFilters, setColumnFilters }) => {
  const isSorted = sort.key === col.key;
  const dir = isSorted ? sort.dir : null;
  const openHere = menuOpenForKey === col.key;
  const btnRef = useRef(null);
  const [rect, setRect] = useStateR(null);

  // Keep menu attached to button on scroll/resize while open
  useEffectR(() => {
    if (!openHere) return;
    const update = () => {
      if (btnRef.current) setRect(btnRef.current.getBoundingClientRect());
    };
    update();
    window.addEventListener("scroll", update, true);
    window.addEventListener("resize", update);
    return () => {
      window.removeEventListener("scroll", update, true);
      window.removeEventListener("resize", update);
    };
  }, [openHere]);

  return (
    <th className={col.num ? "num" : ""} style={col.w ? {width: col.w} : null}>
      <div className="header-cell">
        <button
          className={isSorted ? "sorted" : ""}
          title={col.hint || col.label}
          onClick={() => {
            if (!isSorted) setSort({ key: col.key, dir: col.num ? "desc" : "asc" });
            else if (dir === "desc") setSort({ key: col.key, dir: "asc" });
            else setSort({ key: null, dir: null });
          }}
          style={{flex: 1, justifyContent: col.num ? "flex-end" : "flex-start"}}
        >
          <span className="th-label">{col.label}</span>
          <span className="sort-arrow">
            {isSorted
              ? <Icon name={dir === "desc" ? "chevron-down" : "chevron-up"} size={12} stroke={2.5}/>
              : <Icon name="arrow-up-down" size={11} stroke={1.6}/>}
          </span>
        </button>
        <button
          ref={btnRef}
          className={"col-filter-btn" + (isFiltered ? " col-filter-btn--active" : "") + (openHere ? " col-filter-btn--active" : "")}
          aria-label={`Filter ${col.label}`}
          title={`Filter ${col.label}`}
          onClick={(e) => {
            e.stopPropagation();
            if (openHere) { onOpenFilter(null); return; }
            setRect(e.currentTarget.getBoundingClientRect());
            onOpenFilter(col.key);
          }}
        >
          <Icon name="filter" size={11} stroke={2}/>
        </button>
      </div>
      {openHere && rect && ReactDOM.createPortal(
        <ColumnFilterMenu
          col={col}
          allRows={allRowsForColumn(col.key)}
          currentSet={columnFilters[col.key]}
          buttonRect={rect}
          onSort={(d) => setSort({ key: col.key, dir: d })}
          onApply={(set) => setColumnFilters({ ...columnFilters, [col.key]: set })}
          onClear={() => {
            const next = { ...columnFilters };
            delete next[col.key];
            setColumnFilters(next);
          }}
          onClose={() => onOpenFilter(null)}
        />,
        document.body
      )}
    </th>
  );
};

const Gauge = ({ value }) => {
  const cls = value >= 80 ? "" : value >= 65 ? "gauge__fill--warn" : "gauge__fill--bad";
  return (
    <div className="gauge">
      <div className="gauge__track">
        <div className={"gauge__fill " + cls} style={{ width: `${Math.max(0, Math.min(100, value))}%` }}></div>
      </div>
      <span className="gauge__num">{value.toFixed(1)}%</span>
    </div>
  );
};

const orgFlag = (region) => {
  const map = { "Africa": "AF", "Asia": "AS", "Americas": "AM", "Oceania": "OC" };
  return map[region] || "—";
};

const Table = ({ rows, sort, setSort, totals, groupByRegion, expandedRegions, toggleRegion, columnFilters, setColumnFilters, allRowsForColumn, openFilterKey, setOpenFilterKey, filterAnchor, setFilterAnchor, narrow }) => {
  const renderRow = (r) => (
    <tr key={r.id}>
      <td className="col-org">
        <div className="org-cell">
          <span className="org-flag" title={r.region}>{orgFlag(r.region)}</span>
          <div>
            <div className="org-name">{r.org}</div>
            <div className="org-region">{r.region}</div>
          </div>
        </div>
      </td>
      <td className="num">{fmt(r.histInv)}</td>
      <td className="num" style={{color: "var(--ink-3)"}}>{fmt(r.decomm)}</td>
      <td className="num">{fmt(r.currInv)}</td>
      <td className="num" style={{color: "var(--ink-3)"}}>{fmt(r.undepl)}</td>
      <td className="num">{fmt(r.deployed)}</td>
      <td className="num">{fmt(r.sites)}</td>
      <td className="num">
        <span className={"issues-badge" + (r.issues === 0 ? " issues-badge--zero" : "")}>{fmt(r.issues)}</span>
      </td>
      <td className="num"><span className="online-num">{fmt(r.online)}</span></td>
      <td className="num"><span className="offline-num">{fmt(r.offline)}</span></td>
      <td className="num">{fmt(r.total)}</td>
      <td className="num"><Gauge value={r.onlinePct}/></td>
    </tr>
  );

  return (
    <table className={"tbl" + (narrow ? " tbl--narrow" : "")}>
      <thead>
        <tr>
          {COLUMNS.map(col => (
            <SortHeader
              key={col.key}
              col={col}
              sort={sort}
              setSort={setSort}
              isFiltered={!!columnFilters[col.key] && columnFilters[col.key].size > 0}
              menuOpenForKey={openFilterKey}
              onOpenFilter={setOpenFilterKey}
              allRowsForColumn={allRowsForColumn}
              columnFilters={columnFilters}
              setColumnFilters={setColumnFilters}
            />
          ))}
        </tr>
      </thead>
      <tbody>
        {groupByRegion ? (
          rollupByRegion(rows).map(group => {
            const open = expandedRegions[group.region] !== false; // default open
            return (
              <React.Fragment key={group.region}>
                <tr className="is-region" onClick={() => toggleRegion(group.region)}>
                  <td>
                    <button className="expand-toggle">
                      <Icon name={open ? "chevron-down" : "chevron-right"} size={12} stroke={2.5}/>
                    </button>
                    {group.region} <span style={{color:"var(--ink-3)", fontWeight:500}}>· {group.children.length} orgs</span>
                  </td>
                  <td className="num">{fmt(group.histInv)}</td>
                  <td className="num">{fmt(group.decomm)}</td>
                  <td className="num">{fmt(group.currInv)}</td>
                  <td className="num">{fmt(group.undepl)}</td>
                  <td className="num">{fmt(group.deployed)}</td>
                  <td className="num">{fmt(group.sites)}</td>
                  <td className="num"><span className={"issues-badge" + (group.issues === 0 ? " issues-badge--zero" : "")}>{fmt(group.issues)}</span></td>
                  <td className="num">{fmt(group.online)}</td>
                  <td className="num">{fmt(group.offline)}</td>
                  <td className="num">{fmt(group.total)}</td>
                  <td className="num"><Gauge value={group.onlinePct}/></td>
                </tr>
                {open && group.children.map(renderRow)}
              </React.Fragment>
            );
          })
        ) : rows.map(renderRow)}
        <tr className="is-total">
          <td>Grand total <span style={{color:"var(--ink-3)", fontWeight:500}}>· {rows.length} orgs</span></td>
          <td className="num">{fmt(totals.histInv)}</td>
          <td className="num">{fmt(totals.decomm)}</td>
          <td className="num">{fmt(totals.currInv)}</td>
          <td className="num">{fmt(totals.undepl)}</td>
          <td className="num">{fmt(totals.deployed)}</td>
          <td className="num">{fmt(totals.sites)}</td>
          <td className="num">
            <span className="issues-badge">{fmt(totals.issues)}</span>
          </td>
          <td className="num">{fmt(totals.online)}</td>
          <td className="num">{fmt(totals.offline)}</td>
          <td className="num">{fmt(totals.total)}</td>
          <td className="num"><Gauge value={totals.onlinePct}/></td>
        </tr>
      </tbody>
    </table>
  );
};

const CardGrid = ({ rows }) => (
  <div className="cards-grid">
    {rows.map(r => {
      const healthCls = r.onlinePct >= 80 ? "" : r.onlinePct >= 65 ? "warn" : "bad";
      return (
        <div key={r.id} className="org-card">
          <div className="org-card__head">
            <span className="org-flag" style={{width:28, height:28, fontSize:11, flex:"0 0 28px"}}>{orgFlag(r.region)}</span>
            <div style={{flex:1, minWidth:0}}>
              <div className="org-card__name">{r.org}</div>
              <div className="org-card__region">{r.region}</div>
            </div>
            {r.issues > 0 && (
              <span className="issues-badge" title={`${r.issues} active issues`}>
                <Icon name="alert" size={11} stroke={2.2}/> {r.issues}
              </span>
            )}
          </div>
          <div className="org-card__gauge">
            <Gauge value={r.onlinePct}/>
          </div>
          <div className="org-card__stats">
            <div>
              <div className="org-card__stat-label">Deployed</div>
              <div className="org-card__stat-value">{fmt(r.deployed)}</div>
            </div>
            <div>
              <div className="org-card__stat-label">Online</div>
              <div className="org-card__stat-value">{fmt(r.online)}</div>
            </div>
            <div>
              <div className="org-card__stat-label">Offline</div>
              <div className={"org-card__stat-value " + (r.offline > 30 ? "warn" : "")}>{fmt(r.offline)}</div>
            </div>
            <div>
              <div className="org-card__stat-label">Sites</div>
              <div className="org-card__stat-value">{fmt(r.sites)}</div>
            </div>
          </div>
        </div>
      );
    })}
  </div>
);

// ---------- Main report ----------

const ReportPage = ({ rows, tweaks, chatOpen, setChatOpen }) => {
  const [filters, setFilters] = useStateR({ region: "All", health: null, search: "" });
  const [sort, setSort] = useStateR({ key: "issues", dir: "desc" });
  const [modalOpen, setModalOpen] = useStateR(false);
  const [expandedRegions, setExpandedRegions] = useStateR({});
  const [page, setPage] = useStateR(1);
  const [columnFilters, setColumnFilters] = useStateR({}); // { [key]: Set<value> }
  const [globalSearch, setGlobalSearch] = useStateR("");
  const [openFilterKey, setOpenFilterKey] = useStateR(null);
  const [filterAnchor, setFilterAnchor] = useStateR(null);

  // 1) Apply top-level filters (region/health/text search box) — these stay independent of column filters.
  const topFiltered = useMemo(() => {
    let rows2 = rows;
    if (filters.region && filters.region !== "All") rows2 = rows2.filter(r => r.region === filters.region);
    if (filters.health === "issues") rows2 = rows2.filter(r => r.issues > 0);
    if (filters.health === "offline") rows2 = rows2.filter(r => r.onlinePct < 70);
    if (filters.search) {
      const s = filters.search.toLowerCase();
      rows2 = rows2.filter(r => r.org.toLowerCase().includes(s));
    }
    if (globalSearch) {
      const s = globalSearch.toLowerCase();
      rows2 = rows2.filter(r =>
        COLUMNS.some(c => String(r[c.key] ?? "").toLowerCase().includes(s))
      );
    }
    return rows2;
  }, [rows, filters, globalSearch]);

  // 2) Apply column filters. allRowsForColumn returns the row set with every
  //    OTHER column filter applied — so the per-column menu options reflect
  //    what would be visible if the user changed this column's filter.
  const filtered = useMemo(
    () => applyColumnFilters(topFiltered, columnFilters),
    [topFiltered, columnFilters]
  );
  const allRowsForColumn = (key) => applyColumnFilters(topFiltered, columnFilters, key);

  const sorted = useMemo(() => {
    if (!sort.key) return filtered;
    const dir = sort.dir === "desc" ? -1 : 1;
    return [...filtered].sort((a, b) => {
      const va = a[sort.key], vb = b[sort.key];
      if (typeof va === "string") return va.localeCompare(vb) * dir;
      return (va - vb) * dir;
    });
  }, [filtered, sort]);

  const totals = useMemo(() => aggregate(filtered), [filtered]);

  const regionCounts = useMemo(() => {
    const counts = {};
    rows.forEach(r => { counts[r.region] = (counts[r.region] || 0) + 1; });
    return counts;
  }, [rows]);

  const toggleRegion = (region) => {
    setExpandedRegions(prev => ({ ...prev, [region]: prev[region] === false ? true : false }));
  };

  // Determine layouts from tweaks
  const summaryMode = tweaks.summary || "cards";       // "cards" | "strip"
  const filterMode = tweaks.filterUi || "chips";       // "chips" | "rail" | "modal"
  const density = tweaks.density || "compact";          // "compact" | "roomy"
  const showRegions = tweaks.groupByRegion !== false;   // default true
  const showChartStrip = tweaks.showChartStrip !== false;

  return (
    <main className="content">
      {/* Header */}
      <div className="report-header">
        <div className="report-title-wrap">
          <div className="report-eyebrow">
            <span className="dot"></span>
            <Icon name="report" size={12}/>
            Fleet Health
          </div>
          <h1 className="report-title">Region & Site Totals</h1>
          <div className="report-sub">
            Showing <strong>April 14, 2026 — May 14, 2026</strong> · Inventory and live status across all reachable organizations · Updated <strong>3 minutes ago</strong>
          </div>
        </div>
        <div className="report-actions">
          <button className="btn btn--ghost"><Icon name="star" size={14}/> Save view</button>
          <button className="btn btn--ghost"><Icon name="refresh" size={14}/> Refresh</button>
          <button className="btn btn--ghost"><Icon name="download" size={14}/> Export <Icon name="chevron-down" size={12}/></button>
          <button
            className={"btn btn--primary" + (chatOpen ? " btn--ai-active" : "")}
            onClick={() => setChatOpen && setChatOpen(!chatOpen)}>
            <Icon name="sparkles" size={14}/> {chatOpen ? "Hide chat" : "Ask a follow-up"}
          </button>
        </div>
      </div>

      {/* Summary */}
      {summaryMode === "cards" ? <KPICards totals={totals} rows={rows}/> : <TotalsStrip totals={totals} rows={rows}/>}

      {/* Optional chart strip — only when issues filter is the headline */}
      {showChartStrip && <TopIssuesStrip rows={sorted}/>}

      {/* Active filter chips */}
      <ActiveFilterChips
        filters={filters}
        setFilters={setFilters}
        columnFilters={columnFilters}
        setColumnFilters={setColumnFilters}
        globalSearch={globalSearch}
        setGlobalSearch={setGlobalSearch}
        resultCount={filtered.length}
      />

      {/* Common Table/Toolbar props so both filter-mode branches stay in sync */}
      {(() => null)()}
      {/* Layout depending on filter mode */}
      {filterMode === "rail" ? (
        <div className="with-rail">
          <FilterRail filters={filters} setFilters={setFilters} regionCounts={regionCounts} rows={rows}/>
          <div>
            <div className="table-wrap table-wrap--standalone">
              <TableToolbar count={filtered.length} globalSearch={globalSearch} setGlobalSearch={setGlobalSearch}/>
              {density === "compact"
                ? <div className="table-scroll"><Table rows={sorted} sort={sort} setSort={setSort} totals={totals} groupByRegion={showRegions} expandedRegions={expandedRegions} toggleRegion={toggleRegion} columnFilters={columnFilters} setColumnFilters={setColumnFilters} allRowsForColumn={allRowsForColumn} openFilterKey={openFilterKey} setOpenFilterKey={setOpenFilterKey} filterAnchor={filterAnchor} setFilterAnchor={setFilterAnchor} narrow={chatOpen}/></div>
                : <CardGrid rows={sorted}/>}
              <Pagination total={filtered.length} page={page} setPage={setPage}/>
            </div>
          </div>
        </div>
      ) : (
        <div>
          {filterMode === "chips" && (
            <FilterChips filters={filters} setFilters={setFilters} regionCounts={regionCounts} openModal={() => setModalOpen(true)} rows={rows}/>
          )}
          <div className={"table-wrap" + (filterMode === "modal" ? " table-wrap--standalone" : "")}>
            <TableToolbar
              count={filtered.length}
              globalSearch={globalSearch}
              setGlobalSearch={setGlobalSearch}
              onOpenFilters={filterMode === "modal" ? () => setModalOpen(true) : null}
            />
            {density === "compact"
              ? <div className="table-scroll"><Table rows={sorted} sort={sort} setSort={setSort} totals={totals} groupByRegion={showRegions} expandedRegions={expandedRegions} toggleRegion={toggleRegion} columnFilters={columnFilters} setColumnFilters={setColumnFilters} allRowsForColumn={allRowsForColumn} openFilterKey={openFilterKey} setOpenFilterKey={setOpenFilterKey} filterAnchor={filterAnchor} setFilterAnchor={setFilterAnchor} narrow={chatOpen}/></div>
              : <CardGrid rows={sorted}/>}
            <Pagination total={filtered.length} page={page} setPage={setPage}/>
          </div>
        </div>
      )}

      {modalOpen && <FilterModal filters={filters} setFilters={setFilters} onClose={() => setModalOpen(false)}/>}
    </main>
  );
};

const TableToolbar = ({ count, globalSearch, setGlobalSearch, onOpenFilters }) => (
  <div className="table-toolbar">
    {onOpenFilters && (
      <button className="btn btn--ghost btn--sm" onClick={onOpenFilters}>
        <Icon name="filter" size={13}/> Filters
      </button>
    )}
    <div className="table-search">
      <Icon name="search" size={13}/>
      <input
        placeholder="Search this view…"
        value={globalSearch || ""}
        onChange={(e) => setGlobalSearch(e.target.value)}
      />
      <kbd>/</kbd>
    </div>
    <span className="table-toolbar__count"><strong>{count}</strong> {count === 1 ? "organization" : "organizations"}</span>
    <span className="spacer"></span>
    <button className="btn btn--subtle btn--sm"><Icon name="settings" size={13}/> Columns</button>
    <button className="btn btn--subtle btn--sm"><Icon name="share" size={13}/> Share</button>
  </div>
);

const Pagination = ({ total, page, setPage }) => {
  const pageSize = 50; // pretend
  const pages = Math.max(1, Math.ceil(total / pageSize));
  return (
    <div className="pagination">
      <span className="pagination__info">Showing 1–{Math.min(pageSize, total)} of {total}</span>
      <span className="spacer" style={{flex:1}}></span>
      <button className="pagination__btn" disabled={page===1}><Icon name="chevron-left" size={13}/></button>
      {Array.from({length: Math.min(pages, 4)}, (_, i) => (
        <button key={i} className={"pagination__btn" + (i === 0 ? " pagination__btn--active" : "")}>{i+1}</button>
      ))}
      {pages > 4 && <span style={{color:"var(--ink-4)"}}>…</span>}
      <button className="pagination__btn"><Icon name="chevron-right" size={13}/></button>
    </div>
  );
};

export { ReportPage };
