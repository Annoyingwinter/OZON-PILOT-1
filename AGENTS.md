## Project UI Rules

When changing OZON-PILOT UI, review it from a first-time operator's perspective before finishing.

- Default entry must follow the real setup path: prepare 1688 login and Ozon API first, then operate.
- Keep primary tabs ordered by user workflow, not internal modules.
- Do not expose logs, paths, API internals, or diagnostic wording on the primary operation surface unless the user asks for advanced mode.
- Operation screens should answer three questions immediately: what is ready, what should I click now, and where do I see results.
- After each meaningful UI change, run two separate review agents before final response:
  - Practicality review: first-time operator flow, clarity, fewer mistakes, next action.
  - Visual review: beauty, modernity, spacing, hierarchy, and whether it still feels like an old engineering tool.
- Revise immediately when either review finds a must-fix issue.
