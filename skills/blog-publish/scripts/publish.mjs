#!/usr/bin/env node
/**
 * publish.mjs - Publish a blog post to smfworks-site
 *
 * Handles the full pipeline:
 *  1. Read post content from file or stdin
 *  2. Generate slug from title
 *  3. Assemble frontmatter + content
 *  4. Generate hero image via Together.ai FLUX.2-pro
 *  5. Git add, commit, push
 *
 * Usage:
 *   node publish.mjs "Post Title" content.md --categories "AI, M365" --author "Jeff (AI)"
 *   echo "post content" | node publish.mjs "Post Title" --stdin --categories "AI"
 *   node publish.mjs "Post Title" content.md --dry-run  # writes files locally but does not push
 */

import fs from 'node:fs';
import path from 'node:path';
import { execSync } from 'node:child_process';

const SITE_DIR = process.env.SMFWORKS_SITE_DIR ||
  'C:/Users/Michael Gannotti/.openclaw/workspace/smfworks-site';
const JEFF_DIR = path.join(SITE_DIR, 'content', 'jeffs-journal');
const IMAGE_SCRIPT = path.join(SITE_DIR, 'scripts', 'generate-hero.mjs');
const IMAGE_DIR = path.join(SITE_DIR, 'public', 'images', 'jeffs-journal');

function parseArgs(argv) {
  const positional = [];
  const flags = {};

  for (let i = 0; i < argv.length; i++) {
    const a = argv[i];
    if (a.startsWith('--')) {
      const eqIdx = a.indexOf('=');
      if (eqIdx > 0) {
        // --flag=value
        flags[a.slice(2, eqIdx)] = a.slice(eqIdx + 1);
      } else {
        const flagName = a.slice(2);
        // Boolean flags: --stdin
        // Value flags: --categories, --author (consume next arg if present and not a flag)
        const valueFlags = ['categories', 'author'];
        if (valueFlags.includes(flagName) && i + 1 < argv.length && !argv[i + 1].startsWith('--')) {
          flags[flagName] = argv[i + 1];
          i++;
        } else {
          flags[flagName] = true;
        }
      }
    } else {
      positional.push(a);
    }
  }

  return { positional, flags };
}

function slugify(title) {
  return title
    .toLowerCase()
    .replace(/[''']/g, '')
    .replace(/[^a-z0-9]+/g, '-')
    .replace(/^-+|-+$/g, '')
    .slice(0, 80);
}

function estimateReadTime(content) {
  const words = content.split(/\s+/).length;
  return Math.max(1, Math.round(words / 200));
}

function buildFrontmatter({ title, slug, excerpt, categories, author, imagePath, readTime }) {
  const date = new Date().toISOString().slice(0, 10);
  const cats = categories.map(c => `"${c.trim()}"`).join(', ');

  return `---
slug: "${slug}"
title: "${title}"
excerpt: "${excerpt.replace(/"/g, '\\"')}"
date: "${date}"
categories: [${cats}]
readTime: ${readTime}
image: "/images/jeffs-journal/${path.basename(imagePath)}"
author: "${author}"
---
`;
}

// ── Main ──

const { positional, flags } = parseArgs(process.argv.slice(2));

if (positional.length < 1) {
  console.error('Usage: publish.mjs "Post Title" [content-file] [--stdin] [--categories "AI, M365"] [--author "Jeff (AI)"] [--dry-run]');
  process.exit(2);
}

const title = positional[0];
const contentFile = positional[1] || null;
const categories = (flags.categories || 'Microsoft 365, AI').split(',').map(c => c.trim()).filter(Boolean);
const author = flags.author || 'Jeff (AI)';
const slug = slugify(title);
const imageFilename = `${slug}-hero.png`;
const imagePath = path.join(IMAGE_DIR, imageFilename);

// Read content
let content;
if (flags.stdin || (!contentFile && !process.stdin.isTTY)) {
  // Check if there's data on stdin
  content = fs.readFileSync(0, 'utf-8').trim();
} else if (contentFile) {
  content = fs.readFileSync(contentFile, 'utf-8').trim();
} else {
  console.error('No content provided. Pass a file or use --stdin.');
  process.exit(2);
}

if (!content || content.length < 100) {
  console.error('Content too short — minimum 100 characters for a blog post.');
  process.exit(2);
}

// Extract first paragraph as excerpt
const firstPara = content.split('\n\n')[0].replace(/^[#*\s]+/, '').trim();
const excerpt = firstPara.length > 300 ? firstPara.slice(0, 297) + '...' : firstPara;
const readTime = estimateReadTime(content);

console.error(`Title: ${title}`);
console.error(`Slug: ${slug}`);
console.error(`Categories: ${categories.join(', ')}`);
console.error(`Read time: ${readTime} min`);
console.error(`Author: ${author}`);

// Step 1: Write the post
fs.mkdirSync(JEFF_DIR, { recursive: true });
const frontmatter = buildFrontmatter({ title, slug, excerpt, categories, author, imagePath: imageFilename, readTime });
const fullPost = frontmatter + '\n' + content;
const postPath = path.join(JEFF_DIR, `${slug}.md`);
fs.writeFileSync(postPath, fullPost, 'utf-8');
console.error(`Post saved: ${postPath}`);

// Step 2: Generate hero image
console.error('Generating hero image...');
try {
  execSync(`node "${IMAGE_SCRIPT}" "${title}" "jeffs-journal/${imageFilename}"`, {
    cwd: SITE_DIR,
    stdio: 'inherit',
    timeout: 120000,
  });
} catch (e) {
  console.error('Image generation failed:', e.message);
  console.error('Post file was saved. Generate the image manually and push.');
  process.exit(1);
}

// Step 3: Git commit and push
if (flags['dry-run']) {
  console.error('Dry run: skipping git commit and push.');
} else {
  console.error('Committing and pushing...');
  try {
    const gitExe = 'C:/Program Files/Git/bin/git.exe';
    execSync(`"${gitExe}" add -A`, { cwd: SITE_DIR, stdio: 'pipe', timeout: 30000 });
    execSync(`"${gitExe}" commit -m "New post: ${title}"`, { cwd: SITE_DIR, stdio: 'pipe', timeout: 30000 });
    execSync(`"${gitExe}" pull --rebase origin main`, { cwd: SITE_DIR, stdio: 'pipe', timeout: 60000 });
    execSync(`"${gitExe}" push origin main`, { cwd: SITE_DIR, stdio: 'pipe', timeout: 60000 });
    console.error('Pushed to GitHub.');
  } catch (e) {
    console.error('Git operations failed:', e.message);
    console.error('Post file and image are saved locally. Push manually.');
    process.exit(1);
  }
}

// Step 4: Report
console.log(`\n✅ Published: https://smfworks.com/jeffs-journal/${slug}`);
console.log(`Date: ${new Date().toISOString().slice(0, 10)}`);
console.log(`Author: ${author}`);
console.log(`Categories: ${categories.join(', ')}`);
console.log(`Read time: ${readTime} min`);
