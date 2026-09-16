import { useEffect, useState, type FormEvent } from 'react'
import { Link as RouterLink, useNavigate, useParams } from 'react-router'
import { EditorContent, useEditor, type Editor } from '@tiptap/react'
import StarterKit from '@tiptap/starter-kit'
import TiptapLink from '@tiptap/extension-link'

import { problemDetail } from '@/lib/api'
import { useAdminPages, useCreatePage, usePage, useUpdatePage } from '@/lib/pages'
import { useDocumentTitle, pageTitle } from '@/lib/use-document-title'

const PAGE_H1 = 'border-b border-line-strong pb-4 text-[22px] font-semibold -tracking-[0.01em] sm:text-[26px]'
const FIELD_LABEL = 'text-[13px] font-medium text-text'
const FIELD_INPUT =
  'h-10 w-full rounded-md border border-line bg-bg px-3 text-[14px] text-text outline-none focus:border-line-strong'
const BUTTON_SECONDARY =
  'inline-flex h-9 min-h-10 items-center justify-center rounded-md border border-line px-2.5 text-[13px] font-medium text-text hover:border-line-strong disabled:pointer-events-none disabled:opacity-50'
const BUTTON_PRIMARY =
  'inline-flex h-10 items-center justify-center rounded-md bg-text px-4 text-[14px] font-medium text-bg hover:opacity-90 disabled:pointer-events-none disabled:opacity-50'
const ALERT = 'rounded-md border border-down/40 bg-down-bg px-3 py-2 text-[14px] font-medium text-down'

/**
 * Turns a title into a slug suggestion: lower-case, non-alphanumeric runs collapsed to one
 * hyphen, no leading or trailing hyphen — the same shape `Page.SlugPattern` requires
 * server-side.
 */
function suggestSlug(title: string): string {
  return title
    .trim()
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, '-')
    .replace(/^-+|-+$/g, '')
}

/**
 * `/admin/pages/new` and `/admin/pages/:id` — the create and edit routes share this
 * component. There is no `GET /pages/{id}`, so editing looks up the page's slug from the
 * already-fetched admin list and reuses `usePage(slug)` to load the full body.
 */
export function AdminPageEditorPage() {
  const { id } = useParams()
  const navigate = useNavigate()
  const isNew = id === undefined

  const adminPages = useAdminPages()
  const editingSummary = isNew ? undefined : adminPages.data?.find((page) => page.id === id)
  const editingPage = usePage(editingSummary?.slug)

  const createPage = useCreatePage()
  const updatePage = useUpdatePage()
  const mutation = isNew ? createPage : updatePage

  const [slug, setSlug] = useState('')
  const [slugTouched, setSlugTouched] = useState(false)
  const [title, setTitle] = useState('')
  const [isPublished, setIsPublished] = useState(false)
  const [prefilled, setPrefilled] = useState(false)

  const editor = useEditor({
    extensions: [
      StarterKit.configure({ heading: { levels: [2, 3, 4] } }),
      TiptapLink.configure({ openOnClick: false, autolink: false }),
    ],
    content: '',
    editorProps: {
      attributes: {
        role: 'textbox',
        'aria-label': 'Body',
        class: 'prose min-h-[220px] rounded-md border border-line bg-bg px-3 py-2 outline-none focus:border-line-strong',
      },
    },
  })

  useDocumentTitle(pageTitle(isNew ? 'New page' : title || 'Edit page', 'Admin'))

  // Fills the form once the page being edited has loaded. Guarded by `prefilled` so a
  // background refetch (after a save) does not stomp on further edits.
  useEffect(() => {
    if (isNew || prefilled || !editingPage.data) {
      return
    }

    setSlug(editingPage.data.slug)
    setSlugTouched(true)
    setTitle(editingPage.data.title)
    setIsPublished(editingPage.data.isPublished)
    editor?.commands.setContent(editingPage.data.bodyHtml)
    setPrefilled(true)
  }, [isNew, prefilled, editingPage.data, editor])

  function onTitleChange(value: string) {
    setTitle(value)

    if (!slugTouched) {
      setSlug(suggestSlug(value))
    }
  }

  function onSlugChange(value: string) {
    setSlugTouched(true)
    setSlug(value)
  }

  function onSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()

    const trimmedSlug = slug.trim().toLowerCase()
    const trimmedTitle = title.trim()

    if (trimmedSlug.length === 0 || trimmedTitle.length === 0 || !editor) {
      return
    }

    const fields = { slug: trimmedSlug, title: trimmedTitle, bodyHtml: editor.getHTML(), isPublished }

    if (isNew) {
      createPage.mutate(fields, {
        // The response is what was actually stored (sanitised) — re-set the editor's
        // content from it so a stripped element is visibly gone, not silently different.
        onSuccess: (created) => {
          editor.commands.setContent(created.bodyHtml)
          navigate(`/admin/pages/${created.id}`, { replace: true })
        },
      })
      return
    }

    if (!id) {
      return
    }

    updatePage.mutate(
      { id, fields },
      { onSuccess: (updated) => editor.commands.setContent(updated.bodyHtml) },
    )
  }

  return (
    <>
      <h1 className={PAGE_H1}>{isNew ? 'New page' : `Edit ${title || 'page'}`}</h1>
      <p>
        <RouterLink to="/admin/pages" className="text-[14px] text-text underline decoration-line-strong underline-offset-[3px] hover:decoration-text">
          Back to pages
        </RouterLink>
      </p>
      <form onSubmit={onSubmit} aria-labelledby="page-form-heading" className="flex flex-col gap-4 rounded-md border border-line bg-surface p-5">
        <h2 id="page-form-heading" className="text-[15px] font-semibold">
          {isNew ? 'New page' : 'Edit page'}
        </h2>
        <p className="flex flex-col gap-1">
          <label htmlFor="page-title" className={FIELD_LABEL}>
            Title
          </label>
          <input
            id="page-title"
            required
            value={title}
            onChange={(event) => onTitleChange(event.target.value)}
            className={FIELD_INPUT}
          />
        </p>
        <p className="flex flex-col gap-1">
          <label htmlFor="page-slug" className={FIELD_LABEL}>
            Slug
          </label>
          <input
            id="page-slug"
            required
            value={slug}
            onChange={(event) => onSlugChange(event.target.value)}
            className={`${FIELD_INPUT} mono`}
          />
        </p>
        <p>
          <label className="flex items-center gap-2 text-[14px] text-text">
            <input
              type="checkbox"
              checked={isPublished}
              onChange={(event) => setIsPublished(event.target.checked)}
              className="size-4 rounded border-line"
            />
            Published
          </label>
        </p>
        <div className="flex flex-col gap-2">
          <EditorToolbar editor={editor} />
          <EditorContent editor={editor} />
        </div>
        {mutation.isError ? (
          <p role="alert" className={ALERT}>
            {problemDetail(mutation.error) ?? 'Could not save the page. Try again.'}
          </p>
        ) : null}
        <p>
          <button type="submit" disabled={mutation.isPending} className={BUTTON_PRIMARY}>
            {isNew ? 'Create page' : 'Save changes'}
          </button>
        </p>
      </form>
    </>
  )
}

/**
 * One real button per allow-listed action — the extension list here and
 * `PageHtmlSanitizer.cs`'s allow-list move together (plans/007, Maintenance notes). No image
 * button: uploads are out of scope, and there is deliberately no raw-HTML escape hatch.
 */
function EditorToolbar({ editor }: { editor: Editor | null }) {
  function runLink() {
    if (!editor) {
      return
    }

    const currentHref = editor.getAttributes('link').href as string | undefined
    const url = window.prompt('Link URL', currentHref ?? '')

    if (url === null) {
      return
    }

    const trimmed = url.trim()

    if (trimmed.length === 0) {
      editor.chain().focus().extendMarkRange('link').unsetLink().run()
      return
    }

    editor.chain().focus().extendMarkRange('link').setLink({ href: trimmed }).run()
  }

  return (
    <p className="flex flex-wrap gap-1.5 rounded-md border border-line bg-bg p-1.5">
      <button type="button" aria-label="Bold" disabled={!editor} onClick={() => editor?.chain().focus().toggleBold().run()} className={BUTTON_SECONDARY}>
        Bold
      </button>
      <button type="button" aria-label="Italic" disabled={!editor} onClick={() => editor?.chain().focus().toggleItalic().run()} className={BUTTON_SECONDARY}>
        Italic
      </button>
      <button
        type="button"
        aria-label="Strikethrough"
        disabled={!editor}
        onClick={() => editor?.chain().focus().toggleStrike().run()}
        className={BUTTON_SECONDARY}
      >
        Strikethrough
      </button>
      <button type="button" aria-label="Code" disabled={!editor} onClick={() => editor?.chain().focus().toggleCode().run()} className={BUTTON_SECONDARY}>
        Code
      </button>
      <button
        type="button"
        aria-label="Heading 2"
        disabled={!editor}
        onClick={() => editor?.chain().focus().toggleHeading({ level: 2 }).run()}
        className={BUTTON_SECONDARY}
      >
        Heading 2
      </button>
      <button
        type="button"
        aria-label="Heading 3"
        disabled={!editor}
        onClick={() => editor?.chain().focus().toggleHeading({ level: 3 }).run()}
        className={BUTTON_SECONDARY}
      >
        Heading 3
      </button>
      <button
        type="button"
        aria-label="Heading 4"
        disabled={!editor}
        onClick={() => editor?.chain().focus().toggleHeading({ level: 4 }).run()}
        className={BUTTON_SECONDARY}
      >
        Heading 4
      </button>
      <button
        type="button"
        aria-label="Bullet list"
        disabled={!editor}
        onClick={() => editor?.chain().focus().toggleBulletList().run()}
        className={BUTTON_SECONDARY}
      >
        Bullet list
      </button>
      <button
        type="button"
        aria-label="Numbered list"
        disabled={!editor}
        onClick={() => editor?.chain().focus().toggleOrderedList().run()}
        className={BUTTON_SECONDARY}
      >
        Numbered list
      </button>
      <button
        type="button"
        aria-label="Blockquote"
        disabled={!editor}
        onClick={() => editor?.chain().focus().toggleBlockquote().run()}
        className={BUTTON_SECONDARY}
      >
        Blockquote
      </button>
      <button type="button" aria-label="Link" disabled={!editor} onClick={runLink} className={BUTTON_SECONDARY}>
        Link
      </button>
      <button
        type="button"
        aria-label="Horizontal rule"
        disabled={!editor}
        onClick={() => editor?.chain().focus().setHorizontalRule().run()}
        className={BUTTON_SECONDARY}
      >
        Horizontal rule
      </button>
      <button type="button" aria-label="Undo" disabled={!editor} onClick={() => editor?.chain().focus().undo().run()} className={BUTTON_SECONDARY}>
        Undo
      </button>
      <button type="button" aria-label="Redo" disabled={!editor} onClick={() => editor?.chain().focus().redo().run()} className={BUTTON_SECONDARY}>
        Redo
      </button>
    </p>
  )
}
