---
name: blog-publish
description: "Publishes a blog post to smfworks-site by writing the Markdown file, generating a hero image via Together.ai FLUX.2-pro, committing, and pushing to GitHub. USE FOR: publishing Jeff's Journal or any SMF Works blog post. DO NOT USE FOR: editing existing posts, non-blog content, or anything outside the smfworks-site repo."
metadata:
  openclaw:
    emoji: "📝"
    requires:
      bins: ["node"]
---

# blog-publish

## When to Use

✅ Use when:

- Publishing a new blog post to Jeff's Journal or any SMF Works blog
- The full workflow is needed: write → image → commit → push
- A cron job or interactive session needs a single publish command
- You want the same reliable output format every time

## When NOT to Use

❌ Don't use when:

- Editing an existing post (just edit the .md file and push manually)
- Publishing non-blog content (use the appropriate workflow for that)
- The hero image already exists (skip the image generation step)

## Workflow

1. **Write the post.** Use `node {baseDir}/scripts/publish.mjs <title> <content-file>`
   - Or pass content via stdin for one-shot publishing
2. **The script handles:**
   - Slug generation from title
   - Today's date in ISO format
   - Frontmatter assembly (slug, title, excerpt, date, categories, readTime, image, author)
   - Saves to `content/jeffs-journal/<slug>.md`
3. **Generate hero image.** Calls `scripts/generate-hero.mjs` in the site repo
4. **Git add, commit, pull --rebase, and push.** Rebases on origin/main before pushing to avoid fetch-first rejections.
5. **Report.** Outputs the published URL

## Examples

### Full publish (content passed via file)
```bash
node skills/blog-publish/scripts/publish.mjs \
  "My Blog Post Title" \
  /path/to/post-content.md \
  --categories "Microsoft 365, AI Agents" \
  --author "Jeff (AI)"
```

### Content via stdin (one-shot)
```bash
cat post.md | node skills/blog-publish/scripts/publish.mjs "Title" --stdin --categories "AI"
```

### Dry run (writes files locally, skips git push)
```bash
node skills/blog-publish/scripts/publish.mjs \
  "My Blog Post Title" \
  /path/to/post-content.md \
  --categories "OpenClaw, Windows" \
  --author "Jeff (AI)" \
  --dry-run
```

**Note:** Use `--categories "cat1, cat2"` (space-separated string) or `--categories="cat1,cat2"` (equals form). Both are accepted.

## Privacy

- Git credentials stored in `~/.git-credentials` (file-based, no GUI popups)
- Together.ai API key stored in `smfworks-site/scripts/generate-hero.mjs`
- All content stays in local workspace until pushed to public GitHub

## Troubleshooting

- **Push fails** → check `~/.git-credentials` has valid token
- **Image generation fails** → check Together.ai API key and model availability
- **Content loader not picking up post** → verify frontmatter YAML is valid
- **Vercel not deploying** → check GitHub Actions or Vercel dashboard
