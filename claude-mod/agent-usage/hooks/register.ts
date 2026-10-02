import type { EngineInterface, Register, SessionRateLimit } from 'claude-code'

const writeUsage = async ($: EngineInterface, rateLimits: SessionRateLimit[]) => {
  if (rateLimits.length === 0) return
  const home = (await $.env.get('USERPROFILE')) ?? (await $.env.get('HOME'))
  if (home === undefined) return
  const windows = Object.fromEntries(
    rateLimits.map(r => [r.kind, { percentUsed: r.percentUsed, resetsAt: r.resetsAt ?? null }]),
  )
  const updatedAt = new Date(await $.clock.now()).toISOString()
  await $.fs.write(`${home}/.agent-usage/claude.json`, JSON.stringify({ updatedAt, ...windows }, null, 2))
}

export const register: Register = on => {
  on('session.start', async ($, e, next) => {
    const result = await next(e)
    await writeUsage($, (await $.session.usage()).rateLimits)
    return result
  })

  on('session.measure', async ($, e, next) => {
    if (e.changed.includes('rateLimits')) await writeUsage($, e.rateLimits)
    return next(e)
  })
}
