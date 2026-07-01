import React from 'react';

// Small inline SVG icon helpers - simple stroke icons
const Icon = ({ name, size = 16, stroke = 1.6, ...rest }) => {
  const s = size;
  const common = {
    width: s, height: s, viewBox: "0 0 24 24", fill: "none",
    stroke: "currentColor", strokeWidth: stroke, strokeLinecap: "round", strokeLinejoin: "round",
    ...rest,
  };
  switch (name) {
    case "search": return (
      <svg {...common}><circle cx="11" cy="11" r="7"/><path d="m20 20-3.5-3.5"/></svg>
    );
    case "chevron-down": return (
      <svg {...common}><path d="m6 9 6 6 6-6"/></svg>
    );
    case "chevron-right": return (
      <svg {...common}><path d="m9 18 6-6-6-6"/></svg>
    );
    case "chevron-left": return (
      <svg {...common}><path d="m15 18-6-6 6-6"/></svg>
    );
    case "chevron-up": return (
      <svg {...common}><path d="m6 15 6-6 6 6"/></svg>
    );
    case "arrow-up-down": return (
      <svg {...common}><path d="m7 15 5 5 5-5"/><path d="m7 9 5-5 5 5"/></svg>
    );
    case "filter": return (
      <svg {...common}><path d="M3 5h18"/><path d="M6 12h12"/><path d="M10 19h4"/></svg>
    );
    case "x": return (
      <svg {...common}><path d="M18 6 6 18"/><path d="m6 6 12 12"/></svg>
    );
    case "download": return (
      <svg {...common}><path d="M12 3v12"/><path d="m7 10 5 5 5-5"/><path d="M5 21h14"/></svg>
    );
    case "star": return (
      <svg {...common}><path d="M12 3l2.6 6.3 6.8.5-5.2 4.4 1.6 6.6L12 17.3 6.2 20.8l1.6-6.6L2.6 9.8l6.8-.5z"/></svg>
    );
    case "star-fill": return (
      <svg {...common} fill="currentColor" stroke="none"><path d="M12 3l2.6 6.3 6.8.5-5.2 4.4 1.6 6.6L12 17.3 6.2 20.8l1.6-6.6L2.6 9.8l6.8-.5z"/></svg>
    );
    case "calendar": return (
      <svg {...common}><rect x="3" y="5" width="18" height="16" rx="2"/><path d="M3 10h18"/><path d="M8 3v4"/><path d="M16 3v4"/></svg>
    );
    case "share": return (
      <svg {...common}><circle cx="18" cy="5" r="3"/><circle cx="6" cy="12" r="3"/><circle cx="18" cy="19" r="3"/><path d="m8.6 13.5 6.8 4"/><path d="m15.4 6.5-6.8 4"/></svg>
    );
    case "more": return (
      <svg {...common}><circle cx="5" cy="12" r="1.4" fill="currentColor"/><circle cx="12" cy="12" r="1.4" fill="currentColor"/><circle cx="19" cy="12" r="1.4" fill="currentColor"/></svg>
    );
    case "alert": return (
      <svg {...common}><path d="M12 3 2 21h20Z"/><path d="M12 10v5"/><circle cx="12" cy="18" r="0.6" fill="currentColor"/></svg>
    );
    case "alert-circle": return (
      <svg {...common}><circle cx="12" cy="12" r="9"/><path d="M12 8v5"/><circle cx="12" cy="16.5" r="0.6" fill="currentColor"/></svg>
    );
    case "check": return (
      <svg {...common}><path d="m5 12 5 5 9-11"/></svg>
    );
    case "check-circle": return (
      <svg {...common}><circle cx="12" cy="12" r="9"/><path d="m8 12 3 3 5-7"/></svg>
    );
    case "wifi": return (
      <svg {...common}><path d="M5 13a10 10 0 0 1 14 0"/><path d="M8.5 16.5a5 5 0 0 1 7 0"/><circle cx="12" cy="20" r="0.6" fill="currentColor"/></svg>
    );
    case "wifi-off": return (
      <svg {...common}><path d="M3 3l18 18"/><path d="M5 13a10 10 0 0 1 11.5-1.9"/><path d="M8.5 16.5a5 5 0 0 1 5.4-1"/><circle cx="12" cy="20" r="0.6" fill="currentColor"/></svg>
    );
    case "camera": return (
      <svg {...common}><path d="M4 8h3l2-3h6l2 3h3v11H4Z"/><circle cx="12" cy="13" r="3.5"/></svg>
    );
    case "users": return (
      <svg {...common}><circle cx="9" cy="8" r="3.5"/><path d="M3 20c0-3.3 2.7-6 6-6s6 2.7 6 6"/><circle cx="17" cy="8" r="2.5"/><path d="M21 20c0-2.5-1.5-4.5-3.5-5.3"/></svg>
    );
    case "globe": return (
      <svg {...common}><circle cx="12" cy="12" r="9"/><path d="M3 12h18"/><path d="M12 3a14 14 0 0 1 0 18"/><path d="M12 3a14 14 0 0 0 0 18"/></svg>
    );
    case "map": return (
      <svg {...common}><path d="m3 6 6-3 6 3 6-3v15l-6 3-6-3-6 3Z"/><path d="M9 3v15"/><path d="M15 6v15"/></svg>
    );
    case "report": return (
      <svg {...common}><path d="M6 3h9l4 4v14H6Z"/><path d="M14 3v5h5"/><path d="M9 13h6"/><path d="M9 17h4"/></svg>
    );
    case "sparkles": return (
      <svg {...common}><path d="M12 4v3M12 17v3M4 12h3M17 12h3"/><path d="m6 6 1.5 1.5M16.5 16.5 18 18M6 18l1.5-1.5M16.5 7.5 18 6"/></svg>
    );
    case "chat": return (
      <svg {...common}><path d="M21 12a8 8 0 0 1-12 7l-5 1 1-4a8 8 0 1 1 16-4Z"/></svg>
    );
    case "sun": return (
      <svg {...common}><circle cx="12" cy="12" r="4"/><path d="M12 2v2M12 20v2M4 12H2M22 12h-2M5 5l1.5 1.5M17.5 17.5 19 19M5 19l1.5-1.5M17.5 6.5 19 5"/></svg>
    );
    case "user": return (
      <svg {...common}><circle cx="12" cy="9" r="4"/><path d="M3 21c0-4.4 4-8 9-8s9 3.6 9 8"/></svg>
    );
    case "save": return (
      <svg {...common}><path d="M5 3h11l3 3v15H5Z"/><path d="M8 3v6h8V3"/><rect x="8" y="13" width="8" height="6"/></svg>
    );
    case "settings": return (
      <svg {...common}><circle cx="12" cy="12" r="3"/><path d="M20 12a8 8 0 0 0-.4-2.5l2-1.5-2-3.5-2.4 1a8 8 0 0 0-4.2-2.4L12.5 1h-1l-.5 2.1a8 8 0 0 0-4.2 2.4l-2.4-1-2 3.5 2 1.5A8 8 0 0 0 4 12c0 .9.1 1.7.4 2.5l-2 1.5 2 3.5 2.4-1a8 8 0 0 0 4.2 2.4L11.5 23h1l.5-2.1a8 8 0 0 0 4.2-2.4l2.4 1 2-3.5-2-1.5c.3-.8.4-1.6.4-2.5Z"/></svg>
    );
    case "refresh": return (
      <svg {...common}><path d="M21 12a9 9 0 1 1-3-6.7"/><path d="M21 4v5h-5"/></svg>
    );
    case "trend-up": return (
      <svg {...common}><path d="M3 18 9 12l4 4 8-9"/><path d="M14 5h7v7"/></svg>
    );
    case "trend-down": return (
      <svg {...common}><path d="M3 6l6 6 4-4 8 9"/><path d="M14 19h7v-7"/></svg>
    );
    case "circle": return (
      <svg {...common}><circle cx="12" cy="12" r="9"/></svg>
    );
    case "code": return (
      <svg {...common}><path d="m8 6-6 6 6 6"/><path d="m16 6 6 6-6 6"/></svg>
    );
    default: return null;
  }
};

export { Icon };
