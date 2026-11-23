---
description: Check the API before a push: build, then the full test suite
---
Run `dotnet build FdeTracker.Api` and `dotnet test FdeTracker.Tests` (needs Docker for the Postgres container). Report passed/failed counts and the first failing test with its message.
A new migration must also apply cleanly on a fresh Postgres; say if you checked that. Never claim "works" without the output. Do not push.
