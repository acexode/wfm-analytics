# WP02 web foundation report

Status: accepted as a synthetic development foundation on 13 September 2026

The React and TypeScript dashboard consumes the real daily report route through the local Vite proxy. It shows source freshness first, computes eligible-time-weighted coverage, preserves unknown and conflict categories, and renders missing operational output as unavailable with its reason. Loading, HTTP error, invalid-response, empty-roster, stale, unknown-freshness, and retry states are explicit. A permanent synthetic-data notice prevents the reference slice from being mistaken for employee collection.

Actual checks passed with React 19.3, TypeScript 7.0.2, Vite 8.3, and the bundled Node.js runtime: four report parsing/formatting unit tests, TypeScript checking, production Vite build, direct API request, request through the development proxy, and live browser review at 1280 by 720 and 390 by 844. The mobile layout reflows the sidebar, controls, cards, and table region without page-level horizontal overflow. The reviewed report displayed the seeded 83.3% coverage, 10-minute unknown interval, and unavailable output reason.

Limitations: this is one team-day overview with a fixed synthetic development manager. Production navigation, company identity, larger-roster paging or virtualization, operational imports, audit views, and formal accessibility testing remain later work.
