# simple-agent

`simple-agent` is a small .NET 8 AI agent example that shows how to run an LLM-driven decision loop behind an ASP.NET Core API and exchange work/results over RabbitMQ.

## What this currently covers

- **Agent loop orchestration**: the agent asks the LLM for the next action, validates it, executes the allowed tool, and finishes with a final answer.
- **Constrained actions**: the current tool surface is intentionally small and only supports `get_complaints` and `finish`.
- **Structured tool execution**: the agent executes tools through a CQRS adapter instead of calling handlers directly.
- **Permission checks**: every tool action is checked before execution.
- **RabbitMQ background processing**:
  - durable `agent-jobs` queue
  - durable `agent-responses` queue
  - hosted background worker
  - manual ack/nack with requeue on failure
  - prefetch count of 1 for simple sequential processing
- **Request-response API flow**:
  - `POST /agent/run` publishes an agent job
  - API waits up to 30 seconds for a correlated response
  - pending requests are matched with `TaskCompletionSource` + `ConcurrentDictionary`
- **Observability and tracing**:
  - correlation ID per agent run
  - structured `ILogger` logging
  - step-level tracing for LLM decisions, validation, CQRS dispatch, and tool results
  - timing for total run duration, step duration, LLM calls, and CQRS execution
  - run summary output with success/failure metadata
- **Agent run persistence**:
  - `AgentRun` aggregate root with child `AgentStep` records
  - run state transitions handled in the domain (`AddStep`, `Complete`, `Fail`)
  - SQLite-backed persistence through `Pokok.BuildingBlocks.Persistence`
  - execution history stored per correlation ID
- **Safe replay for failed runs**:
  - replay creates a brand-new `AgentRun` with a new correlation ID
  - replay runs point back to the original run with `ReplayOfRunId`
  - only failed runs can be replayed
  - replay is rejected when recorded actions are not marked replayable and idempotent
- **Layered solution structure**:
  - `Agent`: hosted worker entrypoint / composition root
  - `SimpleAgent.Api`: ASP.NET Core API publisher and response listener
  - `SimpleAgent.Domain`: core models and persisted agent run aggregate
  - `SimpleAgent.Application`: agent orchestration, use-case logic, abstractions
  - `SimpleAgent.Infrastructure`: OpenAI client, CQRS adapter, correlation context, permission service, persistence

## Current flow

1. The API receives `POST /agent/run`.
2. The API generates a correlation ID, stores a pending `TaskCompletionSource`, and publishes an `AgentJobMessage` to `agent-jobs`.
3. The worker consumes the job, creates a scoped agent execution, and runs the agent.
4. The agent sends the current goal/history to the LLM.
5. The LLM returns a JSON decision.
6. The decision is parsed and validated against allowed actions.
7. If the action is `get_complaints`, the request is mapped to a CQRS query and dispatched.
8. The tool result is added to history until the agent finishes.
9. The worker persists the `AgentRun` and each `AgentStep` as the execution progresses.
10. The worker publishes an `AgentResultMessage` to the reply queue (`agent-responses`).
11. The API response listener matches the correlation ID, completes the pending request, and the HTTP endpoint returns the result or a timeout/error response.

Replay flow:

1. The API receives `POST /agent/runs/{agentRunId}/replay`.
2. The replay command loads the original run and verifies it is failed.
3. The failed run's recorded actions are checked against replay safety metadata.
4. A brand-new replay run is created with a new correlation ID and `ReplayOfRunId` set to the original run.
5. A new RabbitMQ job is published for the replay run.
6. The original run remains immutable and the replay chain stays auditable.

## Message contract

Request messages in `agent-jobs` are JSON serialized using this shape:

```json
{
  "goal": "Generate complaint summary for April",
  "correlationId": "9f1f0ffb-6f34-45f8-8e95-1a4dc9f9d1b3",
  "replyTo": "agent-responses",
  "replayOfRunId": null
}
```

Response messages in `agent-responses` use this shape:

```json
{
  "correlationId": "9f1f0ffb-6f34-45f8-8e95-1a4dc9f9d1b3",
  "success": true,
  "result": "Complaint summary...",
  "error": null
}
```

- `goal`: the natural-language task for the agent to execute
- `correlationId`: trace identifier propagated through the worker, agent, LLM, CQRS, and logs
- `replyTo`: optional response queue the worker should publish back to
- `replayOfRunId`: original run ID when the job was queued as a replay
- `success`: whether the worker completed the agent run successfully
- `result`: final agent output when successful
- `error`: failure message when unsuccessful

## Queue behavior

- Queue name: `agent-jobs`
- Response queue: `agent-responses`
- Durable queue declaration
- Manual acknowledgment on success
- Negative acknowledgment with `requeue: true` on failure
- `BasicQos` prefetch count set to `1`

## Project structure

```text
Agent/                      Hosted worker entrypoint, queue consumer, and composition root
SimpleAgent.Api/            ASP.NET Core API, request publisher, response listener
SimpleAgent.Domain/         Agent state, decisions, errors, run summary, AgentRun aggregate
SimpleAgent.Application/    Agent loop, action definitions, abstractions, queries
SimpleAgent.Infrastructure/ OpenAI client, CQRS tool adapter, permissions, correlation context, EF persistence
```

## Run locally

Set `OPENAI_API_KEY`, configure RabbitMQ settings, then run the worker and API separately:

```powershell
cd Agent
dotnet run
```

```powershell
cd SimpleAgent.Api
dotnet run
```

Call the API:

```http
POST /agent/run
Content-Type: application/json

{
  "goal": "Generate complaint summary for April"
}
```

Replay a failed run:

```http
POST /agent/runs/{agentRunId}/replay
```

RabbitMQ configuration is read from the `RabbitMQ` configuration section, for example via environment variables:

```text
RabbitMQ__HostName=localhost
RabbitMQ__Port=5672
RabbitMQ__UserName=guest
RabbitMQ__Password=guest
```

Agent run persistence uses the `ConnectionStrings__AgentRuns` setting and falls back to a local SQLite file when the connection string is not provided:

```text
ConnectionStrings__AgentRuns=Data Source=agent-runs.db
```

## Build

```powershell
dotnet build simple-agent.sln
```

## Notes

- Prompt and raw LLM response logging is truncated to reduce the risk of leaking sensitive data.
- The worker creates the SQLite database automatically on startup if it does not exist yet.
- Replay requests return structured validation errors for missing runs, invalid run status, and unsafe recorded actions.
- The project is intentionally simple: it demonstrates the mechanics of an AI agent without introducing external observability tooling or a large action catalog.
