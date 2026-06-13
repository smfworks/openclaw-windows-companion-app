#!/usr/bin/env python3
"""
memory-promotion-scanner.py — Explicit importance marking for MEMORY.md promotion.

Scans daily memory files (memory/YYYY-MM-DD.md) for explicit promotion blocks:

    <!-- !!promote priority=1 topic="topic-name" type="decision" -->
    - Decision: ...
    - Rationale: ...
    <!-- /!!promote -->

Priority levels:
  1 = critical (score=1.0) — must survive
  2 = important (score=0.95) — should survive
  3 = noteworthy (score=0.90) — nice to have

Integrates with the existing auto-promotion format so the cleanup mechanism
treats explicit and auto promotions identically.

Usage:
  python memory-promotion-scanner.py [--dry-run] [--days 7]

Author: Aiona Edge, SMF Works Project (2026-05-26)
Adapted for Windows: Jeff (2026-05-27)
"""

import argparse
import os
import re
import sys
from datetime import datetime, timedelta
from pathlib import Path

# --- Configuration ---
WORKSPACE = Path(os.environ.get("OPENCLAW_WORKSPACE", 
    r"C:\Users\Michael Gannotti\.openclaw\workspace"))
MEMORY_DIR = WORKSPACE / "memory"
MEMORY_MD = WORKSPACE / "MEMORY.md"

# Regex for promotion blocks
PROMOTE_OPEN = re.compile(
    r'<!--\s*!!promote\s+priority=(\d)\s+topic="([^"]*)"\s+type="([^"]*)"\s*-->'
)
PROMOTE_CLOSE = re.compile(r'<!--\s*/!!promote\s*-->')

# Priority score map
PRIORITY_SCORES = {
    1: 1.0,    # critical
    2: 0.95,   # important
    3: 0.90,   # noteworthy
}


def find_promotion_blocks(filepath):
    """Extract all !!promote blocks from a file.
    
    Returns list of dicts: {priority, topic, type, content, source_file, line_start}
    """
    blocks = []
    
    try:
        with open(filepath, "r", encoding="utf-8") as f:
            lines = f.readlines()
    except (FileNotFoundError, UnicodeDecodeError):
        return blocks
    
    i = 0
    while i < len(lines):
        match = PROMOTE_OPEN.match(lines[i].strip())
        if match:
            priority = int(match.group(1))
            topic = match.group(2)
            block_type = match.group(3)
            
            # Collect content until close tag
            content_lines = []
            j = i + 1
            while j < len(lines):
                if PROMOTE_CLOSE.match(lines[j].strip()):
                    break
                content_lines.append(lines[j].rstrip())
                j += 1
            
            if j < len(lines):  # Found close tag
                blocks.append({
                    "priority": priority,
                    "topic": topic,
                    "type": block_type,
                    "content": "\n".join(content_lines).strip(),
                    "source_file": str(filepath),
                    "line_start": i + 1,
                    "line_end": j + 1,
                })
                i = j + 1
            else:
                print(f"WARN: Unclosed !!promote block at {filepath}:{i+1}", file=sys.stderr)
                i += 1
        else:
            i += 1
    
    return blocks


def format_promotion_entry(block):
    """Format a promotion block into the auto-promotion format for MEMORY.md.
    
    Uses the standard format:
    <!-- openclaw-memory-promotion:memory:<path>:<start>:<end> -->
    - <content>
    """
    score = PRIORITY_SCORES.get(block["priority"], 0.90)
    source = block["source_file"]
    start = block["line_start"]
    end = block["line_end"]
    
    header = f"<!-- openclaw-memory-promotion:memory:{source}:{start}:{end} -->"
    
    # Format the content with metadata
    lines = [
        header,
        f"- **Priority:** {block['priority']} ({block['type']})",
        f"- **Topic:** {block['topic']}",
        f"- **Score:** {score}",
        block["content"],
    ]
    
    return "\n".join(lines)


def scan_days(days=7, dry_run=False):
    """Scan the last N days of memory files for promotion blocks."""
    today = datetime.now()
    all_blocks = []
    
    for i in range(days):
        date = today - timedelta(days=i)
        date_str = date.strftime("%Y-%m-%d")
        filepath = MEMORY_DIR / f"{date_str}.md"
        
        if filepath.exists():
            blocks = find_promotion_blocks(filepath)
            all_blocks.extend(blocks)
    
    return all_blocks


def promote_to_memory(blocks, dry_run=False):
    """Write promotion entries to MEMORY.md."""
    if not blocks:
        print("No promotion blocks found.")
        return
    
    # Sort by priority (1 first, then 2, then 3)
    blocks.sort(key=lambda b: b["priority"])
    
    # Format entries
    entries = []
    for block in blocks:
        entry = format_promotion_entry(block)
        entries.append(entry)
    
    promotion_text = "\n\n".join(entries) + "\n"
    
    if dry_run:
        print(f"DRY RUN: Would promote {len(blocks)} blocks to MEMORY.md")
        print("=" * 60)
        for i, block in enumerate(blocks):
            print(f"\n[{i+1}] Priority {block['priority']} | {block['topic']} ({block['type']})")
            print(f"    Source: {block['source_file']}:{block['line_start']}-{block['line_end']}")
            print(f"    Content: {block['content'][:120]}...")
        print("\n" + "=" * 60)
        print("Run without --dry-run to apply.")
        return
    
    # Append to MEMORY.md
    if MEMORY_MD.exists():
        with open(MEMORY_MD, "r", encoding="utf-8") as f:
            existing = f.read()
    else:
        existing = ""
    
    # Check if any of these blocks are already promoted
    for block in blocks:
        source = block["source_file"]
        if f"openclaw-memory-promotion:memory:{source}" in existing:
            print(f"SKIP: Already promoted: {block['topic']} from {source}")
            continue
        
        with open(MEMORY_MD, "a", encoding="utf-8") as f:
            f.write("\n" + format_promotion_entry(block) + "\n")
        print(f"PROMOTED: {block['topic']} (priority {block['priority']}) from {source}")
    
    print(f"\nPromoted {len(blocks)} blocks to {MEMORY_MD}")


def ensure_python3_alias_works():
    """
    Windows compatibility note: 'python3' may resolve to the Microsoft Store stub.
    This script should be invoked with the interpreter discovered by
    find-python.ps1 (or a working 'python' executable). No runtime action needed.
    """
    pass


def main():
    ensure_python3_alias_works()
    parser = argparse.ArgumentParser(
        description="Scan memory files for explicit !!promote blocks and promote to MEMORY.md"
    )
    parser.add_argument("--dry-run", action="store_true", 
                       help="Show what would be promoted without writing")
    parser.add_argument("--days", type=int, default=7,
                       help="Number of days to scan back (default: 7)")
    parser.add_argument("--file", help="Scan a specific file instead of daily logs")
    
    args = parser.parse_args()
    
    if args.file:
        filepath = Path(args.file)
        if not filepath.exists():
            print(f"ERROR: File not found: {filepath}", file=sys.stderr)
            sys.exit(1)
        blocks = find_promotion_blocks(filepath)
    else:
        blocks = scan_days(args.days, args.dry_run)
    
    promote_to_memory(blocks, args.dry_run)


if __name__ == "__main__":
    main()
