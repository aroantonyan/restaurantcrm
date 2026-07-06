import { useEffect, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { z } from 'zod'
import { useTranslation } from 'react-i18next'
import { api, ApiError, type MenuCategoryDto, type Station } from '../../lib/api'
import { usePermissions } from '../../hooks/usePermissions'
import Field from '../../components/Field'
import SubmitButton from '../../components/SubmitButton'
import AppHeader from '../../components/AppHeader'
import Sheet from '../../components/Sheet'
import StationPicker from '../../components/StationPicker'
import { SkeletonRow } from '../../components/Skeleton'
import SharedEmptyState from '../../components/EmptyState'
import { BookOpen } from 'lucide-react'

interface CategoryFormProps {
  onClose: () => void
  onSaved: () => void
  nextSortOrder: number
}

function CategoryFormSheet({ onClose, onSaved, nextSortOrder }: CategoryFormProps) {
  const { t } = useTranslation()
  const [serverError, setServerError] = useState<string | null>(null)
  const [station, setStation] = useState<Station>('Kitchen')

  const schema = z.object({
    name: z.string().min(1, { error: t('auth.errors.required') }).max(100, { error: t('auth.errors.tooLong') }),
    description: z.string().max(500, { error: t('auth.errors.tooLong') }).optional(),
  })
  type FormData = z.infer<typeof schema>

  const { register, handleSubmit, formState: { errors, isSubmitting } } = useForm<FormData>({
    resolver: zodResolver(schema),
    defaultValues: { name: '', description: '' },
  })

  const onSubmit = async (data: FormData) => {
    setServerError(null)
    try {
      await api.menu.createCategory({
        name: data.name,
        description: data.description?.trim() || undefined,
        sortOrder: nextSortOrder,
        station,
      })
      onSaved()
    } catch (e) {
      setServerError(e instanceof ApiError ? e.message : t('menu.errors.saveFailed'))
    }
  }

  return (
    <Sheet open onClose={onClose} title={t('menu.addCategory')}>
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
        <SubmitButton loading={isSubmitting}>{t('menu.addCategory')}</SubmitButton>
      </form>
    </Sheet>
  )
}

export default function MenuPage() {
  const { t } = useTranslation()
  const navigate = useNavigate()
  const perm = usePermissions()

  const [categories, setCategories] = useState<MenuCategoryDto[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [addingCategory, setAddingCategory] = useState(false)

  const canManage = perm.has('ManageMenu')

  const load = async () => {
    setLoading(true)
    setError(null)
    try {
      const data = await api.menu.getAll()
      setCategories(data)
    } catch {
      setError(t('menu.errors.loadFailed'))
    } finally {
      setLoading(false)
    }
  }

  useEffect(() => { load() }, [])

  const totalItems = categories.reduce((s, c) => s + c.items.length, 0)

  return (
    <main className="page-enter h-full overflow-y-auto pb-7">
      <AppHeader
        title={t('menu.title')}
        subtitle={`${categories.length} ${t('menu.categoriesWord', { defaultValue: 'categories' })} · ${t('menu.itemCount', { count: totalItems })}`}
        trailing={canManage ? (
          <button
            type="button"
            onClick={() => setAddingCategory(true)}
            aria-label={t('menu.addCategory')}
            className="w-9 h-9 rounded-full bg-accent text-white border-0 flex items-center justify-center tappable"
          >
            <PlusIcon />
          </button>
        ) : undefined}
      />

      <div className="px-5 flex flex-col gap-2">
        {loading ? (
          <>{[0, 1, 2, 3].map(i => <SkeletonRow key={i} />)}</>
        ) : error ? (
          <ErrorState message={error} onRetry={load} />
        ) : categories.length === 0 ? (
          <EmptyState />
        ) : (
          categories.map((cat, idx) => {
            const total = cat.items.length
            const unavailable = cat.items.filter(i => !i.isAvailable).length
            return (
              <button
                key={cat.id}
                type="button"
                onClick={() => navigate(`/menu/categories/${cat.id}`)}
                className="tappable item-enter w-full bg-card border-0 rounded-[18px] py-3.5 px-3.5 flex items-center gap-3 text-left"
                style={{
                  animationDelay: `${idx * 35}ms`,
                  boxShadow: '0 1px 0 rgba(15,15,16,.04), 0 1px 3px rgba(15,15,16,.05)',
                }}
              >
                <div className="w-[46px] h-[46px] rounded-[14px] bg-bg flex items-center justify-center text-[22px] shrink-0">
                  {pickEmoji(cat.name)}
                </div>
                <div className="flex-1 min-w-0">
                  <p className="m-0 text-[15.5px] font-semibold truncate"
                     style={{ letterSpacing: '-0.005em' }}>
                    {cat.name}
                  </p>
                  {cat.description && (
                    <p className="m-0 mt-0.5 text-[12.5px] text-fg-3 truncate">{cat.description}</p>
                  )}
                  <p className="m-0 mt-0.5 text-[12.5px] text-fg-3">
                    {t('menu.itemCount', { count: total })}
                    {unavailable > 0 && (
                      <span className="text-warn ml-1.5">· {t('menu.unavailableCount', { count: unavailable })}</span>
                    )}
                  </p>
                </div>
                <span className="text-fg-4 shrink-0"><ChevronIcon /></span>
              </button>
            )
          })
        )}
      </div>

      {addingCategory && (
        <CategoryFormSheet
          onClose={() => setAddingCategory(false)}
          onSaved={() => { setAddingCategory(false); load() }}
          nextSortOrder={categories.length + 1}
        />
      )}
    </main>
  )
}

function EmptyState() {
  const { t } = useTranslation()
  return <SharedEmptyState icon={BookOpen} title={t('menu.noCategories')} hint={t('menu.noCategoriesHint')} />
}

function ErrorState({ message, onRetry }: { message: string; onRetry: () => void }) {
  const { t } = useTranslation()
  return (
    <div className="flex flex-col items-center text-center px-6 py-16 gap-3">
      <p className="m-0 text-sm text-danger">{message}</p>
      <button
        type="button"
        onClick={onRetry}
        className="px-4 py-2 rounded-xl bg-muted text-fg-2 text-sm font-semibold tappable border-0"
      >
        {t('common.retry')}
      </button>
    </div>
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
function ChevronIcon() {
  return (
    <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
      <polyline points="9 18 15 12 9 6" />
    </svg>
  )
}

function pickEmoji(name: string): string {
  const n = name.toLowerCase()
  if (n.includes('appet') || n.includes('starter')) return '🥗'
  if (n.includes('soup')) return '🍲'
  if (n.includes('main') || n.includes('grill') || n.includes('meat')) return '🍖'
  if (n.includes('salad')) return '🥬'
  if (n.includes('drink') || n.includes('beverage')) return '🥤'
  if (n.includes('wine') || n.includes('cocktail') || n.includes('bar')) return '🍷'
  if (n.includes('beer')) return '🍺'
  if (n.includes('coffee') || n.includes('tea')) return '☕'
  if (n.includes('dessert') || n.includes('sweet')) return '🍰'
  if (n.includes('pizza')) return '🍕'
  if (n.includes('pasta') || n.includes('noodle')) return '🍝'
  if (n.includes('burger')) return '🍔'
  if (n.includes('seafood') || n.includes('fish')) return '🐟'
  if (n.includes('breakfast')) return '🍳'
  if (n.includes('bread')) return '🥖'
  return '🍽️'
}
