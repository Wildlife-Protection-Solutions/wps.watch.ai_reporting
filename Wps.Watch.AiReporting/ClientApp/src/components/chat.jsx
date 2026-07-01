// "Ask AI Reports" chat rail, wired to the real /api/ask endpoint (Claude → SQL →
// guarded execution). Every AI answer renders the brief-mandated transparency block:
// live row count + time window, the applied filters, the actual matching rows, and
// the exact SQL that produced them. The transparency fields come from the server's
// real query result, not from anything the model asserts.

import React, { useState, useEffect, useRef } from "react";
import { Icon } from "./icons";
import { ask } from "../api";

const SAMPLE_PROMPTS = [
  "Which organizations have the most active issues?",
  "Show regions ranked by number of sites",
  "Which orgs are below 70% online?",
  "How many cameras are deployed in total?",
];

function downloadCsv(answer) {
  const cols = answer.columns || [];
  const esc = (v) => {
    const s = v == null ? "" : String(v);
    return /[",\n\r]/.test(s) ? `"${s.replace(/"/g, '""')}"` : s;
  };
  const lines = [cols.map(esc).join(",")];
  for (const row of answer.rows || []) {
    lines.push(cols.map((c) => esc(row[c])).join(","));
  }
  const blob = new Blob(["﻿" + lines.join("\n")], { type: "text/csv" });
  const url = URL.createObjectURL(blob);
  const a = document.createElement("a");
  a.href = url;
  a.download = "ai-report-result.csv";
  a.click();
  URL.revokeObjectURL(url);
}

const cellStyle = {
  padding: "4px 8px",
  borderBottom: "1px solid var(--line-2)",
  fontSize: 11.5,
  fontVariantNumeric: "tabular-nums",
  textAlign: "left",
  whiteSpace: "nowrap",
};

const AnswerBlock = ({ answer, expanded, onToggleSql, onCsv }) => {
  const cols = answer.columns || [];
  return (
    <div className="ai-answer">
      <div className="ai-answer__row">
        <span className="ai-answer__chip">
          <Icon name="circle" size={10} /> <strong>{answer.count.toLocaleString("en-US")}</strong> rows
          {answer.truncated ? "+" : ""}
        </span>
        <span className="ai-answer__chip">
          <Icon name="calendar" size={10} /> {answer.window}
        </span>
      </div>

      {answer.filters && answer.filters.length > 0 && (
        <div className="ai-answer__filters">
          {answer.filters.map((f, i) => (
            <span key={i} className="ai-answer__filter">
              {f}
            </span>
          ))}
        </div>
      )}

      {cols.length > 0 && answer.rows && answer.rows.length > 0 && (
        <div className="ai-answer__sources" style={{ overflowX: "auto" }}>
          <table style={{ borderCollapse: "collapse", width: "100%" }}>
            <thead>
              <tr>
                {cols.map((c) => (
                  <th
                    key={c}
                    style={{
                      ...cellStyle,
                      fontWeight: 700,
                      color: "var(--ink-3)",
                      textTransform: "uppercase",
                      fontSize: 10,
                      letterSpacing: "0.4px",
                    }}
                  >
                    {c}
                  </th>
                ))}
              </tr>
            </thead>
            <tbody>
              {answer.rows.map((row, ri) => (
                <tr key={ri}>
                  {cols.map((c) => (
                    <td key={c} style={cellStyle}>
                      {formatCell(row[c])}
                    </td>
                  ))}
                </tr>
              ))}
            </tbody>
          </table>
          {answer.moreCount > 0 && (
            <div style={{ padding: "6px 8px", fontSize: 11, color: "var(--ink-3)" }}>
              + {answer.moreCount.toLocaleString("en-US")} more row
              {answer.moreCount === 1 ? "" : "s"} (showing first {answer.rows.length})
            </div>
          )}
        </div>
      )}

      <div className="ai-answer__actions">
        {answer.sql && (
          <button className="ai-answer__btn" onClick={onToggleSql}>
            <Icon name="code" size={11} /> {expanded ? "Hide" : "Show"} SQL
          </button>
        )}
        {cols.length > 0 && (
          <button className="ai-answer__btn" onClick={() => onCsv(answer)}>
            <Icon name="download" size={11} /> CSV
          </button>
        )}
      </div>

      {answer.sql && expanded && <pre className="ai-answer__sql">{answer.sql}</pre>}
    </div>
  );
};

function formatCell(v) {
  if (v == null) return "—";
  if (typeof v === "number") return v.toLocaleString("en-US");
  return String(v);
}

const ChatBubble = ({ msg, expandedSql, setExpandedSql, idx }) => {
  if (msg.role === "user") {
    return (
      <div className="chat-msg chat-msg--user">
        <div className="chat-bubble chat-bubble--user">{msg.text}</div>
      </div>
    );
  }
  return (
    <div className="chat-msg chat-msg--ai">
      <div className="chat-bubble chat-bubble--ai">
        <div className="chat-bubble__text">{msg.text}</div>
        {msg.answer && (
          <AnswerBlock
            answer={msg.answer}
            expanded={expandedSql === idx}
            onToggleSql={() => setExpandedSql(expandedSql === idx ? null : idx)}
            onCsv={downloadCsv}
          />
        )}
      </div>
    </div>
  );
};

export const ChatPanel = ({ onClose }) => {
  const [messages, setMessages] = useState([]);
  const [input, setInput] = useState("");
  const [expandedSql, setExpandedSql] = useState(null);
  const [isTyping, setIsTyping] = useState(false);
  const scrollRef = useRef(null);

  useEffect(() => {
    if (scrollRef.current) {
      scrollRef.current.scrollTop = scrollRef.current.scrollHeight;
    }
  }, [messages, expandedSql, isTyping]);

  const send = async (text) => {
    if (!text || !text.trim() || isTyping) return;

    // History = the conversation so far (text turns only), before this question.
    const history = messages
      .filter((m) => m.text)
      .map((m) => ({ role: m.role, text: m.text }));

    setMessages((m) => [...m, { role: "user", text }]);
    setInput("");
    setIsTyping(true);

    try {
      const body = await ask(text, history);
      setMessages((m) => [
        ...m,
        { role: "assistant", text: body.text, answer: body.answer || null },
      ]);
    } catch (e) {
      const msg =
        e && e.code === "chat_not_configured"
          ? "The chat isn't configured yet — an Anthropic API key needs to be set on the server (Anthropic:ApiKey). The reports table works without it."
          : `Sorry, I couldn't answer that. ${e && e.message ? e.message : ""}`;
      setMessages((m) => [...m, { role: "assistant", text: msg, answer: null }]);
    } finally {
      setIsTyping(false);
    }
  };

  return (
    <aside className="chatpanel">
      <header className="chatpanel__head">
        <div className="chatpanel__title">
          <Icon name="sparkles" size={14} />
          <span>Ask AI Reports</span>
          <span className="chatpanel__beta">BETA</span>
        </div>
        <div className="chatpanel__actions">
          <button className="btn btn--subtle btn--icon" title="History">
            <Icon name="chat" size={14} />
          </button>
          <button className="btn btn--subtle btn--icon" title="Close" onClick={onClose}>
            <Icon name="x" size={14} />
          </button>
        </div>
      </header>

      <div className="chatpanel__transparency">
        <Icon name="check-circle" size={12} />
        Every answer shows its rows, time window, filters, and (if you want) the SQL.
      </div>

      <div className="chatpanel__scroll" ref={scrollRef}>
        {messages.length === 0 && (
          <div className="chat-msg chat-msg--ai">
            <div className="chat-bubble chat-bubble--ai">
              <div className="chat-bubble__text">
                Ask anything about your fleet in plain English. I'll run a read-only query,
                scoped to your access, and show you the rows and the SQL behind every answer.
              </div>
            </div>
          </div>
        )}
        {messages.map((m, i) => (
          <ChatBubble
            key={i}
            msg={m}
            idx={i}
            expandedSql={expandedSql}
            setExpandedSql={setExpandedSql}
          />
        ))}
        {isTyping && (
          <div className="chat-msg chat-msg--ai">
            <div className="chat-bubble chat-bubble--ai chat-bubble--typing">
              <span className="dot1"></span>
              <span className="dot2"></span>
              <span className="dot3"></span>
            </div>
          </div>
        )}
      </div>

      <div className="chatpanel__suggestions">
        {SAMPLE_PROMPTS.map((p) => (
          <button key={p} className="chatpanel__chip" onClick={() => send(p)}>
            {p}
          </button>
        ))}
      </div>

      <form
        className="chatpanel__compose"
        onSubmit={(e) => {
          e.preventDefault();
          send(input);
        }}
      >
        <textarea
          rows={2}
          placeholder="Ask anything about your fleet, in plain English…"
          value={input}
          onChange={(e) => setInput(e.target.value)}
          onKeyDown={(e) => {
            if (e.key === "Enter" && !e.shiftKey) {
              e.preventDefault();
              send(input);
            }
          }}
        />
        <div className="chatpanel__compose-row">
          <div className="chatpanel__compose-hint">
            Scoped to <strong>your access</strong> · Read-only
          </div>
          <button type="submit" className="btn btn--primary btn--sm" disabled={isTyping}>
            <Icon name="sparkles" size={12} /> Ask
          </button>
        </div>
      </form>
    </aside>
  );
};
