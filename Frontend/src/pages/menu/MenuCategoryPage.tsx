import { useEffect, useState } from 'react'
import { useParams, useNavigate } from 'react-router-dom'
import { useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { z } from 'zod'
import { useTranslation } from 'react-i18next'
import { api, ApiError, type MenuCategoryDto, type MenuItemDto, type Station } from '../../lib/api'
import { usePermissions } from '../../hooks/usePermissions'
import { formatPrice } from '../../lib/format'
import Field from '../../components/Field'
import SubmitButton from '../../components/SubmitButton'
import AppHeader from '../../components/AppHeader'
import Sheet from '../../components/Sheet'
import StationPicker from '../../components/StationPicker'
import StickyActions from '../../components/StickyActions'
import PrimaryButton from '../../components/PrimaryButton'
import { SkeletonRow } from '../../components/Skeleton'

type EditingItem = MenuItemDto | 'new' | null

interface ItemFormProps {
  categoryId: string
  item: MenuItemDto | 'new'
  onClose: () => void
  onSaved: () => void
  onDeleted: () => void
}

function ItemFormSheet({ categoryId, item, onClose, onSaved, onDeleted }: ItemFormProps) {
  const { t } = useTranslation()
  const navigate = useNavigate()
  const perm = usePermissions()
  const [serverError, setServerError] = useState<string | null>(null)
  const [confirmDelete, setConfirmDelete] = useState(false)

  const schema = z.object({
    name:        z.string().min(1, { error: t('auth.errors.required') }).max(200, { error: t('auth.errors.tooLong') }),
    description: z.string().max(1000, { error: t('auth.errors.tooLong') }).optional(),
    price:       z.number({ error: t('auth.errors.required') }).positive({ error: t('menu.errors.pricePositive') }),
    isAvailable: z.boolean(),
  })
  type FormData = z.infer<typeof schema>

  const isNew = item === 'new'

  const { register, handleSubmit, formState: { errors, isSubmitting } } = useForm<FormData>({
    resolver: zodResolver(schema),
    defaultValues: isNew
      ? { name: '', description: '', price: 0, isAvailable: true }
      : { name: item.name, description: item.description ?? '', price: item.price, isAvailable: item.isAvailable },
  })

  const onSubmit = async (data: FormData) => {
    setServerError(null)
    try {
      if (isNew) {
        await api.menu.createItem({
          categoryId,
          name:        data.name,
          description: data.description || undefined,
          price:       data.price,
          isAvailable: data.isAvailable,
        })
      } else {
        await api.menu.updateItem(item.id, {
          categoryId:  item.categoryId,
          name:        data.name,
          description: data.description || undefined,
          price:       data.price,
          isAvailable: data.isAvailable,
        })
      }
      onSaved()
    } catch (e) {
      setServerError(e instanceof ApiError ? e.message : t('menu.errors.saveFailed'))
    }
  }

  const handleDelete = async () => {
    if (isNew) return
    try {
      await api.menu.deleteItem(item.id)
      onDeleted()
    } catch (e) {
      setServerError(e instanceof ApiError ? e.message : t('menu.errors.saveFailed'))
    }
  }

  return (
    <Sheet open onClose={onClose} title={isNew ? t('menu.addItem') : t('menu.editItem')} height="tall">
      <form onSubmit={handleSubmit(onSubmit)} className="flex flex-col gap-4">
        <Field
          label={t('menu.itemName')}
          enterKeyHint="next"
          autoFocus={isNew}
          {...register('name')}
          error={errors.name?.message}
        />
        <Field
          label={t('menu.description')}
          enterKeyHint="next"
          {...register('description')}
          error={errors.description?.message}
        />
        <Field
          label={t('menu.price')}
          type="number"
          inputMode="decimal"
          enterKeyHint="done"
          step="0.01"
          min="0"
          {...register('price', { valueAsNumber: true })}
          error={errors.price?.message}
        />
        <label className="flex items-center justify-between px-1 py-2 cursor-pointer">
          <span className="text-base text-fg">{t('menu.available')}</span>
          <input type="checkbox" className="w-5 h-5 rounded accent-[color:var(--color-accent)]" {...register('isAvailable')} />
        </label>

        {serverError && <p className="m-0 text-sm text-danger text-center">{serverError}</p>}
        <SubmitButton loading={isSubmitting}>
          {isNew ? t('menu.addItem') : t('common.submit')}
        </SubmitButton>

        {!isNew && perm.has('ViewWarehouse') && (
          <PrimaryButton
            kind="soft"
            type="button"
            onClick={() => navigate(`/menu/items/${item.id}/recipe`)}
          >
            📋 {t('recipe.openEditor')}
          </PrimaryButton>
        )}

        {!isNew && !confirmDelete && (
          <PrimaryButton kind="dangerSoft" type="button" onClick={() => setConfirmDelete(true)}>
            {t('menu.deleteItem')}
          </PrimaryButton>
        )}
        {!isNew && confirmDelete && (
          <PrimaryButton kind="danger" type="button" onClick={handleDelete}>
            {t('menu.deleteConfirm')}
          </PrimaryButton>
        )}
      </form>
    </Sheet>
  )
}

interface CategoryEditProps {
  category: MenuCategoryDto
  onClose: () => void
  onSaved: () => void
  onDeleted: () => void
}

function CategoryEditSheet({ category, onClose, onSaved, onDeleted }: CategoryEditProps) {
  const { t } = useTranslation()
  const [serverError, setServerError] = useState<string | null>(null)
  const [confirmDelete, setConfirmDelete] = useState(false)
  const [station, setStation] = useState<Station>(category.station)

  const schema = z.object({
    name: z.string().min(1, { error: t('auth.errors.required') }).max(100, { error: t('auth.errors.tooLong') }),
    description: z.string().max(500, { error: t('auth.errors.tooLong') }).optional(),
  })
  type FormData = z.infer<typeof schema>

  const { register, handleSubmit, formState: { errors, isSubmitting } } = useForm<FormData>({
    resolver: zodResolver(schema),
    defaultValues: { name: category.name, description: category.description ?? '' },
  })

  const onSubmit = async (data: FormData) => {
    setServerError(null)
    try {
      await api.menu.updateCategory(category.id, {
        name: data.name,
        description: data.description?.trim() || undefined,
        sortOrder: category.sortOrder,
        station,
      })
      onSaved()
    } catch (e) {
      setServerError(e instanceof ApiError ? e.message : t('menu.errors.saveFailed'))
    }
  }

  const handleDelete = async () => {
    try {
      await api.menu.deleteCategory(category.id)
      onDeleted()
    } catch (e) {
      setServerError(e instanceof ApiError ? e.message : t('menu.errors.saveFailed'))
    }
  }

  return (
    <Sheet open onClose={onClose} title={t('menu.editCategory')}>
      <form onSubmit={handleSubmit(onSubmit)} className="flex flex-col gap-4">
        <Field
          label={t('menu.categoryName')}
          enterKeyHint="next"
          autoFocus
          {...register('name')}
          error={errors.name?.message}
        />
        <Field
          label={t('menu.categoryDescription')}
          enterKeyHint="done"
          {...register('description')}
          error={errors.description?.message}
        />
        <StationPicker value={station} onChange={setStation} />
        {serverError && <p className="m-0 text-sm text-danger text-center">{serverError}</p>}
        <SubmitButton loading={isSubmitting}>{t('common.submit')}</SubmitButton>

        {!confirmDelete && (
          <PrimaryButton kind="dangerSoft" type="button" onClick={() => setConfirmDelete(true)}>
            {t('menu.deleteCategory')}
          </PrimaryButton>
        )}
        {confirmDelete && (
          <PrimaryButton kind="danger" type="button" onClick={handleDelete}>
            {t('menu.deleteCategoryConfirm')}
          </PrimaryButton>
        )}
      </form>
    </Sheet>
  )
}

export default function MenuCategoryPage() {
  const { id } = useParams<{ id: string }>()
  const { t } = useTranslation()
  const navigate = useNavigate()
  const perm = usePermissions()

  const [category, setCategory] = useState<MenuCategoryDto | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [editingItem, setEditingItem] = useState<EditingItem>(null)
  const [editingCategory, setEditingCategory] = useState(false)

  const canManage = perm.has('ManageMenu')

  const load = async () => {
    if (!id) return
    setLoading(true)
    setError(null)
    try {
      const all = await api.menu.getAll()
      const found = all.find(c => c.id === id)
      if (!found) throw new Error('Category not found')
      setCategory(found)
    } catch {
      setError(t('menu.errors.loadFailed'))
    } finally {
      setLoading(false)
    }
  }

  useEffect(() => { load() }, [id])

  const handleToggle = async (item: MenuItemDto, e: React.MouseEvent) => {
    e.stopPropagation()
    try {
      const updated = await api.menu.toggleItem(item.id)
      setCategory(prev => prev ? { ...prev, items: prev.items.map(i => i.id === updated.id ? updated : i) } : prev)
    } catch {
      // silent
    }
  }

  if (loading) {
    return (
      <main className="page-enter h-full overflow-y-auto pb-7">
        <AppHeader onBack={() => navigate('/menu')} title="…" />
        <div className="px-5 flex flex-col gap-2">
          {[0, 1, 2, 3].map(i => <SkeletonRow key={i} />)}
        </div>
      </main>
    )
  }

  if (error || !category) {
    return (
      <main className="page-enter h-full overflow-y-auto pb-7">
        <AppHeader onBack={() => navigate('/menu')} title={t('menu.title')} />
        <div className="flex flex-col items-center px-6 pt-16 text-center gap-3">
          <p className="m-0 text-sm text-danger">{error ?? t('menu.errors.loadFailed')}</p>
          <button
            type="button"
            onClick={() => navigate('/menu')}
            className="px-4 py-2 rounded-xl bg-muted text-fg-2 text-sm font-semibold tappable border-0"
          >
            {t('common.back')}
          </button>
        </div>
      </main>
    )
  }

  return (
    <div className="relative h-full overflow-hidden">
      <main className={`page-enter h-full overflow-y-auto ${canManage ? 'pb-24' : 'pb-7'}`}>
        <AppHeader
          onBack={() => navigate('/menu')}
          title={category.name}
          subtitle={t('menu.itemCount', { count: category.items.length })}
          trailing={canManage ? (
            <button
              type="button"
              onClick={() => setEditingCategory(true)}
              aria-label={t('menu.editCategory')}
              className="w-10 h-10 rounded-full bg-[rgba(15,15,16,0.05)] text-fg-2 flex items-center justify-center tappable border-0"
            >
              <PencilIcon />
            </button>
          ) : undefined}
        />

        {category.description && (
          <p className="m-0 mb-3 px-5 text-[13px] leading-snug text-fg-3">{category.description}</p>
        )}

        <div className="px-5">
          {category.items.length === 0 ? (
            <div className="flex flex-col items-center text-center pt-12 px-4 gap-2">
              <div className="text-[40px] mb-2" aria-hidden>🍽️</div>
              <p className="m-0 text-base font-semibold text-fg">{t('menu.noItems')}</p>
              <p className="m-0 text-sm text-fg-3">{t('menu.noItemsHint')}</p>
            </div>
          ) : (
            <div className="flex flex-col gap-2">
              {category.items.map((item, idx) => (
                <div
                  key={item.id}
                  className={`item-enter bg-card rounded-[18px] py-3 px-3.5 flex items-start gap-3 ${!item.isAvailable ? 'opacity-60' : ''}`}
                  style={{
                    animationDelay: `${idx * 30}ms`,
                    boxShadow: '0 1px 0 rgba(15,15,16,.04), 0 1px 3px rgba(15,15,16,.05)',
                  }}
                >
                  <div className="flex-1 min-w-0">
                    <p className="m-0 text-[15px] font-semibold truncate"
                       style={{ letterSpacing: '-0.005em' }}>
                      {item.name}
                    </p>
                    {item.description && (
                      <p className="m-0 mt-0.5 text-xs text-fg-3 clamp-2">{item.description}</p>
                    )}
                    <div className="flex items-center gap-2 mt-1.5">
                      <span className="text-[14px] font-bold tabular-nums">{formatPrice(item.price)}</span>
                      {!item.isAvailable && (
                        <span className="text-[10px] text-warn font-bold uppercase" style={{ letterSpacing: '0.04em' }}>
                          {t('menu.unavailable')}
                        </span>
                      )}
                    </div>
                  </div>
                  {canManage && (
                    <div className="flex flex-col gap-1.5 shrink-0">
                      <button
                        type="button"
                        onClick={() => setEditingItem(item)}
                        aria-label={t('menu.editItem')}
                        className="w-9 h-9 rounded-full bg-bg text-fg-2 flex items-center justify-center tappable border-0"
                      >
                        <PencilIcon />
                      </button>
                      <button
                        type="button"
                        onClick={e => handleToggle(item, e)}
                        aria-label={item.isAvailable ? t('menu.unavailable') : t('menu.available')}
                        className={`w-9 h-9 rounded-full bg-bg flex items-center justify-center tappable border-0 ${
                          item.isAvailable ? 'text-ok' : 'text-fg-3'
                        }`}
                      >
                        {item.isAvailable ? <CheckIcon /> : <EyeOffIcon />}
                      </button>
                    </div>
                  )}
                </div>
              ))}
            </div>
          )}
        </div>
      </main>

      {canManage && (
        <StickyActions>
          <PrimaryButton kind="primary" onClick={() => setEditingItem('new')} icon={<PlusIcon />}>
            {t('menu.addItem')}
          </PrimaryButton>
        </StickyActions>
      )}

      {editingItem !== null && (
        <ItemFormSheet
          categoryId={category.id}
          item={editingItem}
          onClose={() => setEditingItem(null)}
          onSaved={() => { setEditingItem(null); load() }}
          onDeleted={() => { setEditingItem(null); load() }}
        />
      )}

      {editingCategory && (
        <CategoryEditSheet
          category={category}
          onClose={() => setEditingCategory(false)}
          onSaved={() => { setEditingCategory(false); load() }}
          onDeleted={() => navigate('/menu', { replace: true })}
        />
      )}
    </div>
  )
}

function PencilIcon() {
  return (
    <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
      <path d="M11 4H4a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h14a2 2 0 0 0 2-2v-7" />
      <path d="m18.5 2.5 3 3L12 15l-4 1 1-4 9.5-9.5z" />
    </svg>
  )
}
function CheckIcon() {
  return (
    <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.5" strokeLinecap="round" strokeLinejoin="round">
      <polyline points="20 6 9 17 4 12" />
    </svg>
  )
}
function EyeOffIcon() {
  return (
    <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
      <path d="M17.94 17.94A10.07 10.07 0 0 1 12 20c-7 0-11-8-11-8a18.45 18.45 0 0 1 5.06-5.94M9.9 4.24A9.12 9.12 0 0 1 12 4c7 0 11 8 11 8a18.5 18.5 0 0 1-2.16 3.19" />
      <line x1="1" y1="1" x2="23" y2="23" />
    </svg>
  )
}
function PlusIcon() {
  return (
    <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.4" strokeLinecap="round" strokeLinejoin="round">
      <line x1="12" y1="5" x2="12" y2="19" />
      <line x1="5" y1="12" x2="19" y2="12" />
    </svg>
  )
}
