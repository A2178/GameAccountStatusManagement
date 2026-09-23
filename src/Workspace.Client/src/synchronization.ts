// A single refresh loop owns applying snapshots. Events arriving during a fetch raise
// the required version; reconnect invalidates the entire in-flight cycle.
export class SnapshotSynchronizer {
  private generation = 0
  private requested = 0
  private pending?: Promise<void>
  constructor(private load: () => Promise<number>, private version: () => Promise<number>, private ready: (value: boolean) => void) {}
  invalidate() { this.generation++; this.ready(false) }
  refresh(minimum = 0): Promise<void> {
    this.requested = Math.max(this.requested, minimum)
    if (!this.pending) this.pending = this.run().finally(() => { this.pending = undefined })
    return this.pending
  }
  private async run() {
    try {
      for (;;) {
        const generation = this.generation
        const before = await this.version()
        const loaded = await this.load()
        const after = await this.version()
        if (generation !== this.generation) continue
        if (before === after && loaded >= after && loaded >= this.requested) { this.ready(true); return }
      }
    } catch (error) { this.ready(false); throw error }
  }
}
