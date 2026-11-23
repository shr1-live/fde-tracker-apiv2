---
description: Is the latest API change live? Compare main, Render and the health check
---
1. `git fetch`, then say whether the current branch is merged into `origin/main`.
2. Call `https://fde-tracker-apiv2.onrender.com/health` and report the status and the time it took (the free server sleeps when idle).
3. If a new route was added, call it without a token: 401 means it exists, 404 means the live API is older than main.
4. Report: merged yes/no, live yes/no, API up yes/no. If something is missing, say which step and what to do. Never merge or push without being asked.
