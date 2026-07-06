import { HubConnection, HubConnectionBuilder, HubConnectionState, LogLevel } from '@microsoft/signalr'
import { auth } from './auth'

/**
 * Single shared SignalR connection. Module-level singleton so every page
 * subscribes to the same socket — no fan-out, no multiple WebSockets.
 *
 * The connection is best-effort: if it can't establish or drops, the app
 * still works via normal REST. Pages that subscribe will simply not get
 * push updates until reconnect.
 */

let connection: HubConnection | null = null
let startPromise: Promise<void> | null = null

// SignalR's onreconnected callbacks can be added but never removed, so the
// connection gets ONE dispatcher and components subscribe through this set —
// unsubscribing is just a delete, no leaked closures across remounts.
const reconnectedListeners = new Set<() => void>()

/** Subscribe to "connection recovered" — returns the unsubscribe function.
 *  Pages showing live data should refetch here: every event emitted while the
 *  socket was down is gone for good, so the view is stale until a refetch. */
export function onRealtimeReconnected(listener: () => void): () => void {
  reconnectedListeners.add(listener)
  return () => { reconnectedListeners.delete(listener) }
}

function build(): HubConnection {
  const conn = new HubConnectionBuilder()
    .withUrl('/hubs/orders', {
      accessTokenFactory: () => auth.getToken() ?? '',
    })
    .withAutomaticReconnect()
    .configureLogging(LogLevel.Warning)
    .build()
  conn.onreconnected(() => { for (const l of reconnectedListeners) l() })
  return conn
}

export function connectRealtime(): HubConnection | null {
  // No token → no connection. (Pages call this defensively; logged-out users get nothing.)
  if (!auth.getToken()) return null

  if (connection && connection.state !== HubConnectionState.Disconnected) {
    return connection
  }

  if (!connection) connection = build()

  if (connection.state === HubConnectionState.Disconnected && !startPromise) {
    startPromise = connection.start()
      .catch(err => {
        // Best-effort: log once and let the page keep working with REST.
        console.warn('[realtime] failed to connect', err)
      })
      .finally(() => { startPromise = null })
  }

  return connection
}

export async function disconnectRealtime(): Promise<void> {
  if (!connection) return
  try {
    await connection.stop()
  } catch {
    // ignore — we're tearing down
  } finally {
    connection = null
    startPromise = null
  }
}

export function getRealtimeConnection(): HubConnection | null {
  return connection
}
