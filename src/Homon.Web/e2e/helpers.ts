import { expect, type Page } from '@playwright/test'

/** Every reader-facing page, as an address and a thing to wait for. */
export const READER_ROUTES = [
  { path: '/', name: 'the dashboard' },
  { path: '/pages/welcome', name: 'a page' },
  { path: '/admin/sign-in', name: 'the sign-in form' },
] as const

/** Every administrator page. */
export const ADMIN_ROUTES = [
  { path: '/admin', name: 'the admin home' },
  { path: '/admin/probes', name: 'probes' },
  { path: '/admin/links', name: 'links' },
  { path: '/admin/pages', name: 'pages' },
  { path: '/admin/api-keys', name: 'API keys' },
] as const

/**
 * Asserts that nothing on the page sticks out sideways.
 *
 * `documentElement.scrollWidth` against `clientWidth` is the whole check. The 1px tolerance
 * is for sub-pixel rounding on fractional device pixel ratios, not slack — a real overflow
 * is tens or hundreds of pixels, never one.
 */
export async function expectNoHorizontalOverflow(page: Page) {
  const { scrollWidth, clientWidth, widest } = await page.evaluate(() => {
    // Name the widest offending element, so a failure says what to look at rather than only
    // that the number is wrong.
    const viewport = document.documentElement.clientWidth
    let offender: string | null = null
    let worst = viewport

    for (const element of Array.from(document.querySelectorAll('body *'))) {
      const box = element.getBoundingClientRect()

      if (box.width === 0 && box.height === 0) {
        continue
      }

      // Anything inside a deliberately scrollable container is out of scope.
      if (element.closest('.overflow-x-auto') !== null) {
        continue
      }

      if (box.right > worst) {
        worst = box.right
        offender = `<${element.tagName.toLowerCase()} class="${String(element.className).slice(0, 80)}">`
      }
    }

    return {
      scrollWidth: document.documentElement.scrollWidth,
      clientWidth: viewport,
      widest: offender,
    }
  })

  expect(
    scrollWidth,
    `The document is ${String(scrollWidth)}px wide in a ${String(clientWidth)}px viewport. ` +
      `Widest offender: ${widest ?? '(none found outside a scroll container)'}`,
  ).toBeLessThanOrEqual(clientWidth + 1)
}

/**
 * Asserts that no two elements from `selector` overlap one another. Overflow is the cause;
 * overlap is the symptom a reader actually sees, and a cell can be squeezed to nothing
 * without the document ever growing.
 */
export async function expectNoOverlap(page: Page, selector: string) {
  const collisions = await page.evaluate((query) => {
    const boxes = Array.from(document.querySelectorAll(query))
      .map((element) => ({
        text: (element.textContent ?? '').trim().slice(0, 30),
        box: element.getBoundingClientRect(),
      }))
      .filter(({ box }) => box.width > 0 && box.height > 0)

    const found: string[] = []

    for (let i = 0; i < boxes.length; i += 1) {
      for (let j = i + 1; j < boxes.length; j += 1) {
        const a = boxes[i].box
        const b = boxes[j].box
        const overlapX = Math.min(a.right, b.right) - Math.max(a.left, b.left)
        const overlapY = Math.min(a.bottom, b.bottom) - Math.max(a.top, b.top)

        // A shared border is not a collision; two characters of shared column is.
        if (overlapX > 2 && overlapY > 2) {
          found.push(`"${boxes[i].text}" over "${boxes[j].text}"`)
        }
      }
    }

    return found
  }, selector)

  expect(collisions, `Overlapping ${selector}: ${collisions.join('; ')}`).toEqual([])
}

/**
 * Asserts every control in `selector` is big enough to hit with a thumb. 40px is the floor;
 * the design pass may raise it.
 */
export async function expectTappable(page: Page, selector: string, minimum = 40) {
  const tooSmall = await page.evaluate(
    ({ query, floor }) =>
      Array.from(document.querySelectorAll(query))
        .map((element) => ({
          label: element.getAttribute('aria-label') ?? element.textContent?.trim() ?? '?',
          box: element.getBoundingClientRect(),
        }))
        .filter(({ box }) => box.width > 0 && (box.width < floor || box.height < floor))
        .map(({ label, box }) => `${label} is ${Math.round(box.width)}×${Math.round(box.height)}`),
    { query: selector, floor: minimum },
  )

  expect(tooSmall, `Targets under ${String(minimum)}px: ${tooSmall.join('; ')}`).toEqual([])
}
