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
