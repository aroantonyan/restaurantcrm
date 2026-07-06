import { useEffect, useRef } from 'react'
import { connectRealtime, onRealtimeReconnected } from '../lib/realtime'

/**
 * Subscribes the component to a realtime event for its lifetime.
 * On unmount the handler is removed cleanly.
 *
 * Usage:
 *   useRealtimeEvent('orderChanged', ({ orderId }) => {
 *     if (orderId === currentId) refetch()
 *   })
 */
export function useRealtimeEvent<T = unknown>(
  eventName: string,
  handler: (payload: T) => void
) {
  // Keep the latest handler in a ref so we don't have to re-subscribe
  // every time the component re-renders with a new closure.
  const handlerRef = useRef(handler)
  handlerRef.current = handler

  useEffect(() => {
    const conn = connectRealtime()
    if (!conn) return

    const wrapped = (payload: T) => handlerRef.current(payload)
    conn.on(eventName, wrapped)
    return () => conn.off(eventName, wrapped)
  }, [eventName])
}

/**
 * Runs the handler when the SignalR connection recovers after a drop.
 * Live views should refetch here — events emitted while the socket was down
 * were never delivered, so the screen is silently stale until then.
 */
export function useRealtimeReconnected(handler: () => void) {
  const handlerRef = useRef(handler)
  handlerRef.current = handler

  useEffect(() => onRealtimeReconnected(() => handlerRef.current()), [])
}
