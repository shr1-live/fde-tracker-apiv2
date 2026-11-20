# Day 3: real-model run of the task assistant

Model: qwen2.5:3b (local, Ollama), prompt tasks-v1, temperature 0, through the real API and a real Postgres database. 5 runs of the same scenario, 2026-11-18.

| Step | What is checked | Passed | Average time |
|---|---|---|---|
| create | Create a task with title, due date and priority from one sentence | 5 of 5 | 25 s |
| list | List the person's tasks and name them in the reply | 5 of 5 | 18 s |
| update | Mark a task done, found by its title | 5 of 5 | 30 s |
| delete asks first | Asked to delete: a confirmation question is created and the task is still there | 5 of 5 | 45 s |
| confirm deletes | Saying yes deletes it | 5 of 5 | 0 s |
| cancel keeps it | Saying no keeps it | 5 of 5 | 51 s |
| **whole flow** | every step in the run | **5 of 5** | |

Checks are made on the database (the task list), not on what the model says.

**What the model got wrong, and what stopped it.** In 5 of 5 runs the model first asked to delete with an id it made up. The server refused it ("invalid"), the refusal went back to the model, and it then looked the task up and asked correctly. In no run was anything deleted without the person's yes.

**A real flaw it shows.** In 2 of 5 runs the model also made a change nobody asked for (an update_task that succeeded) before it asked to delete. The task was then deleted after the yes, so the end state was right, but an unrequested change is a mistake. Nothing in the code stops the model from updating a person's own task, because updates are allowed without confirmation.

**Earlier result.** The first real-model run (2026-11-16) failed the delete flow (made-up id, then an update instead). This run passes the flow every time; the change is not isolated to one cause. A one-off check just before this run also saw the model update a task when asked to delete it, and no confirmation was offered that time. A 3-billion-parameter model is not fully reliable here; a stronger hosted model is still the right choice for real use.

## Every run

| Run | create | list | update | delete asks first | confirm deletes | cancel keeps it |
|---|---|---|---|---|---|---|
| 1 | ok | ok | ok | ok | ok | ok |
| 2 | ok | ok | ok | ok | ok | ok |
| 3 | ok | ok | ok | ok | ok | ok |
| 4 | ok | ok | ok | ok | ok | ok |
| 5 | ok | ok | ok | ok | ok | ok |

Raw results, with the tool calls made in each step: [day3-local-results.json](day3-local-results.json).
