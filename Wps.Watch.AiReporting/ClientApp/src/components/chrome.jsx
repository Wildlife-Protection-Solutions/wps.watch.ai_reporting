import React from 'react';
import { Icon } from './icons';

// Top navigation bar matching wpsWatch + the new "AI Reports" pilot menu item.

const TopNav = () => {
  const items = [
    "Live Feed", "Photo Search", "Users", "Inventory",
    "Deployment Status", "Incident Management", "Issue Tracking",
    "Map", "Admin", "Reports"
  ];
  return (
    <header className="topnav">
      <div className="topnav__logo" aria-label="Wildlife Protection Solutions">
        <div className="topnav__logo-mark" title="Wildlife Protection Solutions">
          <span>WILDLIFE<br/>PROTECTION<br/>SOLUTIONS</span>
        </div>
      </div>
      <nav className="topnav__nav">
        {items.map((label) => {
          const active = label === "Reports";
          return (
            <a key={label}
               className={"topnav__item" + (active ? " topnav__item--active topnav__item--pilot" : "")}>
              {label}
            </a>
          );
        })}
      </nav>
      <div className="topnav__right">
        <button className="topnav__icon-btn" title="Theme"><Icon name="sun" size={18}/></button>
        <button className="topnav__icon-btn" title="Account"><Icon name="user" size={18}/></button>
      </div>
    </header>
  );
};

const SubHeader = () => (
  <div className="subheader">
    <div className="subheader__crumbs">
      <span>Reports</span>
      <span className="subheader__crumb-sep">/</span>
      <strong>Region & Site Totals</strong>
    </div>
    <div className="pilot-banner">
      <span className="dot"></span>
      <span>You're using the <strong>new Reports</strong> experience (pilot).</span>
      <a>Switch to legacy ↗</a>
    </div>
  </div>
);

// Reports sidebar — list of all phase-1 reports, plus future categories.
const ReportSidebar = ({ activeId, setActiveId }) => {
  const groups = [
    {
      title: "Saved",
      items: [
        { id: "morning-sweep", label: "Morning sweep — overnight changes", saved: true },
        { id: "warden-monthly", label: "Warden monthly planning", saved: true },
      ],
    },
    {
      title: "Fleet health",
      items: [
        { id: "region-site-totals", label: "Region & Site Totals", badge: "TRACER" },
        { id: "cameras-offline", label: "Cameras offline" },
        { id: "battery-sim", label: "Battery & SIM status" },
        { id: "deployment-status", label: "Deployment status" },
        { id: "issues-summary", label: "Issue summary" },
      ],
    },
    {
      title: "Activity",
      items: [
        { id: "captures", label: "Captures by camera" },
        { id: "daily-activity", label: "Daily activity" },
        { id: "incidents", label: "Incidents summary" },
        { id: "tags", label: "Tags & classification" },
        { id: "users", label: "User activity" },
      ],
    },
  ];
  return (
    <aside className="sidebar">
      <div className="sidebar__title">Reports</div>
      <div className="sidebar__search">
        <Icon name="search" size={14}/>
        <input placeholder="Find a report…"/>
      </div>
      {groups.map((g, gi) => (
        <div className="sidebar__group" key={gi}>
          <div className="sidebar__group-title">{g.title}</div>
          {g.items.map((it) => (
            <div key={it.id}
                 className={"sidebar__link" + (it.id === activeId ? " sidebar__link--active" : "")}
                 onClick={() => setActiveId && setActiveId(it.id)}>
              <Icon name={it.id === activeId ? "report" : "report"} size={14}/>
              <span style={{ overflow: "hidden", textOverflow: "ellipsis", whiteSpace: "nowrap" }}>{it.label}</span>
              {it.saved && <Icon name="star-fill" size={12} className="star"/>}
              {it.badge && <span className="sidebar__badge">{it.badge}</span>}
            </div>
          ))}
        </div>
      ))}
      <div className="sidebar__sep"></div>
      <div className="sidebar__footer">
        <strong>Phase 2 preview:</strong> ask follow-up questions in plain English. Joining the beta opens a chat panel above this report.
      </div>
    </aside>
  );
};

export { TopNav, SubHeader, ReportSidebar };
