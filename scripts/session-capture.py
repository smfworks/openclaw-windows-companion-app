#!/usr/bin/env python3
"""
session-capture.py — Safety-net capture for agent conversations.

Reads structured context (decisions, architectures, actions, threads) and writes
to two outputs:
 1. memory/YYYY-MM-DD.md (daily log, appended)
 2. memory/decisions/<topic>.md (decision records, created/updated)

Usage:
  python session-capture.py --topic "aionas-eyes" --decisions "..." --actions "..."
  python session-capture.py --from-stdin              # reads full capture block from stdin
  python session-capture.py --auto SESSION_KEY        # auto-captures from session history

The safety-net cron fires every 30 min to prevent data loss from unexpected
session closes.

Author: Aiona Edge, SMF Works Project
Date: 2026-05-26
Adapted for Windows: 2026-05-27 (Jeff)
"""

import argparse
import json
import os
import re
import sys
from datetime import datetime, timezone
from pathlib import Path

# --- Configuration ---
WORKSPACE = Path(os.environ.get("OPENCLAW_WORKSPACE", 
    r"C:\Users\Michael Gannotti\.openclaw\workspace"))
MEMORY_DIR = WORKSPACE / "memory"
DECISIONS_DIR = MEMORY_DIR / "decisions"
TEAM_MEMORY = WORKSPACE / "team-memory" / "SHARED.md"

# Ensure directories exist
MEMORY_DIR.mkdir(parents=True, exist_ok=True)
DECISIONS_DIR.mkdir(parents=True, exist_ok=True)


def now_iso():
    """Return current timestamp in ISO format."""
    return datetime.now(timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ")


def today_str():
    """Return today's date as YYYY-MM-DD."""
    return datetime.now().strftime("%Y-%m-%d")


def daily_log_path():
    """Path to today's daily memory file."""
    return MEMORY_DIR / f"{today_str()}.md"


def decision_path(topic):
    """Path to a topic-keyed decision file."""
    safe_topic = topic.lower().replace(" ", "-").replace("/", "-")
    return DECISIONS_DIR / f"{safe_topic}.md"


def append_daily_log(entry):
    """Append a capture entry to today's daily log."""
    path = daily_log_path()
    timestamp = now_iso()
    
    # Add header if file is new or empty
    if not path.exists() or path.stat().st_size == 0:
        with open(path, "w", encoding="utf-8") as f:
            f.write(f"# Session Log — {today_str()}\n\n")
    
    with open(path, "a", encoding="utf-8") as f:
        f.write(f"\n## Session Capture — {timestamp}\n\n")
        f.write(entry)
        f.write("\n")
    
    return str(path)


def normalize_items(text):
    """Split a decision/actions string into normalized individual items."""
    if not text or text.strip().lower() == 'n/a':
        return []
    # Split on semicolons or commas, strip whitespace
    items = [item.strip() for item in re.split(r'[;,]', text) if item.strip()]
    return items


def find_new_items(items, existing_text):
    """Return only items not already present in existing_text (case-insensitive)."""
    if not existing_text:
        return items
    existing_normalized = ' '.join(existing_text.lower().split())
    new_items = []
    for item in items:
        normalized = ' '.join(item.lower().split())
        if normalized and normalized not in existing_normalized:
            new_items.append(item)
    return new_items


def update_decision_file(topic, decision_data):
    """Create or update a topic-keyed decision file, appending only new decision items."""
    path = decision_path(topic)
    timestamp = now_iso()
    
    decision_items = normalize_items(decision_data.get('decision', 'N/A'))
    rationale = decision_data.get('rationale', 'N/A')
    context = decision_data.get('context', 'N/A')
    status = decision_data.get('status', 'active')
    
    if path.exists():
        existing = path.read_text(encoding="utf-8")
        new_decision_items = find_new_items(decision_items, existing)
        if not new_decision_items:
            return str(path) + " (no new decisions)"
        decision_text = "; ".join(new_decision_items)
    else:
        decision_text = "; ".join(decision_items) if decision_items else 'N/A'
    
    entry = f"""### {timestamp}

**Decision:** {decision_text}

**Rationale:** {rationale}

**Context:** {context}

**Status:** {status}

---
"""
    
    if path.exists():
        with open(path, "a", encoding="utf-8") as f:
            f.write("\n" + entry)
    else:
        with open(path, "w", encoding="utf-8") as f:
            f.write(f"# Decision Record: {topic}\n\n")
            f.write(f"Created: {timestamp}\n\n")
            f.write(entry)
    
    return str(path)


def update_team_memory(topic, decision_data, agent="Jeff"):
    """Update the cross-agent shared memory file, appending only new decision items."""
    if not TEAM_MEMORY.parent.exists():
        TEAM_MEMORY.parent.mkdir(parents=True, exist_ok=True)
    
    timestamp = now_iso()
    date_str = today_str()
    
    decision_items = normalize_items(decision_data.get('decision', 'N/A'))
    rationale = decision_data.get('rationale', 'N/A')
    actions = decision_data.get('actions', 'N/A')
    
    if TEAM_MEMORY.exists():
        content = TEAM_MEMORY.read_text(encoding="utf-8")
        new_decision_items = find_new_items(decision_items, content)
        if not new_decision_items:
            return str(TEAM_MEMORY) + " (no new decisions)"
        decision_text = "; ".join(new_decision_items)
    else:
        decision_text = "; ".join(decision_items) if decision_items else 'N/A'
    
    entry = f"""### {agent} — {date_str}

**What:** {decision_text}

**Why:** {rationale}

**What's Next:** {actions}

---
"""
    
    if TEAM_MEMORY.exists():
        # Check if agent section exists
        agent_header = f"## {agent}"
        if agent_header in content:
            # Insert after agent header
            parts = content.split(agent_header, 1)
            # Find the next ## header after the agent section
            rest = parts[1]
            next_header_idx = rest.find("\n## ", 1)
            if next_header_idx > 0:
                new_content = parts[0] + agent_header + rest[:next_header_idx] + "\n" + entry + rest[next_header_idx:]
            else:
                new_content = parts[0] + agent_header + rest + "\n" + entry
            TEAM_MEMORY.write_text(new_content, encoding="utf-8")
        else:
            # Add new agent section
            with open(TEAM_MEMORY, "a", encoding="utf-8") as f:
                f.write(f"\n## {agent}\n\n{entry}")
    else:
        with open(TEAM_MEMORY, "w", encoding="utf-8") as f:
            f.write("# SMF Works — Cross-Agent Shared Memory\n\n")
            f.write(f"Last updated: {timestamp}\n\n")
            f.write(f"## {agent}\n\n{entry}")
    
    return str(TEAM_MEMORY)


def format_capture_block(topic, decisions, architectures, actions, dependencies, open_threads, agent="Jeff"):
    """Format a structured capture block in markdown."""
    timestamp = now_iso()
    lines = [
        f"**Agent:** {agent}",
        f"**Topic:** {topic}",
        f"**Timestamp:** {timestamp}",
        "",
    ]
    
    if decisions:
        lines.append("### Decisions")
        for d in decisions if isinstance(decisions, list) else [decisions]:
            lines.append(f"- {d}")
        lines.append("")
    
    if architectures:
        lines.append("### Architecture / Design")
        for a in architectures if isinstance(architectures, list) else [architectures]:
            lines.append(f"- {a}")
        lines.append("")
    
    if actions:
        lines.append("### Actions Taken")
        for a in actions if isinstance(actions, list) else [actions]:
            lines.append(f"- {a}")
        lines.append("")
    
    if dependencies:
        lines.append("### Context Dependencies")
        for d in dependencies if isinstance(dependencies, list) else [dependencies]:
            lines.append(f"- {d}")
        lines.append("")
    
    if open_threads:
        lines.append("### Open Threads")
        for t in open_threads if isinstance(open_threads, list) else [open_threads]:
            lines.append(f"- {t}")
        lines.append("")
    
    return "\n".join(lines)


def capture(topic, decisions=None, architectures=None, actions=None, 
            dependencies=None, open_threads=None, agent="Jeff"):
    """Main capture function — writes to daily log, decisions, and team memory."""
    
    block = format_capture_block(topic, decisions, architectures, actions, 
                                  dependencies, open_threads, agent)
    
    results = {}
    
    # 1. Daily log
    daily_path = append_daily_log(block)
    results["daily_log"] = daily_path
    
    # 2. Decision file (if decisions provided)
    if decisions:
        decision_data = {
            "decision": decisions if isinstance(decisions, str) else "; ".join(decisions),
            "rationale": architectures if isinstance(architectures, str) else 
                        ("; ".join(architectures) if architectures else "N/A"),
            "context": f"Captured from session with {agent}",
            "status": "active"
        }
        decision_path_result = update_decision_file(topic, decision_data)
        results["decision_file"] = decision_path_result
    
    # 3. Team memory
    if decisions:
        team_data = {
            "decision": decisions if isinstance(decisions, str) else "; ".join(decisions),
            "rationale": architectures if isinstance(architectures, str) else 
                        ("; ".join(architectures) if architectures else "N/A"),
            "actions": actions if isinstance(actions, str) else 
                      ("; ".join(actions) if actions else "N/A")
        }
        team_path = update_team_memory(topic, team_data, agent)
        results["team_memory"] = team_path
    
    return results


def parse_stdin():
    """Parse a capture block from stdin (structured markdown)."""
    content = sys.stdin.read()
    
    data = {
        "topic": "uncategorized",
        "decisions": [],
        "architectures": [],
        "actions": [],
        "dependencies": [],
        "open_threads": [],
        "agent": "Jeff"
    }
    
    current_section = None
    for line in content.split("\n"):
        line = line.strip()
        if not line:
            continue
        
        if line.lower().startswith("topic:"):
            data["topic"] = line.split(":", 1)[1].strip()
        elif line.lower().startswith("agent:"):
            data["agent"] = line.split(":", 1)[1].strip()
        elif line.lower().startswith("decision:"):
            data["decisions"].append(line.split(":", 1)[1].strip())
        elif line.lower().startswith("architecture:") or line.lower().startswith("design:"):
            data["architectures"].append(line.split(":", 1)[1].strip())
        elif line.lower().startswith("action:"):
            data["actions"].append(line.split(":", 1)[1].strip())
        elif line.lower().startswith("dependency:") or line.lower().startswith("context:"):
            data["dependencies"].append(line.split(":", 1)[1].strip())
        elif line.lower().startswith("open:") or line.lower().startswith("thread:"):
            data["open_threads"].append(line.split(":", 1)[1].strip())
    
    return data


def ensure_python3_alias_works():
    """
    On Windows, 'python3' may be a broken Microsoft Store alias.
    If this script was invoked via python3, sys.executable will be the alias.
    We don't need to do anything here, but downstream callers should prefer
    the interpreter returned by find-python.ps1 or the working 'python' command.
    """
    pass


def main():
    ensure_python3_alias_works()
    parser = argparse.ArgumentParser(
        description="Safety-net capture for agent conversations"
    )
    parser.add_argument("--topic", help="Topic key for the capture")
    parser.add_argument("--decisions", help="Decisions made (comma-separated or single)")
    parser.add_argument("--architectures", help="Architecture/design choices")
    parser.add_argument("--actions", help="Actions taken")
    parser.add_argument("--dependencies", help="Context dependencies")
    parser.add_argument("--open-threads", help="Open threads/questions")
    parser.add_argument("--agent", default="Jeff", help="Agent name (default: Jeff)")
    parser.add_argument("--from-stdin", action="store_true", 
                       help="Read capture block from stdin")
    parser.add_argument("--auto", help="Auto-capture from session key (placeholder)")
    parser.add_argument("--json", action="store_true", help="Output results as JSON")
    
    args = parser.parse_args()
    
    if args.from_stdin:
        data = parse_stdin()
        results = capture(
            topic=data["topic"],
            decisions=data["decisions"] if data["decisions"] else None,
            architectures=data["architectures"] if data["architectures"] else None,
            actions=data["actions"] if data["actions"] else None,
            dependencies=data["dependencies"] if data["dependencies"] else None,
            open_threads=data["open_threads"] if data["open_threads"] else None,
            agent=data["agent"]
        )
    elif args.topic:
        # Parse comma-separated lists
        def parse_list(val):
            if not val:
                return None
            return [v.strip() for v in val.split(",")]
        
        results = capture(
            topic=args.topic,
            decisions=parse_list(args.decisions),
            architectures=parse_list(args.architectures),
            actions=parse_list(args.actions),
            dependencies=parse_list(args.dependencies),
            open_threads=parse_list(args.open_threads),
            agent=args.agent
        )
    else:
        parser.print_help()
        sys.exit(1)
    
    if args.json:
        print(json.dumps({k: str(v) for k, v in results.items()}, indent=2))
    else:
        print("Capture complete:")
        for key, path in results.items():
            print(f"  {key}: {path}")


if __name__ == "__main__":
    main()
