import { useEffect, useState, type FormEvent } from 'react'
import { useNavigate, useParams } from 'react-router'
import { EditorContent, useEditor, useEditorState, type Editor } from '@tiptap/react'
import StarterKit from '@tiptap/starter-kit'
import TiptapLink from '@tiptap/extension-link'
import {
  Bold,
  Code,
  ExternalLink,
  Italic,
  Link as LinkIcon,
  List,
  ListOrdered,
  Minus,
  Redo2,
  Strikethrough,
  TextQuote,
  Undo2,
} from 'lucide-react'

import { ALERT, BUTTON_PRIMARY, BUTTON_SECONDARY, FIELD_INPUT, FIELD_LABEL, PANEL } from '@/components/admin-classes'
import { AdminPageHeader } from '@/components/admin-page-header'
import { AdminSection } from '@/components/admin-section'
import { IconButton } from '@/components/icon-button'
import { Stamp } from '@/components/stamp'
import { problemDetail } from '@/lib/api'
import { useAdminPages, useCreatePage, usePage, useUpdatePage } from '@/lib/pages'
import { useDocumentTitle, pageTitle } from '@/lib/use-document-title'

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
        // The panel around it supplies the border; the editor only needs room to write in.
        class: 'prose min-h-[420px] px-6 py-5 outline-none',
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

  const savedPage = isNew ? undefined : editingPage.data

  return (
    <form onSubmit={onSubmit} aria-labelledby="page-form-heading" className="flex flex-col gap-6">
      <AdminPageHeader
        title={isNew ? 'New page' : `Edit ${title || 'page'}`}
        back={{ to: '/admin/pages', label: 'Pages' }}
        description={
          savedPage ? (savedPage.isPublished ? `Published at /pages/${savedPage.slug}` : 'Draft') : undefined
        }
      >
        {savedPage?.isPublished ? (
          <a
            href={`/pages/${savedPage.slug}`}
            target="_blank"
            rel="noopener noreferrer"
            className={BUTTON_SECONDARY}
          >
            View page
            <ExternalLink aria-hidden="true" size={14} strokeWidth={2} />
          </a>
        ) : null}
        <button type="submit" disabled={mutation.isPending} className={BUTTON_PRIMARY}>
          {isNew ? 'Create page' : 'Save changes'}
        </button>
      </AdminPageHeader>
      {mutation.isError ? (
        <p role="alert" className={ALERT}>
          {problemDetail(mutation.error) ?? 'Could not save the page. Try again.'}
        </p>
      ) : null}
      {/*
        Wrapping columns rather than a breakpoint: the body wants about 560px and the settings
        about 300, so the aside drops under the body exactly when the two no longer fit, at any
        width, without a media query to keep in step with the shell's own gutters.
      */}
      <div className="flex flex-wrap gap-6">
        <div className="min-w-0 flex-[999_1_560px]">
          <AdminSection id="page-form" heading="Body">
            <div className={PANEL}>
              <EditorToolbar editor={editor} />
              <EditorContent editor={editor} />
            </div>
          </AdminSection>
        </div>
        <div className="flex-[1_1_300px]">
          <AdminSection id="page-settings" heading="Settings">
            <div className={`${PANEL} flex flex-col gap-4 p-4`}>
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
                <span className="flex h-10 items-center rounded-md border border-line bg-bg focus-within:border-line-strong">
                  <span aria-hidden="true" className="mono pl-3 text-[13px] text-muted">
                    /pages/
                  </span>
                  <input
                    id="page-slug"
                    required
                    value={slug}
                    onChange={(event) => onSlugChange(event.target.value)}
                    className="mono h-full min-w-0 flex-1 bg-transparent pr-3 pl-0.5 text-[14px] text-text outline-none"
                  />
                </span>
                <span className="text-[13px] text-muted">
                  Lower-case letters, digits and hyphens. Changing it breaks old bookmarks.
                </span>
              </p>
              <p className="flex flex-col gap-1">
                <label className="flex items-center gap-2 text-[14px] text-text">
                  <input
                    type="checkbox"
                    checked={isPublished}
                    onChange={(event) => setIsPublished(event.target.checked)}
                    className="size-4 rounded border-line"
                  />
                  Published
                </label>
                <span className="text-[13px] text-muted">Readers can open it from the dashboard.</span>
              </p>
              {savedPage ? (
                <dl className="flex flex-col gap-1 border-t border-line pt-3 text-[13px]">
                  <div className="flex justify-between gap-3">
                    <dt className="text-muted">Created</dt>
                    <dd>
                      <Stamp iso={savedPage.createdAt} />
                    </dd>
                  </div>
                  <div className="flex justify-between gap-3">
                    <dt className="text-muted">Last saved</dt>
                    <dd>
                      <Stamp iso={savedPage.updatedAt} />
                    </dd>
                  </div>
                </dl>
              ) : null}
            </div>
          </AdminSection>
        </div>
      </div>
    </form>
  )
}

/**
 * One real button per allow-listed action — the extension list here and
 * `PageHtmlSanitizer.cs`'s allow-list move together (plans/007, Maintenance notes). No image
 * button: uploads are out of scope, and there is deliberately no raw-HTML escape hatch.
 *
 * Icon buttons, as the rest of the admin side has (plan 025's D8): the `aria-label`s are the
 * words they have always had, because the unit and e2e suites find them by name, and each button
 * repeats its label as a `title` for hover. The toggles carry `aria-pressed`, read with
 * `useEditorState` so the toolbar re-renders when the cursor moves into bold text; Undo, Redo and
 * Horizontal rule act once and have no pressed state.
 */
function EditorToolbar({ editor }: { editor: Editor | null }) {
  const active = useEditorState({
    editor,
    selector: ({ editor: current }) => ({
      bold: current?.isActive('bold') ?? false,
      italic: current?.isActive('italic') ?? false,
      strike: current?.isActive('strike') ?? false,
      code: current?.isActive('code') ?? false,
      h2: current?.isActive('heading', { level: 2 }) ?? false,
      h3: current?.isActive('heading', { level: 3 }) ?? false,
      h4: current?.isActive('heading', { level: 4 }) ?? false,
      bulletList: current?.isActive('bulletList') ?? false,
      orderedList: current?.isActive('orderedList') ?? false,
      blockquote: current?.isActive('blockquote') ?? false,
      link: current?.isActive('link') ?? false,
    }),
  })

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

  const disabled = !editor

  return (
    <div role="toolbar" aria-label="Formatting" className="flex flex-wrap items-center gap-0.5 border-b border-line p-1.5">
      <IconButton icon={Bold} label="Bold" disabled={disabled} aria-pressed={active?.bold ?? false} onClick={() => editor?.chain().focus().toggleBold().run()} />
      <IconButton icon={Italic} label="Italic" disabled={disabled} aria-pressed={active?.italic ?? false} onClick={() => editor?.chain().focus().toggleItalic().run()} />
      <IconButton icon={Strikethrough} label="Strikethrough" disabled={disabled} aria-pressed={active?.strike ?? false} onClick={() => editor?.chain().focus().toggleStrike().run()} />
      <IconButton icon={Code} label="Code" disabled={disabled} aria-pressed={active?.code ?? false} onClick={() => editor?.chain().focus().toggleCode().run()} />
      <ToolbarDivider />
      <IconButton label="Heading 2" disabled={disabled} aria-pressed={active?.h2 ?? false} onClick={() => editor?.chain().focus().toggleHeading({ level: 2 }).run()}>
        <span className="mono text-[12.5px] font-semibold">H2</span>
      </IconButton>
      <IconButton label="Heading 3" disabled={disabled} aria-pressed={active?.h3 ?? false} onClick={() => editor?.chain().focus().toggleHeading({ level: 3 }).run()}>
        <span className="mono text-[12.5px] font-semibold">H3</span>
      </IconButton>
      <IconButton label="Heading 4" disabled={disabled} aria-pressed={active?.h4 ?? false} onClick={() => editor?.chain().focus().toggleHeading({ level: 4 }).run()}>
        <span className="mono text-[12.5px] font-semibold">H4</span>
      </IconButton>
      <ToolbarDivider />
      <IconButton icon={List} label="Bullet list" disabled={disabled} aria-pressed={active?.bulletList ?? false} onClick={() => editor?.chain().focus().toggleBulletList().run()} />
      <IconButton icon={ListOrdered} label="Numbered list" disabled={disabled} aria-pressed={active?.orderedList ?? false} onClick={() => editor?.chain().focus().toggleOrderedList().run()} />
      <IconButton icon={TextQuote} label="Blockquote" disabled={disabled} aria-pressed={active?.blockquote ?? false} onClick={() => editor?.chain().focus().toggleBlockquote().run()} />
      <ToolbarDivider />
      <IconButton icon={LinkIcon} label="Link" disabled={disabled} aria-pressed={active?.link ?? false} onClick={runLink} />
      <IconButton icon={Minus} label="Horizontal rule" disabled={disabled} onClick={() => editor?.chain().focus().setHorizontalRule().run()} />
      <span className="flex-1" />
      <IconButton icon={Undo2} label="Undo" disabled={disabled} onClick={() => editor?.chain().focus().undo().run()} />
      <IconButton icon={Redo2} label="Redo" disabled={disabled} onClick={() => editor?.chain().focus().redo().run()} />
    </div>
  )
}

function ToolbarDivider() {
  return <span aria-hidden="true" className="mx-1.5 h-5 w-px bg-line" />
}
