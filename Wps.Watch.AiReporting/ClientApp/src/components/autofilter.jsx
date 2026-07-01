import React, { useState as useStateF, useEffect as useEffectF, useRef as useRefF, useMemo as useMemoF } from 'react';
import { Icon } from './icons';

// Excel-style autofilter — per-column dropdown with sort + value-list filter.
// Click the filter glyph in any column header to open a menu with:
//   • Sort A→Z / Smallest→Largest (and reverse)
//   • Search box that filters the value list
//   • Select all / Clear all
//   • Scrollable checkbox list of unique values (with counts)
//   • Apply / Cancel footer
// Active filters surface as chips in the Active Filters bar.

// Format a column value for display in the filter list.
function formatFilterValue(col, v) {
  if (v == null) return "(blank)";
  if (col.key === "onlinePct") return v.toFixed(1) + "%";
  if (typeof v === "number") return v.toLocaleString("en-US");
  return String(v);
}

// Compare values for sorting filter options. Numbers ascending, strings A-Z.
function compareFilterValue(a, b) {
  if (typeof a === "number" && typeof b === "number") return a - b;
  return String(a).localeCompare(String(b));
}

// Get unique sorted values for a column from the given rows, with counts.
function uniqueValuesWithCounts(rows, key) {
  const counts = new Map();
  for (const r of rows) {
    const v = r[key];
    counts.set(v, (counts.get(v) || 0) + 1);
  }
  return [...counts.entries()]
    .sort(([a], [b]) => compareFilterValue(a, b))
    .map(([value, count]) => ({ value, count }));
}

// Apply all column filters (each is a Set of allowed values) to a row set.
function applyColumnFilters(rows, columnFilters, exceptKey) {
  let out = rows;
  for (const [key, allowed] of Object.entries(columnFilters || {})) {
    if (!allowed || key === exceptKey) continue;
    out = out.filter(r => allowed.has(r[key]));
  }
  return out;
}

// Filter menu component — appears below a column header.
const ColumnFilterMenu = ({ col, allRows, currentSet, onApply, onClear, onClose, onSort, buttonRect }) => {
  // Available values come from `allRows` (which the caller passes already
  // filtered by every OTHER filter — Excel behavior).
  const options = useMemoF(() => uniqueValuesWithCounts(allRows, col.key), [allRows, col.key]);
  const [search, setSearch] = useStateF("");
  // If no filter currently active, default to "everything selected"
  const initial = useMemoF(() => {
    if (currentSet && currentSet.size) return new Set(currentSet);
    return new Set(options.map(o => o.value));
  }, [currentSet, options]);
  const [selected, setSelected] = useStateF(initial);
  const menuRef = useRefF(null);

  // Outside click + Escape closes.
  useEffectF(() => {
    const onDown = (e) => {
      if (menuRef.current && !menuRef.current.contains(e.target)) onClose();
    };
    const onKey = (e) => { if (e.key === "Escape") onClose(); };
    document.addEventListener("mousedown", onDown);
    document.addEventListener("keydown", onKey);
    return () => {
      document.removeEventListener("mousedown", onDown);
      document.removeEventListener("keydown", onKey);
    };
  }, [onClose]);

  // Compute fixed-position coords from the trigger button's viewport rect.
  // Align the menu's right edge to the button's right if the button is past
  // the right half of the viewport, else align to the left.
  const MENU_W = 260;
  let style = { position: "fixed", top: 80, left: 16, width: MENU_W };
  if (buttonRect) {
    const top = Math.min(
      buttonRect.bottom + 6,
      window.innerHeight - 360 // keep at least ~360px below for the menu
    );
    const wantLeft = buttonRect.left + (buttonRect.width / 2) - MENU_W / 2;
    const maxLeft = window.innerWidth - MENU_W - 12;
    const left = Math.max(12, Math.min(wantLeft, maxLeft));
    style = { position: "fixed", top, left, width: MENU_W };
  }

  const filteredOptions = options.filter(o => {
    if (!search) return true;
    return formatFilterValue(col, o.value).toLowerCase().includes(search.toLowerCase());
  });

  const allSelected = filteredOptions.every(o => selected.has(o.value));
  const noneSelected = filteredOptions.every(o => !selected.has(o.value));

  const toggleAll = () => {
    const next = new Set(selected);
    if (allSelected) {
      filteredOptions.forEach(o => next.delete(o.value));
    } else {
      filteredOptions.forEach(o => next.add(o.value));
    }
    setSelected(next);
  };

  const isNum = col.num;
  const sortAscLabel = isNum ? "Sort smallest → largest" : "Sort A → Z";
  const sortDescLabel = isNum ? "Sort largest → smallest" : "Sort Z → A";

  return (
    <div className="colfilter" style={style} ref={menuRef} role="dialog">
      <div className="colfilter__sort">
        <button onClick={() => { onSort("asc"); onClose(); }}>
          <Icon name="chevron-up" size={11} stroke={2.5}/> {sortAscLabel}
        </button>
        <button onClick={() => { onSort("desc"); onClose(); }}>
          <Icon name="chevron-down" size={11} stroke={2.5}/> {sortDescLabel}
        </button>
      </div>
      <div className="colfilter__divider"></div>
      <div className="colfilter__title">Filter <span style={{color:"var(--ink-3)", fontWeight:500}}>· {col.label}</span></div>
      <div className="colfilter__search">
        <Icon name="search" size={12}/>
        <input
          autoFocus
          placeholder="Search values…"
          value={search}
          onChange={(e) => setSearch(e.target.value)}
        />
      </div>
      <div className="colfilter__toggle-all">
        <label>
          <input
            type="checkbox"
            checked={allSelected}
            ref={(el) => el && (el.indeterminate = !allSelected && !noneSelected)}
            onChange={toggleAll}
          />
          {allSelected ? "Unselect all" : "Select all"}
          <span className="colfilter__count">
            {filteredOptions.length} value{filteredOptions.length === 1 ? "" : "s"}
          </span>
        </label>
      </div>
      <div className="colfilter__list">
        {filteredOptions.length === 0 && (
          <div className="colfilter__empty">No matching values</div>
        )}
        {filteredOptions.map(o => (
          <label className="colfilter__opt" key={String(o.value)}>
            <input
              type="checkbox"
              checked={selected.has(o.value)}
              onChange={(e) => {
                const next = new Set(selected);
                if (e.target.checked) next.add(o.value);
                else next.delete(o.value);
                setSelected(next);
              }}
            />
            <span className="colfilter__opt-label">{formatFilterValue(col, o.value)}</span>
            <span className="colfilter__opt-count">{o.count}</span>
          </label>
        ))}
      </div>
      <div className="colfilter__foot">
        {currentSet && currentSet.size > 0 && (
          <button className="colfilter__link" onClick={() => { onClear(); onClose(); }}>
            Clear filter
          </button>
        )}
        <span style={{flex:1}}></span>
        <button className="btn btn--ghost btn--sm" onClick={onClose}>Cancel</button>
        <button className="btn btn--primary btn--sm" onClick={() => {
          // If everything is checked, treat as no filter
          if (selected.size === options.length) onClear();
          else onApply(selected);
          onClose();
        }}>OK</button>
      </div>
    </div>
  );
};

export { ColumnFilterMenu, applyColumnFilters, formatFilterValue, uniqueValuesWithCounts };
