import type { On } from 'claude-code'
import { expect, mock, test } from 'claude-code/testing'

const context = { totalTokens: 0, windowSize: 200000 } as never

test('writes rate limits when they change', async ($, on) => {
  const writes: { path: string; text: string }[] = []
  mock.env(on, { USERPROFILE: 'C:/Users/test' })
  mock.clock(on, { now: Date.UTC(2026, 9, 2, 1, 0, 0) })
  on('fs.write', ($, e) => {
    writes.push(e)
    return null as never
  })
  on('session.measure', ($, e) => ({ changed: e.changed }))

  await $.session.measure({
    context,
    rateLimits: [
      { kind: 'five_hour', percentUsed: 12.5, resetsAt: '2026-10-02T05:00:00Z' },
      { kind: 'seven_day', percentUsed: 40 },
    ],
    changed: ['rateLimits'],
  })

  expect(writes.length).toBe(1)
  expect(writes[0]?.path.replaceAll('\\', '/')).toBe('C:/Users/test/.agent-usage/claude.json')
  expect(JSON.parse(writes[0]?.text ?? '')).toEqual({
    updatedAt: '2026-10-02T01:00:00.000Z',
    five_hour: { percentUsed: 12.5, resetsAt: '2026-10-02T05:00:00Z' },
    seven_day: { percentUsed: 40, resetsAt: null },
  })
})

test('skips when only context changed or no readings', async ($, on) => {
  let count = 0
  mock.env(on, { USERPROFILE: 'C:/Users/test' })
  mock.clock(on)
  on('fs.write', () => {
    count++
    return null as never
  })
  on('session.measure', ($, e) => ({ changed: e.changed }))
  await $.session.measure({ context, rateLimits: [{ kind: 'five_hour', percentUsed: 1 }], changed: ['context'] })
  await $.session.measure({ context, rateLimits: [], changed: ['rateLimits'] })
  expect(count).toBe(0)
})

const sessionWrites = (on: On) => {
  const records: Record<string, unknown>[] = []
  mock.env(on, { USERPROFILE: 'C:/Users/test' })
  const clock = mock.clock(on, { now: Date.UTC(2026, 9, 2, 1, 0, 0) })
  on('session.id', () => ({ value: '0f8a2c1e-1111-4222-8333-944455556666' }) as never)
  on('session.cwd', () => ({ value: 'D:/work/proj' }) as never)
  on('fs.write', ($, e) => {
    if (e.path.replaceAll('\\', '/') === 'C:/Users/test/.agent-usage/sessions/0f8a2c1e-1111-4222-8333-944455556666.json') records.push(JSON.parse(e.text))
    return { value: undefined } as never
  })
  return { records, clock }
}

test('tracks running and idle across a turn', async ($, on) => {
  const { records } = sessionWrites(on)
  on('turn.start', ($, e) => ({ turnId: e.turnId }))
  on('turn.complete', () => ({ text: '' }))

  await $.turn.start({ text: 'hi', turnId: 't1' })
  await $.turn.complete({ reason: 'end_turn' } as never)

  expect(records.map(r => r.state)).toEqual(['running', 'idle'])
  expect(records[0]).toMatchObject({ sessionId: '0f8a2c1e-1111-4222-8333-944455556666', cwd: 'D:/work/proj', detail: null })
})

test('marks waiting while a permission prompt is pending', async ($, on) => {
  const { records } = sessionWrites(on)
  on('turn.start', ($, e) => ({ turnId: e.turnId }))
  on('tool.check', () => ({ decision: 'ask' }))
  on('classic.PermissionRequest', () => ({}))
  on('tool.call', () => ({ result: 'ok' }) as never)

  await $.turn.start({ text: 'hi', turnId: 't1' })
  // An 'ask' verdict alone may still be settled by the mode without a dialog
  await $.tool.check({ tool: 'Bash', input: { command: 'ls' }, tool_use_id: 'tu1' })
  expect(records.length).toBe(1)

  await $.classic.PermissionRequest({ tool_name: 'Bash', tool_input: { command: 'ls' } })
  await $.tool.call({ tool: 'Bash', command: 'ls', tool_use_id: 'tu1' } as never)
  expect(records.map(r => [r.state, r.detail])).toEqual([
    ['running', null],
    ['waiting', 'Bash'],
    ['running', null],
  ])
})

test('marks waiting while AskUserQuestion is open', async ($, on) => {
  const { records } = sessionWrites(on)
  on('tool.call', () => ({ result: 'answered' }) as never)

  await $.tool.call({ tool: 'AskUserQuestion', questions: [] } as never)

  expect(records.map(r => [r.state, r.detail])).toEqual([
    ['waiting', 'AskUserQuestion'],
    ['idle', null],
  ])
})

test('heartbeats even when not interactive', async ($, on) => {
  const { records, clock } = sessionWrites(on)
  on('session.start', ($, e) => ({ cwd: e.cwd }))
  on('session.usage', () => ({ value: { rateLimits: [] } }) as never)

  await $.session.start({ cwd: 'D:/work/proj', surface: null, isInteractive: false })
  await clock.advance(60_000)

  expect(records.map(r => [r.state, r.updatedAt, r.heartbeatAt])).toEqual([
    ['idle', '2026-10-02T01:00:00.000Z', '2026-10-02T01:00:00.000Z'],
    ['idle', '2026-10-02T01:00:00.000Z', '2026-10-02T01:01:00.000Z'],
  ])
})

test('marks the session ended', async ($, on) => {
  const { records } = sessionWrites(on)
  on('session.end', ($, e) => ({ sessionId: e.sessionId }))

  await $.session.end({ reason: 'clear', sessionId: '0f8a2c1e-1111-4222-8333-944455556666' } as never)

  expect(records.map(r => r.state)).toEqual(['ended'])
})
