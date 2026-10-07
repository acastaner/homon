import { formatStamp } from '@/lib/format-stamp'

/**
 * A timestamp as a `<time>`: readable text for people (`formatStamp`, in their locale), the exact
 * ISO instant for machines (`dateTime`) and for anyone who hovers (`title`), since the readable
 * form drops the year, the seconds and the offset.
 */
export function Stamp({ iso, time = true }: { iso: string; time?: boolean }) {
  return (
    <time dateTime={iso} title={iso}>
      {formatStamp(iso, { time })}
    </time>
  )
}
