import type { EngineInterface, Register, SessionRateLimit } from 'claude-code'

type SessionState = 'idle' | 'running' | 'waiting' | 'ended'

// These tools block on the user until they return; everything else waits only while a permission dialog is open
const QUESTION_TOOLS = new Set(['AskUserQuestion', 'ExitPlanMode'])
const HEARTBEAT_MS = 60_000

let turnActive = false
let updatedAt = ''
// tool_use_id -> tool name, for calls currently waiting on the user
const waitingOn = new Map<string, string>()

const currentState = (): SessionState => (waitingOn.size > 0 ? 'waiting' : turnActive ? 'running' : 'idle')

const homeDir = async ($: EngineInterface) => (await $.env.get('USERPROFILE')) ?? (await $.env.get('HOME'))

const writeUsage = async ($: EngineInterface, rateLimits: SessionRateLimit[]) => {
  if (rateLimits.length === 0) return
  const home = await homeDir($)
  if (home === undefined) return
  const windows = Object.fromEntries(
    rateLimits.map(r => [r.kind, { percentUsed: r.percentUsed, resetsAt: r.resetsAt ?? null }]),
  )
  const updatedAt = new Date(await $.clock.now()).toISOString()
  await $.fs.write(`${home}/.agent-usage/claude.json`, JSON.stringify({ updatedAt, ...windows }, null, 2))
}

// updatedAt marks the last state change; heartbeatAt alone moves on the timer so the reader can drop dead sessions
const writeSession = async ($: EngineInterface, state: SessionState, sessionId?: string, touch = true) => {
  const home = await homeDir($)
  if (home === undefined) return
  const id = sessionId ?? (await $.session.id())
  const now = new Date(await $.clock.now()).toISOString()
  if (touch || updatedAt === '') updatedAt = now
  const detail = state === 'waiting' ? ([...waitingOn.values()].at(-1) ?? null) : null
  const record = { sessionId: id, cwd: await $.session.cwd(), state, detail, updatedAt, heartbeatAt: now }
  await $.fs.write(`${home}/.agent-usage/sessions/${id}.json`, JSON.stringify(record, null, 2))
}

const heartbeat = ($: EngineInterface) => writeSession($, currentState(), undefined, false)

const stopWaiting = async ($: EngineInterface, ...keys: string[]) => {
  const removed = keys.filter(k => waitingOn.delete(k))
  if (removed.length > 0) await writeSession($, currentState())
}

export const register: Register = on => {
  on('session.start', async ($, e, next) => {
    const result = await next(e)
    await writeUsage($, (await $.session.usage()).rateLimits)
    // Not gated on isInteractive: the VS Code extension runs through the SDK and never got a heartbeat
    await writeSession($, currentState())
    $.clock.every(HEARTBEAT_MS, () => heartbeat($))
    return result
  })

  on('session.measure', async ($, e, next) => {
    if (e.changed.includes('rateLimits')) await writeUsage($, e.rateLimits)
    return next(e)
  })

  on('turn.start', async ($, e, next) => {
    turnActive = true
    await writeSession($, currentState())
    return next(e)
  })

  on('turn.complete', async ($, e, next) => {
    if (e.agentId === undefined) {
      turnActive = false
      waitingOn.clear()
      await writeSession($, currentState())
    }
    return next(e)
  })

  // tool.check answers 'ask' before the mode (e.g. auto) settles it, so only PermissionRequest means a dialog is shown.
  // It carries no tool_use_id, hence the key by tool name, cleared when that tool's call resolves.
  on('classic.PermissionRequest', async ($, e, next) => {
    waitingOn.set(`perm:${e.tool_name}`, e.tool_name)
    await writeSession($, currentState())
    return next(e)
  })

  on('tool.call', async ($, e, next) => {
    if (QUESTION_TOOLS.has(e.tool)) {
      waitingOn.set(e.tool_use_id, e.tool)
      await writeSession($, currentState())
    }
    try {
      return await next(e)
    } finally {
      await stopWaiting($, e.tool_use_id, `perm:${e.tool}`)
    }
  })

  on('session.end', async ($, e, next) => {
    turnActive = false
    waitingOn.clear()
    await writeSession($, 'ended', e.sessionId)
    return next(e)
  })
}
