---
paths:
  - "src/Admin/**"
---

# Admin app (Angular)

Decisions: ADR 012. Layout of the folders that exist: `docs/repository-layout.md`.

## Structure and names

Follow the angular.dev style guide. Before adding a feature or a new kind of file, show the folder tree and wait for
an ok.

- By feature under `src/app/` (`scenarios/`, later `parking-lot/`), never by type: no `components/`, `services/`,
  `models/` at the top. Inside a feature: one folder per component (`.ts` / `.html` / `.scss`), `data-access/` for
  HTTP, `model/` for its types, plus folders named after what they hold (`catalog/`).
- App-wide, non-UI things in `core/` (e.g. `core/runtime-config`).
- `app.ts`, `app.config.ts` (Angular providers), `app.routes.ts` stay in the root, as the CLI generates them.
- One concept per file. No generic names: no `config.ts`, `utils.ts`, `helpers.ts`, no `index.ts` barrels. Name
  after what it is: `RuntimeConfig`, `SimulatorsClient`, `ResultCheck`, `scenario-catalog.ts`.
- HTTP clients are `*Client`, as in the backend.
- "Teamtailor" never appears here (CLAUDE.md, Naming): the "ATS simulator", `atsSimulatorUrl`.

## Angular

- Standalone, zoneless: state the template reads is a `signal` / `computed`; update with a new array or object,
  never mutate in place.
- `input()` / `output()`, built-in control flow (`@if`, `@for` with `track`, `@switch`, `@let`).
- Reads through `httpResource`, commands (POST / PATCH / PUT / DELETE) through `HttpClient`.
- Addresses come from `RUNTIME_CONFIG` (`public/config.json`, checked before bootstrap), never from
  `environment.ts` or literals in code. A new key goes into `RuntimeConfig`, `RUNTIME_CONFIG_KEYS` and `config.json`.
- Angular Material for UI; colours from `--mat-sys-*` variables, never hard-coded.

## Checks

- `npm run build` in `src/Admin/TalentSync.Admin.Web` before saying it works; CI runs `npm ci` and the build.
- Tests only for the parking lot calls (merge of both workers, one worker failing); none for the Scenarios page
  (ADR 012).
- Worker API types are generated from the committed OpenAPI documents, never written by hand (ADR 012).
- `ng serve` started in the background outlives its shell: stop it by the process listening on port 4200.
