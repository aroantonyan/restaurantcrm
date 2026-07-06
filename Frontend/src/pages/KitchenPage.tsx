import { useEffect, useMemo, useRef, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { useTranslation } from 'react-i18next'
import { api, ApiError, type KitchenQueueItemDto, type OrderItemStatus, type Station } from '../lib/api'
import { usePermissions } from '../hooks/usePermissions'
import { useRealtimeEvent, useRealtimeReconnected } from '../hooks/useRealtimeEvent'
import AppHeader from '../components/AppHeader'
import Chip from '../components/Chip'
import EmptyState from '../components/EmptyState'
import Portal from '../components/Portal'
import { SkeletonRow } from '../components/Skeleton'
import { Bell, BellOff, ChefHat, Check } from 'lucide-react'

/**
 * Kitchen Display System — grouped by ticket (order), the layout every mature KDS
 * (Toast, Square, TouchBistro) uses: a table's dishes stay together so the kitchen
 * fires and the expo plates them as one. Within a ticket the cook can advance a
 * single item, or bump the whole ticket in one tap.
 *
 *   • Station routing: the board can show one prep station (Kitchen / Bar) —
 *     items route via their menu category. Ticket bumps only touch the visible
 *     station's items, so the bartender can't mark the kitchen's food ready.
 *   • "All day" strip: live per-dish totals still to cook, so the line can batch
 *     (7× fries fire together) — the production view every real KDS has.
 *   • Ticket card per open order, FIFO (oldest order first), colour-coded urgency
 *     on the ticket age (green <5 min, yellow <10, red 10+).
 *   • Bump & recall: "Clear ticket" (→ Served) shows an Undo snackbar for a few
 *     seconds — fat-fingered bumps are routine on a kitchen screen.
 *   • New-item chime + vibration (mutable, persisted) so the line notices tickets
 *     without staring at the screen.
 *   • Resilience: refetch on SignalR reconnect (events missed while offline are
 *     gone for good) and a screen wake-lock so the tablet doesn't sleep mid-rush.
 *
 * Permission: `MoveOrderItems` (cooks + bartenders by default).
 */

const STATUS_ORDER: OrderItemStatus[] = ['Pending', 'Preparing', 'Ready', 'Served']
type Filter = 'all' | 'Pending' | 'Preparing' | 'Ready'
type StationFilter = 'all' | Station
const STATIONS: StationFilter[] = ['all', 'Kitchen', 'Bar']

const STATION_KEY = 'kds_station'
const MUTED_KEY = 'kds_muted'

// Urgency tiers tuned to restaurant prep times (Square/Toast defaults).
const WARN_AFTER_SECONDS = 5 * 60
const LATE_AFTER_SECONDS = 10 * 60

// The age badge renders minute-granularity ("82 min"), so a 1-second tick would
// re-render the whole board 60× more often than the display can change. 20s keeps
// the minute rollover (and the 5/10-min colour change) accurate to within 20s —
// imperceptible on a kitchen board — for a fraction of the render churn.
const CLOCK_INTERVAL_MS = 20_000

// How long the "table cleared" snackbar offers Undo before it gives up.
const UNDO_WINDOW_MS = 8_000

interface Ticket {
  orderId: string
  tableNumber: number
  serverName: string
  items: KitchenQueueItemDto[]   // scoped to the active station
  oldestMs: number               // start of the oldest item's clock — the ticket's age
  allReady: boolean              // every visible item is Ready → awaiting pickup
}

interface ClearedTicket {
  orderId: string
  itemIds: string[]
  tableNumber: number
}

function urgencyClasses(elapsedSeconds: number): { ring: string; badge: string } {
  if (elapsedSeconds < WARN_AFTER_SECONDS) return { ring: 'border-l-ok',     badge: 'bg-ok-soft text-ok'       }
  if (elapsedSeconds < LATE_AFTER_SECONDS) return { ring: 'border-l-warn',   badge: 'bg-warn-soft text-warn'   }
  return                                        { ring: 'border-l-danger', badge: 'bg-danger-soft text-danger' }
}

function nextStatus(s: OrderItemStatus): OrderItemStatus | null {
  const i = STATUS_ORDER.indexOf(s)
  return i < 0 || i >= STATUS_ORDER.length - 1 ? null : STATUS_ORDER[i + 1]
}

function formatElapsed(secs: number, t: (k: string, opts?: Record<string, unknown>) => string): string {
  if (secs < 60) return t('kitchen.justNow')
  const mins = Math.floor(secs / 60)
  if (mins < 60) return t('kitchen.minAgo', { count: mins })
  return t('kitchen.hAgo', { count: Math.floor(mins / 60) })
}

// Two-tone ding synthesized with WebAudio — no asset to load, works offline.
let chimeCtx: AudioContext | null = null

// Autoplay policy: an AudioContext created outside a user gesture starts
// suspended and produces no sound. Warming it up on the first tap anywhere on
// the page means the first real chime is audible instead of silently dropped.
function unlockChime() {
  try {
    chimeCtx ??= new AudioContext()
    if (chimeCtx.state === 'suspended') void chimeCtx.resume()
  } catch { /* no audio available */ }
}

function playChime() {
  try {
    chimeCtx ??= new AudioContext()
    if (chimeCtx.state === 'suspended') void chimeCtx.resume()
    const t0 = chimeCtx.currentTime
    for (const [freq, at] of [[880, 0], [1174.7, 0.12]] as const) {
      const osc = chimeCtx.createOscillator()
      const gain = chimeCtx.createGain()
      osc.type = 'sine'
      osc.frequency.value = freq
      gain.gain.setValueAtTime(0.0001, t0 + at)
      gain.gain.exponentialRampToValueAtTime(0.35, t0 + at + 0.02)
      gain.gain.exponentialRampToValueAtTime(0.0001, t0 + at + 0.35)
      osc.connect(gain).connect(chimeCtx.destination)
      osc.start(t0 + at)
      osc.stop(t0 + at + 0.4)
    }
  } catch { /* no audio permission yet — silent is fine */ }
}

export default function KitchenPage() {
  const { t } = useTranslation()
  const navigate = useNavigate()
  const perm = usePermissions()
  const canMove = perm.has('MoveOrderItems')

  const [items, setItems] = useState<KitchenQueueItemDto[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [filter, setFilter] = useState<Filter>('all')
  const [station, setStation] = useState<StationFilter>(() => {
    const saved = localStorage.getItem(STATION_KEY)
    return saved === 'Kitchen' || saved === 'Bar' ? saved : 'all'
  })
  const [muted, setMuted] = useState(() => localStorage.getItem(MUTED_KEY) === '1')
  // Ticking clock so the elapsed badges + urgency colours refresh periodically
  // without re-fetching the API.
  const [tick, setTick] = useState(0)
  // Tickets / items mid-flight, so a double-tap can't fire duplicate requests.
  const [busy, setBusy] = useState<Set<string>>(new Set())
  // Last ticket cleared from this device — offered for Undo (KDS recall).
  const [lastCleared, setLastCleared] = useState<ClearedTicket | null>(null)
  const [undoBusy, setUndoBusy] = useState(false)
  const undoTimer = useRef<number | undefined>(undefined)

  // Every item id ever seen this session; anything not in here is a genuinely
  // new arrival worth a chime. Cumulative on purpose: recalled items come back
  // without re-alerting.
  const seenItemIds = useRef<Set<string> | null>(null)
  // Monotonic id per fetch. Realtime bursts overlap requests, and responses can
  // resolve out of order — an older response must not overwrite newer state.
  const loadSeq = useRef(0)

  const inStation = (i: KitchenQueueItemDto) => station === 'all' || i.station === station

  const notifyNewItems = (data: KitchenQueueItemDto[]) => {
    const seen = seenItemIds.current
    if (!seen) {
      seenItemIds.current = new Set(data.map(i => i.id))
      return
    }
    const fresh = data.filter(i => !seen.has(i.id))
    for (const i of fresh) seen.add(i.id)
    if (muted || !fresh.some(inStation)) return
    playChime()
    navigator.vibrate?.(150)
  }

  const load = async () => {
    const seq = ++loadSeq.current
    setError(null)
    try {
      const data = await api.kitchen.queue()
      if (seq !== loadSeq.current) return   // superseded by a newer fetch
      setItems(data)
      notifyNewItems(data)
    } catch (e) {
      if (seq !== loadSeq.current) return
      setError(e instanceof ApiError ? e.message : t('kitchen.errors.loadFailed'))
    } finally {
      setLoading(false)
    }
  }

  useEffect(() => { void load() }, [])
  useRealtimeEvent('orderChanged', () => { void load() })
  // Events emitted while the socket was down were never delivered — the board
  // is silently stale until a refetch, which on a kitchen screen means missed
  // tickets. Refetch the moment the connection recovers.
  useRealtimeReconnected(() => { void load() })

  useEffect(() => {
    const handle = window.setInterval(() => setTick(n => n + 1), CLOCK_INTERVAL_MS)
    return () => window.clearInterval(handle)
  }, [])

  // A kitchen display must not go dark mid-shift. Best-effort: unsupported
  // browsers just keep their normal screen timeout. The lock is released by the
  // OS whenever the tab is hidden, so re-acquire on return.
  useEffect(() => {
    let sentinel: WakeLockSentinel | null = null
    let disposed = false
    const acquire = () => {
      navigator.wakeLock?.request('screen')
        .then(s => {
          // The page can unmount while the request is in flight — release the
          // late-arriving lock instead of holding it until the tab hides.
          if (disposed) void s.release().catch(() => {})
          else sentinel = s
        })
        .catch(() => { /* unsupported or denied */ })
    }
    acquire()
    const onVisible = () => { if (document.visibilityState === 'visible') acquire() }
    document.addEventListener('visibilitychange', onVisible)
    return () => {
      disposed = true
      document.removeEventListener('visibilitychange', onVisible)
      void sentinel?.release().catch(() => {})
    }
  }, [])

  // One-time gesture hook so the chime's AudioContext is unlocked before the
  // first alert needs it (see unlockChime).
  useEffect(() => {
    document.addEventListener('pointerdown', unlockChime, { once: true })
    return () => document.removeEventListener('pointerdown', unlockChime)
  }, [])

  useEffect(() => () => window.clearTimeout(undoTimer.current), [])

  const switchStation = (s: StationFilter) => {
    setStation(s)
    localStorage.setItem(STATION_KEY, s)
  }

  const toggleMuted = () => {
    const next = !muted
    setMuted(next)
    localStorage.setItem(MUTED_KEY, next ? '1' : '0')
  }

  const markBusy = (key: string, on: boolean) =>
    setBusy(s => { const n = new Set(s); on ? n.add(key) : n.delete(key); return n })

  // Advance a single item one step. Optimistic; a Served item drops off the board.
  const advanceItem = async (item: KitchenQueueItemDto) => {
    const next = nextStatus(item.status)
    if (!next) return
    markBusy(item.id, true)
    const prev = items
    setItems(cur => cur.flatMap(i =>
      i.id !== item.id ? [i] : next === 'Served' ? [] : [{ ...i, status: next }]))
    try {
      await api.orders.updateItemStatus(item.orderId, item.id, next)
    } catch (e) {
      setItems(prev)
      setError(e instanceof ApiError ? e.message : t('kitchen.errors.advanceFailed'))
    } finally {
      markBusy(item.id, false)
    }
  }

  // Bump every visible item on a ticket at once (atomic on the server). When a
  // station is selected only that station's items move — the server enforces
  // the same scoping. Clearing a ticket arms the Undo snackbar with the exact
  // item ids that were served, so recall can't resurrect anything else.
  const bumpTicket = async (ticket: Ticket, target: 'Ready' | 'Served') => {
    markBusy(ticket.orderId, true)
    const affected = new Set(ticket.items.map(i => i.id))
    const prev = items
    setItems(cur => cur.flatMap(i => {
      if (!affected.has(i.id)) return [i]
      if (target === 'Served') return []                                   // clears the ticket
      return i.status === 'Pending' || i.status === 'Preparing'
        ? [{ ...i, status: 'Ready' as OrderItemStatus }]
        : [i]
    }))
    try {
      await api.kitchen.bump(ticket.orderId, target, station === 'all' ? undefined : station)
      if (target === 'Served') {
        window.clearTimeout(undoTimer.current)
        setLastCleared({ orderId: ticket.orderId, itemIds: [...affected], tableNumber: ticket.tableNumber })
        undoTimer.current = window.setTimeout(() => setLastCleared(null), UNDO_WINDOW_MS)
      }
    } catch (e) {
      setItems(prev)
      setError(e instanceof ApiError ? e.message : t('kitchen.errors.advanceFailed'))
    } finally {
      markBusy(ticket.orderId, false)
    }
  }

  const undoClear = async () => {
    if (!lastCleared) return
    setUndoBusy(true)
    try {
      await api.kitchen.recall(lastCleared.orderId, lastCleared.itemIds)
      window.clearTimeout(undoTimer.current)
      setLastCleared(null)
      await load()
    } catch (e) {
      setError(e instanceof ApiError ? e.message : t('kitchen.errors.recallFailed'))
    } finally {
      setUndoBusy(false)
    }
  }

  // Everything below renders the active station's slice of the queue.
  const scoped = useMemo(
    () => station === 'all' ? items : items.filter(i => i.station === station),
    [items, station])

  // Per-status counts for the chips (across the station scope, ignoring the filter).
  const counts = useMemo(() => ({
    all: scoped.length,
    Pending:   scoped.filter(i => i.status === 'Pending').length,
    Preparing: scoped.filter(i => i.status === 'Preparing').length,
    Ready:     scoped.filter(i => i.status === 'Ready').length,
  }), [scoped])

  // "All day" production counts — per-dish totals still to cook (Pending +
  // Preparing), biggest first, so the line batches same-dish fires.
  const allDay = useMemo(() => {
    const totals = new Map<string, number>()
    for (const i of scoped) {
      if (i.status !== 'Pending' && i.status !== 'Preparing') continue
      totals.set(i.menuItemName, (totals.get(i.menuItemName) ?? 0) + i.quantity)
    }
    return [...totals.entries()].sort((a, b) => b[1] - a[1])
  }, [scoped])

  // Group into tickets. The filter selects which tickets show (a ticket with ≥1
  // matching item), but the whole (station-scoped) ticket renders — keeping the
  // table's dishes together is the entire point.
  const tickets = useMemo<Ticket[]>(() => {
    const byOrder = new Map<string, KitchenQueueItemDto[]>()
    for (const i of scoped) (byOrder.get(i.orderId) ?? byOrder.set(i.orderId, []).get(i.orderId)!).push(i)

    const result: Ticket[] = []
    for (const [orderId, list] of byOrder) {
      if (filter !== 'all' && !list.some(i => i.status === filter)) continue
      result.push({
        orderId,
        tableNumber: list[0].tableNumber,
        serverName: list[0].serverName,
        items: list,
        oldestMs: Math.min(...list.map(i => new Date(i.createdAt).getTime())),
        allReady: list.every(i => i.status === 'Ready'),
      })
    }
    // FIFO: oldest ticket first.
    return result.sort((a, b) => a.oldestMs - b.oldestMs)
  }, [scoped, filter])

  // Re-derive elapsed/urgency on every clock tick. `tick` is an intentional dep.
  const now = Date.now()
  void tick

  if (!canMove) {
    return (
      <main className="page-enter h-full overflow-y-auto pb-7">
        <AppHeader onBack={() => navigate('/dashboard')} title={t('kitchen.title')} />
        <div className="px-5">
          <EmptyState icon={ChefHat} title={t('common.forbidden')} hint={t('kitchen.permissionHint')} />
        </div>
      </main>
    )
  }

  return (
    <main className="page-enter h-full overflow-y-auto pb-7">
      <AppHeader
        onBack={() => navigate('/dashboard')}
        title={t('kitchen.title')}
        subtitle={t('kitchen.itemCount', { count: scoped.length })}
        trailing={
          <button
            type="button"
            onClick={toggleMuted}
            aria-label={t(muted ? 'kitchen.soundOff' : 'kitchen.soundOn')}
            className="w-9 h-9 rounded-full bg-[rgba(15,15,16,0.05)] text-fg-2 flex items-center justify-center tappable border-0"
          >
            {muted ? <BellOff size={17} aria-hidden /> : <Bell size={17} aria-hidden />}
          </button>
        }
      />

      {/* Station selector — which prep line this screen is */}
      <div className="px-5 pt-1">
        <div className="flex rounded-xl bg-muted p-1 gap-1">
          {STATIONS.map(s => (
            <button
              key={s}
              type="button"
              onClick={() => switchStation(s)}
              aria-pressed={station === s}
              className={`flex-1 py-2.5 rounded-lg text-[13px] font-semibold border-0 tappable ${
                station === s
                  ? 'bg-card text-fg shadow-[0_1px_0_rgba(15,15,16,.04),0_1px_3px_rgba(15,15,16,.08)]'
                  : 'bg-transparent text-fg-3'
              }`}
            >
              {t(`kitchen.station.${s}`)}
            </button>
          ))}
        </div>
      </div>

      <div className="flex gap-2 px-5 pt-3 pb-3 overflow-x-auto" style={{ scrollbarWidth: 'none' }}>
        <Chip active={filter === 'all'}       onClick={() => setFilter('all')}       count={counts.all}>{t('kitchen.filter.all')}</Chip>
        <Chip active={filter === 'Pending'}   onClick={() => setFilter('Pending')}   count={counts.Pending}>{t('kitchen.filter.Pending')}</Chip>
        <Chip active={filter === 'Preparing'} onClick={() => setFilter('Preparing')} count={counts.Preparing}>{t('kitchen.filter.Preparing')}</Chip>
        <Chip active={filter === 'Ready'}     onClick={() => setFilter('Ready')}     count={counts.Ready}>{t('kitchen.filter.Ready')}</Chip>
      </div>

      {/* "All day" — what still needs cooking, aggregated across tickets */}
      {!loading && allDay.length > 0 && (
        <div className="px-5 pb-3">
          <div className="bg-card rounded-[18px] px-3.5 py-2.5"
               style={{ boxShadow: '0 1px 0 rgba(15,15,16,.04), 0 1px 3px rgba(15,15,16,.05)' }}>
            <p className="m-0 mb-1.5 text-[10.5px] font-bold uppercase text-fg-3" style={{ letterSpacing: '0.06em' }}>
              {t('kitchen.allDay')}
            </p>
            <div className="flex flex-wrap gap-1.5">
              {allDay.map(([name, qty]) => (
                <span key={name} className="inline-flex items-center px-2 py-1 rounded-lg bg-bg text-[12.5px] font-semibold">
                  <span className="tabular-nums text-accent mr-1">{qty}×</span>{name}
                </span>
              ))}
            </div>
          </div>
        </div>
      )}

      <div className="px-5">
        {/* A failed action shouldn't blank a board that still has live data —
            show the error inline and keep the tickets up. */}
        {error && items.length > 0 && (
          <div className="mb-3 rounded-xl bg-danger-soft px-3.5 py-2.5 flex items-center gap-2">
            <p className="m-0 flex-1 text-[13px] text-danger font-semibold">{error}</p>
            <button onClick={() => setError(null)}
                    className="shrink-0 border-0 bg-transparent text-danger text-[13px] font-bold tappable px-1">
              ✕
            </button>
          </div>
        )}

        {loading ? (
          <div className="flex flex-col gap-2">
            {[0, 1, 2, 3].map(i => <SkeletonRow key={i} />)}
          </div>
        ) : error && items.length === 0 ? (
          <div className="rounded-[18px] bg-card py-8 text-center"
               style={{ boxShadow: '0 1px 0 rgba(15,15,16,.04), 0 1px 3px rgba(15,15,16,.05)' }}>
            <p className="m-0 text-sm text-danger mb-3">{error}</p>
            <button onClick={() => { setLoading(true); void load() }}
                    className="px-4 py-2 rounded-xl bg-muted text-fg-2 text-sm font-semibold tappable border-0">
              {t('common.retry')}
            </button>
          </div>
        ) : tickets.length === 0 ? (
          <EmptyState icon={ChefHat}
                      title={t('kitchen.empty')}
                      hint={filter === 'all' ? t('kitchen.emptyHint') : t('kitchen.emptyFilterHint')} />
        ) : (
          <div className="flex flex-col gap-3">
            {tickets.map((ticket, idx) => {
              const elapsed = Math.max(0, Math.floor((now - ticket.oldestMs) / 1000))
              const urgency = ticket.allReady
                ? { ring: 'border-l-ok', badge: 'bg-ok-soft text-ok' }
                : urgencyClasses(elapsed)
              const ticketBusy = busy.has(ticket.orderId)

              return (
                <section
                  key={ticket.orderId}
                  className={`item-enter bg-card rounded-[18px] border-l-4 ${urgency.ring} overflow-hidden`}
                  style={{
                    animationDelay: `${Math.min(idx, 6) * 30}ms`,
                    boxShadow: '0 1px 0 rgba(15,15,16,.04), 0 1px 3px rgba(15,15,16,.05)',
                  }}
                >
                  {/* Ticket header — table + server + age + ready badge */}
                  <div className="flex items-center gap-2 px-3.5 pt-3 pb-2">
                    <div className="flex-1 min-w-0">
                      <p className="m-0 text-[15.5px] font-bold leading-tight" style={{ letterSpacing: '-0.01em' }}>
                        {t('kitchen.table', { number: ticket.tableNumber })}
                      </p>
                      {ticket.serverName && (
                        <p className="m-0 text-[12px] text-fg-3 truncate">{ticket.serverName}</p>
                      )}
                    </div>
                    {ticket.allReady ? (
                      <span className="inline-flex items-center gap-1 px-2 py-0.5 rounded-md text-[11px] font-bold uppercase bg-ok-soft text-ok"
                            style={{ letterSpacing: '0.04em' }}>
                        <Check size={12} strokeWidth={3} aria-hidden /> {t('kitchen.readyBadge')}
                      </span>
                    ) : (
                      <span className={`inline-flex px-2 py-0.5 rounded-md text-[12px] font-bold tabular-nums ${urgency.badge}`}>
                        {formatElapsed(elapsed, t)}
                      </span>
                    )}
                  </div>

                  {/* Item rows */}
                  <ul className="m-0 px-3.5 pb-2 list-none flex flex-col gap-1.5">
                    {ticket.items.map(item => {
                      const next = nextStatus(item.status)
                      const showAdvance = next === 'Preparing' || next === 'Ready'
                      return (
                        <li key={item.id} className="flex items-center gap-2.5">
                          <div className="flex-1 min-w-0">
                            <p className="m-0 text-[14.5px] font-semibold leading-tight" style={{ letterSpacing: '-0.005em' }}>
                              <span className="tabular-nums mr-1">×{item.quantity}</span>{item.menuItemName}
                            </p>
                            {item.notes && (
                              <p className="m-0 text-[12px] text-accent-press italic truncate">“{item.notes}”</p>
                            )}
                          </div>
                          {station === 'all' && item.station === 'Bar' && (
                            <span className="shrink-0 inline-flex items-center px-1.5 py-0.5 rounded-md text-[10px] font-bold uppercase bg-accent-soft text-accent"
                                  style={{ letterSpacing: '0.04em' }}>
                              {t('kitchen.station.Bar')}
                            </span>
                          )}
                          <span className="shrink-0 inline-flex items-center px-1.5 py-0.5 rounded-md text-[10px] font-bold uppercase bg-bg text-fg-3"
                                style={{ letterSpacing: '0.04em' }}>
                            {t(`kitchen.status.${item.status}`)}
                          </span>
                          {showAdvance && (
                            <button
                              type="button"
                              onClick={() => advanceItem(item)}
                              disabled={busy.has(item.id) || ticketBusy}
                              className="tappable shrink-0 px-2.5 py-1.5 rounded-lg bg-bg text-accent text-[12.5px] font-semibold border-0 disabled:opacity-50"
                            >
                              {t(`kitchen.advance.${next}`)}
                            </button>
                          )}
                        </li>
                      )
                    })}
                  </ul>

                  {/* Ticket-level action */}
                  <div className="px-3.5 pb-3 pt-1">
                    {ticket.allReady ? (
                      <button
                        type="button"
                        onClick={() => bumpTicket(ticket, 'Served')}
                        disabled={ticketBusy}
                        className="tappable w-full py-2.5 rounded-xl bg-ok text-white text-sm font-semibold border-0 disabled:opacity-50"
                      >
                        {t('kitchen.clearTicket')}
                      </button>
                    ) : (
                      <button
                        type="button"
                        onClick={() => bumpTicket(ticket, 'Ready')}
                        disabled={ticketBusy}
                        className="tappable w-full py-2.5 rounded-xl bg-accent text-white text-sm font-semibold border-0 disabled:opacity-50"
                      >
                        {t('kitchen.allReady')}
                      </button>
                    )}
                  </div>
                </section>
              )
            })}
          </div>
        )}
      </div>

      {/* Undo snackbar — recall the ticket just cleared. Portaled to <body>:
          `position: fixed` inside this <main> would anchor to the page (its
          .page-enter animation leaves a transform, which per CSS spec makes it
          the containing block) and the snackbar would scroll away with the
          board. The bottom offset clears the app's bottom tab bar. */}
      {lastCleared && (
        <Portal>
          <div role="status"
               className="fixed left-4 right-4 z-50 flex items-center gap-3 bg-fg text-white rounded-2xl px-4 py-3"
               style={{ bottom: 'calc(80px + env(safe-area-inset-bottom, 0px))', boxShadow: '0 8px 24px rgba(15,15,16,.28)' }}>
            <p className="m-0 flex-1 text-[13.5px] font-semibold">
              {t('kitchen.cleared', { number: lastCleared.tableNumber })}
            </p>
            <button
              type="button"
              onClick={undoClear}
              disabled={undoBusy}
              className="shrink-0 px-3 py-1.5 rounded-lg bg-white/15 text-white text-[13px] font-bold border-0 tappable disabled:opacity-50"
            >
              {t('kitchen.undo')}
            </button>
          </div>
        </Portal>
      )}
    </main>
  )
}
