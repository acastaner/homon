import { describe, expect, it } from 'vitest'
import { render } from '@testing-library/react'

import { Sparkline } from '@/components/sparkline'

describe('Sparkline', () => {
  it('renders a path with one point per sample', () => {
    const { container } = render(<Sparkline samples={[11, 12, 10, 13, 9]} state="up" />)

    const path = container.querySelector('path')
    expect(path).toBeInTheDocument()

    // "M x y" then one "L x y" per remaining sample.
    const commandCount = path?.getAttribute('d')?.match(/[ML]/g)?.length
    expect(commandCount).toBe(5)
  })

  it('renders nothing for an empty sample list', () => {
    const { container } = render(<Sparkline samples={[]} state="up" />)

    expect(container).toBeEmptyDOMElement()
  })

  it('a single sample draws a level line rather than dividing by zero', () => {
    const { container } = render(<Sparkline samples={[42]} state="unknown" />)

    const path = container.querySelector('path')
    expect(path?.getAttribute('d')).toMatch(/^M/)
    expect(container.querySelector('circle')).toBeInTheDocument()
  })
})
