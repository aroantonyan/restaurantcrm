import { useTranslation } from 'react-i18next'
import type { Station } from '../lib/api'

/**
 * Segmented Kitchen/Bar toggle used in the menu category forms — decides which
 * prep station the category's items appear on in the Kitchen Display.
 */
export default function StationPicker({ value, onChange }: { value: Station; onChange: (s: Station) => void }) {
  const { t } = useTranslation()
  return (
    <div className="flex flex-col gap-1.5">
      <span className="text-[11.5px] text-fg-3 uppercase font-bold px-1" style={{ letterSpacing: '0.06em' }}>
        {t('menu.station')}
      </span>
      <div className="flex rounded-xl bg-muted p-1 gap-1">
        {(['Kitchen', 'Bar'] as const).map(s => (
          <button
            key={s}
            type="button"
            onClick={() => onChange(s)}
            aria-pressed={value === s}
            className={`flex-1 py-2.5 rounded-lg text-[13.5px] font-semibold border-0 tappable ${
              value === s
                ? 'bg-card text-fg shadow-[0_1px_0_rgba(15,15,16,.04),0_1px_3px_rgba(15,15,16,.08)]'
                : 'bg-transparent text-fg-3'
            }`}
          >
            {t(`menu.stationOption.${s}`)}
          </button>
        ))}
      </div>
    </div>
  )
}
