import { describe, expect, it, vi } from 'vitest'
import { SnapshotSynchronizer } from './synchronization'
function deferred<T>() { let resolve!: (value: T) => void; const promise = new Promise<T>(r => { resolve = r }); return { promise, resolve } }
describe('snapshot subscription reconciliation', () => {
  it('repeats when a write happens between snapshot reads even without an event', async () => {
    let current = 1; const ready = vi.fn()
    const load = vi.fn(async () => { const loaded = current; current = 2; return loaded })
    await new SnapshotSynchronizer(load, async () => current, ready).refresh()
    expect(load).toHaveBeenCalledTimes(2); expect(ready).toHaveBeenLastCalledWith(true)
  })
  it('coalesces events during a fetch and never accepts less than their high water mark', async () => {
    const first = deferred<number>(); const ready = vi.fn(); let current = 1
    const load = vi.fn().mockReturnValueOnce(first.promise).mockImplementation(async () => current)
    const sync = new SnapshotSynchronizer(load, async () => current, ready)
    const initial = sync.refresh(); await Promise.resolve()
    current = 3; const later = sync.refresh(3); first.resolve(1)
    await Promise.all([initial, later]); expect(load).toHaveBeenCalledTimes(2)
    expect(ready.mock.calls.filter(x => x[0])).toHaveLength(1)
  })
  it('reconnect invalidates a pending read and waits for a new complete cycle', async () => {
    const first = deferred<number>(); const ready = vi.fn()
    const load = vi.fn().mockReturnValueOnce(first.promise).mockResolvedValue(2)
    const sync = new SnapshotSynchronizer(load, async () => 2, ready)
    const pending = sync.refresh(); await Promise.resolve()
    sync.invalidate(); const reconnected = sync.refresh(); first.resolve(2)
    await Promise.all([pending, reconnected]); expect(load).toHaveBeenCalledTimes(2)
    expect(ready).toHaveBeenLastCalledWith(true)
  })
  it('failed reads disable saving and a later reconcile can recover', async () => {
    const ready = vi.fn(); const load = vi.fn().mockRejectedValueOnce(new Error('offline')).mockResolvedValue(4)
    const sync = new SnapshotSynchronizer(load, async () => 4, ready)
    await expect(sync.refresh()).rejects.toThrow('offline'); expect(ready).toHaveBeenLastCalledWith(false)
    await sync.refresh(); expect(ready).toHaveBeenLastCalledWith(true)
  })
})
