import { clsx, type ClassValue } from 'clsx'
import { twMerge } from 'tailwind-merge'

/** shadcn's class-merging helper. Unused until the design pass; the generated components expect it here. */
export function cn(...inputs: ClassValue[]) {
  return twMerge(clsx(inputs))
}
