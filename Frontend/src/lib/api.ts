import { auth } from './auth'
import { disconnectRealtime } from './realtime'

const API_BASE = import.meta.env.VITE_API_BASE_URL ?? ''

// ---- Auth ----

export interface AuthResponse {
  token: string
  userId: string
  restaurantId: string
  restaurantName: string
  currency: string
  firstName: string
  lastName: string
  roleName: string
  permissions: string[]
  status: UserStatus
  refreshToken: string
}

export interface RegisterRequest {
  firstName: string
  lastName: string
  fatherName: string
  email: string
  password: string
  restaurantName: string
}

export interface LoginRequest {
  email: string
  password: string
}

// ---- Staff ----

export type UserStatus = 'Active' | 'Inactive' | 'PendingPasswordChange'

export interface StaffMember {
  id: string
  firstName: string
  lastName: string
  fatherName: string
  email: string
  phone: string | null
  roleName: string
  roleId: string
  status: UserStatus
  createdAt: string
  permissions: string[]
}

export interface Role {
  id: string
  name: string
  isDefault: boolean
  permissions: string[]
}

export interface CreateStaffRequest {
  firstName: string
  lastName: string
  fatherName: string
  email: string
  temporaryPassword: string
  roleId: string
  phone?: string
  permissions?: string[]
}

export interface UpdateStaffRequest {
  firstName: string
  lastName: string
  fatherName: string
  phone?: string
  roleId?: string
}

// ---- Menu ----

export interface RecipeIngredientDto {
  productId: string
  productName: string
  productUnit: string
  quantity: number
}

export interface RecipeDto {
  menuItemId: string
  menuItemName: string
  ingredients: RecipeIngredientDto[]
}

export interface SetRecipeRequest {
  ingredients: Array<{ productId: string; quantity: number }>
}

export interface MenuItemDto {
  id: string
  categoryId: string
  categoryName: string
  name: string
  description: string | null
  price: number
  photoUrl: string | null
  isAvailable: boolean
  // True when every recipe ingredient has enough stock for at least one portion.
  // Items without a recipe always report true.
  canFulfill: boolean
}

// KDS prep station a category's items route to.
export type Station = 'Kitchen' | 'Bar'

export interface MenuCategoryDto {
  id: string
  name: string
  description?: string | null
  sortOrder: number
  station: Station
  items: MenuItemDto[]
}

export interface CreateCategoryRequest { name: string; description?: string; sortOrder?: number; station?: Station }
// station omitted → backend leaves the current routing unchanged.
export interface UpdateCategoryRequest { name: string; description?: string; sortOrder: number; station?: Station }
export interface CreateMenuItemRequest { categoryId: string; name: string; description?: string; price: number; photoUrl?: string; isAvailable?: boolean }
export interface UpdateMenuItemRequest { categoryId: string; name: string; description?: string; price: number; photoUrl?: string; isAvailable: boolean }

// ---- Tables ----

export interface TableDto { id: string; number: number; capacity: number; status: string; isVip: boolean; vipAmount: number }
export interface CreateTableRequest { number: number; capacity?: number; isVip?: boolean; vipAmount?: number }
export interface UpdateTableRequest { number: number; capacity: number; isVip?: boolean; vipAmount?: number }
export type TableStatus = 'Free' | 'Occupied' | 'Reserved'

// ---- Orders ----

export interface OrderItemDto { id: string; menuItemId: string; menuItemName: string; price: number; quantity: number; status: string; notes: string | null }
export interface OrderDto { id: string; tableId: string; tableNumber: number; status: string; createdBy: string; createdAt: string; items: OrderItemDto[]; total: number; paymentMethod: string | null; clientId: string | null; clientName: string | null }
export interface CreateOrderRequest { tableId: string; items: Array<{ menuItemId: string; quantity: number; notes?: string }>; clientId?: string | null }
export interface AddOrderItemRequest { menuItemId: string; quantity: number; notes?: string }

export type OrderItemStatus = 'Pending' | 'Preparing' | 'Ready' | 'Served'
export interface KitchenQueueItemDto {
  id: string
  orderId: string
  menuItemName: string
  quantity: number
  notes: string | null
  status: OrderItemStatus
  station: Station
  tableNumber: number
  tableId: string
  serverName: string
  createdAt: string
}

export interface BillPreviewDto {
  subtotal: number
  vipSurcharge: number
  clientId: string | null
  clientName: string | null
  clientDepositBalance: number
  loyaltyType: LoyaltyType
  loyaltyRate: number
  cashbackToEarn: number
  suggestedCharge: number
  depositCovers: number
  depositRemainder: number
  balanceAfterDeposit: number
}

// ---- Reservations ----

export type ReservationStatus = 'Confirmed' | 'Seated' | 'Completed' | 'Cancelled' | 'NoShow'

export interface ReservationDto {
  id: string
  tableId: string
  tableNumber: number
  guestName: string
  guestPhone: string | null
  guestCount: number
  startAt: string        // ISO UTC
  durationMinutes: number
  endAt: string          // ISO UTC
  status: ReservationStatus
  notes: string | null
  createdByName: string
  createdAt: string
  // Optional CRM link. Null for one-off walk-ins.
  clientId: string | null
  clientName: string | null
}

export interface CreateReservationRequest {
  tableId: string
  guestName: string
  guestPhone?: string
  guestCount: number
  startAt: string        // ISO UTC
  durationMinutes: number
  notes?: string
  clientId?: string | null
}

export interface UpdateReservationRequest extends CreateReservationRequest {}

// ---- Schedule ----

export type ShiftStatus = 'Scheduled' | 'Cancelled'

export interface ShiftDto {
  id: string
  userId: string
  userName: string
  userRoleName: string
  startAt: string
  endAt: string
  durationMinutes: number
  roleForShift: string | null
  notes: string | null
  status: ShiftStatus
  createdAt: string
}

export interface CreateShiftRequest {
  userId: string
  startAt: string
  endAt: string
  roleForShift?: string | null
  notes?: string | null
}

export interface UpdateShiftRequest {
  userId: string
  startAt: string
  endAt: string
  roleForShift?: string | null
  notes?: string | null
  status: ShiftStatus
}

// ---- Restaurant ----

export interface RestaurantInfo {
  id: string
  name: string
  legalName: string
  currency: string
  address: string | null
  phone: string | null
  logoUrl: string | null
}

export interface UpdateRestaurantRequest {
  name: string
  legalName: string
  currency: string
  address?: string
  phone?: string
}

// ---- Reports ----

export interface ReportSummaryDto {
  revenue: number
  orderCount: number
  averageTicket: number
  itemsSold: number
  /** Percentage change vs prior equivalent period. null = no prior data. */
  revenuePctChange: number | null
  orderCountPctChange: number | null
}

export interface HourlyPointDto {
  hour: number       // 0–23
  orderCount: number
  revenue: number
}

export interface TopItemDto {
  menuItemId: string
  name: string
  quantity: number
  revenue: number
}

export interface TopServerDto {
  userId: string
  name: string
  orderCount: number
  revenue: number
}

export interface RevenuePointDto {
  date: string            // YYYY-MM-DD (DateOnly serialized)
  revenue: number
  orderCount: number
}

// ---- Cash Register ----

export type PaymentMethod = 'Cash' | 'Card' | 'BankTransfer' | 'Deposit' | 'Other'
export type CashTransactionType = 'OrderPayment' | 'Refund' | 'ManualIncome' | 'ManualExpense'

export interface CashRegisterTransactionDto {
  id: string
  type: CashTransactionType
  method: PaymentMethod
  amount: number
  balanceAfter: number
  reason: string | null
  orderId: string | null
  createdByName: string
  createdAt: string
}

export interface CashRegisterSummaryDto {
  cashBalance: number
  incomeCash: number
  incomeCard: number
  incomeBankTransfer: number
  incomeOther: number
  refunds: number
  manualIncome: number
  manualExpense: number
  net: number
  transactionCount: number
}

export interface RecordManualOpRequest {
  type: 'ManualIncome' | 'ManualExpense'
  amount: number
  reason: string
}

// ---- Clients (CRM) ----

export type LoyaltyType = 'None' | 'Cashback' | 'Discount'
export type ClientTransactionType = 'Deposit' | 'Withdrawal' | 'OrderPayment' | 'CashbackEarned'

export interface ClientDto {
  id: string
  fullName: string
  phone: string | null
  email: string | null
  birthday: string | null      // ISO date "YYYY-MM-DD" or null
  notes: string | null
  depositBalance: number
  loyaltyType: LoyaltyType
  loyaltyRate: number
  isArchived: boolean
  createdAt: string
  updatedAt: string | null
}

export interface ClientTransactionDto {
  id: string
  clientId: string
  type: ClientTransactionType
  amount: number
  balanceAfter: number
  reason: string | null
  orderId: string | null
  createdByName: string
  createdAt: string
}

export interface CreateClientRequest {
  fullName: string
  phone?: string | null
  email?: string | null
  birthday?: string | null
  notes?: string | null
  loyaltyType?: LoyaltyType
  loyaltyRate?: number
}

export interface UpdateClientRequest {
  fullName: string
  phone?: string | null
  email?: string | null
  birthday?: string | null
  notes?: string | null
  loyaltyType: LoyaltyType
  loyaltyRate: number
}

export interface ClientDepositRequest {
  amount: number
  method: 'Cash' | 'Card' | 'BankTransfer'
  reason?: string | null
}

export interface ClientWithdrawalRequest {
  amount: number
  method: 'Cash' | 'Card' | 'BankTransfer'
  reason?: string | null
}

// ---- Activity Log ----

export type ActivityCategory =
  | 'Auth' | 'Order' | 'Menu' | 'Inventory' | 'Table' | 'Reservation'
  | 'Staff' | 'Role' | 'Client' | 'CashRegister' | 'Settings' | 'Security'

export interface ActivityLogEntryDto {
  id: string
  userId: string | null
  userName: string
  category: ActivityCategory
  action: string
  entityType: string
  entityId: string | null
  description: string
  createdAt: string
}

// ---- Inventory ----

export type ProductUnit = 'Kg' | 'Gram' | 'Liter' | 'Milliliter' | 'Piece'
export type StockMovementType = 'Initial' | 'Purchase' | 'Adjustment' | 'Wastage' | 'Sale'

export interface ProductDto {
  id: string
  name: string
  category: string | null
  unit: ProductUnit
  currentStock: number
  lowStockThreshold: number
  isLowStock: boolean
  notes: string | null
  isArchived: boolean
  createdAt: string
  updatedAt: string | null
}

export interface StockMovementDto {
  id: string
  productId: string
  type: StockMovementType
  quantityChange: number
  quantityAfter: number
  reason: string | null
  createdByName: string
  createdAt: string
}

export interface CreateProductRequest {
  name: string
  category?: string | null
  unit: ProductUnit
  initialStock: number
  lowStockThreshold: number
  notes?: string | null
}

export interface UpdateProductRequest {
  name: string
  category?: string | null
  unit: ProductUnit
  lowStockThreshold: number
  notes?: string | null
}

export interface AddStockMovementRequest {
  type: Exclude<StockMovementType, 'Initial' | 'Sale'>
  quantityChange: number
  reason?: string | null
}

// ---- Error ----

export class ApiError extends Error {
  status: number
  constructor(status: number, message: string) {
    super(message)
    this.status = status
  }
}

// ---- Core ----

// Endpoints that mint/rotate tokens themselves — never run the refresh-retry on
// these, or a failed login would loop.
const NO_REFRESH = new Set(['/api/auth/login', '/api/auth/register', '/api/auth/refresh'])

// Single-flight refresh: many requests can 401 at once when the access token
// expires; they all await the same rotation instead of stampeding /auth/refresh.
let refreshInFlight: Promise<boolean> | null = null

function tryRefresh(): Promise<boolean> {
  if (refreshInFlight) return refreshInFlight
  const refreshToken = auth.getRefreshToken()
  if (!refreshToken) return Promise.resolve(false)

  refreshInFlight = (async () => {
    try {
      const res = await fetch(`${API_BASE}/api/auth/refresh`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ refreshToken }),
      })
      if (!res.ok) return false
      auth.setFromResponse(await res.json() as AuthResponse)
      return true
    } catch {
      return false
    }
  })()
  refreshInFlight.finally(() => { refreshInFlight = null })
  return refreshInFlight
}

async function request<T>(path: string, init?: RequestInit, retried = false): Promise<T> {
  const headers = new Headers(init?.headers)
  headers.set('Content-Type', 'application/json')

  const token = auth.getToken()
  if (token) headers.set('Authorization', `Bearer ${token}`)

  const res = await fetch(`${API_BASE}${path}`, { ...init, headers })

  if (!res.ok) {
    // Access token expired → silently rotate via the refresh token and replay the
    // request once. Only fall back to logout when refresh itself fails.
    if (res.status === 401 && token && !retried && !NO_REFRESH.has(path)) {
      if (await tryRefresh()) return request<T>(path, init, true)
    }
    if (res.status === 401 && auth.getToken()) {
      auth.clear()
      disconnectRealtime()
      window.location.replace('/login')
    }
    const message = await extractError(res)
    throw new ApiError(res.status, message)
  }

  return res.status === 204 ? (undefined as T) : await res.json()
}

async function extractError(res: Response): Promise<string> {
  try {
    const body = await res.json()
    return body.error ?? body.title ?? body.message ?? res.statusText
  } catch {
    return res.statusText || `HTTP ${res.status}`
  }
}

// ---- API ----

export const api = {
  auth: {
    login: (data: LoginRequest) =>
      request<AuthResponse>('/api/auth/login', { method: 'POST', body: JSON.stringify(data) }),

    register: (data: RegisterRequest) =>
      request<AuthResponse>('/api/auth/register', { method: 'POST', body: JSON.stringify(data) }),

    changePassword: (data: { currentPassword: string; newPassword: string }) =>
      request<void>('/api/auth/change-password', { method: 'POST', body: JSON.stringify(data) }),

    // Revokes the refresh token server-side so it can't be rotated after logout.
    logout: (refreshToken: string) =>
      request<void>('/api/auth/logout', { method: 'POST', body: JSON.stringify({ refreshToken }) }),
  },

  staff: {
    getAll: () =>
      request<StaffMember[]>('/api/staff'),

    getById: (id: string) =>
      request<StaffMember>(`/api/staff/${id}`),

    getRoles: () =>
      request<Role[]>('/api/staff/roles'),

    create: (data: CreateStaffRequest) =>
      request<StaffMember>('/api/staff', { method: 'POST', body: JSON.stringify(data) }),

    update: (id: string, data: UpdateStaffRequest) =>
      request<StaffMember>(`/api/staff/${id}`, { method: 'PUT', body: JSON.stringify(data) }),

    deactivate: (id: string) =>
      request<void>(`/api/staff/${id}`, { method: 'DELETE' }),

    setPermissions: (id: string, permissions: string[]) =>
      request<StaffMember>(`/api/staff/${id}/permissions`, { method: 'PUT', body: JSON.stringify({ permissions }) }),
  },

  restaurant: {
    getMe: () =>
      request<RestaurantInfo>('/api/restaurants/me'),

    updateMe: (data: UpdateRestaurantRequest) =>
      request<RestaurantInfo>('/api/restaurants/me', { method: 'PUT', body: JSON.stringify(data) }),
  },

  menu: {
    getAll: () => request<MenuCategoryDto[]>('/api/menu'),
    createCategory: (data: CreateCategoryRequest) => request<MenuCategoryDto>('/api/menu/categories', { method: 'POST', body: JSON.stringify(data) }),
    updateCategory: (id: string, data: UpdateCategoryRequest) => request<MenuCategoryDto>(`/api/menu/categories/${id}`, { method: 'PUT', body: JSON.stringify(data) }),
    deleteCategory: (id: string) => request<void>(`/api/menu/categories/${id}`, { method: 'DELETE' }),
    createItem: (data: CreateMenuItemRequest) => request<MenuItemDto>('/api/menu/items', { method: 'POST', body: JSON.stringify(data) }),
    updateItem: (id: string, data: UpdateMenuItemRequest) => request<MenuItemDto>(`/api/menu/items/${id}`, { method: 'PUT', body: JSON.stringify(data) }),
    toggleItem: (id: string) => request<MenuItemDto>(`/api/menu/items/${id}/toggle`, { method: 'PATCH' }),
    deleteItem: (id: string) => request<void>(`/api/menu/items/${id}`, { method: 'DELETE' }),
    getRecipe: (itemId: string) => request<RecipeDto>(`/api/menu/items/${itemId}/recipe`),
    setRecipe: (itemId: string, data: SetRecipeRequest) =>
      request<RecipeDto>(`/api/menu/items/${itemId}/recipe`, { method: 'PUT', body: JSON.stringify(data) }),
  },

  tables: {
    getAll: () => request<TableDto[]>('/api/tables'),
    create: (data: CreateTableRequest) => request<TableDto>('/api/tables', { method: 'POST', body: JSON.stringify(data) }),
    update: (id: string, data: UpdateTableRequest) => request<TableDto>(`/api/tables/${id}`, { method: 'PUT', body: JSON.stringify(data) }),
    setStatus: (id: string, status: TableStatus) =>
      request<TableDto>(`/api/tables/${id}/status`, { method: 'PATCH', body: JSON.stringify({ status }) }),
    delete: (id: string) => request<void>(`/api/tables/${id}`, { method: 'DELETE' }),
  },

  reservations: {
    getAll: (params?: { from?: string; to?: string; status?: ReservationStatus }) => {
      const q = new URLSearchParams()
      if (params?.from) q.set('from', params.from)
      if (params?.to) q.set('to', params.to)
      if (params?.status) q.set('status', params.status)
      const qs = q.toString()
      return request<ReservationDto[]>(`/api/reservations${qs ? `?${qs}` : ''}`)
    },
    getById: (id: string) => request<ReservationDto>(`/api/reservations/${id}`),
    create: (data: CreateReservationRequest) =>
      request<ReservationDto>('/api/reservations', { method: 'POST', body: JSON.stringify(data) }),
    update: (id: string, data: UpdateReservationRequest) =>
      request<ReservationDto>(`/api/reservations/${id}`, { method: 'PUT', body: JSON.stringify(data) }),
    setStatus: (id: string, status: ReservationStatus) =>
      request<ReservationDto>(`/api/reservations/${id}/status`, { method: 'PATCH', body: JSON.stringify({ status }) }),
    delete: (id: string) =>
      request<void>(`/api/reservations/${id}`, { method: 'DELETE' }),
  },

  orders: {
    getAll: () => request<OrderDto[]>('/api/orders'),
    getById: (id: string) => request<OrderDto>(`/api/orders/${id}`),
    create: (data: CreateOrderRequest) => request<OrderDto>('/api/orders', { method: 'POST', body: JSON.stringify(data) }),
    addItem: (orderId: string, data: AddOrderItemRequest) => request<OrderDto>(`/api/orders/${orderId}/items`, { method: 'POST', body: JSON.stringify(data) }),
    removeItem: (orderId: string, itemId: string) => request<OrderDto>(`/api/orders/${orderId}/items/${itemId}`, { method: 'DELETE' }),
    markPaid: (id: string, paymentMethod: PaymentMethod, opts?: { useDeposit?: boolean; applyCashback?: boolean }) =>
      request<OrderDto>(`/api/orders/${id}/status`, {
        method: 'PATCH',
        body: JSON.stringify({
          status: 'Paid',
          paymentMethod,
          useDeposit: opts?.useDeposit ?? false,
          applyCashback: opts?.applyCashback ?? false,
        }),
      }),
    assignClient: (id: string, clientId: string | null) =>
      request<OrderDto>(`/api/orders/${id}/client`, { method: 'PATCH', body: JSON.stringify({ clientId }) }),
    getBill: (id: string) => request<BillPreviewDto>(`/api/orders/${id}/bill`),
    cancel: (id: string) => request<OrderDto>(`/api/orders/${id}/cancel`, { method: 'PATCH' }),
    updateItemStatus: (orderId: string, itemId: string, status: string) => request<OrderDto>(`/api/orders/${orderId}/items/${itemId}/status`, { method: 'PATCH', body: JSON.stringify({ status }) }),
  },

  kitchen: {
    // Cross-order queue of items in Pending / Preparing / Ready status, oldest first.
    queue: () => request<KitchenQueueItemDto[]>('/api/kitchen/queue'),
    // Atomically advance every kitchen-side item on a ticket to Ready or Served.
    // Passing a station narrows the bump to that station's items.
    bump: (orderId: string, status: 'Ready' | 'Served', station?: Station) =>
      request<OrderDto>(`/api/kitchen/orders/${orderId}/bump`, { method: 'POST', body: JSON.stringify({ status, station }) }),
    // Undo a bump: flip the named Served items back to Ready (ticket reappears).
    recall: (orderId: string, itemIds: string[]) =>
      request<OrderDto>(`/api/kitchen/orders/${orderId}/recall`, { method: 'POST', body: JSON.stringify({ itemIds }) }),
  },

  reports: {
    // Bounds are half-open [from, to). Caller passes ISO datetime strings.
    summary: (from: string, to: string) =>
      request<ReportSummaryDto>(`/api/reports/summary?from=${encodeURIComponent(from)}&to=${encodeURIComponent(to)}`),
    topItems: (from: string, to: string, limit = 5) =>
      request<TopItemDto[]>(`/api/reports/top-items?from=${encodeURIComponent(from)}&to=${encodeURIComponent(to)}&limit=${limit}`),
    topServers: (from: string, to: string, limit = 5) =>
      request<TopServerDto[]>(`/api/reports/top-servers?from=${encodeURIComponent(from)}&to=${encodeURIComponent(to)}&limit=${limit}`),
    revenueTrend: (from: string, to: string) =>
      request<RevenuePointDto[]>(`/api/reports/revenue-trend?from=${encodeURIComponent(from)}&to=${encodeURIComponent(to)}`),
    hourlyBreakdown: (from: string, to: string) =>
      request<HourlyPointDto[]>(`/api/reports/hourly-breakdown?from=${encodeURIComponent(from)}&to=${encodeURIComponent(to)}`),
  },

  products: {
    getAll: (params?: { category?: string; lowStockOnly?: boolean; includeArchived?: boolean }) => {
      const q = new URLSearchParams()
      if (params?.category) q.set('category', params.category)
      if (params?.lowStockOnly) q.set('lowStockOnly', 'true')
      if (params?.includeArchived) q.set('includeArchived', 'true')
      const qs = q.toString()
      return request<ProductDto[]>(`/api/products${qs ? `?${qs}` : ''}`)
    },
    getCategories: () => request<string[]>('/api/products/categories'),
    getById: (id: string) => request<ProductDto>(`/api/products/${id}`),
    create: (data: CreateProductRequest) =>
      request<ProductDto>('/api/products', { method: 'POST', body: JSON.stringify(data) }),
    update: (id: string, data: UpdateProductRequest) =>
      request<ProductDto>(`/api/products/${id}`, { method: 'PUT', body: JSON.stringify(data) }),
    archive: (id: string) =>
      request<void>(`/api/products/${id}`, { method: 'DELETE' }),
    getMovements: (id: string, limit = 50) =>
      request<StockMovementDto[]>(`/api/products/${id}/movements?limit=${limit}`),
    addMovement: (id: string, data: AddStockMovementRequest) =>
      request<ProductDto>(`/api/products/${id}/movements`, { method: 'POST', body: JSON.stringify(data) }),
  },

  cashRegister: {
    summary: (from: string, to: string) =>
      request<CashRegisterSummaryDto>(`/api/cash-register/summary?from=${encodeURIComponent(from)}&to=${encodeURIComponent(to)}`),
    transactions: (params: { from: string; to: string; type?: CashTransactionType; method?: PaymentMethod; limit?: number }) => {
      const q = new URLSearchParams()
      q.set('from', params.from)
      q.set('to', params.to)
      if (params.type) q.set('type', params.type)
      if (params.method) q.set('method', params.method)
      if (params.limit) q.set('limit', String(params.limit))
      return request<CashRegisterTransactionDto[]>(`/api/cash-register/transactions?${q.toString()}`)
    },
    recordManual: (data: RecordManualOpRequest) =>
      request<CashRegisterTransactionDto>('/api/cash-register/manual', { method: 'POST', body: JSON.stringify(data) }),
  },

  clients: {
    getAll: (params?: { search?: string; includeArchived?: boolean }) => {
      const q = new URLSearchParams()
      if (params?.search) q.set('search', params.search)
      if (params?.includeArchived) q.set('includeArchived', 'true')
      const qs = q.toString()
      return request<ClientDto[]>(`/api/clients${qs ? `?${qs}` : ''}`)
    },
    getById: (id: string) => request<ClientDto>(`/api/clients/${id}`),
    create: (data: CreateClientRequest) =>
      request<ClientDto>('/api/clients', { method: 'POST', body: JSON.stringify(data) }),
    update: (id: string, data: UpdateClientRequest) =>
      request<ClientDto>(`/api/clients/${id}`, { method: 'PUT', body: JSON.stringify(data) }),
    archive: (id: string) => request<void>(`/api/clients/${id}`, { method: 'DELETE' }),
    getTransactions: (id: string, limit = 50) =>
      request<ClientTransactionDto[]>(`/api/clients/${id}/transactions?limit=${limit}`),
    deposit: (id: string, data: ClientDepositRequest) =>
      request<ClientDto>(`/api/clients/${id}/deposit`, { method: 'POST', body: JSON.stringify(data) }),
    withdraw: (id: string, data: ClientWithdrawalRequest) =>
      request<ClientDto>(`/api/clients/${id}/withdraw`, { method: 'POST', body: JSON.stringify(data) }),
  },

  activityLog: {
    get: (params: { from: string; to: string; category?: ActivityCategory; userId?: string; entityType?: string; limit?: number }) => {
      const q = new URLSearchParams()
      q.set('from', params.from)
      q.set('to', params.to)
      if (params.category) q.set('category', params.category)
      if (params.userId) q.set('userId', params.userId)
      if (params.entityType) q.set('entityType', params.entityType)
      if (params.limit) q.set('limit', String(params.limit))
      return request<ActivityLogEntryDto[]>(`/api/activity-log?${q.toString()}`)
    },
  },

  schedule: {
    // Server-side enforces: callers without ManageSchedules see only their own shifts
    // regardless of the userId param. So passing nothing = "everyone I'm allowed to see".
    get: (params: { from: string; to: string; userId?: string }) => {
      const q = new URLSearchParams()
      q.set('from', params.from)
      q.set('to', params.to)
      if (params.userId) q.set('userId', params.userId)
      return request<ShiftDto[]>(`/api/schedule?${q.toString()}`)
    },
    getById: (id: string) => request<ShiftDto>(`/api/schedule/${id}`),
    create: (data: CreateShiftRequest) =>
      request<ShiftDto>('/api/schedule', { method: 'POST', body: JSON.stringify(data) }),
    update: (id: string, data: UpdateShiftRequest) =>
      request<ShiftDto>(`/api/schedule/${id}`, { method: 'PUT', body: JSON.stringify(data) }),
    delete: (id: string) =>
      request<void>(`/api/schedule/${id}`, { method: 'DELETE' }),
  },
}
