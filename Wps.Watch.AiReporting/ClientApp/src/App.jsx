import React from "react";
import { TopNav, SubHeader, ReportSidebar } from "./components/chrome";
import { ReportPage } from "./components/report";
import { ChatPanel } from "./components/chat";
import {
  TweaksPanel,
  TweakSection,
  TweakRadio,
  TweakToggle,
  useTweaks,
} from "./components/tweaks-panel";
import { useReportData } from "./useReportData";

// One production combination of the prototype's layout dimensions. The Tweaks panel
// is kept during the pilot so the warden's office can pick a final combo; chat is
// opt-in (off by default) since it calls the Claude-backed /api/ask endpoint.
const TWEAK_DEFAULTS = {
  summary: "cards",
  filterUi: "chips",
  density: "compact",
  groupByRegion: true,
  showChartStrip: true,
  chatPanel: false,
};

export function App() {
  const [activeReport, setActiveReport] = React.useState("region-site-totals");
  const [tweaks, setTweak] = useTweaks(TWEAK_DEFAULTS);
  const chatOpen = !!tweaks.chatPanel;
  const setChatOpen = (v) => setTweak("chatPanel", v);

  const { rows, status, error } = useReportData(activeReport);

  return (
    <div data-screen-label="01 Reports — Region & Site Totals">
      <TopNav />
      <SubHeader />
      <div className={"layout" + (chatOpen ? " layout--with-chat" : "")}>
        <ReportSidebar activeId={activeReport} setActiveId={setActiveReport} />
        {status === "ready" ? (
          <ReportPage
            rows={rows}
            tweaks={tweaks}
            chatOpen={chatOpen}
            setChatOpen={setChatOpen}
          />
        ) : (
          <main style={{ padding: "48px 24px", maxWidth: 1480 }}>
            <div
              style={{
                padding: "40px",
                textAlign: "center",
                color: "var(--ink-3)",
                background: "var(--bg-card)",
                border: "1px solid var(--line-1)",
                borderRadius: "var(--radius-3)",
                fontSize: 14,
              }}
            >
              {status === "error"
                ? `Couldn't load the report — ${error}`
                : "Loading Region & Site Totals…"}
            </div>
          </main>
        )}
        {chatOpen && <ChatPanel onClose={() => setChatOpen(false)} />}
      </div>

      <TweaksPanel title="Tweaks">
        <TweakSection label="Summary" />
        <TweakRadio
          label="Layout"
          value={tweaks.summary}
          onChange={(v) => setTweak("summary", v)}
          options={[
            { value: "cards", label: "KPI cards" },
            { value: "strip", label: "Totals" },
          ]}
        />
        <TweakToggle
          label="Top-issues chart"
          value={tweaks.showChartStrip}
          onChange={(v) => setTweak("showChartStrip", v)}
        />

        <TweakSection label="Filter UI" />
        <TweakRadio
          label="Layout"
          value={tweaks.filterUi}
          onChange={(v) => setTweak("filterUi", v)}
          options={[
            { value: "chips", label: "Chips" },
            { value: "rail", label: "Rail" },
            { value: "modal", label: "Modal" },
          ]}
        />

        <TweakSection label="Table" />
        <TweakRadio
          label="Density"
          value={tweaks.density}
          onChange={(v) => setTweak("density", v)}
          options={[
            { value: "compact", label: "Table" },
            { value: "roomy", label: "Cards" },
          ]}
        />
        <TweakToggle
          label="Group by region"
          value={tweaks.groupByRegion}
          onChange={(v) => setTweak("groupByRegion", v)}
        />

        <TweakSection label="Phase 2 preview" />
        <TweakToggle
          label="AI chat panel"
          value={tweaks.chatPanel}
          onChange={(v) => setTweak("chatPanel", v)}
        />
      </TweaksPanel>
    </div>
  );
}
