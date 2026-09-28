repo: Wildlife-Protection-Solutions/wps.watch.ai_reporting
branch: main

## Last sync
date: 2026-09-24T15:19:26Z

### Updated in this project
- Rethemed WPS Portfolio to the wpsWatch tokens in ClientApp/src/styles.css (cream page, grey top bar, WPS gold, Segoe UI)
- Column filters follow the Excel-style autofilter in components/autofilter.jsx (sort, value list with counts, select all, clear)
- Ask answers carry the AskAnswer transparency block from Chat/ChatModels.cs (row count, window, filters, CSV)
- CSV export uses the formula-injection guard and BOM from Discovery/CsvExporter.cs and components/chat.jsx

## Screen map
| Screen | Repo files |
|---|---|
| Sign in | docs/ai-reports-design-brief-v4.md (same wpsWatch login, same roles) |
| Portfolio | ClientApp/src/styles.css, ClientApp/src/components/autofilter.jsx, ClientApp/src/components/chrome.jsx |
| AI | ClientApp/src/styles.css (KPI cards, .tbl), Reports/ReportResult.cs (columns + rows record) |
| AI detail | Reports/ReportResult.cs, Discovery/CsvExporter.cs |
| Ask | Chat/ChatModels.cs (AskAnswer), ClientApp/src/components/chat.jsx |
| Settings | Authorization/Roles.cs, Authorization/ReportOperations.cs |

## Notes
- The repo is the sibling wpsWatch AI Reports service, not the Portfolio app; it is used for visual tokens, table/filter/chat patterns, roles and the tabular record shape.
- Roles in the prototype (Admin / Viewer) map to Roles.SystemAdmin / Roles.Viewer.
